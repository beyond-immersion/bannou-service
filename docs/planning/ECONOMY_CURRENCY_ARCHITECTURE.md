# Economy Architecture Design: Market, Trade, and NPC Economic Systems

> **Created**: 2026-01-19
> **Last Updated**: 2026-01-24
> **Purpose**: Architecture for Bannou's higher-level economic systems (market, trade, NPC economy)
> **Scope**: Markets, NPC Economic Participation, Quest Integration, Trade Routes, Taxation
> **Dependencies**: lib-currency, lib-item, lib-inventory, lib-contract, lib-escrow, lib-actor, lib-analytics, lib-behavior

---

## Executive Summary

Economy is not a single plugin but an **architectural layer** spanning multiple services.

### Foundation Layer (COMPLETE)

These services are fully implemented and production-ready:

| Service | Endpoints | Key Features |
|---------|-----------|--------------|
| **lib-currency** | 32 | Multi-currency wallets, credit/debit/transfer, authorization holds, escrow integration, exchange rates, conversion, batch operations, transaction history, global supply stats, wallet distribution analytics |
| **lib-item** | 13 | Item templates (category, rarity, quantity models, grid dims, weight, soulbinding, durability), item instances (binding, custom stats, origin tracking), batch operations |
| **lib-inventory** | 16 | Multi-constraint containers (slot/weight/grid/volumetric/unlimited), nested containers with weight propagation, equipment slots, split/merge/transfer, distributed locking |
| **lib-contract** | 30 | Template-based agreements, milestone progression, clause execution, breach/cure, guardian custody, consent flows |
| **lib-escrow** | 20 | Multi-party asset exchange, deposit/release/refund, conditions, consent tracking, arbiter resolution, custom asset handlers |

### Planned Layer (THIS DOCUMENT)

| Service | Responsibility | Priority |
|---------|---------------|----------|
| **lib-market** | Auctions, trading, vendors, price discovery | Next |
| **lib-craft** | Recipes, production, skill gating | Parallel with market |
| **lib-economy** | Orchestration, NPC participation, monitoring | After market |

**Core Architectural Decisions** (for planned services):
1. **Event-sourced transactions** for audit and rollback capability
2. **GOAP integration** for NPC economic decision-making
3. **Faucet/Sink discipline** - every currency source has corresponding removal
4. **Divine economic intervention** - Specialized gods manipulate velocity via narrative events
5. **Three-tier usage** - Divine oversight / NPC governance / External management
6. **Declarative shipment lifecycle** - Game drives events, plugin records state

---

## Part 1: Market Service (`lib-market`)

### 1.1 Design Philosophy

Markets handle **exchange** - converting items to currency and vice versa. This includes:
- Auction houses (player-to-player trading via listings)
- Vendor systems (NPC-to-player fixed-price trading)
- Trade posts (regional markets with shipping)

### 1.2 Auction House Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Listing API    │────▶│   Bid Engine    │────▶│  Settlement     │
│  (POST-only)    │     │ (Redis Sorted)  │     │   Service       │
└─────────────────┘     └─────────────────┘     └─────────────────┘
         │                      │                       │
         ▼                      ▼                       ▼
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│  Search Index   │     │   Bid State     │     │  Transaction    │
│  (Redis Search) │     │   (Redis)       │     │  Log (MySQL)    │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

**Integration with existing services:**
- Item escrow: Use `lib-escrow` for safe item/currency holding during auctions
- Payments: Use `lib-currency` credit/debit/transfer for all financial operations
- Authorization holds: Use `lib-currency` hold/capture/release for bid reservations

### 1.3 Listing Schema

```yaml
# Schema: auction-listing
AuctionListing:
  id: uuid

  # Seller
  sellerId: uuid
  sellerType: enum          # character | npc | guild

  # Realm/Market
  realmId: uuid
  marketId: uuid            # For multiple markets per realm

  # Item (escrowed)
  itemInstanceId: uuid      # Item is moved to escrow on listing
  itemTemplateId: uuid      # Denormalized for search
  quantity: integer

  # Pricing
  currencyDefinitionId: uuid
  startPrice: decimal
  buyoutPrice: decimal      # null for no buyout
  currentBid: decimal       # null if no bids
  currentBidderId: uuid
  bidCount: integer

  # Timing
  duration: enum            # short (12h), medium (24h), long (48h)
  listedAt: timestamp
  expiresAt: timestamp

  # Status
  status: enum              # active | sold | expired | cancelled

  # Fees
  listingFee: decimal       # Paid upfront (sink)
  transactionFee: decimal   # Percentage on sale (sink)

# Schema: auction-bid
AuctionBid:
  id: uuid
  listingId: uuid
  bidderId: uuid
  bidderType: enum
  amount: decimal
  timestamp: timestamp
  status: enum              # active | outbid | won | refunded
```

### 1.4 Vendor System

NPCs as economic actors (not just UI):

```yaml
# Schema: vendor-catalog
VendorCatalog:
  id: uuid
  npcId: uuid               # Links to Character service
  realmId: uuid

  # Catalog behavior
  catalogType: enum         # static | dynamic | personality_driven
  restockInterval: duration # null for unlimited stock

# Schema: vendor-item
VendorItem:
  catalogId: uuid
  templateId: uuid

  # Pricing (can be multi-currency)
  prices: [
    { currencyId: uuid, amount: decimal }
  ]

  # Stock
  currentStock: integer     # null for unlimited
  maxStock: integer
  lastRestocked: timestamp

  # Requirements
  requirements: object      # { reputation: "friendly", level: 10 }

# Schema: vendor-buyback
VendorBuyback:
  catalogId: uuid
  templateId: uuid
  buybackPrice: decimal     # What vendor pays for this item
  buybackMultiplier: decimal # e.g., 0.25 = 25% of base value
```

### 1.5 API Endpoints

```yaml
# Auction House
/market/auction/list:
  access: user
  request:
    realmId: uuid
    marketId?: uuid
    category?: enum
    search?: string
    minPrice?: decimal
    maxPrice?: decimal
    sortBy: enum            # price | time_remaining | bid_count
  response: { listings[] }

/market/auction/create:
  access: user
  request:
    itemInstanceId: uuid
    currencyDefinitionId: uuid
    startPrice: decimal
    buyoutPrice?: decimal
    duration: enum
  response: { listing }
  errors:
    - ITEM_NOT_TRADEABLE
    - INSUFFICIENT_FUNDS_FOR_FEE

/market/auction/bid:
  access: user
  request:
    listingId: uuid
    amount: decimal
  response: { bid, listing }
  errors:
    - BID_TOO_LOW
    - AUCTION_ENDED
    - INSUFFICIENT_FUNDS

/market/auction/buyout:
  access: user
  request:
    listingId: uuid
  response: { transaction }
  errors:
    - NO_BUYOUT_AVAILABLE
    - INSUFFICIENT_FUNDS

/market/auction/cancel:
  access: user
  request:
    listingId: uuid
  response: { listing, itemReturned: boolean }
  errors:
    - HAS_BIDS              # Cannot cancel with active bids

# Vendor
/market/vendor/catalog:
  access: user
  request:
    npcId: uuid
  response: { catalog, items[] }

/market/vendor/buy:
  access: user
  request:
    npcId: uuid
    templateId: uuid
    quantity: integer
    paymentCurrencyId: uuid
  response: { transaction, itemInstance }
  errors:
    - OUT_OF_STOCK
    - INSUFFICIENT_FUNDS
    - REQUIREMENTS_NOT_MET

/market/vendor/sell:
  access: user
  request:
    npcId: uuid
    itemInstanceId: uuid
    quantity: integer
  response: { transaction, currencyReceived }
  errors:
    - VENDOR_DOES_NOT_BUY
```

