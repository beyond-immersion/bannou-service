# Location-Bound Production: From Farming to Factorio

> **Status**: Design
> **Created**: 2026-02-18
> **Author**: Lysander (design) + Claude (analysis)
> **Category**: Cross-cutting pattern (behavioral + Workshop orchestration)
> **Related Services**: Workshop (L4), Inventory (L2), Item (L2), Location (L2), Transit (L2), Utility (L4), Environment (L4), Worldstate (L2), Craft (L4), Trade (L4)
> **Related Plans**: [MEMENTO-INVENTORIES.md](MEMENTO-INVENTORIES.md), [DEATH-AND-PLOT-ARMOR.md](DEATH-AND-PLOT-ARMOR.md)
> **Related Docs**: [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md), [VISION.md](../reference/VISION.md)
> **Related Deep Dives**: [WORKSHOP.md](../plugins/WORKSHOP.md), [UTILITY.md](../plugins/UTILITY.md), [TRANSIT.md](../plugins/TRANSIT.md)

---

## Executive Summary

A location is a container. A container holds items. Items transform over time. Workshop already models time-based transformation with lazy evaluation against Worldstate's game clock, source/destination inventories, worker scaling, and environment-modified rates. Location already supports hierarchical sub-locations. Inventory already supports containers associated with any entity.

Put these together: **location-bound production** is a general pattern where sub-locations hold stage-based inventories, Workshop blueprints drive transformation from one stage to the next, and the game clock progresses production even when nobody is watching. This pattern handles farming, construction, fermentation, mining, composting, and -- when combined with Transit for item transport and reactive production triggers -- full factory automation chains approaching Factorio or Satisfactory complexity.

No new services. No new plugins. Workshop blueprints (seed data), inventory containers (standard lib-inventory), location hierarchy (standard lib-location), and ABML behaviors for NPC autonomous operation. The heaviest lift is one Workshop design decision: reactive production triggered by inventory events, which transforms Workshop from a passive timer into an active production pipeline.

---

## The Stage Inventory Pattern

### Core Concept

Instead of mutating item data to track progression (which complicates stacking and requires polling for state changes), model progression as **movement between stage inventories**. Each stage is a standard lib-inventory Container at a location. Workshop blueprints consume from one stage container and produce into the next.

```
STAGE_1_CONTAINER ──Workshop Blueprint──▶ STAGE_2_CONTAINER ──Workshop Blueprint──▶ STAGE_3_CONTAINER
   (source)            (time-based)          (source)            (time-based)          (output)
```

The item in each container may be the same template (a wheat plant progressing through growth) or a different template (raw ore becoming refined ingot). The stage containers tell you the state at a glance: "This farm has 12 items in GROWTH_SPROUT and 3 in GROWTH_MATURE" is immediately queryable without examining any item's custom stats.

### Why Stage Inventories Over Mutable Items

| Approach | Pros | Cons |
|----------|------|------|
| **Mutable item stats** | Single container, item stays put | Requires polling for state changes; breaks stacking if stats diverge; no natural Workshop integration |
| **Stage inventories** | Workshop composes naturally; state visible from container queries; event-driven transitions; clear capacity per stage | More containers to manage; item moves between containers |

Stage inventories win because they compose with Workshop's existing source/destination model without modification. Workshop already consumes from a source inventory and produces to a destination inventory. The stage containers ARE the source and destination.

### The Farm Example

A wheat farm sub-location gets these containers on creation:

| Container | Purpose | Capacity | Notes |
|-----------|---------|----------|-------|
| `GROWTH_SEED` | Planted seeds awaiting germination | Per plot size | Player/NPC places seed items here |
| `GROWTH_SPROUT` | Germinated, fragile | Same | Workshop produces here from SEED |
| `GROWTH_JUVENILE` | Growing, needs tending | Same | Workshop produces here from SPROUT |
| `GROWTH_MATURE` | Ready for harvest | Same | Workshop produces here from JUVENILE |
| `GROWTH_DYING` | Past peak, degrading | Same | Workshop produces here from MATURE if unharvested |
| `GROWTH_DEAD` | Fully dead, seeds recoverable | Same | Workshop produces here from DYING |
| `HARVEST_OUTPUT` | Harvested produce | Configurable | Harvest action moves from MATURE to here |

