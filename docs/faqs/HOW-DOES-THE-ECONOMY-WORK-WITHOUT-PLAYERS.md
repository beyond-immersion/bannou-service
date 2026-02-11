# How Does the Economy Work Without Players?

> **Short Answer**: NPCs drive the economy. The Currency, Item, and Inventory services provide the infrastructure for multi-currency wallets, item instances, and container management. NPCs -- running as actors with GOAP planning -- make economic decisions based on their needs, personality, and world state. They buy, sell, craft, and trade according to their own goals. Player economies layer on top of this NPC economic substrate. If every player logs off, the economy continues because the participants are world citizens, not player avatars.

---

## The Problem With Player-Driven Economies

Most MMO economies have a structural flaw: they only work when players are online. In EVE Online, if half the player base stops logging in, trade volume drops proportionally. Markets that were active become stagnant. The economy is a function of player attendance.

For a living world where the simulation runs continuously whether players are watching or not, this is unacceptable. If the economy stalls when players are offline, the world is not alive -- it is a stage set that goes dark when the audience leaves.

---

## The Three-Layer Economic Architecture

Bannou's economy has three distinct layers, each at a different service hierarchy level:

### Layer 1: The Infrastructure (Currency, Item, Inventory)

**Currency (L2)** provides the monetary primitives:
- **Currency definitions** with scope and realm restrictions. A currency can be global (used across all realms) or realm-specific (only valid in Arcadia, not Fantasia).
- **Wallet management** -- any entity (character, NPC, shop, guild, realm treasury) can have wallets.
- **Balance operations** with idempotency-key deduplication: credit, debit, transfer. Every mutating operation uses distributed locks for multi-instance safety.
- **Authorization holds**: reserve funds, then capture or release. This enables "hold 50 gold while the trade is negotiated" without actually moving the funds until both parties confirm.
- **Exchange rates** via a base-currency pivot. If gold-to-silver is 1:100 and gold-to-copper is 1:10000, the system computes silver-to-copper as 1:100 through the gold pivot.
- **An autogain background worker** for passive income generation. This is how NPCs earn money from their jobs without explicit transactions -- the baker earns a baseline income from operating the bakery.

**Item (L2)** provides the goods primitives:
- **Item templates** (what things can exist): a "steel sword" template with stats, rarity, crafting requirements.
- **Item instances** (specific things that do exist): this particular steel sword with 87% durability and a fire enchantment.
- Multiple quantity models: discrete stacks (47 arrows), continuous weights (2.3 kg of flour), unique items.

**Inventory (L2)** provides the placement primitives:
- **Containers** with constraint models: a shop's display shelf (10 slots, weapons category only), a character's backpack (weight-limited), a guild vault (unlimited).
- **Movement operations**: move items between containers with validation.
- **Stacking**: split and merge stacks.

These three services know nothing about economics. They provide the mechanical primitives -- wallets hold currencies, instances represent goods, containers store instances. The economic behavior emerges from how these primitives are used.

### Layer 2: The Participants (Actor + GOAP)

The Actor service (L2) runs NPC brains. Each NPC has goals, and GOAP (Goal-Oriented Action Planning) finds action sequences to achieve those goals. Economic behavior emerges from NPC goals interacting with the world:

**A blacksmith NPC** might have these GOAP goals:
- Maintain iron ore supply above 20 units.
- Maintain coal supply above 10 units.
- Earn enough gold to pay rent this month.
- Fulfill outstanding customer orders.

GOAP evaluates the world state ("I have 5 iron ore, need 20; I have 30 gold, need 100 for rent") and plans actions: "Go to the mine, purchase 15 iron ore at market price, return to the smithy, forge swords from the iron, sell swords at the storefront."

The blacksmith's buy/sell decisions are not scripted. They emerge from goal satisfaction. If iron ore prices spike because the mine was damaged in a war (a Realm History event), the blacksmith's costs increase, sword prices increase, fewer customers buy swords, the blacksmith's income drops, and the blacksmith may need to find alternative materials or change professions. This cascades through the economy naturally.