### 1.6 Events

```yaml
market.listing.created:
  listingId: uuid
  sellerId: uuid
  templateId: uuid
  startPrice: decimal

market.bid.placed:
  listingId: uuid
  bidderId: uuid
  amount: decimal
  previousBidderId?: uuid

market.auction.sold:
  listingId: uuid
  sellerId: uuid
  buyerId: uuid
  finalPrice: decimal
  feeAmount: decimal        # Sink tracking

market.auction.expired:
  listingId: uuid
  sellerId: uuid

market.price.changed:
  templateId: uuid
  realmId: uuid
  averagePrice: decimal
  direction: up | down | stable
```

---

## Part 2: Economy Orchestration (`lib-economy`)

### 2.1 Design Philosophy

The economy service is the **intelligence layer** that:
- Monitors faucet/sink balance
- Provides NPC economic AI integration
- Offers analytics and balancing tools
- Coordinates cross-service economic events

### 2.2 Faucet/Sink Monitoring

Every healthy economy tracks currency flow:

```yaml
# Schema: economy-metrics (time-series)
EconomyMetrics:
  realmId: uuid
  currencyDefinitionId: uuid
  periodStart: timestamp
  periodEnd: timestamp

  # Faucets
  totalFaucetAmount: decimal
  faucetsByType:
    quest_reward: decimal
    loot_drop: decimal
    vendor_sale: decimal
    # ...

  # Sinks
  totalSinkAmount: decimal
  sinksByType:
    vendor_purchase: decimal
    auction_fee: decimal
    repair_cost: decimal
    # ...

  # Net flow
  netFlow: decimal          # Faucets - Sinks

  # Stock
  totalCurrencyInCirculation: decimal
  averagePlayerWealth: decimal
  medianPlayerWealth: decimal
  wealthGiniCoefficient: decimal
```

### 2.3 NPC Economic Participation

NPCs participate in economy via GOAP-driven decisions:

```yaml
# Schema: npc-economic-profile
NpcEconomicProfile:
  characterId: uuid         # Links to Character service

  # Economic role
  economicRole: enum        # merchant | craftsman | farmer | consumer | none

  # Production (what they make)
  produces: [
    { templateId: uuid, rate: decimal, skillLevel: decimal }
  ]

  # Consumption (what they need)
  consumes: [
    { templateId: uuid, rate: decimal, priority: integer }
  ]

  # Trading behavior
  tradingPersonality:
    riskTolerance: decimal  # 0-1, affects speculation
    priceAwareness: decimal # How closely they track market
    loyaltyFactor: decimal  # Preference for repeat partners
```

### 2.4 GOAP Integration for Economic NPCs

Economic actions as GOAP flows (integrates with lib-behavior):

```yaml
# Economic World State keys for GOAP
economic_worldstate_schema:
  # Resources
  gold_reserves: decimal
  has_raw_materials: boolean
  has_finished_goods: boolean
  inventory_space_remaining: integer

  # Market knowledge
  market_price_iron: decimal
  market_supply_iron: enum    # scarce | normal | abundant
  market_demand_swords: enum

  # NPC state
  shop_is_open: boolean
  fatigue: decimal
  hunger: decimal

  # Relationships
  supplier_trust: decimal
  customer_loyalty: decimal

# Economic GOAP Goals
economic_goals:
  survive:
    priority: 100
    conditions:
      hunger: "< 0.9"
      gold_reserves: "> 10"   # Enough for food

  maintain_wealth:
    priority: 70
    conditions:
      gold_reserves: ">= ${personality.greed * 500}"

  grow_business:
    priority: 50
    conditions:
      gold_reserves: ">= ${previous_gold * 1.1}"

  restock_shop:
    priority: 60
    conditions:
      has_finished_goods: "== true"

# Economic GOAP Actions
economic_flows:
  buy_raw_materials:
    goap:
      preconditions:
        gold_reserves: "> 100"
        has_raw_materials: "== false"
        market_supply_iron: "!= scarce"
      effects:
        gold_reserves: "-${market_price_iron * quantity}"
        has_raw_materials: true
      cost: 2

  craft_goods:
    goap:
      preconditions:
        has_raw_materials: "== true"
        fatigue: "< 0.8"
      effects:
        has_raw_materials: false
        has_finished_goods: true
        fatigue: "+0.15"
      cost: 3

  sell_at_market:
    goap:
      preconditions:
        has_finished_goods: "== true"
        market_demand_swords: "!= low"
      effects:
        has_finished_goods: false
        gold_reserves: "+${calculate_sale_price()}"
      cost: 1

  adaptive_pricing:
    goap:
      preconditions:
        has_finished_goods: "== true"
      effects:
        listed_price: "${adjust_price_for_market()}"
      cost: 1
```

### 2.5 API Endpoints

```yaml
# Metrics
/economy/metrics/summary:
  access: admin
  request:
    realmId: uuid
    currencyDefinitionId?: uuid
    period: enum            # hour | day | week | month
  response: { metrics }

/economy/metrics/faucet-sink-balance:
  access: admin
  request:
    realmId: uuid
    period: enum
  response: { faucets, sinks, netFlow, trend }

# NPC Economics
/economy/npc/profile/get:
  access: admin
  request:
    characterId: uuid
  response: { profile }

/economy/npc/profile/set:
  access: developer
  request:
    characterId: uuid
    economicRole: enum
    produces: array
    consumes: array
  response: { profile }

/economy/npc/market-analysis:
  access: authenticated
  request:
    characterId: uuid       # NPC performing analysis
  response: { prices, supply, demand, recommendations }

# Price Queries
/economy/price/average:
  access: user
  request:
    templateId: uuid
    realmId: uuid
    period: enum
  response: { average, min, max, trend }

/economy/price/history:
  access: user
  request:
    templateId: uuid
    realmId: uuid
    granularity: enum       # hour | day | week
    limit: integer
  response: { dataPoints[] }
```

---

## Part 3: Quest Integration

### 3.1 Quest → Economy Dependencies

The Quest service requires integration with existing foundation services:

```yaml
# Quest Reward Types
QuestReward:
  type: enum

  # Currency rewards → lib-currency /currency/credit
  currency_reward:
    currencyDefinitionId: uuid
    amount: decimal

  # Item rewards → lib-item /item/instance/create + lib-inventory /inventory/add
  item_reward:
    templateId: uuid
    quantity: integer
    selectionGroup: integer   # For "choose one" rewards

# Quest Prerequisites
QuestPrerequisite:
  # Economic prerequisites
  currency_minimum:
    currencyDefinitionId: uuid
    amount: decimal

  item_possession:
    templateId: uuid
    quantity: integer
    consumed: boolean         # Removed on quest accept?

# Quest Objectives
QuestObjective:
  type: enum

  collect_item:
    templateId: uuid
    quantity: integer

  earn_currency:
    currencyDefinitionId: uuid
    amount: decimal
    trackingType: enum        # total | since_accept

  craft_item:
    templateId: uuid
    quantity: integer

  trade_with_npc:
    npcId: uuid
    action: buy | sell
    templateId?: uuid
    currencyAmount?: decimal
```

### 3.2 Quest Completion Flow

```
Quest.Completed Event
    │
    ├──▶ lib-currency
    │      └── /currency/credit
    │          - transactionType: quest_reward
    │          - referenceId: questId
    │
    ├──▶ lib-inventory
    │      └── /inventory/add
    │          - originType: quest
    │          - originId: questId
    │
    └──▶ lib-economy (analytics)
           └── Record quest as faucet source
```

---

## Part 4: Scale Considerations

### 4.1 Handling 100K+ NPCs

**Key Insight**: Not all NPCs are economically active simultaneously.

**Strategies**:

1. **Lazy Wallet Creation**: NPCs don't get wallets until they transact
   - lib-currency already provides `/currency/wallet/get-or-create` for this pattern

2. **Template-Based Defaults**: NPCs derive baseline wealth from templates
   ```yaml
   NpcTemplate:
     economicProfile:
       baselineGold: 100-500
       restockBudget: 50-200
   ```

3. **Tick-Based Processing**: Economic decisions run periodically, not real-time
   ```
   Every 5 minutes:
     - Process NPC vendor restocking (batched)
     - Run market price discovery
     - Age out stale listings
   ```

4. **Regional Aggregation**: NPCs share market intelligence by location
   ```yaml
   # Regional market cache
   market:eldoria:iron_price: 15.5
   market:eldoria:iron_supply: abundant
   ```

### 4.2 Database Partitioning

```
┌─────────────────────────────────────────────────────────────┐
│                    GLOBAL (Replicated)                       │
├─────────────────────────────────────────────────────────────┤
│  currency_definitions   - Currency types (lib-currency)     │
│  item_templates         - Item definitions (lib-item)       │
│  vendor_catalogs        - Base vendor configs (lib-market)  │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                   PER-REALM (Sharded)                        │
├─────────────────────────────────────────────────────────────┤
│  currency_balances      - Realm-specific (lib-currency)     │
│  currency_transactions  - Transaction log (lib-currency)    │
│  item_instances         - Item occurrences (lib-item)       │
│  auction_listings       - Active auctions (lib-market)      │
│  economy_metrics        - Time-series (lib-economy)         │
└─────────────────────────────────────────────────────────────┘
```

### 4.3 Caching Strategy

```yaml
# Already implemented in foundation services:
currency_balance:
  key: "curr:bal:{walletId}:{currencyId}"
  # lib-currency handles caching internally

item_template:
  key: "item:tpl:{templateId}"
  ttl: 3600  # 1 hour (lib-item handles internally)

# Planned for lib-market:
market_price:
  key: "market:price:{realmId}:{templateId}"
  ttl: 60   # 1 minute (prices change frequently)
  update_on: auction_sold
```

---

## Part 5: ABML Action Handlers

New handlers for economic behaviors (integrates with lib-behavior ABML system):

```yaml
# Handler: economy_credit
economy_credit:
  description: "Credit currency to a wallet"
  parameters:
    target: expression      # Entity ID
    targetType: enum        # character | npc | guild
    currency: string        # Currency code
    amount: expression
    reason: string
  example:
    - economy_credit:
        target: "${character_id}"
        targetType: character
        currency: "gold"
        amount: 500
        reason: "quest_completion"

# Handler: economy_debit
economy_debit:
  description: "Debit currency from a wallet"
  parameters:
    target: expression
    targetType: enum
    currency: string
    amount: expression
    reason: string
  errors:
    - INSUFFICIENT_FUNDS

# Handler: inventory_add
inventory_add:
  description: "Add item to entity's inventory"
  parameters:
    target: expression
    targetType: enum
    item: string            # Template code
    quantity: expression
    origin: string
  example:
    - inventory_add:
        target: "${character_id}"
        targetType: character
        item: "iron_sword"
        quantity: 1
        origin: "quest_reward"

# Handler: inventory_has
inventory_has:
  description: "Check if entity has item (condition helper)"
  parameters:
    target: expression
    item: string
    quantity: expression
  returns: boolean
  example:
    - cond:
        - when: "${inventory_has(character_id, 'wolf_pelt', 10)}"
          then:
            - call: complete_objective

# Handler: market_query
market_query:
  description: "Query market prices for NPC decision-making"
  parameters:
    realm: expression
    items: [string]
  returns: object           # { iron: { price, supply }, ... }
```

---

## Part 6: Money Velocity and Divine Economic Intervention

### 6.1 Design Philosophy

Traditional game economies use direct manipulation (adjusting drop rates, vendor prices) to control economic health. Bannou takes a different approach: **specialized divine actors observe economic metrics and spawn narrative events** that naturally adjust velocity through NPC reactions.

This approach:
- Preserves NPC autonomy (they respond to events, not forced behavior)
- Creates narrative coherence (every adjustment has a story)
- Allows intentional stagnation (dead towns stay dead until revitalized)
- Enables creative variation (different gods, different intervention styles)

### 6.2 Money Velocity

**Definition**: How frequently currency changes hands over time.

```
Velocity = Transaction Volume / Average Currency Stock
```

| Velocity | State | Implication |
|----------|-------|-------------|
| < 0.3 | Stagnant | Hoarding, dead economy, no opportunity |
| 0.5 - 2.0 | Healthy | Active trade, balanced supply/demand |
| > 4.0 | Overheated | Speculation, inflation risk, instability |

**Tracking Granularity**:

| Scope | Use Case |
|-------|----------|
| **Per-Currency** | Compare gold vs. premium gems activity |
| **Per-Realm** | Overall realm economic health |
| **Per-Location** | Identify dead villages vs. thriving cities |
| **Global** | Only meaningful for global currencies |

### 6.3 Analytics Integration for Velocity

Velocity tracking belongs in `lib-analytics`, not `lib-currency`. The currency service records transactions; analytics aggregates patterns.

