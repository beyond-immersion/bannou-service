# Item & Economy Plugin Landscape

> **Created**: 2026-02-12
> **Purpose**: High-level survey of remaining plugins needed to support the full item/economy vision
> **Status**: IDEAS ONLY -- no API specs yet, just concept separation and concern mapping
> **Inputs**: VISION.md, PLAYER-VISION.md, ITEM-SYSTEM.md (PoE reference), ECONOMY-CURRENCY-ARCHITECTURE.md, item/inventory/currency/escrow deep dives, STATUS.md plan

---

## What Already Exists

### L1 Foundation
| Service | Endpoints | Role in Item/Economy |
|---------|-----------|---------------------|
| **lib-contract** | 30 | FSM + milestone progression + prebound API execution. THE behavioral engine for item use, crafting workflows, escrow conditions. |
| **lib-resource** | 17 | Reference tracking and cleanup coordination. Ensures safe deletion of entities referenced by items/containers. |

### L2 Game Foundation
| Service | Endpoints | Role in Item/Economy |
|---------|-----------|---------------------|
| **lib-item** | 16 | Template/instance dual model. "Itemize anything" via contract delegation. Quantity models (discrete/continuous/unique). Binding. Contract-delegated use behavior. |
| **lib-inventory** | 16 | Container constraints (slot/weight/grid/volumetric/unlimited). Placement, movement, stacking. Equipment slots. |
| **lib-currency** | 32 | Multi-currency wallets. Credit/debit/transfer with idempotency. Authorization holds. Exchange rates via base-currency pivot. Autogain worker. Escrow endpoints. |
| **lib-collection** | 20 | Universal content unlock/archive. Items-in-containers pattern for collectibles. |
| **lib-seed** | 24 | Progressive growth primitives. Capability manifests. Polymorphic ownership. |

### L4 Game Features
| Service | Endpoints | Status | Role in Item/Economy |
|---------|-----------|--------|---------------------|
| **lib-escrow** | 22 | PARTIAL | Multi-party asset exchange state machine. Token-based consent. **Missing**: actual asset movement via currency/inventory, contract integration, periodic validation. |
| **lib-license** | 20 | Complete | Grid-based progression boards. Thin orchestrator over items + containers + contracts. |

### Already Planned (spec exists)
| Service | Endpoints | Status | Blocked By |
|---------|-----------|--------|------------|
| **lib-status** | ~17 | DRAFT plan (docs/plans/STATUS.md) | #407 (Item decay/expiration system) |

---

## The Architectural Pattern

Every L4 item/economy plugin follows the same composability thesis from #280:

```
L4 Plugin (domain semantics, orchestration)
    |
    +-- lib-item (L2) .......... stores the "thing"
    +-- lib-inventory (L2) ..... places the "thing"
    +-- lib-contract (L1) ...... manages the "thing's" behavior/lifecycle
    +-- lib-currency (L2) ...... handles the money side
    +-- lib-escrow (L4) ........ atomicity for multi-party exchanges
```

