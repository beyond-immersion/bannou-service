# Why Is Quest Just a Wrapper Around Contract?

> **Short Answer**: Because quests ARE contracts. A quest is a binding agreement between parties (quest giver and quest taker) with milestones (objectives), terms (rewards and penalties), consent flows (accepting the quest), and state machine progression (objective completion sequence). Building a bespoke quest engine would mean reimplementing Contract's state machine, cleanup orchestration, and prebound API execution -- all of which already exist and are battle-tested. Quest translates game-flavored semantics into Contract infrastructure and adds quest-specific concerns (prerequisites, quest giver roles, reward distribution).

---

## The Traditional Approach

Most game backends build quests as a standalone system:

- A quest definition table with objectives, rewards, and prerequisites.
- A quest progress table tracking which objectives a character has completed.
- Custom state machine logic: offered -> accepted -> in_progress -> objectives_complete -> rewards_granted -> done.
- Custom cleanup logic: when a quest is abandoned, undo partial progress.
- Custom reward logic: on completion, grant items, currency, XP.

This works. It has worked for decades. But it means every quest-like system reinvents the same machinery.

---

## What Contract Already Provides

The Contract service (L1 App Foundation) provides general-purpose agreement management:

| Contract Concept | Quest Equivalent |
|-----------------|-----------------|
| Template | Quest definition |
| Instance | Active quest for a specific character |
| Parties (roles) | Quest giver + quest taker |
| Milestones | Objectives (sequential progression) |
| Terms | Rewards and penalties |
| Consent flow | Quest acceptance |
| State machine | Quest lifecycle (offered -> active -> complete -> rewarded) |
| Prebound API execution | Automatic reward distribution on milestone completion |
| Breach handling | Quest failure / abandonment |
| Cleanup orchestration | Rollback on abandonment |

Contract's state machine is a generic finite state machine that tracks consent from multiple parties, progresses through sequential milestones, executes callbacks on state transitions, and coordinates cleanup on failure. This is exactly what a quest system needs.

---

## What Quest Adds on Top

If Contract provides the engine, Quest provides the game-flavored interface. Quest handles concerns that are quest-specific and would be inappropriate in a general-purpose contract service:

### Prerequisites

Quest supports prerequisite validation before a character can accept a quest. Prerequisites come from multiple sources:

- **Built-in (L2)**: Has the character completed a previous quest? Do they have enough currency? Do they have a required item? Is their character level high enough? Quest checks these directly via L2 service clients.
- **Dynamic (L4)**: Does the character have a required skill level? Have they achieved a specific achievement? Do they have a magic ability? L4 services implement `IPrerequisiteProviderFactory` and register via DI. Quest discovers them at runtime.

Contract has no concept of prerequisites -- it manages agreements between parties who have already agreed to participate. The "can this party participate?" question is a quest-layer concern.

### Quest Giver Semantics

In Contract, parties are abstract role identifiers. In Quest, one party is specifically the "quest giver" -- an NPC or location that offers the quest. Quest manages:

- Which NPCs can offer which quests (based on NPC type, location, world state).
- How quests are presented to players (via the Actor service's behavior system).
- NPC-to-quest-taker relationship effects (completing quests for an NPC affects your relationship with them).

### Variable Provider for Actor

Quest exposes quest data to the Actor service via the Variable Provider Factory pattern, making expressions like `${quest.active_count}` and `${quest.has_objective_type.kill}` available in ABML behavior definitions. This allows NPCs to react to a character's quest state -- a guard might comment on the quest you are pursuing, or a merchant might offer discounts if you are on a quest for their faction.

Contract has no concept of behavior system integration. That is a game-specific concern.

### Reward Translation

When a quest milestone completes, Contract executes prebound API callbacks. Quest configures these callbacks to perform game-specific actions:

- Grant items via the Item service.
- Credit currency via the Currency service.
- Progress seeds via the Seed service.
- Update relationships via the Relationship service.

Quest translates "reward: 50 gold and a steel sword" into the specific API calls that Contract will execute on milestone completion. Contract just knows "call these APIs when milestone 3 completes."

---

## Why Not Build Quest Independently?

Consider what you would need to build from scratch:

1. **A state machine** with configurable states and transitions. Contract has this.
2. **Sequential milestone tracking** with ordered progression. Contract has this.
3. **Multi-party consent** (quest giver offers, quest taker accepts). Contract has this.
4. **Prebound API execution** on state transitions (grant rewards, update progress). Contract has this.
5. **Cleanup orchestration** when a quest is abandoned mid-progress. Contract has this.
6. **Event publication** on state changes (quest accepted, objective completed, quest finished). Contract has this.

Building all of this independently means duplicating Contract's tested logic. And when a bug is found in state machine progression, it needs to be fixed in both places. And when a new feature is added to Contract (say, time-limited milestones), Quest doesn't get it for free.

The "thin wrapper" approach means Quest inherits all of Contract's infrastructure improvements automatically. Contract gets better, Quest gets better.

---

## The Escrow Parallel

Quest is not the only service that uses Contract as infrastructure. Escrow (L4) uses Contract for conditional release escrows -- where contract fulfillment triggers escrow completion. License (L4) uses Contract for unlock behavior on license board nodes.

This pattern -- L4 game services composing L1 infrastructure services to build game-specific semantics -- is a core architectural principle:

| L4 Service | Uses Contract For |
|-----------|-------------------|
| Quest | Quest lifecycle, objective tracking, reward execution |
| Escrow | Conditional release triggers, multi-party consent |
| License | Node unlock behavior, progression gating |

Three different game features, three different semantic domains, one shared state machine and agreement engine. This is the payoff of placing Contract at L1 -- it is infrastructure that any game feature can leverage.

---

## The Prerequisite Provider Factory

Quest's approach to prerequisites demonstrates how L2 services interact with L4 services cleanly. Quest defines the `IPrerequisiteProviderFactory` interface in shared code. L4 services implement it:

```
Quest (L2) defines: "I need to check prerequisites. Here's the interface."

Skills (L4) registers:   "I can check skill-level prerequisites."
Magic (L4) registers:    "I can check magic-ability prerequisites."
Achievement (L4) registers: "I can check achievement prerequisites."
```

Quest discovers all registered providers via DI and delegates prerequisite checks to the appropriate provider based on the prerequisite type. If Skills is not deployed, skill-level prerequisites simply fail validation with a clear error. Quest does not crash, does not degrade silently, and does not need to know that Skills exists.

This is the same pattern Actor uses for variable providers, and it exists for the same reason: foundational services need data from optional feature services without depending on them.

---

## What Quest Is Not

Quest is explicitly NOT:

- **A scripting engine.** Quest does not contain branching dialogue trees, conditional quest chains, or procedural quest generation. Those are higher-layer concerns (Storyline at L4 generates narrative arcs; Puppetmaster at L4 orchestrates scenarios).
- **A progression system.** Quest tracks objective completion within a single quest. Cross-quest progression (reputation, quest chains, story arcs) is handled by other services (Seed for growth, Relationship for reputation, Storyline for narrative).
- **A reward calculator.** Quest stores what rewards to grant and delegates the actual granting to the appropriate services. It does not compute reward scaling, loot tables, or dynamic reward adjustment.

By staying thin and delegating to both Contract (below) and specialized services (at the same layer or above), Quest remains a focused translation layer between game semantics and infrastructure primitives.