```yaml
# Economic velocity event (ingested to analytics)
EconomicVelocityEvent:
  eventType: "economy.transaction"
  timestamp: datetime

  # Dimensions for aggregation
  dimensions:
    currencyId: uuid
    realmId: uuid
    locationId: uuid          # City, district, region
    transactionType: string   # trade, vendor, quest, gift, theft

  # Metrics
  metrics:
    amount: decimal
    participantCount: integer # 1 for faucet/sink, 2 for transfers

# Analytics query endpoint
/analytics/economy/velocity:
  access: admin
  request:
    currencyId: uuid
    scope: realm | location | global
    scopeId: uuid
    period: hour | day | week
    granularity: hour | day
  response:
    velocity: decimal
    transactionVolume: decimal
    averageStock: decimal
    trend: accelerating | stable | decelerating
    hotspots: [
      { locationId, velocity, deviation }
    ]
    coldspots: [
      { locationId, velocity, daysSinceActivity }
    ]

# Wealth distribution metrics
/analytics/economy/distribution:
  access: admin
  request:
    currencyId: uuid
    realmId: uuid
  response:
    totalStock: decimal
    averageWealth: decimal
    medianWealth: decimal
    giniCoefficient: decimal  # 0 = perfect equality, 1 = one entity has all
    wealthPercentiles: object # { p10, p25, p50, p75, p90, p99 }
```

**Note**: lib-currency already provides `/currency/stats/global-supply` and `/currency/stats/wallet-distribution` endpoints. The analytics extensions above would add time-series velocity tracking and location-scoped analysis.

### 6.4 Economic Deities (God Actors)

Economic balance is maintained by **specialized divine actors** - long-running Actor instances (via lib-actor) that observe analytics and spawn corrective events.

**Key Principles**:
- Gods are **specialized**, not omnibus (God of Commerce ≠ God of Thieves)
- Gods can manage **multiple realms** but not necessarily all realms
- Every realm with an economy has **at least one** economic deity
- Gods have **personalities** that affect intervention style
- Most interventions are **redistribution**, not creation/destruction

#### Example Economic Deities

| Deity | Domain | Intervention Style | Personality |
|-------|--------|-------------------|-------------|
| **Mercurius** | Commerce, Trade | Business opportunities, traveling merchants, trade festivals | Balanced, invisible hand |
| **Plutus** | Wealth, Prosperity | Treasure discoveries, inheritances, windfalls | Generous, favors the ambitious |
| **Binbougami** | Poverty, Misfortune | Curses, lost wallets, bad luck streaks | Mischievous, explicit curses |
| **Laverna** | Thieves, Deception | Robberies, pickpockets, protection rackets | Chaotic, targets the wealthy |
| **Ceres** | Harvest, Abundance | Bumper crops, resource discoveries, fertile seasons | Cyclical, tied to seasons |
| **Nemesis** | Balance, Retribution | Karmic redistribution, "what goes around" | Corrective, targets extremes |

#### Deity Realm Assignment

```yaml
# Schema: economic-deity-assignment
EconomicDeityAssignment:
  deityActorId: uuid          # The god's Actor instance (lib-actor)
  deityType: string           # "mercurius", "binbougami", etc.
  realmsManaged: [uuid]       # Which realms this god watches

  # Personality parameters (affect intervention style)
  personality:
    interventionFrequency: decimal  # How often to act (0.1 = rarely, 1.0 = constantly)
    subtlety: decimal               # 0 = obvious miracles, 1 = invisible hand
    favoredTargets: enum            # wealthy | poor | merchants | anyone
    chaosAffinity: decimal          # 0 = orderly, 1 = loves disruption
```

### 6.5 Intervention Event Types

Most divine interventions are **naturalistic redistribution** - currency/items change hands through believable events, not magical creation/destruction.

#### Redistribution Events (Currency Moves, Not Created/Destroyed)

| Event | Trigger | Effect | Narrative |
|-------|---------|--------|-----------|
| **Dropped Wallet** | Target has excess currency | Currency becomes findable item | "Someone tripped and their coin purse scattered" |
| **Pickpocket** | Wealthy target in crowd | Currency transfers to thief NPC | "A nimble hand relieved them of their burden" |
| **Inheritance** | Elderly NPC with savings | Wealth transfers to heir | "Old Mathilda passed, leaving everything to..." |
| **Gambling Loss** | Target with gambling personality | Currency to "house" or other NPCs | "The dice were not kind last night" |
| **Business Deal** | Stagnant merchant | New trade opportunity spawns | "A buyer from the capital seeks your wares" |
| **Debt Collection** | Target owes NPC | Forced transfer or item seizure | "The creditor's patience has run out" |

#### True Faucet Events (Currency Enters System)

Use sparingly - these create inflation pressure:

| Event | Trigger | Effect | Narrative |
|-------|---------|--------|-----------|
| **Treasure Discovery** | Very stagnant area | Ancient coins found | "The plow struck something metal..." |
| **Royal Grant** | Story event | Official currency injection | "The crown rewards loyal service" |
| **Bounty Claimed** | Dangerous creature killed | Official reward | "The monster's head fetches its price" |

#### True Sink Events (Currency Exits System)

Essential for inflation control:

| Event | Trigger | Effect | Narrative |
|-------|---------|--------|-----------|
| **Tax Collection** | Periodic or wealth threshold | Currency removed to "crown" | "The tax collector makes their rounds" |
| **Disaster Repair** | Location event | Currency consumed for rebuilding | "The fire damage must be repaired" |
| **Festival Expense** | Community event | Collective spending on consumables | "The harvest festival demands preparations" |
| **Offering/Tithe** | Religious NPCs | Currency to temple (sink) | "The gods require their due" |
| **Lost Forever** | NPC death without heir | Unclaimed wealth vanishes | "Their hidden stash was never found" |

### 6.6 God Actor GOAP Implementation

