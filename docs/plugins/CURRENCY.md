# Currency Plugin Deep Dive

> **Plugin**: lib-currency
> **Schema**: schemas/currency-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Stores**: currency-definitions (MySQL), currency-wallets (MySQL), currency-balances (MySQL), currency-transactions (MySQL), currency-holds (MySQL), currency-balance-cache (Redis), currency-holds-cache (Redis), currency-idempotency (Redis)

---

## Overview

Multi-currency management service (L2 GameFoundation) for game economies. Handles currency definitions with scope/realm restrictions, wallet lifecycle management, balance operations (credit/debit/transfer with idempotency-key deduplication), authorization holds (reserve/capture/release), currency conversion via exchange-rate-to-base pivot, and escrow integration (deposit/release/refund endpoints consumed by lib-escrow). Features a background autogain worker for passive income and transaction history with configurable retention. All mutating balance operations use distributed locks for multi-instance safety.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for definitions, wallets, balances, transactions, holds; Redis caching for balances and holds; Redis idempotency store |
| lib-state (`IDistributedLockProvider`) | Balance-level locks for atomic credit/debit/transfer/hold-creation; hold-level locks for capture serialization; wallet-level locks for close operations; index-level locks for list operations; autogain locks to prevent concurrent modification |
| lib-messaging (`IMessageBus`) | Publishing all currency events (balance, wallet lifecycle, autogain, cap, hold, exchange rate); error event publishing via TryPublishErrorAsync |
| lib-worldstate (`IWorldstateClient`, L2, **required future migration**) | Autogain worker MUST transition from real-time intervals to game-time via Worldstate's `GetElapsedGameTime` API. At the default 24:1 time ratio, real-time autogain dramatically under-credits compared to game-time. This affects the living economy: NPC passive income must track the simulated world's time, not server time. Migration requires adding an `AutogainTimeSource` config property (enum: `RealTime`, `GameTime`; default `GameTime` once Worldstate is implemented). |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-escrow | References currency as an AssetType; stores CurrencyDefinitionId, CurrencyCode, CurrencyAmount on escrow asset models for currency custody |
| lib-contract | Registers `currency_transfer` and `fee` clause types that route to `/currency/transfer` and `/currency/balance/get` endpoints via mesh invocation |
| lib-quest | Hard dependency via `ICurrencyClient` for built-in `currency` prerequisite checking |
| lib-license | Hard dependency via `ICurrencyClient` for cost verification on license board operations |

---

## State Storage

**Stores**: 8 state stores (5 MySQL, 3 Redis)