**A farmer NPC** decides what to plant based on current market prices, soil conditions, and personal preference (via personality traits). If wheat prices are high because a drought destroyed crops in a neighboring region, more farmers plant wheat, supply increases, prices normalize. Basic supply and demand -- but driven by individual NPC decisions, not by a global "economy manager" script.

### Layer 3: The Orchestration (Escrow at L4, Gods)

**Escrow (L4)** provides atomic multi-party exchanges. When an NPC blacksmith sells a sword to a player character, the transaction goes through escrow:
1. Buyer deposits gold.
2. Seller deposits the sword.
3. Both parties consent.
4. Escrow atomically releases the sword to the buyer and the gold to the seller.

This prevents race conditions, ensures atomicity, and handles failure gracefully (if either party backs out, deposits are returned).

**Regional Watcher gods** (specifically Hermes/Commerce) monitor economic event streams and can intervene narratively. If a realm's economy is stagnating, Hermes might orchestrate a trade route event -- caravans from a distant realm arrive with exotic goods, stimulating trade. If inflation is rampant, Hermes might inspire an NPC noble to establish a new tax. These are not mechanical economic controls -- they are narrative events that have economic consequences.

---

## The Autogain Worker

Currency's background autogain worker deserves specific attention because it solves a bootstrapping problem.

In a player-driven economy, initial currency supply comes from quest rewards and monster drops -- artificial injection. In an NPC-driven economy, currency needs to flow from economic activity. But economic activity requires currency. The chicken-and-egg problem.

The autogain worker provides passive income generation. NPCs with jobs (baker, guard, farmer, merchant) earn a baseline income proportional to their economic activity. This is not "money from nowhere" -- it represents the economic value of their labor that the simplified simulation does not model transaction-by-transaction.

A guard earns a salary from the realm treasury. The realm treasury is funded by taxes on trade. Trade generates currency through the exchange of goods between NPCs. The autogain worker short-circuits the parts of this chain that would be too expensive to simulate individually (tracking every tax payment on every transaction) while preserving the economic dynamics that matter (NPCs have income proportional to their contribution).

---

## Why This Requires Separate L2 Services

The question "why not just have one economy service?" is natural. The answer is that the economy is not one system -- it is an emergent property of multiple independent systems interacting:

- **Currency** manages the medium of exchange. It does not know about items or inventories.
- **Item** manages the goods. It does not know about currency or containers.
- **Inventory** manages the placement. It does not know about currency or item definitions.
- **Actor** manages the participants. It calls Currency, Item, and Inventory to execute economic decisions but does not contain economic logic.
- **Escrow** manages the transactions. It calls Currency and Inventory to execute atomic exchanges but does not determine prices or trade routes.

No single service "is" the economy. The economy is the interaction pattern between these services as mediated by NPC behavior. This decomposition means each service can be independently scaled (Currency gets more load during peak trading hours), independently tested (Currency's distributed lock logic can be unit-tested without Item or Inventory), and independently evolved (adding a new quantity model to Item does not affect Currency).

---

## What Happens When Players Are Offline

When every player logs off:

1. NPC actors continue running (Actor is L2, always on).
2. NPCs continue making economic decisions via GOAP.
3. NPCs continue buying, selling, crafting, and trading via Currency, Item, and Inventory.
4. The autogain worker continues generating baseline income.
5. Supply and demand continue fluctuating based on NPC activity.
6. Regional Watcher gods continue monitoring economic event streams and orchestrating narrative interventions.

When players log back in, the economy has evolved. Prices have shifted. The blacksmith sold out of steel swords because a war started and the guard captain bought them all. Wheat prices dropped because the drought ended and farmers overplanted. The merchant guild raised tariffs on imported goods.

None of this was scripted. None of this required player participation. The economy is a simulation that runs on the same foundational infrastructure as everything else in the living world.
