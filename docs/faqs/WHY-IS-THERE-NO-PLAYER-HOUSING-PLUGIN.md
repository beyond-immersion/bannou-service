# Why Is There No Player Housing Plugin?

> **Short Answer**: Because player housing is not a *thing* in Bannou -- it is a *garden type* that composes entirely from existing primitives. The conceptual space is Gardener (garden type: `housing`). Capability progression is Seed (housing seed type with phase-based unlocks). Physical layout is Scene (node trees including voxel nodes). Interactive building is the Voxel Builder SDK (pure computation). Persistence is Save-Load (versioned delta saves). Furnishing is Item + Inventory (furniture as item instances placed in a housing container). Experience management is a divine god-actor running via Puppetmaster on the Actor runtime. Visitor access is Game Session + Permission (role matrix for owner/visitor/trusted friend). A dedicated `lib-housing` plugin would duplicate what these services already compose together.

---

## The Pattern: Housing Is a Garden

PLAYER-VISION.md defines a "garden" as an abstract conceptual space that a player inhabits. Every player is always in some garden. The void/discovery experience is a garden. A lobby is a garden. An in-game combat encounter is a garden. And a player's home is a garden.

The Gardener service (L4) manages all of these through the same mechanism: garden instances with per-garden entity associations, a divine god-actor tending the experience, and garden-to-garden transitions. Housing is structurally identical to the void/discovery experience that Gardener already implements -- just with different tools and different behavioral logic.

```
Discovery garden:              Housing garden:
  POIs spawn around player       Furniture placed in rooms
  Drift metrics tracked          Voxel edits tracked
  Scenarios offered              Visitors managed
  God-actor tends experience     God-actor tends experience
  Seed grows from engagement     Seed grows from engagement
```

The flow is the same:

1. Player selects housing seed from the void (garden-to-garden transition: discovery -> housing)
2. Gardener creates `housing` garden instance, binds per-garden entity associations (housing seed, inventory, scene)
3. Gardener god-actor begins tending (spawns NPC servants, manages visitors, seasonal changes, event reactions)
4. Scene data loaded -- SceneComposer renders the node tree, VoxelBuilder renders voxel nodes
5. Player interacts: placing furniture goes through Inventory/Item; voxel editing goes through the SDK's operation system
6. Seed grows as player engages -- unlocks capabilities (bigger space, more decoration slots, crafting stations, garden plots)
7. Save-Load persists the scene + voxel delta saves (chunk-aligned `.bvox` format means only modified 16x16x16 chunks re-serialize)

---

## Every Housing Concern Is Already Solved

| Housing Concern | Solved By | Layer |
|---|---|---|
| "A conceptual space the player inhabits" | **Gardener** (garden type: `housing`) | L4 |
| "Capabilities emerge from growth" | **Seed** (housing seed type -- phases unlock rooms, decorations, NPC servants, visitor capacity) | L2 |
| "Physical layout stored persistently" | **Scene** (node tree of housing objects, including `voxel` node types for player-built structures) + **Save-Load** (versioned persistence with chunk-level delta saves for voxel data) | L4 + L4 |
| "Interactive voxel building" | **Voxel Builder SDK** (pure computation -- brush, fill, mirror, WFC generation; engine bridges render; runs on both client and server) | SDK |
| "Rendered to the client on demand" | **Scene Composer SDK** + **Voxel Builder SDK** (engine bridges render the scene graph and voxel nodes respectively) | SDK |
| "A god tends the space" | **Divine/Puppetmaster** (gardener god-actor manages the housing experience -- spawning NPC servants, routing events, seasonal decoration changes) | L4 |
| "Items placed in the space" | **Inventory** (housing container) + **Item** (furniture/decorations as item instances with `placed_scene_id`/`placed_node_id` custom stats) | L2 |
| "Visitors can enter" | **Game Session** (housing visit = game session) + **Permission** (owner/visitor/trusted_friend role matrix) | L2 + L1 |
| "Entity events route to player" | **Entity Session Registry** in Connect (L1) | L1 |
| "Player-crafted furniture" | **lib-craft** (recipe-based production sessions producing Item instances from player voxel designs) | L4 (planned) |
| "Progressive UX for building tools" | **Agency** (UX modules for building operations gated by housing seed depth -- basic placement at depth 0, advanced brush tools at depth 4, WFC generation at depth 8) | L4 |