| Store | Backend | Purpose |
|-------|---------|---------|
| `currency-definitions` | MySQL | Currency type definitions and behavior rules |
| `currency-wallets` | MySQL | Wallet ownership and status |
| `currency-balances` | MySQL | Currency balance records per wallet |
| `currency-transactions` | MySQL | Immutable transaction history |
| `currency-holds` | MySQL | Authorization hold records |
| `currency-balance-cache` | Redis | Real-time balance lookups (cached, refreshed on access) |
| `currency-holds-cache` | Redis | Authorization hold state cache for pre-auth scenarios |
| `currency-idempotency` | Redis | Idempotency key deduplication with TTL |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `def:{definitionId}` | `CurrencyDefinitionModel` | Currency definition data |
| `def-code:{code}` | `string` | Code-to-ID reverse lookup |
| `base-currency:{scope}` | `string` (`{defId}:{code}`) | Base currency per scope for O(1) lookup |
| `all-defs` | `List<string>` (JSON) | All definition IDs for iteration |
| `wallet:{walletId}` | `WalletModel` | Wallet state and ownership |
| `wallet-owner:{ownerId}:{ownerType}[:{realmId}]` | `string` | Owner-to-wallet-ID index |
| `bal:{walletId}:{currencyDefId}` | `BalanceModel` | Balance record per wallet+currency |
| `bal-wallet:{walletId}` | `List<string>` (JSON) | Currency IDs with balances in wallet |
| `bal-currency:{currencyDefId}` | `List<string>` (JSON) | Wallet IDs holding this currency (reverse index for autogain task) |
| `tx:{transactionId}` | `TransactionModel` | Transaction record |
| `tx-wallet:{walletId}` | `List<string>` (JSON) | Transaction IDs for a wallet |
| `tx-ref:{referenceType}:{referenceId}` | `List<string>` (JSON) | Transaction IDs by reference |
| `hold:{holdId}` | `HoldModel` | Authorization hold record |
| `hold-wallet:{walletId}:{currencyDefId}` | `List<string>` (JSON) | Hold IDs for a wallet+currency |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `currency.credited` | `CurrencyCreditedEvent` | Currency credited to wallet |
| `currency.debited` | `CurrencyDebitedEvent` | Currency debited from wallet |
| `currency.transferred` | `CurrencyTransferredEvent` | Currency transferred between wallets |
| `currency.autogain.calculated` | `CurrencyAutogainCalculatedEvent` | Autogain applied (lazy or task mode) |
| `currency.earn_cap.reached` | `CurrencyEarnCapReachedEvent` | Credit limited by daily/weekly earn cap |
| `currency.wallet_cap.reached` | `CurrencyWalletCapReachedEvent` | Credit hit wallet cap with cap_and_lose behavior |
| `currency.expired` | `CurrencyExpiredEvent` | Currency expired (schema-defined, not yet implemented) |
| `currency.exchange_rate.updated` | `CurrencyExchangeRateUpdatedEvent` | Exchange rate changed |
| `currency-definition.created` | `CurrencyDefinitionCreatedEvent` | New currency definition created |
| `currency-definition.updated` | `CurrencyDefinitionUpdatedEvent` | Currency definition updated |
| `currency-wallet.created` | `CurrencyWalletCreatedEvent` | New wallet created |
| `currency-wallet.frozen` | `CurrencyWalletFrozenEvent` | Wallet frozen |
| `currency-wallet.unfrozen` | `CurrencyWalletUnfrozenEvent` | Wallet unfrozen |
| `currency-wallet.closed` | `CurrencyWalletClosedEvent` | Wallet permanently closed |
| `currency.hold.created` | `CurrencyHoldCreatedEvent` | Authorization hold created |
| `currency.hold.captured` | `CurrencyHoldCapturedEvent` | Hold captured (funds debited) |
| `currency.hold.released` | `CurrencyHoldReleasedEvent` | Hold released (funds available again) |
| `currency.hold.expired` | `CurrencyHoldExpiredEvent` | Hold auto-released (schema-defined, not yet implemented) |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultAllowNegative` | `CURRENCY_DEFAULT_ALLOW_NEGATIVE` | `false` | Default for currencies that do not specify allowNegative |
| `AutogainProcessingMode` | `CURRENCY_AUTOGAIN_PROCESSING_MODE` | `Lazy` | How autogain is calculated (enum: Lazy = on-demand at query, Task = background worker) |
| `AutogainTaskStartupDelaySeconds` | `CURRENCY_AUTOGAIN_TASK_STARTUP_DELAY_SECONDS` | `15` | Delay before first autogain background cycle |
| `AutogainTaskIntervalMs` | `CURRENCY_AUTOGAIN_TASK_INTERVAL_MS` | `60000` | Background task processing interval (1 minute) |
| `AutogainBatchSize` | `CURRENCY_AUTOGAIN_BATCH_SIZE` | `1000` | Wallets processed per batch in task mode |
| `TransactionRetentionDays` | `CURRENCY_TRANSACTION_RETENTION_DAYS` | `365` | Transaction history retention period |
| `IdempotencyTtlSeconds` | `CURRENCY_IDEMPOTENCY_TTL_SECONDS` | `3600` | Idempotency key expiry (1 hour) |
| `HoldMaxDurationDays` | `CURRENCY_HOLD_MAX_DURATION_DAYS` | `7` | Maximum authorization hold duration |
| `BalanceCacheTtlSeconds` | `CURRENCY_BALANCE_CACHE_TTL_SECONDS` | `60` | Redis balance cache TTL |
| `HoldCacheTtlSeconds` | `CURRENCY_HOLD_CACHE_TTL_SECONDS` | `120` | Redis hold cache TTL |
| `BalanceLockTimeoutSeconds` | `CURRENCY_BALANCE_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for balance-level distributed locks |
| `HoldLockTimeoutSeconds` | `CURRENCY_HOLD_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for hold-level distributed locks |
| `WalletLockTimeoutSeconds` | `CURRENCY_WALLET_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for wallet-level distributed locks |
| `IndexLockTimeoutSeconds` | `CURRENCY_INDEX_LOCK_TIMEOUT_SECONDS` | `15` | Timeout for index update distributed locks |
| `IndexLockMaxRetries` | `CURRENCY_INDEX_LOCK_MAX_RETRIES` | `3` | Max retry attempts for acquiring index locks before logging error |
| `ExchangeRateUpdateMaxRetries` | `CURRENCY_EXCHANGE_RATE_UPDATE_MAX_RETRIES` | `3` | Max retry attempts for exchange rate update with optimistic concurrency |
| `ConversionRoundingPrecision` | `CURRENCY_CONVERSION_ROUNDING_PRECISION` | `8` | Number of decimal places for currency conversion rounding |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<CurrencyService>` | Scoped | Structured logging |
| `CurrencyServiceConfiguration` | Singleton | All 17 config properties (see Configuration section) |
| `IStateStoreFactory` | Singleton | Access to all 8 state stores |
| `IDistributedLockProvider` | Singleton | Balance locks (`currency-balance`), hold locks (`currency-hold`), wallet locks (`currency-wallet`), index locks (`currency-index`), autogain locks (`currency-autogain`) |
| `ITelemetryProvider` | Singleton | Telemetry span instrumentation for all async helper methods |
| `IMessageBus` | Scoped | Event publishing and error events |
| `CurrencyAutogainTaskService` | Hosted (Singleton) | Background worker for proactive autogain |

Service lifetime is **Scoped** (per-request). Background service is a hosted singleton.

---

## API Endpoints (Implementation Notes)

### Currency Definition Operations (4 endpoints)

- **CreateCurrencyDefinition** (`/currency/definition/create`): Validates code uniqueness via `def-code:` index. If `isBaseCurrency=true`, iterates all definitions to ensure only one base per scope. Generates UUID, saves model with all autogain/expiration/linkage/exchange-rate fields. Creates code-to-ID index. Adds to `all-defs` list. Publishes `currency-definition.created`.
- **GetCurrencyDefinition** (`/currency/definition/get`): Resolves by ID (direct lookup) or code (via `def-code:` index). Returns full definition response with all fields.
- **ListCurrencyDefinitions** (`/currency/definition/list`): Loads all definition IDs from `all-defs`. Iterates and loads each. Filters by `scope`, `isBaseCurrency`, `realmId` (global currencies always pass realm filter), and `includeInactive`. No pagination - returns all matching definitions.
- **UpdateCurrencyDefinition** (`/currency/definition/update`): Loads by ID. Applies partial updates to mutable fields (name, description, transferable, tradeable, allowNegative, caps, autogain settings, exchange rate, icon, displayFormat, isActive). Exchange rate update also sets `ExchangeRateUpdatedAt`. Publishes `currency-definition.updated`.

### Wallet Operations (6 endpoints)

- **CreateWallet** (`/currency/wallet/create`): Builds owner key from `ownerId:ownerType[:realmId]`. Checks uniqueness via owner index. Creates wallet with Active status. Saves wallet and owner-to-wallet index. Publishes `currency-wallet.created`. Returns Conflict if owner already has wallet.
- **GetWallet** (`/currency/wallet/get`): Resolves by walletId (direct) or by ownerId+ownerType+realmId (via owner index). Loads all balance summaries for the wallet including locked amounts from active holds. Returns wallet with balances.
- **GetOrCreateWallet** (`/currency/wallet/get-or-create`): Attempts owner index lookup first. If found, returns existing wallet with balances and `created=false`. If not found, delegates to CreateWallet and returns with `created=true` and empty balances.
- **FreezeWallet** (`/currency/wallet/freeze`): Uses ETag-based optimistic concurrency (`GetWithETagAsync` + `TrySaveAsync`). Sets status to Frozen with reason and timestamp. Returns Conflict if already frozen or on concurrent modification. Publishes `currency.wallet.frozen`.
- **UnfreezeWallet** (`/currency/wallet/unfreeze`): Uses ETag-based optimistic concurrency. Requires current status is Frozen (returns BadRequest otherwise). Clears frozen fields, sets status to Active. Publishes `currency.wallet.unfrozen`.
- **CloseWallet** (`/currency/wallet/close`): Requires a `transferRemainingTo` destination wallet. Loads all balances for closing wallet. Credits each positive balance to destination wallet via `InternalCreditAsync` (bypasses earn caps). Sets wallet status to Closed. Publishes `currency.wallet.closed`. Returns transferred balance details.

### Balance Operations (7 endpoints)

- **GetBalance** (`/currency/balance/get`): Validates wallet and definition exist. Gets or creates balance record. Applies lazy autogain if currency is autogain-enabled (acquires `currency-autogain` lock on `{walletId}:{currencyDefId}`, skips if lock unavailable). Resets daily/weekly earn caps if periods elapsed. Calculates locked amount from active holds. Returns amount, lockedAmount, effectiveAmount, earnCapInfo, and autogainInfo.
- **BatchGetBalances** (`/currency/balance/batch-get`): Iterates query list. For each, gets balance, applies lazy autogain, calculates locked amounts. Returns list of balance results. No locking between items - individual consistency per balance.
- **CreditCurrency** (`/currency/credit`): Validates amount > 0. Checks idempotency. Validates wallet exists and is not frozen (returns 422 if frozen). Acquires distributed lock on `walletId:currencyDefId`. Resets earn caps. Enforces daily/weekly earn caps (publishes `earn_cap.reached` when limited). Enforces per-wallet cap with configurable overflow behavior (reject returns 422, cap_and_lose truncates and publishes `wallet_cap.reached`). Updates balance and earn-tracking counters. Records transaction. Records idempotency key. Publishes `currency.credited`. Returns transaction record and cap-applied info.
- **DebitCurrency** (`/currency/debit`): Validates amount > 0. Checks idempotency. Validates wallet not frozen. Acquires distributed lock. Checks sufficient funds (negative allowed if definition or transaction-level override permits). Debits balance. Records transaction. Publishes `currency.debited`. Returns 422 for insufficient funds.
- **TransferCurrency** (`/currency/transfer`): Validates both wallets exist and are not frozen. Validates currency is transferable. Acquires two distributed locks in deterministic order (string comparison of lock keys) to prevent deadlock. Checks source has sufficient funds. Applies wallet cap on target (reject or cap_and_lose). Updates both balances. Records single transaction with both source/target balance snapshots. Publishes `currency.transferred`.
- **BatchCreditCurrency** (`/currency/batch-credit`): Checks batch-level idempotency. Iterates operations sequentially, delegating each to CreditCurrencyAsync with sub-key `{batchKey}:{index}`. Collects per-operation success/failure results. Records batch-level idempotency key. Returns partial success results.
- **BatchDebitCurrency** (`/currency/batch-debit`): Checks batch-level idempotency. Iterates operations sequentially, delegating each to DebitCurrencyAsync with sub-key `{batchKey}:{index}`. Each operation supports `allowNegative` override and metadata. Collects per-operation success/failure results. Records batch-level idempotency key. Returns partial success results. Symmetric to BatchCredit.

### Conversion Operations (4 endpoints)

- **CalculateConversion** (`/currency/convert/calculate`): Looks up both currency definitions. Calculates effective rate via base-currency pivot: `fromRate / toRate` where each rate is `ExchangeRateToBase` (1.0 for base currency itself). Returns calculated amount rounded to 8 decimal places, effective rate, and two-step conversion path. Returns 422 if exchange rates are missing.
- **ExecuteConversion** (`/currency/convert/execute`): Checks idempotency. Validates wallet not frozen. Calculates rate. Pre-validates target currency wallet cap (if `PerWalletCap` set with `CapOverflowBehavior.Reject`, checks whether `toAmount` would exceed cap before debiting source). Debits source currency (transaction type `Conversion_debit`). Credits target currency (transaction type `Conversion_credit`) with `BypassEarnCap=true` (conversions are exchanges, not earnings). If credit fails after successful debit, issues a compensating credit to the source currency (idempotency key `{key}:compensate`, `BypassEarnCap=true`) to reverse the debit. Uses sub-keys for idempotency on debit/credit legs. Returns both transaction records and effective rate.
- **GetExchangeRate** (`/currency/exchange-rate/get`): Looks up both definitions. Calculates effective rate and inverse rate. Returns both rates plus individual rates-to-base. Returns 422 if rates undefined.
- **UpdateExchangeRate** (`/currency/exchange-rate/update`): Validates currency is not the base currency (returns BadRequest). Updates `ExchangeRateToBase` and `ExchangeRateUpdatedAt`. Publishes `currency.exchange_rate.updated` with previous and new rates. Returns updated definition.

### Transaction History Operations (3 endpoints)

- **GetTransaction** (`/currency/transaction/get`): Simple lookup by transaction ID from MySQL store. Returns full transaction record.
- **GetTransactionHistory** (`/currency/transaction/history`): Validates wallet exists. Loads transaction IDs from wallet index. Iterates in reverse order (newest first). Applies filters: `currencyDefinitionId`, `transactionTypes` list, date range (bounded by retention floor). Client-side pagination via offset/limit after filtering. Returns paginated list with total count.
- **GetTransactionsByReference** (`/currency/transaction/by-reference`): Looks up transaction IDs via `tx-ref:{type}:{id}` index. Loads each transaction. Returns unfiltered list (all transactions for that reference).

### Analytics Operations (2 endpoints)

- **GetGlobalSupply** (`/currency/stats/global-supply`): Validates definition exists. Returns stubbed response with all zeros for totalSupply, inCirculation, inEscrow, totalMinted, totalBurned. Only returns the definition's GlobalSupplyCap value. Comment indicates production would use pre-computed aggregates.
- **GetWalletDistribution** (`/currency/stats/wallet-distribution`): Validates definition exists. Returns stubbed response with all zeros for totalWallets, walletsWithBalance, averageBalance, medianBalance, all percentiles, and giniCoefficient.

### Escrow Integration Operations (3 endpoints)

- **EscrowDeposit** (`/currency/escrow/deposit`): Thin wrapper around DebitCurrencyAsync with `TransactionType.Escrow_deposit` and `referenceType="escrow"`. Passes through escrowId as referenceId. Returns transaction record and new balance.
- **EscrowRelease** (`/currency/escrow/release`): Thin wrapper around CreditCurrencyAsync with `TransactionType.Escrow_release`, `referenceType="escrow"`, and `bypassEarnCap=true` (escrow releases should not count against earn limits). Returns transaction and new balance.
- **EscrowRefund** (`/currency/escrow/refund`): Same as EscrowRelease but with `TransactionType.Escrow_refund`. Also bypasses earn cap. Returns transaction and new balance.

### Authorization Hold Operations (4 endpoints)

- **CreateHold** (`/currency/hold/create`): Validates amount > 0. Checks idempotency. Validates wallet and definition exist. Acquires `currency-balance` distributed lock on `{walletId}:{currencyDefId}` (30s TTL) to serialize against concurrent balance changes and hold creation. Calculates effective balance (amount minus current active holds). Returns 422 if effective balance insufficient. Clamps expiry to `HoldMaxDurationDays`. Saves hold with Active status to MySQL. Updates Redis hold cache. Adds to wallet-currency hold index. Publishes `currency.hold.created`.
- **CaptureHold** (`/currency/hold/capture`): Checks idempotency. Reads hold from MySQL with ETag. Validates status is Active. Acquires `currency-hold` distributed lock on `{holdId}` (30s TTL) to prevent concurrent captures on the same hold. Validates captureAmount does not exceed hold amount. Debits the captured amount (transaction type `Fee`). Updates hold to Captured status with capturedAmount and completedAt. Uses optimistic concurrency on hold save. Updates Redis cache. Calculates and reports amountReleased (hold - captured). Publishes `currency.hold.captured`. Returns hold record, transaction, and new balance.
- **ReleaseHold** (`/currency/hold/release`): Reads hold from MySQL with ETag. Validates status is Active. Sets status to Released with completedAt. Uses optimistic concurrency. Updates Redis cache. Publishes `currency.hold.released`. No balance modification - funds simply become available again.
- **GetHold** (`/currency/hold/get`): Checks Redis hold cache first. On cache miss, reads from MySQL. Populates cache for future reads. Returns hold record with all fields including status, amounts, and timestamps.

---

## Visual Aid

```
Wallet Lifecycle
==================

  CreateWallet(ownerId, ownerType, realmId?)
       |
       +---> [Active]
                |
       FreezeWallet(reason)
                |
                v
            [Frozen]  <--- All credit/debit/transfer operations return 422
                |
       UnfreezeWallet()
                |
                v
            [Active]
                |
       CloseWallet(transferRemainingTo)
                |
                +--- For each positive balance:
                |       InternalCreditAsync(destination, amount, bypass_earn_cap)
                |
                v
            [Closed]  <--- Terminal state, no further operations


