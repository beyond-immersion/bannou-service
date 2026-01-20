# Economy and Currency Architecture Design

> **Created**: 2026-01-19
> **Last Updated**: 2026-01-19
> **Purpose**: Comprehensive architecture for Bannou's economic systems
> **Scope**: Currency, Items, Markets, NPC Economic Participation, Quest Integration
> **Dependencies**: Character, Realm, ABML/GOAP, lib-state, lib-messaging, lib-analytics, Actor Service

This document synthesizes research on virtual economies, item systems, and agent-based economics into a concrete architecture for Bannou's economic layer.

---

## Executive Summary

Economy is not a single plugin but an **architectural layer** spanning multiple services:

| Service | Responsibility | Priority |
|---------|---------------|----------|
| **lib-currency** | Wallets, balances, multi-currency, transfers | 1st |
| **lib-inventory** | Item templates, instances, containers | 1st (parallel) |
| **lib-market** | Auctions, trading, price discovery | 2nd |
| **lib-craft** | Recipes, production, skill gating | 2nd (parallel) |
| **lib-economy** | Orchestration, NPC participation, monitoring | 3rd |

**Core Architectural Decisions**:
1. **Template/Instance pattern** for items (memory efficient, clear separation)
2. **Event-sourced transactions** for audit and rollback capability
3. **Realm-scoped sharding** with cross-realm capability where needed
4. **GOAP integration** for NPC economic decision-making
5. **Faucet/Sink discipline** - every currency source has corresponding removal
6. **Divine economic intervention** - Specialized gods manipulate velocity via narrative events

---

## Part 1: Currency Service (`lib-currency`)

### 1.1 Design Philosophy

Currency is the **medium of exchange**, not the economy itself. The currency service handles:
- Wallet management (polymorphic ownership)
- Balance tracking with optional caps
- Atomic transfers and transactions
- Multi-currency support with realm scoping
- Transaction audit trail

### 1.2 Currency Scoping Model

Currencies can exist at different scopes:

| Scope | Description | Example |
|-------|-------------|---------|
| **Global** | Available in all realms, account-level | Premium gems, loyalty points |
| **Realm-Specific** | Only exists in one realm | Eldoria gold, Arcadia credits |
| **Multi-Realm** | Available in selected realms | Regional alliance tokens |

```yaml
# Schema: currency-definition
CurrencyDefinition:
  id: uuid
  code: string              # "gold", "arcane_dust", "premium_gems"
  name: string              # Display name
  description: string

  # Scoping
  scope: enum               # global | realm_specific | multi_realm
  realmsAvailable: [uuid]   # null for global, list for multi_realm

  # Behavior
  tradeable: boolean        # Can be traded between players
  transferable: boolean     # Can be transferred (gifted) without trade
  cappable: boolean         # Supports daily/weekly earn caps
  dailyCap: decimal         # null if no cap
  weeklyCap: decimal

  # Display
  iconAssetId: uuid
  decimalPlaces: integer    # 0 for whole units, 2 for cents-style

  # Metadata
  createdAt: timestamp
  isActive: boolean
```

### 1.3 Wallet Architecture

Wallets provide polymorphic ownership following Bannou patterns:

```yaml
# Schema: wallet
Wallet:
  id: uuid

  # Polymorphic ownership (per IMPLEMENTATION TENETS)
  ownerId: uuid
  ownerType: enum           # account | character | guild | npc | location

  # Realm binding
  realmId: uuid             # null for account-level wallets

  # Metadata
  createdAt: timestamp

# Schema: currency-balance
CurrencyBalance:
  walletId: uuid
  currencyDefinitionId: uuid

  amount: decimal           # Current balance

  # Cap tracking (if currency is cappable)
  dailyEarned: decimal      # Reset daily
  weeklyEarned: decimal     # Reset weekly
  dailyResetAt: timestamp
  weeklyResetAt: timestamp

  lastModified: timestamp
```

### 1.4 Transaction Model

All currency mutations are recorded as immutable transactions:

```yaml
# Schema: currency-transaction (event-sourced)
CurrencyTransaction:
  id: uuid

  # Parties
  sourceWalletId: uuid      # null for faucets (currency creation)
  targetWalletId: uuid      # null for sinks (currency destruction)

  # Currency
  currencyDefinitionId: uuid
  amount: decimal           # Always positive

  # Classification
  transactionType: enum
    # Faucets (currency enters system)
    - quest_reward
    - loot_drop
    - vendor_sale           # Selling items to NPC
    - daily_login
    - admin_grant

    # Sinks (currency leaves system)
    - vendor_purchase       # Buying items from NPC
    - repair_cost
    - fast_travel
    - auction_fee
    - crafting_cost
    - tax
    - admin_remove

    # Transfers (currency moves between wallets)
    - player_trade
    - guild_deposit
    - guild_withdraw
    - gift
    - auction_sale
    - auction_purchase

  # Reference to source event
  referenceId: uuid         # Quest ID, Auction ID, etc.
  referenceType: enum       # quest | auction | trade | admin | etc.

  # Audit
  timestamp: timestamp
  sourceBalanceBefore: decimal
  sourceBalanceAfter: decimal
  targetBalanceBefore: decimal
  targetBalanceAfter: decimal

  # Idempotency
  idempotencyKey: string    # Prevents double-processing
```

### 1.5 API Endpoints

```yaml
# Currency Definition Management (admin)
/currency/definition/create:
  access: admin
  request: { code, name, scope, realmsAvailable, ... }
  response: { currencyDefinition }

/currency/definition/list:
  access: authenticated
  request: { realmId? }     # Filter by realm availability
  response: { definitions[] }

# Wallet Management
/currency/wallet/get:
  access: user
  request: { ownerId, ownerType, realmId? }
  response: { wallet, balances[] }

/currency/wallet/get-or-create:
  access: authenticated
  request: { ownerId, ownerType, realmId? }
  response: { wallet, balances[], created: boolean }

# Balance Operations
/currency/balance/get:
  access: user
  request: { walletId, currencyDefinitionId }
  response: { balance, dailyRemaining?, weeklyRemaining? }

/currency/credit:
  access: authenticated     # Services can credit
  request:
    walletId: uuid
    currencyDefinitionId: uuid
    amount: decimal
    transactionType: enum   # Must be a faucet type
    referenceId: uuid
    referenceType: enum
    idempotencyKey: string
  response: { transaction, newBalance }

/currency/debit:
  access: authenticated
  request:
    walletId: uuid
    currencyDefinitionId: uuid
    amount: decimal
    transactionType: enum   # Must be a sink type
    referenceId: uuid
    referenceType: enum
    idempotencyKey: string
  response: { transaction, newBalance }
  errors:
    - INSUFFICIENT_FUNDS

/currency/transfer:
  access: authenticated
  request:
    sourceWalletId: uuid
    targetWalletId: uuid
    currencyDefinitionId: uuid
    amount: decimal
    transactionType: enum   # Must be a transfer type
    referenceId: uuid
    referenceType: enum
    idempotencyKey: string
  response: { transaction }
  errors:
    - INSUFFICIENT_FUNDS
    - CURRENCY_NOT_TRANSFERABLE
    - CROSS_REALM_NOT_ALLOWED

# History/Audit
/currency/transaction/history:
  access: user
  request: { walletId, currencyDefinitionId?, limit, offset }
  response: { transactions[], totalCount }

/currency/transaction/get:
  access: authenticated
  request: { transactionId }
  response: { transaction }
```

### 1.6 Events

```yaml
# Published to lib-messaging
currency.credited:
  walletId: uuid
  currencyDefinitionId: uuid
  amount: decimal
  transactionType: string
  newBalance: decimal

currency.debited:
  walletId: uuid
  currencyDefinitionId: uuid
  amount: decimal
  transactionType: string
  newBalance: decimal

currency.transferred:
  sourceWalletId: uuid
  targetWalletId: uuid
  currencyDefinitionId: uuid
  amount: decimal
  transactionType: string

currency.cap.reached:
  walletId: uuid
  currencyDefinitionId: uuid
  capType: daily | weekly

currency.cap.reset:
  walletId: uuid
  currencyDefinitionId: uuid
  capType: daily | weekly
```

