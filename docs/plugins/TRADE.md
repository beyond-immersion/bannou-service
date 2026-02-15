# Trade Plugin Deep Dive

> **Plugin**: lib-trade (not yet created)
> **Schema**: `schemas/trade-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: trade-routes (MySQL), trade-shipments (Redis), trade-shipments-archive (MySQL), trade-tariff-policies (MySQL), trade-tariff-records (MySQL), trade-contraband (MySQL), trade-tax-policies (MySQL), trade-tax-assessments (MySQL), trade-npc-profiles (MySQL), trade-supply-demand (Redis), trade-velocity (Redis), trade-velocity-history (MySQL) — all planned
> **Layer**: L4 GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: [ECONOMY-SYSTEM.md](../guides/ECONOMY-SYSTEM.md), [ITEM-ECONOMY-PLUGINS.md](../plans/ITEM-ECONOMY-PLUGINS.md)

---

## Overview

The Trade service (L4 GameFeatures) is the economic logistics and supply orchestration layer for Bannou. It provides the mechanisms for moving goods across distances over game-time, enforcing border policies, calculating supply/demand dynamics, and enabling NPC economic decision-making. Trade is to the economy what Puppetmaster is to NPC behavior -- an orchestration layer that composes lower-level primitives (Transit for movement, Currency for payments, Item/Inventory for cargo, Escrow for custody) into higher-level economic flows. Internal-only, never internet-facing.

---

## Design Philosophy

**What Trade IS**: Trade routes, shipments, tariffs, taxation, supply/demand dynamics, NPC economic intelligence, velocity monitoring.

**What Trade is NOT**: An auction house (that's Market), a crafting system (that's Craft), a production automator (that's Workshop), a currency ledger (that's Currency), a movement calculator (that's Transit). Trade orchestrates across all of these.

Where Market handles exchange **at** a location (auctions, vendor catalogs, price discovery), Trade handles the logistics of moving goods **between** locations. Where Transit handles the raw mechanics of movement (connections, modes, journeys), Trade layers economic meaning onto that movement (cargo value, tariff liability, profit margins, supply chains). Distance creates value: iron costs 10g at the mine and 25g in the capital because someone paid the transit cost, bore the risk, and waited the travel time.

Trade absorbs the "lib-economy" monitoring concept from the Economy Architecture planning document. Velocity tracking, NPC economic profiles, and supply/demand signals live here because they are inseparable from logistics -- you cannot monitor economic health without understanding how goods flow through the geography.

### Three-Tier Usage

The trade and economic systems serve three different usage patterns through the same APIs. The plugin provides primitives and data; it does not enforce game-specific rules.

```
┌─────────────────────────────────────────────────────────────────┐
│ Tier 1: Divine/System Oversight                                  │
│ God actors or system processes with full visibility               │
│ Use: Full analytics, automatic interventions, velocity control   │
│ Example: Hermes detects stagnant village, spawns trade event     │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Same APIs
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ Tier 2: NPC Governance                                           │
│ Economist NPCs, customs officials, merchant guild leaders        │
│ Behaviors encode economic knowledge, imperfect information       │
│ Use: Query what they can "see", make bounded-rational decisions  │
│ Example: Tax collector assesses wealth, visits taxpayers          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ Same APIs
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ Tier 3: External Management                                      │
│ Game server handles all decisions outside Bannou                  │
│ Use: "Where's money flowing weakest?" -- just give me data       │
│ Example: Admin dashboard monitoring inflation pressure            │
└─────────────────────────────────────────────────────────────────┘
```

### Declarative Shipment Lifecycle

Trade follows the established Bannou pattern: the **game drives events**, the **plugin records state**. Trade does not automatically progress shipments, collect tariffs, or assess taxes. The game server (or NPC Actor brains) calls the APIs to indicate what happened. Trade records the state, publishes events, and provides calculations.

This means:
- `/trade/shipment/depart` is called by the game when a caravan actually leaves
- `/trade/shipment/border-crossing` is called by the game when a customs interaction occurs
- `/trade/tariff/collect` is called by the game (or NPC customs officer) when tariffs are paid
- The game decides whether smuggling succeeds -- Trade records the outcome either way

The exception is background workers for **periodic computation** (velocity metrics, supply/demand snapshots, tax assessment generation). These are analytical, not transactional -- they compute and publish, not mutate game state.

### Distance Creates Value

The fundamental economic principle Transit enables: **goods have different value at different locations because moving them costs time, money, and risk.** Without this principle, economies are spatially flat -- iron costs the same everywhere, and there's no reason for trade to exist.

Trade makes distance economic by:
1. **Transit cost**: Travel time × mode operating cost (fuel, stamina, wages)
2. **Risk premium**: Connection risk × cargo value × insurance factor
3. **Tariff burden**: Border crossings add per-item and per-currency charges
4. **Opportunity cost**: Time spent traveling is time not spent producing/selling

These costs create natural price differentials between locations, which create profit opportunities, which motivate NPC merchants to form trade routes, which create supply chains, which create economic velocity, which creates a living economy.

### Faucet/Sink Discipline

Every currency source (faucet) must have a corresponding removal mechanism (sink). Trade provides multiple sink channels:
- **Listing/transaction fees** (via Market integration)
- **Tariffs and import/export duties** (via border crossings)
- **Taxes** (transaction, property, income, wealth, sales)
- **Shipment losses** (bandit attacks, storms, spoilage)
- **Transit costs** (fuel, feed, crew wages)

Trade monitors the faucet/sink balance via velocity metrics and publishes alerts when imbalance is detected. Divine economic actors (god Actors via Puppetmaster) subscribe to these alerts and spawn narrative interventions.

---

## Core Architecture

### Orchestration Over Primitives

```
┌─────────────────────────────────────────────────────────────────┐
│                        TRADE (L4)                                │
│  Trade Routes • Shipments • Tariffs • Taxes • NPC Economics      │
│  Supply/Demand • Velocity Monitoring                             │
└────────┬──────────┬──────────┬──────────┬───────────────────────┘
         │          │          │          │
    uses │     uses │     uses │     uses │
         ▼          ▼          ▼          ▼
┌────────────┐ ┌────────┐ ┌──────┐ ┌───────────┐
│  Transit   │ │Currency│ │ Item │ │ Inventory │   ← L2 Foundation
│  (L2)      │ │ (L2)   │ │ (L2) │ │ (L2)      │
│ connections│ │wallets │ │templ.│ │containers │
│ journeys   │ │credit  │ │inst. │ │transfer   │
│ route calc │ │debit   │ │      │ │           │
└────────────┘ └────────┘ └──────┘ └───────────┘
         │
    optional │
         ▼
┌────────────┐ ┌────────┐ ┌──────────┐ ┌──────────┐
│  Escrow    │ │Faction │ │Environm. │ │Analytics │  ← L4 Peers
│  (L4)      │ │ (L4)   │ │ (L4)     │ │ (L4)     │
│ custody    │ │borders │ │weather   │ │velocity  │
│ during     │ │tariff  │ │risk      │ │metrics   │
│ transport  │ │enforce │ │modifier  │ │          │
└────────────┘ └────────┘ └──────────┘ └──────────┘
```

### The Supply Chain Emergence

Trade doesn't define supply chains -- they **emerge** from NPC GOAP decisions interacting with trade infrastructure:

```
WORKSHOP (L4) at Iron Mine              MARKET (L4) at Capital
  Produces 10 iron ingots/game-day        Demand: 15 ingots/day
  Local price: 10g/ingot                  Local price: 25g/ingot
         │                                       ▲
         │                                       │
         ▼                                       │
TRADE ROUTE: "Iron Road"                         │
  3 legs via Transit connections                 │
  Total distance: 120km                          │
  Wagon speed: 10 km/gh                          │
  Travel time: 12 game-hours                     │
         │                                       │
         ▼                                       │
MERCHANT NPC (Actor brain):                      │
  GOAP evaluates:                                │
    Revenue: 30 ingots × 25g = 750g              │
    Cost:    30 ingots × 10g = 300g (purchase)   │
           + transit cost     =  50g (wagon hire) │
           + tariff at border =  35g (5% import)  │
           + risk premium     =  15g (0.15 risk)  │
    Profit: 750 - 400 = 350g                      │
    Time: 12 game-hours (travel) + 4 (loading)    │
    Profit/hour: 21.9g/gh                         │
         │                                       │
         └──────── SHIPMENT ─────────────────────┘
           Creates transit journey for wagon
           Items escrowed during transport
           Arrives 12 game-hours later
           Supply increases, price drops to 20g
           Merchant earned 350g
           Tariff sank 35g from economy

NEXT CYCLE:
  Price dropped → profit margin thinner
  Merchant evaluates alternative routes
  Other merchants enter market if profitable
  Competition drives prices toward equilibrium
  Weather closes mountain pass → price spikes
  Hermes (god actor) notices velocity drop → spawns event