Transaction Flow (Credit with Cap Enforcement)
=================================================

  CreditCurrencyAsync(walletId, currencyDefId, amount, idempotencyKey)
       |
       +--- Check idempotency key (Redis, TTL=3600s)
       |    +-- Duplicate? -> return Conflict
       |
       +--- Validate wallet exists & not frozen
       +--- Validate currency definition exists
       |
       +--- Acquire distributed lock: "currency-balance:{walletId}:{currencyDefId}"
       |    +-- Timeout 30s -> return Conflict
       |
       +--- Reset earn caps if period elapsed (daily/weekly)
       |
       +--- Earn Cap Check (if !bypassEarnCap):
       |    +-- DailyEarnCap: remaining = cap - dailyEarned
       |    +-- WeeklyEarnCap: remaining = cap - weeklyEarned
       |    +-- Limited? -> publish earn_cap.reached, reduce amount
       |    +-- Amount=0 after cap? -> return 422
       |
       +--- Wallet Cap Check (if perWalletCap set):
       |    +-- newBalance > cap?
       |         +-- Behavior=Reject -> return 422
       |         +-- Behavior=CapAndLose -> truncate, publish wallet_cap.reached
       |
       +--- balance.Amount += creditAmount
       +--- balance.DailyEarned += creditAmount
       +--- balance.WeeklyEarned += creditAmount
       +--- SaveBalance (MySQL + Redis cache)
       |
       +--- RecordTransaction (MySQL + wallet/ref indexes)
       +--- RecordIdempotency (Redis, TTL)
       +--- Publish currency.credited
       |
       +--- Release lock (via using statement)
       |
       v
  Return (transaction, newBalance, capInfo)