### 1.7 State Stores

```yaml
# Redis for hot data
currency-balance-cache:
  backend: redis
  prefix: "curr:bal"
  purpose: Real-time balance lookups

currency-transaction-recent:
  backend: redis
  prefix: "curr:txn"
  ttl: 3600  # 1 hour
  purpose: Recent transaction deduplication

# MySQL for persistence
currency-definition-statestore:
  backend: mysql
  table: currency_definitions
  purpose: Currency type registry

currency-wallet-statestore:
  backend: mysql
  table: wallets
  purpose: Wallet ownership records

currency-balance-statestore:
  backend: mysql
  table: currency_balances
  purpose: Authoritative balance records

currency-transaction-statestore:
  backend: mysql
  table: currency_transactions
  purpose: Immutable transaction log (event store)
```

---

## Part 2: Inventory Service (`lib-inventory`)

### 2.1 Design Philosophy

The **Template/Instance pattern** separates static definitions from dynamic state:

- **Item Templates**: Designer-created, shared across all instances, cached globally
- **Item Instances**: Runtime occurrences with unique state, owned by entities

This is the industry standard (WoW, EVE, RuneScape) for memory efficiency and clean separation.

### 2.2 Item Template Schema

```yaml
# Schema: item-template
ItemTemplate:
  id: uuid

  # Identification
  code: string              # "iron_sword", "health_potion"
  name: string
  description: string

  # Classification
  category: enum            # weapon, armor, consumable, material, quest, misc
  subcategory: string       # "sword", "helmet", "food", etc.
  tags: [string]            # Searchable tags

  # Stacking
  stackable: boolean
  maxStackSize: integer     # 1 for non-stackable, 20/100/999 for stackable

  # Value
  baseValue: decimal        # Vendor buy/sell reference price
  rarity: enum              # common, uncommon, rare, epic, legendary

  # Physical properties
  weight: decimal           # For encumbrance systems

  # Realm scoping
  realmScope: enum          # global | realm_specific | multi_realm
  realmsAvailable: [uuid]

  # Flags
  tradeable: boolean
  destroyable: boolean
  questItem: boolean        # Cannot be dropped/traded if true
  soulbound: enum           # none | on_pickup | on_equip | on_use

  # Stats/Effects (JSON for flexibility)
  stats: object             # { attack: 25, defense: 0, speed: -2 }
  effects: object           # { on_use: "heal", amount: 50 }

  # Display
  iconAssetId: uuid
  modelAssetId: uuid

  # Metadata
  createdAt: timestamp
  isActive: boolean
```

### 2.3 Item Instance Schema

```yaml
# Schema: item-instance
ItemInstance:
  id: uuid
  templateId: uuid

  # Ownership (polymorphic)
  ownerId: uuid
  ownerType: enum           # character | inventory | bank | auction | mail | ground

  # Location specifics
  containerId: uuid         # For nested containers (bags within bags)
  slotIndex: integer        # Position in container (null for unslotted)

  # Realm binding
  realmId: uuid

  # Instance state
  quantity: integer         # >1 for stackable items
  currentDurability: integer # null if item has no durability
  maxDurability: integer

  # Modifications
  customStats: object       # Enchantments, modifications, random rolls
  customName: string        # Player-renamed items

  # Binding
  boundToId: uuid           # Character ID if soulbound
  boundAt: timestamp

  # Origin tracking
  originType: enum          # loot, quest, craft, trade, admin
  originId: uuid            # Quest ID, creature ID, etc.
  originTimestamp: timestamp

  # Metadata
  createdAt: timestamp
  modifiedAt: timestamp
```

### 2.4 Container Schema

Inventories are containers that hold item instances:

```yaml
# Schema: container
Container:
  id: uuid

  # Ownership
  ownerId: uuid
  ownerType: enum           # character | bank | guild | location

  # Container type
  containerType: enum       # inventory, bank, bag, chest, mailbox

  # Capacity
  maxSlots: integer
  usedSlots: integer        # Denormalized for quick checks

  # Weight limit (optional)
  maxWeight: decimal
  currentWeight: decimal

  # Realm binding
  realmId: uuid

  # Metadata
  createdAt: timestamp
```