The L4 plugin is a **thin orchestration layer** that:
1. Defines domain-specific templates (recipes, affix definitions, loot tables, vendor catalogs)
2. Translates domain operations into item/inventory/contract/currency calls
3. Publishes domain-specific events for analytics and NPC GOAP consumption
4. Provides domain-specific queries (what affixes can this item have? what's the market price?)

**Plugins do NOT duplicate foundation capabilities.** They compose them.

---

## Proposed New Plugins

### Tier 1: High Confidence (Definitely Needed)

#### 1. lib-affix (L4 Game Features)

**Concern**: Item modifier definitions, application, validation, and generation.

**Why it exists**: Items in Arcadia (and any game using Bannou) need modifiers -- enchantments, quality levels, magical properties, stat bonuses. The current lib-item stores these as opaque `customStats` and `instanceMetadata` JSON blobs. lib-affix gives structure to those blobs: typed definitions, tier progressions, exclusivity rules, weighted random generation.

**Why L4**: Optional. Games without complex modifiers don't need it. No L2 service requires affix data.

**Why separate from crafting**: Affixes define WHAT modifiers exist and manage WHICH are on items. Crafting determines HOW modifiers are applied/changed. Different consumers use affixes independently of crafting -- loot generation applies affixes, vendors sell pre-affixed items, NPCs evaluate item worth by affixes.

**Depends on**: lib-item (L2), lib-game-service (L2)

**API concept groups**:
- **Definition management** -- a set for creating/managing affix definitions with tiers, mod groups, spawn weights, stat grants, valid item classes, generation tags
- **Application** -- a set for applying/removing/rerolling affixes on item instances, validating mod group conflicts
- **Generation** -- a set for weighted random affix generation with context (item class, item level, optional weight modifiers like "fossils")
- **Query** -- a set for getting affixes on an item, getting valid affix pool for an item, getting computed item stats from base + affixes
- **Influence/special** -- a set for influence types, fractured/corrupted/mirrored state management, implicit modifier management

**Open questions**:
- Should rarity (normal/magic/rare/unique) be managed by lib-affix or remain an item template property? Recommend to move to lib-affix, otherwise would need to be an overridable string so that higher level plugins can dictate their own rarity without two different properties confusing things.
- Should item level (determines valid affix pool) be a lib-item field or lib-affix concern? Recommend to keep in lib-item, unlike rarity there's no semantic shift with overrides- an item level is meaningless by itself, higher level plugins give it context, but it's never going to have different constraints (positive integers, 1 default, works in every game).
- How does Arcadia's pneuma-based enchantment system map to generic affixes?

---

#### 2. lib-craft (L4 Game Features)

**Concern**: Recipe definitions, multi-step production workflows, skill-gated crafting execution.

**Why it exists**: Arcadia's vision demands 37+ authentic crafting processes mirroring real-world procedures. NPCs craft autonomously via GOAP. Players learn and master recipes across generations. Crafting is a core economic activity (raw materials -> finished goods) that drives NPC supply chains.

**Why L4**: Optional. Depends on lib-item, lib-inventory, lib-contract. NPC crafting integrates with Actor/GOAP.

**Why separate from affix**: Crafting is about PROCESS (recipes, steps, materials, skill). Affixes are about PROPERTIES (what modifiers exist on items). Crafting may apply affixes as part of production, but the recipe system, proficiency tracking, station requirements, and multi-step workflows are distinct concerns.

**Depends on**: lib-item (L2), lib-inventory (L2), lib-contract (L1), lib-currency (L2). Optionally: lib-affix (L4, for applying modifiers during crafting).

**API concept groups**:
- **Recipe management** -- a set for creating/managing recipe definitions (inputs, outputs, steps, skill requirements, tool/station requirements, quality formulas)
- **Crafting execution** -- a set for starting a crafting session, providing materials, executing steps (multi-step via Contract milestones), completing/cancelling
- **Proficiency** -- a set for tracking per-entity crafting skill levels, checking requirements, recording experience gains
- **Station/tool** -- a set for registering crafting station types, checking availability, tool quality effects
- **Discovery** -- a set for recipe discovery (experimentation with unknown combinations), listing known recipes per entity
- **Bulk seeding** -- a set for loading recipe definitions and proficiency templates from configuration

**Open questions**:
- Should proficiency be part of lib-craft or part of a more general "skill" system? Recommend to use proficiency "seeds" (lib-seed) fueled by lib-collection for growth, borrowing from the pattern lib-status uses, or to directly USE lib-status as an optional L4 game feature to provide the status values. This would allow both temporary buffs and permanent blessings to seamlessly benefit crafting without additional complexity to sync them.
- How do NPC crafting decisions integrate with GOAP? Recommend to become the Variable Provider for `${craft.*}` expressions.
- Should recipe outputs support probability (chance of failure, quality variance)? Recommend yes.

---

#### 3. lib-market (L4 Game Features)

**Concern**: Marketplace operations -- auctions, vendor catalogs, price discovery.

**Why it exists**: The economy vision requires auctions (player-to-player and NPC-to-player exchange), NPC vendor shops with dynamic pricing driven by personality and supply, and price tracking for NPC GOAP economic decisions.

**Why L4**: Optional game feature. Depends on lib-currency, lib-item, lib-inventory, lib-escrow.

**Why separate from trade**: Market is about EXCHANGE (buying and selling at a point of sale). Trade is about LOGISTICS (moving goods across distances, borders, tariffs). A game can have markets without trade routes, or trade routes without formal markets.

**Depends on**: lib-item (L2), lib-inventory (L2), lib-currency (L2), lib-escrow (L4). Optionally: lib-affix (L4, for search filtering by modifiers).

**API concept groups**:
- **Auction house** -- a set for creating listings, searching/filtering, placing bids, buyout, cancellation, settlement
- **Vendor catalogs** -- a set for creating/managing NPC vendor inventories (static, dynamic, personality-driven), restocking configuration
- **Buy/sell operations** -- a set for purchasing from vendors, selling to vendors (with buyback pricing)
- **Price analytics** -- a set for average prices, price history, market stats per item template per realm
- **Market configuration** -- a set for market definitions (which realm, listing fees, transaction fee rates -- fee rates are sinks)

**Open questions**:
- Should vendor catalogs be per-NPC or per-location? Recommend per-NPC with location scoping.
- How does dynamic pricing work for personality-driven vendors? GOAP-driven or formula-based? Recommend both possible / configurable per vendor (autonomous or managed)- players seem like they'd be in the former category while playing, too (overriding managed behavior for simpler NPC models without GOAP-driven sales embedded).
- Should auction settlement be synchronous or background-processed? Recommend background-processed at least as an option, with immediate as another.

---

#### 4. lib-loot (L4 Game Features)

**Concern**: Loot table definitions, weighted drop generation, and group distribution.

**Why it exists**: Enemies drop items, chests contain treasure, quest rewards are granted, events produce spoils. Every game needs a system to define "what can be obtained from this source" with weighted probabilities and contextual modifiers.

**Why L4**: Optional. Depends on lib-item. Optionally uses lib-affix for modifier generation on drops.

**Why a separate plugin**: Loot generation is consumed by many systems (combat, quests, world events, NPC death, crafting byproducts) but none of those systems should contain loot logic. Loot tables are configuration data managed independently from the systems that trigger them.

**Depends on**: lib-item (L2), lib-inventory (L2), lib-game-service (L2). Optionally: lib-affix (L4), lib-currency (L2, for currency drops).

**API concept groups**:
- **Table management** -- a set for creating/managing loot tables with weighted entries (item templates, currency amounts, nested sub-tables), probability curves, guaranteed drops
- **Generation** -- a set for rolling loot from a table with context (source level, killer luck, party size, realm modifiers), returning generated item instances
- **Distribution** -- a set for distributing generated loot to a group (need/greed, round-robin, free-for-all, personal loot modes)
- **Preview** -- a set for previewing loot table contents (for tooltips, bestiary entries) without generating
- **Bulk seeding** -- a set for loading loot table definitions from configuration

**Open questions**:
- Should loot tables support "pity" systems (guaranteed drop after N failures)? Recommend yes, configurable per realm- in some, pity-tracking would seem the responsibility for lib-divine, and they could generate a "freebie" on demand, so be sure to add an endpoint for "guarantee NEXT drop" so that it's seamless with actual gameplay not an item appearing from mid-air.
- Should affix generation be embedded in loot rolls or a separate step? Recommend considering: what's the expected impact at 100k NPCs and 100k players or more?
- How do NPCs interact with loot? (NPC death -> loot generated -> items created in "loot container" -> players/NPCs claim). Recommend becoming Variable Provider for "has_loot" GOAP data. Recommend adding GOAP-driven looting behaviors, similar to GOAP-driven sales behaviors (setting prices on items) as with lib-trade above.

---

### Tier 2: Probably Needed (Medium Confidence)

#### 5. lib-trade (L4 Game Features)

**Concern**: Trade route logistics, shipment tracking, border crossings, tariffs, and taxation.

**Why it exists**: Arcadia's economy vision includes NPC-driven trade routes with caravans, border crossings with tariffs and customs, smuggling mechanics, and taxation systems. These create emergent economic geography -- some routes are profitable, some are dangerous, tariffs affect what goods flow where.

**Why L4**: Highly optional. Many games don't need trade route simulation. Depends on lib-currency, lib-item, lib-location (L2).

**Depends on**: lib-currency (L2), lib-item (L2), lib-location (L2), lib-inventory (L2). Optionally: lib-escrow (L4, for securing shipments in transit).

**API concept groups**:
- **Route management** -- a set for defining trade routes (legs, terrain, risk, distance), querying routes between locations
- **Shipment lifecycle** -- a set for creating shipments, departing, completing legs, recording incidents, arriving/lost (declarative -- game drives events, plugin records state)
- **Border/customs** -- a set for border crossing declarations (declared vs actual goods), inspection outcomes, seizure recording
- **Tariff policies** -- a set for defining tariff rates (per-realm, per-border, per-category), calculating tariffs, collecting tariffs
- **Tax policies** -- a set for defining tax types (transaction, income, property, wealth, sales), calculating taxes, assessing taxes, tracking debt
- **Contraband** -- a set for defining contraband rules per realm, checking goods against contraband lists

**Open questions**:
- Should tariffs and taxes be in lib-trade or a separate lib-tax? (Tariffs are inherently about borders/trade; general taxes apply to all economic activity.)
- How much of this is advisory (pure calculation) vs enforcement (automatic deduction)?
- Should trade routes be game-defined or discoverable by NPC exploration?

---

#### 6. lib-economy (L4 Game Features)

**Concern**: Economic intelligence, monitoring, and NPC economic profile management.

**Why it exists**: The vision requires divine economic intervention via god actors that observe velocity metrics and spawn corrective narrative events. NPCs need economic profiles (what they produce/consume/trade) for GOAP planning. Someone needs to compute faucet/sink balance and wealth distribution.

**Why L4**: Optional intelligence layer. Primarily reads from analytics and currency events.

**Why it might NOT be a standalone plugin**: Much of its monitoring functionality could be analytics extensions. NPC economic profiles could live in Character Personality (L4). The "economic deity" behavior is just an Actor with economic GOAP behaviors, not a service.

**Depends on**: lib-currency (L2, events), lib-analytics (L4, event aggregation), lib-actor (L2, for NPC profiles). Optionally: lib-market (L4), lib-trade (L4).

**API concept groups**:
- **Velocity metrics** -- a set for querying money velocity at various scopes (realm, location, currency), identifying hotspots/coldspots
- **Faucet/sink analysis** -- a set for tracking currency sources and sinks over time periods, computing net flow
- **NPC economic profiles** -- a set for managing what NPCs produce, consume, and trade (feeds GOAP economic decisions)
- **Location economic profiles** -- a set for divine attention levels, stagnation policies, revitalization triggers
- **Price guidance** -- a set for computed price recommendations based on supply/demand signals

**Open questions**:
- Should this be a service or should its concerns be distributed across analytics extensions + actor behaviors + character personality?
- Does NPC economic profile data belong here or in Character Personality?
- How much computation should be server-side vs game-server-side?

---

### Tier 3: Maybe Needed (Depends on Design Decisions)

#### 7. lib-equipment (L4 Game Features)

**Concern**: Aggregate stat computation from all equipped items, set bonuses, equipment requirement validation.

**Why it might exist**: Something needs to compute "this character's total stats" from equipped items + affixes + buffs + seed capabilities. If server-authoritative stat computation is required (for 100K NPCs making combat/economic decisions based on stats), a dedicated service could compute and cache aggregate stats.

**Why it might NOT exist**: This could be computed client-side, or by the game server, or by the Actor runtime as part of GOAP world-state population. It might be too game-specific to generalize.

**Open questions**:
- Is server-authoritative stat aggregation needed for 100K NPCs, or can NPCs use approximations?
- Could this be a Variable Provider Factory implementation rather than a full service?
- Does Arcadia's "progressive agency" UX model require the server to compute exactly which stats are visible?

---

#### 8. lib-socket (L4 Game Features)

**Concern**: Socket configurations on items, gem/rune insertion, link management.

**Why it might exist**: The PoE reference doc identifies sockets as a distinct concern from affixes. If Arcadia uses a socketing system (runes, gems, enchantment crystals), this manages the socket grid, color weighting, link mechanics, and gem placement.

**Why it might NOT exist**: Arcadia's metaphysics uses pneuma/logos for enchantment, not physical sockets. Socketing might not fit the world design. If it does exist, it might be very different from PoE's system.

**Open questions**:
- Does Arcadia's enchantment system use sockets, or is it purely affix-based?
- If yes, are sockets physical (gem slots) or metaphysical (pneuma channels)?
- Could socketing be a subset of lib-affix rather than a separate concern?

---

## Foundation Enhancements Needed

Not new plugins, but enhancements to existing L2 services that multiple planned plugins depend on:

### lib-item enhancements
- **#407: Item decay/expiration** -- `expiresAt` field on instances + background decay worker. Blocks lib-status. Would also serve lib-craft (timed crafting sessions), lib-market (listing expiration).
- **Structured metadata conventions** -- While `customStats` remains opaque JSON, conventions for how lib-affix writes to it (affix array structure) should be documented so lib-craft and lib-loot can read affix data without importing lib-affix.

### lib-escrow completion
- **Asset movement** -- Deposit/release/refund must actually move currency/items/contracts via lib-currency/lib-inventory/lib-contract.
- **Contract integration** -- "Contract as brain, escrow as vault" pattern.
- **Periodic validation** -- Background job verifying held assets remain intact.
- This is prerequisite for lib-market (auction settlement) and lib-trade (shipment escrow).

---

## Dependency Graph

```
L4 (all optional, graceful degradation between L4 services):

lib-affix ---------> lib-item (L2)
                     lib-game-service (L2)

lib-craft ---------> lib-item (L2)
                     lib-inventory (L2)
                     lib-contract (L1)
                     lib-currency (L2)
                     lib-affix (L4, soft)

lib-market --------> lib-item (L2)
                     lib-inventory (L2)
                     lib-currency (L2)
                     lib-escrow (L4, soft)
                     lib-affix (L4, soft -- for search filtering)

lib-loot ----------> lib-item (L2)
                     lib-inventory (L2)
                     lib-currency (L2, soft -- for currency drops)
                     lib-affix (L4, soft -- for modifier generation)

lib-trade ---------> lib-item (L2)
                     lib-inventory (L2)
                     lib-currency (L2)
                     lib-location (L2)
                     lib-escrow (L4, soft)

lib-economy -------> lib-currency (L2, events)
                     lib-analytics (L4)
                     lib-market (L4, soft)
                     lib-trade (L4, soft)

lib-status --------> lib-item (L2)
  (already planned)  lib-inventory (L2)
                     lib-contract (L1, soft)
                     lib-seed (L2, soft)
```

All L4-to-L4 dependencies are soft (graceful degradation). All L4-to-L2/L1 dependencies are hard (constructor injection).

---

## Implementation Priority (Suggested)

Based on what blocks what and what the vision needs first:

1. **lib-escrow completion** -- Foundation prerequisite. Unblocks market and trade.
2. **#407 Item decay/expiration** -- Foundation prerequisite. Unblocks status.
3. **lib-status** -- Plan already exists. First consumer of "itemize anything" beyond License/Collection. Needed for divine blessings, combat effects.
4. **lib-affix** -- Core item complexity layer. Needed before loot and crafting can generate interesting items.
5. **lib-loot** -- Drop generation. Needed for combat rewards, chest contents, quest rewards.
6. **lib-craft** -- Production system. Needed for NPC economic participation (blacksmiths, bakers, etc.).
7. **lib-market** -- Exchange system. Needed for NPC-driven economy.
8. **lib-trade** -- Logistics. Needed for inter-realm commerce and trade route gameplay.
9. **lib-economy** -- Intelligence layer. Needed for divine economic intervention.

---

## What This Document Is NOT

- Not API specifications (that's the next pass)
- Not schema definitions
- Not implementation plans (those come after API design)
- Not a commitment to build all of these (some may be deferred or merged)

This is a **landscape survey** identifying the concern boundaries and dependencies so we can make informed decisions about what to build, in what order, and how the pieces fit together.