### Workshop Blueprints for Farming

Each growth stage transition is a Workshop blueprint:

```
Blueprint: wheat_germination
  Input: 1x WHEAT_SEED (from GROWTH_SEED container)
  Output: 1x WHEAT_SPROUT (to GROWTH_SPROUT container)
  baseGameSecondsPerUnit: 86400  (1 game-day)
  minWorkers: 0  (grows passively -- sunlight and rain)
  Rate modifiers: Environment.moisture * Environment.temperature_suitability * Environment.season_modifier

Blueprint: wheat_growth_stage_1
  Input: 1x WHEAT_SPROUT (from GROWTH_SPROUT container)
  Output: 1x WHEAT_JUVENILE (to GROWTH_JUVENILE container)
  baseGameSecondsPerUnit: 259200  (3 game-days)
  minWorkers: 0
  Rate modifiers: same environmental factors + optional worker bonus (tending)

Blueprint: wheat_maturation
  Input: 1x WHEAT_JUVENILE (from GROWTH_JUVENILE container)
  Output: 1x WHEAT_MATURE (to GROWTH_MATURE container)
  baseGameSecondsPerUnit: 432000  (5 game-days)
  minWorkers: 0
  Rate modifiers: environmental + soil quality

Blueprint: wheat_decay
  Input: 1x WHEAT_MATURE (from GROWTH_MATURE container)
  Output: 1x WHEAT_DYING (to GROWTH_DYING container)
  baseGameSecondsPerUnit: 172800  (2 game-days after maturity)
  minWorkers: 0
  Note: this represents unharvested crops rotting

Blueprint: wheat_death
  Input: 1x WHEAT_DYING (from GROWTH_DYING container)
  Output: 1x WHEAT_DEAD_HUSK (to GROWTH_DEAD container)
  baseGameSecondsPerUnit: 86400  (1 game-day)
  minWorkers: 0
  Note: dead husks can be composted or seeds recovered
```

**The lazy evaluation is key**: A player plants wheat and logs off for a week. When they return, Workshop's materialization evaluates all elapsed game-time at once. The wheat has progressed through germination, growth, maturation, and may be dying if unharvested. All computed retroactively from the rate segments and elapsed time. No server ticks needed during the player's absence.

**Environment integration**: Workshop's rate modifiers come from Environment service (L4), which provides seasonal growth multipliers, moisture levels, temperature suitability, and soil quality. A drought reduces the growth rate. A frost kills plants in GROWTH_SPROUT (rate modifier goes to 0, or a special "frost_kill" blueprint activates that consumes SPROUT and produces DEAD directly). All driven by the same Environment data that drives weather simulation for the rest of the world.

---

## The Sub-Location Pattern

### Farms as Sub-Locations

A farm is created as a sub-location under its parent location (a village, a homestead, wilderness), exactly as dungeon rooms are sub-locations under the dungeon:

```
Village of Millfield (Location)
├── Market Square (sub-location)
├── The Rusty Anvil Inn (sub-location)
├── Aldric's Wheat Farm (sub-location)  ← farm sub-location
│   ├── North Field (sub-sub-location)  ← individual plots, optional
│   └── South Field (sub-sub-location)
└── Mill (sub-location)
```

The farm sub-location gets its stage inventories on creation. The owner (character, household, organization) owns the sub-location. Location's existing hierarchy, depth tracking, and realm scoping all apply.

### Creation Flow

1. Character acquires land (via Contract, purchase, or settlement)
2. Farm sub-location created under parent location: `POST /location/create`
3. Stage containers created for the farm: `POST /inventory/container/create` (one per stage)
4. Container IDs stored as location metadata or in a farm registry
5. Workshop tasks created for each growth stage blueprint: `POST /workshop/task/create` with `sourceInventoryId` and `destinationInventoryId` pointing to the stage containers
6. Tasks start in `paused:no_materials` state (nothing planted yet)
7. Player plants seeds (moves seed items from personal inventory to GROWTH_SEED container)
8. Workshop task resumes, growth begins

**No code changes required beyond what's already planned** -- Location supports sub-locations, Inventory supports containers at locations, Workshop supports location-owned tasks.

---

## Reactive Production: The Missing Piece

### The Current Model