There is no remaining concern that needs a dedicated service -- only authored content (housing seed type definitions, gardener behavior documents, item templates for furniture, Scene validation rules for spatial constraints).

---

## The Seed IS the Capability System

Traditional housing systems have a bespoke progression: "unlock 5-room house at level 10, 10-room house at level 20." Bannou already has a generic progressive growth primitive that does exactly this.

A housing seed type defines growth domains and phase-based capability unlocks:

| Seed Phase | Example Capabilities Unlocked | Seed Depth Range |
|---|---|---|
| **Humble** (phase 1) | Single room, basic furniture placement, 10 decoration slots | 0.0 - 2.0 |
| **Established** (phase 2) | Multiple rooms, garden plot, NPC servant (1), visitor hosting | 2.0 - 5.0 |
| **Comfortable** (phase 3) | Crafting stations, expanded storage, advanced voxel tools, NPC servants (3) | 5.0 - 8.0 |
| **Grand** (phase 4) | Large estate, multiple buildings, trade post, public events, WFC room generation | 8.0 - 12.0 |
| **Legendary** (phase 5) | Architectural wonders, teleportation hub, faction headquarters capability | 12.0+ |

The gardener god-actor queries the seed's capability manifest to gate what the player can do. `${seed.housing.decoration_slots}` tells the behavior how many items can be placed. `${seed.housing.visitor_capacity}` gates how many concurrent visitors the game session allows.

This is the same Seed primitive that powers guardian spirit evolution, dungeon core growth, combat archetype mastery, and faction governance. A housing seed type is just another configuration of the generic growth system.

---

## The Scene IS the Layout

Scene (L4) stores hierarchical node trees. A housing layout is a node tree:

```yaml
- id: "housing-root"
  type: "group"
  children:
    - id: "main-room"
      type: "group"
      children:
        - id: "floor-voxels"
          type: "voxel"
          asset: { type: "bvox", assetId: "..." }
          annotations:
            voxel.gridScale: 0.25
            voxel.mesher: "greedy"
        - id: "table-001"
          type: "mesh"
          annotations:
            item.instanceId: "item-guid-here"
        - id: "fireplace"
          type: "reference"
          sceneRef: "prefab:fireplace-stone-v2"
    - id: "garden-plot"
      type: "voxel"
      asset: { type: "bvox", assetId: "..." }
      annotations:
        voxel.gridScale: 0.25
```

Scene already provides the checkout/commit/discard workflow for exclusive editing, version history, validation rules, and full-text search. A housing plugin would need to reinvent all of this or delegate to Scene -- which is just using Scene with extra steps.

**Item-Scene node binding** is handled by convention: Item instances use `customStats` to track placement (`placed_scene_id`, `placed_node_id`), and Scene nodes carry `annotations.item.instanceId` referencing back. The gardener behavior enforces consistency, with Scene checkout as the implicit transaction boundary. If the scene commit fails after an inventory removal, the behavior compensates by returning the item to inventory.

---

## The Voxel Builder SDK IS the Construction Tool

The Voxel Builder SDK is a pure computation library (like MusicTheory, SceneComposer, AssetBundler) that runs on both client and server. It provides everything a player needs to build within their housing space:

| Building Operation | SDK Component |
|---|---|
| Place/erase individual voxels | `PlaceOperation`, `EraseOperation` |
| Paint with sphere/cube/cylinder brushes | `BrushOperation` |
| Fill rectangular regions | `BoxOperation`, `FillOperation` |
| Mirror structures across axes | `MirrorOperation` |
| Copy and paste sections | `CopyPasteOperation` |
| Undo/redo everything | `OperationStack` |
| Procedurally generate rooms | `WaveFunctionCollapse`, `TemplateStamper` |

The gardener god-actor gates which operations are available based on the housing seed's capability manifest. A new homeowner gets basic place/erase. An established homeowner gets brushes and fill. A grand homeowner unlocks WFC generation to procedurally compose room layouts from authored tilesets. This is the same progressive agency model from PLAYER-VISION.md, manifested through Agency's UX module system.

Save-Load persists voxel data efficiently: the `.bvox` format is chunk-aligned (16x16x16), so a single modified voxel re-serializes one chunk (~4KB compressed), not the entire grid. Delta saves via Save-Load's JSON Patch system mean only modified chunks transit between saves.

---

## The Divine Actor IS the Experience Manager

The gardener god-actor -- a divine actor running via Puppetmaster on the L2 Actor runtime -- manages the housing experience the same way a regional watcher manages a realm region. The god's ABML behavior document encodes the orchestration logic:

- **Spawn NPC servants** when the seed's capabilities unlock them
- **Manage visitor arrivals** (create game sessions, apply permission role matrices)
- **React to events** (seasonal decoration changes, neighborhood events from the realm, divine blessings)
- **Gate building operations** based on seed capabilities
- **Compensate for failures** (return items to inventory if scene commit fails)
- **Orchestrate ambiance** (weather, lighting, music selection based on housing theme)

The god-actor doesn't know it's managing a house. It's executing an ABML behavior document that references housing-specific variables (`${seed.housing.*}`, `${garden.visitor_count}`, `${inventory.housing.item_count}`) and calls housing-specific APIs (Gardener, Scene, Inventory). A different behavior document on the same divine actor infrastructure manages a dungeon. Another manages a void. The infrastructure is shared; the behavior is authored.

---

## Why This Matches the Dungeon Pattern

The strongest evidence that housing doesn't need a plugin: dungeons don't have one either. Dungeon cores are structurally identical to player housing:

| Aspect | Dungeon Core | Player Housing |
|---|---|---|
| Conceptual space | Dungeon garden (Pattern A) | Housing garden |
| Entity progression | `dungeon_core` seed type | `housing` seed type |
| Physical layout | Scene node tree + voxel data | Scene node tree + voxel data |
| Interactive modification | Dungeon core reshapes chambers | Player builds and decorates |
| God-actor management | Dungeon awareness behavior doc | Housing management behavior doc |
| Visitor model | Adventurers enter (game sessions) | Friends visit (game sessions) |
| Capability unlocks | Room generation, trap complexity, memory depth | Room count, decoration slots, crafting stations |
| Persistence | Save-Load + Asset | Save-Load + Asset |

If a dungeon -- with its complex layout shifting, trap activation, memory manifestation, and creature spawning -- can compose from existing primitives, a player house certainly can.

---

## Why Not Build One Anyway?

The strongest argument: "Players think of 'my house,' not 'a housing garden type with a seed-backed capability manifest.' A dedicated service would present a clearer abstraction."

This confuses the player-facing experience with the data model. Players don't call Bannou services. They interact through a game client that presents whatever UX the game designers choose. The client can display "Your Cozy Cottage (Level 3)" while the backend stores `seed.housing` at depth 5.3 in phase "Comfortable." The presentation layer belongs in the client UX and the gardener behavior document, not in a redundant backend service.

Building a housing plugin would mean:

1. **Duplicating Gardener's garden lifecycle** (enter/leave/transition) but calling it "enter/leave house"
2. **Duplicating Seed's growth model** (progressive depth with phase-based capability unlocks) but calling it "house levels"
3. **Duplicating Scene's node tree** (hierarchical object placement with checkout/commit) but calling it "room layout"
4. **Duplicating Save-Load's persistence** (versioned saves with delta encoding) but calling it "house save"
5. **Duplicating Inventory/Item's placement model** (item instances in containers) but calling it "furniture placement"
6. **Duplicating Permission's role matrix** (entity-scoped access control) but calling it "visitor permissions"
7. **Adding a coordination burden** (keeping six duplicate models synchronized with the authoritative sources)

Every duplicate creates a synchronization problem. When the seed's phase changes, the "house level" must update. When a scene node is committed, the "room layout" must update. When an item is placed, both the "furniture database" and the actual inventory must agree. This coordination overhead is pure cost with no benefit.

---

## The Content Flywheel Connection

Per VISION.md, the content flywheel thesis is: "more play produces more content, which produces more play." Housing participates in this flywheel through existing mechanisms:

1. **Player builds a unique house** -- the voxel data and scene layout accumulate as authored content
2. **The house grows via Seed** -- growth awards from engagement feed the Collection -> Seed -> Status pipeline
3. **NPCs interact with the housing** -- NPC builders can reference player-authored housing designs via TemplateStamper, seeding future construction in the game world
4. **Housing history feeds narrative** -- when a character dies, their housing data (via Character History and Resource compression) becomes part of the generative archive. Future Storyline scenarios can reference "the old blacksmith's workshop on Harbor Street" because the data exists
5. **Gods curate housing districts** -- the gardener god-actor for housing can orchestrate neighborhood-level events (trade fairs, building competitions, seasonal festivals) using the same Puppetmaster infrastructure that manages realm regions

None of this requires a housing plugin. Each connection flows through existing event infrastructure, existing compression pipelines, and existing narrative generation.

---

## The Litmus Test

Before proposing a housing plugin, answer these questions:

1. **What state does housing own that no existing store covers?** The layout is in Scene. The items are in Inventory. The progression is in Seed. The persistence is in Save-Load. The voxel data is in Asset. The visitor sessions are in Game Session. The permissions are in Permission. There is no orphaned state.

2. **What orchestration does housing need that no existing orchestrator provides?** Gardener already orchestrates garden lifecycle, entity associations, and divine actor management. The housing garden type is a configuration of Gardener, not a replacement for it.

3. **What business logic is unique to housing?** The behavior document -- authored ABML content that tells the god-actor how to manage a housing garden. Behavior documents are content, not services. They live in Asset, compiled by Behavior, executed by Actor.

4. **Would the plugin just be a facade?** Yes. Every API endpoint would delegate to Gardener (lifecycle), Seed (progression), Scene (layout), Inventory (items), or Save-Load (persistence). A facade over six services with no unique logic is not a service -- it's a client-side abstraction.

If all four answers point to existing primitives, the "missing plugin" is not missing -- it was never needed.

---

## The Composition Is the Design

| Housing Primitive | Service | What It Answers |
|---|---|---|
| "Where am I?" | Gardener | Garden type, entity associations, god-actor lifecycle |
| "How capable is my house?" | Seed | Phase-based progression, capability manifest |
| "What does my house look like?" | Scene + Voxel Builder SDK | Hierarchical node tree with voxel geometry |
| "What's in my house?" | Inventory + Item | Container with placed item instances |
| "Who can visit?" | Game Session + Permission | Session-based access with role matrix |
| "How do I save my house?" | Save-Load + Asset | Versioned delta persistence with MinIO storage |
| "What can I build right now?" | Agency | UX modules gated by housing seed depth |
| "Who manages my experience?" | Divine/Puppetmaster | God-actor executing housing behavior document |
| "How does my house interact with the world?" | Existing event infrastructure | Standard Bannou pub/sub via lib-messaging |

The answer to "where is the housing plugin?" is: it's Gardener + Seed + Scene + Voxel Builder SDK + Save-Load + Item + Inventory + Game Session + Permission + Agency + Divine/Puppetmaster, composed. That composition is player housing.