### 2.5 API Endpoints

```yaml
# Template Management (admin/developer)
/inventory/template/create:
  access: developer
  request: { code, name, category, ... }
  response: { template }

/inventory/template/get:
  access: user
  request: { templateId | code }
  response: { template }

/inventory/template/list:
  access: user
  request: { category?, realmId?, search? }
  response: { templates[], totalCount }

# Container Management
/inventory/container/get:
  access: user
  request: { ownerId, ownerType, containerType }
  response: { container, items[] }

/inventory/container/create:
  access: authenticated
  request: { ownerId, ownerType, containerType, maxSlots }
  response: { container }

# Item Operations
/inventory/add:
  access: authenticated
  request:
    containerId: uuid
    templateId: uuid
    quantity: integer
    originType: enum
    originId: uuid
    customStats?: object
    slotIndex?: integer     # null for auto-placement
  response: { instance, stacked: boolean }
  errors:
    - CONTAINER_FULL
    - WEIGHT_EXCEEDED

/inventory/remove:
  access: authenticated
  request:
    instanceId: uuid
    quantity: integer       # For partial stack removal
    reason: enum            # consumed, destroyed, transferred, sold
  response: { removed: boolean, remainingQuantity: integer }

/inventory/move:
  access: authenticated
  request:
    instanceId: uuid
    targetContainerId: uuid
    targetSlotIndex?: integer
  response: { instance }
  errors:
    - CONTAINER_FULL
    - INCOMPATIBLE_CONTAINER

/inventory/transfer:
  access: authenticated
  request:
    instanceId: uuid
    targetOwnerId: uuid
    targetOwnerType: enum
    quantity: integer
  response: { sourceInstance?, targetInstance }
  errors:
    - NOT_TRADEABLE
    - SOULBOUND
    - TARGET_CONTAINER_FULL

/inventory/split:
  access: user
  request:
    instanceId: uuid
    quantity: integer       # Amount to split off
  response: { originalInstance, newInstance }

/inventory/merge:
  access: user
  request:
    sourceInstanceId: uuid
    targetInstanceId: uuid
  response: { instance, sourceDestroyed: boolean }

# Queries
/inventory/query:
  access: user
  request:
    ownerId: uuid
    ownerType: enum
    templateId?: uuid
    category?: enum
    tags?: [string]
  response: { instances[] }

/inventory/count:
  access: user
  request:
    ownerId: uuid
    ownerType: enum
    templateId: uuid
  response: { totalQuantity }

/inventory/has:
  access: authenticated
  request:
    ownerId: uuid
    ownerType: enum
    templateId: uuid
    quantity: integer
  response: { has: boolean, actualQuantity: integer }
```

### 2.6 Events

```yaml
inventory.item.added:
  containerId: uuid
  instanceId: uuid
  templateId: uuid
  quantity: integer
  originType: string

inventory.item.removed:
  containerId: uuid
  instanceId: uuid
  templateId: uuid
  quantity: integer
  reason: string

inventory.item.transferred:
  sourceOwnerId: uuid
  targetOwnerId: uuid
  instanceId: uuid
  templateId: uuid
  quantity: integer

inventory.item.modified:
  instanceId: uuid
  changes: object           # What changed (durability, customStats, etc.)

inventory.container.full:
  containerId: uuid
  ownerId: uuid
```

### 2.7 State Stores

```yaml
# Redis for hot data
inventory-template-cache:
  backend: redis
  prefix: "inv:tpl"
  purpose: Template lookup cache (global)

inventory-instance-cache:
  backend: redis
  prefix: "inv:inst"
  purpose: Hot item instance data

# MySQL for persistence
inventory-template-statestore:
  backend: mysql
  table: item_templates
  purpose: Item definitions (global)

inventory-instance-statestore:
  backend: mysql
  table: item_instances
  purpose: Item instances (realm-partitioned)

inventory-container-statestore:
  backend: mysql
  table: containers
  purpose: Container definitions
```

---

## Part 3: Market Service (`lib-market`)

### 3.1 Design Philosophy