```

This is the vision from `VISION.md`: *"The economy must be NPC-driven, not player-driven. Supply, demand, pricing, and trade routes emerge from NPC behavior."*

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| lib-state (IStateStoreFactory) | L0 (hard) | Persistence for routes, shipments, tariff/tax policies, NPC profiles, supply/demand snapshots, velocity metrics |
| lib-messaging (IMessageBus) | L0 (hard) | Publishing shipment lifecycle, tariff, tax, velocity, and supply/demand events |
| lib-transit (ITransitClient) | L2 (hard) | Route validation (connection existence), travel time estimation, journey creation/tracking for shipments |
| lib-currency (ICurrencyClient) | L2 (hard) | Tariff/tax collection (debit), revenue credits, wallet balance queries for velocity/tax assessment |
| lib-item (IItemClient) | L2 (hard) | Item instance validation, template lookup for contraband checking |
| lib-inventory (IInventoryClient) | L2 (hard) | Cargo custody transfers, local stock level queries for supply/demand snapshots |
| lib-worldstate (IWorldstateClient) | L2 (hard) | Game-time timestamps for shipments/journeys, seasonal route availability, tax assessment periods |
| lib-escrow (IEscrowClient) | L4 (soft) | Safe custody during transport when `custodyMode: "escrow"` |
| lib-faction (IFactionClient) | L4 (soft) | Border sovereignty, faction-owned tariff policies, diplomatic exemptions |
| lib-environment (IEnvironmentClient) | L4 (soft) | Seasonal resource availability, weather risk modifiers, spoilage factors |
| lib-analytics (IAnalyticsClient) | L4 (soft) | Historical transaction data for richer velocity computation |
| lib-market (IMarketClient) | L4 (soft) | Price data, listing counts for supply/demand snapshot enrichment |
| lib-hearsay (IHearsayClient) | L4 (soft) | Belief-filtered price knowledge for NPC market analysis (imperfect information) |

---

## Dependents

| Dependent | Relationship |
|-----------|-------------|
| lib-market (L4) | Subscribes to `trade.shipment.arrived` for supply arrival price updates |
| lib-analytics (L4) | Subscribes to `trade.shipment.departed`, `trade.shipment.arrived`, `trade.shipment.lost`, `trade.shipment.border_crossed`, `trade.tariff.collected`, `trade.tariff.evaded` for economic tracking |
| lib-puppetmaster (L4) | Subscribes to `trade.shipment.departed`, `trade.shipment.border_crossed`, `trade.shipment.lost`, `trade.velocity.alert`, `trade.tax.debt_defaulted` for divine economic intervention |
| lib-obligation (L4) | Subscribes to `trade.tax.debt_defaulted` for enforcement obligation creation |
| lib-actor (L2) | Consumes `${trade.*}` variables via IVariableProviderFactory (pull-based, hierarchy-safe) |

---

## Data Models

### Trade Route

```yaml
TradeRoute:
  id: uuid

  # Identification
  name: string                      # "Iron Road", "Silk Route", "Northern Passage"
  code: string                      # "iron_road", "silk_route"
  description: string

  # Ownership (optional -- for personal/guild routes)
  ownerId: uuid                     # null for system routes
  ownerType: string                 # "system", "character", "guild", "faction", "organization"

  # Scope
  realmId: uuid                     # Primary realm (for queries)
  crossesRealms: boolean            # Does this route cross realm boundaries?

  # Legs (ordered list of transit connections forming the route)
  legs: [TradeRouteLeg]

  # Aggregated summary (computed from legs)
  totalLegs: integer
  totalDistanceKm: decimal
  totalEstimatedGameHours: decimal  # Via best common mode
  borderCrossings: integer
  riskProfile: object               # { "low": 3, "medium": 1, "high": 0 }

  # Status
  status: string                    # "active", "closed", "dangerous", "seasonal"

  # Metadata
  tags: [string]                    # "maritime", "mountain", "safe", "smuggler"
  seasonalAvailability: object      # { "winter": false, "summer": true }

  createdAt: timestamp
  modifiedAt: timestamp

TradeRouteLeg:
  legIndex: integer                 # Order in route (0, 1, 2...)

  # Endpoints
  fromLocationId: uuid
  toLocationId: uuid

  # Transit connection reference
  transitConnectionId: uuid         # The Transit connection this leg uses

  # Travel metadata (computed from Transit connection + default mode)
  estimatedDurationGameHours: decimal
  distanceKm: decimal
  terrainType: string

  # Border crossing
  crossesBorder: boolean
  fromRealmId: uuid                 # Only if crossesBorder
  toRealmId: uuid                   # Only if crossesBorder

  # Checkpoint
  hasCheckpoint: boolean
  checkpointLocationId: uuid        # Where tariffs are assessed
```

### Shipment

```yaml
Shipment:
  id: uuid

  # Route
  routeId: uuid                     # Which trade route this follows
  currentLegIndex: integer          # Which leg we're on

  # Ownership
  ownerId: uuid                     # Who owns the goods being shipped
  ownerType: string                 # "character", "guild", "organization", "caravan_company"

  # Carrier (who's physically moving it)
  carrierId: uuid                   # Character/NPC doing the transport
  carrierType: string               # "character", "npc"

  # Transit journey (created in lib-transit for the carrier)
  transitJourneyId: uuid            # The Transit journey tracking this shipment's movement

  # Transit mode
  transitModeCode: string           # How this shipment is being moved

  # Contents
  goods: [ShipmentGoods]
  currencies: [ShipmentCurrency]
  totalDeclaredValue: decimal       # Computed for tariff calculation

  # Custody
  escrowAgreementId: uuid           # Optional -- if using lib-escrow for safe holding
  custodyMode: string               # "escrow" (via lib-escrow), "carrier" (carrier's inventory),
                                    # "virtual" (game manages custody externally)

  # Status
  status: string
    # preparing        -- Being loaded
    # in_transit       -- On the move
    # at_checkpoint    -- Waiting at border crossing
    # arrived          -- Reached destination
    # lost             -- Destroyed, stolen, etc.
    # seized           -- Confiscated at border
    # abandoned        -- Carrier gave up

  # Tracking
  departedAtGameTime: decimal
  currentLocationId: uuid
  estimatedArrivalGameTime: decimal
  arrivedAtGameTime: decimal

  # Incident tracking
  incidents: [ShipmentIncident]

  # Financial summary (computed after arrival)
  financialSummary: ShipmentFinancialSummary

  createdAt: timestamp
  modifiedAt: timestamp

ShipmentGoods:
  itemInstanceId: uuid              # Item instance being shipped
  templateId: uuid                  # Denormalized for search
  templateCode: string              # Denormalized for display
  quantity: integer
  declaredAtBorder: boolean         # Was this declared at customs?
  unitValue: decimal                # Declared unit value for tariff calculation

ShipmentCurrency:
  currencyDefinitionId: uuid
  amount: decimal
  declaredAtBorder: boolean

ShipmentIncident:
  legIndex: integer
  incidentType: string              # "bandit_attack", "storm", "customs_inspection",
                                    # "spoilage", "breakdown", "smuggling_detected"
  outcome: string                   # "escaped", "lost_cargo", "delayed", "seized"
  lostGoods: [{ templateId: uuid, quantity: integer }]
  lostCurrency: [{ currencyDefinitionId: uuid, amount: decimal }]
  delayGameHours: decimal           # Extra time added by this incident
  gameTime: decimal                 # When the incident occurred
  description: string               # Narrative description

ShipmentFinancialSummary:
  totalGoodsValue: decimal          # Value of goods at destination prices
  totalTransitCost: decimal         # Transit expenses (crew, fuel, wear)
  totalTariffsPaid: decimal         # All border tariffs collected
  totalLosses: decimal              # Value of lost/seized goods
  netProfit: decimal                # Revenue - costs - tariffs - losses
  profitPerGameHour: decimal        # Efficiency metric
```

### Tariff Policy

```yaml
TariffPolicy:
  id: uuid
  realmId: uuid                     # Which realm's borders this applies to

  # Scope
  scope: string                     # "realm_wide", "specific_border", "category"
  borderLocationId: uuid            # For "specific_border" scope
  direction: string                 # "import", "export", "both"

  # Rates
  defaultRate: decimal              # e.g., 0.05 = 5%
  categoryRates: [                  # Category-specific overrides
    { category: string, rate: decimal }
  ]
  itemRates: [                      # Item-specific overrides
    { templateId: uuid, rate: decimal }
  ]
  currencyRate: decimal             # Rate applied to currency itself

  # Exemptions
  exemptions: [TariffExemption]

  # Collection mode
  collectionMode: string            # "automatic" | "assessed" | "advisory"
                                    # automatic: plugin auto-debits via lib-currency
                                    # assessed: plugin calculates, game/NPCs collect
                                    # advisory: plugin provides data only

  # Status
  status: string                    # "active", "suspended", "wartime"
  effectiveFrom: timestamp
  effectiveUntil: timestamp         # null = indefinite

  createdAt: timestamp
  modifiedAt: timestamp

TariffExemption:
  entityType: string                # "character", "guild", "faction", "organization"
  entityId: uuid
  exemptionType: string             # "full", "partial"
  partialRate: decimal              # Rate when partially exempt
  reason: string                    # "diplomatic_immunity", "guild_agreement", "noble_privilege"
  expiresAt: timestamp              # null = permanent

TariffRecord:
  id: uuid
  shipmentId: uuid
  policyId: uuid
  legIndex: integer                 # Which leg triggered this tariff
  borderType: string                # "internal", "external"

  # Calculated amounts
  goodsTariff: decimal              # Tariff on goods
  currencyTariff: decimal           # Tariff on currency
  totalTariff: decimal              # Combined

  # Actual collection
  collectionStatus: string          # "pending", "collected", "partial", "evaded", "exempt"
  amountCollected: decimal
  collectedBy: uuid                 # NPC customs officer who collected (null if automatic)
  collectedAtGameTime: decimal

  # Smuggling data
  declaredGoods: [{ templateId: uuid, quantity: integer }]
  actualGoods: [{ templateId: uuid, quantity: integer, hidden: boolean }]
  declaredCurrency: [{ currencyDefinitionId: uuid, amount: decimal }]
  actualCurrency: [{ currencyDefinitionId: uuid, amount: decimal, hidden: boolean }]
  inspectionOccurred: boolean
  smugglingDetected: boolean
  seizures: [{ templateId: uuid, quantity: integer }]
```

### Contraband Definition

```yaml
ContrabandDefinition:
  id: uuid
  realmId: uuid

  # What is contraband
  type: string                      # "item_category", "item_template", "currency"
  targetId: uuid                    # Template ID or Currency Definition ID
  targetCategory: string            # "weapons", "narcotics", "religious_artifacts", "magical"

  # Severity
  severity: string                  # "restricted" | "prohibited" | "capital_offense"

  # Consequences (advisory -- game enforces)
  suggestedFine: decimal
  suggestedPenalty: string          # "confiscation", "imprisonment", "exile", "death"

  # Status
  effectiveFrom: timestamp
  effectiveUntil: timestamp         # null = permanent

  createdAt: timestamp
  modifiedAt: timestamp
```

### Tax Policy

```yaml
TaxPolicy:
  id: uuid
  realmId: uuid
  taxType: string                   # "transaction", "income", "property", "wealth", "sales", "custom"
  name: string                      # "Heartlands Property Tax", "Royal Sales Tax"
  description: string

  # Rate
  baseRate: decimal                 # e.g., 0.05 = 5%
  progressiveBrackets: [TaxBracket] # Optional for progressive taxation

  # Exemptions
  exemptions: [
    { entityType: string, entityId: uuid, reason: string }
  ]

  # Collection
  collectionMode: string            # "automatic" | "assessed" | "advisory"
  collectionTarget: string          # "void" (pure sink), "realm_treasury", "faction_treasury"
  collectionTargetId: uuid          # Wallet or entity to receive tax revenue (null for void)

  # For assessed taxes
  assessmentFrequency: string       # "weekly", "monthly", "seasonal", "annual" (game-time)
  gracePeriod: string               # "7_game_days", "30_game_days"
  latePenaltyRate: decimal          # Additional rate per period overdue

  # Status
  status: string                    # "active", "suspended"
  effectiveFrom: timestamp
  effectiveUntil: timestamp

  createdAt: timestamp
  modifiedAt: timestamp

