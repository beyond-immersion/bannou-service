# Quest Plugin Architecture

> **Version**: 1.1
> **Last Updated**: 2026-02-07
> **Status**: Planning
> **Layer**: **L2 (Game Foundation)** - Quest is a core game primitive
> **Dependencies**: lib-contract (L1), lib-currency (L2), lib-inventory (L2), lib-item (L2), lib-character (L2)

## Executive Summary

The Quest plugin is a **thin orchestration layer** over lib-contract that adds game-specific quest semantics. Following the established pattern (Escrow wraps Contract for asset exchanges), Quest wraps Contract for objective-based gameplay progression.

**Core Principle**: Quests are contracts with game-flavored terminology. A "quest objective" is a milestone. A "quest reward" is a prebound API execution. A "quest chain" is a contract with sequential milestones. Quest adds domain language, not new primitives.

---

## Architectural Position

### Why Quest is L2 (Game Foundation)

Quest is a **core game primitive** alongside Character, Currency, and Items - not a feature layer service:

| L2 Game Foundation | Why Foundational |
|-------------------|------------------|
| Character | Core entity - everything references it |
| Currency | Core economic primitive |
| Item/Inventory | Core possession primitive |
| **Quest** | Core progression primitive |
| Realm/Location | Core world structure |

Quest is **agnostic to what validates prerequisites**. Higher-layer services (L4) provide prerequisite implementations via the IPrerequisiteProviderFactory pattern.

### Service Hierarchy

```
Layer 4: Game Features
├── lib-storyline
│   └── Composes: lib-quest (creates quest chains)
│   └── Consumes: lib-resource (compressed archives)
├── lib-skills, lib-magic, lib-achievement (example L4 services)
│   └── Implement: IPrerequisiteProviderFactory for Quest

Layer 2: Game Foundation
├── lib-quest (this plugin)
│   └── Wraps: lib-contract (L1)
│   └── Calls directly: lib-currency, lib-inventory, lib-item, lib-character (L2)
│   └── Discovers: IPrerequisiteProviderFactory implementations (L4)
│   └── Publishes events to: lib-analytics, lib-leaderboard (L4)
```

### Dependency Flow

```
Storyline (L4, narrative arc)
    ↓ creates
Quest Chain (L2, contract with milestones)
    ↓ wraps
Contract (L1, FSM + consent + prebound APIs)
    ↓ triggers
Currency/Inventory (L2, reward execution)
```

**Key Insight**: Storyline doesn't need to know about Contract internals. It creates quests; quests handle the contract mechanics. Clean separation of concerns.

---

## The "Contract is Brain" Pattern Extended

| Domain | Orchestration Layer | Layer | What It Adds |
|--------|---------------------|-------|--------------|
| Agreements | Contract | L1 | FSM, consent, milestones, prebound APIs |
| Objective Gameplay | **Quest** | **L2** | Objectives, progress UI, rewards, quest log |
| Asset Exchanges | Escrow | L4 | Deposit tracking, release modes, custody |
| Narrative Arcs | Storyline | L4 | Archive mining, GOAP composition, lazy phases |

**Note**: Quest is L2 because it's a foundational game primitive. Escrow remains L4 because it orchestrates complex multi-party exchanges that build on foundations.

Quest adds **game-facing semantics** without reinventing state machines:

| Contract Concept | Quest Terminology |
|------------------|-------------------|
| Contract Template | Quest Definition |
| Contract Instance | Active Quest |
| Party (employer) | Quest Giver |
| Party (employee) | Questor (character) |
| Milestone | Objective |
| Milestone completion | Objective progress |
| Prebound API (onComplete) | Reward distribution |
| Prebound API (onExpire) | Failure consequence |
| Breach | Quest failure |
| Consent | Quest acceptance |
| Termination | Quest abandonment |

---

## Core Capabilities

### 1. Quest Definition Management

Quest definitions are **contract templates with quest metadata**:

```yaml
# Example: Bounty Hunt quest definition
questDefinition:
  code: "BOUNTY_HUNT_WOLF"
  name: "Wolf Bounty"
  description: "Eliminate wolves threatening the village"

  # Contract template underneath
  contractTemplate:
    partyRoles:
      - role: questgiver
        description: "NPC or faction offering the bounty"
      - role: questor
        description: "Character accepting the quest"
    minParties: 2
    maxParties: 2
    enforcementMode: consequence_based

    milestones:
      - sequence: 1
        code: "ACCEPT"
        name: "Accept Quest"
        required: true
        # Auto-completed on consent

      - sequence: 2
        code: "KILL_WOLVES"
        name: "Eliminate 5 Wolves"
        required: true
        deadline: "P7D"  # 7 days
        deadlineBehavior: fail
        onComplete:
          - serviceName: quest
            endpoint: /quest/objective-completed
            payloadTemplate: |
              {"questInstanceId": "{contractId}", "milestoneCode": "KILL_WOLVES"}

      - sequence: 3
        code: "RETURN"
        name: "Return to Quest Giver"
        required: true
        onComplete:
          - serviceName: currency
            endpoint: /currency/credit
            payloadTemplate: |
              {"walletId": "{questor.walletId}", "amount": 100, "currencyCode": "GOLD"}
          - serviceName: quest
            endpoint: /quest/complete
            payloadTemplate: |
              {"questInstanceId": "{contractId}"}

  # Quest-specific metadata
  questMetadata:
    category: bounty
    difficulty: easy
    level_requirement: 5
    faction: village_guard
    repeatable: true
    cooldown: "P1D"
    tags: ["combat", "wolf", "bounty"]
```

### 2. Quest Instance Lifecycle

```
┌─────────────┐
│  Available  │ ← Quest giver offers quest (definition exists)
└──────┬──────┘
       │ Accept (create contract instance + consent)
       ▼
┌─────────────┐
│   Active    │ ← Character working on objectives
└──────┬──────┘
       │ Complete all required milestones
       ▼
┌─────────────┐
│  Completed  │ ← Rewards distributed, quest logged
└─────────────┘

Alternative paths:
  Active → Abandoned (voluntary termination)
  Active → Failed (deadline breach, death, etc.)
  Active → Expired (time limit reached)
```

### 3. Objective Tracking

Quest tracks objective progress **outside** Contract (Contract only knows complete/incomplete):

```csharp
public class QuestObjectiveProgress
{
    public Guid QuestInstanceId { get; set; }
    public string MilestoneCode { get; set; }

    // Progress tracking (Quest-specific, not in Contract)
    public int CurrentCount { get; set; }
    public int RequiredCount { get; set; }
    public List<Guid> TrackedEntityIds { get; set; }  // e.g., killed wolf IDs

    // Computed
    public bool IsComplete => CurrentCount >= RequiredCount;
    public float ProgressPercent => (float)CurrentCount / RequiredCount;
}
```

When `IsComplete` becomes true, Quest calls Contract's `/contract/milestone/complete` endpoint.

### 4. Event-Driven Progress Updates

Quest subscribes to game events to auto-update progress:

```yaml
# quest-events.yaml
x-event-subscriptions:
  # Combat events update kill objectives
  - topic: combat.entity.killed
    event: EntityKilledEvent

  # Item acquisition updates collection objectives
  - topic: inventory.item.added
    event: ItemAddedEvent

  # Location events update travel/discovery objectives
  - topic: character.location.entered
    event: LocationEnteredEvent

  # Dialogue events update talk/persuade objectives
  - topic: dialogue.completed
    event: DialogueCompletedEvent
```

**Event Handler Pattern**:
```csharp
[EventSubscription("combat.entity.killed")]
public async Task HandleEntityKilled(EntityKilledEvent evt, CancellationToken ct)
{
    // Find active quests with kill objectives for this entity type
    var relevantQuests = await FindQuestsWithObjective(
        questorId: evt.KillerId,
        objectiveType: ObjectiveType.Kill,
        targetType: evt.EntityType,
        targetSubtype: evt.EntitySubtype  // e.g., "wolf"
    );

    foreach (var quest in relevantQuests)
    {
        await IncrementObjectiveProgress(quest.InstanceId, quest.MilestoneCode, evt.EntityId);

        if (quest.IsNowComplete)
        {
            // Tell Contract the milestone is done
            await _contractClient.CompleteMilestoneAsync(new CompleteMilestoneRequest
            {
                ContractId = quest.ContractInstanceId,
                MilestoneCode = quest.MilestoneCode
            });
        }
    }
}
```

### 5. Quest Log & UI Support

Quest maintains a **player-facing view** separate from Contract's internal state:

```csharp
public class QuestLogEntry
{
    public Guid QuestInstanceId { get; set; }
    public string QuestCode { get; set; }
    public string QuestName { get; set; }
    public string Description { get; set; }

    // Display state
    public QuestStatus Status { get; set; }  // Active, Completed, Failed, Abandoned
    public List<ObjectiveDisplay> Objectives { get; set; }
    public QuestGiverInfo QuestGiver { get; set; }

    // Tracking
    public DateTimeOffset AcceptedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? Deadline { get; set; }

    // Rewards (preview, not yet granted)
    public List<RewardPreview> Rewards { get; set; }
}
```

### 6. Reward Distribution via Prebound APIs

Rewards are **not Quest's responsibility**. Contract's prebound API system handles them:

```yaml
# Milestone onComplete triggers these in sequence:
onComplete:
  # 1. Grant currency
  - serviceName: currency
    endpoint: /currency/credit
    payloadTemplate: '{"walletId": "{questor.walletId}", "amount": 100}'

  # 2. Grant items
  - serviceName: inventory
    endpoint: /inventory/add-item
    payloadTemplate: '{"containerId": "{questor.inventoryId}", "itemCode": "WOLF_PELT", "quantity": 3}'

  # 3. Grant experience (if XP system exists)
  - serviceName: character
    endpoint: /character/grant-experience
    payloadTemplate: '{"characterId": "{questor.entityId}", "amount": 50}'

  # 4. Update quest state (Quest's own callback)
  - serviceName: quest
    endpoint: /quest/internal/milestone-completed
    payloadTemplate: '{"questInstanceId": "{contractId}", "milestoneCode": "{milestoneCode}"}'

  # 5. Publish analytics
  - serviceName: analytics
    endpoint: /analytics/ingest
    payloadTemplate: '{"eventType": "quest_milestone", "entityId": "{questor.entityId}"}'
```

**Quest doesn't call Currency/Inventory directly**. Contract does, via prebound APIs configured in the quest definition.

---

## Integration with Storyline

### Storyline Creates Quest Chains

A "storyline" from one character's perspective is a sequence of quests:

```
Storyline: "Revenge for Father's Death"
├── Quest 1: "Investigate the Murder" (talk to witnesses)
├── Quest 2: "Track the Assassin" (follow clues)
├── Quest 3: "Confront the Killer" (combat or persuade)
└── Quest 4: "Justice or Mercy" (branching outcome)
```

Storyline service:
1. Generates the narrative arc from compressed archives
2. Creates quest definitions (or uses existing templates)
3. Creates the first quest instance
4. Listens for `quest.completed` events
5. Creates next quest in chain (lazy phase evaluation)

### Quest Knows Nothing About Storylines

Quest just processes individual quests. It publishes events:

```yaml
# Quest publishes
quest.accepted:
  questInstanceId: uuid
  questCode: string
  questorId: uuid

quest.objective.progressed:
  questInstanceId: uuid
  milestoneCode: string
  progress: int
  required: int

quest.completed:
  questInstanceId: uuid
  questCode: string
  questorId: uuid
  rewards: [...]

quest.failed:
  questInstanceId: uuid
  reason: string
```

Storyline subscribes to `quest.completed` and `quest.failed` to advance/branch the narrative.

### GOAP Quest Generation

The Behavior service's GOAP planner can generate quest definitions:

```
Input: Character backstory (TRAUMA: "father murdered by assassin")
       Character goals (GOAL: "bring father's killer to justice")
       World state (assassin is alive, in nearby city)

GOAP Planning: Find action sequence to achieve "killer brought to justice"

Output: Quest chain definition matching character's personal narrative
```

This is Storyline's responsibility, not Quest's. Quest just executes what Storyline creates.

---

## API Design

### Quest Definition Endpoints

```yaml
/quest/definition/create:
  summary: Create a new quest definition (wraps contract template creation)

/quest/definition/get:
  summary: Get quest definition by ID or code

/quest/definition/list:
  summary: List quest definitions with filtering

/quest/definition/update:
  summary: Update quest metadata (not contract template - immutable)

/quest/definition/deprecate:
  summary: Mark definition as deprecated (no new instances)
```

### Quest Instance Endpoints