Markets handle **exchange** - converting items to currency and vice versa. This includes:
- Auction houses (player-to-player trading via listings)
- Vendor systems (NPC-to-player fixed-price trading)
- Trade posts (regional markets with shipping)

### 3.2 Auction House Architecture

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

### 3.3 Listing Schema

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

### 3.4 Vendor System

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

### 3.5 API Endpoints

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

### 3.6 Events

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

## Part 4: Economy Orchestration (`lib-economy`)

### 4.1 Design Philosophy

The economy service is the **intelligence layer** that:
- Monitors faucet/sink balance
- Provides NPC economic AI integration
- Offers analytics and balancing tools
- Coordinates cross-service economic events

### 4.2 Faucet/Sink Monitoring

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

### 4.3 NPC Economic Participation

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

### 4.4 GOAP Integration for Economic NPCs

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

### 4.5 API Endpoints

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

## Part 5: Quest Integration

### 5.1 Quest → Economy Dependencies

The Quest service (from ABML planning doc) requires:

```yaml
# Quest Reward Types
QuestReward:
  type: enum

  # Currency rewards → lib-currency
  currency_reward:
    currencyDefinitionId: uuid
    amount: decimal

  # Item rewards → lib-inventory
  item_reward:
    templateId: uuid
    quantity: integer
    selectionGroup: integer   # For "choose one" rewards

  # Reputation (handled by relationships, not economy)

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

### 5.2 Quest Completion Flow

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

## Part 6: Scale Considerations

### 6.1 Handling 100K+ NPCs

**Key Insight**: Not all NPCs are economically active simultaneously.

**Strategies**:

1. **Lazy Wallet Creation**: NPCs don't get wallets until they transact
   ```csharp
   // Only create when needed
   var wallet = await _currencyService.GetOrCreateWallet(npcId, "npc");
   ```

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

### 6.2 Database Partitioning

```
┌─────────────────────────────────────────────────────────────┐
│                    GLOBAL (Replicated)                       │
├─────────────────────────────────────────────────────────────┤
│  currency_definitions   - Currency types                     │
│  item_templates         - Item definitions                   │
│  vendor_catalogs        - Base vendor configs                │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│                   PER-REALM (Sharded)                        │
├─────────────────────────────────────────────────────────────┤
│  currency_balances      - Realm-specific balances            │
│  currency_transactions  - Transaction audit log              │
│  item_instances         - Item occurrences                   │
│  auction_listings       - Active auctions                    │
│  economy_metrics        - Time-series metrics                │
└─────────────────────────────────────────────────────────────┘
```

### 6.3 Caching Strategy

```yaml
# Hot path caching (Redis)
currency_balance:
  key: "curr:bal:{walletId}:{currencyId}"
  ttl: 300  # 5 minutes
  invalidate_on: credit, debit, transfer

item_template:
  key: "inv:tpl:{templateId}"
  ttl: 3600  # 1 hour (templates rarely change)
  invalidate_on: template_update

market_price:
  key: "market:price:{realmId}:{templateId}"
  ttl: 60   # 1 minute (prices change frequently)
  update_on: auction_sold
```

---

## Part 7: Implementation Sequence

Based on dependencies:

```
Week 1-2: Foundation (Parallel)
├── lib-currency (5-7 days)
│   ├── Currency definition schema + API
│   ├── Wallet management
│   ├── Credit/Debit/Transfer operations
│   └── Transaction logging
│
└── lib-inventory (5-7 days)
    ├── Item template schema + API
    ├── Item instance management
    ├── Container system
    └── Basic queries

Week 3-4: Exchange (Parallel)
├── lib-market (5-7 days)
│   ├── Auction listing/bidding
│   ├── Settlement system
│   └── Vendor catalogs
│
└── lib-craft (3-5 days) [Optional]
    ├── Recipe definitions
    ├── Crafting execution
    └── Skill integration

Week 5-6: Intelligence
└── lib-economy (5-7 days)
    ├── Faucet/sink monitoring
    ├── NPC economic profiles
    ├── GOAP action handlers
    └── Price analytics

Week 7+: Quest Integration
└── lib-quest economic features
    ├── Currency rewards
    ├── Item rewards
    └── Economic prerequisites/objectives