TaxBracket:
  threshold: decimal                # Income/wealth above this amount
  rate: decimal                     # Rate applied to the amount above threshold

TaxAssessment:
  id: uuid
  policyId: uuid
  taxpayerId: uuid
  taxpayerType: string              # "character", "guild", "organization", "household"
  realmId: uuid

  # Assessment
  assessedValue: decimal            # Base value being taxed
  taxDue: decimal                   # Calculated tax amount
  effectiveRate: decimal            # Actual rate after brackets/exemptions
  bracketApplied: string            # Which bracket was used

  # Payment tracking
  dueDate: timestamp                # Game-time deadline
  status: string                    # "pending", "partial", "paid", "overdue", "defaulted"
  amountPaid: decimal
  penaltiesAccrued: decimal
  lastPaymentGameTime: decimal

  createdAt: timestamp
  modifiedAt: timestamp

TaxDebt:
  taxpayerId: uuid
  taxpayerType: string
  realmId: uuid
  totalOwed: decimal                # Across all overdue assessments
  oldestDueDate: timestamp
  assessmentCount: integer
  suggestedConsequences: [string]   # "wage_garnishment", "asset_seizure", "imprisonment"
```

### NPC Economic Profile

```yaml
NpcEconomicProfile:
  characterId: uuid                 # Links to Character service

  # Economic role
  economicRole: string              # "merchant", "craftsman", "farmer", "miner",
                                    # "fisher", "laborer", "noble", "consumer", "none"

  # Production (what they make or gather)
  produces: [NpcProductionEntry]

  # Consumption (what they need)
  consumes: [NpcConsumptionEntry]

  # Trading behavior (personality-derived)
  tradingPersonality: NpcTradingPersonality

  # Financial state (snapshot)
  primaryWalletId: uuid             # Their main currency wallet
  estimatedNetWorth: decimal        # Last computed
  dailyRevenue: decimal             # Average over last 7 game-days
  dailyExpenses: decimal            # Average over last 7 game-days

  # Location economics
  homeLocationId: uuid              # Where they primarily operate
  tradingRadius: decimal            # How far they'll travel for trade (game-km)

  modifiedAt: timestamp

NpcProductionEntry:
  templateId: uuid                  # What item they produce
  templateCode: string              # Denormalized
  ratePerGameDay: decimal           # How many they produce per game-day
  skillLevel: decimal               # Production quality factor (0.0-1.0)
  requiresWorkshop: boolean         # Does this come from a Workshop task?
  workshopTaskId: uuid              # Reference to active Workshop task (if applicable)

NpcConsumptionEntry:
  templateId: uuid                  # What item they consume
  templateCode: string              # Denormalized
  ratePerGameDay: decimal           # How many they need per game-day
  priority: integer                 # 1 = essential (food), 2 = important (tools), 3 = luxury
  substitutes: [uuid]               # Alternative template IDs if primary unavailable

NpcTradingPersonality:
  riskTolerance: decimal            # 0-1: willingness to invest in risky ventures
  priceAwareness: decimal           # 0-1: how closely they track market prices
  loyaltyFactor: decimal            # 0-1: preference for repeat trading partners
  hoarding: decimal                 # 0-1: tendency to stockpile beyond immediate need
  bargainDrive: decimal             # 0-1: how aggressively they negotiate
  explorationRange: decimal         # 0-1: willingness to seek distant markets
```

### Supply/Demand Snapshot

```yaml
SupplyDemandSnapshot:
  locationId: uuid
  realmId: uuid
  computedAtGameTime: decimal

  # Per-item signals
  items: [SupplyDemandItem]

  # Aggregate
  totalSupplyValue: decimal         # Total value of goods available
  totalDemandValue: decimal         # Total value of goods wanted
  supplyDemandRatio: decimal        # > 1.0 = oversupply, < 1.0 = undersupply

SupplyDemandItem:
  templateId: uuid
  templateCode: string

  # Supply
  localSupply: integer              # Units available at this location
  localProductionRate: decimal      # Units produced per game-day locally
  inboundShipmentRate: decimal      # Units arriving per game-day via shipments

  # Demand
  localDemand: integer              # Units wanted at this location
  localConsumptionRate: decimal     # Units consumed per game-day locally
  outboundShipmentRate: decimal     # Units leaving per game-day via shipments

  # Pricing
  localPrice: decimal               # Current average price at this location
  lowestKnownPrice: decimal         # Cheapest known source
  lowestPriceLocationId: uuid       # Where that source is
  priceDifferential: decimal        # localPrice - lowestKnownPrice
  transitCostToLowest: decimal      # Cost to ship from cheapest source
  arbitrageOpportunity: boolean     # priceDifferential > transitCostToLowest
```

### Economic Velocity Metrics

```yaml
EconomicVelocityMetrics:
  realmId: uuid
  currencyDefinitionId: uuid
  locationId: uuid                  # null for realm-wide metrics
  periodStart: decimal              # Game-time period start
  periodEnd: decimal                # Game-time period end

  # Velocity
  velocity: decimal                 # Transaction volume / average stock
  velocityTrend: string             # "accelerating", "stable", "decelerating", "stagnant"

  # Transaction volume
  transactionCount: integer
  transactionVolume: decimal
  averageTransactionSize: decimal

  # Stock
  totalCurrencyInCirculation: decimal
  averageEntityWealth: decimal
  medianEntityWealth: decimal
  wealthGiniCoefficient: decimal    # 0 = perfect equality, 1 = one entity has all

  # Faucets
  totalFaucetAmount: decimal
  faucetsByType: object             # { "quest_reward": 500, "vendor_sale": 300, ... }

  # Sinks
  totalSinkAmount: decimal
  sinksByType: object               # { "vendor_purchase": 200, "tariff": 100, ... }

  # Net flow
  netFlow: decimal                  # Faucets - Sinks (positive = inflationary)

  # Hotspots / Coldspots
  locationVelocities: [             # Per-location breakdown (only in realm-wide metrics)
    {
      locationId: uuid
      locationCode: string
      velocity: decimal
      deviation: decimal            # How far from realm average
    }
  ]

  computedAt: timestamp
```

---

## API Endpoints

### Route Management

```yaml
/trade/route/create:
  access: authenticated
  description: "Create a named trade route from a sequence of locations"
  request:
    name: string
    code: string
    description: string             # Optional
    ownerId: uuid                   # Optional -- null for system routes
    ownerType: string               # "system", "character", "guild", "faction", "organization"
    realmId: uuid
    legs: [
      {
        fromLocationId: uuid
        toLocationId: uuid
      }
    ]
    tags: [string]                  # Optional
    seasonalAvailability: object    # Optional
  response: { route: TradeRoute }
  errors:
    - ROUTE_CODE_ALREADY_EXISTS
    - LOCATIONS_NOT_FOUND
    - NO_TRANSIT_CONNECTION         # No Transit connection exists for a leg
    - DISCONNECTED_LEGS             # Leg N's toLocationId != leg N+1's fromLocationId
  notes: |
    Internally resolves each leg to a Transit connection ID.
    Computes aggregated summary (distance, time, border crossings, risk).
    If a Transit connection doesn't exist for a leg, the route creation fails --
    connections must be established in lib-transit first.

/trade/route/get:
  access: user
  description: "Get a trade route by ID or code"
  request:
    routeId: uuid                   # One required
    code: string                    # One required
  response: { route: TradeRoute }
  errors:
    - ROUTE_NOT_FOUND

/trade/route/query:
  access: user
  description: "Query trade routes by location, owner, tags, or realm"
  request:
    fromLocationId: uuid            # Optional -- routes starting here
    toLocationId: uuid              # Optional -- routes ending here
    passingThrough: uuid            # Optional -- routes passing through this location
    realmId: uuid                   # Optional
    ownerId: uuid                   # Optional
    ownerType: string               # Optional
    tags: [string]                  # Optional
    status: string                  # Optional
    maxLegs: integer                # Optional
    maxDurationGameHours: decimal   # Optional
    page: integer
    pageSize: integer
  response: { routes: [TradeRoute], totalCount: integer }

/trade/route/update:
  access: authenticated
  description: "Update a trade route's properties or legs"
  request:
    routeId: uuid
    name: string                    # Optional
    description: string             # Optional
    status: string                  # Optional
    legs: [{ fromLocationId, toLocationId }]  # Optional -- replaces all legs
    tags: [string]                  # Optional
    seasonalAvailability: object    # Optional
  response: { route: TradeRoute }
  errors:
    - ROUTE_NOT_FOUND
    - NOT_OWNER                     # Must own route or be admin
    - ACTIVE_SHIPMENTS_ON_ROUTE     # Cannot change legs while shipments are in transit

/trade/route/delete:
  access: authenticated
  description: "Delete a trade route"
  request:
    routeId: uuid
  response: { deleted: boolean }
  errors:
    - ROUTE_NOT_FOUND
    - NOT_OWNER
    - ACTIVE_SHIPMENTS_ON_ROUTE

/trade/route/estimate-cost:
  access: user
  description: "Estimate the total cost of shipping goods along a route"
  request:
    routeId: uuid
    goods: [{ templateId: uuid, quantity: integer, unitValue: decimal }]
    currencies: [{ currencyDefinitionId: uuid, amount: decimal }]
    transitModeCode: string         # Which transit mode to use
    carrierId: uuid                 # Optional -- for entity-specific transit cost modifiers
  response:
    transitCost: decimal            # From transit time + mode operating cost
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
    riskAssessment: object          # { expectedLoss: decimal, worstCase: decimal }
    estimatedDurationGameHours: decimal
    estimatedProfit: decimal        # Revenue at destination prices - all costs
  notes: |
    Pure calculation -- does not create a shipment.
    Calls lib-transit route/calculate for travel time.
    Looks up applicable tariff policies for border crossings.
    Queries supply/demand snapshots for destination prices.