```yaml
# Economic Deity World State
# Populated from lib-analytics queries
deity_worldstate:
  # Velocity by location (from analytics)
  velocity:
    market_square: 2.1
    riverside_village: 0.08
    northern_mines: 5.2
    abandoned_quarter: 0.0

  # Wealth distribution
  realm_gini: 0.52
  hoarding_detected: true     # Someone has >10% of realm wealth

  # Intervention tracking
  recent_interventions:
    riverside_village: 3      # Days since last intervention
    northern_mines: 12

  # Deity personality (from assignment)
  subtlety: 0.8               # Prefers invisible hand
  chaos_affinity: 0.2         # Orderly

# Economic Deity Goals
goals:
  maintain_healthy_velocity:
    priority: 70
    conditions:
      min_location_velocity: "> 0.3"
      max_location_velocity: "< 4.0"

  prevent_hoarding:
    priority: 60
    conditions:
      hoarding_detected: "== false"

  allow_natural_death:
    # Some places SHOULD be stagnant
    priority: 50
    conditions:
      abandoned_locations_velocity: "< 0.1"  # Intentionally low

# Intervention Flows
flows:
  spawn_business_opportunity:
    description: "Create trade opportunity in stagnant area"
    goap:
      preconditions:
        target_velocity: "< 0.3"
        target_has_merchants: "== true"
        days_since_intervention: "> ${7 / personality.interventionFrequency}"
      effects:
        target_velocity: "+0.4"  # Expected, not guaranteed
      cost: "${2 * personality.subtlety}"  # Subtle gods prefer this

    execution:
      - select_merchant:
          location: "${target_location}"
          criteria: "struggling_but_capable"
      - spawn_event:
          type: "business_opportunity"
          params:
            merchant: "${selected_merchant}"
            opportunity_type: "traveling_buyer"
            potential_profit: "${calculate_meaningful_amount()}"
      - record_intervention:
          location: "${target_location}"
          type: "business_opportunity"

  spawn_dropped_wallet:
    description: "Cause wealthy entity to drop currency"
    goap:
      preconditions:
        target_velocity: "< 0.5"
        wealthy_entities_in_area: "> 0"
        days_since_intervention: "> 3"
      effects:
        target_velocity: "+0.2"
      cost: "${3 * personality.subtlety}"

    execution:
      - select_wealthy_target:
          location: "${target_location}"
          minimum_wealth_percentile: 70
      - calculate_drop_amount:
          # Meaningful but not devastating
          range: "5-15% of target wealth"
      - spawn_event:
          type: "dropped_wallet"
          params:
            dropper: "${selected_target}"
            amount: "${drop_amount}"
            findable_by: "anyone_in_location"
            discovery_window: "1-24 hours"

  spawn_theft:
    description: "Orchestrate robbery of wealthy target"
    goap:
      preconditions:
        target_velocity: "> 4.0"  # Overheated
        OR:
          hoarding_detected: "== true"
        wealthy_targets_exist: "== true"
      effects:
        target_velocity: "-0.8"
      cost: "${5 - (personality.chaos_affinity * 3)}"  # Chaotic gods love this

    execution:
      - select_target:
          criteria: "wealthiest_in_area OR largest_recent_gains"
      - select_or_spawn_thief:
          # Use existing thief NPC or spawn traveling bandit
      - spawn_event:
          type: "robbery"
          params:
            target: "${selected_target}"
            perpetrator: "${thief}"
            severity: "${calculate_extraction()}"
            chance_of_recovery: 0.3  # Target might get it back

  apply_explicit_curse:
    description: "Binbougami-style direct divine intervention"
    goap:
      preconditions:
        personality.subtlety: "< 0.3"  # Only unsubtle gods
        target_deserves_curse: "== true"  # Hoarding, cheating, etc.
      effects:
        target_wealth: "-significant"
      cost: 1  # Cheap for gods who do this

    execution:
      - mark_target_cursed:
          curse_type: "binbougami"
          duration: "until_lesson_learned OR divine_intervention"
          effects:
            - "transactions_fail_randomly"
            - "items_break_more_often"
            - "finds_less_loot"
      - announce_curse:
          visibility: "target_and_witnesses"
          message: "The god of poverty has taken notice..."
```

### 6.7 Spillover Effects

When currency is injected into a location, **spillover is inevitable and desirable**. NPCs don't sit on money; they spend it according to their GOAP goals.

```
Treasure Found in Riverside Village
    │
    ├── Finder (farmer) has 500 extra gold
    │   └── GOAP: "I need a new plow" → Buys from blacksmith
    │
    ├── Blacksmith has 450 gold (minus margin)
    │   └── GOAP: "I need iron ore" → Buys from traveling merchant
    │
    ├── Merchant has 400 gold
    │   └── GOAP: "I'll stock up in the city" → Travels, spends elsewhere
    │
    └── Velocity increase ripples outward from injection point
```

**Design Implications**:
- Don't try to contain spillover - it's the economy working
- Track velocity at multiple granularities to see ripple effects
- Gods can observe where injected currency ends up
- Extreme hoarding (breaking the chain) becomes visible and targetable

### 6.8 Intentional Stagnation

Not every location should be economically healthy. Dead towns, abandoned quarters, and frontier regions may be **intentionally stagnant** for narrative purposes.

```yaml
# Location economic profile
LocationEconomicProfile:
  locationId: uuid

  # Divine attention level
  divineAttention: enum
    - active          # Gods actively maintain velocity
    - passive         # Gods observe but rarely intervene
    - abandoned       # Gods have withdrawn attention
    - cursed          # Intentionally suppressed

  # Stagnation policy
  allowedToStagnate: boolean
  stagnationReason: string    # "abandoned_mine", "plague_aftermath", etc.
  revitalizationTrigger: string  # What would bring gods' attention back
```

**Example**: The old mining district has `divineAttention: abandoned`. Velocity is 0.02. The Economic Deity sees this but takes no action because `allowedToStagnate: true`. Only when players or NPCs take significant action (clearing the monster, reopening the mine) does the `revitalizationTrigger` fire and divine attention return.

### 6.9 God Personality Variations

Different economic deities create different economic "feels" for their realms:

| Personality | Intervention Pattern | Economic Feel |
|-------------|---------------------|---------------|
| **High Subtlety** | Invisible hand, natural-seeming events | "The economy just works" |
| **Low Subtlety** | Obvious miracles, explicit curses | "The gods are watching" |
| **High Chaos** | Frequent, unpredictable interventions | "Anything can happen" |
| **Low Chaos** | Rare, measured corrections | "Stable and predictable" |
| **Favors Wealthy** | Protects hoarders, enables accumulation | "The rich get richer" |
| **Favors Poor** | Robin Hood redistribution | "Fortune favors the humble" |

**Multiple Gods Per Realm**: A realm might have both Mercurius (subtle, commerce-focused) and Laverna (chaotic, theft-focused). Their interventions can conflict, creating interesting dynamics:

```
Mercurius: "I'll send a wealthy merchant to revitalize this town"
Laverna: "Oh good, a wealthy target just arrived"
```

### 6.10 Integration Points

```
┌─────────────────────────────────────────────────────────────────┐
│                 Economic Deity (God Actor)                       │
│  - Observes velocity metrics                                     │
│  - Spawns intervention events                                    │
│  - Respects location stagnation policies                        │
└─────────────────────────────────────────────────────────────────┘
         │ Reads                          │ Spawns
         ▼                                ▼
┌─────────────────────┐          ┌─────────────────────┐
│   lib-analytics     │          │   lib-actor          │
│   /economy/velocity │          │   Event dispatch     │
│   /economy/distrib  │          │                      │
└─────────────────────┘          └─────────────────────┘
         ▲                                │
         │ Records                        │ Affects
┌─────────────────────┐          ┌─────────────────────┐
│   lib-currency      │          │   NPC GOAP Brains   │
│   Transactions      │◀─────────│   React to events   │
│   (32 endpoints)    │          │   Natural behavior  │
└─────────────────────┘          └─────────────────────┘
```

---

## Part 7: Foreign Trade, Exchange Rates, and Taxation

### 7.1 Design Philosophy: Three-Tier Usage