Workshop's current design uses two materialization triggers:
1. **Lazy**: Production materializes when someone queries the task status
2. **Background worker**: Every 30 real seconds, the materialization worker processes tasks

This is perfect for slow-progression systems (farming, construction, aging). A 30-second background tick for a crop that takes 3 game-days to grow is more than sufficient.

### The Factory Automation Model

For Factorio/Satisfactory-style production chains, 30-second ticks are too slow. When an item arrives on a belt and enters a machine's input buffer, the machine should start processing immediately and output the result when done. The chain needs to be responsive.

**The reactive trigger**: Workshop subscribes to inventory events. When an item is added to a task's source inventory, the task is notified and can begin processing immediately if it has sufficient materials.

```
Item arrives in source inventory
    → inventory.item.added event published (already exists in lib-inventory design)
    → Workshop task listening on that container wakes up
    → Checks material sufficiency
    → If sufficient: begins production timer (or instant for zero-time recipes)
    → On completion: output placed in destination inventory
    → Destination inventory's item.added event fires
    → Downstream Workshop task wakes up
    → Chain continues
```

This is a single design addition to Workshop: **event-driven task activation alongside the existing lazy/background paths**. The three materialization triggers coexist:

| Trigger | Latency | Use Case |
|---------|---------|----------|
| **Lazy** (on query) | On-demand | Player checks status, NPC queries production |
| **Background worker** (30s tick) | 0-30 seconds | Slow progression (farming, aging, fermentation) |
| **Reactive** (inventory event) | Near-instant | Factory chains, conveyor belt automation |

### Blueprint Configuration

A new flag on Workshop blueprints controls the reactive behavior:

```yaml
Blueprint: smelt_iron_ore
  inputs:
    - template_code: IRON_ORE
      quantity: 2
  outputs:
    - template_code: IRON_INGOT
      quantity: 1
  baseGameSecondsPerUnit: 600  (10 game-minutes per ingot)
  minWorkers: 1
  reactiveTrigger: true  # ← NEW: wake on source inventory change
  # When false (default): rely on lazy/background materialization only
  # When true: also subscribe to source inventory events
```

Farming blueprints leave `reactiveTrigger: false` (default). Factory blueprints set it to `true`. The behavior is identical in both cases -- the reactive trigger just adds an additional materialization check on inventory events. The same `MaterializeProduction` algorithm runs regardless of trigger source.

---

## Transport: Belts and Logistics

### Discrete Transport via Transit

Transit (L2) provides the edges between locations. Items moving between locations travel via Transit journeys. In a factory context:

| Factorio Concept | Arcadia Equivalent |
|---|---|
| Conveyor belt | Transit connection between sub-locations with a cargo-carrying mode |
| Belt speed | Transit mode base speed (configurable per mode code) |
| Item on belt | Item in transit (Journey with cargo) |
| Belt segment | Transit connection (one edge in the location graph) |

A "belt" between two factory stations is a Transit connection with a `belt` or `cart` or `minecart` transit mode. Items placed in one station's output container are loaded into a journey, travel the connection, and arrive at the next station's input container.

**The transport mechanism**:
1. Factory station A completes production, output goes to OUTBOX container
2. Transport actor (NPC worker, automated cart, magical conveyor) picks up items from OUTBOX
3. Transit journey created: A → B via belt/cart connection
4. Journey arrives at B, items placed in B's INBOX container
5. Reactive Workshop trigger: B's task wakes up, processes, outputs to B's OUTBOX
6. Transport actor picks up from B's OUTBOX, delivers to C's INBOX
7. Chain continues

**For the game client**: The visual belt/conveyor is a client-side rendering of items in transit between locations. The server tracks the journey (departure time, arrival time, items in transit). The client interpolates the visual position. The server doesn't need to tick every frame -- it computes arrival time from Transit's route calculation, and the client handles the visual interpolation.

### Continuous Transport via Utility

For resources that flow continuously rather than in discrete units (water, power, mana, gas), Utility provides the network:

| Factorio Concept | Arcadia Equivalent |
|---|---|
| Pipe | Utility connection (network type: "water", "gas", etc.) |
| Pipe throughput | Connection capacity (units/game-hour) |
| Fluid | Utility network resource type |
| Pump | Workshop production source registered with Utility |
| Tank | Location with Utility coverage snapshot |