```

### Shipment Lifecycle

```yaml
/trade/shipment/create:
  access: authenticated
  description: "Create a shipment (status: preparing)"
  request:
    routeId: uuid
    ownerId: uuid
    ownerType: string
    carrierId: uuid
    carrierType: string
    transitModeCode: string         # How the carrier will travel
    goods: [{ itemInstanceId: uuid, declared: boolean }]
    currencies: [{ currencyDefinitionId: uuid, amount: decimal, declared: boolean }]
    custodyMode: string             # "escrow", "carrier", "virtual"
  response: { shipment: Shipment }
  errors:
    - ROUTE_NOT_FOUND
    - ROUTE_CLOSED
    - CARRIER_NOT_FOUND
    - TRANSIT_MODE_NOT_AVAILABLE    # Carrier can't use this mode
    - ITEM_NOT_FOUND                # Referenced item instance doesn't exist
    - ITEM_NOT_TRANSFERABLE         # Item is soulbound or otherwise locked
  notes: |
    If custodyMode is "escrow": creates an Escrow agreement and transfers
    goods/currency to escrow custody via lib-escrow.
    If custodyMode is "carrier": transfers goods to carrier's inventory
    via lib-inventory.
    If custodyMode is "virtual": no item movement -- game manages custody.
    Does NOT start the journey -- call /trade/shipment/depart for that.

/trade/shipment/depart:
  access: authenticated
  description: "Start a prepared shipment (creates Transit journey, status: in_transit)"
  request:
    shipmentId: uuid
  response: { shipment: Shipment }
  emits: trade.shipment.departed
  errors:
    - SHIPMENT_NOT_FOUND
    - INVALID_STATUS                # Must be "preparing"
    - FIRST_CONNECTION_CLOSED       # First leg's transit connection is closed
  notes: |
    Creates a Transit journey for the carrier via /transit/journey/create
    and /transit/journey/depart. Stores the transitJourneyId on the shipment.

/trade/shipment/complete-leg:
  access: authenticated
  description: "Mark a shipment leg as completed (carrier arrived at waypoint)"
  request:
    shipmentId: uuid
    legIndex: integer
    arrivedAtGameTime: decimal
    incidents: [                    # Optional -- things that happened during this leg
      {
        incidentType: string        # "bandit_attack", "storm", "spoilage", "breakdown"
        outcome: string             # "escaped", "lost_cargo", "delayed"
        lostGoods: [{ templateId: uuid, quantity: integer }]
        lostCurrency: [{ currencyDefinitionId: uuid, amount: decimal }]
        delayGameHours: decimal
        description: string
      }
    ]
  response: { shipment: Shipment }
  emits: trade.shipment.leg_completed
  errors:
    - SHIPMENT_NOT_FOUND
    - INVALID_STATUS
    - WRONG_LEG_INDEX               # Must match currentLegIndex
  notes: |
    Advances the Transit journey via /transit/journey/advance.
    If this was the last leg, transitions to "arrived" and emits
    trade.shipment.arrived instead.
    Records any incidents and updates financial summary.

/trade/shipment/border-crossing:
  access: authenticated
  description: "Record a border crossing event with customs interaction"
  request:
    shipmentId: uuid
    legIndex: integer

    # What the carrier declared
    declaredGoods: [{ templateId: uuid, quantity: integer }]
    declaredCurrency: [{ currencyDefinitionId: uuid, amount: decimal }]

    # What was actually carried (full truth -- game knows this)
    actualGoods: [{ templateId: uuid, quantity: integer, hidden: boolean }]
    actualCurrency: [{ currencyDefinitionId: uuid, amount: decimal, hidden: boolean }]

    # Crossing outcome (game-determined)
    crossingType: string            # "legal", "illegal", "attempted_smuggle"
    inspectionOccurred: boolean
    smugglingDetected: boolean
    tariffCollection: string        # "auto", "manual", "evaded", "exempt"
  response:
    shipment: Shipment
    tariffRecord: TariffRecord
    estimatedTariff: decimal
    actualTariffCollected: decimal
    seizures: [{ templateId: uuid, quantity: integer }]
  emits: trade.shipment.border_crossed
  emits: trade.tariff.collected     # If tariff was collected
  emits: trade.tariff.evaded        # If smuggling succeeded
  errors:
    - SHIPMENT_NOT_FOUND
    - NO_BORDER_ON_LEG              # This leg doesn't cross a border
  notes: |
    The game decides everything about the customs interaction:
    - Did inspection occur? (NPC customs officer skill check)
    - Was smuggling detected? (game logic)
    - Was tariff paid, evaded, or exempt? (game determines)
    Trade records the full truth for analytics and divine oversight.
    Gods monitoring smuggling rates can see both declared and actual.

/trade/shipment/arrive:
  access: authenticated
  description: "Mark a shipment as arrived at destination"
  request:
    shipmentId: uuid
    arrivedAtGameTime: decimal
    actualGoods: [{ templateId: uuid, quantity: integer }]
    actualCurrency: [{ currencyDefinitionId: uuid, amount: decimal }]
  response: { shipment: Shipment }
  emits: trade.shipment.arrived
  errors:
    - SHIPMENT_NOT_FOUND
    - INVALID_STATUS
  notes: |
    Completes the Transit journey via /transit/journey/arrive.
    If custodyMode is "escrow": releases escrow to owner at destination.
    If custodyMode is "carrier": goods remain in carrier's inventory.
    Computes financial summary (revenue, costs, tariffs, losses, profit).

/trade/shipment/lost:
  access: authenticated
  description: "Mark a shipment as lost (destroyed, stolen, sunk)"
  request:
    shipmentId: uuid
    reason: string
    recoverable: boolean
    lostGoods: [{ templateId: uuid, quantity: integer }]
    lostCurrency: [{ currencyDefinitionId: uuid, amount: decimal }]
  response: { shipment: Shipment }
  emits: trade.shipment.lost
  errors:
    - SHIPMENT_NOT_FOUND
    - INVALID_STATUS
  notes: |
    If custodyMode is "escrow": triggers escrow refund for remaining goods.
    Abandons the Transit journey via /transit/journey/abandon.

/trade/shipment/get:
  access: user
  description: "Get a shipment by ID"
  request:
    shipmentId: uuid
  response: { shipment: Shipment }
  errors:
    - SHIPMENT_NOT_FOUND

/trade/shipment/list:
  access: user
  description: "List shipments by owner, carrier, route, or realm"
  request:
    ownerId: uuid                   # Optional
    carrierId: uuid                 # Optional
    routeId: uuid                   # Optional
    realmId: uuid                   # Optional
    status: string                  # Optional
    activeOnly: boolean             # Default true
    page: integer
    pageSize: integer
  response: { shipments: [Shipment], totalCount: integer }
```

### Tariff Management

```yaml
/trade/tariff/policy/create:
  access: admin
  description: "Create a tariff policy for a realm's borders"
  request:
    realmId: uuid
    scope: string                   # "realm_wide", "specific_border", "category"
    borderLocationId: uuid          # For "specific_border" scope
    direction: string               # "import", "export", "both"
    defaultRate: decimal
    categoryRates: [{ category: string, rate: decimal }]
    itemRates: [{ templateId: uuid, rate: decimal }]
    currencyRate: decimal
    collectionMode: string          # "automatic", "assessed", "advisory"
    exemptions: [TariffExemption]
    effectiveFrom: timestamp
    effectiveUntil: timestamp       # Optional
  response: { policy: TariffPolicy }

/trade/tariff/policy/list:
  access: user
  description: "List tariff policies for a realm"
  request:
    realmId: uuid
    includeExpired: boolean         # Default false
    scope: string                   # Optional filter
  response: { policies: [TariffPolicy] }

/trade/tariff/policy/update:
  access: admin
  description: "Update a tariff policy"
  request:
    policyId: uuid
    defaultRate: decimal            # Optional
    categoryRates: [{ category, rate }] # Optional
    itemRates: [{ templateId, rate }]   # Optional
    currencyRate: decimal           # Optional
    status: string                  # Optional
    exemptions: [TariffExemption]   # Optional
    effectiveUntil: timestamp       # Optional
  response: { policy: TariffPolicy }
  errors:
    - POLICY_NOT_FOUND

/trade/tariff/calculate:
  access: user
  description: "Calculate tariffs for goods/currency crossing a border (pure calculation)"
  request:
    fromRealmId: uuid
    toRealmId: uuid
    goods: [{ templateId: uuid, quantity: integer, unitValue: decimal }]
    currencies: [{ currencyDefinitionId: uuid, amount: decimal }]
    entityId: uuid                  # Optional -- for exemption checking
  response:
    totalTariff: decimal
    goodsTariff: decimal
    currencyTariff: decimal
    breakdown: [{ templateId: uuid, rate: decimal, amount: decimal }]
    exemptionsApplied: [{ reason: string, savings: decimal }]
  notes: |
    Pure calculation -- doesn't collect anything.
    Uses currently active tariff policies for the border.

/trade/tariff/collect:
  access: authenticated
  description: "Record tariff collection (called by game or NPC customs officer)"
  request:
    tariffRecordId: uuid            # From border-crossing response
    walletId: uuid                  # Payer's wallet
    amount: decimal                 # Amount being paid
  response:
    tariffRecord: TariffRecord
    remainingDue: decimal
    transaction: object             # Currency transaction reference
  emits: trade.tariff.collected
  errors:
    - TARIFF_RECORD_NOT_FOUND
    - INSUFFICIENT_FUNDS
    - ALREADY_COLLECTED
  notes: |
    Calls lib-currency /currency/debit internally.
    If collectionTarget is not "void", credits the target wallet.
```

### Contraband

```yaml
/trade/contraband/define:
  access: admin
  description: "Define an item or currency as contraband in a realm"
  request:
    realmId: uuid
    type: string                    # "item_category", "item_template", "currency"
    targetId: uuid                  # Template ID or Currency Definition ID
    targetCategory: string          # For "item_category" type
    severity: string                # "restricted", "prohibited", "capital_offense"
    suggestedFine: decimal
    suggestedPenalty: string
    effectiveFrom: timestamp
    effectiveUntil: timestamp       # Optional
  response: { definition: ContrabandDefinition }

/trade/contraband/list:
  access: user
  description: "List contraband definitions for a realm"
  request:
    realmId: uuid
    includeExpired: boolean         # Default false
  response: { definitions: [ContrabandDefinition] }

/trade/contraband/check:
  access: user
  description: "Check if goods/currency are contraband in a realm"
  request:
    realmId: uuid
    goods: [{ templateId: uuid, quantity: integer }]
    currencies: [{ currencyDefinitionId: uuid, amount: decimal }]
  response:
    hasContraband: boolean
    violations: [
      {
        type: string
        targetId: uuid
        targetCategory: string
        severity: string
        suggestedFine: decimal
        suggestedPenalty: string
      }
    ]
    totalContrabandValue: decimal
