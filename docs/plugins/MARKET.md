# Market Plugin Deep Dive

> **Plugin**: lib-market
> **Schema**: schemas/market-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: market-definitions (MySQL), market-listings (MySQL), market-bids (Redis), market-vendors (MySQL), market-vendor-stock (Redis), market-price-history (MySQL), market-settlement-queue (Redis), market-idempotency (Redis), market-lock (Redis)
> **Status**: Pre-implementation (architectural specification)
> **Planning**: [Economy System Guide](../guides/ECONOMY-SYSTEM.md)
> **Short**: Marketplace orchestration (auctions, NPC vendors, price discovery) composing Escrow/Currency/Item

## Overview

Marketplace orchestration service (L4 GameFeatures) for auctions, NPC vendor management, and price discovery. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver game economy exchange mechanics. Game-agnostic: auction house rules, vendor personality templates, fee structures, and pricing modes are configured through market definitions, ABML behaviors, and seed type definitions at deployment time. Internal-only, never internet-facing.

---

## Why Not lib-escrow? (Architectural Rationale)

lib-escrow provides the atomic exchange primitive -- deposit, consent, release/refund. lib-market could theoretically be "just use escrow for everything." The question arises: why a separate service?

**The answer is the same as Divine vs. Dungeon -- same infrastructure, different ceremony.**

| Concern | lib-escrow Ceremony | lib-market Ceremony |
|---------|---------------------|---------------------|
| **Identity** | Agreement with parties, deposits, allocations | Listing with seller, item, pricing, duration; Vendor with catalog, stock, personality |
| **Discovery** | Direct -- caller creates escrow with known parties | Search -- buyers browse listings, filter by category/price/realm |
| **Pricing** | Fixed -- parties agree on amounts before escrow | Dynamic -- auctions discover price through bidding; vendors set prices through formulas or GOAP |
| **Lifecycle** | Explicit consent flow (deposit/consent/release) | Timed lifecycle (listing duration, auto-settlement, restock intervals) |
| **NPC participation** | Party in an escrow like anyone else | Economic actor with catalog, personality, buy/sell decisions, stock management |
| **Analytics** | Per-agreement tracking | Aggregate price history, market-wide velocity, supply/demand signals |

lib-escrow's APIs (create agreement, deposit, consent, release) don't map to auction mechanics (list item, search, bid, buyout, settle). Vendor catalog management (stock, restock, buyback pricing) has no analogue in escrow. Forcing both into lib-escrow would bloat it with marketplace semantics or require auctions to shoehorn their operations into consent/deposit flows.

**What they share is infrastructure, not API surface**:
- Both use Currency for financial operations
- Both use Item/Inventory for goods management
- Auctions use Escrow internally for safe item custody during listing
- Both publish events consumed by analytics

lib-escrow remains the low-level exchange primitive. lib-market is the marketplace orchestration layer that composes Escrow (among other primitives) to deliver auction and vendor mechanics.

---

## Why Not lib-trade? (Concern Separation)

The economy architecture identifies three distinct services: **lib-market** (exchange), **lib-trade** (logistics), and **lib-economy** (intelligence). The question arises: why separate market from trade?

**Market is about EXCHANGE** (buying and selling at a point of sale). Trade is about **LOGISTICS** (moving goods across distances, borders, tariffs). A game can have:
- Markets without trade routes (local-only economy)
- Trade routes without formal markets (caravan-based direct trading)
- Both (markets at endpoints connected by trade routes)

| Concern | lib-market | lib-trade |
|---------|-----------|-----------|
| **Core question** | "What is this item worth here?" | "How do I move this item there?" |
| **Physical model** | Point of sale (stationary) | Route with legs (in motion) |
| **Time model** | Listing duration (hours/days) | Transit duration (game-time travel) |
| **Risk model** | Price risk (will it sell? at what price?) | Transit risk (bandits, storms, customs) |
| **NPC role** | Vendor (buy/sell) or bidder (auction) | Carrier, customs officer, bandit |
| **Currency flow** | Payment for goods | Tariffs, taxes, shipping costs |
| **Border concern** | None (local to a market) | Central (tariffs, contraband, customs) |

A blacksmith NPC buys iron at the market (lib-market), but a merchant NPC ships iron from the mines to the city (lib-trade). Different systems, different APIs, different NPC GOAP actions.

---

## The Auction House Subsystem

lib-market supports two fundamentally different exchange patterns. **Auction houses** are player/NPC-to-player/NPC exchanges mediated by escrow — items listed, bids placed, settlement orchestrated. **Vendor catalogs** are NPC-managed storefronts with pricing and stock — buy from vendor, sell to vendor, personality-driven behavior. Both models use the same underlying Currency/Item/Inventory/Escrow primitives but present different game-flavored APIs. A game can use either or both.

The auction house is a time-bounded, bid-driven exchange mechanism mediated by escrow for safe custody.

### Lifecycle

```
Seller lists item
 |
 +--> Listing fee deducted (Currency debit -- sink)
 +--> Item moved to escrow (Escrow deposit)
 +--> Listing saved with start price, optional buyout, duration
 |
 +--> ACTIVE
 | |
 | +--> Bid placed
 | | +--> Previous bidder's hold released (Currency hold release)
 | | +--> New bid amount reserved (Currency hold create)
 | | +--> Listing updated (currentBid, currentBidderId, bidCount)
 | | +--> Event: market.bid.placed
 | |
 | +--> Buyout executed (if buyout price set)
 | | +--> Buyer pays buyout price (Currency debit)
 | | +--> Transaction fee deducted from payment (sink)
 | | +--> Seller receives net amount (Currency credit, bypassEarnCap)
 | | +--> Item released from escrow to buyer (Escrow release)
 | | +--> All active bid holds released
 | | +--> Event: market.auction.sold
 | |
 | +--> Cancelled (only if no bids)
 | | +--> Item returned from escrow (Escrow refund)
 | | +--> Listing fee NOT refunded (deliberate sink)
 | | +--> Event: market.auction.cancelled
 | |
 | +--> Duration expires
 | |
 | +--> Has bids? --> Settlement
 | | +--> Winning bidder's hold captured (Currency hold capture)
 | | +--> Transaction fee deducted (sink)
 | | +--> Seller receives net amount (Currency credit)
 | | +--> Item released to winner (Escrow release)
 | | +--> Event: market.auction.sold
 | |
 | +--> No bids? --> Expired
 | +--> Item returned from escrow (Escrow refund)
 | +--> Event: market.auction.expired
 |
 +--> Price history updated on settlement/buyout
 +--> Event: market.price.changed (if average price shifted meaningfully)
```

### Bid Reservation via Currency Holds

Bids use lib-currency's authorization hold system, not actual debits. This is critical for auction integrity:

- **On bid**: Create a hold for the bid amount on the bidder's wallet. This reserves funds without moving them. The bidder can't spend reserved funds elsewhere.
- **On outbid**: Release the previous bidder's hold. Their funds are available again immediately.
- **On settlement**: Capture the winning bidder's hold (actual debit occurs). This is atomic with the escrow release.
- **On cancellation/expiration with no winner**: Release all active holds.

This pattern avoids the "double-spend" problem where a bidder commits funds to multiple auctions simultaneously and can't cover all wins. The hold system in lib-currency already handles this -- effective balance = actual balance minus sum of active holds.

### Settlement Background Worker

Auction settlement is background-processed via `MarketSettlementService`:

1. Periodically scans for listings past `expiresAt` that are still `Active`
2. For listings with bids: captures winning hold, credits seller, releases item, records transaction
3. For listings without bids: releases escrowed item back to seller
4. Handles settlement failures with retry and error event publication
5. Configurable batch size and processing interval

