# Economy System - Architecture & Design

> **Version**: 1.0
> **Status**: Foundation services production-ready; higher-level services planned
> **Foundation Plugins**: `lib-currency` (L2), `lib-item` (L2), `lib-inventory` (L2), `lib-contract` (L1), `lib-escrow` (L4)
> **Integration Plugins**: `lib-quest` (L2), `lib-actor` (L2), `lib-behavior` (L4), `lib-analytics` (L4), `lib-divine` (L4)
> **Deep Dives**: [Currency](../plugins/CURRENCY.md), [Item](../plugins/ITEM.md), [Inventory](../plugins/INVENTORY.md), [Contract](../plugins/CONTRACT.md), [Escrow](../plugins/ESCROW.md), [Quest](../plugins/QUEST.md), [Analytics](../plugins/ANALYTICS.md), [Divine](../plugins/DIVINE.md)
> **Related Guides**: [Behavior System](./BEHAVIOR-SYSTEM.md), [Seed System](./SEED-SYSTEM.md), [Morality System](./MORALITY-SYSTEM.md)

Economy is not a single plugin but an **architectural layer** spanning multiple services. This guide documents the cross-cutting design: how foundation services compose into a living economy, how NPCs participate as economic actors, how divine entities maintain economic health through narrative intervention, and what planned services will complete the picture.

---

## Table of Contents