```

### Tax Management

```yaml
/trade/tax/policy/create:
  access: admin
  description: "Create a tax policy for a realm"
  request:
    realmId: uuid
    taxType: string                 # "transaction", "income", "property", "wealth", "sales", "custom"
    name: string
    description: string
    baseRate: decimal
    progressiveBrackets: [TaxBracket]   # Optional
    collectionMode: string          # "automatic", "assessed", "advisory"
    collectionTarget: string        # "void", "realm_treasury", "faction_treasury"
    collectionTargetId: uuid        # Optional
    assessmentFrequency: string     # For assessed taxes
    gracePeriod: string             # Optional
    latePenaltyRate: decimal        # Optional
    exemptions: [{ entityType, entityId, reason }]
    effectiveFrom: timestamp
    effectiveUntil: timestamp       # Optional
  response: { policy: TaxPolicy }

/trade/tax/policy/list:
  access: user
  description: "List tax policies for a realm"
  request:
    realmId: uuid
    taxType: string                 # Optional filter
    includeExpired: boolean
  response: { policies: [TaxPolicy] }

/trade/tax/calculate:
  access: user
  description: "Calculate tax due for a given value and policy (pure calculation)"
  request:
    policyId: uuid
    taxpayerId: uuid                # For exemption checking
    baseValue: decimal
  response:
    taxDue: decimal
    effectiveRate: decimal
    bracketApplied: string
    exemptionApplied: boolean
    exemptionReason: string

/trade/tax/assess:
  access: admin
  description: "Create a tax assessment for an entity"
  request:
    policyId: uuid
    taxpayerId: uuid
    taxpayerType: string
    assessedValue: decimal
    dueDate: timestamp              # Game-time deadline
  response: { assessment: TaxAssessment }
  emits: trade.tax.assessment_created
  notes: |
    Can be called manually by admin or automatically by the
    TaxAssessmentWorker for assessed-mode tax policies.

/trade/tax/pay:
  access: authenticated
  description: "Record a tax payment"
  request:
    assessmentId: uuid
    walletId: uuid
    amount: decimal
  response:
    assessment: TaxAssessment
    remainingDue: decimal
    transaction: object
  emits: trade.tax.payment_received
  errors:
    - ASSESSMENT_NOT_FOUND
    - INSUFFICIENT_FUNDS
    - ALREADY_PAID
  notes: |
    Calls lib-currency /currency/debit internally.
    If collectionTarget is not "void", credits the target wallet.

/trade/tax/debt/get:
  access: user
  description: "Get total tax debt for an entity"
  request:
    taxpayerId: uuid
    realmId: uuid                   # Optional
  response: { debt: TaxDebt, assessments: [TaxAssessment] }

/trade/tax/debt/list-delinquent:
  access: admin
  description: "List entities with overdue tax debt"
  request:
    realmId: uuid
    minOwed: decimal                # Optional minimum debt
    minDaysOverdue: integer         # Optional minimum days overdue
    page: integer
    pageSize: integer
  response: { debts: [TaxDebt], totalCount: integer }
  notes: |
    Used by NPC tax collectors to determine who to visit.
    Also used by divine oversight for economic health monitoring.
```

### NPC Economics

```yaml
/trade/npc/profile/get:
  access: admin
  description: "Get an NPC's economic profile"
  request:
    characterId: uuid
  response: { profile: NpcEconomicProfile }
  errors:
    - PROFILE_NOT_FOUND

/trade/npc/profile/set:
  access: developer
  description: "Create or update an NPC's economic profile"
  request:
    characterId: uuid
    economicRole: string
    produces: [NpcProductionEntry]
    consumes: [NpcConsumptionEntry]
    tradingPersonality: NpcTradingPersonality
    homeLocationId: uuid
    tradingRadius: decimal
  response: { profile: NpcEconomicProfile }

/trade/npc/market-analysis:
  access: authenticated
  description: "Get a market analysis from an NPC's perspective (bounded by their knowledge)"
  request:
    characterId: uuid               # NPC performing analysis
    itemCodes: [string]             # Optional -- specific items to analyze
  response:
    localPrices: [{ templateCode, price, supply, demand }]
    knownOpportunities: [           # Filtered by NPC's tradingRadius and awareness
      {
        templateCode: string
        buyLocationId: uuid
        buyPrice: decimal
        sellLocationId: uuid
        sellPrice: decimal
        estimatedProfit: decimal
        transitHours: decimal
        risk: decimal
      }
    ]
    recommendations: [string]       # "Buy iron locally", "Ship swords to Riverside"
  notes: |
    Results are bounded by the NPC's economic profile:
    - Only considers locations within tradingRadius
    - Only considers items the NPC produces/consumes or knows about
    - priceAwareness affects accuracy of price data
    - NPC may not know about all opportunities (imperfect information)
    Integrates with Hearsay (L4) if available for belief-filtered price knowledge.
```

### Supply/Demand Queries

```yaml
/trade/supply-demand/snapshot:
  access: user
  description: "Get current supply/demand snapshot for a location"
  request:
    locationId: uuid
    itemCodes: [string]             # Optional -- specific items
  response: { snapshot: SupplyDemandSnapshot }

/trade/supply-demand/price-differential:
  access: user
  description: "Find price differentials between locations for an item"
  request:
    templateId: uuid
    realmId: uuid
    minDifferential: decimal        # Optional -- minimum price gap to report
  response:
    differentials: [
      {
        lowPriceLocationId: uuid
        lowPrice: decimal
        highPriceLocationId: uuid
        highPrice: decimal
        differential: decimal
        transitHours: decimal       # Via fastest mode
        transitCost: decimal
        netArbitrageProfit: decimal # differential - transitCost
        arbitrageViable: boolean    # netArbitrageProfit > 0
      }
    ]
  notes: |
    Calls lib-transit route/calculate internally for transit cost estimation.
    This is the data that NPC merchants use to decide where to trade.
```

### Velocity Metrics

```yaml
/trade/velocity/summary:
  access: admin
  description: "Get economic velocity metrics for a realm or location"
  request:
    realmId: uuid
    locationId: uuid                # Optional -- specific location
    currencyDefinitionId: uuid      # Optional -- specific currency
    period: string                  # "hour", "day", "week", "month" (game-time)
    granularity: string             # "hour", "day" (for time-series breakdown)
  response: { metrics: EconomicVelocityMetrics }

/trade/velocity/hotspots:
  access: admin
  description: "Find locations with unusually high or low economic velocity"
  request:
    realmId: uuid
    currencyDefinitionId: uuid      # Optional
  response:
    hotspots: [                     # Locations with velocity > healthy max
      {
        locationId: uuid
        locationCode: string
        velocity: decimal
        deviationFromMean: decimal
        trend: string               # "accelerating", "stable", "decelerating"
      }
    ]
    coldspots: [                    # Locations with velocity < healthy min
      {
        locationId: uuid
        locationCode: string
        velocity: decimal
        daysSinceLastTransaction: integer
        trend: string
      }
    ]
    realmAverage: decimal
    healthyRange: object            # { min: config, max: config }
  notes: |
    Used by divine economic actors to identify intervention targets.
    Hermes sees a coldspot → spawns business opportunity.
    Nemesis sees a hotspot → targets the speculator.

/trade/velocity/faucet-sink-balance:
  access: admin
  description: "Get faucet/sink balance for a realm's economy"
  request:
    realmId: uuid
    currencyDefinitionId: uuid      # Optional
    period: string                  # "day", "week", "month"
  response:
    totalFaucets: decimal
    faucetsByType: object           # { "quest_reward": 500, "loot_drop": 300, ... }
    totalSinks: decimal
    sinksByType: object             # { "vendor_purchase": 200, "tariff": 100, "tax": 50, ... }
    netFlow: decimal                # Positive = inflationary pressure
    trend: string                   # "inflationary", "deflationary", "balanced"
    inflationRisk: string           # "low", "moderate", "high", "critical"
```

**Total endpoints: 37**

---

## Events

### Published Events

```yaml
# Shipment lifecycle events
trade.shipment.created:
  payload: { shipmentId, routeId, ownerId, ownerType, carrierId, totalGoods, totalCurrency }

trade.shipment.departed:
  payload: { shipmentId, routeId, carrierId, transitJourneyId, estimatedArrivalGameTime, realmId }
  consumers:
    - Analytics (L4): shipment volume tracking
    - Puppetmaster (L4): regional watchers notice trade activity

trade.shipment.leg_completed:
  payload: { shipmentId, legIndex, remainingLegs, currentLocationId, incidentCount, realmId }

trade.shipment.border_crossed:
  payload: { shipmentId, legIndex, fromRealmId, toRealmId, tariffCollected, smugglingAttempted, smugglingDetected }
  consumers:
    - Analytics (L4): customs revenue tracking, smuggling rate monitoring
    - Puppetmaster (L4): gods monitoring smuggling patterns

trade.shipment.arrived:
  payload: { shipmentId, routeId, destinationLocationId, totalGameHours, netProfit, realmId }
  consumers:
    - Analytics (L4): trade volume, route profitability tracking
    - Market (L4): supply arrival updates price

trade.shipment.lost:
  payload: { shipmentId, routeId, reason, lostGoodsValue, lostCurrencyAmount, locationId, realmId }
  consumers:
    - Analytics (L4): route risk statistics
    - Puppetmaster (L4): narrative opportunities from shipment losses

# Tariff events
trade.tariff.collected:
  payload: { tariffRecordId, policyId, shipmentId, amount, realmId }
  consumers:
    - Analytics (L4): customs revenue tracking

trade.tariff.evaded:
  payload: { tariffRecordId, policyId, shipmentId, estimatedAmount, realmId }
  consumers:
    - Analytics (L4): smuggling success rate
    - Puppetmaster (L4): gods notice smuggling patterns

# Tax events
trade.tax.assessment_created:
  payload: { assessmentId, policyId, taxpayerId, taxpayerType, taxDue, dueDate, realmId }
  # NOTE: Actor (L2) cannot subscribe to this L4 event per T27.
  # Tax collector NPCs receive this data via ${trade.tax_debt.*} Variable Provider
  # or Trade calls Actor's API directly (L4→L2 is valid direction).

trade.tax.payment_received:
  payload: { assessmentId, taxpayerId, amount, remainingDue, realmId }

trade.tax.debt_defaulted:
  payload: { taxpayerId, taxpayerType, totalOwed, realmId, suggestedConsequences }
  consumers:
    - Puppetmaster (L4): debt collection narratives, arrest warrants
    - Obligation (L4): creates enforcement obligations

# Economic signal events
trade.velocity.alert:
  payload: { realmId, locationId, currencyDefinitionId, velocity, threshold, alertType }
  alertType: "stagnant" | "overheated" | "critical_imbalance"
  consumers:
    - Puppetmaster (L4): divine economic intervention triggers