Transfer with Deadlock Prevention
====================================

  TransferCurrencyAsync(sourceWalletId, targetWalletId, currencyDefId, amount)
       |
       +--- Validate both wallets exist & not frozen
       +--- Validate currency is transferable
       |
       +--- Compute lock keys:
       |      sourceLockKey = "{sourceWalletId}:{currencyDefId}"
       |      targetLockKey = "{targetWalletId}:{currencyDefId}"
       |
       +--- Sort keys lexicographically:
       |      firstLockKey = min(source, target)    <-- Deterministic order
       |      secondLockKey = max(source, target)   <-- prevents deadlock
       |
       +--- Acquire lock 1: "currency-balance:{firstLockKey}"
       +--- Acquire lock 2: "currency-balance:{secondLockKey}"
       |
       +--- Check source balance >= amount
       +--- Apply wallet cap on target
       +--- Modify both balances atomically
       |
       +--- Record transaction (source+target snapshots)
       +--- Publish currency.transferred
       |
       v
  Both locks released via using statements


Authorization Hold Pattern (Reserve / Capture / Release)
==========================================================

  CreateHold(walletId, currencyDefId, amount, expiresAt)
       |
       +--- effectiveBalance = balance.Amount - SUM(active holds)
       +--- effectiveBalance >= amount? -> proceed
       |
       +--- Save hold: status=Active
       +--- [Hold] reserves funds (reduces effectiveBalance but not actual balance)
       |
       +-----------+-----------+
       |           |           |
  CaptureHold  ReleaseHold  [Expired]
       |           |           |
       v           v           v
  Debit actual   No balance  Auto-release
  amount from    change -    (not yet
  wallet         funds free  implemented)
       |           |
       v           v
  status=        status=
  Captured       Released


