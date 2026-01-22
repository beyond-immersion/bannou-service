# lib-contract Service Architecture

> **Version**: 0.2.0
> **Status**: Architecture Draft
> **Created**: 2026-01-22
> **Updated**: 2026-01-22
> **Dependencies**: lib-state, lib-messaging, lib-mesh (ServiceNavigator)
> **Related Plugins**: lib-escrow, lib-currency, lib-inventory, lib-relationship
> **Future Dependents**: lib-quest, lib-law, lib-guild, lib-employment, lib-property

This document defines the architecture for `lib-contract`, a foundational service for binding agreements between entities. Contracts represent ongoing obligations, rights, and relationships that persist beyond simple transactions.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Design Philosophy](#2-design-philosophy)
3. [Core Concepts](#3-core-concepts)
   - 3.1-3.6: Templates, Instances, Parties, Terms, Milestones, Breaches
   - 3.7: [Interpretation Modes](#37-interpretation-modes)
   - 3.8: [Game Metadata System](#38-game-metadata-system)
4. [Entity Model](#4-entity-model)
5. [Prebound API System](#5-prebound-api-system)
6. [Contract Lifecycle](#6-contract-lifecycle)
7. [Enforcement Modes](#7-enforcement-modes)
8. [Integration Patterns](#8-integration-patterns)
9. [Universal Game Applications](#9-universal-game-applications)
10. [Relationship to Other Systems](#10-relationship-to-other-systems)
11. [Configuration Surface](#11-configuration-surface)
12. [Open Questions](#12-open-questions)
13. [Next Steps](#13-next-steps)
- [Appendix A: Example Contract Flow](#appendix-a-example-contract-flow)
- [Appendix B: Prebound API Examples](#appendix-b-prebound-api-examples)
- [Appendix C: Research References](#appendix-c-research-references)

---

## 1. Overview

### 1.1 What is lib-contract?

lib-contract provides a generic system for **binding agreements between entities**. While lib-escrow handles the mechanics of holding assets during exchanges, lib-contract represents the **ongoing obligations and rights** that may result from, accompany, or exist independently of asset exchanges.

Consider: If an NPC agrees to work at a player's shop for a week, what represents that arrangement?
- The **relationship** service says "they work for me" (current state)
- The **currency** service can pay them (individual transactions)
- But nothing captures: "for 7 days, under these terms, with these consequences for breach"

**lib-contract fills this gap.**

### 1.2 Core Features

- **Polymorphic parties**: Any entity type can enter contracts (characters, NPCs, guilds, companies, governments, locations)
- **Template/Instance pattern**: Define contract types once, instantiate many times
- **Milestone-based progression**: Contracts can have multiple stages with conditions
- **Prebound API execution**: Consequences and rewards execute via mesh calls without tight coupling
- **Configurable enforcement**: From advisory-only to community-enforced
- **Hierarchical contracts**: Contracts can reference or require other contracts
- **Transfer and assignment**: Contracts can change hands (with restrictions)
- **Implicit binding support**: Foundation for laws/governance (parties auto-bound by criteria)

### 1.3 What lib-contract Does NOT Do

- **Hold assets**: That's lib-escrow (contracts can reference escrows)
- **Transfer currency**: That's lib-currency (contracts can trigger transfers via prebound APIs)
- **Move items**: That's lib-inventory (contracts can trigger moves via prebound APIs)
- **Quest progression UI**: That's lib-quest (built on top of contracts)
- **Legal/governance logic**: That's lib-law (built on top of contracts)
- **Guild management**: That's lib-guild (uses contracts for membership)

### 1.4 Architectural Position

```
┌─────────────────────────────────────────────────────────────┐
│                    Game / Application                        │
└─────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                    ▼                    ▼
    lib-quest            lib-law              lib-guild
    (quest chains)    (governance)        (membership)
         │                    │                    │
         └────────────────────┼────────────────────┘
                              │
                              ▼
                    ┌─────────────────┐
                    │  lib-contract   │ ◄─── You are here
                    └─────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                    ▼                    ▼
   lib-currency         lib-inventory        lib-escrow
         │                    │                    │
         └────────────────────┼────────────────────┘
                              │
                   ┌──────────┴──────────┐
                   ▼                     ▼
              lib-state           lib-messaging
```

lib-contract sits ABOVE the asset-management plugins but BELOW domain-specific systems like quests, laws, and guilds. It provides the generic "agreement" primitive that those systems compose.

---

## 2. Design Philosophy

### 2.1 Contracts as the Connective Tissue

In worlds with thousands of autonomous NPCs forming organic organizations, contracts are the **connective tissue** that binds entities together:

- A **guild** is a contract where members agree to rules and dues
- **Employment** is a contract specifying work terms and compensation
- A **marriage** is a contract with social/legal implications
- A **land deed** is a contract establishing ownership rights
- A **law** is a contract where one party (government) binds others implicitly
- A **quest** is a contract: "complete X conditions, receive Y rewards"

### 2.2 Generic Foundation, Specific Implementations

lib-contract provides primitives. Higher-level plugins provide semantics:

| Primitive (lib-contract) | Semantic Layer | Example |
|--------------------------|----------------|---------|
| Contract with milestones | lib-quest | "Deliver 10 herbs" quest |
| Contract with membership terms | lib-guild | Guild charter |
| Implicit-binding contract | lib-law | City ordinance |
| Time-bound service contract | lib-employment | Mercenary contract |
| Property rights contract | lib-property | Land deed |

### 2.3 Loose Coupling via Prebound APIs

Contracts should trigger actions (payments, reputation changes, item transfers) without importing those systems. The **Prebound API System** allows contracts to store pre-configured mesh calls that execute when conditions are met.

This pattern:
- Keeps lib-contract generic and dependency-free
- Allows any future service to integrate with contracts
- Mirrors the Connect service's client capability system
- Enables atomic "do X then Y" sequences without orchestration code

### 2.4 Configuration Over Code

Different games need different contract behaviors:
- **Casual games**: Contracts are advisory; breaking them has no mechanical consequence
- **Simulation games**: Contracts are community-enforced with reputation consequences
- **Hardcore games**: Contracts have severe mechanical penalties for breach

lib-contract should be **configurable at multiple levels**:
- Global defaults (per deployment)
- Per contract template (defined by content creators)
- Per contract instance (negotiated by parties)

---

## 3. Core Concepts

### 3.1 Contract Templates

A **Contract Template** defines a type of agreement that can be instantiated:

```yaml
ContractTemplate:
  id: uuid
  code: string                    # "employment_standard", "guild_membership", "trade_agreement"
  name: string                    # Display name
  description: string

  # Scope
  realmId: uuid                   # null for cross-realm templates

  # Party Configuration
  minParties: integer             # Minimum parties required (usually 2)
  maxParties: integer             # Maximum parties allowed
  partyRoles:                     # Named roles in this contract type
    - role: string                # "employer", "employee", "buyer", "seller", "member", "authority"
      minCount: integer           # How many entities must fill this role
      maxCount: integer
      allowedEntityTypes: [enum]  # Which entity types can fill this role

  # Terms (default values, can be overridden per instance)
  defaultTerms: ContractTerms

  # Milestones (stages the contract progresses through)
  milestones: [MilestoneTemplate]

  # Implicit binding (for laws/governance)
  implicitBinding:
    enabled: boolean
    authorityRole: string         # Which role can create implicitly-binding instances
    bindingTrigger: enum          # presence | citizenship | membership | custom
    scopeType: enum               # location | realm | faction | custom

  # Transferability
  transferable: boolean           # Can this contract be assigned to another party?
  transferRequiresConsent: enum   # none | receiving_party | all_parties
  inheritable: boolean            # Does this transfer on party death?

  # Enforcement
  defaultEnforcementMode: EnforcementMode

  # Interpretation (how strictly terms are evaluated)
  interpretationMode: InterpretationMode  # See section 3.7

  # Game-Specific Metadata (arbitrary storage for higher-level systems)
  # lib-contract stores and returns this data but does not interpret it
  gameMetadata:
    # Template-level metadata set by content creators
    # Examples: required materials, skill requirements, cultural prerequisites
    templateData: map<string, any>

    # Schema hint for validation (optional, not enforced by lib-contract)
    templateDataSchema: string      # JSON Schema ID or URL

  # Metadata
  createdAt: timestamp
  createdBy: uuid
  isActive: boolean
```

### 3.2 Contract Instances

A **Contract Instance** is a specific agreement between specific parties:

```yaml
ContractInstance:
  id: uuid
  templateId: uuid

  # Status
  status: ContractStatus

  # Parties
  parties: [ContractParty]

  # Terms (merged from template defaults + instance overrides)
  terms: ContractTerms

  # Milestones (current progress)
  milestones: [MilestoneInstance]
  currentMilestoneIndex: integer

  # References
  escrowIds: [uuid]               # Related escrow agreements
  parentContractId: uuid          # If this contract is nested within another
  childContractIds: [uuid]        # Contracts spawned from this one

  # Timing
  proposedAt: timestamp
  acceptedAt: timestamp           # When all parties consented (or implicit binding occurred)
  effectiveFrom: timestamp        # When contract becomes active
  effectiveUntil: timestamp       # When contract naturally expires (null = perpetual)
  terminatedAt: timestamp         # When contract was terminated early

  # Breach tracking
  breaches: [BreachRecord]

  # Game-Specific Metadata (arbitrary storage for higher-level systems)
  # lib-contract stores and returns this data but does not interpret it
  gameMetadata:
    # Instance-level metadata set at creation or during execution
    # Examples: consumed materials, crafting results, narrative context
    instanceData: map<string, any>

    # Runtime state that game systems can read/write
    # Examples: current quest stage details, NPC mood modifiers
    runtimeState: map<string, any>

  # Audit
  createdBy: uuid
  createdByType: EntityType
```

### 3.3 Contract Parties

```yaml
ContractParty:
  entityId: uuid
  entityType: EntityType          # character | npc | guild | company | government | location | system
  role: string                    # Role from template (e.g., "employer", "employee")

  # Consent
  consentStatus: enum             # pending | consented | declined | implicit
  consentedAt: timestamp
  consentToken: string            # For explicit consent verification

  # Binding method
  bindingMethod: enum             # explicit | implicit | inherited | assigned

  # Status within contract
  partyStatus: enum               # active | withdrawn | expelled | deceased
  withdrawnAt: timestamp
```

### 3.4 Contract Terms

Terms are the configurable parameters of a contract:

```yaml
ContractTerms:
  # Duration
  duration: duration              # null = perpetual until terminated
  renewalPolicy: enum             # none | manual | auto_renew
  renewalNoticePeriod: duration   # How much notice before expiration to renew

  # Financial terms (references, not values - actual amounts in prebound APIs)
  paymentSchedule: enum           # one_time | recurring | milestone_based
  paymentFrequency: duration      # For recurring (e.g., "P7D" for weekly)

  # Termination
  terminationPolicy: enum         # mutual_consent | unilateral_with_notice | unilateral_immediate | non_terminable
  terminationNoticePeriod: duration
  earlyTerminationPenalty: boolean

  # Breach
  breachThreshold: integer        # How many breaches before auto-termination (0 = no auto)
  gracePeriodForCure: duration    # Time to fix breach before consequences

  # Custom terms (game-specific key-value pairs)
  customTerms: map<string, any>
```

### 3.5 Milestones

Milestones represent stages or checkpoints in a contract:

```yaml
MilestoneTemplate:
  code: string                    # "initial_deposit", "work_complete", "final_payment"
  name: string
  description: string

  # Ordering
  sequence: integer               # Order in the contract flow
  required: boolean               # Must this milestone be completed?

  # Conditions (what must be true for milestone to complete)
  conditions: [MilestoneCondition]
  conditionLogic: enum            # all | any | custom_expression

  # Timing
  deadline: duration              # Relative to contract start or previous milestone
  deadlineFrom: enum              # contract_start | previous_milestone | custom

  # Outcomes
  onComplete: [PreboundApi]       # APIs to call when milestone completes
  onExpire: [PreboundApi]         # APIs to call if deadline passes without completion
  onSkip: [PreboundApi]           # APIs to call if milestone is skipped (for optional milestones)

MilestoneCondition:
  type: enum
    - party_consent               # Specific party must confirm
    - external_event              # Wait for event on message bus
    - time_elapsed                # Wait for duration
    - contract_fulfilled          # Another contract must be fulfilled
    - api_check                   # Call an API and check response
    - custom                      # Game-specific condition

  # Type-specific configuration
  config: map<string, any>
```

### 3.6 Breach Records

```yaml
BreachRecord:
  id: uuid
  contractId: uuid

  # Who breached
  breachingPartyId: uuid
  breachingPartyType: EntityType

  # What was breached
  breachType: enum                # term_violation | milestone_missed | unauthorized_action | non_payment
  breachedTermOrMilestone: string # Code of the term or milestone breached

  # Details
  description: string
  detectedAt: timestamp
  detectedBy: enum                # system | party_report | external

  # Resolution
  status: enum                    # detected | cure_period | cured | consequences_applied | disputed | forgiven
  cureDeadline: timestamp
  curedAt: timestamp
  consequencesAppliedAt: timestamp

  # Consequences that were/will be applied
  consequences: [PreboundApi]
```

### 3.7 Interpretation Modes

Contracts can specify how strictly their terms and conditions are evaluated. This affects breach detection and condition matching.

```yaml
InterpretationMode:
  type: enum

  values:
    # Exact literal matching - conditions must match precisely as written
    # Enables "loophole" gameplay where creative interpretation of poorly-worded
    # terms can be exploited (inspired by Log Horizon's contract magic)
    - strict_letter

    # Reasonable interpretation - conditions evaluated by intent, not just wording
    # System or arbiter interprets ambiguous situations charitably
    - reasonable_spirit

    # Arbiter decides - ambiguous conditions are flagged for third-party resolution
    # Requires arbiter role to be defined in contract parties
    - arbiter_decides

    # Game decides - interpretation deferred to game-specific logic via callback
    # lib-contract emits event, game system responds with interpretation
    - game_callback
```

**Design Note**: The `strict_letter` mode enables interesting gameplay where carefully reading contract terms matters. A contract that says "deliver the package undamaged" might be satisfied by delivering an empty package if it doesn't specify contents. Games wanting this emergent gameplay can use `strict_letter`; games wanting straightforward contracts use `reasonable_spirit`.

**Loophole Discovery Pattern**:
```yaml
# API: /contract/analyze-terms
# Returns potential ambiguities or loopholes in contract wording
# Only available for strict_letter contracts
# Could be gated by character skills in game layer

Request:
  contract_id: uuid
  analyzing_party_id: uuid       # Who is looking for loopholes

Response:
  ambiguities: [TermAmbiguity]
  potential_exploits: [string]   # Descriptions of possible interpretations
```

### 3.8 Game Metadata System

lib-contract provides opaque storage for game-specific data at multiple levels. This enables higher-level systems (skills, items, narrative) to attach meaning to contracts without lib-contract needing knowledge of those systems.

**Use Cases**:

| Level | Field | Example Use |
|-------|-------|-------------|
| Template | `gameMetadata.templateData` | Required scribing skill level, material costs, cultural prerequisites |
| Instance | `gameMetadata.instanceData` | Consumed materials on creation, crafting inputs, narrative flags |
| Instance | `gameMetadata.runtimeState` | Quest tracker state, NPC relationship modifiers, dynamic pricing |

**API Support**:
```yaml
# Update game metadata without touching contract state
/contract/metadata/update:
  access: authenticated
  request:
    contract_id: uuid
    metadata_type: enum           # instance_data | runtime_state
    operations: [MetadataOperation]

MetadataOperation:
  type: enum                      # set | merge | delete | increment
  path: string                    # JSON path (e.g., "quest.stage", "materials.ink")
  value: any                      # Value for set/merge/increment
```

**Validation Pattern**:
Games can register JSON schemas for their metadata, enabling validation without lib-contract understanding the content:
```yaml
/contract/metadata/register-schema:
  access: admin
  request:
    schema_id: string             # "arcadia.quest.metadata.v1"
    schema: object                # JSON Schema document
    applies_to: enum              # template_data | instance_data | runtime_state
```

---

## 4. Entity Model

### 4.1 Entity Types

Following Bannou's polymorphic association pattern, contracts support these entity types:

| Entity Type | Description | Example Contract Roles |
|-------------|-------------|------------------------|
| `character` | Player-controlled character | Employee, guild member, property owner |
| `npc` | Non-player character | Shopkeeper (employer), quest giver |
| `guild` | Player organization | Employer, alliance member, landowner |
| `company` | Commercial entity | Employer, supplier, vendor |
| `government` | Governing body | Law issuer, tax collector, licensing authority |
| `faction` | NPC organization | Treaty party, trade partner |
| `location` | Physical place | Jurisdiction for laws, property being sold |
| `system` | The game itself | Quest system, automated rewards |

### 4.2 Party Combinations

All combinations are valid. Examples:

| Contract Type | Party A | Party B | Party C |
|---------------|---------|---------|---------|
| Employment | character (employee) | company (employer) | - |
| Guild Membership | character (member) | guild (organization) | - |
| Trade Agreement | company (seller) | company (buyer) | - |
| Marriage | character (spouse) | character (spouse) | government (registrar) |
| Land Deed | character (buyer) | character (seller) | government (registrar) |
| City Law | government (authority) | location (jurisdiction) | [implicit: all present] |
| Treaty | government (party) | government (party) | - |
| Quest | character (questee) | system (quest system) | npc (quest giver) |

### 4.3 Role Flexibility

Contract templates define roles, but the same template can be used in different contexts:

```yaml
# Template: "service_agreement"
partyRoles:
  - role: "provider"
    allowedEntityTypes: [character, npc, company]
  - role: "client"
    allowedEntityTypes: [character, npc, company, guild]

# Instance 1: Mercenary contract
# provider = character (mercenary)
# client = guild (hiring guild)

# Instance 2: Catering contract
# provider = company (catering company)
# client = character (event host)
```

---

## 5. Prebound API System

### 5.1 Concept

A **Prebound API** is a pre-configured mesh call that can be executed later. It stores:
- Target service and endpoint
- HTTP method
- Payload template with variable placeholders
- Expected response handling

This allows contracts to trigger actions in any service without compile-time dependencies.

### 5.2 Schema

```yaml
PreboundApi:
  id: uuid

  # Target
  serviceName: string             # "currency", "inventory", "reputation", "escrow"
  endpoint: string                # "/currency/debit", "/escrow/release"
  method: enum                    # POST (always POST for Bannou services)

  # Payload
  payloadTemplate: string         # JSON with {{variable}} placeholders

  # Variable sources
  variableBindings:
    - variable: string            # "breaching_party.wallet_id"
      source: enum                # contract | party | milestone | config | literal
      path: string                # JSON path or literal value

  # Execution
  executionMode: enum             # sync | async | fire_and_forget
  retryPolicy:
    maxAttempts: integer
    backoffMs: integer

  # Response handling
  expectedStatus: [integer]       # [200, 201] - success statuses
  onFailure: enum                 # log | retry | escalate | ignore

  # Metadata
  description: string             # Human-readable explanation
  tags: [string]                  # "reward", "consequence", "notification"
```

### 5.3 Variable Substitution

Variables in payload templates are substituted at execution time:

```yaml
# Prebound API definition
serviceName: "currency"
endpoint: "/currency/debit"
payloadTemplate: |
  {
    "wallet_id": "{{breaching_party.wallet_id}}",
    "currency_id": "{{contract.terms.penalty_currency_id}}",
    "amount": {{contract.terms.penalty_amount}},
    "reason": "Contract breach: {{contract.id}}",
    "reference_id": "{{breach.id}}",
    "reference_type": "contract_breach"
  }

variableBindings:
  - variable: "breaching_party.wallet_id"
    source: party
    path: "parties[?role='employee'].walletId"

  - variable: "contract.terms.penalty_currency_id"
    source: contract
    path: "terms.customTerms.penaltyCurrencyId"

  - variable: "contract.terms.penalty_amount"
    source: contract
    path: "terms.customTerms.penaltyAmount"

  - variable: "contract.id"
    source: contract
    path: "id"

  - variable: "breach.id"
    source: context
    path: "currentBreach.id"
```

### 5.4 Execution Contexts

Prebound APIs execute in specific contexts:

| Context | Available Variables |
|---------|---------------------|
| Milestone completion | contract, milestone, all parties |
| Milestone expiration | contract, milestone, all parties |
| Breach detected | contract, breach, breaching party, affected parties |
| Breach consequences | contract, breach, breaching party |
| Contract termination | contract, all parties, termination reason |
| Contract renewal | contract, all parties |

### 5.5 ServiceNavigator Integration

The Prebound API system requires extending the ServiceNavigator to support:

```csharp
public interface IServiceNavigator
{
    // Existing
    Task<TResponse> InvokeAsync<TRequest, TResponse>(string service, string endpoint, TRequest request);

    // New: Execute prebound API with variable substitution
    Task<PreboundApiResult> ExecutePreboundAsync(
        PreboundApi api,
        IDictionary<string, object> context,
        CancellationToken ct = default);

    // New: Batch execute (for milestone completions with multiple actions)
    Task<IReadOnlyList<PreboundApiResult>> ExecutePreboundBatchAsync(
        IEnumerable<PreboundApi> apis,
        IDictionary<string, object> context,
        BatchExecutionMode mode,  // parallel | sequential | sequential_stop_on_failure
        CancellationToken ct = default);
}
```

### 5.6 Connect Service Alignment

The Connect service currently uses a similar pattern for client capabilities (GUIDs mapping to endpoints). The Prebound API system should be implemented in a shared location (likely lib-mesh) so both systems benefit:

- Connect: Client sends GUID → ServiceNavigator resolves to endpoint → executes
- Contract: Milestone completes → ServiceNavigator executes prebound APIs → results logged

---

## 6. Contract Lifecycle

### 6.1 Status States

```
┌─────────────┐
│   draft     │  Contract created but not proposed to all parties
└──────┬──────┘
       │ propose()
       ▼
┌─────────────┐
│  proposed   │  Awaiting consent from required parties
└──────┬──────┘
       │ all parties consent (or implicit binding)
       ▼
┌─────────────┐
│   pending   │  Consented but not yet effective (future start date)
└──────┬──────┘
       │ effectiveFrom reached
       ▼
┌─────────────┐
│   active    │◄────────────────────────────────────┐
└──────┬──────┘                                     │
       │                                            │
       ├─── milestone completes ───► [still active] ┘
       │
       ├─── all milestones complete ───┐
       │                               │
       │                               ▼
       │                        ┌─────────────┐
       │                        │  fulfilled  │  All obligations met
       │                        └─────────────┘
       │
       ├─── breach detected ───► [breach handling, may remain active]
       │
       ├─── effectiveUntil reached ───┐
       │                              │
       │                              ▼
       │                        ┌─────────────┐
       │                        │   expired   │  Natural end of term
       │                        └─────────────┘
       │
       ├─── mutual termination ───┐
       │                          │
       ├─── unilateral termination│
       │    (if allowed)          │
       │                          ▼
       │                    ┌─────────────┐
       │                    │ terminated  │  Ended early by parties
       │                    └─────────────┘
       │
       └─── breach threshold exceeded ───┐
                                         │
                                         ▼
                                   ┌─────────────┐
                                   │  breached   │  Contract broken beyond repair
                                   └─────────────┘

Special states:
┌─────────────┐
│  suspended  │  Temporarily inactive (e.g., party incapacitated)
└─────────────┘

┌─────────────┐
│  disputed   │  Under arbitration
└─────────────┘

┌─────────────┐
│  declined   │  One or more parties declined during proposal
└─────────────┘
```

### 6.2 State Transitions

| From | To | Trigger | Actions |
|------|-----|---------|---------|
| draft | proposed | `propose()` | Notify parties, start consent timer |
| proposed | pending | All consents received | Lock terms, schedule activation |
| proposed | declined | Any party declines | Notify parties, close |
| proposed | expired | Consent timeout | Notify parties, close |
| pending | active | `effectiveFrom` reached | Execute `onActivate` prebound APIs |
| active | active | Milestone completes | Execute milestone `onComplete` APIs |
| active | fulfilled | All required milestones complete | Execute `onFulfill` APIs |
| active | expired | `effectiveUntil` reached | Execute `onExpire` APIs |
| active | terminated | Termination request | Execute `onTerminate` APIs |
| active | breached | Breach threshold exceeded | Execute breach consequence APIs |
| active | suspended | Suspension trigger | Pause milestone timers |
| active | disputed | Dispute filed | Pause actions, await resolution |
| suspended | active | Suspension lifted | Resume milestone timers |
| disputed | active/terminated | Resolution | Apply resolution outcome |

### 6.3 Milestone Progression

Within an active contract, milestones progress independently:

```
┌─────────────┐
│   pending   │  Milestone not yet started
└──────┬──────┘
       │ previous milestone completes (or contract activates for first)
       ▼
┌─────────────┐
│   active    │  Conditions being evaluated
└──────┬──────┘
       │
       ├─── all conditions met ───┐
       │                          │
       │                          ▼
       │                    ┌─────────────┐
       │                    │  completed  │  Execute onComplete APIs
       │                    └─────────────┘
       │
       ├─── deadline passed ───┐
       │    (required milestone)│
       │                        ▼
       │                  ┌─────────────┐
       │                  │   failed    │  Execute onExpire APIs, may breach
       │                  └─────────────┘
       │
       └─── deadline passed ───┐
            (optional milestone)│
                               ▼
                         ┌─────────────┐
                         │   skipped   │  Execute onSkip APIs, continue
                         └─────────────┘
```

---

## 7. Enforcement Modes

### 7.1 Mode Definitions

| Mode | Description | Breach Detection | Consequence Execution |
|------|-------------|------------------|----------------------|
| `advisory` | Contract is informational only | Manual/external | None (events emitted for game to handle) |
| `event_only` | System detects breaches, emits events | Automatic | None (consumers handle consequences) |
| `consequence_based` | System detects and applies consequences | Automatic | Via prebound APIs |
| `community` | Breaches affect reputation/standing | Automatic | Reputation APIs + events |

### 7.2 Advisory Mode

In advisory mode, lib-contract:
- Stores contract data
- Tracks milestone progress (if externally updated)
- Provides query APIs ("is X under contract with Y?")
- Emits events for state changes
- Does NOT detect breaches automatically
- Does NOT execute consequences

Use case: Games where contracts are flavor/RP, or where the game engine handles all enforcement.

### 7.3 Event-Only Mode

In event-only mode, lib-contract:
- Everything in advisory mode, PLUS
- Automatically detects breaches (missed deadlines, reported violations)
- Emits detailed breach events with consequence specifications
- Does NOT execute consequences directly

Use case: Games that want breach detection but custom consequence handling.

### 7.4 Consequence-Based Mode

In consequence-based mode, lib-contract:
- Everything in event-only mode, PLUS
- Executes prebound APIs when breaches occur
- Handles cure periods and escalation

Use case: Games with mechanical contract enforcement (penalties, forfeitures).

### 7.5 Community Mode

In community mode, lib-contract:
- Everything in consequence-based mode, PLUS
- Automatically affects reputation/standing via reputation service
- May trigger social consequences (gossip events, faction standing changes)

Use case: Simulation games like Arcadia where reputation matters.

### 7.6 Constraint Checking

Regardless of enforcement mode, lib-contract provides **constraint checking** that other systems can call:

```yaml
# API: /contract/check-constraint
Request:
  entityId: uuid
  entityType: EntityType
  constraintType: enum            # exclusivity | non_compete | territory | etc.
  proposedAction: map<string, any>  # What the entity wants to do

Response:
  allowed: boolean
  conflictingContracts: [ContractSummary]
  reason: string
```

This enables "auto-enforcement" at the calling system's discretion - the contract doesn't prevent actions, but it knows about constraints that callers can enforce.

---

## 8. Integration Patterns

### 8.1 With lib-escrow

Contracts and escrows work together but remain independent:

**Pattern A: Contract creates escrow**
```
1. Contract created for land sale
2. Contract activation triggers prebound API: /escrow/create
3. Escrow ID stored in contract.escrowIds
4. Buyer milestone "deposit_payment" triggers: /escrow/deposit
5. Seller milestone "transfer_deed" triggers: /escrow/deposit
6. Contract fulfillment triggers: /escrow/release
```

**Pattern B: Contract references existing escrow**
```
1. Trade negotiation creates escrow first
2. Contract created with escrowIds = [existing_escrow_id]
3. Contract terms reference escrow state as conditions
4. Contract completion coordinated with escrow release
```

**Pattern C: Escrow tokens as contract secrets**
```
1. Escrow created with consent tokens
2. Contract stores tokens in secure term storage
3. Contract milestones release tokens to appropriate parties
4. Parties use tokens to interact with escrow directly
```

### 8.2 With lib-currency

Contracts don't hold currency; they trigger currency operations:

```yaml
# Employment contract milestone: "weekly_payment"
onComplete:
  - serviceName: "currency"
    endpoint: "/currency/transfer"
    payloadTemplate: |
      {
        "source_wallet_id": "{{employer.wallet_id}}",
        "target_wallet_id": "{{employee.wallet_id}}",
        "currency_id": "{{contract.terms.payment_currency}}",
        "amount": {{contract.terms.weekly_wage}},
        "reference_id": "{{contract.id}}",
        "reference_type": "contract_payment"
      }
```

### 8.3 With lib-inventory

Similar pattern for item transfers:

```yaml
# Delivery contract milestone: "goods_delivered"
onComplete:
  - serviceName: "inventory"
    endpoint: "/inventory/transfer"
    payloadTemplate: |
      {
        "item_instance_id": "{{contract.terms.item_to_deliver}}",
        "from_container_id": "{{deliverer.inventory_id}}",
        "to_container_id": "{{recipient.inventory_id}}",
        "reference_id": "{{contract.id}}",
        "reference_type": "contract_delivery"
      }
```

### 8.4 With lib-relationship

Contract lifecycle can create/modify relationships:

```yaml
# Marriage contract onActivate
onActivate:
  - serviceName: "relationship"
    endpoint: "/relationship/create"
    payloadTemplate: |
      {
        "entity_a_id": "{{spouse_1.id}}",
        "entity_a_type": "{{spouse_1.type}}",
        "entity_b_id": "{{spouse_2.id}}",
        "entity_b_type": "{{spouse_2.type}}",
        "relationship_type_code": "spouse",
        "metadata": {
          "contract_id": "{{contract.id}}",
          "married_at": "{{contract.acceptedAt}}"
        }
      }

# Marriage contract onTerminate (divorce)
onTerminate:
  - serviceName: "relationship"
    endpoint: "/relationship/end"
    payloadTemplate: |
      {
        "entity_a_id": "{{spouse_1.id}}",
        "entity_b_id": "{{spouse_2.id}}",
        "relationship_type_code": "spouse"
      }
```

### 8.5 With Future lib-quest

Quests are contracts with specialized presentation and chaining:

```yaml
# Quest system creates contract
ContractTemplate:
  code: "quest_delivery"
  partyRoles:
    - role: "questee"
      allowedEntityTypes: [character]
    - role: "quest_giver"
      allowedEntityTypes: [npc, system]

  milestones:
    - code: "accept"
      conditions:
        - type: party_consent
          config: { party_role: "questee" }

    - code: "deliver_items"
      conditions:
        - type: api_check
          config:
            service: "inventory"
            endpoint: "/inventory/check-has"
            payload: { ... }

    - code: "return_to_giver"
      conditions:
        - type: external_event
          config:
            topic: "proximity.entered"
            filter: { ... }

  # Quest-specific presentation handled by lib-quest, not lib-contract
```

### 8.6 With Future lib-law

Laws are implicit-binding contracts:

```yaml
ContractTemplate:
  code: "no_weapons_in_temple"

  implicitBinding:
    enabled: true
    authorityRole: "governing_body"
    bindingTrigger: "presence"
    scopeType: "location"

  partyRoles:
    - role: "governing_body"
      allowedEntityTypes: [government, faction]
    - role: "subject"
      allowedEntityTypes: [character, npc]
      # Note: subjects are added implicitly, not explicitly

  milestones:
    - code: "compliance"
      conditions:
        - type: api_check
          config:
            service: "inventory"
            endpoint: "/inventory/check-equipped"
            payload:
              entity_id: "{{subject.id}}"
              item_tags: ["weapon"]
            expected_result: { has_items: false }
```

---

## 9. Universal Game Applications

### 9.1 Simple Games

**Match Commitment (Battle Royale)**
```yaml
# Players commit to staying in match
Template: "match_commitment"
Parties: [player, game_system]
Terms: { duration: "30 minutes" }
Breach: Leaving early
Consequence: Temporary matchmaking ban (prebound API to matchmaking service)
```

**Trade Protection (Any Trading Game)**
```yaml
# Ensures both parties fulfill trade
Template: "protected_trade"
Parties: [buyer, seller]
Escrow: Created automatically
Milestones: [buyer_deposits, seller_deposits, mutual_confirm]
```

### 9.2 MMO Games

**Guild Charter**
```yaml
Template: "guild_membership"
Parties: [member, guild]
Terms: { dues_amount: 100, dues_frequency: "P7D" }
Milestones: [initial_dues, recurring_dues...]
Breach: Non-payment
Consequence: Automatic expulsion (prebound API to guild service)
```

**Raid Loot Agreement**
```yaml
Template: "raid_loot_rules"
Parties: [raid_leader, member1, member2, ...]
Terms: { distribution: "dkp", master_looter: "raid_leader" }
# Contract exists to document agreement; enforcement is social
```

### 9.3 Strategy Games

**Alliance Treaty**
```yaml
Template: "mutual_defense_treaty"
Parties: [faction_a, faction_b]
Terms: { duration: "P30D", auto_renew: true }
Milestones: [ratification, first_joint_action]
Breach: Attacking ally, failing to respond to ally attack
Consequence: Reputation loss, treaty void
```

**Trade Route License**
```yaml
Template: "trade_route_license"
Parties: [merchant_company, regional_authority]
Terms: { route: "city_a_to_city_b", fee: 500, duration: "P90D" }
Breach: Trading without license
Consequence: Confiscation (prebound API to inventory), fine (prebound API to currency)
```

### 9.4 Life Simulation (Arcadia)

**Employment Contract**
```yaml
Template: "shop_employment"
Parties: [employee (character/npc), employer (company/character)]
Terms: { wage: 50, frequency: "P1D", hours: 8 }
Milestones: [daily_shift_complete...]
Breach: No-show, early departure
Consequence: Wage deduction, reputation loss, potential termination
```

**Property Lease**
```yaml
Template: "residential_lease"
Parties: [tenant, landlord, (optional) guarantor]
Terms: { rent: 200, frequency: "P30D", deposit: 600, duration: "P365D" }
Escrow: Holds deposit
Milestones: [move_in, monthly_payments..., move_out_inspection]
```

**Marriage**
```yaml
Template: "marriage"
Parties: [spouse_a, spouse_b, registrar (government)]
Terms: { property_regime: "community", divorce_process: "mutual_or_court" }
onActivate: Create spouse relationship
onTerminate: End relationship, trigger property division
```

**Apprenticeship**
```yaml
Template: "craft_apprenticeship"
Parties: [apprentice, master]
Terms: { duration: "P180D", craft: "blacksmithing", tuition: 1000 }
Milestones: [basic_techniques, intermediate_work, masterpiece]
onFulfill: Grant craft certification (prebound to skill service)
```

### 9.5 Quest-Like Structures

Any of these can underpin quest systems:

**Bounty**
```yaml
Template: "bounty"
Parties: [hunter, bounty_board (system)]
Terms: { target: entity_id, reward: 500, deadline: "P7D" }
Milestones: [target_eliminated, proof_submitted]
```

**Delivery**
```yaml
Template: "delivery_job"
Parties: [courier, client]
Terms: { item: item_id, destination: location_id, deadline: "P1D" }
Milestones: [pickup, delivery]
```

**Escort**
```yaml
Template: "escort_mission"
Parties: [escort, client, (implicit) protectee]
Terms: { protectee: npc_id, destination: location_id }
Milestones: [depart, arrive_safely]
Breach: Protectee death
```

---

## 10. Relationship to Other Systems

### 10.1 Dependency Direction

```
lib-contract depends on:
├── lib-state (storage)
├── lib-messaging (events)
└── lib-mesh (prebound API execution)

lib-contract is used by (future):
├── lib-quest (quest = specialized contract)
├── lib-law (law = implicit-binding contract)
├── lib-guild (membership = contract)
├── lib-employment (job = contract)
├── lib-property (deed = contract)
└── Game-specific systems
```

### 10.2 No Circular Dependencies

lib-contract never imports lib-currency, lib-inventory, lib-escrow, etc. All interactions happen via:
1. **Prebound APIs** - Stored calls executed at runtime
2. **Events** - Published for consumers to react
3. **References** - IDs stored, not object references

### 10.3 Event Publishing

lib-contract publishes events for all state changes:

| Event | Payload | Consumers |
|-------|---------|-----------|
| `contract.proposed` | Contract summary, parties | Notification service |
| `contract.accepted` | Contract ID, parties | Analytics, relationships |
| `contract.activated` | Contract ID | Dependent systems |
| `contract.milestone.completed` | Contract ID, milestone | Quest UI, analytics |
| `contract.milestone.failed` | Contract ID, milestone | Quest UI, breach handling |
| `contract.breached` | Contract ID, breach details | Reputation, enforcement |
| `contract.fulfilled` | Contract ID | Analytics, achievements |
| `contract.terminated` | Contract ID, reason | Cleanup systems |
| `contract.transferred` | Contract ID, old/new party | Audit |

### 10.4 Query Patterns

Other systems query contracts to make decisions:

```yaml
# "Does this character have any active employment?"
/contract/query:
  party_id: character_id
  party_type: character
  template_codes: ["employment_*"]
  statuses: [active]

# "What contracts govern this location?"
/contract/query:
  party_id: location_id
  party_type: location
  statuses: [active]

# "Is this entity allowed to join another guild?"
/contract/check-constraint:
  entity_id: character_id
  entity_type: character
  constraint_type: exclusivity
  proposed_action: { join_guild: other_guild_id }
```

---

## 11. Configuration Surface

### 11.1 Global Configuration

```yaml
# Environment prefix: CONTRACT_

ContractPluginConfiguration:
  # Defaults
  defaultEnforcementMode: EnforcementMode
  default: event_only

  defaultConsentTimeout: duration
  default: "P7D"

  # Limits
  maxPartiesPerContract: integer
  default: 20

  maxMilestonesPerTemplate: integer
  default: 50

  maxPreboundApisPerMilestone: integer
  default: 10

  maxActiveContractsPerEntity: integer
  default: 100  # 0 = unlimited

  # Processing
  milestoneCheckInterval: duration
  default: "PT1M"

  breachDetectionInterval: duration
  default: "PT5M"

  preboundApiBatchSize: integer
  default: 10

  # Retention
  fulfilledContractRetention: duration
  default: "P365D"

  terminatedContractRetention: duration
  default: "P90D"
```

### 11.2 Per-Template Configuration

Templates override global defaults:

```yaml
ContractTemplate:
  # ... other fields ...

  configuration:
    enforcementMode: EnforcementMode  # Override global
    consentTimeout: duration          # Override global
    maxInstances: integer             # Limit active instances of this template
    cooldownBetweenInstances: duration  # Per-party cooldown
```

### 11.3 Per-Instance Configuration

Instances can override template defaults (if template allows):

```yaml
ContractInstance:
  # ... other fields ...

  overrides:
    allowTermsOverride: boolean       # From template
    allowedOverrides: [string]        # Which terms can be overridden

    # Actual overrides
    termOverrides:
      duration: "P14D"                # Extended from template default
      paymentFrequency: "P3D"         # More frequent than default
```

### 11.4 Realm-Level Configuration

Realms can have contract policies:

```yaml
RealmContractPolicy:
  realmId: uuid

  # Which templates are available in this realm
  allowedTemplates: [uuid]            # null = all
  blockedTemplates: [uuid]

  # Enforcement overrides
  enforcementModeOverride: EnforcementMode  # null = use template/global

  # Restrictions
  crossRealmContractsAllowed: boolean
  implicitBindingAllowed: boolean     # Can laws be created?
```

---

## 12. Open Questions

### 12.1 Resolved (Pending Implementation)

| Question | Resolution |
|----------|------------|
| Naming | `lib-contract` |
| Party types | All entity types via EntityId/EntityType pattern |
| Enforcement | Four modes: advisory, event_only, consequence_based, community |
| Escrow integration | Parallel systems, contracts reference escrows by ID |
| Prebound APIs | New system in ServiceNavigator, shared with Connect |
| Laws | Higher-level plugin built on implicit-binding contracts |
| Quests | Higher-level plugin, configuration determines contract usage |

### 12.2 Open for Future Specification

| Question | Considerations |
|----------|----------------|
| **Dispute resolution** | How do arbiters work? Is there a separate arbitration flow? |
| **Contract versioning** | Can active contracts be amended? How? |
| **Partial fulfillment** | If 3 of 4 milestones complete, what happens? |
| **Multi-currency penalties** | How to specify penalties in multiple currencies? |
| **Contract marketplace** | Should contracts be listable/searchable/biddable? |
| **Witness/notary pattern** | Some cultures require witnesses - third party that confirms but has no obligations? |
| **Template inheritance** | Can templates extend other templates? |
| **Localization** | How are contract terms displayed to players in different languages? |
| **Audit/compliance** | What audit trail is needed for legal contracts (deeds, marriages)? |
| **Backward compatibility** | If template changes, what happens to existing instances? |

### 12.3 Performance Considerations

| Concern | Mitigation Strategy |
|---------|---------------------|
| Many active contracts | Realm-scoped sharding, efficient indexing |
| Milestone condition checks | Batch processing, event-driven where possible |
| Prebound API storms | Rate limiting, batch execution, circuit breakers |
| Contract queries | Denormalized read models, caching |
| Historical contract storage | Tiered storage, archival policies |

### 12.4 Security Considerations

| Concern | Mitigation Strategy |
|---------|---------------------|
| Unauthorized contract creation | Template permissions, party consent required |
| Forged consent | Cryptographic consent tokens |
| Prebound API abuse | Validate APIs at template creation, not execution |
| Implicit binding abuse | Authority verification, scope limitations |
| Contract spam | Rate limits, costs for creation |

---

## 13. Next Steps

### 13.1 Immediate (Architecture Phase)

1. Review this document with stakeholders
2. Resolve open questions in section 12.2
3. Design prebound API system in detail (for lib-mesh)
4. Draft OpenAPI schema for lib-contract

### 13.2 Implementation Phase

1. Implement prebound API system in lib-mesh/ServiceNavigator
2. Implement lib-contract core (templates, instances, parties)
3. Implement milestone system
4. Implement enforcement modes
5. Implement breach detection and handling
6. Integration testing with lib-escrow, lib-currency

### 13.3 Higher-Level Plugins (Future)

1. lib-quest (using contract milestones)
2. lib-law (using implicit binding)
3. lib-guild (using membership contracts)
4. lib-property (using deed contracts)
5. lib-employment (using service contracts)

---

## Appendix A: Example Contract Flow

### A.1 Mercenary Contract (Full Flow)

```
1. TEMPLATE EXISTS: "mercenary_service"
   - Roles: client, mercenary
   - Terms: duration, payment, mission_type
   - Milestones: contract_signed, mission_complete, payment_made
   - Enforcement: consequence_based

2. CONTRACT CREATION
   Guild leader creates contract instance:
   POST /contract/create
   {
     template_code: "mercenary_service",
     parties: [
       { entity_id: guild_id, entity_type: "guild", role: "client" },
       { entity_id: merc_id, entity_type: "character", role: "mercenary" }
     ],
     terms: {
       duration: "P14D",
       payment: 500,
       payment_currency: gold_id,
       mission_type: "escort"
     },
     escrow_config: {
       create_escrow: true,
       escrow_type: "two_party",
       client_deposits: [{ type: "currency", currency_id: gold_id, amount: 500 }]
     }
   }

   Response: { contract_id: "...", status: "draft" }

3. PROPOSAL
   POST /contract/propose
   { contract_id: "..." }

   - Contract status → "proposed"
   - Mercenary receives notification
   - Escrow created, client deposits 500 gold

4. CONSENT
   Mercenary reviews and accepts:
   POST /contract/consent
   { contract_id: "...", party_id: merc_id, consent_token: "..." }

   - Contract status → "active"
   - Milestone "contract_signed" auto-completes
   - Event: contract.activated

5. MISSION EXECUTION
   (Game events occur over 14 days)

6. MILESTONE COMPLETION
   External system reports mission success:
   POST /contract/milestone/complete
   { contract_id: "...", milestone_code: "mission_complete", evidence: {...} }

   - Milestone "mission_complete" → completed
   - onComplete prebound APIs execute:
     - /escrow/release (moves 500 gold to mercenary)
   - Milestone "payment_made" auto-completes (escrow release)

7. FULFILLMENT
   All milestones complete:
   - Contract status → "fulfilled"
   - Event: contract.fulfilled
   - Reputation boost for both parties (if community mode)

8. ARCHIVAL
   After retention period:
   - Contract moved to archive storage
   - Summary retained for historical queries
```

---

## Appendix B: Prebound API Examples

### B.1 Currency Transfer

```yaml
serviceName: "currency"
endpoint: "/currency/transfer"
payloadTemplate: |
  {
    "source_wallet_id": "{{employer.wallet_id}}",
    "target_wallet_id": "{{employee.wallet_id}}",
    "currency_id": "{{terms.payment_currency}}",
    "amount": {{terms.payment_amount}},
    "transaction_type": "contract_payment",
    "reference_id": "{{contract.id}}",
    "reference_type": "contract",
    "idempotency_key": "{{contract.id}}-{{milestone.code}}-payment"
  }
```

### B.2 Reputation Impact

```yaml
serviceName: "reputation"
endpoint: "/reputation/modify"
payloadTemplate: |
  {
    "entity_id": "{{breaching_party.id}}",
    "entity_type": "{{breaching_party.type}}",
    "faction_id": "{{contract.terms.reputation_faction}}",
    "delta": -50,
    "reason": "Contract breach: {{contract.template_code}}",
    "reference_id": "{{breach.id}}",
    "reference_type": "contract_breach"
  }
```

### B.3 Escrow Release

```yaml
serviceName: "escrow"
endpoint: "/escrow/release"
payloadTemplate: |
  {
    "escrow_id": "{{contract.escrow_ids[0]}}",
    "release_token": "{{contract.secrets.escrow_release_token}}",
    "release_mode": "to_beneficiary"
  }
```

### B.4 Relationship Creation

```yaml
serviceName: "relationship"
endpoint: "/relationship/create"
payloadTemplate: |
  {
    "entity_a_id": "{{parties[0].id}}",
    "entity_a_type": "{{parties[0].type}}",
    "entity_b_id": "{{parties[1].id}}",
    "entity_b_type": "{{parties[1].type}}",
    "relationship_type_code": "{{contract.terms.relationship_type}}",
    "started_at": "{{contract.activated_at}}",
    "metadata": {
      "contract_id": "{{contract.id}}",
      "contract_template": "{{contract.template_code}}"
    }
  }
```

### B.5 Notification

```yaml
serviceName: "notification"
endpoint: "/notification/send"
payloadTemplate: |
  {
    "recipient_id": "{{affected_party.id}}",
    "recipient_type": "{{affected_party.type}}",
    "template": "contract_breach_notification",
    "variables": {
      "contract_name": "{{contract.template_name}}",
      "breach_type": "{{breach.breach_type}}",
      "breaching_party_name": "{{breaching_party.display_name}}",
      "cure_deadline": "{{breach.cure_deadline}}"
    }
  }
```

### B.6 Zone Control / Economic Exile

Inspired by Log Horizon's Guild Center purchase - controlling infrastructure enables powerful non-combat enforcement. A contract breach can result in being banned from zones the enforcing party controls.

```yaml
# Consequence for violating Round Table Alliance rules
serviceName: "property"
endpoint: "/property/zone/ban"
payloadTemplate: |
  {
    "zone_id": "{{contract.terms.controlled_zone_id}}",
    "banned_entity_id": "{{breaching_party.id}}",
    "banned_entity_type": "{{breaching_party.type}}",
    "duration": "{{contract.terms.exile_duration}}",
    "reason": "Contract violation: {{contract.template_code}}",
    "reference_id": "{{contract.id}}",
    "reference_type": "contract_breach",
    "effects": {
      "deny_entry": true,
      "deny_services": true,
      "freeze_stored_assets": "{{contract.terms.freeze_assets_on_exile}}"
    }
  }
```

**Use Case**: A merchant guild controls the trade district. Violating guild rules results in being banned from all guild-controlled shops, warehouses, and market stalls - effectively economic exile without combat.

### B.7 Entity Property Modification

Inspired by Log Horizon's Rundelhaus contract - powerful contracts can modify entity properties, grant abilities, or change fundamental attributes. This represents "world-class" contract magic that should be rare and resource-intensive at the game layer.

```yaml
# Grant a subclass or special status via contract (like making an NPC an "Adventurer")
serviceName: "character"
endpoint: "/character/grant-subclass"
payloadTemplate: |
  {
    "character_id": "{{beneficiary.id}}",
    "subclass_code": "{{contract.terms.granted_subclass}}",
    "granted_by": {
      "contract_id": "{{contract.id}}",
      "granting_party_id": "{{grantor.id}}",
      "granted_at": "{{milestone.completed_at}}"
    },
    "duration": "{{contract.terms.subclass_duration}}",
    "revocable": "{{contract.terms.subclass_revocable}}",
    "revocation_conditions": "{{contract.terms.revocation_terms}}"
  }
```

```yaml
# Modify entity attributes (strength, permissions, capabilities)
serviceName: "character"
endpoint: "/character/modify-attributes"
payloadTemplate: |
  {
    "character_id": "{{beneficiary.id}}",
    "modifications": [
      {
        "attribute": "{{contract.terms.enhanced_attribute}}",
        "modifier_type": "contract_blessing",
        "value": "{{contract.terms.enhancement_value}}",
        "duration": "{{contract.terms.enhancement_duration}}",
        "source_contract_id": "{{contract.id}}"
      }
    ]
  }
```

**Use Case**: A master craftsman contract grants the apprentice the "Journeyman" subclass upon completion. A divine pact contract grants temporary stat bonuses. A citizenship contract grants "Resident" status with associated permissions.

### B.8 Matchmaking / Service Access Control

Contract status can gate access to game services - breach consequences can include temporary bans from matchmaking, auctions, or other systems.

```yaml
# Temporary ban from matchmaking for abandoning match commitment contract
serviceName: "matchmaking"
endpoint: "/matchmaking/ban"
payloadTemplate: |
  {
    "entity_id": "{{breaching_party.id}}",
    "entity_type": "{{breaching_party.type}}",
    "ban_type": "temporary",
    "duration": "{{contract.terms.abandonment_penalty_duration}}",
    "reason": "Match abandonment - contract breach",
    "reference_id": "{{breach.id}}",
    "reference_type": "contract_breach",
    "queues_affected": "{{contract.terms.affected_queues}}"
  }
```

---

## Appendix C: Research References

This architecture was informed by research into contract systems across multiple domains:

### Contract Theory (Economics)
- **Principal-Agent Problem**: Quest givers (principals) hiring players (agents)
- **Incentive Compatibility**: Design so honest behavior is optimal
- **Residual Control Rights**: Who decides unspecified matters (Oliver Hart, Nobel Prize)
- **Hold-Up Problem**: Parties underinvest if they expect renegotiation

### Game Implementations
- **EVE Online**: Escrow-based contracts, courier collateral system (gold standard)
- **Star Citizen**: Pre-funded service beacons, reputation-gated access
- **Crusader Kings 3**: Vassal contracts with limited modification frequency
- **Chronicles of Elyria**: Skill-gated contract complexity (designed, not shipped)

### Creative Inspiration
- **Log Horizon (Scribe class)**: Material-gated power, strict literal interpretation, zone control as enforcement, entity modification via contract magic

---

*This document is an architecture draft. Implementation specification will follow after review and resolution of open questions.*
