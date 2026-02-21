# What Is the Difference Between License and Collection?

> **Short Answer**: License manages progression boards where unlocking nodes costs something and follows structured rules (skill trees, tech trees, license boards). Collection manages content archives where entries are granted by experiencing them (bestiaries, music galleries, recipe books). License is about earning and spending. Collection is about discovering and cataloging.

---

## They Sound Similar Because They Share Infrastructure

License is an L4 Game Feature. Collection is an L2 Game Foundation service -- it was promoted from L4 because it serves as the upstream experience source that feeds the Seed (L2) growth pipeline (see the Collection -> Seed -> Status pipeline in issue #375). Despite being in different layers, they build on the same L2 foundations:

- Both use **Item** for defining what can be unlocked/collected (item templates)
- Both use **Inventory** for storing what has been unlocked/collected (item instances in containers)
- Both support polymorphic ownership (characters, accounts, guilds, locations)

This shared infrastructure is exactly what makes the distinction confusing. If they both use items in inventories, why are they different services?

---

## License: Structured Progression with Cost

License provides grid-based progression boards -- think Final Fantasy XII's License Board, Path of Exile's passive skill tree, or a generic tech tree.

Key characteristics:

- **Nodes have costs.** Unlocking a node on the board costs LP (License Points) or another resource. The player makes a deliberate choice to spend.
- **Adjacency matters.** You can only unlock nodes adjacent to already-unlocked nodes. Progression follows paths across the grid.
- **Contracts orchestrate unlocks.** License uses the Contract service (L1) to manage unlock behavior. When a node is unlocked, a prebound API call fires that grants the item and deducts the cost. The contract's state machine handles the transactional safety.
- **Prerequisites exist.** Some nodes require specific conditions beyond adjacency: character level, another board's node, quest completion.
- **The board is the content.** The designer defines the board layout, node costs, adjacency rules, and prerequisites. The player navigates this authored structure.

License is a **spending-based progression system**. The player makes economic decisions: "Do I unlock the strength node or the agility node? I can't afford both."

---

## Collection: Experience-Based Discovery

Collection manages universal content archives -- bestiaries (catalog of creatures encountered), music galleries (compositions heard), scene archives (locations discovered), recipe books (crafting recipes learned), and custom types.

Key characteristics:

- **Entries are granted, not purchased.** You add an entry to your bestiary by encountering the creature, not by spending resources. The Collection service uses direct grants without contract delegation.
- **No adjacency or grid.** Collections are flat lists or categorized sets. There is no "you must collect creature A before you can collect creature B" spatial constraint.
- **The world is the content.** The collection grows by playing the game. What you collect reflects what you experienced, not what you chose to buy.
- **Dynamic music integration.** Collection specifically features dynamic music track selection -- unlocked tracks influence what music plays in different areas. This is a gameplay loop where collecting music compositions directly affects the auditory experience.
- **Completionism drives exploration.** A bestiary showing "47/120 creatures discovered" motivates visiting new areas. A music gallery showing "12/30 compositions heard" motivates attending NPC performances.

Collection is a **discovery-based cataloging system**. The player's collection grows as a side effect of playing, not as a result of economic decisions.

---

## The Mechanical Distinction

| Dimension | License | Collection |
|-----------|---------|------------|
| How entries are acquired | Purchased/unlocked with cost | Granted upon experience |
| Spatial structure | Grid with adjacency rules | Flat or categorized |
| Orchestration | Contract service (FSM + consent) | Direct grants (no contract) |
| Player decision | "Which node do I unlock?" | "Where do I explore?" |
| Resource consumption | Yes (LP, currency, items) | No |
| Prerequisites | Adjacency + custom conditions | None (encounter-based) |
| Designer authoring | Board layout, costs, paths | Entry templates, categories |
| Gameplay loop | Economic optimization | Exploration and completionism |

---

## Why Not One Service?

The merge argument: "Both put items in inventories. One service with a `type` field (license vs. collection) could handle both."

Here is what breaks:

### Different Orchestration Models

License's reliance on the Contract service for unlock orchestration is fundamental, not incidental. When you unlock a license node:

1. A contract instance is created with milestones (prerequisites, cost deduction, item grant)
2. The contract FSM validates preconditions
3. Prebound API calls execute the cost deduction and item grant atomically
4. If any step fails, the contract rolls back

Collection's direct grant model is intentionally simpler:

1. Verify the entry template exists
2. Create an item instance in the owner's collection container
3. Done

Merging them means either Collection gains unnecessary contract overhead (creating contracts for simple grants), or License loses its transactional safety (using direct grants for cost-bearing operations). Neither is acceptable.

### Different Growth Patterns

A license board is designed once and explored by many players. The number of nodes on a board is fixed at design time. Board interactions are write-heavy during active progression and then stop (once fully unlocked, the board is static).

A collection grows continuously as the player encounters new content. The bestiary is never "complete" because new creatures may be added, new areas may be discovered, and the content flywheel may generate new species. Collection interactions are append-only over the lifetime of the character.

These growth patterns favor different state management strategies. License benefits from the full board being loaded into cache (small, bounded). Collection benefits from paginated queries (potentially large, unbounded).

### Different Disablement Profiles

License is an optional L4 feature supporting progression systems (skill trees, tech trees). A game without RPG-style progression can disable License entirely.

Collection is an L2 foundational service. It feeds the Seed growth pipeline -- disabling Collection means disabling the cross-pollination mechanism that makes progressive agency work across seeds. Collection is as foundational as Currency or Inventory.

This layer difference alone makes merging impossible. You cannot have a single service that is simultaneously an optional L4 feature and a required L2 foundation.

---

## How They Interact with the Content Flywheel

License boards are authored content -- a designer creates them. They do not participate directly in the content flywheel (though the choices a player makes on a license board affect their character's capabilities, which affect their life experiences, which affect their archive when they die).

Collections are flywheel-native. A bestiary entry for a creature that was procedurally generated by the content flywheel is itself flywheel content. A music gallery containing compositions that NPC bards created based on world events is flywheel content. Collections are where players interact with the flywheel's output -- they literally catalog the emergent content they encounter.

This is another reason the services are separate. License is infrastructure for designer-authored progression. Collection is the player-facing surface of the emergent content pipeline. Mixing these concerns in one service would obscure the flywheel's most visible player-facing mechanism.