Conversion System (Base Currency Pivot)
==========================================

  Currency A ---- ExchangeRateToBase=2.5 ----> [BASE]
  Currency B ---- ExchangeRateToBase=0.1 ----> [BASE]

  Convert 100 A -> B:
    effectiveRate = fromRate / toRate = 2.5 / 0.1 = 25.0
    toAmount = 100 * 25.0 = 2500 B

  ExecuteConversion:
    0. Pre-validate: target wallet cap on B (Reject behavior only)
    1. Debit  100 A  (type=Conversion_debit)
    2. Credit 2500 B (type=Conversion_credit, BypassEarnCap=true)
       If credit fails: compensating credit 100 A (key="{key}:compensate")
    Both idempotent via sub-keys: "{key}:debit", "{key}:credit"


Autogain Background Task (CurrencyAutogainTaskService)
========================================================

  [Mode: "lazy"]                          [Mode: "task"]
  Autogain calculated on              Background worker processes
  GetBalance/BatchGetBalances          all eligible wallets proactively
  requests only                              |
                                             |
                                   +------- Loop (every AutogainTaskIntervalMs) ------+
                                   |                                                  |
                                   |  1. Load all-defs from state store               |
                                   |  2. Filter: autogainEnabled=true                 |
                                   |  3. For each autogain currency:                  |
                                   |     a. Load bal-currency:{defId} reverse index   |
                                   |     b. Process wallets in batches                |
                                   |        (AutogainBatchSize per batch)             |
                                   |     c. For each balance:                         |
                                   |        - Acquire lock "currency-autogain"        |
                                   |        - Calculate periods elapsed               |
                                   |        - Apply gain (simple or compound)         |
                                   |        - Enforce autogain cap                    |
                                   |        - Save balance                            |
                                   |        - Publish autogain.calculated             |
                                   |                                                  |
                                   +--------------------------------------------------+

  Autogain Modes:
    Simple:   gain = periodsElapsed * autogainAmount
    Compound: gain = balance * ((1 + rate)^periods - 1)

  Cap enforcement:
    balance >= cap -> gain = 0
    balance + gain > cap -> gain = cap - balance