trade.supply_demand.shift:
  payload: { locationId, realmId, templateId, previousSupplyLevel, newSupplyLevel, priceChange }
  consumers:
    - Market (L4): vendor catalog pricing adjustment
  # NOTE: Actor (L2) cannot subscribe to this L4 event per T27.
  # NPC economic behavior accesses supply/demand data via ${trade.supply.*}
  # and ${trade.demand.*} Variable Provider (pull-based, hierarchy-safe).

# Route events
trade.route.created:
  payload: { routeId, code, ownerId, ownerType, realmId, legCount }

trade.route.status_changed:
  payload: { routeId, code, previousStatus, newStatus, reason, realmId }
  # NOTE: Actor (L2) cannot subscribe to this L4 event per T27.
  # Merchant NPCs access route status via ${trade.*} Variable Provider
  # or Trade provides a DI Listener interface for route change notifications.
```

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `transit.connection.status_changed` | `HandleTransitConnectionStatusChangedAsync` | Updates trade route viability when a Transit connection changes status (closed, seasonal, etc.) |
| Market price events (soft, when available) | `HandleMarketPriceChangedAsync` | Enriches supply/demand snapshots with current Market price data |

---

## Configuration

```yaml
# schemas/trade-configuration.yaml
TradeServiceConfiguration:
  properties:
    HealthyVelocityMin:
      type: number
      default: 0.3
      description: "Minimum velocity considered healthy. Below this triggers stagnation alerts."
      env: TRADE_HEALTHY_VELOCITY_MIN

    HealthyVelocityMax:
      type: number
      default: 4.0
      description: "Maximum velocity considered healthy. Above this triggers overheating alerts."
      env: TRADE_HEALTHY_VELOCITY_MAX

    VelocityCalculationIntervalSeconds:
      type: integer
      default: 300
      description: "How often the velocity worker recomputes metrics (real-time seconds)"
      env: TRADE_VELOCITY_CALCULATION_INTERVAL_SECONDS

    SupplyDemandRefreshIntervalSeconds:
      type: integer
      default: 120
      description: "How often supply/demand snapshots are recomputed (real-time seconds)"
      env: TRADE_SUPPLY_DEMAND_REFRESH_INTERVAL_SECONDS

    DefaultTransitCostPerKmPerKg:
      type: number
      default: 0.01
      description: "Default cost per km per kg of cargo for transit cost estimation"
      env: TRADE_DEFAULT_TRANSIT_COST_PER_KM_PER_KG

    MaxShipmentLegs:
      type: integer
      default: 12
      description: "Maximum legs a trade route can have"
      env: TRADE_MAX_SHIPMENT_LEGS

    ShipmentExpirationGameDays:
      type: number
      default: 30.0
      description: "Game-days after which a 'preparing' shipment is auto-expired"
      env: TRADE_SHIPMENT_EXPIRATION_GAME_DAYS

    TaxAssessmentWorkerIntervalSeconds:
      type: integer
      default: 600
      description: "How often the tax assessment worker runs (real-time seconds)"
      env: TRADE_TAX_ASSESSMENT_WORKER_INTERVAL_SECONDS

    PriceDifferentialMinPercent:
      type: number
      default: 10.0
      description: "Minimum price differential percentage to report as an arbitrage opportunity"
      env: TRADE_PRICE_DIFFERENTIAL_MIN_PERCENT

    VelocityHistoryRetentionGameDays:
      type: integer
      default: 90
      description: "How many game-days of velocity history to retain"
      env: TRADE_VELOCITY_HISTORY_RETENTION_GAME_DAYS
```

---

## DI Services & Helpers

*Aspirational -- no implementation exists. Expected dependencies based on design:*

| Service | Role |
|---------|------|
| `ILogger<TradeService>` | Structured logging |
| `TradeServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for routes, shipments, policies, profiles, metrics |
| `IMessageBus` | Event publishing for shipment lifecycle, tariff, tax, velocity events |
| `IEventConsumer` | Event handler registration for transit connection status changes |
| `IDistributedLockProvider` | Distributed locking for shipment state transitions |
| `ITransitClient` | Transit connection validation, journey creation, route calculation |
| `ICurrencyClient` | Tariff/tax debit/credit operations, wallet balance queries |
| `IItemClient` | Item instance validation, template lookup |
| `IInventoryClient` | Cargo transfer, local stock queries |
| `IWorldstateClient` | Game-time queries for timestamps and period boundaries |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (Escrow, Faction, Environment, Analytics, Market, Hearsay) |
| `IVariableProviderFactory` (implements) | Provides `${trade.*}` namespace to Actor (L2) |

---

## State Stores

```yaml
# schemas/state-stores.yaml additions

trade-routes:
  backend: mysql
  description: "Trade route definitions with legs (durable)"
  key_format: "trade:route:{routeId}"
  indexes:
    - realmId
    - ownerId
    - code
    - status

trade-shipments:
  backend: redis
  description: "Active shipments (hot state)"
  key_format: "trade:shipment:{shipmentId}"
  notes: |
    Active and recently-completed shipments in Redis for fast access.
    Archived to MySQL by background worker after completion.

trade-shipments-archive:
  backend: mysql
  description: "Archived shipments (historical record)"
  key_format: "trade:shipment:archive:{shipmentId}"
  indexes:
    - routeId
    - ownerId
    - carrierId
    - realmId
    - status
    - departedAtGameTime

trade-tariff-policies:
  backend: mysql
  description: "Tariff policy definitions (durable)"
  key_format: "trade:tariff:policy:{policyId}"
  indexes:
    - realmId
    - scope
    - status

trade-tariff-records:
  backend: mysql
  description: "Tariff collection records (durable audit trail)"
  key_format: "trade:tariff:record:{recordId}"
  indexes:
    - shipmentId
    - policyId
    - realmId

trade-contraband:
  backend: mysql
  description: "Contraband definitions (durable)"
  key_format: "trade:contraband:{definitionId}"
  indexes:
    - realmId
    - type

trade-tax-policies:
  backend: mysql
  description: "Tax policy definitions (durable)"
  key_format: "trade:tax:policy:{policyId}"
  indexes:
    - realmId
    - taxType
    - status

trade-tax-assessments:
  backend: mysql
  description: "Tax assessment records (durable)"
  key_format: "trade:tax:assessment:{assessmentId}"
  indexes:
    - taxpayerId
    - policyId
    - realmId
    - status
    - dueDate

trade-npc-profiles:
  backend: mysql
  description: "NPC economic profiles (durable)"
  key_format: "trade:npc:profile:{characterId}"
  indexes:
    - economicRole
    - homeLocationId

trade-supply-demand:
  backend: redis
  description: "Computed supply/demand snapshots (ephemeral, recomputed periodically)"
  key_format: "trade:supply:{locationId}"
  ttl: 300  # 5 minutes, refreshed by worker

trade-velocity:
  backend: redis
  description: "Computed velocity metrics (ephemeral, recomputed periodically)"
  key_format: "trade:velocity:{realmId}:{currencyDefinitionId}:{locationId}"
  ttl: 600  # 10 minutes, refreshed by worker

trade-velocity-history:
  backend: mysql
  description: "Historical velocity metrics for trend analysis"
  key_format: "trade:velocity:history:{realmId}:{periodStart}"
  indexes:
    - realmId
    - currencyDefinitionId
    - periodStart
```

---

## Visual Aid

**Shipment Lifecycle (Multi-Step Operation Flow)**

```
CREATED ──► LOADING ──► IN_TRANSIT ──► ARRIVED ──► COMPLETED
   │           │            │             │
   │ items     │ departure  │ journey     │ unload
   │ reserved  │ confirmed  │ tracked     │ + settle
   │ via       │ via        │ via Transit │ currency
   │ Escrow    │ Worldstate │ connection  │ via
   │           │ clock tick │ traversal   │ Currency
   │           │            │             │
   ▼           ▼            ▼             ▼
CANCELLED  CANCELLED    LOST/         DISPUTED
(refund    (refund +    DELAYED       (partial
 escrow)    restock)    (risk event   damage →
                         from Env)    Escrow
                                      arbitration)
```

This diagram shows the state machine for a single shipment, which is the core multi-step operation. Each transition involves a different L2/L4 service (Escrow for custody, Worldstate for game clock, Transit for movement, Currency for settlement). The failure branches illustrate how environmental events and disputes interact with the happy path. The dependency diagram is already in the Core Architecture section above.

---

## Variable Provider (`${trade.*}`)

Trade implements `IVariableProviderFactory` providing the `${trade}` namespace to Actor (L2) via the Variable Provider Factory pattern.

### Available Variables

```yaml
# Route and shipment state
${trade.shipment.active_count}:
  type: integer
  description: "Number of active shipments owned by this entity"

${trade.shipment.total_value_in_transit}:
  type: decimal
  description: "Total value of goods currently in transit for this entity"

# Supply/demand at current location
${trade.supply.<item_code>.local}:
  type: string
  description: "Supply level at entity's current location"
  values: "scarce | low | normal | abundant | oversupplied"
  example: "${trade.supply.iron_ingot.local}"

${trade.demand.<item_code>.local}:
  type: string
  description: "Demand level at entity's current location"
  values: "none | low | normal | high | desperate"
  example: "${trade.demand.iron_sword.local}"

${trade.price.<item_code>.local}:
  type: decimal
  description: "Average price of item at entity's current location"
  example: "${trade.price.iron_ingot.local}"

# Arbitrage opportunities (filtered by NPC awareness)
${trade.opportunity.best_profit}:
  type: decimal
  description: "Estimated profit of the best known trade opportunity"

${trade.opportunity.best_item}:
  type: string
  description: "Item code of the best known trade opportunity"

${trade.opportunity.best_destination}:
  type: string
  description: "Location code of the best destination for trade"

# Economic health at current location
${trade.velocity.local}:
  type: decimal
  description: "Economic velocity at entity's current location"

${trade.velocity.local_health}:
  type: string
  description: "Economic health assessment"
  values: "stagnant | sluggish | healthy | overheated | critical"

# Tax state
${trade.tax_debt.total}:
  type: decimal
  description: "Total outstanding tax debt for this entity"

${trade.tax_debt.overdue}:
  type: boolean
  description: "Does this entity have overdue tax debt?"

# NPC economic profile
${trade.role}:
  type: string
  description: "Entity's economic role (merchant, craftsman, farmer, etc.)"

${trade.net_worth}:
  type: decimal
  description: "Entity's estimated net worth"

${trade.daily_profit}:
  type: decimal
  description: "Entity's average daily revenue minus expenses"

${trade.trading_radius}:
  type: decimal
  description: "How far this entity is willing to travel for trade (game-km)"
```