**Workshop produces, Utility distributes**: A water pump (Workshop task producing "water" at a well location) registers as a Utility source. Utility's flow calculation distributes that water through pipe connections to downstream locations. A farm location's irrigation system is a Utility coverage consumer -- the farm's growth rate modifier includes `${utility.water.coverage}` as a factor.

### The Factorio Pipeline

Combining discrete (Transit) and continuous (Utility) transport:

```
Iron Mine (Location)
├── MINING_FACE (sub-location)
│   └── Workshop task: mine_iron_ore (produces IRON_ORE to OUTBOX)
├── OUTBOX (inventory container)
│   └── Transport actor picks up ore
│
── Transit connection (minecart, 2km, 0.5 game-hours) ──
│
Smelter (Location)
├── INBOX (inventory container)
│   └── Ore arrives from mine
├── FURNACE (sub-location)
│   └── Workshop task: smelt_iron_ore (reactive trigger)
│       Source: INBOX, Destination: OUTBOX
│       Rate modified by: Utility.coal_power coverage
├── OUTBOX (inventory container)
│   └── Transport actor picks up ingots
│
── Transit connection (cart, 5km, 1 game-hour) ──
│
Smithy (Location)
├── INBOX (inventory container)
│   └── Ingots arrive from smelter
├── FORGE (sub-location)
│   └── Workshop task: forge_sword (reactive trigger)
│       Source: INBOX (iron ingots) + FUEL_SUPPLY (coal via Utility)
│       Destination: OUTBOX
│       Workers: 1 (smith NPC)
│       Rate modified by: worker proficiency, Environment.temperature
├── OUTBOX (inventory container)
│   └── Finished swords for trade
```

### Splitters and Combiners

A junction location with routing logic handles splitting and combining:

**Splitter**: One OUTBOX, multiple Transit connections to different destinations. The transport actor (or automated routing behavior) decides which items go where based on item template, fill level of downstream INBOX containers, or priority rules.

```yaml
# NPC transport routing behavior at a junction
route_output:
  when:
    condition: "${self.location.outbox.item_count} > 0"
    for_each: ${self.location.outbox.items}
    as: item
    evaluate:
      - route:
          when: "${item.template_code} == 'IRON_INGOT'"
          destination: smithy_inbox
      - route:
          when: "${item.template_code} == 'COPPER_INGOT'"
          destination: jeweler_inbox
      - route:
          default: true
          destination: warehouse_inbox
```

**Combiner**: Multiple Transit connections deliver to one INBOX. The Workshop task at this location has multiple input requirements. Reactive trigger fires when ANY input arrives, but production only starts when ALL inputs are sufficient. This is Workshop's existing material sufficiency check -- it already handles multi-input blueprints.

---

## Domain Applications

The stage inventory + Workshop pattern generalizes across any domain where things transform over time at a location.

### Farming

| Stage | Container | Workshop Blueprint | Duration | Notes |
|-------|-----------|-------------------|----------|-------|
| Planted | GROWTH_SEED | seed → sprout | 1 game-day | Passive (minWorkers: 0) |
| Sprouted | GROWTH_SPROUT | sprout → juvenile | 3 game-days | Fragile -- frost can kill |
| Growing | GROWTH_JUVENILE | juvenile → mature | 5 game-days | Tending adds workers = faster |
| Mature | GROWTH_MATURE | mature → dying | 2 game-days (if unharvested) | Harvest window |
| Dying | GROWTH_DYING | dying → dead | 1 game-day | Seeds recoverable from dead |
| Dead | GROWTH_DEAD | -- | -- | Compost or recover seeds |

**Environment factors**: Season (spring 1.5x, summer 1.0x, fall 0.5x, winter 0x), moisture, temperature, soil quality. A drought in midsummer halves growth rate. An unexpected frost kills sprouts.

**NPC farmer behavior**: GOAP planner evaluates: season approaching? → plant. Crops mature? → harvest. Market price high? → sell. Price low? → store. Drought coming? → irrigate (add workers to Workshop tasks). All from `${workshop.*}` and `${environment.*}` variables.

### Construction