```yaml
/quest/accept:
  summary: Accept a quest (creates contract instance + consents)

/quest/abandon:
  summary: Abandon active quest (terminates contract)

/quest/get:
  summary: Get quest instance details

/quest/list:
  summary: List character's quests (active, completed, failed)

/quest/log:
  summary: Get player-facing quest log (UI optimized)
```

### Objective Endpoints

```yaml
/quest/objective/progress:
  summary: Report progress on an objective (manual reporting)

/quest/objective/complete:
  summary: Manually complete an objective (GM/debug)

/quest/objective/get:
  summary: Get objective progress details
```

### Internal Endpoints (Prebound API Callbacks)

```yaml
/quest/internal/milestone-completed:
  summary: Called by Contract prebound API when milestone completes
  x-permissions: [service_only]

/quest/internal/quest-completed:
  summary: Called when all required milestones done
  x-permissions: [service_only]
```

---

## State Storage

### State Stores

```yaml
# state-stores.yaml additions
QuestDefinition:
  backend: mysql
  description: Quest definitions (contract templates + quest metadata)

QuestInstance:
  backend: mysql
  description: Active/completed quest instances

QuestObjectiveProgress:
  backend: redis
  description: Real-time objective progress tracking
  ttl: null  # Persists until quest completes

QuestCooldown:
  backend: redis
  description: Per-character quest cooldowns (for repeatable quests)
  ttl: dynamic  # Based on quest cooldown duration
```

### Index Strategy

```yaml
# Redis indexes for fast lookups
quest:character:{characterId}:active     # Set of active quest instance IDs
quest:character:{characterId}:completed  # Sorted set (by completion time)
quest:definition:{code}                  # Definition lookup by code
quest:objective:{instanceId}:{milestone} # Objective progress
```

---

## Event Subscriptions

### Consumed Events

| Topic | Purpose |
|-------|---------|
| `contract.milestone.completed` | Update quest state when Contract milestone completes |
| `contract.terminated` | Handle quest abandonment/failure |
| `contract.fulfilled` | Handle quest completion |
| `combat.entity.killed` | Auto-progress kill objectives |
| `inventory.item.added` | Auto-progress collection objectives |
| `character.location.entered` | Auto-progress travel objectives |
| `dialogue.completed` | Auto-progress talk objectives |

### Published Events

| Topic | Purpose |
|-------|---------|
| `quest.accepted` | Notify systems of new quest |
| `quest.objective.progressed` | Real-time progress updates |
| `quest.completed` | Trigger storyline advancement, achievements |
| `quest.failed` | Trigger storyline branching, consequences |
| `quest.abandoned` | Cleanup, reputation effects |

---

## Objective Types

Quest supports these objective types (extensible):

| Type | Event Source | Progress Tracking |
|------|--------------|-------------------|
| `kill` | combat.entity.killed | Count killed entities |
| `collect` | inventory.item.added | Count items acquired |
| `deliver` | inventory.item.removed + location | Items given at location |
| `travel` | character.location.entered | Locations visited |
| `discover` | scene.discovered | Scenes/POIs found |
| `talk` | dialogue.completed | NPCs conversed with |
| `craft` | crafting.completed | Items crafted |
| `escort` | actor.arrived + character.alive | NPC reached destination alive |
| `defend` | combat + timer | Area defended for duration |
| `custom` | quest.objective.progress (manual) | External system reports |

---

## Configuration

```yaml
# quest-configuration.yaml
QuestServiceConfiguration:
  type: object
  x-service-configuration:
    envPrefix: QUEST
  properties:
    MaxActiveQuestsPerCharacter:
      type: integer
      default: 25
      description: Maximum concurrent active quests
      env: MAX_ACTIVE

    ObjectiveProgressCacheTtlSeconds:
      type: integer
      default: 300
      description: TTL for objective progress cache
      env: PROGRESS_CACHE_TTL

    DefaultQuestDeadlineDays:
      type: integer
      default: 7
      description: Default deadline for quests without explicit deadline
      env: DEFAULT_DEADLINE_DAYS

    EnableAutoProgressFromEvents:
      type: boolean
      default: true
      description: Subscribe to game events for auto-progress
      env: AUTO_PROGRESS_ENABLED
```

---

## Implementation Phases

### Phase 1: Core Quest Lifecycle (Foundation)
- Quest definition CRUD (wrapping Contract templates)
- Quest accept/abandon (Contract instance management)
- Quest log retrieval
- Basic objective tracking (manual progress reporting)
- Event publishing