The trade and taxation system must serve three different usage patterns:

```
┌─────────────────────────────────────────────────────────────────┐
│ Tier 1: Divine/System Oversight                                  │
│ God actors or system processes with full visibility              │
│ Use: Full analytics, automatic interventions                     │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Same APIs
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ Tier 2: NPC Governance                                           │
│ Economist NPCs, customs officials, merchant guild leaders        │
│ Behaviors encode economic knowledge, imperfect information       │
│ Use: Query what they can "see", make bounded-rational decisions │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Same APIs
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ Tier 3: External Management                                      │
│ Game server handles all decisions outside Bannou                 │
│ Use: "Where's money flowing weakest?" - just give me data       │
└─────────────────────────────────────────────────────────────────┘
```

**Key Principle**: The plugin provides primitives and data. It doesn't enforce game-specific rules. Games decide how to use the APIs.

### 7.2 Trade Routes

Trade routes are **data constructs** describing paths between locations. The plugin doesn't pathfind - games define routes and the plugin stores them.

#### Trade Route Schema

```yaml
# Schema: trade-route
TradeRoute:
  id: uuid

  # Identification
  name: string
  code: string                    # "silk_road", "northern_passage"
  description: string

  # Ownership (optional - for personal/guild routes)
  ownerId: uuid                   # null for system routes
  ownerType: enum                 # system | character | guild | faction

  # Scope
  realmId: uuid                   # Primary realm (for queries)
  crossesRealms: boolean          # Does this route cross realm boundaries?

  # Status
  status: enum                    # active | closed | dangerous | seasonal

  # Metadata (game-provided)
  tags: [string]                  # "maritime", "mountain", "safe"
  seasonalAvailability: object    # { winter: false, summer: true }

  createdAt: timestamp
  modifiedAt: timestamp

# Schema: trade-route-leg
TradeRouteLeg:
  routeId: uuid
  legIndex: integer               # Order in route (0, 1, 2...)

  # Endpoints
  fromLocationId: uuid
  toLocationId: uuid

  # Travel metadata (game-provided)
  estimatedDuration: duration     # How long this leg takes
  distance: decimal               # In game units
  terrainType: string             # "road", "sea", "mountain", "river"
  riskLevel: decimal              # 0-1, base chance of incident

  # Border crossing (if applicable)
  crossesBorder: boolean
  fromRealmId: uuid
  toRealmId: uuid
  borderType: enum                # none | internal | external

  # Customs/checkpoint
  hasCheckpoint: boolean
  checkpointLocationId: uuid      # Where tariffs are assessed

# Aggregated route summary (computed)
TradeRouteSummary:
  routeId: uuid
  totalLegs: integer
  totalDistance: decimal
  totalEstimatedDuration: duration
  borderCrossings: integer
  internalBorderCrossings: integer
  externalBorderCrossings: integer
  riskProfile: object             # { low: 3, medium: 1, high: 0 } legs by risk
```

#### Trade Route API

```yaml
# Route Management
/trade/route/create:
  access: authenticated
  request:
    name: string
    code: string
    ownerId?: uuid
    ownerType: system | character | guild | faction
    realmId: uuid
    legs: [
      {
        fromLocationId: uuid
        toLocationId: uuid
        estimatedDuration: duration
        distance: decimal
        terrainType: string
        riskLevel: decimal
      }
    ]
  response: { route, summary }

/trade/route/get:
  access: user
  request:
    routeId: uuid
  response: { route, legs[], summary }

/trade/route/query:
  access: user
  request:
    fromLocationId?: uuid        # Routes starting here
    toLocationId?: uuid          # Routes ending here
    passingThrough?: uuid        # Routes passing through
    realmId?: uuid
    ownerId?: uuid
    ownerType?: enum
    tags?: [string]
    maxLegs?: integer
    maxDuration?: duration
  response: { routes[], summaries[] }

/trade/route/update:
  access: authenticated          # Must own route or be admin
  request:
    routeId: uuid
    status?: enum
    legs?: array                 # Replace all legs
  response: { route, summary }

/trade/route/delete:
  access: authenticated
  request:
    routeId: uuid
  response: { deleted: boolean }

# Route Analysis
/trade/route/estimate-cost:
  access: user
  request:
    routeId: uuid
    goods: [
      { templateId: uuid, quantity: integer }
    ]
    currencyId: uuid             # For tariff calculation
    currencyAmount: decimal      # Currency being transported
  response:
    totalTariffs: decimal
    tariffBreakdown: [
      {
        legIndex: integer
        borderType: string
        importTariff: decimal
        exportTariff: decimal
        currencyExchangeLoss: decimal
      }
    ]
    riskAssessment: object
    estimatedDuration: duration
```

### 7.3 Shipments (Declarative Lifecycle)

Shipments track goods/currency moving along routes. The plugin **does not automatically progress shipments** - the game server calls APIs to indicate what happened.

This follows `lib-scene`'s instantiate pattern: the plugin records state, the game drives events.

#### Shipment Schema

```yaml
# Schema: shipment
Shipment:
  id: uuid

  # Route
  routeId: uuid
  currentLegIndex: integer        # Which leg we're on (or completed)

  # Ownership
  ownerId: uuid
  ownerType: enum                 # character | guild | caravan_company

  # Carrier (who's physically moving it)
  carrierId: uuid                 # Character doing the transport
  carrierType: enum               # character | npc | vehicle

  # Contents
  goods: [
    {
      itemInstanceId: uuid
      templateId: uuid            # Denormalized for queries
      quantity: integer
      declared: boolean           # Was this declared at customs?
    }
  ]
  currencies: [
    {
      currencyId: uuid
      amount: decimal
      declared: boolean
    }
  ]

  # Status
  status: enum
    - preparing                   # Being loaded
    - in_transit                  # On the move
    - at_checkpoint               # Waiting at border
    - arrived                     # Reached destination
    - lost                        # Destroyed, stolen, etc.
    - seized                      # Confiscated at border

  # Tracking
  departedAt: timestamp
  currentLocationId: uuid         # Where it is now
  estimatedArrivalAt: timestamp
  arrivedAt: timestamp

  # Incident tracking
  incidents: [
    {
      legIndex: integer
      incidentType: string        # "bandit_attack", "storm", "customs_inspection"
      outcome: string
      losses: object              # What was lost/damaged
      timestamp: timestamp
    }
  ]

  createdAt: timestamp
  modifiedAt: timestamp
```

#### Shipment API (Game-Driven)