Escrow Integration Flow
=========================

  lib-escrow                        lib-currency
      |                                  |
      |-- EscrowDeposit(amount) -------->|
      |   (debit wallet for escrow)      |-- DebitCurrency(Escrow_deposit)
      |                                  |      ref: "escrow", escrowId
      |                                  |
      |      [Escrow holds custody]      |
      |                                  |
      |-- EscrowRelease(amount) -------->|
      |   (credit recipient)             |-- CreditCurrency(Escrow_release)
      |                                  |      bypassEarnCap=true
      |       OR                         |
      |                                  |
      |-- EscrowRefund(amount) --------->|
      |   (credit depositor back)        |-- CreditCurrency(Escrow_refund)
      |                                  |      bypassEarnCap=true
```

---

## Stubs & Unimplemented Features

1. **Global supply analytics**: `GetGlobalSupply` returns all zeros. Comment indicates production would use pre-computed aggregates from balance data. No aggregation logic exists.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/211 -->
2. **Wallet distribution analytics**: `GetWalletDistribution` returns all zeros for wallet count, averages, percentiles, and Gini coefficient. No statistical computation is implemented.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/470 -->
3. **Currency expiration**: The `CurrencyExpiredEvent` is defined in the events schema and the definition model has `Expires`, `ExpirationPolicy`, `ExpirationDate`, `ExpirationDuration`, and `SeasonId` fields, but no background task or lazy check implements expiration logic.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/222 -->
4. **Hold expiration**: The `CurrencyHoldExpiredEvent` is defined in the events schema but no mechanism (background task or lazy check) auto-releases expired holds. Expired holds remain Active and continue to reduce effective balance.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/222 -->
5. **Global supply cap enforcement**: `GlobalSupplyCap` is stored on currency definitions but never checked during credit operations. There is no aggregate tracking of total minted supply.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/471 -->
6. **Item linkage**: `LinkedToItem`, `LinkedItemTemplateId`, and `LinkageMode` fields are stored on definitions but no logic enforces item-currency linkage (e.g., requiring item ownership for currency access).
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/473 -->
7. **Transaction retention cleanup**: `TransactionRetentionDays` config exists but old transactions are only filtered at query time, never actually deleted. Transactions accumulate indefinitely.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/222 -->

---

## Potential Extensions

1. **Aggregate tracking for analytics**: Maintain running totals (minted, burned, in-circulation) via transaction events. Use pre-computed Redis counters updated on each credit/debit for O(1) supply queries.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/211 -->
2. **Hold expiration background task**: Similar to CurrencyAutogainTaskService, periodically scan active holds and auto-release those past ExpiresAt, publishing `currency.hold.expired`.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/222 -->
3. **Currency expiration background task**: Scan balances for currencies with expiration policies. Apply expiration logic (zero-out, reduce, or convert) based on ExpirationPolicy.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/222 -->
4. **Global supply cap enforcement**: Track total supply per currency in a Redis counter. Check cap during credit operations. Reject or truncate credits that would exceed global cap.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/471 -->
5. **Item-linked currencies**: Enforce that item must exist in player inventory for linked currency operations. Query lib-item during credit/debit to validate linkage.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/473 -->
6. **Transaction pruning background task**: Delete transactions older than TransactionRetentionDays to prevent unbounded state growth. Currently retention is only enforced at query time (filtered out of results).
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/222 -->
7. **Universal value anchoring**: Add `UniversalValue` field to currency definitions representing intrinsic worth (relative to a 1.0 baseline). Exchange rates can then be computed dynamically from universal values plus location-scoped modifiers (tariff, war, festival, shortage) rather than requiring manual `ExchangeRateToBase` updates. Universal values shift in response to game events (gold discoveries lower gold's value, wartime increases weapon-currency values). See [Economy System Guide](../guides/ECONOMY-SYSTEM.md#8-exchange-rate-extensions).
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/478 -->
8. **Location-scoped exchange rates**: Extend exchange rates to vary by scope (global, realm, location). A frontier outpost might offer worse rates than a capital city. Support modifier stacking with source tracking and expiry. Add buy/sell spread fields for NPC money changer profit margins. Enables arbitrage opportunities and regional economic variation.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/478 -->

---

## Known Quirks & Caveats

### Bugs

1. ~~**EarnCapResetTime not updatable**~~: **FIXED** (2026-02-24) - Added `earnCapResetTime` to `UpdateCurrencyDefinitionRequest` schema and corresponding field update in `UpdateCurrencyDefinitionAsync`.

### Intentional Quirks

1. **Transfer debits full amount even when target cap truncates**: When the target wallet cap causes overflow with `cap_and_lose` behavior, the source is debited the full transfer amount but the target only receives the capped portion. The overflow amount is lost (burned) as a currency sink.

2. **First autogain access initializes without retroactive gain**: When a balance with autogain is first accessed (either lazy or task mode), `LastAutogainAt` is set to now without applying any retroactive gain. This prevents exploits from creating a balance and waiting before first access.

3. **Batch credit/debit is non-atomic**: Individual credit or debit operations in a batch can succeed or fail independently. A batch can return partial success (some items credited/debited, some failed). The batch-level idempotency key prevents replaying the entire batch. On retry of a partially-completed batch, already-completed sub-operations are detected via pre-check of sub-operation idempotency keys and reported as successful with the original transaction data (TOCTOU race during concurrent submissions possible but harmless — inner idempotency prevents double-processing).

4. **List indexes are read-modify-write under lock**: `AddToListAsync` acquires a per-key distributed lock, reads a JSON list from MySQL, appends, and saves. Lock contention is scoped to a single index key (e.g., `bal-currency:{defId}`). Index writes only occur on new balance creation (not on every credit/debit), so contention is limited to concurrent new-wallet-creation for the same currency. Lock timeout (`IndexLockTimeoutSeconds`, default 15s) and retries (`IndexLockMaxRetries`, default 3) are configurable. At very high scale (thousands of simultaneous new balances for one currency), this could bottleneck; migration to Redis sets or a dedicated index store would be the remedy.

5. **Balance cache has sub-millisecond stale window**: `SaveBalanceAsync` writes to MySQL then immediately updates Redis cache (write-through pattern). Between the MySQL write and Redis update, or if the Redis cache write silently fails (non-fatal), a concurrent reader could get the pre-mutation value. Cache TTL is 60 seconds (configurable via `BalanceCacheTtlSeconds`). For authoritative balance checks (e.g., pre-authorization), use the hold mechanism which bypasses cache and reads directly from MySQL under distributed lock.

6. **Read-only hold queries are eventually consistent**: All mutating balance operations (`CreateHoldAsync`, `CreditCurrencyAsync`, `DebitCurrencyAsync`, `TransferCurrencyAsync`) acquire the same `currency-balance:{walletId}:{currencyDefId}` distributed lock, so mutations are fully serialized — a hold created by one operation is guaranteed visible to the next mutation. However, read-only queries (`GetBalanceAsync`, `BatchGetBalancesAsync`) do NOT acquire the lock and may briefly see stale hold state if a `CreateHoldAsync` is mid-execution (hold saved to MySQL but index not yet updated). This window is sub-millisecond on a single node. For authoritative pre-authorization checks, use `CreateHoldAsync` itself (which reads under lock), not `GetBalanceAsync`.

7. **Conversion has transient cross-currency balance inconsistency**: `ExecuteConversionAsync` uses a two-step saga: debit source currency (under `currency-balance:{walletId}:{fromCurrencyId}` lock), then credit target currency (under `currency-balance:{walletId}:{toCurrencyId}` lock). Between the debit completing and the credit completing, a concurrent `GetBalance` would see the source reduced but the target not yet increased, making the wallet's total cross-currency value appear temporarily lower. This is NOT a double-spending risk — each individual balance operation is fully serialized under its own distributed lock, so no currency can be spent twice. The window is sub-millisecond (between one async call returning and the next starting). If the credit fails, a compensating credit with idempotency key `{key}:compensate` reverses the debit. This is inherent to the saga pattern without distributed transactions and is the correct architectural trade-off.

### Design Considerations

1. **Transaction retention only enforced at query time**: Transactions beyond `TransactionRetentionDays` are filtered out of history queries but remain in the MySQL store indefinitely. No background cleanup task exists to actually delete old transactions.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/222 -->

2. **Autogain uses real-time, should use game-time**: The autogain background worker (`CurrencyAutogainTaskService`) uses `DateTimeOffset.UtcNow` and real-time `Task.Delay` intervals. In a living world with a 24:1 game-time ratio, NPCs earning passive income in real-time receive 24x less income than they should per game-day. When Worldstate (L2) is implemented, the autogain worker must call `GetElapsedGameTime` to compute game-time elapsed since last accrual, then apply the autogain rate against game-time rather than real-time. This ensures the NPC-driven economy scales correctly with the world's simulated time. Both `Lazy` mode (on-demand calculation) and `Task` mode (background worker) need this transition.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/433 -->

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Pending Design

- **Global supply analytics** - Needs design decisions on aggregation strategy, minted/burned semantics, and escrow integration. Issue: https://github.com/beyond-immersion/bannou-service/issues/211
- [#433](https://github.com/beyond-immersion/bannou-service/issues/433) - Currency autogain must transition from real-time to game-time via Worldstate (blocked by Worldstate implementation)
- [#470](https://github.com/beyond-immersion/bannou-service/issues/470) - Wallet distribution analytics stub returns all zeros. Needs aggregation strategy decision (shared with #211)
- [#471](https://github.com/beyond-immersion/bannou-service/issues/471) - Global supply cap enforcement needs aggregation strategy (shared with #211). `GlobalSupplyCap` stored but never checked during credits.
- [#473](https://github.com/beyond-immersion/bannou-service/issues/473) - Item linkage enforcement: `LinkedToItem`, `LinkedItemTemplateId`, and `LinkageMode` fields are stored but have zero runtime enforcement. Needs design decisions on what each linkage mode means and which operations to gate.
- [#478](https://github.com/beyond-immersion/bannou-service/issues/478) - Universal value anchoring for dynamic exchange rates. Needs design on coexistence vs replacement of `ExchangeRateToBase`, modifier ownership (L2 vs L4), value change triggers, and relationship to location-scoped exchange rates.

### Completed

*(All previous completed items processed and removed during 2026-02-24 maintenance.)*