### Phase 2: Auto-Progress Integration
- Subscribe to combat/inventory/location events
- Objective type handlers
- Progress aggregation and milestone completion triggers

### Phase 3: Storyline Integration
- Quest chain support (prerequisite quests)
- Storyline → Quest creation API
- Quest completion → Storyline advancement events

### Phase 4: Advanced Features
- Repeatable quests with cooldowns
- Group/party quests
- Competitive quests (first to complete)
- Branching quest outcomes
- Quest sharing/trading

---

## Relationship to Other Systems

```
┌─────────────────────────────────────────────────────────────────┐
│                        STORYLINE (L4)                           │
│  • Mines compressed archives for narrative seeds                │
│  • Uses GOAP to plan quest chains                               │
│  • Creates Quest definitions/instances                          │
│  • Listens for quest completion to advance narrative            │
└───────────────────────────┬─────────────────────────────────────┘
                            │ creates/observes
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                          QUEST (L4)                             │
│  • Thin orchestration over Contract                             │
│  • Tracks objective progress                                    │
│  • Provides player-facing quest log                             │
│  • Subscribes to game events for auto-progress                  │
└───────────────────────────┬─────────────────────────────────────┘
                            │ wraps
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                        CONTRACT (L1)                            │
│  • FSM for agreement lifecycle                                  │
│  • Milestone tracking and deadlines                             │
│  • Prebound API execution for rewards                           │
│  • Consent and breach management                                │
└───────────────────────────┬─────────────────────────────────────┘
                            │ triggers
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    CURRENCY / INVENTORY (L2)                    │
│  • Execute reward distribution                                  │
│  • Publish transfer completion events                           │
└─────────────────────────────────────────────────────────────────┘
```

---

## Prerequisite Architecture (IPrerequisiteProviderFactory)

Quest is L2 and cannot depend on L4 services for prerequisite validation. The solution is the **IPrerequisiteProviderFactory pattern** - the same pattern Actor uses for `IVariableProviderFactory`.

### Interface Definition

```csharp
// bannou-service/Providers/IPrerequisiteProviderFactory.cs
public interface IPrerequisiteProviderFactory
{
    /// <summary>The prerequisite namespace (e.g., "skill", "magic", "achievement")</summary>
    string ProviderName { get; }

    /// <summary>Check if character meets prerequisite</summary>
    Task<PrerequisiteResult> CheckAsync(
        Guid characterId,
        string prerequisiteCode,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct);
}

public record PrerequisiteResult(
    bool Satisfied,
    string? FailureReason,
    object? CurrentValue,
    object? RequiredValue
);
```

### Built-in Prerequisites (Quest checks directly)

Quest calls L2 services with hard dependencies:

| Type | Service | Implementation |
|------|---------|----------------|
| `quest_completed` | Quest itself | Check own state |
| `currency` | Currency (L2) | `ICurrencyClient.GetBalanceAsync` |
| `item` | Inventory (L2) | `IInventoryClient.QueryAsync` |
| `character_level` | Character (L2) | `ICharacterClient.GetAsync` |
| `relationship` | Relationship (L2) | `IRelationshipClient.QueryAsync` |

### Dynamic Prerequisites (L4 providers)

Quest discovers L4 providers via DI collection injection:

| Type | Service | Provider |
|------|---------|----------|
| `skill` | Skills (L4) | `SkillPrerequisiteProviderFactory` |
| `magic` | Magic (L4) | `MagicPrerequisiteProviderFactory` |
| `achievement` | Achievement (L4) | `AchievementPrerequisiteProviderFactory` |
| `status_effect` | Effects (L4) | `StatusEffectPrerequisiteProviderFactory` |

### Provider Implementation Example

```csharp
// In lib-skills (L4)
public class SkillPrerequisiteProviderFactory : IPrerequisiteProviderFactory
{
    public string ProviderName => "skill";

    public async Task<PrerequisiteResult> CheckAsync(
        Guid characterId, string code,
        IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var currentLevel = await _skillStore.GetLevelAsync(characterId, code, ct);
        var requiredLevel = (int)(parameters.GetValueOrDefault("level") ?? 1);

        return new PrerequisiteResult(
            Satisfied: currentLevel >= requiredLevel,
            FailureReason: currentLevel < requiredLevel
                ? $"Requires {code} level {requiredLevel}, you have {currentLevel}"
                : null,
            CurrentValue: currentLevel,
            RequiredValue: requiredLevel
        );
    }
}

// DI registration in SkillsServicePlugin
services.AddSingleton<IPrerequisiteProviderFactory, SkillPrerequisiteProviderFactory>();
```