| Stage | Container | Workshop Blueprint | Duration | Notes |
|-------|-----------|-------------------|----------|-------|
| Foundation | CONSTRUCTION_FOUNDATION | foundation → frame | 5 game-days | Requires workers (minWorkers: 2) |
| Frame | CONSTRUCTION_FRAME | frame → walls | 8 game-days | Material-dependent |
| Walls | CONSTRUCTION_WALLS | walls → roof | 6 game-days | Weather delays in rain/snow |
| Roof | CONSTRUCTION_ROOF | roof → finishing | 4 game-days | Workers with carpentry proficiency |
| Finishing | CONSTRUCTION_FINISHING | finishing → complete | 3 game-days | Interior work |
| Complete | CONSTRUCTION_COMPLETE | -- | -- | Building usable |

**Material consumption**: Each blueprint stage consumes specific materials from a MATERIALS container at the construction site. Workers carry materials from warehouses via Transit. Missing materials pause the Workshop task (`paused:no_materials`).

**The NPC construction crew**: An Organization (construction guild) assigns workers to construction tasks. The foreman NPC's GOAP planner manages material procurement, worker scheduling, and deadline tracking -- all from Workshop variable provider data.

### Fermentation / Brewing / Aging

| Stage | Container | Workshop Blueprint | Duration | Notes |
|-------|-----------|-------------------|----------|-------|
| Raw | FERMENT_RAW | raw → fermenting | 0.5 game-days | Preparation (crushing, mixing) |
| Fermenting | FERMENT_ACTIVE | fermenting → aging | 7 game-days | Temperature-sensitive |
| Aging | FERMENT_AGING | aging → mature | 30 game-days | Longer = better quality |
| Mature | FERMENT_MATURE | mature → peak | 60 game-days | Optional extended aging |
| Peak | FERMENT_PEAK | peak → degrading | 90 game-days | Optimal window |
| Degrading | FERMENT_DEGRADING | -- | -- | Past prime, vinegar |

**Quality scaling with duration**: Items that age longer before being harvested could carry a quality modifier based on time spent in the aging stage. The lazy evaluation computes exact elapsed time, enabling quality differentiation between a 30-day wine and a 90-day wine.

**The NPC vintner**: Checks `${workshop.task.wine_aging.progress}` and personality's patience axis to decide when to bottle. An impatient NPC bottles early (lower quality, faster to market). A perfectionist waits for peak (higher quality, slower to market). Emergent quality variation in the economy.

### Mining / Ore Processing

| Stage | Container | Workshop Blueprint | Duration | Notes |
|-------|-----------|-------------------|----------|-------|
| Survey | MINE_SURVEYED | surveyed → excavating | 2 game-days | Geology skill |
| Excavation | MINE_EXCAVATING | excavating → raw_ore | 1 game-day per unit | Workers required |
| Raw Ore | MINE_RAW_ORE | raw → processed | 0.5 game-days | Crushing, sorting |
| Processed | MINE_PROCESSED | processed → refined | 1 game-day | Smelting, requires fuel |
| Refined | MINE_REFINED | -- | -- | Final product |

### Composting / Organic Recycling

| Stage | Container | Workshop Blueprint | Duration | Notes |
|-------|-----------|-------------------|----------|-------|
| Fresh | COMPOST_FRESH | fresh → decomposing | 3 game-days | Any organic material input |
| Decomposing | COMPOST_ACTIVE | decomposing → humus | 14 game-days | Temperature-dependent |
| Humus | COMPOST_HUMUS | humus → rich_soil | 7 game-days | Moisture-dependent |
| Rich Soil | COMPOST_OUTPUT | -- | -- | Used as farming input (soil quality modifier) |

**Closed loop with farming**: Dead crops (GROWTH_DEAD) → compost input → rich soil → farming growth rate modifier. A self-sustaining agricultural system.

### Factory Automation

| Station | INBOX | Workshop Blueprint | OUTBOX | Transport |
|---------|-------|-------------------|--------|-----------|
| Mine | -- | mine_iron_ore | IRON_ORE | Minecart to smelter |
| Smelter | IRON_ORE | smelt_iron_ore | IRON_INGOT | Cart to smithy |
| Smithy | IRON_INGOT | forge_sword_blade | SWORD_BLADE | Cart to assembly |
| Assembly | SWORD_BLADE + LEATHER_GRIP | assemble_sword | IRON_SWORD | Cart to warehouse |
| Warehouse | IRON_SWORD | -- | -- | Trade/NPC purchase |

