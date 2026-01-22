# lib-currency Service Specification

> **Version**: 1.1.0
> **Status**: Implementation Ready
> **Created**: 2026-01-22
> **Updated**: 2026-01-22
> **Dependencies**: lib-state, lib-messaging, lib-escrow, lib-analytics (optional)

This document is the implementation-ready specification for the `lib-currency` service. It defines all schemas, APIs, events, and configuration options.

---

## Table of Contents

1. [Overview](#overview)
2. [Plugin Configuration](#plugin-configuration)
3. [Currency Definition](#currency-definition)
4. [Wallets](#wallets)
5. [Balances](#balances)
6. [Transactions](#transactions)
7. [Autogain Processing](#autogain-processing)
8. [Currency Conversion](#currency-conversion)
9. [API Endpoints](#api-endpoints)
10. [Events](#events)
11. [State Stores](#state-stores)
12. [Error Codes](#error-codes)
13. [lib-escrow Integration](#lib-escrow-integration)

---

## 1. Overview

lib-currency provides foundational currency management for any game type:

- **Multi-currency support** with per-currency configuration
- **Polymorphic wallet ownership** (accounts, characters, NPCs, guilds, locations, etc.)
- **Event-sourced transactions** for complete audit trail
- **lib-escrow integration** for multi-party agreements (trades, auctions, contracts)
- **Autogain** (energy regeneration, interest) with simple or compound calculation
- **Base currency conversion** for simple exchange rate math

### What lib-currency Does NOT Do

- Escrow agreement management (lib-escrow - lib-currency implements the lock/unlock/transfer handlers)
- Inventory slot management for physical currency (game responsibility)
- Complex location-based exchange rates (lib-economy/lib-market)
- Taxation (lib-economy)
- Trade/auction mechanics (lib-market)

---

## 2. Plugin Configuration

Environment-based configuration for the entire plugin.

```yaml
# Schema: CurrencyPluginConfiguration
# Environment prefix: CURRENCY_

CurrencyPluginConfiguration:
  # ═══════════════════════════════════════════════════════════════
  # DEFAULT BEHAVIORS
  # ═══════════════════════════════════════════════════════════════

  # Default for currencies that don't specify allowNegative
  # Environment: CURRENCY_DEFAULT_ALLOW_NEGATIVE
  defaultAllowNegative: boolean
  default: false

  # Default precision for currencies that don't specify
  # Environment: CURRENCY_DEFAULT_PRECISION
  defaultPrecision: CurrencyPrecision
  default: decimal_2

  # ═══════════════════════════════════════════════════════════════
  # AUTOGAIN PROCESSING
  # ═══════════════════════════════════════════════════════════════

  # How autogain is calculated
  # Environment: CURRENCY_AUTOGAIN_PROCESSING_MODE
  autogainProcessingMode: AutogainProcessingMode
  default: lazy
  values:
    - lazy:
        description: |
          Autogain is calculated on-demand when balance is queried.
          Events are emitted at query time, not at the actual interval.
          Pro: No background task, simpler infrastructure
          Con: Events are delayed until something queries the balance
    - task:
        description: |
          Background task processes autogain at regular intervals.
          Events are emitted close to the actual interval time.
          Pro: Timely events for consumers
          Con: Requires background task infrastructure

  # For task mode: how often to process autogain
  # Environment: CURRENCY_AUTOGAIN_TASK_INTERVAL
  autogainTaskInterval: duration
  default: "PT1M"  # 1 minute

  # For task mode: batch size per processing cycle
  # Environment: CURRENCY_AUTOGAIN_BATCH_SIZE
  autogainBatchSize: integer
  default: 1000

  # ═══════════════════════════════════════════════════════════════
  # TRANSACTION HISTORY
  # ═══════════════════════════════════════════════════════════════

  # How long to retain detailed transaction history
  # Environment: CURRENCY_TRANSACTION_RETENTION
  transactionRetention: duration
  default: "P365D"  # 1 year

  # ═══════════════════════════════════════════════════════════════
  # IDEMPOTENCY
  # ═══════════════════════════════════════════════════════════════

  # How long to cache idempotency keys
  # Environment: CURRENCY_IDEMPOTENCY_TTL
  idempotencyTtl: duration
  default: "PT1H"  # 1 hour
```

---

## 3. Currency Definition

Defines a type of currency and its behavior rules.

```yaml
# Schema: CurrencyDefinition
CurrencyDefinition:
  # ═══════════════════════════════════════════════════════════════
  # IDENTIFICATION
  # ═══════════════════════════════════════════════════════════════

  id: uuid

  # Unique code for this currency (immutable after creation)
  code: string
  pattern: "^[a-z][a-z0-9_]{1,31}$"
  examples: ["gold", "premium_gems", "energy", "realm_token_eldoria"]

  name: string
  description: string

  # ═══════════════════════════════════════════════════════════════
  # REALM SCOPING
  # ═══════════════════════════════════════════════════════════════

  scope: CurrencyScope
  values:
    - global:
        description: "Available in all realms, typically account-level"
    - realm_specific:
        description: "Only exists in one realm"
    - multi_realm:
        description: "Available in selected realms"

  # For realm_specific: the single realm
  # For multi_realm: list of realms where this currency exists
  # For global: null
  realmsAvailable: [uuid]
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # PRECISION
  # ═══════════════════════════════════════════════════════════════

  # How this currency handles decimal values (immutable after creation)
  precision: CurrencyPrecision
  values:
    - integer:
        description: "Whole numbers only (gold coins)"
        dotnet_type: "long"
        max_value: 9223372036854775807
    - decimal_2:
        description: "2 decimal places (dollars.cents)"
        dotnet_type: "decimal"
    - decimal_4:
        description: "4 decimal places (forex-style)"
        dotnet_type: "decimal"
    - decimal_8:
        description: "8 decimal places (crypto-style)"
        dotnet_type: "decimal"
    - decimal_full:
        description: "Full decimal precision (28 significant digits)"
        dotnet_type: "decimal"
        max_value: 79228162514264337593543950335
    - big_integer:
        description: "Arbitrary large integers (idle games)"
        dotnet_type: "BigInteger"
        notes: "Stored as string, no decimal support"

  # ═══════════════════════════════════════════════════════════════
  # TRANSFER RULES
  # ═══════════════════════════════════════════════════════════════

  # Can this currency be transferred between wallets?
  transferable: boolean
  default: true

  # Can this currency be used in trades/auctions?
  tradeable: boolean
  default: true

  # ═══════════════════════════════════════════════════════════════
  # BALANCE RULES
  # ═══════════════════════════════════════════════════════════════

  # Can wallets have negative balance? (overrides plugin default)
  allowNegative: boolean
  nullable: true
  notes: "null = use plugin default"

  # Maximum amount a single wallet can hold
  # null = no cap (internally treated as type's max value)
  perWalletCap: decimal
  nullable: true

  # What happens when a credit would exceed perWalletCap?
  capOverflowBehavior: CapOverflowBehavior
  default: reject
  values:
    - reject:
        description: "Transaction fails with WALLET_CAP_EXCEEDED error"
    - cap_and_lose:
        description: "Balance set to cap, excess is lost (logged)"
    - cap_and_return:
        description: "Balance set to cap, excess returned to source (if applicable)"

  # Maximum total supply across all wallets
  # null = unlimited supply
  globalSupplyCap: decimal
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # EARN RATE CAPS
  # ═══════════════════════════════════════════════════════════════

  # Maximum amount that can be earned (credited as faucet) per day
  # null = unlimited
  dailyEarnCap: decimal
  nullable: true

  # Maximum amount that can be earned per week
  # null = unlimited
  weeklyEarnCap: decimal
  nullable: true

  # When daily/weekly caps reset (UTC time)
  earnCapResetTime: time
  default: "00:00:00"

  # ═══════════════════════════════════════════════════════════════
  # AUTOGAIN (ENERGY/INTEREST)
  # ═══════════════════════════════════════════════════════════════

  autogainEnabled: boolean
  default: false

  # How autogain is calculated
  autogainMode: AutogainMode
  default: simple
  values:
    - simple:
        description: "Flat amount added per interval"
        formula: "newBalance = oldBalance + (autogainAmount * periodsElapsed)"
    - compound:
        description: "Percentage of current balance per interval"
        formula: "newBalance = oldBalance * (1 + autogainAmount)^periodsElapsed"

  # For simple: flat amount per interval
  # For compound: rate per interval (0.01 = 1%)
  autogainAmount: decimal
  nullable: true

  # How often autogain is applied
  autogainInterval: duration
  nullable: true
  examples: ["PT5M", "PT1H", "P1D"]

  # Don't apply autogain when balance is at or above this
  # null = no cap on autogain
  autogainCap: decimal
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # EXPIRATION
  # ═══════════════════════════════════════════════════════════════

  expires: boolean
  default: false

  expirationPolicy: ExpirationPolicy
  nullable: true
  values:
    - fixed_date:
        description: "All currency of this type expires on a specific date"
    - duration_from_earn:
        description: "Each unit expires X time after being earned"
    - end_of_season:
        description: "Expires when a season ends"

  # For fixed_date policy
  expirationDate: timestamp
  nullable: true

  # For duration_from_earn policy
  expirationDuration: duration
  nullable: true

  # For end_of_season policy
  seasonId: uuid
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # ITEM LINKAGE (OPTIONAL)
  # ═══════════════════════════════════════════════════════════════

  # Is this currency represented by an item in lib-inventory?
  linkedToItem: boolean
  default: false

  # The item template that represents this currency
  linkedItemTemplateId: uuid
  nullable: true

  # How the linkage works
  linkageMode: ItemLinkageMode
  default: none
  values:
    - none:
        description: "No item linkage"
    - visual_only:
        description: "Item exists for display/flavor, currency balance is authoritative"
    - reference_only:
        description: "Currency tracks amount, game manages physical items separately"
  notes: |
    lib-currency does NOT support synced_with_inventory mode due to
    race condition complexity. Games that need inventory-slot-based
    currency should manage that logic at the game layer.

  # ═══════════════════════════════════════════════════════════════
  # BASE CURRENCY (FOR SIMPLE CONVERSION)
  # ═══════════════════════════════════════════════════════════════

  # Is this THE base currency for its scope?
  # Only one currency per realm (or one global) should be marked as base
  isBaseCurrency: boolean
  default: false

  # Exchange rate TO the base currency
  # Example: If base is "gold" and this is "silver" with rate 0.01,
  #          then 100 silver = 1 gold
  # null for base currency itself
  exchangeRateToBase: decimal
  nullable: true

  # When the exchange rate was last updated
  exchangeRateUpdatedAt: timestamp
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # DISPLAY
  # ═══════════════════════════════════════════════════════════════

  iconAssetId: uuid
  nullable: true

  # Format string for display
  # {amount} is replaced with the value
  displayFormat: string
  default: "{amount}"
  examples: ["{amount} gold", "${amount}", "{amount}g"]

  # ═══════════════════════════════════════════════════════════════
  # METADATA
  # ═══════════════════════════════════════════════════════════════

  createdAt: timestamp
  modifiedAt: timestamp
  isActive: boolean
  default: true
```

---

## 4. Wallets

A wallet holds currency balances for an owner.

```yaml
# Schema: Wallet
Wallet:
  id: uuid

  # ═══════════════════════════════════════════════════════════════
  # POLYMORPHIC OWNERSHIP
  # ═══════════════════════════════════════════════════════════════

  # Who owns this wallet
  ownerId: uuid

  # Type of owner (extensible)
  ownerType: WalletOwnerType
  values:
    - account:
        description: "Player account (cross-character)"
    - character:
        description: "Individual character"
    - npc:
        description: "Non-player character"
    - guild:
        description: "Guild/clan treasury"
    - faction:
        description: "Faction treasury"
    - location:
        description: "Location treasury (town, dungeon)"
    - system:
        description: "System wallet (faucet source, sink destination)"
  notes: "Games can extend with additional types"

  # ═══════════════════════════════════════════════════════════════
  # REALM BINDING
  # ═══════════════════════════════════════════════════════════════

  # For realm-scoped currencies
  # null for account-level wallets holding global currencies
  realmId: uuid
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # STATUS
  # ═══════════════════════════════════════════════════════════════

  status: WalletStatus
  default: active
  values:
    - active:
        description: "Normal operation"
    - frozen:
        description: "No transactions allowed"
    - closed:
        description: "Permanently closed"

  # If frozen, why
  frozenReason: string
  nullable: true

  frozenAt: timestamp
  nullable: true

  frozenBy: string
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # METADATA
  # ═══════════════════════════════════════════════════════════════

  createdAt: timestamp
  lastActivityAt: timestamp
```

---

## 5. Balances

A balance tracks one currency in one wallet.

```yaml
# Schema: CurrencyBalance
CurrencyBalance:
  # Composite key
  walletId: uuid
  currencyDefinitionId: uuid

  # ═══════════════════════════════════════════════════════════════
  # BALANCE
  # ═══════════════════════════════════════════════════════════════

  # Current available balance
  amount: decimal

  # Amount locked in escrow or pending transactions
  lockedAmount: decimal
  default: 0

  # Effective balance = amount - lockedAmount
  # (computed, not stored)

  # ═══════════════════════════════════════════════════════════════
  # AUTOGAIN TRACKING
  # ═══════════════════════════════════════════════════════════════

  # When autogain was last calculated/applied
  # Used for both lazy and task-based processing
  lastAutogainAt: timestamp
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # EARN CAP TRACKING
  # ═══════════════════════════════════════════════════════════════

  # How much has been earned today (faucet transactions)
  dailyEarned: decimal
  default: 0

  # How much has been earned this week
  weeklyEarned: decimal
  default: 0

  # When daily counter resets
  dailyResetAt: timestamp

  # When weekly counter resets
  weeklyResetAt: timestamp

  # ═══════════════════════════════════════════════════════════════
  # METADATA
  # ═══════════════════════════════════════════════════════════════

  createdAt: timestamp
  lastModifiedAt: timestamp
```

---

## 6. Transactions

All currency mutations are recorded as immutable transactions.

```yaml
# Schema: CurrencyTransaction
CurrencyTransaction:
  id: uuid

  # ═══════════════════════════════════════════════════════════════
  # PARTIES
  # ═══════════════════════════════════════════════════════════════

  # Source wallet (null for faucets - currency creation)
  sourceWalletId: uuid
  nullable: true

  # Target wallet (null for sinks - currency destruction)
  targetWalletId: uuid
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # CURRENCY
  # ═══════════════════════════════════════════════════════════════

  currencyDefinitionId: uuid

  # Always positive
  amount: decimal

  # ═══════════════════════════════════════════════════════════════
  # CLASSIFICATION
  # ═══════════════════════════════════════════════════════════════

  transactionType: TransactionType
  values:
    # ─────────────────────────────────────────────────────────────
    # FAUCETS (currency enters system)
    # ─────────────────────────────────────────────────────────────
    - mint:
        description: "Currency created by admin"
        source: null
        target: wallet
    - quest_reward:
        description: "Reward from quest completion"
        source: null
        target: wallet
    - loot_drop:
        description: "Currency dropped by creature/container"
        source: null
        target: wallet
    - vendor_sale:
        description: "Currency received from selling to NPC"
        source: null
        target: wallet
    - autogain:
        description: "Energy regeneration, interest, passive income"
        source: null
        target: wallet
    - refund:
        description: "Refund from cancelled transaction"
        source: null
        target: wallet
    - conversion_credit:
        description: "Currency received from conversion"
        source: null
        target: wallet

    # ─────────────────────────────────────────────────────────────
    # SINKS (currency exits system)
    # ─────────────────────────────────────────────────────────────
    - burn:
        description: "Currency destroyed by admin"
        source: wallet
        target: null
    - vendor_purchase:
        description: "Currency spent buying from NPC"
        source: wallet
        target: null
    - fee:
        description: "Service fee (auction listing, fast travel, etc.)"
        source: wallet
        target: null
    - expiration:
        description: "Currency expired due to time limit"
        source: wallet
        target: null
    - cap_overflow:
        description: "Currency lost due to wallet cap"
        source: wallet
        target: null
    - conversion_debit:
        description: "Currency spent in conversion"
        source: wallet
        target: null

    # ─────────────────────────────────────────────────────────────
    # TRANSFERS (currency moves between wallets)
    # ─────────────────────────────────────────────────────────────
    - transfer:
        description: "Generic transfer"
        source: wallet
        target: wallet
    - trade:
        description: "Player-to-player trade"
        source: wallet
        target: wallet
    - gift:
        description: "One-way gift"
        source: wallet
        target: wallet
    - escrow_deposit:
        description: "Deposited into escrow"
        source: wallet
        target: escrow
    - escrow_release:
        description: "Released from escrow to recipient"
        source: escrow
        target: wallet
    - escrow_refund:
        description: "Refunded from escrow to depositor"
        source: escrow
        target: wallet

  # ═══════════════════════════════════════════════════════════════
  # REFERENCE
  # ═══════════════════════════════════════════════════════════════

  # What triggered this transaction
  referenceType: string
  nullable: true
  examples: ["quest", "auction", "escrow", "trade", "admin"]

  referenceId: uuid
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # ESCROW LINK
  # ═══════════════════════════════════════════════════════════════

  # For escrow-related transactions
  escrowId: uuid
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # IDEMPOTENCY
  # ═══════════════════════════════════════════════════════════════

  idempotencyKey: string

  # ═══════════════════════════════════════════════════════════════
  # AUDIT TRAIL
  # ═══════════════════════════════════════════════════════════════

  timestamp: timestamp

  # Balance snapshots for audit
  sourceBalanceBefore: decimal
  nullable: true

  sourceBalanceAfter: decimal
  nullable: true

  targetBalanceBefore: decimal
  nullable: true

  targetBalanceAfter: decimal
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # ADDITIONAL CONTEXT
  # ═══════════════════════════════════════════════════════════════

  # For autogain transactions
  autogainPeriodsApplied: integer
  nullable: true

  # For cap overflow
  overflowAmountLost: decimal
  nullable: true

  # For earn cap limited
  earnCapAmountLimited: decimal
  nullable: true

  # Free-form metadata
  metadata: object
  nullable: true
```

---

## 7. Autogain Processing

### 8.1 Calculation Logic

**Simple Mode (flat amount)**:
```
periodsElapsed = floor((now - lastAutogainAt) / autogainInterval)

if periodsElapsed > 0:
    potentialGain = periodsElapsed * autogainAmount

    if autogainCap is not null and amount >= autogainCap:
        gain = 0  # Already at cap, no gain
    else if autogainCap is not null:
        gain = min(potentialGain, autogainCap - amount)
    else:
        gain = potentialGain

    newAmount = amount + gain
    lastAutogainAt = lastAutogainAt + (periodsElapsed * autogainInterval)
```

**Compound Mode (percentage)**:
```
periodsElapsed = floor((now - lastAutogainAt) / autogainInterval)

if periodsElapsed > 0:
    # Compound interest formula
    multiplier = (1 + autogainAmount) ^ periodsElapsed
    newAmount = amount * multiplier

    if autogainCap is not null:
        newAmount = min(newAmount, autogainCap)

    gain = newAmount - amount
    lastAutogainAt = lastAutogainAt + (periodsElapsed * autogainInterval)
```

### 8.2 Processing Modes

**Lazy Mode** (default):
- Autogain calculated on-demand when balance is queried
- `lastAutogainAt` updated on each query
- Events emitted at query time
- Pro: No background infrastructure needed
- Con: Events delayed until query

**Task Mode**:
- Background task runs at `autogainTaskInterval`
- Processes balances where `now - lastAutogainAt >= autogainInterval`
- Batched processing (`autogainBatchSize` per cycle)
- Events emitted close to actual interval time
- Pro: Timely events
- Con: Requires background task

### 8.3 Event Timing Comparison

```
Currency: energy
Interval: 5 minutes
Amount: +10 (simple)

Timeline:
00:00 - Balance queried, autogain calculated, lastAutogainAt = 00:00
00:05 - Interval passes
00:10 - Interval passes
00:12 - Balance queried

Lazy Mode:
  - At 00:12: Calculates 2 periods elapsed
  - Emits: currency.autogain.calculated at 00:12
  - Gain: +20, lastAutogainAt = 00:10

Task Mode (1 min task interval):
  - At 00:05: Task runs, calculates 1 period
  - Emits: currency.autogain.calculated at 00:05
  - At 00:10: Task runs, calculates 1 period
  - Emits: currency.autogain.calculated at 00:10
  - At 00:12: Balance queried, no additional calculation needed

Both modes result in same final balance, different event timing.
```

---

## 8. Currency Conversion

Simple conversion through a base currency.

### 9.1 Base Currency Designation

Each realm (or globally) can have ONE base currency:
- `isBaseCurrency: true` on the currency definition
- All other currencies have `exchangeRateToBase` defining their value relative to base

### 9.2 Conversion Formula

```
Converting A to B:

1. Get exchangeRateToBase for A (rateA)
2. Get exchangeRateToBase for B (rateB)
3. effectiveRate = rateA / rateB
4. toAmount = fromAmount * effectiveRate

Example:
  Base: Gold (rate = 1.0)
  Silver: rate = 0.01 (100 silver = 1 gold)
  Gems: rate = 10.0 (1 gem = 10 gold)

  Converting 500 silver to gems:
  rateA = 0.01
  rateB = 10.0
  effectiveRate = 0.01 / 10.0 = 0.001
  toAmount = 500 * 0.001 = 0.5 gems
```

### 9.3 Simple vs Complex Conversion

| Feature | lib-currency (simple) | lib-economy (complex) |
|---------|----------------------|----------------------|
| Rate source | `exchangeRateToBase` | Location-specific, dynamic |
| Modifiers | None | War, festivals, tariffs |
| Spread | None | Buy/sell spreads |
| History | None | Rate history, trends |

---

## 9. API Endpoints

### 9.1 Currency Definition APIs

```yaml
# ══════════════════════════════════════════════════════════════════
# DEFINITION MANAGEMENT
# ══════════════════════════════════════════════════════════════════

/currency/definition/create:
  method: POST
  access: admin
  description: Create a new currency definition
  request:
    code: string                  # Required, immutable
    name: string                  # Required
    description: string
    scope: CurrencyScope          # Required
    realmsAvailable: [uuid]
    precision: CurrencyPrecision  # Required, immutable
    transferable: boolean
    tradeable: boolean
    allowNegative: boolean
    perWalletCap: decimal
    capOverflowBehavior: CapOverflowBehavior
    globalSupplyCap: decimal
    dailyEarnCap: decimal
    weeklyEarnCap: decimal
    earnCapResetTime: time
    autogainEnabled: boolean
    autogainMode: AutogainMode
    autogainAmount: decimal
    autogainInterval: duration
    autogainCap: decimal
    expires: boolean
    expirationPolicy: ExpirationPolicy
    expirationDate: timestamp
    expirationDuration: duration
    seasonId: uuid
    linkedToItem: boolean
    linkedItemTemplateId: uuid
    linkageMode: ItemLinkageMode
    isBaseCurrency: boolean
    exchangeRateToBase: decimal
    iconAssetId: uuid
    displayFormat: string
  response:
    definition: CurrencyDefinition
  errors:
    - CODE_ALREADY_EXISTS
    - INVALID_REALM
    - BASE_CURRENCY_ALREADY_EXISTS

/currency/definition/get:
  method: POST
  access: user
  description: Get currency definition by ID or code
  request:
    definitionId: uuid            # One of these required
    code: string
  response:
    definition: CurrencyDefinition
  errors:
    - CURRENCY_NOT_FOUND

/currency/definition/list:
  method: POST
  access: user
  description: List currency definitions with filters
  request:
    realmId: uuid                 # Filter by realm availability
    scope: CurrencyScope
    includeInactive: boolean
    isBaseCurrency: boolean
  response:
    definitions: [CurrencyDefinition]

/currency/definition/update:
  method: POST
  access: admin
  description: Update currency definition
  request:
    definitionId: uuid            # Required
    # Mutable fields only:
    name: string
    description: string
    transferable: boolean
    tradeable: boolean
    allowNegative: boolean
    perWalletCap: decimal
    capOverflowBehavior: CapOverflowBehavior
    dailyEarnCap: decimal
    weeklyEarnCap: decimal
    autogainEnabled: boolean
    autogainMode: AutogainMode
    autogainAmount: decimal
    autogainInterval: duration
    autogainCap: decimal
    exchangeRateToBase: decimal
    iconAssetId: uuid
    displayFormat: string
    isActive: boolean
  response:
    definition: CurrencyDefinition
  errors:
    - CURRENCY_NOT_FOUND
    - CANNOT_CHANGE_IMMUTABLE_FIELD
  notes: |
    code, precision, and scope are immutable after creation.
```

### 9.2 Wallet APIs

```yaml
# ══════════════════════════════════════════════════════════════════
# WALLET MANAGEMENT
# ══════════════════════════════════════════════════════════════════

/currency/wallet/create:
  method: POST
  access: authenticated
  description: Create a new wallet
  request:
    ownerId: uuid                 # Required
    ownerType: WalletOwnerType    # Required
    realmId: uuid                 # Required for realm-scoped
  response:
    wallet: Wallet
  errors:
    - WALLET_ALREADY_EXISTS

/currency/wallet/get:
  method: POST
  access: user
  description: Get wallet by ID or owner
  request:
    walletId: uuid                # One of these required
    ownerId: uuid
    ownerType: WalletOwnerType
    realmId: uuid                 # Required if using ownerId
  response:
    wallet: Wallet
    balances: [CurrencyBalance]   # All non-zero balances
  errors:
    - WALLET_NOT_FOUND

/currency/wallet/get-or-create:
  method: POST
  access: authenticated
  description: Get existing wallet or create if not exists
  request:
    ownerId: uuid
    ownerType: WalletOwnerType
    realmId: uuid
  response:
    wallet: Wallet
    balances: [CurrencyBalance]
    created: boolean              # Was a new wallet created?

/currency/wallet/freeze:
  method: POST
  access: admin
  description: Freeze a wallet (prevent transactions)
  request:
    walletId: uuid
    reason: string
  response:
    wallet: Wallet
  errors:
    - WALLET_NOT_FOUND
    - WALLET_ALREADY_FROZEN

/currency/wallet/unfreeze:
  method: POST
  access: admin
  description: Unfreeze a wallet
  request:
    walletId: uuid
  response:
    wallet: Wallet
  errors:
    - WALLET_NOT_FOUND
    - WALLET_NOT_FROZEN

/currency/wallet/close:
  method: POST
  access: admin
  description: Permanently close a wallet
  request:
    walletId: uuid
    transferRemainingTo: uuid     # Wallet to receive remaining balances
  response:
    wallet: Wallet
    transferredBalances: [{ currencyId, amount }]
  errors:
    - WALLET_NOT_FOUND
    - WALLET_ALREADY_CLOSED
    - DESTINATION_WALLET_NOT_FOUND
```

### 9.3 Balance APIs

```yaml
# ══════════════════════════════════════════════════════════════════
# BALANCE OPERATIONS
# ══════════════════════════════════════════════════════════════════

/currency/balance/get:
  method: POST
  access: user
  description: Get balance for a specific currency
  request:
    walletId: uuid
    currencyDefinitionId: uuid
  response:
    balance:
      amount: decimal
      lockedAmount: decimal
      effectiveAmount: decimal    # amount - lockedAmount
    earnCapInfo:                  # null if no caps
      dailyEarned: decimal
      dailyRemaining: decimal
      dailyResetsAt: timestamp
      weeklyEarned: decimal
      weeklyRemaining: decimal
      weeklyResetsAt: timestamp
    autogainInfo:                 # null if no autogain
      lastCalculatedAt: timestamp
      nextGainAt: timestamp
      nextGainAmount: decimal
      mode: AutogainMode
  notes: |
    In lazy autogain mode, this may trigger autogain calculation
    and emit autogain events.

/currency/balance/batch-get:
  method: POST
  access: user
  description: Get multiple balances in one call
  request:
    queries: [
      { walletId: uuid, currencyDefinitionId: uuid }
    ]
  response:
    balances: [
      {
        walletId: uuid
        currencyDefinitionId: uuid
        amount: decimal
        lockedAmount: decimal
        effectiveAmount: decimal
      }
    ]

/currency/credit:
  method: POST
  access: authenticated
  description: Credit currency to a wallet (faucet)
  request:
    walletId: uuid
    currencyDefinitionId: uuid
    amount: decimal               # Must be positive
    transactionType: TransactionType  # Must be faucet type
    referenceType: string
    referenceId: uuid
    idempotencyKey: string        # Required
    bypassEarnCap: boolean        # Admin only
    metadata: object
  response:
    transaction: CurrencyTransaction
    newBalance: decimal
    earnCapApplied: boolean
    earnCapAmountLimited: decimal # How much was limited by cap
    walletCapApplied: boolean
    walletCapAmountLost: decimal  # How much was lost due to cap
  errors:
    - WALLET_NOT_FOUND
    - WALLET_FROZEN
    - CURRENCY_NOT_FOUND
    - INVALID_TRANSACTION_TYPE    # Not a faucet type
    - EARN_CAP_EXCEEDED           # Only if bypassEarnCap=false
    - GLOBAL_SUPPLY_CAP_EXCEEDED
    - IDEMPOTENCY_KEY_REUSED

/currency/debit:
  method: POST
  access: authenticated
  description: Debit currency from a wallet (sink)
  request:
    walletId: uuid
    currencyDefinitionId: uuid
    amount: decimal               # Must be positive
    transactionType: TransactionType  # Must be sink type
    referenceType: string
    referenceId: uuid
    idempotencyKey: string
    allowNegative: boolean        # Override for this transaction
    metadata: object
  response:
    transaction: CurrencyTransaction
    newBalance: decimal
  errors:
    - WALLET_NOT_FOUND
    - WALLET_FROZEN
    - CURRENCY_NOT_FOUND
    - INSUFFICIENT_FUNDS
    - NEGATIVE_NOT_ALLOWED
    - INVALID_TRANSACTION_TYPE

/currency/transfer:
  method: POST
  access: authenticated
  description: Transfer currency between wallets
  request:
    sourceWalletId: uuid
    targetWalletId: uuid
    currencyDefinitionId: uuid
    amount: decimal
    transactionType: TransactionType  # Must be transfer type
    referenceType: string
    referenceId: uuid
    idempotencyKey: string
    metadata: object
  response:
    transaction: CurrencyTransaction
    sourceNewBalance: decimal
    targetNewBalance: decimal
    targetCapApplied: boolean
    targetCapAmountLost: decimal
  errors:
    - SOURCE_WALLET_NOT_FOUND
    - TARGET_WALLET_NOT_FOUND
    - SOURCE_WALLET_FROZEN
    - TARGET_WALLET_FROZEN
    - CURRENCY_NOT_FOUND
    - CURRENCY_NOT_TRANSFERABLE
    - INSUFFICIENT_FUNDS
    - CROSS_REALM_NOT_ALLOWED
    - INVALID_TRANSACTION_TYPE

/currency/batch-credit:
  method: POST
  access: authenticated
  description: Credit multiple wallets in one call
  request:
    operations: [
      {
        walletId: uuid
        currencyDefinitionId: uuid
        amount: decimal
        transactionType: TransactionType
        referenceType: string
        referenceId: uuid
      }
    ]
    idempotencyKey: string        # Covers entire batch
  response:
    results: [
      {
        index: integer
        success: boolean
        transaction: CurrencyTransaction
        error: string
      }
    ]
  notes: |
    Each operation is independent. Failures don't rollback others.
    For atomic multi-wallet operations, use lib-escrow.
```

### 9.4 Conversion APIs

```yaml
# ══════════════════════════════════════════════════════════════════
# CURRENCY CONVERSION
# ══════════════════════════════════════════════════════════════════

/currency/convert/calculate:
  method: POST
  access: user
  description: Calculate conversion without executing
  request:
    fromCurrencyId: uuid
    toCurrencyId: uuid
    fromAmount: decimal
  response:
    toAmount: decimal
    effectiveRate: decimal
    conversionPath: [
      { from: string, to: string, rate: decimal }
    ]
    baseCurrency: string
  errors:
    - CURRENCY_NOT_FOUND
    - NO_BASE_CURRENCY
    - MISSING_EXCHANGE_RATE

/currency/convert/execute:
  method: POST
  access: authenticated
  description: Execute currency conversion
  request:
    walletId: uuid
    fromCurrencyId: uuid
    toCurrencyId: uuid
    fromAmount: decimal
    idempotencyKey: string
  response:
    debitTransaction: CurrencyTransaction
    creditTransaction: CurrencyTransaction
    fromDebited: decimal
    toCredited: decimal
    effectiveRate: decimal
  errors:
    - WALLET_NOT_FOUND
    - WALLET_FROZEN
    - CURRENCY_NOT_FOUND
    - INSUFFICIENT_FUNDS
    - NO_BASE_CURRENCY
    - MISSING_EXCHANGE_RATE

/currency/exchange-rate/get:
  method: POST
  access: user
  description: Get exchange rate between currencies
  request:
    fromCurrencyId: uuid
    toCurrencyId: uuid
  response:
    rate: decimal
    inverseRate: decimal
    baseCurrency: string
    fromCurrencyRateToBase: decimal
    toCurrencyRateToBase: decimal
  errors:
    - CURRENCY_NOT_FOUND
    - NO_BASE_CURRENCY
    - MISSING_EXCHANGE_RATE

/currency/exchange-rate/update:
  method: POST
  access: admin
  description: Update a currency's exchange rate to base
  request:
    currencyDefinitionId: uuid
    exchangeRateToBase: decimal
  response:
    definition: CurrencyDefinition
    previousRate: decimal
  emits: currency.exchange_rate.updated
  errors:
    - CURRENCY_NOT_FOUND
    - CANNOT_SET_RATE_ON_BASE_CURRENCY
```

### 9.5 Transaction History APIs

```yaml
# ══════════════════════════════════════════════════════════════════
# TRANSACTION HISTORY
# ══════════════════════════════════════════════════════════════════

/currency/transaction/get:
  method: POST
  access: authenticated
  description: Get transaction by ID
  request:
    transactionId: uuid
  response:
    transaction: CurrencyTransaction
  errors:
    - TRANSACTION_NOT_FOUND

/currency/transaction/history:
  method: POST
  access: user
  description: Get transaction history for a wallet
  request:
    walletId: uuid
    currencyDefinitionId: uuid    # Optional filter
    transactionTypes: [TransactionType]  # Optional filter
    fromDate: timestamp
    toDate: timestamp
    limit: integer
    offset: integer
  response:
    transactions: [CurrencyTransaction]
    totalCount: integer

/currency/transaction/by-reference:
  method: POST
  access: authenticated
  description: Get transactions by reference
  request:
    referenceType: string
    referenceId: uuid
  response:
    transactions: [CurrencyTransaction]
```

### 9.6 Analytics APIs

```yaml
# ══════════════════════════════════════════════════════════════════
# ANALYTICS / AGGREGATE QUERIES
# ══════════════════════════════════════════════════════════════════

/currency/stats/global-supply:
  method: POST
  access: user
  description: Get global supply stats for a currency
  request:
    currencyDefinitionId: uuid
  response:
    totalSupply: decimal          # Sum of all positive balances
    inCirculation: decimal        # Total in wallets
    inEscrow: decimal             # Locked in escrows
    totalMinted: decimal          # All-time faucets
    totalBurned: decimal          # All-time sinks
    supplyCap: decimal            # From definition, null if none
    supplyCapRemaining: decimal

/currency/stats/wallet-distribution:
  method: POST
  access: admin
  description: Get wealth distribution stats
  request:
    currencyDefinitionId: uuid
    realmId: uuid
  response:
    totalWallets: integer
    walletsWithBalance: integer
    averageBalance: decimal
    medianBalance: decimal
    percentiles:
      p10: decimal
      p25: decimal
      p50: decimal
      p75: decimal
      p90: decimal
      p99: decimal
    giniCoefficient: decimal      # 0 = equal, 1 = one has all
```

---

## 10. Events

All events are published to lib-messaging.

```yaml
# ══════════════════════════════════════════════════════════════════
# BALANCE EVENTS
# ══════════════════════════════════════════════════════════════════

currency.credited:
  topic: "currency.credited"
  payload:
    transactionId: uuid
    walletId: uuid
    ownerId: uuid
    ownerType: string
    currencyDefinitionId: uuid
    currencyCode: string
    amount: decimal
    transactionType: string
    newBalance: decimal
    referenceType: string
    referenceId: uuid
    earnCapApplied: boolean
    walletCapApplied: boolean
    timestamp: timestamp

currency.debited:
  topic: "currency.debited"
  payload:
    transactionId: uuid
    walletId: uuid
    ownerId: uuid
    ownerType: string
    currencyDefinitionId: uuid
    currencyCode: string
    amount: decimal
    transactionType: string
    newBalance: decimal
    referenceType: string
    referenceId: uuid
    timestamp: timestamp

currency.transferred:
  topic: "currency.transferred"
  payload:
    transactionId: uuid
    sourceWalletId: uuid
    sourceOwnerId: uuid
    sourceOwnerType: string
    targetWalletId: uuid
    targetOwnerId: uuid
    targetOwnerType: string
    currencyDefinitionId: uuid
    currencyCode: string
    amount: decimal
    transactionType: string
    timestamp: timestamp

# ══════════════════════════════════════════════════════════════════
# AUTOGAIN EVENTS
# ══════════════════════════════════════════════════════════════════

currency.autogain.calculated:
  topic: "currency.autogain.calculated"
  payload:
    walletId: uuid
    ownerId: uuid
    ownerType: string
    currencyDefinitionId: uuid
    currencyCode: string
    previousBalance: decimal
    periodsApplied: integer
    amountGained: decimal
    newBalance: decimal
    autogainMode: string          # simple | compound
    calculatedAt: timestamp
    periodsFrom: timestamp        # Start of calculation period
    periodsTo: timestamp          # End of calculation period
  notes: |
    In lazy mode: emitted when balance is queried
    In task mode: emitted when background task processes

# ══════════════════════════════════════════════════════════════════
# CAP EVENTS
# ══════════════════════════════════════════════════════════════════

currency.earn_cap.reached:
  topic: "currency.earn_cap.reached"
  payload:
    walletId: uuid
    ownerId: uuid
    currencyDefinitionId: uuid
    currencyCode: string
    capType: string               # daily | weekly
    capAmount: decimal
    attemptedAmount: decimal
    limitedAmount: decimal
    timestamp: timestamp

currency.wallet_cap.reached:
  topic: "currency.wallet_cap.reached"
  payload:
    walletId: uuid
    ownerId: uuid
    currencyDefinitionId: uuid
    currencyCode: string
    capAmount: decimal
    overflowBehavior: string
    amountLost: decimal           # If cap_and_lose
    timestamp: timestamp

# ══════════════════════════════════════════════════════════════════
# EXPIRATION EVENTS
# ══════════════════════════════════════════════════════════════════

currency.expired:
  topic: "currency.expired"
  payload:
    transactionId: uuid
    walletId: uuid
    ownerId: uuid
    currencyDefinitionId: uuid
    currencyCode: string
    amountExpired: decimal
    expirationPolicy: string
    timestamp: timestamp

# ══════════════════════════════════════════════════════════════════
# EXCHANGE RATE EVENTS
# ══════════════════════════════════════════════════════════════════

currency.exchange_rate.updated:
  topic: "currency.exchange_rate.updated"
  payload:
    currencyDefinitionId: uuid
    currencyCode: string
    previousRate: decimal
    newRate: decimal
    baseCurrencyCode: string
    updatedAt: timestamp

# ══════════════════════════════════════════════════════════════════
# DEFINITION EVENTS
# ══════════════════════════════════════════════════════════════════

currency.definition.created:
  topic: "currency.definition.created"
  payload:
    definitionId: uuid
    code: string
    name: string
    scope: string

currency.definition.updated:
  topic: "currency.definition.updated"
  payload:
    definitionId: uuid
    code: string
    changedFields: [string]

# ══════════════════════════════════════════════════════════════════
# WALLET EVENTS
# ══════════════════════════════════════════════════════════════════

currency.wallet.created:
  topic: "currency.wallet.created"
  payload:
    walletId: uuid
    ownerId: uuid
    ownerType: string
    realmId: uuid

currency.wallet.frozen:
  topic: "currency.wallet.frozen"
  payload:
    walletId: uuid
    ownerId: uuid
    reason: string

currency.wallet.unfrozen:
  topic: "currency.wallet.unfrozen"
  payload:
    walletId: uuid
    ownerId: uuid

currency.wallet.closed:
  topic: "currency.wallet.closed"
  payload:
    walletId: uuid
    ownerId: uuid
    balancesTransferredTo: uuid
```

---

## 11. State Stores

```yaml
# ══════════════════════════════════════════════════════════════════
# REDIS (Hot Data)
# ══════════════════════════════════════════════════════════════════

currency-balance-cache:
  backend: redis
  prefix: "currency:balance"
  key_pattern: "{walletId}:{currencyId}"
  purpose: Real-time balance lookups
  ttl: 300  # 5 minutes, refreshed on access

currency-idempotency:
  backend: redis
  prefix: "currency:idemp"
  key_pattern: "{idempotencyKey}"
  purpose: Idempotency key deduplication
  ttl: 3600  # From config

currency-locks:
  backend: redis
  prefix: "currency:lock"
  key_pattern: "{walletId}:{currencyId}:{lockId}"
  purpose: Track currency locked for escrow
  ttl: 2592000  # 30 days (max escrow lifetime)

# ══════════════════════════════════════════════════════════════════
# MYSQL (Persistent)
# ══════════════════════════════════════════════════════════════════

currency-definitions:
  backend: mysql
  table: currency_definitions
  indexes:
    - code (unique)
    - scope
    - isBaseCurrency

currency-wallets:
  backend: mysql
  table: currency_wallets
  indexes:
    - ownerId, ownerType, realmId (unique)
    - status

currency-balances:
  backend: mysql
  table: currency_balances
  indexes:
    - walletId, currencyDefinitionId (primary)
    - currencyDefinitionId, amount (for distribution queries)
    - lastAutogainAt (for task processing)

currency-transactions:
  backend: mysql
  table: currency_transactions
  partition_by: timestamp (monthly)
  indexes:
    - walletId, timestamp
    - currencyDefinitionId, timestamp
    - referenceType, referenceId
    - idempotencyKey (unique)
    - escrowLockId

currency-locks:
  backend: mysql
  table: currency_locks
  indexes:
    - walletId, currencyDefinitionId
    - escrowId
    - lockId (unique)
```

---

## 12. Error Codes

```yaml
# Standard error response format
ErrorResponse:
  code: string                    # Machine-readable code
  message: string                 # Human-readable message
  details: object                 # Additional context

# ══════════════════════════════════════════════════════════════════
# CURRENCY DEFINITION ERRORS
# ══════════════════════════════════════════════════════════════════

CURRENCY_NOT_FOUND:
  message: "Currency definition not found"
  details: { currencyId, code }

CODE_ALREADY_EXISTS:
  message: "Currency code already exists"
  details: { code }

BASE_CURRENCY_ALREADY_EXISTS:
  message: "A base currency already exists for this scope"
  details: { existingBaseId, scope }

CANNOT_CHANGE_IMMUTABLE_FIELD:
  message: "Cannot change immutable field after creation"
  details: { field }

# ══════════════════════════════════════════════════════════════════
# WALLET ERRORS
# ══════════════════════════════════════════════════════════════════

WALLET_NOT_FOUND:
  message: "Wallet not found"
  details: { walletId, ownerId, ownerType }

WALLET_ALREADY_EXISTS:
  message: "Wallet already exists for this owner"
  details: { ownerId, ownerType, realmId }

WALLET_FROZEN:
  message: "Wallet is frozen and cannot process transactions"
  details: { walletId, frozenReason }

WALLET_ALREADY_FROZEN:
  message: "Wallet is already frozen"

WALLET_NOT_FROZEN:
  message: "Wallet is not frozen"

WALLET_ALREADY_CLOSED:
  message: "Wallet is already closed"

# ══════════════════════════════════════════════════════════════════
# BALANCE ERRORS
# ══════════════════════════════════════════════════════════════════

INSUFFICIENT_FUNDS:
  message: "Insufficient funds for this transaction"
  details: { available, required, locked }

NEGATIVE_NOT_ALLOWED:
  message: "Currency does not allow negative balances"
  details: { currencyId, wouldResultIn }

WALLET_CAP_EXCEEDED:
  message: "Transaction would exceed wallet cap"
  details: { currentBalance, transactionAmount, cap }

EARN_CAP_EXCEEDED:
  message: "Earn rate cap exceeded"
  details: { capType, earned, cap, attemptedAmount }

GLOBAL_SUPPLY_CAP_EXCEEDED:
  message: "Transaction would exceed global supply cap"
  details: { currentSupply, cap, attemptedAmount }

# ══════════════════════════════════════════════════════════════════
# TRANSFER ERRORS
# ══════════════════════════════════════════════════════════════════

CURRENCY_NOT_TRANSFERABLE:
  message: "This currency cannot be transferred"
  details: { currencyId }

CROSS_REALM_NOT_ALLOWED:
  message: "Cannot transfer this currency across realms"
  details: { currencyId, sourceRealm, targetRealm }

# ══════════════════════════════════════════════════════════════════
# TRANSACTION ERRORS
# ══════════════════════════════════════════════════════════════════

INVALID_TRANSACTION_TYPE:
  message: "Invalid transaction type for this operation"
  details: { provided, allowedTypes }

IDEMPOTENCY_KEY_REUSED:
  message: "Idempotency key has already been used"
  details: { idempotencyKey, originalTransactionId }

TRANSACTION_NOT_FOUND:
  message: "Transaction not found"
  details: { transactionId }

# ══════════════════════════════════════════════════════════════════
# LOCK ERRORS (for lib-escrow integration)
# ══════════════════════════════════════════════════════════════════

LOCK_NOT_FOUND:
  message: "Currency lock not found"
  details: { lockId }

LOCK_ALREADY_RELEASED:
  message: "Currency lock has already been released"
  details: { lockId, releasedAt }

LOCK_AMOUNT_MISMATCH:
  message: "Lock amount does not match requested transfer"
  details: { lockId, lockedAmount, requestedAmount }

# ══════════════════════════════════════════════════════════════════
# CONVERSION ERRORS
# ══════════════════════════════════════════════════════════════════

NO_BASE_CURRENCY:
  message: "No base currency defined for conversion"
  details: { scope }

MISSING_EXCHANGE_RATE:
  message: "Currency missing exchange rate to base"
  details: { currencyId }

CANNOT_SET_RATE_ON_BASE_CURRENCY:
  message: "Cannot set exchange rate on base currency"
  details: { currencyId }
```

---

## Appendix A: Example Configurations

### A.1 Classic MMO (Gold + Premium Gems)

```yaml
# Gold - transferable, tradeable, no caps
gold:
  code: "gold"
  scope: realm_specific
  precision: integer
  transferable: true
  tradeable: true
  isBaseCurrency: true

# Premium Gems - account-level, not tradeable
premium_gems:
  code: "premium_gems"
  scope: global
  precision: integer
  transferable: false
  tradeable: false
  exchangeRateToBase: 100  # 1 gem = 100 gold equivalent
```

### A.2 Mobile F2P (Energy + Multiple Currencies)

```yaml
# Energy - regenerating
energy:
  code: "energy"
  scope: global
  precision: integer
  transferable: false
  tradeable: false
  perWalletCap: 100
  capOverflowBehavior: cap_and_lose
  autogainEnabled: true
  autogainMode: simple
  autogainAmount: 1
  autogainInterval: "PT5M"
  autogainCap: 100

# Soft currency - daily capped
coins:
  code: "coins"
  scope: global
  precision: integer
  transferable: true
  tradeable: false
  dailyEarnCap: 10000
  isBaseCurrency: true

# Hard currency
gems:
  code: "gems"
  scope: global
  precision: integer
  transferable: false
  tradeable: false
  exchangeRateToBase: 100
```

### A.3 Interest-Bearing Currency (Idle Game)

```yaml
gold:
  code: "gold"
  scope: global
  precision: big_integer  # Support huge numbers
  autogainEnabled: true
  autogainMode: compound
  autogainAmount: 0.001   # 0.1% per interval
  autogainInterval: "PT1M"
  isBaseCurrency: true
```

### A.4 Seasonal Event Currency

```yaml
event_tokens:
  code: "winter_fest_2026"
  scope: global
  precision: integer
  transferable: false
  tradeable: false
  expires: true
  expirationPolicy: fixed_date
  expirationDate: "2026-01-15T00:00:00Z"
```

---

## 13. lib-escrow Integration

lib-currency implements the asset handler interface for lib-escrow, enabling currency
to be used in multi-party escrow agreements (trades, auctions, contracts).

### 13.1 Handler Registration

lib-currency registers as an asset handler on startup:

```yaml
assetType: "currency"
pluginId: "lib-currency"
lockEndpoint: "/currency/escrow/lock"
unlockEndpoint: "/currency/escrow/unlock"
transferEndpoint: "/currency/escrow/transfer"
```

### 13.2 Escrow Lock APIs

These internal APIs are called by lib-escrow, not directly by games.

```yaml
# ══════════════════════════════════════════════════════════════════
# ESCROW INTEGRATION (Internal - called by lib-escrow)
# ══════════════════════════════════════════════════════════════════

/currency/escrow/lock:
  method: POST
  access: authenticated
  description: Lock currency for escrow (called by lib-escrow on deposit)
  request:
    walletId: uuid
    currencyDefinitionId: uuid
    amount: decimal
    escrowId: uuid
    idempotencyKey: string
  response:
    lockId: uuid
    success: boolean
    lockedAmount: decimal
    newLockedTotal: decimal       # Total locked in this wallet
    newAvailableBalance: decimal  # Balance - locked
  errors:
    - WALLET_NOT_FOUND
    - WALLET_FROZEN
    - CURRENCY_NOT_FOUND
    - INSUFFICIENT_FUNDS
  notes: |
    Creates a lock record and increases lockedAmount on the balance.
    The currency remains in the wallet but cannot be spent.

/currency/escrow/unlock:
  method: POST
  access: authenticated
  description: Unlock currency from escrow (called on refund/cancel)
  request:
    lockId: uuid
    escrowId: uuid
    idempotencyKey: string
  response:
    success: boolean
    unlockedAmount: decimal
    newLockedTotal: decimal
    newAvailableBalance: decimal
  errors:
    - LOCK_NOT_FOUND
    - LOCK_ALREADY_RELEASED
  notes: |
    Removes the lock and decreases lockedAmount on the balance.
    Currency becomes available again without any transfer.

/currency/escrow/transfer:
  method: POST
  access: authenticated
  description: Transfer locked currency to recipient (called on release)
  request:
    lockId: uuid
    targetWalletId: uuid
    escrowId: uuid
    idempotencyKey: string
  response:
    success: boolean
    transactionId: uuid
    transferredAmount: decimal
    sourceNewBalance: decimal
    targetNewBalance: decimal
  errors:
    - LOCK_NOT_FOUND
    - LOCK_ALREADY_RELEASED
    - TARGET_WALLET_NOT_FOUND
    - TARGET_WALLET_FROZEN
  notes: |
    Atomically releases the lock and transfers the currency
    to the target wallet. Creates a transaction record with
    transactionType: escrow_release.
```

### 13.3 Lock Schema

```yaml
# Schema: CurrencyLock
CurrencyLock:
  lockId: uuid
  walletId: uuid
  currencyDefinitionId: uuid
  amount: decimal
  escrowId: uuid
  createdAt: timestamp
  releasedAt: timestamp
  nullable: true
  releaseType: LockReleaseType
  nullable: true
  values:
    - transferred:
        description: "Lock released via transfer to recipient"
    - unlocked:
        description: "Lock released via unlock (refund)"
  transactionId: uuid
  nullable: true
  notes: "Set when releaseType is transferred"
```

### 13.4 Events

```yaml
# Emitted when currency is locked for escrow
currency.locked:
  topic: "currency.locked"
  payload:
    lockId: uuid
    walletId: uuid
    ownerId: uuid
    ownerType: string
    currencyDefinitionId: uuid
    currencyCode: string
    amount: decimal
    escrowId: uuid
    newLockedTotal: decimal
    newAvailableBalance: decimal
    lockedAt: timestamp

# Emitted when currency is unlocked (refund)
currency.unlocked:
  topic: "currency.unlocked"
  payload:
    lockId: uuid
    walletId: uuid
    ownerId: uuid
    ownerType: string
    currencyDefinitionId: uuid
    currencyCode: string
    amount: decimal
    escrowId: uuid
    newLockedTotal: decimal
    newAvailableBalance: decimal
    unlockedAt: timestamp
```

### 13.5 Integration Sequence

```
lib-escrow                    lib-currency
     │                              │
     │─── /escrow/lock ────────────►│
     │    { walletId, amount,       │
     │      currencyId, escrowId }  │
     │                              │
     │                        [Validate funds]
     │                        [Create lock record]
     │                        [Increase lockedAmount]
     │                        [Emit currency.locked]
     │                              │
     │◄── { lockId, success } ──────│
     │                              │
     │    [...escrow lifecycle...]  │
     │                              │
     │─── /escrow/transfer ────────►│  (on release)
     │    { lockId, targetWalletId }│
     │                              │
     │                        [Validate lock exists]
     │                        [Decrease source balance]
     │                        [Increase target balance]
     │                        [Create transaction]
     │                        [Mark lock as transferred]
     │                        [Emit currency.transferred]
     │                              │
     │◄── { transactionId } ────────│
```

---

*This specification is implementation-ready. lib-currency integrates with lib-escrow for multi-party agreements. Updates should maintain backwards compatibility or include migration guidance.*