```yaml
/trade/shipment/create:
  access: authenticated
  request:
    routeId: uuid
    ownerId: uuid
    ownerType: enum
    carrierId: uuid
    carrierType: enum
    goods: [{ itemInstanceId, declared }]
    currencies: [{ currencyId, amount, declared }]
  response: { shipment }
  notes: |
    Items are NOT automatically moved to escrow. Game decides whether
    to use lib-escrow for safe holding during transport.

/trade/shipment/depart:
  access: authenticated
  request: { shipmentId: uuid }
  response: { shipment }
  emits: shipment.departed

/trade/shipment/complete-leg:
  access: authenticated
  request:
    shipmentId: uuid
    legIndex: integer
    incidents?: [{ incidentType, outcome, losses }]
  response: { shipment }
  emits: shipment.leg_completed

/trade/shipment/border-crossing:
  access: authenticated
  request:
    shipmentId: uuid
    legIndex: integer
    declaredGoods: [{ templateId, quantity }]
    declaredCurrency: [{ currencyId, amount }]
    actualGoods: [{ templateId, quantity, hidden: boolean }]
    actualCurrency: [{ currencyId, amount, hidden: boolean }]
    crossingType: enum            # legal | illegal | attempted_smuggle
    inspectionOccurred: boolean
    smugglingDetected: boolean
    tariffCollection: enum        # auto | manual | evaded | exempt
  response:
    shipment: object
    tariffRecord: object
    estimatedTariff: decimal
    actualTariffCollected: decimal
    seizures: array
  emits: shipment.border_crossed

/trade/shipment/arrive:
  access: authenticated
  request:
    shipmentId: uuid
    actualGoods: [{ templateId, quantity }]
    actualCurrency: [{ currencyId, amount }]
  response: { shipment }
  emits: shipment.arrived

/trade/shipment/lost:
  access: authenticated
  request:
    shipmentId: uuid
    reason: string
    recoverable: boolean
    lostGoods: [{ templateId, quantity }]
    lostCurrency: [{ currencyId, amount }]
  response: { shipment }
  emits: shipment.lost
```

### 7.4 Borders and Tariffs

#### Tariff Definition Schema

```yaml
# Schema: tariff-policy
TariffPolicy:
  id: uuid
  realmId: uuid

  scope: enum                     # realm_wide | specific_border | category
  borderLocationId: uuid          # For specific_border scope
  direction: enum                 # import | export | both

  # Rates
  defaultRate: decimal            # e.g., 0.05 = 5%
  categoryRates: [
    { category: string, rate: decimal }
  ]
  itemRates: [
    { templateId: uuid, rate: decimal }
  ]
  currencyRate: decimal           # Rate on currency itself

  # Exemptions
  exemptions: [
    {
      entityType: string
      entityId: uuid
      exemptionType: full | partial
      partialRate: decimal
    }
  ]

  status: enum                    # active | suspended | wartime
  effectiveFrom: timestamp
  effectiveUntil: timestamp
```

#### Tariff API

```yaml
/trade/tariff/policy/create:
  access: admin
  request: { realmId, scope, direction, rates... }
  response: { policy }

/trade/tariff/policy/list:
  access: user
  request: { realmId, includeExpired: boolean }
  response: { policies[] }

/trade/tariff/calculate:
  access: user
  request:
    fromRealmId: uuid
    toRealmId: uuid
    goods: [{ templateId, quantity, unitValue? }]
    currencies: [{ currencyId, amount }]
    entityId?: uuid               # For exemption checking
  response: { calculation }
  notes: |
    Pure calculation - doesn't collect anything.

/trade/tariff/collect:
  access: authenticated
  request:
    shipmentId: uuid
    tariffRecordId: uuid
    walletId: uuid
    amount: decimal
  response: { transaction, remainingDue }
  notes: |
    Uses lib-currency /currency/debit internally.
```

### 7.5 Exchange Rates and Universal Value

**Note**: lib-currency already provides basic exchange rate endpoints (`/currency/exchange-rate/get`, `/currency/exchange-rate/update`) and conversion (`/currency/convert/calculate`, `/currency/convert/execute`). The architecture below describes a more sophisticated system with universal values and location-scoped modifiers that could extend the existing currency service.

#### Universal Value Concept

Currencies can optionally have a **universal value** - an intrinsic worth that can change. Exchange rates can then be computed dynamically from universal values plus modifiers.

```yaml
# Schema: currency-universal-value
CurrencyUniversalValue:
  currencyId: uuid
  universalValue: decimal         # Relative worth (1.0 = baseline)
  previousValue: decimal
  changedAt: timestamp
  changeReason: string            # "inflation", "gold_discovery", "war_ended"
  changedBy: string               # "admin", "economic_event", "market_algorithm"
```

#### Extended Exchange Rate Schema

```yaml
# Extends existing lib-currency exchange rate with modifiers and scoping
ExchangeRateExtended:
  fromCurrencyId: uuid
  toCurrencyId: uuid

  # Scope (rates can vary by location)
  scope: enum                     # global | realm | location
  scopeId: uuid

  # How determined
  rateType: enum                  # manual | computed

  # For computed rates
  baseRate: decimal               # From universal values
  modifiers: [
    {
      modifierType: string        # "tariff", "war", "festival", "shortage"
      modifierValue: decimal      # Multiplier (1.1 = +10%)
      source: string
      expiresAt: timestamp
    }
  ]

  # Spread (for currency exchange businesses)
  buySpread: decimal
  sellSpread: decimal

# Computed rate formula:
# rate = (fromCurrency.universalValue / toCurrency.universalValue) * product(modifiers)
```

### 7.6 Contraband and Illegal Crossings

The system explicitly supports smuggling and illegal activity, enabling rich gameplay without prescribing outcomes.

#### How It Works

1. **Game tracks what's actually being carried** (full truth)
2. **Carrier declares subset at customs** (what they claim)
3. **Game determines if inspection occurs** (skills, bribes, random checks)
4. **Game reports outcome to plugin** (detected or not)
5. **Plugin records everything for analytics** (smuggling rates, contraband value)

```yaml
# Schema: contraband-definition
ContrabandDefinition:
  id: uuid
  realmId: uuid

  type: enum                      # item_category | item_template | currency
  targetId: uuid
  targetCategory: string          # "weapons", "drugs", "religious_artifacts"

  severity: enum                  # restricted | prohibited | capital_offense

  # Consequences (advisory - game enforces)
  suggestedFine: decimal
  suggestedPenalty: string

  effectiveFrom: timestamp
  effectiveUntil: timestamp

# Query API
/trade/contraband/check:
  access: user
  request:
    realmId: uuid
    goods: [{ templateId, quantity }]
    currencies: [{ currencyId, amount }]
  response:
    violations: [{ type, targetId, severity, suggestedFine }]
    hasContraband: boolean
    totalContrabandValue: decimal
```

### 7.7 Taxation Systems

Taxes are systematic sinks. The plugin provides infrastructure; games decide policy.

#### Tax Types

| Type | Trigger | Collected By | Example |
|------|---------|--------------|---------|
| **Transaction Tax** | Every trade/auction | Automatic or manual | 5% auction house cut |
| **Import/Export** | Border crossing | Via tariff system | 10% on foreign goods |
| **Property Tax** | Ownership over time | Periodic assessment | 100g/week for housing |
| **Income Tax** | Currency gains | Faucet events | 5% on quest rewards |
| **Wealth Tax** | Total holdings | Periodic assessment | 0.1% of net worth annually |
| **Sales Tax** | NPC vendor purchases | Point of sale | 8% on vendor buys |