Each station is a sub-location. Each has INBOX/OUTBOX containers. Workshop blueprints with `reactiveTrigger: true` process immediately on material arrival. Transport actors (NPC workers, automated carts, magical conveyors) move items between stations via Transit connections.

---

## The Factorio Mapping

How Arcadia's existing + planned services map to factory automation game concepts:

| Factorio/Satisfactory | Arcadia Service | Mechanism |
|---|---|---|
| **Assembling machine** | Workshop (L4) | Blueprint with source/destination inventories, time-based production |
| **Conveyor belt** | Transit (L2) | Connection between sub-locations with cargo mode |
| **Item on belt** | Transit journey | Item in transit with departure/arrival times |
| **Belt speed** | Transit mode speed | Configurable per transit mode code |
| **Splitter** | Junction location + ABML routing | Transport actor routes items by template/priority |
| **Merger** | Multi-input Workshop blueprint | Reactive trigger fires on any input; produces when all sufficient |
| **Pipe** | Utility (L4) | Continuous flow network with capacity/condition |
| **Fluid** | Utility network resource | Water, gas, mana -- continuous consumption |
| **Power grid** | Utility (L4) | Power network type; Workshop rate modified by power coverage |
| **Logistics bot** | NPC transport actor | ABML behavior for pickup/delivery between locations |
| **Train** | Transit journey with train mode | High-capacity, scheduled, multi-stop route |
| **Storage chest** | Inventory container | Standard lib-inventory container at a location |
| **Research** | License (L4) | Grid-based progression unlocking new blueprints |
| **Pollution / environment** | Environment (L4) | Factory emissions affect local environment conditions |
| **Power consumption** | Utility coverage dependency | Workshop rate modified by `${utility.power.coverage}` |
| **Recipe** | Craft recipe or Workshop blueprint | Inputs → outputs with time and worker requirements |
| **Module / beacon** | Worker proficiency + Seed growth | Worker skill and entity seed capabilities modify rate |
| **Circuit network** | ABML behavior logic | NPC or event brain evaluates conditions, controls production |
| **Blueprint (Factorio)** | Workshop blueprint template | Reusable production definition |

### What's Not Directly Mapped

| Factorio Concept | Gap | Potential Approach |
|---|---|---|
| **Belt insertion** (inserter arms) | No dedicated "move item between adjacent containers" primitive | NPC transport worker with very short route, or direct inventory transfer API |
| **Stack size on belts** | Transit cargo is item-based, not stack-based | Transit mode cargo capacity effectively limits throughput |
| **Underground belts** | No concept of hidden transit connections | Transit connections can be tagged "underground" for client rendering |
| **Blueprints (player-placeable factory layouts)** | No "stamp a factory layout" primitive | Save-Load (L4) could store/restore location hierarchies with inventory/Workshop configurations |
| **Real-time belt animation** | Server tracks journeys, not per-frame positions | Client interpolates visual position from journey departure/arrival times |
| **Ratio calculation** | No built-in rate visualization | Client computes from Workshop variable provider data (`${workshop.task.*.rate}`) |

### The Critical Path to Factorio

What must exist for factory automation gameplay:

1. **Workshop (L4)** -- implemented with blueprint/task/worker/materialization system *(planned, not yet built)*
2. **Reactive production trigger** -- Workshop subscribes to inventory events for `reactiveTrigger: true` blueprints *(design addition to Workshop)*
3. **Transit (L2)** -- implemented with connection/journey/mode system *(planned, not yet built)*
4. **Automated transport** -- ABML behaviors for NPC/automated cargo movement between locations *(behavior authoring)*
5. **Utility (L4)** -- implemented for continuous resource flow *(planned, not yet built)*
6. **Client rendering** -- Visual representation of factories, belts, items in transit *(game client work)*

Items 1, 3, and 5 are already planned. Item 2 is a design addition. Item 4 is ABML authoring. Item 6 is client-side. No new services needed.

---

## NPC Autonomous Factory Operation

### The NPC Factory Owner

An NPC who owns a factory operates it through ABML behaviors driven by Workshop's variable provider:

```yaml
# NPC factory manager GOAP evaluation
evaluate_factory:
  # Check material supply
  - when:
      condition: "${workshop.any_paused_no_materials} == true"
      actions:
        - add_goal:
            type: procure_materials
            priority: 0.8
            context:
              paused_tasks: ${workshop.paused_tasks.no_materials}

  # Check output storage
  - when:
      condition: "${workshop.any_paused_no_space} == true"
      actions:
        - add_goal:
            type: sell_output
            priority: 0.7
            context:
              full_containers: ${workshop.paused_tasks.no_space}

  # Check worker availability
  - when:
      condition: "${workshop.total_producing} < ${workshop.active_task_count} * 0.7"
      actions:
        - add_goal:
            type: hire_workers
            priority: 0.5

  # Evaluate profitability
  - when:
      condition: "${trade.market_price.iron_sword} < ${workshop.task.forge_sword.cost_per_unit}"
      actions:
        - add_goal:
            type: pause_unprofitable
            priority: 0.6
            context:
              task: forge_sword
```

### The NPC Supply Chain

Multiple NPC factory owners, each operating independently, create emergent supply chains:

1. **NPC miner** operates a mine, sells raw ore to the highest bidder (GOAP: mine → sell)
2. **NPC smelter** buys ore, smelts ingots, sells to smiths (GOAP: buy ore → smelt → sell ingots)
3. **NPC smith** buys ingots, forges weapons, sells to merchants (GOAP: buy ingots → forge → sell weapons)
4. **NPC merchant** buys weapons, transports to market, sells to consumers (GOAP: buy → transport → sell)

Each NPC responds to price signals from Trade/Market services. When iron ore price rises (supply shortage), the miner's GOAP planner increases mining priority. When sword prices drop (oversupply), the smith pauses production. The economy self-regulates through NPC GOAP decisions based on real supply and demand -- no authored economic scripts.

### NPC Farmers in the Economy

NPC farmers are the agricultural equivalent:

1. Evaluate season and weather from `${environment.*}` and `${world.season}`
2. Select crop type based on market prices from `${trade.market_price.*}` and soil suitability
3. Plant seeds (move items to GROWTH_SEED container)
4. Tend crops (add workers to Workshop tasks for growth bonus) based on available labor
5. Harvest at maturity (move from GROWTH_MATURE to HARVEST_OUTPUT)
6. Sell at market or store based on price predictions (personality's risk tolerance affects timing)
7. Compost dead crops (move GROWTH_DEAD to COMPOST_FRESH container for soil improvement)

A village of NPC farmers, each with different personality axes (risk-tolerant vs. conservative, patient vs. hasty, diverse vs. specialized), produces an emergent agricultural economy with genuine supply variation based on weather, personality, and market conditions.

---

## Relationship to Other Systems

### Environment (L4)

Environment provides the conditions that modify Workshop production rates:

| Environmental Factor | Effect on Production | Workshop Mechanism |
|---------------------|---------------------|-------------------|
| **Season** | Spring growth bonus, winter growth stop | Rate modifier via Environment variable provider |
| **Temperature** | Optimal range per blueprint, penalty outside | Rate modifier |
| **Moisture** | Drought reduces farm output, rain helps | Rate modifier |
| **Weather events** | Storms damage construction, frost kills sprouts | Special "damage" blueprints activate |
| **Soil quality** | Farm growth rate modifier | Rate modifier per location |
| **Altitude/biome** | Some crops only grow in certain biomes | Blueprint location constraints |

### Worldstate (L2)

Workshop's lazy evaluation is built on Worldstate's game clock. Every rate calculation converts real time to game time using the realm's time ratio. Season changes trigger rate segment recalculation. Day/night cycles can affect blueprints that require sunlight (farming) vs. those that don't (mining, smelting).

### Trade (L4)

Trade provides the economic context for production decisions. NPC factory owners consult `${trade.market_price.*}` to decide what to produce, when to sell, and whether production is profitable. Trade routes (Transit-based) determine transport costs. Supply/demand dynamics emerge from the aggregate production decisions of hundreds of NPC producers.

### Utility (L4)

Utility connects Workshop production to location-wide coverage. A sawmill Workshop task at a lumber camp produces wood. If the lumber camp is registered as a Utility source for "building_materials" network, Utility's flow calculation distributes that production across connected locations. A construction site downstream has its Workshop rate modified by Utility's building material coverage -- if the sawmill pauses (no workers, no trees), construction downstream slows.

### Transit (L2)

Transit provides the transport layer. Items moving between factory stations travel as Transit journeys. Journey arrival triggers the reactive Workshop production at the destination. Transit's mode system (minecart, cart, river barge, flying mount) determines transport speed and capacity, creating natural logistics constraints.

### Memento Inventories

Location-bound production generates mementos. A masterwork sword forged at a legendary smithy creates a `CRAFT_MEMENTO` at that location. A mine collapse that kills workers creates `DEATH_MEMENTO` instances. The memento inventory system documented in [MEMENTO-INVENTORIES.md](MEMENTO-INVENTORIES.md) captures the spiritual residue of production activity, making historically significant workshops and factories into locations with genuine character.

---

## Relationship to Vision Principles

| Vision Principle | How Location-Bound Production Serves It |
|-----------------|----------------------------------------|
| **Living Game Worlds** (North Star #1) | Factories, farms, and workshops operate autonomously through NPC GOAP decisions. The economy produces and trades without player intervention. |
| **The Content Flywheel** (North Star #2) | Production generates mementos. Economic crises (supply shortages, factory collapses) become narrative events. Infrastructure failures cascade into content. |
| **100K+ Concurrent NPCs** (North Star #3) | Workshop's lazy evaluation means 100K NPCs can each own production tasks without 100K server ticks. Materialization is on-demand, not continuous. |
| **Ship Games Fast** (North Star #4) | The pattern is game-agnostic. Medieval farming, sci-fi manufacturing, magical crafting -- all use the same Workshop + Inventory + Location primitives with different blueprint seed data. |
| **Emergent Over Authored** (North Star #5) | Supply chains, factory layouts, agricultural patterns, and economic specialization all emerge from NPC GOAP decisions and environmental conditions. |
| **Authentic Simulation** (Design Principle #5) | Crop growth modified by real weather, seasonal cycles, and soil quality. Smelting requires fuel. Construction requires materials and workers. Production follows plausible physical constraints. |
| **World-State Drives Everything** (Design Principle #3) | Season determines what grows. Weather determines growth rate. Market prices determine what's produced. Infrastructure determines what's possible. |

---

## Implementation: Minimal New Design

| Component | Service | Status | What's Needed |
|-----------|---------|--------|---------------|
| Stage containers | Inventory (L2) | Exists | Standard containers at locations |
| Production blueprints | Workshop (L4) | Planned (not built) | Blueprint seed data per domain |
| Lazy evaluation | Workshop (L4) | Planned (not built) | Core algorithm already designed |
| **Reactive trigger** | **Workshop (L4)** | **New design addition** | **Subscribe to source inventory events for `reactiveTrigger` blueprints** |
| Location hierarchy | Location (L2) | Exists | Sub-locations for farms/factories |
| Transport between stations | Transit (L2) | Planned (not built) | Journey system with cargo modes |
| Continuous flow | Utility (L4) | Planned (not built) | Network flow calculation already designed |
| Environmental modifiers | Environment (L4) | Planned (not built) | Rate modifier integration with Workshop |
| NPC production behavior | ABML behaviors | Authoring work | Farmer, miner, smith, merchant behaviors |
| Client rendering | Game client | Client work | Farm plots, factory stations, belts, items in transit |

**One design addition**: Workshop `reactiveTrigger` flag on blueprints. Everything else is either already designed, already built, or standard ABML/client authoring.

**The pattern itself requires zero new services and zero new plugins.** It composes entirely from existing primitives. The "farming game," the "factory game," and the "logistics game" are all UX manifests on the same underlying simulation -- exactly as PLAYER-VISION.md describes: "Same systems, different games."

---

*This document describes the design for location-bound production patterns. For memento inventories at locations, see [MEMENTO-INVENTORIES.md](MEMENTO-INVENTORIES.md). For Workshop mechanics, see [WORKSHOP.md](../plugins/WORKSHOP.md). For Utility flow calculation, see [UTILITY.md](../plugins/UTILITY.md). For Transit transport, see [TRANSIT.md](../plugins/TRANSIT.md). For vision context, see [VISION.md](../reference/VISION.md).*