```

---

## Part 8: ABML Action Handlers

New handlers for economic behaviors:

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

## Part 9: Money Velocity and Divine Economic Intervention

### 9.1 Design Philosophy

Traditional game economies use direct manipulation (adjusting drop rates, vendor prices) to control economic health. Bannou takes a different approach: **specialized divine actors observe economic metrics and spawn narrative events** that naturally adjust velocity through NPC reactions.

This approach:
- Preserves NPC autonomy (they respond to events, not forced behavior)
- Creates narrative coherence (every adjustment has a story)
- Allows intentional stagnation (dead towns stay dead until revitalized)
- Enables creative variation (different gods, different intervention styles)

### 9.2 Money Velocity

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

### 9.3 Analytics Integration for Velocity

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

### 9.4 Economic Deities (God Actors)

Economic balance is maintained by **specialized divine actors** - long-running Actor instances that observe analytics and spawn corrective events.

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
  deityActorId: uuid          # The god's Actor instance
  deityType: string           # "mercurius", "binbougami", etc.
  realmsManaged: [uuid]       # Which realms this god watches

  # Personality parameters (affect intervention style)
  personality:
    interventionFrequency: decimal  # How often to act (0.1 = rarely, 1.0 = constantly)
    subtlety: decimal               # 0 = obvious miracles, 1 = invisible hand
    favoredTargets: enum            # wealthy | poor | merchants | anyone
    chaosAffinity: decimal          # 0 = orderly, 1 = loves disruption
```

### 9.5 Intervention Event Types

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

### 9.6 God Actor GOAP Implementation

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

### 9.7 Spillover Effects

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

### 9.8 Intentional Stagnation

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

### 9.9 God Personality Variations

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

### 9.10 Integration Points

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
│   lib-analytics     │          │   Actor Service     │
│   /economy/velocity │          │   Event dispatch    │
│   /economy/distrib  │          │                     │
└─────────────────────┘          └─────────────────────┘
         ▲                                │
         │ Records                        │ Affects
┌─────────────────────┐          ┌─────────────────────┐
│   lib-currency      │          │   NPC GOAP Brains   │
│   Transactions      │◀─────────│   React to events   │
│   Event-sourced     │          │   Natural behavior  │
└─────────────────────┘          └─────────────────────┘
```

---

## Part 10: Summary

### What We're Building

| Component | Purpose | Status |
|-----------|---------|--------|
| **lib-currency** | Multi-currency wallets, transactions, audit trail | Design complete |
| **lib-inventory** | Item templates/instances, containers | Design complete |
| **lib-market** | Auctions, vendors, price discovery | Design complete |
| **lib-economy** | Monitoring, NPC AI, analytics | Design complete |
| **Economic Deities** | God Actors that maintain velocity via narrative events | Design complete |
| **Analytics Extensions** | Velocity tracking at currency/realm/location granularity | Design complete |

### Key Architectural Decisions

1. **Template/Instance** for items (memory efficient)
2. **Event-sourced transactions** (audit, rollback, analytics)
3. **Realm-scoped sharding** (scale)
4. **GOAP-integrated NPC economy** (believable behavior)
5. **Faucet/Sink discipline** (inflation control)
6. **Divine economic intervention** (velocity control via narrative events)
7. **Redistribution over creation** (most interventions move currency, not create/destroy)

### Quest System Requirements

- `/currency/credit` for currency rewards
- `/inventory/add` for item rewards
- `/inventory/has` for prerequisites
- Economic objectives tracked via events

### Scale Strategy

- Lazy instantiation for NPC wallets
- Template-based defaults
- Tick-based batch processing
- Regional market aggregation
- Per-realm sharding

### Velocity Control Strategy

- Analytics tracks velocity at currency/realm/location granularity
- Economic Deities (God Actors) observe metrics and spawn events
- Most interventions are redistribution, not creation/destruction
- Spillover effects are natural and desirable
- Intentional stagnation supported for narrative purposes
- Multiple specialized gods per realm create emergent dynamics

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

*This document should be updated as implementation progresses and new requirements emerge.*