1. [Design Philosophy](#1-design-philosophy)
2. [Foundation Services](#2-foundation-services)
3. [Money Velocity & Economic Health](#3-money-velocity--economic-health)
4. [NPC Economic Participation](#4-npc-economic-participation)
5. [Divine Economic Intervention](#5-divine-economic-intervention)
6. [Quest-Economy Integration](#6-quest-economy-integration)
7. [ABML Economic Integration](#7-abml-economic-integration)
8. [Exchange Rate Extensions](#8-exchange-rate-extensions)
9. [Scale Considerations](#9-scale-considerations)
10. [Planned Services](#10-planned-services)
11. [Integration Map](#11-integration-map)
- [Appendix A: Research Sources](#appendix-a-research-sources)

---

## 1. Design Philosophy

### 1.1 Faucet/Sink Discipline

Every healthy game economy is a system of flows:

- **Faucets**: Sources where currency enters (quest rewards, loot drops, vendor sales, treasure discoveries)
- **Sinks**: Drains where currency exits (vendor purchases, auction fees, repair costs, taxes, offerings)

The cardinal rule: **every faucet has a corresponding sink**. Unbalanced faucets cause inflation; unbalanced sinks cause deflation. The economy services track both sides.

| Source Type | Faucet Example | Corresponding Sink |
|-------------|----------------|-------------------|
| Quest rewards | `/currency/credit` with `quest_reward` | Quest acceptance fees, item consumption |
| Vendor buyback | Selling items to NPCs | Buying items from NPCs |
| Loot drops | Monster kill rewards | Repair costs, durability loss |
| Crafting | Selling crafted goods | Material costs, tool wear |
| Trading | Auction sale proceeds | Listing fees, transaction fees |

### 1.2 Redistribution Over Creation

Most economic interventions should **move currency between entities**, not create or destroy it. When a divine actor needs to stimulate a stagnant village, the preferred intervention is a trade opportunity (redistribution) rather than a treasure discovery (faucet). True faucets and sinks are reserved for structural adjustments.

This principle preserves the meaning of currency -- if gold appears from nowhere too often, it stops feeling valuable.

### 1.3 Three-Tier Usage Pattern

The economy APIs serve three consumer patterns through the same endpoints:

```
Tier 1: Divine/System Oversight
  God actors or system processes with full analytics visibility.
  Use: Automated velocity monitoring, narrative interventions.

Tier 2: NPC Governance
  Economist NPCs, customs officials, merchant guild leaders.
  Behaviors encode economic knowledge with imperfect information.
  Use: Query what they can "see", make bounded-rational decisions.

Tier 3: External Management
  Game server handles all decisions outside Bannou.
  Use: "Where's money flowing weakest?" -- just give me data.
```

The plugin provides primitives and data. It does not enforce game-specific rules. Games decide how to use the APIs.

### 1.4 Declarative Lifecycle Pattern

For planned services like trade shipments, the plugin **does not automatically progress state**. The game server calls APIs to indicate what happened. This follows `lib-scene`'s instantiate pattern and `lib-contract`'s milestone pattern: the plugin records state, the game drives events.

---

## 2. Foundation Services

These services provide the primitives that the economy layer builds upon. All are production-ready.

| Service | Layer | Endpoints | Role in Economy |
|---------|-------|-----------|-----------------|
| **lib-currency** | L2 | 32 | Multi-currency wallets, credit/debit/transfer, authorization holds, escrow integration, exchange rates, conversion, autogain |
| **lib-item** | L2 | 16 | Item templates (category, rarity, quantity models) and instances (binding, durability, origin tracking) |
| **lib-inventory** | L2 | 16 | Multi-constraint containers (slot/weight/grid/volumetric/unlimited), nested containers, split/merge/transfer |
| **lib-contract** | L1 | 30 | Template-based agreements, milestone progression, prebound API execution, breach/cure, guardian custody |
| **lib-escrow** | L4 | 22 | Full-custody asset exchange orchestration, 13-state FSM, multi-party consent, trust modes, contract-bound releases |

### 2.1 How They Compose

```
Contract defines RULES         Escrow holds ASSETS
(milestones, obligations,      (currency in escrow wallets,
 what happens on breach)        items in escrow containers)
         │                              │
         └──────────┬──────────────────┘
                    │
              "Contract as Brain,
               Escrow as Vault"
                    │
         ┌──────────┴──────────┐
         │                     │
    Currency moves         Items move
    via /currency/*        via /inventory/*
```

Escrow calls Currency and Inventory directly (L4 depends on L2). Contract-bound escrows delegate distribution to the contract's prebound APIs. See [ESCROW.md](../plugins/ESCROW.md) for the full state machine.

### 2.2 Current Gaps (Tracked)

| Gap | Issue | Impact |
|-----|-------|--------|
| Escrow asset transfer integration | [#153](https://github.com/beyond-immersion/bannou-service/issues/153) | Critical -- escrow release/refund flow needs completion |
| Escrow periodic validation | [#250](https://github.com/beyond-immersion/bannou-service/issues/250) | Held assets not periodically verified against actual state |
| Escrow cross-service validation | [#213](https://github.com/beyond-immersion/bannou-service/issues/213) | ValidateEscrow always passes (no actual checks) |
| Currency global supply analytics | [#211](https://github.com/beyond-immersion/bannou-service/issues/211) | GetGlobalSupply returns zeros |

---

## 3. Money Velocity & Economic Health

### 3.1 What Is Velocity?

**Money velocity** measures how frequently currency changes hands over time:

```
Velocity = Transaction Volume / Average Currency Stock
```

| Velocity | State | Implication |
|----------|-------|-------------|
| < 0.3 | Stagnant | Hoarding, dead economy, no opportunity |
| 0.5 - 2.0 | Healthy | Active trade, balanced supply/demand |
| > 4.0 | Overheated | Speculation, inflation risk, instability |

### 3.2 Tracking Granularity

Velocity should be tracked at multiple scopes:

| Scope | Use Case |
|-------|----------|
| **Per-Currency** | Compare gold vs premium gems activity |
| **Per-Realm** | Overall realm economic health |
| **Per-Location** | Identify dead villages vs thriving cities |
| **Global** | Only meaningful for global currencies |

### 3.3 Analytics Integration

Velocity tracking belongs in lib-analytics, not lib-currency. Currency records transactions; Analytics aggregates patterns.

The Currency service already publishes `currency.credited`, `currency.debited`, and `currency.transferred` events. Analytics would need to:

1. Subscribe to currency transaction events
2. Aggregate by currency/realm/location dimensions
3. Compute velocity over configurable time windows
4. Expose query endpoints for velocity, distribution, and trend analysis

lib-currency already provides `/currency/stats/global-supply` and `/currency/stats/wallet-distribution` endpoints (currently stubbed -- see [#211](https://github.com/beyond-immersion/bannou-service/issues/211)). Analytics extensions would add time-series velocity tracking and location-scoped analysis on top.

### 3.4 Wealth Distribution Metrics

A healthy economy needs visibility into wealth concentration:

| Metric | Purpose |
|--------|---------|
| **Gini Coefficient** | 0 = perfect equality, 1 = one entity has all |
| **Percentiles** (p10, p25, p50, p75, p90, p99) | Where wealth clusters |
| **Average vs Median** | Median detects skew that averages hide |
| **Hoarding Detection** | Flag when any entity holds >N% of realm wealth |

---

## 4. NPC Economic Participation

### 4.1 NPCs as Economic Actors

In Arcadia, NPCs participate in the economy through GOAP-driven decisions, not scripted vendor inventories. A blacksmith NPC:

1. Buys raw materials when stock is low (GOAP: `buy_raw_materials`)
2. Crafts goods when materials are available (GOAP: `craft_goods`)
3. Sells at market based on supply/demand (GOAP: `sell_at_market`)
4. Adjusts prices based on market conditions (GOAP: `adaptive_pricing`)

This creates emergent economic behavior: when iron becomes scarce, blacksmiths raise sword prices. When a new mine opens, prices drop as supply increases.

### 4.2 Economic Roles

NPCs have economic profiles determining their participation:

| Role | Production | Consumption | Behavior |
|------|-----------|-------------|----------|
| **Merchant** | Buys/sells goods | Inventory restocking | Price-aware, risk-tolerant |
| **Craftsman** | Converts materials to goods | Raw materials, tools | Skill-gated, fatigue-limited |
| **Farmer** | Food, raw materials | Seeds, tools | Seasonal, weather-dependent |
| **Consumer** | Nothing | Food, goods, services | Budget-constrained |

### 4.3 GOAP World State for Economic Decisions

Economic GOAP flows integrate with the existing behavior system (see [Behavior System Guide](./BEHAVIOR-SYSTEM.md)):

```yaml
# Economic world state keys accessible to GOAP planner
economic_worldstate:
  gold_reserves: decimal          # NPC's current funds
  has_raw_materials: boolean
  has_finished_goods: boolean
  inventory_space_remaining: integer
  market_price_iron: decimal      # From market analytics
  market_supply_iron: enum        # scarce | normal | abundant
  shop_is_open: boolean
  fatigue: decimal
  supplier_trust: decimal         # From relationship data

# Economic goals
survive:
  priority: 100
  conditions: { hunger: "< 0.9", gold_reserves: "> 10" }

maintain_wealth:
  priority: 70
  conditions: { gold_reserves: ">= ${personality.greed * 500}" }

restock_shop:
  priority: 60
  conditions: { has_finished_goods: "== true" }

# Economic actions
buy_raw_materials:
  goap:
    preconditions: { gold_reserves: "> 100", has_raw_materials: "== false" }
    effects: { gold_reserves: "-${market_price_iron * quantity}", has_raw_materials: true }
    cost: 2

craft_goods:
  goap:
    preconditions: { has_raw_materials: "== true", fatigue: "< 0.8" }
    effects: { has_raw_materials: false, has_finished_goods: true, fatigue: "+0.15" }
    cost: 3

sell_at_market:
  goap:
    preconditions: { has_finished_goods: "== true", market_demand_swords: "!= low" }
    effects: { has_finished_goods: false, gold_reserves: "+${calculate_sale_price()}" }
    cost: 1
```

### 4.4 Scale Strategy

Not all NPCs are economically active simultaneously. Key strategies for handling 100K+ NPCs:

1. **Lazy Wallet Creation**: NPCs don't get wallets until they transact (lib-currency already provides `/currency/wallet/get-or-create`)
2. **Template-Based Defaults**: NPCs derive baseline wealth from templates rather than individual state
3. **Tick-Based Processing**: Economic decisions run periodically (every 5 minutes), not real-time
4. **Regional Aggregation**: NPCs share market intelligence by location via cached regional price data

See [Section 9: Scale Considerations](#9-scale-considerations) for more details.

---

## 5. Divine Economic Intervention

### 5.1 The Invisible Hand

Traditional game economies use direct manipulation (adjusting drop rates, vendor prices) to control health. Bannou takes a different approach: **specialized divine actors observe economic metrics and spawn narrative events** that naturally adjust velocity through NPC reactions.

This approach:
- Preserves NPC autonomy (they respond to events, not forced behavior)
- Creates narrative coherence (every adjustment has a story)
- Allows intentional stagnation (dead towns stay dead until revitalized)
- Enables creative variation (different gods, different intervention styles)

### 5.2 Economic Deities

Economic deities are long-running Actor instances (via lib-actor) that observe analytics and spawn corrective events. They use the Divine service (lib-divine) for identity, divinity economy, and blessing mechanics.

| Deity Archetype | Domain | Intervention Style |
|-----------------|--------|-------------------|
| **Commerce God** | Trade, markets | Business opportunities, traveling merchants, trade festivals |
| **Wealth God** | Prosperity | Treasure discoveries, inheritances, windfalls |
| **Poverty God** | Misfortune | Curses, lost wallets, bad luck streaks |
| **Thief God** | Deception | Robberies, pickpockets, protection rackets |
| **Harvest God** | Abundance | Bumper crops, resource discoveries, fertile seasons |
| **Balance God** | Retribution | Karmic redistribution, targeting extremes |

Key principles:
- Gods are **specialized**, not omnibus (Commerce != Thieves)
- Gods can manage **multiple realms** but not necessarily all realms
- Every realm with an economy has **at least one** economic deity
- Gods have **personalities** that affect intervention style
- Most interventions are **redistribution**, not creation/destruction

### 5.3 Intervention Event Types

#### Redistribution Events (Currency Moves, Not Created/Destroyed)

| Event | Trigger | Effect | Narrative |
|-------|---------|--------|-----------|
| **Dropped Wallet** | Excess currency target | Currency becomes findable | "Someone tripped and their purse scattered" |
| **Pickpocket** | Wealthy target in crowd | Transfer to thief NPC | "A nimble hand relieved them" |
| **Inheritance** | Elderly NPC with savings | Transfer to heir | "Old Mathilda passed, leaving everything to..." |
| **Business Deal** | Stagnant merchant | New trade opportunity | "A buyer from the capital seeks your wares" |
| **Debt Collection** | Target owes NPC | Forced transfer or seizure | "The creditor's patience has run out" |

#### True Faucet Events (Use Sparingly -- Creates Inflation)

| Event | Trigger | Effect | Narrative |
|-------|---------|--------|-----------|
| **Treasure Discovery** | Very stagnant area | Ancient coins found | "The plow struck something metal..." |
| **Royal Grant** | Story event | Official injection | "The crown rewards loyal service" |

#### True Sink Events (Essential for Inflation Control)

| Event | Trigger | Effect | Narrative |
|-------|---------|--------|-----------|
| **Tax Collection** | Periodic/threshold | Currency removed | "The tax collector makes their rounds" |
| **Disaster Repair** | Location event | Currency consumed | "The fire damage must be repaired" |
| **Offering/Tithe** | Religious NPCs | Currency to temple | "The gods require their due" |

### 5.4 God Personality Variations

Different economic deities create different economic "feels":

| Personality | Intervention Pattern | Economic Feel |
|-------------|---------------------|---------------|
| **High Subtlety** | Invisible hand, natural events | "The economy just works" |
| **Low Subtlety** | Obvious miracles, explicit curses | "The gods are watching" |
| **High Chaos** | Frequent, unpredictable | "Anything can happen" |
| **Low Chaos** | Rare, measured corrections | "Stable and predictable" |
| **Favors Wealthy** | Protects hoarders | "The rich get richer" |
| **Favors Poor** | Robin Hood redistribution | "Fortune favors the humble" |

**Multiple Gods Per Realm**: A realm might have both a Commerce God (subtle, trade-focused) and a Thief God (chaotic, theft-focused). Their interventions can conflict, creating interesting dynamics.

### 5.5 Spillover Effects

When currency is injected into a location, spillover is inevitable and desirable. NPCs don't sit on money; they spend according to their GOAP goals:

```
Treasure Found in Riverside Village
    Finder (farmer) has 500 extra gold
    └── GOAP: "I need a new plow" → Buys from blacksmith
        └── Blacksmith has 450 gold
            └── GOAP: "I need iron ore" → Buys from traveling merchant
                └── Merchant has 400 gold
                    └── GOAP: "I'll stock up in the city" → Travels, spends elsewhere

Velocity increase ripples outward from injection point
```

Don't try to contain spillover -- it's the economy working. Track velocity at multiple granularities to see ripple effects.

### 5.6 Intentional Stagnation

Not every location should be economically healthy. Dead towns, abandoned quarters, and frontier regions may be **intentionally stagnant** for narrative purposes.

Locations can have a divine attention level: `active` (gods maintain velocity), `passive` (gods observe rarely), `abandoned` (gods have withdrawn), `cursed` (intentionally suppressed). Only when players or NPCs take significant action (clearing a monster, reopening a mine) does the revitalization trigger fire and divine attention return.

### 5.7 God Actor GOAP Flows

Economic deities use the same GOAP infrastructure as NPC brains, with world state populated from analytics queries:

```yaml
# Economic Deity World State (from analytics)
deity_worldstate:
  velocity:
    market_square: 2.1
    riverside_village: 0.08
    northern_mines: 5.2
  realm_gini: 0.52
  hoarding_detected: true
  subtlety: 0.8
  chaos_affinity: 0.2

# Goals
maintain_healthy_velocity:
  priority: 70
  conditions:
    min_location_velocity: "> 0.3"
    max_location_velocity: "< 4.0"

prevent_hoarding:
  priority: 60
  conditions:
    hoarding_detected: "== false"

# Actions
spawn_business_opportunity:
  goap:
    preconditions:
      target_velocity: "< 0.3"
      target_has_merchants: "== true"
      days_since_intervention: "> ${7 / personality.interventionFrequency}"
    effects:
      target_velocity: "+0.4"
    cost: "${2 * personality.subtlety}"
```

---

## 6. Quest-Economy Integration

### 6.1 Current State (Implemented)

Quest-economy integration is **fully implemented** via lib-quest's contract orchestration layer:

- **Currency rewards**: Prebound API calls to `/currency/credit` with `quest_reward` transaction type, generated from quest reward definitions
- **Item rewards**: Prebound API calls to `/item/instance/create` + `/inventory/add` with quest origin tracking
- **Prerequisites**: Direct L2 client calls -- `ICurrencyClient` for `CURRENCY_AMOUNT` checks, `IInventoryClient`/`IItemClient` for `ITEM_OWNED` checks
- **Reward execution**: Automatically triggered when the quest's contract completes its final milestone

See [QUEST.md](../plugins/QUEST.md) for implementation details.

### 6.2 Quest as Economic Faucet

Quest rewards are a primary faucet. Every quest completion that awards currency injects money into the system. This should be tracked for faucet/sink balance analysis:

```
Quest Completed
    ├── lib-currency /currency/credit (transactionType: quest_reward)
    ├── lib-inventory /inventory/add (originType: quest)
    └── Analytics: record quest as faucet source
```

### 6.3 Planned Economic Objectives

Future quest objective types for economic gameplay:

| Objective Type | Description |
|----------------|-------------|
| `collect_item` | Gather N items of template X |
| `earn_currency` | Earn N currency (total or since accept) |
| `craft_item` | Produce N items via crafting system |
| `trade_with_npc` | Complete a buy/sell transaction with specific NPC |

These would leverage lib-quest's existing `IPrerequisiteProviderFactory` pattern for L4 validation without Quest depending on higher layers.

---

## 7. ABML Economic Integration

### 7.1 Economic Action Handlers

New ABML handlers for economic behaviors. These extend the existing handler registry in `bannou-service/Abml/DocumentExecutorFactory.cs`:

| Handler | Description | Parameters |
|---------|-------------|------------|
| `economy_credit` | Credit currency to a wallet | target, targetType, currency code, amount, reason |
| `economy_debit` | Debit currency from a wallet | target, targetType, currency code, amount, reason |
| `inventory_add` | Add item to entity's inventory | target, targetType, item template code, quantity, origin |
| `inventory_has` | Check if entity has item (condition) | target, item template code, quantity; returns boolean |
| `market_query` | Query market prices for NPC decisions | realm, item codes; returns price/supply map |

Example usage in ABML:

```yaml
flows:
  complete_delivery:
    - economy_credit:
        target: "${character_id}"
        targetType: character
        currency: "gold"
        amount: 500
        reason: "quest_completion"

  check_resources:
    - cond:
        when: "${inventory_has(character_id, 'wolf_pelt', 10)}"
        then:
          - call: complete_objective
```

### 7.2 Variable Providers

Issue [#147](https://github.com/beyond-immersion/bannou-service/issues/147) tracks Phase 2 variable providers for currency, inventory, and relationship data (`${currency.*}`, `${inventory.*}`, `${relationship.*}`). These give ABML read access to economic state. The action handlers above give ABML write access.

---

## 8. Exchange Rate Extensions

### 8.1 Current State

lib-currency provides basic exchange rates:
- `ExchangeRateToBase` field on currency definitions
- Base-currency pivot calculation (`fromRate / toRate`)
- `/currency/exchange-rate/get`, `/currency/exchange-rate/update`
- `/currency/convert/calculate`, `/currency/convert/execute`

### 8.2 Universal Value Concept

Currencies can have a **universal value** -- an intrinsic worth that can change over time. Exchange rates are then computed dynamically from universal values plus modifiers:

```
Effective Rate = (fromCurrency.universalValue / toCurrency.universalValue)
                 * product(active modifiers)
```

Universal values shift in response to game events: a gold discovery lowers gold's universal value, wartime increases weapon-currency values.

### 8.3 Location-Scoped Rates

Exchange rates can vary by location. A frontier outpost might offer worse rates than a capital city's money changer. This enables:

- **Arbitrage opportunities** for merchant NPCs and players
- **Regional economic variation** (poor regions have unfavorable rates)
- **Narrative-driven rate changes** (war zones, festivals, trade embargoes)

Modifiers stack multiplicatively with configurable sources and expiry:

| Modifier Type | Example | Effect |
|---------------|---------|--------|
| Tariff | Border import duty | +10% |
| War | Active conflict between realms | +25% |
| Festival | Trade celebration | -5% |
| Shortage | Local currency scarcity | +15% |

### 8.4 Spread (Buy/Sell Differential)

Currency exchange businesses (NPC money changers) can have configurable buy and sell spreads, representing their profit margin. A money changer with a 5% spread buys gold at 0.95x base rate and sells at 1.05x.

---

## 9. Scale Considerations

### 9.1 Handling 100K+ NPC Economies

Not all NPCs are economically active simultaneously.

**Lazy Wallet Creation**: NPCs don't get wallets until they transact. lib-currency already provides `/currency/wallet/get-or-create` for this pattern.

**Template-Based Defaults**: NPCs derive baseline wealth from species/role templates. Only when an NPC actually transacts does individual state materialize.

**Tick-Based Processing**: Economic decisions run periodically, not in real-time:
```
Every 5 minutes:
  - Process NPC vendor restocking (batched)
  - Run market price discovery
  - Age out stale listings
```

**Regional Aggregation**: NPCs share market intelligence by location. Regional price caches reduce per-NPC queries:
```
market:eldoria:iron_price: 15.5
market:eldoria:iron_supply: abundant
```

### 9.2 Database Partitioning

```
GLOBAL (Replicated)
  currency_definitions    - Currency types (lib-currency)
  item_templates          - Item definitions (lib-item)
  vendor_catalogs         - Base vendor configs (planned)

PER-REALM (Sharded)
  currency_balances       - Realm-specific (lib-currency)
  currency_transactions   - Transaction log (lib-currency)
  item_instances          - Item occurrences (lib-item)
  auction_listings        - Active auctions (planned)
  economy_metrics         - Time-series (planned)
```

### 9.3 Caching Strategy

| Data | Key Pattern | TTL | Update Trigger |
|------|-------------|-----|---------------|
| Currency balance | `curr:bal:{walletId}:{currencyId}` | 60s | lib-currency handles internally |
| Item template | `item:tpl:{templateId}` | 3600s | lib-item handles internally |
| Market price | `market:price:{realmId}:{templateId}` | 60s | auction_sold events (planned) |
| Regional supply | `market:supply:{realmId}:{category}` | 300s | Batch recomputation (planned) |

---

## 10. Planned Services

### 10.1 Market Service (lib-market)

**Purpose**: Auctions, trading, vendors, and price discovery.

**Architecture**:
```
Listing API ──> Bid Engine (Redis Sorted Sets) ──> Settlement
     │                │                              │
     v                v                              v
Search Index     Bid State (Redis)          Transaction Log (MySQL)
```

**Key features**:
- **Auction house**: Player-to-player listings with bid/buyout, duration tiers, listing fees (sink), transaction fees (sink)
- **Vendor system**: NPCs as economic actors with dynamic/personality-driven catalogs, restocking, requirement-gated items, and buyback pricing
- **Price discovery**: Running averages, price history, trend detection
- **Integration**: Item escrow via lib-escrow, payments via lib-currency, authorization holds for bid reservations

**Events**: `market.listing.created`, `market.bid.placed`, `market.auction.sold`, `market.auction.expired`, `market.price.changed`

### 10.2 Trade & Taxation

**Purpose**: Trade routes, shipments, border crossings, tariff policies, taxation, and contraband.

#### Trade Routes

Trade routes are **data constructs** describing paths between locations. The plugin doesn't pathfind -- games define routes and the plugin stores them.

Routes have legs (ordered sequence of location-to-location segments) with per-leg metadata: estimated duration, terrain type, risk level, and border crossing information.

#### Shipments

Shipments track goods/currency moving along routes with a **declarative lifecycle**: the game server calls APIs to indicate what happened at each stage (depart, complete-leg, border-crossing, arrive, lost).

States: `preparing` -> `in_transit` -> `at_checkpoint` -> `arrived` (or `lost`, `seized`).

Border crossing is the richest endpoint: tracking declared vs actual goods, smuggling detection, tariff collection modes (auto, manual, evaded, exempt), and seizure records.

#### Tariffs

Tariff policies are realm-scoped with configurable scope (realm-wide, specific border, category), direction (import/export/both), and rates (default, per-category, per-item, currency-specific). Support exemptions for specific entities.

**Collection modes**: `auto` (deducted automatically), `manual` (NPC tax collectors), `advisory` (calculation only, game handles collection).

#### Taxation

Six tax types: transaction, import/export, property, income, wealth, and sales. Each with:
- Base rate and optional progressive brackets
- Exemptions per entity
- Collection mode (automatic/assessed/advisory)
- Collection target (void sink, realm treasury, or faction)
- For assessed taxes: assessment frequency, grace period, late penalty rate

Tax debt tracking with delinquent entity queries enables NPC tax collectors and narrative consequences for tax evasion.

#### Contraband

Items and currencies can be declared contraband per realm with severity levels (restricted, prohibited, capital offense). The system explicitly supports smuggling -- tracking declared vs actual goods at border crossings. Detection is game logic; the plugin records outcomes for analytics.

### 10.3 Economy Monitoring & NPC Economic AI

**Purpose**: The intelligence layer that monitors economic health and provides NPC economic decision-making data. These capabilities extend existing services (primarily lib-analytics) rather than forming a standalone service.

**Key features**:
- **Faucet/sink monitoring**: Time-series metrics per realm/currency tracking all inflows and outflows by type (via lib-analytics extensions, see [#429](https://github.com/beyond-immersion/bannou-service/issues/429))
- **NPC economic profiles**: Per-character economic role, production/consumption rates, trading personality (owned by whichever service manages NPC economic state -- potentially lib-market's vendor system or a future lib-craft)
- **Price analytics**: Average prices, price history, trend detection per item per realm (owned by lib-market)
- **Wealth distribution**: Gini coefficient, percentile calculations, hoarding detection (via lib-analytics extensions)

---

## 11. Integration Map

```
                    Divine Economic Deities (L4)
                    observe velocity → spawn narrative events
                              │
                    ┌─────────┴─────────┐
                    │                   │
              lib-analytics         lib-actor
              /economy/velocity     Event dispatch
              /economy/distrib      NPC GOAP brains
                    ▲                   │
                    │ records            │ affects
              lib-currency          NPC Economic
              32 endpoints          Decisions
              transactions    ◄──── Natural behavior
                    ▲
                    │ rewards
              lib-quest
              Prebound APIs
              via lib-contract
                    ▲
                    │ item custody
              lib-item + lib-inventory
              16 + 16 endpoints
                    ▲
                    │ escrow custody
              lib-escrow
              22 endpoints
              13-state FSM
```

**Data Flow Summary**:
1. Players and NPCs transact via lib-currency, lib-item, lib-inventory
2. Quests distribute rewards via lib-contract prebound APIs
3. Escrow holds assets during multi-party exchanges
4. Currency transactions publish events consumed by Analytics
5. Analytics computes velocity and distribution metrics
6. Economic deities observe analytics and spawn intervention events
7. NPC GOAP brains react to events, creating organic economic activity
8. The cycle continues, with divine intervention maintaining healthy velocity

---

## Appendix A: Research Sources

### Virtual Economies
- EVE Online economic model (University of Wisconsin research)
- WoW inflation history (Engadget, SimpleBoost)
- Second Life Linden Dollar (Philip Rosedale)
- Faucet/Sink model (1kxnetwork, Lost Garden)

### Item Systems
- Template/Instance pattern (industry standard)
- MMO database design (Vertabelo, GameDev.net)
- Auction house architecture (Hello Interview)

### Agent-Based Economics
- ACE foundations (Leigh Tesfatsion)
- BazaarBot engine (Doran & Parberry)
- LLM-enabled economic agents (arXiv 2506.04699)
- GOAP + Utility AI (Game AI Pro)

---

*For implementation details of individual services, see their deep dive documents. For the behavior system that powers NPC economic decisions, see [BEHAVIOR-SYSTEM.md](./BEHAVIOR-SYSTEM.md). For the divine service that powers economic deities, see [DIVINE.md](../plugins/DIVINE.md).*