### Quest Validation Flow

```csharp
private async Task<List<FailedPrerequisite>> CheckPrerequisitesAsync(...)
{
    var failures = new List<FailedPrerequisite>();

    foreach (var prereq in definition.Prerequisites)
    {
        // Built-in L2 checks
        if (prereq.Type == "currency")
        {
            var balance = await _currencyClient.GetBalanceAsync(...);
            if (balance < prereq.Amount)
                failures.Add(new FailedPrerequisite(...));
            continue;
        }

        // Dynamic provider lookup for L4 prereqs
        var provider = _prerequisiteProviders
            .FirstOrDefault(p => p.ProviderName == prereq.Type);

        if (provider == null)
        {
            // Graceful degradation: L4 service not enabled
            _logger.LogWarning("No provider for prerequisite type {Type}", prereq.Type);
            failures.Add(new FailedPrerequisite(prereq.Type, prereq.Code,
                "Prerequisite system not available"));
            continue;
        }

        var result = await provider.CheckAsync(characterId, prereq.Code, prereq.Parameters, ct);
        if (!result.Satisfied)
            failures.Add(new FailedPrerequisite(...));
    }

    return failures;
}
```

---

## Open Questions

1. **Quest Templates vs Dynamic Generation**: Should Storyline always create new quest definitions, or should it instantiate from a library of templates with variable substitution?

2. **Multi-Character Quests**: How do group quests work? Shared contract with multiple questor parties? Separate contracts with linked outcomes?

3. **Quest Giver Actors**: Should quest givers be Actor instances with behavior documents that "offer" quests, or should Quest service handle availability directly?

4. ~~**Prerequisite Chains**~~: **RESOLVED** - Quest validates prerequisites via `IPrerequisiteProviderFactory` pattern. Built-in types call L2 services directly; L4 services implement providers for their domains.

5. **Hidden Objectives**: Some quests have secret objectives. Track in Quest (invisible in log) or separate system?

---

## Relationship with Scenarios

Quests and Scenarios are siblings, not parent-child:

| Aspect | Scenario | Quest |
|--------|----------|-------|
| **Trigger** | Conditions met (may be involuntary) | Explicit acceptance |
| **Focus** | Narrative events, character development | Player objectives, rewards |
| **Outcome** | State mutations (backstory, personality) | Success/failure with rewards |
| **Awareness** | Character may not realize significance | Player knows they're "on a quest" |

**Scenarios can spawn quests** via `questHooks`, but they're not required to. A scenario might:
- Apply backstory mutations only (no quest)
- Spawn immediate quests
- Spawn delayed quests (years later)
- Spawn different quests based on outcome

**Storyline composes both**:
```
Storyline Phase 1: Scenario (childhood trauma)
Storyline Phase 2: Quest (overcome fear)
Storyline Phase 3: Scenario (confrontation)
Storyline Phase 4: Quest (final choice)
```

See [Scenario Plugin Architecture](SCENARIO_PLUGIN_ARCHITECTURE.md) for details.

---

## References

- [Scenario Plugin Architecture](SCENARIO_PLUGIN_ARCHITECTURE.md) - Sibling system for narrative events
- [Contract Plugin Deep Dive](../plugins/CONTRACT.md)
- [Escrow Plugin Deep Dive](../plugins/ESCROW.md) - Reference implementation of thin orchestration pattern
- [ABML/GOAP Expansion Opportunities](ABML_GOAP_EXPANSION_OPPORTUNITIES.md) - Quest generation concepts
- [Storyline Composer](STORYLINE_COMPOSER.md) - Narrative arc generation
- [Economy Currency Architecture](ECONOMY_CURRENCY_ARCHITECTURE.md) - Quest reward integration
- [Service Hierarchy](../reference/SERVICE-HIERARCHY.md) - Layer dependencies

---

*This document defines the architectural vision for lib-quest. Implementation should follow schema-first development: create quest-api.yaml, quest-events.yaml, and quest-configuration.yaml before any code.*