### GOAP Integration Example

```yaml
# Merchant NPC evaluating whether to make a trade run

goap_action: trade_run_to_capital
  preconditions:
    ${trade.role}: "== merchant"
    ${trade.opportunity.best_profit}: "> 100"          # Minimum worthwhile profit
    ${transit.nearest.capital.hours}: "< 24"            # Can reach within a game-day
    ${trade.supply.iron_ingot.local}: "!= scarce"       # Have local supply to buy
    ${trade.shipment.active_count}: "< 3"               # Not already overcommitted

  effects:
    gold_reserves: "+${trade.opportunity.best_profit}"
    ${trade.shipment.active_count}: "+1"
    at_location: "capital"

  cost: |
    base_trade_cost (3.0)
    + ${transit.nearest.capital.hours} × time_weight (0.2)
    + ${transit.mode.${transit.nearest.capital.best_mode}.preference_cost} × fear_weight (3.0)
    + (1.0 - ${personality.risk_tolerance}) × risk_weight (2.0)

  execution:
    - buy_goods:
        item: "${trade.opportunity.best_item}"
        quantity: "${calculate_affordable_quantity()}"
        location: "current"
    - create_shipment:
        route: "${find_route_to(trade.opportunity.best_destination)}"
        goods: "${purchased_goods}"
    - travel:
        destination: "${trade.opportunity.best_destination}"
        mode: "${transit.nearest.${destination}.best_mode}"
```

---

## Background Services

### Velocity Calculation Worker

```yaml
VelocityCalculationWorker:
  interval: config.VelocityCalculationIntervalSeconds
  description: |
    Periodically computes economic velocity metrics per realm, currency,
    and location. Queries lib-currency transaction history and computes:
    - Transaction volume / average stock = velocity
    - Faucet/sink breakdown by source type
    - Gini coefficient for wealth distribution
    - Hotspot/coldspot identification

    Publishes trade.velocity.alert events when velocity exits the healthy
    range (config.HealthyVelocityMin to config.HealthyVelocityMax).

    Results are cached in Redis (trade-velocity) and archived to MySQL
    (trade-velocity-history) for trend analysis.

  dependencies:
    - lib-currency: transaction queries, wallet balance queries
    - Worldstate (L2): game-time period boundaries
```

### Supply/Demand Snapshot Worker

```yaml
SupplyDemandSnapshotWorker:
  interval: config.SupplyDemandRefreshIntervalSeconds
  description: |
    Periodically recomputes supply/demand snapshots per location.
    Aggregates data from:
    - NPC economic profiles (production/consumption rates)
    - Active shipments (inbound/outbound flow)
    - Market listings (available supply and recent prices)
    - Inventory queries (local stock levels)

    Publishes trade.supply_demand.shift events when significant changes occur.

    Results cached in Redis (trade-supply-demand) for fast variable
    provider access.

  dependencies:
    - NPC profile store: production/consumption rates
    - Active shipments store: flow rates
    - lib-market (L4, optional): price and listing data
    - lib-inventory (L2): local stock queries
```

### Tax Assessment Worker

```yaml
TaxAssessmentWorker:
  interval: config.TaxAssessmentWorkerIntervalSeconds
  description: |
    Processes assessed-mode tax policies. For each policy with
    assessmentFrequency, checks if the current game-time has crossed
    the next assessment boundary. If so, generates assessments for
    all eligible taxpayers in the policy's realm.

    For wealth taxes: queries lib-currency wallet balances.
    For property taxes: queries lib-inventory container ownership.
    For income taxes: queries transaction history since last assessment.

    Publishes trade.tax.assessment_created for each new assessment.
    NPC tax collectors subscribe to these events to begin collection.

  dependencies:
    - Worldstate (L2): game-time for period boundaries
    - lib-currency (L2): wallet balance queries
    - lib-inventory (L2): property/ownership queries
```

### Shipment Expiration Worker

```yaml
ShipmentExpirationWorker:
  interval: 300  # Every 5 minutes (real-time)
  description: |
    Scans for shipments in "preparing" status older than
    config.ShipmentExpirationGameDays. Expires them and returns
    any escrowed goods to the owner.

    Also archives completed/lost/seized shipments from Redis to
    MySQL for historical analysis.
```

---

## Integration Points

### Transit (L2) -- Hard Dependency

Trade routes are economic overlays on Transit connections. Every trade route leg maps to a Transit connection. Trade calls Transit for:
- **Route creation**: Validates Transit connections exist for each leg
- **Travel time estimation**: `/transit/route/calculate` for cost estimation
- **Shipment movement**: Creates Transit journeys for carriers
- **Connection status**: Subscribes to `transit.connection.status_changed` to update trade route viability

### Currency (L2) -- Hard Dependency

Trade uses Currency for:
- **Tariff collection**: `/currency/debit` for tariff payment
- **Tax collection**: `/currency/debit` for tax payment
- **Revenue credits**: `/currency/credit` for tariff/tax target wallets
- **Velocity metrics**: Queries transaction history for velocity computation
- **Wallet balance**: Queries for wealth tax assessment

### Item (L2) + Inventory (L2) -- Hard Dependencies

Trade uses Item/Inventory for:
- **Shipment goods**: Validates item instances exist and are transferable
- **Custody during transport**: Transfers items to carrier/escrow inventory
- **Stock level queries**: Counts items at locations for supply/demand snapshots
- **Contraband checking**: Matches item templates against contraband definitions

### Worldstate (L2) -- Hard Dependency

Trade uses Worldstate for:
- **Game-time timestamps**: All shipment/journey/assessment times are game-time
- **Seasonal trade routes**: Route availability tied to Worldstate seasons
- **Tax assessment periods**: Assessment boundaries computed from game calendar
- **Velocity periods**: Metric windows aligned to game-time boundaries

### Escrow (L4) -- Soft Dependency

When `custodyMode: "escrow"`, Trade uses Escrow for safe custody during transport. If Escrow is not available, Trade falls back to carrier-based custody or virtual mode.

### Faction (L4) -- Soft Dependency

When available, Faction provides:
- **Border sovereignty**: Which faction controls a border checkpoint
- **Tariff policy ownership**: Factions can set their own tariff rates
- **Diplomatic exemptions**: Allied factions may have reduced tariffs
- **Contraband variation**: Different factions ban different items

### Environment (L4) -- Soft Dependency

When available, Environment provides:
- **Seasonal resource availability**: Modulates supply/demand snapshots
- **Weather risk modifiers**: Storm warnings increase shipment risk assessment
- **Spoilage factors**: Perishable goods lose value during transit in hot weather

### Analytics (L4) -- Soft Dependency

When available, Analytics provides:
- **Historical transaction data**: Richer velocity computation with longer lookback
- **Player behavior patterns**: Distinguish NPC vs player economic activity
- **Event correlation**: Link economic changes to narrative events

### Market (L4) -- Soft Dependency

When available, Market provides:
- **Price data**: Current auction/vendor prices for supply/demand snapshots
- **Listing counts**: Active listings as supply indicators
- **Price change events**: Real-time price signal updates

### Hearsay (L4) -- Soft Dependency

When available, Hearsay provides:
- **Belief-filtered price knowledge**: NPC market analysis results bounded by what the NPC has heard or experienced, not omniscient price data
- **Rumor propagation**: Trade opportunity information spreads through social networks, creating imperfect information asymmetry between NPCs

### Divine Economic Intervention Integration

The velocity monitoring system is designed to feed god actors (via Puppetmaster):

```
Trade velocity worker computes metrics
    ↓
trade.velocity.alert event published
    ↓
Hermes (god actor via Puppetmaster) subscribes
    ↓
Hermes GOAP evaluates:
  Current state: riverside_village velocity = 0.08 (stagnant)
  Goal: maintain_healthy_velocity (all locations > 0.3)
  Action: spawn_business_opportunity
    Precondition: target_velocity < 0.3
    Effect: target_velocity += 0.4 (estimated)
    ↓
Hermes spawns event:
  "A wealthy merchant caravan from the capital seeks local goods"
  NPC merchant Actor receives opportunity → GOAP evaluates trade run
  Trade creates shipment → goods flow → velocity increases
    ↓
Next velocity calculation: riverside_village = 0.45 (healthy)
Hermes satisfied → moves attention elsewhere
```

---

## Scale Considerations

### Shipment Volume

With 100,000+ NPCs and perhaps 5% acting as merchants, that's ~5,000 NPC merchants. If each maintains 1-2 active shipments, the system handles ~10,000 concurrent shipments in Redis. At ~2KB per shipment document, total Redis footprint is ~20MB -- negligible.

Shipment lifecycle events (departed, leg_completed, arrived) at ~10,000 shipments with an average journey of 3 legs = ~40,000 events per game-day. At 24:1 time ratio, that's ~1,700 events per real-hour, well within RabbitMQ capacity.

### Velocity Computation

Velocity computation queries Currency transaction history. With realm-scoped computation and configurable intervals (default 5 minutes), each computation scans transactions for the configured period window. At 100 locations per realm and 10 currencies, that's ~1,000 velocity computations per interval -- batched into a single worker cycle.

### Supply/Demand Snapshots

Supply/demand snapshots query NPC profiles, inventory levels, and shipment data per location. With 100 locations and 50 tracked item types per location, that's 5,000 supply/demand entries refreshed every 2 minutes. Each is a small Redis document (~200 bytes). Total footprint: ~1MB.

### Tax Assessment

Tax assessment runs against eligible taxpayers per realm per policy. With progressive brackets and exemption checking, each assessment is a Currency wallet balance query + a calculation. Batch processing with fair scheduling prevents thundering herd on assessment boundaries.

---

## Stubs & Unimplemented Features

The entire Trade service is aspirational. No schema files, generated code, or service implementation exist.

**Before implementation, the following must be created:**
1. `schemas/trade-api.yaml` -- API schema with all endpoint definitions
2. `schemas/trade-events.yaml` -- Event schemas with x-lifecycle and x-event-publications
3. `schemas/trade-configuration.yaml` -- Configuration schema
4. State store entries in `schemas/state-stores.yaml`
5. Code generation via `cd scripts && ./generate-service.sh trade`
6. `TradeService.cs` -- Business logic implementation
7. `TradeServiceEvents.cs` -- Event handler implementations
8. `TradeServiceModels.cs` -- Internal storage models
9. `TradeVariableProviderFactory.cs` -- IVariableProviderFactory implementation for `${trade.*}`
10. Background workers: VelocityCalculationWorker, SupplyDemandSnapshotWorker, TaxAssessmentWorker, ShipmentExpirationWorker