**Why background-processed**: Settlement involves multiple cross-service operations (hold capture, escrow release, currency credit). If any fails, compensating actions are needed. A background worker can retry systematically and publish error events for monitoring. Immediate settlement on expiration would require the bid-placing caller to wait for multi-service orchestration.

---

## The Vendor Subsystem

NPCs are economic actors, not UI facades. A vendor NPC's catalog, pricing, restock behavior, and buy/sell willingness emerge from the character's Actor brain running GOAP planning with `${market.*}` variables — not from static configuration tables. lib-market provides the data infrastructure (catalogs, stock levels, price history) that the Actor runtime consumes through the Variable Provider Factory pattern. The NPC decides what to stock, how to price, and whether to haggle. lib-market records the outcomes.

Vendor catalogs are NPC-managed storefronts. Unlike auctions (which are between parties), vendor operations are between a player/NPC and a vendor NPC's catalog.

### Vendor Catalog Architecture

```
VendorCatalog
 |
 +--> Owned by a Character (NPC) -- via characterId
 +--> Scoped to a Realm
 +--> Catalog type determines pricing behavior
 | +--> Static: prices fixed at creation
 | +--> Dynamic: prices adjust by formula
 | +--> PersonalityDriven: Actor GOAP sets prices
 |
 +--> Contains VendorItems
 |
 +--> Item template reference (what's for sale)
 +--> Prices (multi-currency support)
 +--> Stock (current, max, restock config)
 +--> Requirements (gating -- reputation, level, etc.)
 +--> Buyback configuration (what vendor pays for this item)
```

### Buy/Sell Flow

**Buying from vendor**:
1. Validate item in catalog and in stock
2. Validate buyer meets requirements (opaque -- game provides check results, not lib-market)
3. Validate buyer has sufficient funds
4. Debit buyer's wallet (Currency debit)
5. Decrement vendor stock
6. Create item instance in buyer's inventory (Item create + Inventory add)
7. Record transaction (with vendor as source)
8. Event: `market.vendor.sold`

**Selling to vendor**:
1. Validate vendor has buyback entry for the item template
2. Calculate buyback price (base price * buyback multiplier)
3. Validate vendor has sufficient funds (vendor has a wallet too)
4. Transfer item from seller's inventory to vendor stock (Inventory transfer or destroy + restock)
5. Credit seller's wallet (Currency credit)
6. Debit vendor's wallet (Currency debit)
7. Record transaction
8. Event: `market.vendor.purchased`

### NPC Vendor Autonomy

The three pricing modes represent increasing NPC sophistication:

| Mode | How Prices Set | Who Decides | Use Case |
|------|---------------|-------------|----------|
| **Static** | At catalog creation | Game designer / admin | Simple shops, tutorial vendors, system stores |
| **Dynamic** | Formula: `basePrice * supplyModifier * demandModifier * regionModifier` | Formula engine | Mid-complexity games, vendors that respond to world state |
| **Personality-driven** | NPC Actor brain via GOAP planning, writes prices through API | NPC's own Actor | Full Arcadia experience -- autonomous merchant NPCs |

For personality-driven vendors, the flow is:

```
NPC Actor (running ABML behavior)
 |
 +--> Queries ${market.my_catalog} for current stock levels
 +--> Queries ${market.price_history} for recent sale prices
 +--> Queries ${personality.greed} for pricing disposition
 +--> Queries ${economy.supply.*} for regional supply data
 |
 +--> GOAP evaluates goals:
 | +--> maintain_wealth: ensure profit margin
 | +--> restock_shop: acquire depleted items
 | +--> attract_customers: competitive pricing
 |
 +--> GOAP selects action:
 | +--> adjust_prices: call /market/vendor/item/update-price
 | +--> restock: call /market/vendor/item/restock
 | +--> close_shop: set catalog status to closed
 |
 +--> lib-market records the price change
 +--> Next customer sees updated prices
```

### Vendor Restock Mechanics

Vendors have configurable restock behavior:

- **Unlimited stock**: `maxStock: null` -- never runs out (system stores, infinite supply vendors)
- **Fixed restock**: `restockInterval: PT4H` -- stock replenishes to max every 4 hours via background worker
- **Purchase-driven restock**: Vendor NPC Actor buys materials from other vendors or market, converts to finished goods, stocks them. lib-market tracks stock levels; the Actor drives procurement.
- **Manual restock**: Game server or NPC Actor calls restock endpoint directly

The `MarketRestockService` background worker handles periodic restock for vendors with `restockInterval` set. It does NOT handle personality-driven restock -- that's the NPC Actor's responsibility.

---

## Price Discovery

Economic deities (Hermes/Commerce, Laverna/Thieves) monitor market health through analytics events published by lib-market (listings created, auctions sold, prices changed). When velocity stagnates or overheats, divine actors spawn narrative events that affect NPC economic behavior — a traveling merchant appears, a trade festival is announced, a robbery disrupts hoarding. lib-market sees these as normal NPC transactions; the divine intervention is invisible at the market layer. This is the same indirect influence pattern used throughout the system: gods act through the world, not on it.

lib-market maintains aggregate price data for NPC economic intelligence and divine intervention:

### Price History Model

```
PriceHistoryEntry:
 templateId: uuid # What item
 realmId: uuid # Where
 marketDefinitionId: uuid # Which market
 periodStart: timestamp # Time bucket start
 periodEnd: timestamp # Time bucket end
 granularity: enum # Hour | Day | Week
 averagePrice: decimal # Mean sale price in the period
 medianPrice: decimal # Median sale price
 minPrice: decimal # Lowest sale price
 maxPrice: decimal # Highest sale price
 volume: integer # Number of transactions
 totalValue: decimal # Sum of all transaction values
 currencyDefinitionId: uuid # Which currency these prices are in
```

Price history is updated on every successful auction settlement and vendor sale. The background worker aggregates raw transactions into time-bucketed entries at configurable granularity.

### Price Change Events

When the rolling average price for an item shifts meaningfully (configurable threshold -- default 5%), lib-market publishes `market.price.changed`. This event feeds:

- **NPC GOAP decisions**: Vendors and economic NPCs adjust behavior (buy low, sell high, restock scarce items, dump surplus)
- **Divine economic actors**: Economic deities observe price trends for velocity monitoring
- **Analytics**: Aggregate economic health metrics

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Market definitions (MySQL), listings (MySQL), bids (Redis), vendors (MySQL), vendor stock (Redis), price history (MySQL), settlement queue (Redis), idempotency (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Listing mutation locks, bid placement locks, vendor stock locks, settlement locks |
| lib-messaging (`IMessageBus`) | Publishing all market events (listing, bid, sale, vendor, price change); error event publishing via `TryPublishErrorAsync` |
| lib-currency (`ICurrencyClient`) | Listing fee deduction, bid hold creation/release/capture, vendor buy/sell payments, transaction fee deduction, seller proceeds credit (L2) |
| lib-item (`IItemClient`) | Item instance validation for listings, item creation for vendor purchases (L2) |
| lib-inventory (`IInventoryClient`) | Item movement between seller/buyer/vendor inventories (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence for market scoping (L2) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration (L1) |
| lib-character (`ICharacterClient`) | Vendor NPC existence validation (L2) |
| lib-location (`ILocationClient`) | Market location validation for market definitions (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-escrow (`IEscrowClient`) | Item custody during active auction listings | Auctions disabled; vendor buy/sell still works (no custody needed for immediate purchase). Returns `ServiceUnavailable` for auction endpoints. |
| lib-analytics (`IAnalyticsClient`) | Publishing economic events for velocity tracking | Market operates without analytics integration; price history still maintained locally |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Market is a new L4 service with no current consumers. Future dependents: lib-economy (queries price data for velocity monitoring), lib-trade (markets at route endpoints), NPC Actor behaviors (economic GOAP via Variable Provider Factory), divine actors (economic deity intervention via analytics events) |

---

## State Storage

### Market Definition Store
**Store**: `market-definitions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `market:{marketId}` | `MarketDefinitionModel` | Primary lookup by market ID. Stores name, code, realm, location, status, fee configuration (listing fee, transaction fee rate), supported currencies, listing duration options. |
| `market-code:{gameServiceId}:{code}` | `MarketDefinitionModel` | Code-uniqueness lookup within game service scope |

### Listing Store
**Store**: `market-listings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `listing:{listingId}` | `AuctionListingModel` | Primary lookup by listing ID. Stores seller info, item reference, pricing (start/buyout/current bid), timing, status, fee amounts, escrow reference, winning bidder. |

Paginated queries by marketId, status, category, price range, seller use `IJsonQueryableStateStore<AuctionListingModel>.JsonQueryPagedAsync()`.

### Bid Store
**Store**: `market-bids` (Backend: Redis, prefix: `market:bid`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bid:{bidId}` | `AuctionBidModel` | Individual bid record: listing, bidder, amount, hold reference, status |
| `bid-listing:{listingId}` | `List<string>` (JSON) | Bid IDs for a listing (ordered by amount descending) |
| `bid-bidder:{bidderId}` | `List<string>` (JSON) | Active bid IDs for a bidder (for cleanup on outbid/settlement) |

### Vendor Store
**Store**: `market-vendors` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `vendor:{vendorId}` | `VendorCatalogModel` | Primary lookup by vendor ID. Stores NPC character reference, realm, catalog type (Static/Dynamic/PersonalityDriven), restock config, wallet reference, status. |
| `vendor-char:{characterId}` | `VendorCatalogModel` | Vendor lookup by owning NPC character |

### Vendor Stock Store
**Store**: `market-vendor-stock` (Backend: Redis, prefix: `market:stock`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `stock:{vendorId}:{templateId}` | `VendorStockModel` | Current stock level, prices, buyback config, requirements, last restock timestamp |
| `stock-list:{vendorId}` | `List<string>` (JSON) | Template IDs stocked by this vendor |

### Price History Store
**Store**: `market-price-history` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `price:{templateId}:{realmId}:{granularity}:{periodStart}` | `PriceHistoryEntryModel` | Time-bucketed price statistics per item per realm |

Paginated queries by templateId, realmId, granularity, date range use `IJsonQueryableStateStore<PriceHistoryEntryModel>.JsonQueryPagedAsync()`.

### Settlement Queue Store
**Store**: `market-settlement-queue` (Backend: Redis, prefix: `market:settle`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `settle:{listingId}` | `SettlementQueueEntry` | Listings awaiting background settlement (expired with bids) |

### Idempotency Store
**Store**: `market-idempotency` (Backend: Redis, prefix: `market:idemp`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `idemp:{idempotencyKey}` | `IdempotencyRecord` | Deduplication for bid placement, vendor purchases, listing creation |

### Distributed Locks
**Store**: `market-lock` (Backend: Redis, prefix: `market:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `listing:{listingId}` | Listing mutation lock (bid, buyout, cancel, settle) |
| `vendor:{vendorId}` | Vendor catalog mutation lock |
| `stock:{vendorId}:{templateId}` | Vendor stock modification lock |
| `settlement-worker` | Settlement background worker singleton lock |
| `restock-worker` | Restock background worker singleton lock |
| `price-aggregation-worker` | Price aggregation background worker singleton lock |

### Type Field Classification

Every polymorphic "type" or "kind" field in the Market domain falls into one of three categories:

| Field | Model(s) | Cat | Values / Source | Rationale |
|-------|----------|-----|-----------------|-----------|
| `MarketEntityType` | `AuctionListingModel` (seller), `AuctionBidModel` (bidder), settlement (buyer) | A | *(see DC#9 below)* | Identifies what kind of entity is transacting. Needs classification resolution -- see Design Considerations. |
| `catalogType` | `VendorCatalogModel` | C | `Static`, `Dynamic`, `PersonalityDriven` | Finite pricing modes the vendor subsystem implements. Service-owned enum (`CatalogType`). |
| `ListingStatus` | `AuctionListingModel` | C | `Active`, `Sold`, `Cancelled`, `Expired` | Finite auction lifecycle states. Service-owned enum (`ListingStatus`). |
| `MarketDefinitionStatus` | `MarketDefinitionModel` | C | `Active`, `Suspended`, `Closed` | Finite market lifecycle states. Service-owned enum. |
| `VendorStatus` | `VendorCatalogModel` | C | `Open`, `Closed`, `Suspended` | Finite vendor lifecycle states. Service-owned enum. |
| `BidStatus` | `AuctionBidModel` | C | `Active`, `Outbid`, `Won`, `Released`, `Expired` | Finite bid lifecycle states. Service-owned enum. |
| `PriceGranularity` | `PriceHistoryEntryModel` | C | `Hour`, `Day`, `Week` | Finite time-bucket sizes for price aggregation. Service-owned enum (`PriceGranularity`). |
| `AuctionSortOrder` | Auction search request | C | `PriceAscending`, `PriceDescending`, `TimeRemaining`, `BidCount` | Finite sort modes for listing queries. Service-owned enum (`AuctionSortOrder`). |
| `PriceTrend` | Variable provider output | C | `Up`, `Down`, `Stable` | Finite trend directions for NPC GOAP consumption. Service-owned enum (`PriceTrend`). |
| `SupplySignal` | Variable provider output | C | `Scarce`, `Normal`, `Abundant` | Finite supply indicators for NPC GOAP consumption. Service-owned enum (`SupplySignal`). |

**Category key**: **A** = Entity Reference (`EntityType` enum or entity-identifying enum), **B** = Content Code (opaque string, game-configurable), **C** = System State (service-owned enum, finite).

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `market.listing.created` | `MarketListingCreatedEvent` | Auction listing created (item escrowed, fee paid) |
| `market.listing.cancelled` | `MarketListingCancelledEvent` | Listing cancelled by seller (no bids, item returned) |
| `market.bid.placed` | `MarketBidPlacedEvent` | Bid placed on listing (hold created, previous bidder released) |
| `market.auction.sold` | `MarketAuctionSoldEvent` | Auction settled (via buyout or expiration with bids) |
| `market.auction.expired` | `MarketAuctionExpiredEvent` | Listing expired with no bids (item returned) |
| `market.vendor.sold` | `MarketVendorSoldEvent` | Player/NPC purchased from vendor catalog |
| `market.vendor.purchased` | `MarketVendorPurchasedEvent` | Vendor bought item from player/NPC (buyback) |
| `market.vendor.restocked` | `MarketVendorRestockedEvent` | Vendor stock replenished (background or manual) |
| `market.vendor.price-changed` | `MarketVendorPriceChangedEvent` | Vendor adjusted item price (dynamic/personality-driven) |
| `market.price.changed` | `MarketPriceChangedEvent` | Rolling average price shifted beyond threshold |
| `market.definition.created` | `MarketDefinitionCreatedEvent` | Market definition created (x-lifecycle auto-generated, `topic_prefix: market`) |
| `market.definition.updated` | `MarketDefinitionUpdatedEvent` | Market definition updated (x-lifecycle auto-generated, `topic_prefix: market`) |
| `market.definition.deleted` | `MarketDefinitionDeletedEvent` | Market definition deleted (x-lifecycle auto-generated, `topic_prefix: market`) |
| `market.vendor.created` | `MarketVendorCreatedEvent` | Vendor catalog created (x-lifecycle auto-generated, `topic_prefix: market`) |
| `market.vendor.updated` | `MarketVendorUpdatedEvent` | Vendor catalog updated (x-lifecycle auto-generated, `topic_prefix: market`) |
| `market.vendor.deleted` | `MarketVendorDeletedEvent` | Vendor catalog deleted (x-lifecycle auto-generated, `topic_prefix: market`) |
| `market.settlement.failed` | `MarketSettlementFailedEvent` | Settlement worker failed to settle a listing (error event) |

**x-lifecycle entities** (in `market-events.yaml`, with `topic_prefix: market`):
- **MarketDefinition**: `marketId`, `gameServiceId`, `code`, `name`, `realmId`, `locationId`, `status`, `listingFee`, `transactionFeeRate`, `supportedCurrencies`
- **VendorCatalog** (as `MarketVendor`): `vendorId`, `characterId`, `gameServiceId`, `realmId`, `catalogType`, `status`, `walletId`

Listings use custom events (not x-lifecycle) because listing lifecycle transitions carry domain-specific semantics (sold vs expired vs cancelled with escrow/fee context) that don't fit the standard lifecycle payload.

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `currency.hold.expired` | `HandleHoldExpiredAsync` | If hold was for an active bid, release the bid record and update listing. Currency hold expiration acts as bid timeout. |

**x-event-subscriptions** (for `market-events.yaml`):
```yaml
x-event-subscriptions:
 - topic: currency.hold.expired
 event: CurrencyHoldExpiredEvent
 handler: HandleHoldExpired
```

**x-event-publications**: All 17 published events above (6 x-lifecycle + 11 custom) must be listed in the `x-event-publications` block in `market-events.yaml`.

### Resource Cleanup

| Target Resource | Source Type | Field | On Delete | Cleanup Endpoint | Payload Template |
|----------------|-------------|-------|-----------|-----------------|------------------|
| character | market | characterId | CASCADE | `/market/cleanup-by-character` | `{"characterId": "{{resourceId}}"}` |
| realm | market | realmId | CASCADE | `/market/cleanup-by-realm` | `{"realmId": "{{resourceId}}"}` |
| game-service | market | gameServiceId | CASCADE | `/market/cleanup-by-game-service` | `{"gameServiceId": "{{resourceId}}"}` |

Item templates are Category B (no delete endpoint). Market does not register item template references with lib-resource. If item template safe deletion is implemented in the future, Market cleanup callbacks should be added.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultListingFee` | `MARKET_DEFAULT_LISTING_FEE` | `1.0` | Default listing fee when market definition doesn't specify one |
| `DefaultTransactionFeeRate` | `MARKET_DEFAULT_TRANSACTION_FEE_RATE` | `0.05` | Default transaction fee rate (5%) on successful sales |
| `DefaultListingDurationShortHours` | `MARKET_DEFAULT_LISTING_DURATION_SHORT_HOURS` | `12` | Short listing duration in hours |
| `DefaultListingDurationMediumHours` | `MARKET_DEFAULT_LISTING_DURATION_MEDIUM_HOURS` | `24` | Medium listing duration in hours |
| `DefaultListingDurationLongHours` | `MARKET_DEFAULT_LISTING_DURATION_LONG_HOURS` | `48` | Long listing duration in hours |
| `MinBidIncrementRate` | `MARKET_MIN_BID_INCREMENT_RATE` | `0.05` | Minimum bid increment as fraction of current bid (5%) |
| `SettlementProcessingIntervalSeconds` | `MARKET_SETTLEMENT_PROCESSING_INTERVAL_SECONDS` | `30` | How often the settlement worker checks for expired listings |
| `SettlementBatchSize` | `MARKET_SETTLEMENT_BATCH_SIZE` | `100` | Maximum listings settled per worker cycle |
| `SettlementMaxRetries` | `MARKET_SETTLEMENT_MAX_RETRIES` | `3` | Max retries for failed settlement operations |
| `RestockProcessingIntervalSeconds` | `MARKET_RESTOCK_PROCESSING_INTERVAL_SECONDS` | `60` | How often the restock worker checks vendors |
| `RestockBatchSize` | `MARKET_RESTOCK_BATCH_SIZE` | `200` | Maximum vendor stock entries restocked per cycle |
| `PriceAggregationIntervalSeconds` | `MARKET_PRICE_AGGREGATION_INTERVAL_SECONDS` | `300` | How often raw transactions are aggregated into price history |
| `PriceChangeThresholdRate` | `MARKET_PRICE_CHANGE_THRESHOLD_RATE` | `0.05` | Minimum average price shift (5%) to publish price.changed event |
| `PriceHistoryRetentionDays` | `MARKET_PRICE_HISTORY_RETENTION_DAYS` | `90` | Days of price history retained before pruning |
| `IdempotencyTtlSeconds` | `MARKET_IDEMPOTENCY_TTL_SECONDS` | `3600` | Idempotency key expiry for bids and purchases |
| `MaxActiveListingsPerSeller` | `MARKET_MAX_ACTIVE_LISTINGS_PER_SELLER` | `50` | Maximum concurrent active listings per seller entity |
| `MaxBidsPerBidder` | `MARKET_MAX_BIDS_PER_BIDDER` | `100` | Maximum concurrent active bids per bidder entity |
| `BidHoldDurationDays` | `MARKET_BID_HOLD_DURATION_DAYS` | `3` | Duration of currency hold for bid reservation |
| `DistributedLockTimeoutSeconds` | `MARKET_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |
| `DefaultBuybackMultiplier` | `MARKET_DEFAULT_BUYBACK_MULTIPLIER` | `0.25` | Default fraction of base price vendors offer for buyback (25%) |

**Removed**: `VendorWalletOwnerType` -- Currency's wallet API uses `EntityType` enum, not free-form strings. Vendor wallets should use the appropriate `EntityType` value (likely `Character`, since vendor NPCs are characters). See DC#11.

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<MarketService>` | Structured logging |
| `MarketServiceConfiguration` | Typed configuration access (22 properties) |
| `IStateStoreFactory` | State store access (creates 9 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ICurrencyClient` | Fee deduction, bid holds, vendor payments, seller proceeds (L2 hard) |
| `IItemClient` | Item validation, instance creation for vendor sales (L2 hard) |
| `IInventoryClient` | Item movement between parties (L2 hard) |
| `IGameServiceClient` | Game service validation (L2 hard) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1 hard) |
| `ICharacterClient` | Vendor NPC existence validation (L2 hard) |
| `ILocationClient` | Market location validation (L2 hard) |
| `ITelemetryProvider` | Telemetry span creation for all async methods (L0) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (Escrow, Analytics) |

### Background Workers

| Worker | Purpose | Interval Config | Lock Key |
|--------|---------|-----------------|----------|
| `MarketSettlementService` | Settles expired auction listings -- captures winning bids, releases escrowed items, credits sellers, handles failed settlements | `SettlementProcessingIntervalSeconds` (30s) | `market:lock:settlement-worker` |
| `MarketRestockService` | Replenishes vendor stock for vendors with `restockInterval` configured -- sets stock to maxStock, publishes restock events | `RestockProcessingIntervalSeconds` (60s) | `market:lock:restock-worker` |
| `MarketPriceAggregationService` | Aggregates raw transaction records into time-bucketed price history entries, detects and publishes meaningful price changes, prunes old history | `PriceAggregationIntervalSeconds` (300s) | `market:lock:price-aggregation-worker` |

All workers acquire distributed locks before processing to ensure multi-instance safety (only one instance processes at a time).

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `MarketCatalogVariableProviderFactory` | `${market.*}` | Vendor's own catalog stock levels, recent sales velocity, current pricing, buyback availability | `IVariableProviderFactory` (DI singleton) |
| `MarketPriceVariableProviderFactory` | `${market-price.*}` | Price history for items in a realm -- average, trend direction, supply/demand signals | `IVariableProviderFactory` (DI singleton) |

**`${market.*}` variables** (vendor-scoped, for NPC merchant GOAP):
- `${market.my_stock.{templateCode}}` -- current stock level for an item
- `${market.my_stock_ratio.{templateCode}}` -- current/max stock ratio (0.0 = empty, 1.0 = full)
- `${market.my_recent_sales}` -- number of sales in last time window
- `${market.my_revenue}` -- revenue in last time window
- `${market.my_price.{templateCode}}` -- current price set for an item
- `${market.competitor_price.{templateCode}}` -- average price at other vendors in same location

**`${market-price.*}` variables** (realm-scoped, for any economic NPC GOAP):
- `${market-price.average.{templateCode}}` -- rolling average price
- `${market-price.trend.{templateCode}}` -- price trend direction (Up/Down/Stable)
- `${market-price.volume.{templateCode}}` -- recent transaction volume
- `${market-price.supply.{templateCode}}` -- supply signal (Scarce/Normal/Abundant based on listing count)

---

## API Endpoints (Implementation Notes)

### Market Definition Management (5 endpoints)

All endpoints: `x-permissions: [{role: developer}]`.

- **Create** (`/market/definition/create`): Validates game service existence. Validates code uniqueness per game service. Validates location existence if locationId provided (L2 hard). Saves definition with fee config and supported currencies. Publishes `market.definition.created`.
- **Get** (`/market/definition/get`): Load from MySQL by marketId. 404 if not found.
- **GetByCode** (`/market/definition/get-by-code`): JSON query by gameServiceId + code. 404 if not found.
- **List** (`/market/definition/list`): Paged JSON query with required gameServiceId filter, optional realmId, status, and locationId filters.
- **Update** (`/market/definition/update`): Acquires distributed lock. Partial update -- fee rates, name, description, status, supported currencies. Publishes `market.definition.updated`.

### Auction House Operations (6 endpoints)

All endpoints: `x-permissions: []` (service-to-service only -- called by game engine/Actor, not directly by WebSocket clients). See DC#10 if player-facing access is needed.

- **CreateListing** (`/market/auction/create`): Validates item exists, is tradeable (soulbound check), and seller owns it. Validates seller hasn't exceeded `MaxActiveListingsPerSeller`. Calculates listing fee. Deducts listing fee (Currency debit -- sink, not to market). Creates escrow for item custody. Moves item to escrow. Saves listing record. Publishes `market.listing.created`. Returns listing with escrow details.
- **Search** (`/market/auction/search`): Paged JSON query on `market-listings` with filters: realmId (required), marketId (optional), category, search text (item name), price range (min/max), quality/rarity, sort order (PriceAscending, PriceDescending, TimeRemaining, BidCount). Returns listing summaries with current bid info.
- **PlaceBid** (`/market/auction/bid`): Idempotency-protected. Validates listing is Active and not expired. Validates bid >= startPrice (first bid) or >= currentBid * (1 + MinBidIncrementRate). Validates bidder != seller. Acquires listing lock. Creates currency hold for bid amount. If outbidding: releases previous bidder's hold. Updates listing (currentBid, currentBidderId, bidCount). Saves bid record. Publishes `market.bid.placed`.
- **Buyout** (`/market/auction/buyout`): Validates listing has buyout price and is Active. Acquires listing lock. Debits buyer's wallet for buyout amount. Calculates and deducts transaction fee (sink). Credits seller's wallet (net = buyout - fee, bypassEarnCap=true). Releases item from escrow to buyer's inventory. Releases all active bid holds for other bidders. Sets listing status to Sold. Records price history datapoint. Publishes `market.auction.sold`.
- **Cancel** (`/market/auction/cancel`): Validates listing is Active and has no bids (returns 422 if bids exist). Acquires listing lock. Releases item from escrow to seller. Sets status to Cancelled. Listing fee NOT refunded. Publishes `market.listing.cancelled`.
- **GetListing** (`/market/auction/get`): Load listing by listingId. Returns full listing details with bid history summary (count, highest, bid timestamps).

### Vendor Operations (7 endpoints)

CreateVendor/UpdateVendor: `x-permissions: [{role: developer}]`. GetVendor/GetVendorByCharacter/GetCatalog/Buy/Sell: `x-permissions: []` (service-to-service only). See DC#10 if player-facing access is needed.

- **CreateVendor** (`/market/vendor/create`): Validates game service and realm. Validates character existence (L2 hard). Creates vendor currency wallet via `ICurrencyClient` (owner = vendorId, ownerType = vendor). Saves catalog model. Returns vendor with empty stock list.
- **GetVendor** (`/market/vendor/get`): Load vendor by vendorId. Enriches with current stock summary from Redis.
- **GetVendorByCharacter** (`/market/vendor/get-by-character`): Lookup by characterId. 404 if character has no vendor catalog.
- **GetCatalog** (`/market/vendor/catalog`): Returns full vendor catalog -- all stocked items with current prices, stock levels, buyback info, and requirement summaries. This is the player-facing "browse shop" endpoint.
- **Buy** (`/market/vendor/buy`): Validates item in stock and quantity available. Validates buyer meets requirements (opaque -- request includes `requirementsMet: boolean`, caller validates). Validates buyer has sufficient funds. Acquires stock lock. Debits buyer wallet. Credits vendor wallet. Decrements stock. Creates item instance in buyer inventory. Records transaction. Publishes `market.vendor.sold`.
- **Sell** (`/market/vendor/sell`): Validates vendor has buyback entry for template. Calculates buyback price. Validates vendor wallet has sufficient funds (vendor can run out of money). Acquires stock lock. Transfers item from seller to vendor (or destroys and restocks, configurable per vendor). Debits vendor wallet. Credits seller wallet. Records transaction. Publishes `market.vendor.purchased`.
- **UpdateVendor** (`/market/vendor/update`): Acquires lock. Updates catalog metadata (type, restock config, status). Does not modify stock -- use stock endpoints for that.

### Vendor Stock Management (4 endpoints)

All endpoints: `x-permissions: []` (service-to-service only -- called by NPC Actor GOAP action handlers and game engine, not directly by WebSocket clients).

- **SetStock** (`/market/vendor/stock/set`): Acquires stock lock (`stock:{vendorId}:{templateId}`). Sets stock entry for a template -- prices, max stock, current stock, buyback config, requirements. Creates if new, updates if existing.
- **UpdatePrice** (`/market/vendor/stock/update-price`): Updates price for a specific stock entry. Used by personality-driven vendors via Actor GOAP. Publishes `market.vendor.price-changed`.
- **Restock** (`/market/vendor/stock/restock`): Manually restock a specific item to maxStock (or specified quantity). Publishes `market.vendor.restocked`.
- **RemoveStock** (`/market/vendor/stock/remove`): Removes a stock entry from the vendor catalog entirely.

### Price Analytics (3 endpoints)

All endpoints: `x-permissions: []` (service-to-service only -- consumed by NPC GOAP, divine actors, and admin tooling).

- **GetAveragePrice** (`/market/price/average`): Returns rolling average, min, max, median, volume for a template in a realm over a specified period. Reads from aggregated price history.
- **GetPriceHistory** (`/market/price/history`): Returns time-bucketed price data points for a template in a realm at specified granularity (hour/day/week). Paginated. Used by NPC GOAP and admin dashboards.
- **GetMarketStats** (`/market/stats`): Aggregate market health metrics for a market definition -- total active listings, total volume, average listing duration, sell-through rate, top traded items.

### Cleanup Endpoints (3 endpoints)

All endpoints: `x-permissions: []` (service-to-service only -- called by lib-resource cleanup callbacks).

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByCharacter** (`/market/cleanup-by-character`): Cancel all active listings by this seller (release escrowed items). Release all active bid holds for this bidder. Remove vendor catalog if character was a vendor NPC.
- **CleanupByRealm** (`/market/cleanup-by-realm`): Cancel all active listings in realm markets. Release all associated bid holds. Remove all vendor catalogs in the realm. Delete price history for the realm.
- **CleanupByGameService** (`/market/cleanup-by-game-service`): Delete all market definitions for the game service. Cascade: cancel listings, release holds, remove vendors, delete price history.

---

## Visual Aid

Market identity (auction houses, vendor catalogs, market locations) is owned here. Item custody during auctions is Escrow. Financial operations (fees, bids, payments) are Currency. Item definitions and instances are Item. Item placement is Inventory. Bid reservation is Currency authorization holds. Auction settlement is a background worker coordinating Escrow release. NPC vendor behavior is Actor (via the Variable Provider Factory pattern). Price analytics feed NPC GOAP decisions and divine economic intervention. lib-market orchestrates the ceremony connecting these primitives.

### Market Architecture

```
+-----------------------------------------------------------------------+
| Market Service Composability |
+-----------------------------------------------------------------------+
| |
| lib-market (L4) -- "Where exchange happens" |
| +------------------+ +------------------+ +------------------+ |
| | MarketDefinition | | AuctionListing | | VendorCatalog | |
| | (where, rules, | | (what's for sale,| | (NPC shop, | |
| | fees) | | bids, timing) | | stock, prices) | |
| +--------+---------+ +--------+---------+ +--------+---------+ |
| | | | |
| +----------+-----------+----------+-----------+ |
| | | |
| v v |
| +-------------------------------------------------------------+ |
| | Existing Primitives (L0/L1/L2) | |
| | | |
| | Currency ---- listing fees (sink), bid holds, payments, | |
| | vendor wallets, seller proceeds | |
| | Item -------- item validation, instance creation | |
| | Inventory --- item movement (seller -> escrow -> buyer, | |
| | vendor <-> customer) | |
| | Character --- NPC vendor existence validation | |
| | Location ---- market location validation | |
| | Resource ---- cleanup coordination on entity deletion | |
| +-------------------------------------------------------------+ |
| | |
| v soft dependencies (L4) |
| +-------------------------------------------------------------+ |
| | Optional Features (L4, graceful degradation) | |
| | | |
| | Escrow ------- item custody during active auction listings | |
| | Analytics ---- economic velocity event publishing | |
| +-------------------------------------------------------------+ |
| |
| Background Workers |
| +-------------------+ +-------------------+ +---------------------+ |
| | SettlementService | | RestockService | | PriceAggregation | |
| | Expired listings | | Periodic vendor | | Raw transactions | |
| | -> settle/return | | stock replenish | | -> time-bucketed | |
| +-------------------+ +-------------------+ | price history | |
| +---------------------+ |
+-----------------------------------------------------------------------+


Auction Flow (Happy Path)
===========================

 Seller lib-market Escrow / Currency
 | | |
 |-- CreateListing -------->| |
 | (item, startPrice, |-- Debit listing fee ------->|
 | buyoutPrice, duration) | (Currency, SINK) |
 | |-- Create escrow ----------->|
 | |-- Deposit item to escrow -->|
 | | |
 |<-- listing + escrowRef --| |
 | | |
 Bidder A | |
 |-- PlaceBid (100g) ----->| |
 | |-- Create hold (100g) ------>|
 |<-- bid confirmed -------| |
 | | |
 Bidder B | |
 |-- PlaceBid (120g) ----->| |
 | |-- Release A's hold -------->|
 | |-- Create hold (120g) ------>|
 |<-- bid confirmed -------| |
 | | |
 [Listing expires] | |
 | Settlement Worker |
 | |-- Capture B's hold (120g)->|
 | | (Currency debit) |
 | |-- Deduct 5% fee (6g SINK)->|
 | |-- Credit seller (114g) --->|
 | | (bypassEarnCap) |
 | |-- Release item to buyer -->|
 | | (Escrow release) |
 | | |
 Seller gets 114g Bidder B gets item |
 Bidder A's hold released 6g removed from circulation |


NPC Vendor Economic Cycle
===========================

 NPC Actor (GOAP Brain) lib-market lib-currency
 | | |
 |-- Query ${market.*} ----->| |
 | "What's my stock?" | |
 |<-- stock levels ----------| |
 | | |
 |-- GOAP: "Need to restock iron swords" |
 | | |
 |-- Buy from supplier NPC --| |
 | (another vendor/market) |-- Debit vendor ---->|
 | |-- Credit supplier -->|
 | | |
 |-- SetStock(iron_sword) -->| |
 | price: based on cost | |
 | + personality.greed | |
 | | |
 Customer (Player/NPC) | |
 |-- Buy(iron_sword) ------>| |
 | |-- Debit customer --->|
 | |-- Credit vendor ---->|
 | |-- Create item ------>|
 | |-- Decrement stock |
 |<-- itemInstanceId --------| |
 | | |
 [Price aggregation worker runs] | |
 | |-- Update price hist |
 | |-- Avg shifted >5%? |
 | | publish price.changed
 | | |
 Economic Deity (God Actor) | |
 |-- Observes price.changed --| |
 | via analytics events | |
 |-- Decides: "Iron too | |
 | expensive, spawn new | |
 | iron mine event" | |
 | | |
 [New mine -> more iron -> vendors | |
 acquire cheaper iron -> prices | |
 adjust naturally via NPC GOAP] | |
```

---

## Tenet Compliance Notes

### String Fields Requiring Enum Definitions (PascalCase)

When creating `market-api.yaml`, the following fields MUST be defined as proper enum types with PascalCase values:

| Field | Used In | Values | Required Enum |
|-------|---------|--------|---------------|
| Catalog type | `VendorCatalogModel` | `Static`, `Dynamic`, `PersonalityDriven` | `CatalogType` |
| Listing status | `AuctionListingModel` | `Active`, `Sold`, `Cancelled`, `Expired` | `ListingStatus` |
| Market definition status | `MarketDefinitionModel` | `Active`, `Suspended`, `Closed` | `MarketDefinitionStatus` |
| Vendor status | `VendorCatalogModel` | `Open`, `Closed`, `Suspended` | `VendorStatus` |
| Price history granularity | `PriceHistoryEntryModel` | `Hour`, `Day`, `Week` | `PriceGranularity` |
| Search sort order | Auction search request | `PriceAscending`, `PriceDescending`, `TimeRemaining`, `BidCount` | `AuctionSortOrder` |
| Seller/bidder/buyer type | `AuctionListingModel`, `AuctionBidModel` | *(see DC#9)* | `MarketEntityType` or `$ref: EntityType` |
| Price trend | Variable provider | `Up`, `Down`, `Stable` | `PriceTrend` |
| Supply signal | Variable provider | `Scarce`, `Normal`, `Abundant` | `SupplySignal` |
| Bid status | `AuctionBidModel` | `Active`, `Outbid`, `Won`, `Released`, `Expired` | `BidStatus` |

### Dependency Classification (Corrected)

Character (L2) and Location (L2) were originally listed as soft dependencies with graceful degradation. Per SERVICE-HIERARCHY.md: when L4 is enabled, ALL of L2 must be running. L2 dependencies MUST be hard (constructor injection, crash at startup if missing). These have been moved to the hard dependencies table in this document.

Only Escrow (L4) and Analytics (L4) remain as soft dependencies, which is correct -- L4-to-L4 dependencies require graceful degradation.

### Deprecation Lifecycle

MarketDefinition does not require deprecation. Per the IMPLEMENTATION TENETS T31 decision tree: no external service stores `marketDefinitionId` — all references (listings, vendors, price history) are internal to lib-market. Internal references are cleaned up via cascade on deletion. Immediate hard delete with internal cascade is the correct pattern. If a future external service stores `marketDefinitionId`, this classification should be revisited (likely Category A). Listings use custom lifecycle events (not x-lifecycle) because their transitions carry domain-specific semantics (sold vs expired vs cancelled). VendorCatalog uses x-lifecycle for standard CRUD events.

### Resource Cleanup (Compliant)

This document correctly defines cleanup endpoints for character, realm, and game-service entity types with CASCADE policy via lib-resource. No gaps identified.

### Event Flow Direction (Compliant)

All consumed events flow in the correct direction: Market (L4) subscribes to `currency.hold.expired` from Currency (L2). Higher-layer consuming lower-layer events is the correct pattern.

All published events are consumed by same-layer or observing services (Analytics L4, divine actors via events). No lower-layer services are listed as consumers.

### Event Topic Naming (Pattern C)

Market is a multi-entity service. All event topics use Pattern C (`market.{entity}.{action}`). The `x-lifecycle` block uses `topic_prefix: market` to generate correct Pattern C topics for `MarketDefinition` and `VendorCatalog` entities.

### Endpoint Count

Planned: 5 (definition) + 6 (auction) + 7 (vendor) + 4 (stock) + 3 (price) + 3 (cleanup) = **28 endpoints**. No schema exists yet — GENERATED-COMPOSITION-REFERENCE.md currently shows "—" for Market.

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 0: Foundation Prerequisites

Before lib-market implementation:
- lib-escrow must complete asset movement (deposit/release/refund actually move items/currency)
- lib-escrow must complete contract integration
- lib-item #407 (item decay/expiration) is desirable but not blocking

### Phase 1: Core Infrastructure (Market Definitions + Auction Basics)

- Create market-api.yaml schema with all endpoints
- Create market-events.yaml schema
- Create market-configuration.yaml schema
- Generate service code
- Implement market definition CRUD
- Implement basic auction listing (create, get, search, cancel)
- Implement listing fee deduction (Currency debit as sink)
- Implement item escrow on listing creation

### Phase 2: Bidding & Settlement

- Implement bid placement with currency hold creation
- Implement outbid flow with hold release/create
- Implement buyout (immediate settlement)
- Implement `MarketSettlementService` background worker
- Implement settlement orchestration (hold capture, fee deduction, seller credit, escrow release)
- Implement settlement failure handling with retry and error events

### Phase 3: Vendor System

- Implement vendor catalog CRUD
- Implement vendor stock management (set, update price, restock, remove)
- Implement buy from vendor flow (payment, item creation, stock decrement)
- Implement sell to vendor flow (buyback pricing, vendor wallet debit)
- Implement `MarketRestockService` background worker
- Implement vendor wallet creation on vendor creation

### Phase 4: Price Analytics

- Implement `MarketPriceAggregationService` background worker
- Implement price history recording from settled auctions and vendor sales
- Implement price history query endpoints
- Implement market stats endpoint
- Implement price change event publication (rolling average shift detection)
- Implement price history pruning (retention days)

### Phase 5: Variable Provider Integration

- Implement `MarketCatalogVariableProviderFactory` for `${market.*}` vendor GOAP variables
- Implement `MarketPriceVariableProviderFactory` for `${market-price.*}` economic GOAP variables
- Register factories as `IVariableProviderFactory` singletons
- Test with sample ABML vendor behaviors

### Phase 6: Resource Cleanup & Hardening

- Implement cleanup endpoints (by-character, by-realm, by-game-service)
- Register with lib-resource for cascading cleanup
- Implement idempotency for all mutating endpoints
- Add distributed lock coverage for all concurrent operations
- Integration testing with lib-escrow, lib-currency, lib-item, lib-inventory

---

## Potential Extensions

1. **Auction sniping protection**: Extend listing duration by a configurable window when a bid is placed in the final minutes. Prevents last-second sniping. Config: `AuctionSnipeProtectionWindowMinutes`.

2. **Reserve price (hidden minimum)**: Sellers can set a minimum acceptable price that isn't revealed to bidders. If the highest bid doesn't meet the reserve, the listing expires and the item returns. The listing shows "reserve not met" without revealing the amount.

3. **Multi-item listings**: Sell a bundle of items in a single listing. Escrow holds all items; buyer gets all or nothing. Useful for recipe material sets, equipment packages.

4. **Market-scoped currency restrictions**: Market definitions can restrict which currencies are accepted. A gold-only auction house, a guild-token vendor, a premium currency marketplace.

5. **Vendor negotiation API**: For personality-driven vendors, expose a `/market/vendor/negotiate` endpoint where the buyer proposes a price and the vendor's ABML behavior decides to accept, counter-offer, or refuse. Transforms fixed-price buying into dynamic haggling.

6. **Auction house NPCs as auctioneers**: The auction house itself could be an NPC Actor that manages listings -- an auctioneer who announces new valuable listings, commentates on bidding wars, and whose personality affects how the auction house feels. Pure flavor that uses existing Actor infrastructure.

7. **Cross-market price arbitrage detection**: Detect when the same item template has significantly different average prices across markets in different locations. Publish events that NPC merchant GOAP can consume to identify trade opportunities (buy low here, sell high there -- the bridge to lib-trade).

8. **Vendor reputation system**: Track per-vendor metrics (total sales, customer satisfaction from post-sale events, pricing fairness score). Feed into NPC GOAP -- vendors with good reputations attract more customers (higher traffic events), vendors with bad reputations get avoided.

9. **Client events**: `market-client-events.yaml` for pushing auction status updates (outbid notification, auction won, listing expired) and vendor catalog refreshes to connected WebSocket clients.

10. **Consignment model**: Sellers place items with an auction house NPC who manages the listing. The NPC takes a consignment fee (separate from listing fee). Enables NPC-run auction houses with their own economic behavior.

11. **Seasonal/event markets**: Markets with limited activation windows -- a harvest festival market, a winter solstice bazaar, a post-war reconstruction sale. Market definitions with `effectiveFrom`/`effectiveUntil` timestamps and seasonal recurrence configuration.

12. **Batch listing**: Create multiple listings in one call for vendors dumping inventory or players listing farming hauls. Uses batch-level idempotency with per-item sub-keys, same pattern as Currency batch credit.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(No bugs identified — this is a pre-implementation specification.)*

### Intentional Quirks (Documented Behavior)

1. **Listing fees are non-refundable sinks**: Cancelling a listing does NOT refund the listing fee. This is deliberate -- listing fees are a currency sink that prevents spam listings. The economic cost of listing incentivizes serious sellers and removes currency from circulation.

2. **Bids cannot be retracted**: Once a bid is placed and the currency hold is created, the bidder cannot cancel the bid. They can only be outbid by someone else. This prevents bid manipulation (bidding up to discover reserve, then retracting). The hold expires naturally per `BidHoldDurationDays` if the listing expires.

3. **Transaction fees are sinks, not revenue**: Transaction fees on successful sales are debited from the payment and destroyed (not transferred to a fee recipient wallet). Games that want auction house revenue flowing to an NPC or faction should implement a separate fee distribution mechanism outside lib-market.

4. **Vendor wallets can run out of money**: When a player sells items to a vendor, the vendor's wallet is debited. If the vendor has insufficient funds, the sell operation returns 422. This is intentional -- vendors are economic actors with finite resources. NPC Actor GOAP should manage vendor liquidity (withdraw from bank, earn from sales, request guild funding).

5. **Seller entity type is opaque**: `sellerId` + `sellerType` are polymorphic (character, guild, npc, system). lib-market doesn't validate the seller entity exists -- that's the caller's responsibility. This enables any entity type to participate in the market without lib-market knowing about all entity services.

6. **Settlement is eventually consistent**: Between listing expiration and settlement worker processing (up to `SettlementProcessingIntervalSeconds`), a listing may appear expired but not yet settled. The winning bidder's hold is still active, and the escrowed item hasn't moved. Consumers should check listing status, not assume immediate settlement.

7. **Price history uses server time, not game time**: Price history buckets use UTC timestamps, not in-game calendar time. Games with accelerated time (1 game-day = 1 real-hour) see price history at real-world granularity. Game-time price analysis is the caller's responsibility.

8. **Buyback multiplier is a floor, not a fixed price**: The `buybackMultiplier` on vendor stock entries defines the minimum fraction of base price the vendor will pay. Personality-driven vendors may offer more (or less, if the multiplier is just a starting point for negotiation). The actual buyback price is whatever amount the sell transaction records.

### Design Considerations (Requires Planning)

1. **Escrow dependency for auctions**: Auctions require lib-escrow for item custody. If lib-escrow is disabled (soft dependency), auction endpoints should return `ServiceUnavailable` rather than silently operating without custody guarantees. Vendor operations don't require escrow (immediate purchase, no custody needed).

2. **Search performance at scale**: `JsonQueryPagedAsync` on `market-listings` with text search, price range, category filters may not perform adequately at 100K+ concurrent listings. Consider Redis Search integration or a dedicated search index (same pattern as lib-documentation's full-text search). Initial implementation uses JSON queries; optimize when scale demands it.

3. **Multi-currency vendor pricing**: Vendor stock entries support multi-currency pricing (`prices: [{ currencyId, amount }]`). The buy endpoint needs to select which currency the buyer pays with. If multiple currencies are acceptable, the buyer chooses. If only one is accepted, that's enforced. The schema should support this without overcomplicating the common single-currency case.

4. **Buyback item destination**: When a player sells an item to a vendor, does the item go into the vendor's inventory (restocking), or is it destroyed? Both patterns are valid -- a weapons merchant might resell a used sword (restock), while a general store might melt it down (destroy + currency debit). Configurable per vendor or per stock entry.

5. **Auction cancellation with bids**: Currently, cancellation is blocked when bids exist. An alternative model allows cancellation with a penalty fee (higher than listing fee), releasing all bid holds. This is a design decision per game -- some games allow it (with steep penalties), others don't.

6. **NPC bidding on auctions**: NPC Actors can place bids through the same API as players. The NPC's GOAP evaluates whether the item is worth the bid amount based on need, budget, and personality. This creates emergent price discovery -- NPCs competing with players for scarce items. No special NPC handling in lib-market; the Actor calls the same endpoints.

7. **Price history granularity vs. storage**: Hourly price history for 10K item templates across 100 realms generates significant data volume. The price aggregation worker should use the coarsest useful granularity per query pattern -- hourly for recent data, daily for older data, weekly for historical. A configurable rollup policy would collapse hourly data into daily after N days.

8. **Market definition immutability for active listings**: Changing fee rates on a market definition while listings are active creates ambiguity about which rate applies at settlement. Option A: new rates apply only to new listings. Option B: settlement uses the rate at listing creation (stored on the listing). Recommend Option B (store fee rate snapshot on listing creation).

9. **MarketEntityType classification** *(from audit)*: The `MarketEntityType` enum identifies seller/bidder/buyer entity types with values `Character`, `Guild`, `Npc`, `System`. Per decision tree: `Npc` is not a distinct EntityType in Bannou (NPCs are Characters). `System` exists in EntityType but `Npc` does not. Options: (a) Use `$ref: EntityType` and represent NPCs as `Character` -- cleanest answer. (b) Keep a service-specific `MarketParticipantType` enum because the `Npc` distinction is functionally meaningful (e.g., fee exemptions, stock behavior) -- this is a Test 3 exception (non-entity role) that must be explicitly justified.

10. **x-permissions for player-facing endpoints** *(from audit)*: Currently all auction/vendor/price endpoints are specified as `x-permissions: []` (service-to-service only). If any endpoints should be directly accessible via WebSocket (player browsing auctions, placing bids from game client), those endpoints must use `x-permissions: [{role: user}]` and per tenets must accept `webSocketSessionId` instead of entity IDs for caller identity (resolved to account/character server-side). The current spec assumes all player actions flow through game engine/Actor -- if direct WebSocket access is needed, this must be revisited.

11. **Vendor wallet EntityType** *(from audit)*: Currency's wallet API uses `EntityType` enum for `ownerType`. The removed `VendorWalletOwnerType` config used string `"vendor"` which is not a valid EntityType. Options: (a) Use `EntityType.Character` since vendor NPCs are characters. (b) Add a `Vendor` value to EntityType if vendors need distinct wallet identity. This ties to DC#9 -- if NPCs are just Characters, their wallets use `EntityType.Character`.

12. **Buyout multi-service compensation** *(from tenet validation)*: The buyout flow involves sequential cross-service calls: Currency debit (buyer pays) → fee deduction → seller credit → Escrow release (item to buyer) → release all active bid holds. Per Implementation Tenets (multi-service call compensation), if step 4 (Escrow release) fails after steps 1-3 succeed, the buyer has paid but hasn't received the item. The settlement worker provides self-healing for expired auctions but does not cover the synchronous buyout flow. Options: (a) Implement catch-block compensation that reverses Currency operations on Escrow failure. (b) Document that a failed buyout transitions the listing to a "pending-settlement" state handled by the settlement worker (self-healing). Either way, the compensation or self-healing mechanism must be explicit — a comment acknowledging possible orphaned state is not sufficient per Implementation Tenets.

13. **`requirementsMet` trust boundary** *(from audit)*: The vendor Buy endpoint accepts `requirementsMet: boolean` in the request -- a caller-attestation pattern where lib-market trusts the caller's requirement validation. This is a design choice, not a tenet violation: Market is game-agnostic and cannot validate arbitrary requirements (level, reputation, faction standing). Options: (a) Keep as-is -- caller attests, Market records. (b) Remove the field entirely -- caller is responsible for gating before calling Buy. (c) Implement a `IRequirementProviderFactory` pattern where requirement plugins register and Market validates server-side. If kept, the field should be `required: true` with no default.

---

## Work Tracking

| Issue | Title | Status | Relevance |
|-------|-------|--------|-----------|
| [#427](https://github.com/beyond-immersion/bannou-service/issues/427) | Economy Layer: Market, Trade & Taxation Services | Open | Primary implementation tracking issue (umbrella) |
| [#153](https://github.com/beyond-immersion/bannou-service/issues/153) | Cross-Cutting: Escrow Asset Transfer Integration Broken | Open | **Phase 0 blocker** -- auction item custody requires Escrow asset movement |
| [#222](https://github.com/beyond-immersion/bannou-service/issues/222) | Currency + Escrow: Missing Background Tasks (4 items) | Open | **Phase 0 blocker** -- partial progress (EscrowExpirationService implemented), remaining background tasks outstanding |
| [#428](https://github.com/beyond-immersion/bannou-service/issues/428) | ABML Economic Action Handlers | Open | Prerequisite -- NPC brains need ABML action handlers to interact with economy |
| [#429](https://github.com/beyond-immersion/bannou-service/issues/429) | Analytics: Economic Velocity & Distribution Extensions | Open | Extension -- analytics integration for economic health metrics |
| [#147](https://github.com/beyond-immersion/bannou-service/issues/147) | Implement Phase 2 Variable Providers (Currency, Inventory, Relationship) | Open | Required for NPC social bond awareness in vendor GOAP |

See also [Economy System Guide](../guides/ECONOMY-SYSTEM.md) for the cross-cutting economy architecture.