#### Tax Collection Patterns

```yaml
# Pattern A: Automatic (simple games)
# Plugin automatically debits taxes during relevant events
TaxPolicy:
  collectionMode: automatic
  collectionTarget: void          # Just a sink

# Pattern B: Assessed (complex games)
# Plugin calculates, game/NPCs collect
TaxPolicy:
  collectionMode: assessed
  assessmentFrequency: monthly

# Pattern C: Advisory (Arcadia-style)
# Plugin provides calculation APIs only, game handles everything
TaxPolicy:
  collectionMode: advisory
```

#### Tax Schema

```yaml
TaxPolicy:
  id: uuid
  realmId: uuid
  taxType: enum                   # transaction | income | property | wealth | sales | custom
  name: string

  baseRate: decimal
  progressiveBrackets: [          # Optional for progressive taxation
    { threshold: 1000, rate: 0.05 },
    { threshold: 10000, rate: 0.08 },
    { threshold: 100000, rate: 0.12 }
  ]

  exemptions: [
    { entityType: string, entityId: uuid, reason: string }
  ]

  collectionMode: enum            # automatic | assessed | advisory
  collectionTarget: enum          # void | realm_treasury | faction_id
  collectionTargetId: uuid

  # For assessed taxes
  assessmentFrequency: duration
  gracePeriod: duration
  latePenaltyRate: decimal

  status: active | suspended
  effectiveFrom: timestamp
  effectiveUntil: timestamp

TaxAssessment:
  id: uuid
  policyId: uuid
  taxpayerId: uuid
  taxpayerType: enum
  assessedValue: decimal
  taxDue: decimal
  dueDate: timestamp
  status: enum                    # pending | partial | paid | overdue | defaulted
  amountPaid: decimal
  penaltiesAccrued: decimal

TaxDebt:
  taxpayerId: uuid
  taxpayerType: enum
  realmId: uuid
  totalOwed: decimal
  oldestDueDate: timestamp
  suggestedConsequences: [string]
```

#### Tax API

```yaml
/tax/policy/create:
  access: admin
  request: { realmId, taxType, rate, collectionMode... }
  response: { policy }

/tax/policy/list:
  access: user
  request: { realmId }
  response: { policies[] }

/tax/calculate:
  access: user
  request:
    policyId: uuid
    taxpayerId: uuid
    baseValue: decimal
  response: { taxDue, effectiveRate, bracketApplied }

/tax/assess:
  access: admin
  request: { policyId, taxpayerId, assessedValue, dueDate }
  response: { assessment }
  emits: tax.assessment.created

/tax/pay:
  access: authenticated
  request: { assessmentId, walletId, amount }
  response: { assessment, transaction, remainingDue }
  emits: tax.payment.received
  notes: |
    Uses lib-currency /currency/debit internally.

/tax/debt/get:
  access: user
  request: { taxpayerId, realmId? }
  response: { debt, assessments[] }

/tax/debt/list-delinquent:
  access: admin
  request: { realmId, minOwed?, minDaysOverdue? }
  response: { debts[] }
```

### 7.8 Integration Examples

#### Simple Game: Auto-Everything

```yaml
TariffPolicy:
  collectionMode: auto

TaxPolicy:
  collectionMode: automatic
  collectionTarget: void          # Just a sink

ExchangeRate:
  rateType: manual                # Admin sets rates directly

# Game flow:
# 1. Shipment crosses border
# 2. Plugin auto-debits tariff via lib-currency
# 3. Done
```

#### Complex Game: NPC Tax Collectors

```yaml
TaxPolicy:
  collectionMode: assessed
  assessmentFrequency: monthly

# Game flow:
# 1. Plugin emits tax.assessment.created monthly
# 2. Tax Collector NPC (lib-actor) receives event
# 3. NPC GOAP: goal = collect_taxes
# 4. NPC visits taxpayers, calls /tax/pay on their behalf
# 5. Some taxpayers evade - NPC tracks, reports to authorities
# 6. Authorities may issue arrest warrants (game logic)
```

#### Arcadia: Full Control

```yaml
TariffPolicy:
  collectionMode: advisory

TaxPolicy:
  collectionMode: advisory

ExchangeRate:
  rateType: computed
  # Individual merchants ignore this and set their own prices

# Game flow:
# 1. Player approaches border with goods
# 2. Game calls /trade/tariff/calculate
# 3. Customs officer NPC decides: collect, negotiate, accept bribe, let slide
# 4. Game calls /trade/shipment/border-crossing with actual outcome
# 5. Plugin records everything for analytics
# 6. Economic gods observe smuggling rates, may intervene narratively
```

---

## Summary

### What's Built (Foundation)

| Component | Status | Endpoints |
|-----------|--------|-----------|
| **lib-currency** | COMPLETE | 32 |
| **lib-item** | COMPLETE | 13 |
| **lib-inventory** | COMPLETE | 16 |
| **lib-contract** | COMPLETE | 30 |
| **lib-escrow** | COMPLETE | 20 |

### What's Planned (This Document)

| Component | Purpose | Status |
|-----------|---------|--------|
| **lib-market** | Auctions, vendors, price discovery | Design complete |
| **lib-economy** | Monitoring, NPC AI, analytics | Design complete |
| **Economic Deities** | God Actors that maintain velocity via narrative events | Design complete |
| **Analytics Extensions** | Velocity tracking at currency/realm/location granularity | Design complete |
| **Trade Routes** | Route definitions, shipment lifecycle, border crossings | Design complete |
| **Tariffs/Taxes** | Configurable collection modes, contraband support | Design complete |
| **Exchange Rate Extensions** | Universal values, computed rates, location-scoped | Design complete |
| **ABML Action Handlers** | Economy/inventory handlers for NPC behaviors | Design complete |
| **Quest Integration** | Currency/item rewards and prerequisites | Design complete |

### Key Architectural Decisions (Planned Services)

1. **Event-sourced transactions** (audit, rollback, analytics)
2. **GOAP-integrated NPC economy** (believable behavior)
3. **Faucet/Sink discipline** (inflation control)
4. **Divine economic intervention** (velocity control via narrative events)
5. **Redistribution over creation** (most interventions move currency, not create/destroy)
6. **Three-tier usage pattern** (divine oversight / NPC governance / external management)
7. **Declarative shipment lifecycle** (game drives events, plugin records state)
8. **Configurable collection modes** (auto / assessed / advisory for all taxes/tariffs)
9. **Universal value anchoring** (computed exchange rates from intrinsic currency worth)
10. **Explicit contraband support** (declared vs actual, detection as game logic)

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

*This document covers planned services building on the completed foundation layer (lib-currency, lib-item, lib-inventory, lib-contract, lib-escrow). Update as implementation progresses.*