---

## Potential Extensions

1. **Insurance system**: Pay a premium to offset shipment loss risk. Insurance policies are Contract instances with prebound refund clauses. Insurance companies (Organizations) set premiums based on route risk profiles and shipment value. Divine economic actors may operate insurance schemes.

2. **Merchant guild integration**: Organization-owned trade routes with exclusive or preferential access. Guild members pay reduced tariffs; non-members pay a premium. Guild seed growth from trade volume on guild routes.

3. **Dynamic tariff adjustment**: Faction norms (via lib-faction) influence tariff rates based on political relationships. War increases tariffs. Peace treaties reduce them. Trade agreements create bilateral exemptions. NPC customs officers with Obligation integration may accept bribes to reduce effective tariff collection.

4. **Commodity futures**: Contract-backed forward purchases at fixed prices. A farmer sells next season's wheat harvest at today's price, hedging against price drops. Implemented as Contract instances with prebound Currency transfers triggered by Worldstate season boundaries.

5. **Caravan company franchising**: Organization expansion via licensed branches at different locations. Trade route networks owned by merchant companies with employee NPCs running shipments. Seed growth from trade volume. Company reputation affects NPC willingness to use their services.

6. **Smuggler networks**: Hearsay-propagated knowledge of smuggling routes. NPCs with low honesty and high risk tolerance prefer smuggler paths that bypass checkpoints. Smuggler reputation (via Character Encounter) affects detection probability. Gods monitoring smuggling rates may crack down or look the other way based on personality.

7. **Economic sanctions**: Faction-level trade embargoes. Contraband lists extend to all goods from a specific realm or faction. Smuggling becomes the primary supply channel for embargoed goods, creating a black market economy with inflated prices.

8. **Price history integration**: Time-series price data for items at locations, enabling trend analysis ("iron has been climbing for 3 game-weeks"), seasonal pattern detection, and more sophisticated NPC trading strategies.

---

## Known Quirks & Caveats

#### Bugs (Fix Immediately)

*No code exists. No bugs to report.*

#### Intentional Quirks (Documented Behavior)

1. **Variable Provider instead of event subscriptions for Actor**: Actor (L2) cannot subscribe to Trade (L4) events per IMPLEMENTATION TENETS (cross-service communication discipline). NPC economic behavior accesses supply/demand, velocity, and trade state via `${trade.*}` Variable Provider (pull-based, hierarchy-safe). Similarly, Currency does not subscribe to tariff events -- Trade calls Currency's API directly.

2. **Declarative shipment lifecycle**: Trade does not automatically progress shipments, collect tariffs, or assess taxes. The game server or NPC Actor brains call the APIs to indicate what happened. Trade records state and publishes events. This is consistent with the Bannou pattern where plugins provide primitives, not policy.

3. **Three custody modes**: Shipments support "escrow" (via lib-escrow), "carrier" (carrier's inventory), and "virtual" (game manages custody externally). The `custodyMode` per-shipment choice follows the three-tier usage pattern: simple games use "escrow", complex games use "virtual".

#### Design Considerations (Requires Planning)

1. **T25: Enum type safety (MUST FIX at schema creation time)**: The data models use `string` with inline value comments for 17 fields that must be proper schema-defined enums. See enum table below.

2. **T25: Untyped object fields (MUST FIX at schema creation time)**: Five fields use `type: object` without property definitions (`riskProfile`, `seasonalAvailability`, `faucetsByType`, `sinksByType`, `riskAssessment`). Need proper typed schemas or explicit `additionalProperties: true` metadata bag documentation.

3. **T28: Resource cleanup endpoints missing**: Trade stores NPC economic profiles keyed by `characterId` (L2 entity) and tariff/tax/contraband policies keyed by `realmId`. Per FOUNDATION TENETS, Trade must register references via lib-resource and implement cleanup callback endpoints (e.g., `/trade/cleanup-by-character`, `/trade/cleanup-by-realm`). No cleanup endpoints are currently defined in the API section.

4. **NPC profile ownership**: Should NPC economic profiles live in Trade or in a shared location? **Recommendation**: Trade owns economic profiles because they're inseparable from trade logistics. The profile is consumed by the Trade variable provider and GOAP integration. Other services that need economic data query Trade.

5. **Velocity data source**: Should velocity come from direct Currency transaction queries or from Analytics event subscriptions? **Recommendation**: Direct Currency queries for accuracy. Analytics can provide supplementary data when available, but velocity computation must work without Analytics (graceful degradation).

6. **Tax enforcement scope**: Should Trade enforce tax consequences (asset seizure, imprisonment) or just report delinquency? **Recommendation**: Advisory only. Trade reports debt and suggests consequences. The game (or NPC actors via Obligation) decides enforcement.

7. **Cross-realm currency conversion**: Should trade routes crossing realm boundaries use automatic Currency conversion? **Recommendation**: No automatic conversion. The game or NPC decides when and how to convert currency. Trade records the border crossing and reports applicable exchange rates.

8. **Relationship with lib-market**: Market handles exchange at a point (auctions, vendors). Trade handles logistics between points. They share supply/demand concepts but own different data. **Recommendation**: Trade subscribes to Market's price events for enrichment. Market subscribes to Trade's shipment arrival events for supply updates. Neither depends on the other.

9. **Workshop integration approach**: **Recommendation**: No direct integration. Workshop deposits outputs into inventory. Trade reads inventory levels for supply/demand. NPC GOAP connects them behaviorally: "Workshop produced iron → inventory full → GOAP goal: sell_surplus → create shipment."

10. **Contract as potential dependency**: Contract (L1) is not required for core Trade functionality but may be needed for extensions (insurance policies, commodity futures). Contract was removed from hard dependencies as the core design uses Currency directly for tariff/tax collection and Escrow for custody.

### Enum Reference (T25)

Fields that must become proper enums at schema creation time:

| Model | Field | Values | Suggested Enum Name |
|-------|-------|--------|---------------------|
| TradeRoute | status | active, closed, dangerous, seasonal | TradeRouteStatus |
| Shipment | status | preparing, in_transit, at_checkpoint, arrived, lost, seized, abandoned | ShipmentStatus |
| Shipment | custodyMode | escrow, carrier, virtual | CustodyMode |
| ShipmentIncident | incidentType | bandit_attack, storm, customs_inspection, spoilage, breakdown, smuggling_detected | ShipmentIncidentType |
| ShipmentIncident | outcome | escaped, lost_cargo, delayed, seized | IncidentOutcome |
| TariffPolicy | scope | realm_wide, specific_border, category | TariffScope |
| TariffPolicy | direction | import, export, both | TariffDirection |
| TariffPolicy | collectionMode | automatic, assessed, advisory | CollectionMode |
| TariffPolicy | status | active, suspended, wartime | TariffPolicyStatus |
| TariffRecord | collectionStatus | pending, collected, partial, evaded, exempt | TariffCollectionStatus |
| TariffRecord | borderType | internal, external | BorderType |
| TariffExemption | exemptionType | full, partial | ExemptionType |
| ContrabandDefinition | severity | restricted, prohibited, capital_offense | ContrabandSeverity |
| TaxPolicy | taxType | transaction, income, property, wealth, sales, custom | TaxType |
| TaxPolicy | collectionTarget | void, realm_treasury, faction_treasury | TaxCollectionTarget |
| TaxAssessment | status | pending, partial, paid, overdue, defaulted | TaxAssessmentStatus |
| EconomicVelocityMetrics | velocityTrend | accelerating, stable, decelerating, stagnant | VelocityTrend |

For polymorphic `ownerType`/`carrierType`/`taxpayerType` fields: use the shared `EntityType` enum from `common-api.yaml` where values are known, or use opaque strings only if the set must remain extensible across layers (per IMPLEMENTATION TENETS exception #3).

### Untyped Object Reference (T25)

| Model | Field | Fix |
|-------|-------|-----|
| TradeRoute | riskProfile | Define `RiskProfile` schema with typed risk-level counts |
| TradeRoute | seasonalAvailability | Define typed array of `SeasonalAvailabilityEntry` (like Transit does) |
| EconomicVelocityMetrics | faucetsByType | Define `FaucetBreakdown` with known faucet type keys |
| EconomicVelocityMetrics | sinksByType | Define `SinkBreakdown` with known sink type keys |
| Route estimate-cost | riskAssessment | Define `RiskAssessment` schema with `expectedLoss`, `worstCase` |

---

## Work Tracking

*No active work items. Service is aspirational.*

---

## Appendix: How Trade Relates to the Economy Architecture

This deep dive absorbs and refines concepts from `docs/guides/ECONOMY-SYSTEM.md` and `docs/plans/ITEM-ECONOMY-PLUGINS.md`:

| Economy Concern | Where It Lives |
|-----------------|----------------|
| Market (auctions, vendor catalogs, price discovery) | **lib-market** (separate aspirational plugin) |
| Economy Orchestration (velocity, faucet/sink monitoring) | **lib-trade** velocity metrics + faucet/sink monitoring |
| NPC Economic Participation (profiles, GOAP) | **lib-trade** NPC economic profiles + GOAP integration |
| Price Queries / Metrics | Split: price queries → lib-market, metrics/NPC → **lib-trade** |
| Quest Integration | **lib-quest** (already implemented, uses Currency/Item directly) |
| ABML Action Handlers | Multiple services register their own handlers |
| Money Velocity | **lib-trade** velocity worker + divine intervention integration |
| Trade Routes + Shipments | **lib-trade** trade route + shipment lifecycle |
| Tariffs + Contraband | **lib-trade** tariff policy + collection + contraband definitions |
| Exchange Rates | **lib-currency** extension (not in Trade) |
| Taxation | **lib-trade** tax policy + assessment + collection |

**Key refinement**: The economy system guide envisions velocity monitoring and NPC economic participation as cross-cutting concerns. This deep dive absorbs those into Trade because velocity monitoring, supply/demand signals, and NPC economic intelligence are inseparable from logistics operations. A separate lib-economy would be a thin wrapper with no unique data ownership.

**Key dependency**: Transit (L2) provides the travel infrastructure. Trade routes are economic overlays on Transit connections, providing the game-time travel calculation that makes distance-based economics work.

---

*This document is an aspirational deep dive for a service that does not yet exist. No schema files or implementation code have been created. The design follows established Bannou patterns, respects the service hierarchy, and absorbs economy system concepts from the architecture guide. Implementation requires schemas, code generation, service implementation, and testing -- in that order.*
