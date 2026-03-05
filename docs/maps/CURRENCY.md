# Currency Implementation Map

> **Plugin**: lib-currency
> **Schema**: schemas/currency-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/CURRENCY.md](../plugins/CURRENCY.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-currency |
| Layer | L2 GameFoundation |
| Endpoints | 33 |
| State Stores | currency-definitions (MySQL), currency-wallets (MySQL), currency-balances (MySQL), currency-transactions (MySQL), currency-holds (MySQL), currency-balance-cache (Redis), currency-holds-cache (Redis), currency-idempotency (Redis), currency-lock (Redis) |
| Events Published | 16 (currency.credited, currency.debited, currency.transferred, currency.autogain.calculated, currency.earn-cap.reached, currency.wallet-cap.reached, currency.exchange-rate.updated, currency.definition.created, currency.definition.updated, currency.wallet.created, currency.wallet.frozen, currency.wallet.unfrozen, currency.wallet.closed, currency.hold.created, currency.hold.captured, currency.hold.released) |
| Events Consumed | 3 (self-subscription for cache invalidation) |
| Client Events | 3 (currency.balance.changed, currency.wallet.frozen, currency.wallet.unfrozen) |
| Background Services | 1 (CurrencyAutogainTaskService) |

---

## State

**Store**: `currency-definitions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `def:{definitionId}` | `CurrencyDefinitionModel` | Currency definition record |
| `def-code:{code}` | `string` | Code-to-ID reverse lookup |
| `base-currency:{scope}` | `string` (`{defId}:{code}`) | Base currency per scope for O(1) lookup |
| `all-defs` | `string` (JSON list of IDs) | All definition IDs for iteration |

**Store**: `currency-wallets` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `wallet:{walletId}` | `WalletModel` | Wallet state and ownership |
| `wallet-owner:{ownerId}:{ownerType}[:{realmId}]` | `string` | Owner-to-wallet-ID index |

**Store**: `currency-balances` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bal:{walletId}:{currencyDefId}` | `BalanceModel` | Balance record per wallet+currency |
| `bal-wallet:{walletId}` | `string` (JSON list) | Currency IDs with balances in wallet |
| `bal-currency:{currencyDefId}` | `string` (JSON list) | Wallet IDs holding this currency (reverse index for autogain) |

**Store**: `currency-transactions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tx:{transactionId}` | `TransactionModel` | Transaction record |
| `tx-wallet:{walletId}` | `string` (JSON list) | Transaction IDs for a wallet |
| `tx-ref:{refType}:{refId}` | `string` (JSON list) | Transaction IDs by reference |

**Store**: `currency-holds` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `hold:{holdId}` | `HoldModel` | Authorization hold record |
| `hold-wallet:{walletId}:{currencyDefId}` | `string` (JSON list) | Hold IDs for a wallet+currency |

**Store**: `currency-balance-cache` (Backend: Redis, TTL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `bal:{walletId}:{currencyDefId}` | `BalanceModel` | Read-through balance cache |

**Store**: `currency-holds-cache` (Backend: Redis, TTL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `hold:{holdId}` | `HoldModel` | Read-through hold cache |

**Store**: `currency-idempotency` (Backend: Redis, TTL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{idempotencyKey}` | `string` | Idempotency key deduplication (value = transactionId or holdId) |

**Store**: `currency-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `{walletId}:{currencyDefId}` | Balance-level lock (credit/debit/transfer/hold/autogain) |
| `{holdId}` | Hold-level lock (capture/release) |
| `{walletId}` | Wallet-level lock (close) |
| `{indexKey}` | Index update lock (list append/remove) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | All 9 state stores |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Balance, hold, wallet, index, autogain locks |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 16 event topics |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation |
| lib-connect (`IEntitySessionRegistry`) | L1 | Hard | Client events to wallet owner WebSocket sessions |

**DI interfaces implemented**: `IVariableProviderFactory` via `CurrencyProviderFactory` — exposes `${currency.*}` ABML namespace to Actor (L2) via the Variable Provider Factory pattern.

**Self-subscription**: Currency subscribes to its own `currency.credited`, `currency.debited`, `currency.transferred` events to invalidate the in-process `ICurrencyDataCache` (used by `CurrencyProviderFactory`). Local-only fan-out; multi-node cache coherence relies on `ProviderCacheTtlSeconds` TTL expiry.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `currency.definition.created` | `CurrencyDefinitionCreatedEvent` | CreateCurrencyDefinition |
| `currency.definition.updated` | `CurrencyDefinitionUpdatedEvent` | UpdateCurrencyDefinition |
| `currency.wallet.created` | `CurrencyWalletCreatedEvent` | CreateWallet |
| `currency.wallet.frozen` | `CurrencyWalletFrozenEvent` | FreezeWallet |
| `currency.wallet.unfrozen` | `CurrencyWalletUnfrozenEvent` | UnfreezeWallet |
| `currency.wallet.closed` | `CurrencyWalletClosedEvent` | CloseWallet |
| `currency.credited` | `CurrencyCreditedEvent` | CreditCurrency (also via BatchCredit, EscrowRelease, EscrowRefund, ExecuteConversion, CloseWallet) |
| `currency.debited` | `CurrencyDebitedEvent` | DebitCurrency (also via BatchDebit, EscrowDeposit, CaptureHold, ExecuteConversion) |
| `currency.transferred` | `CurrencyTransferredEvent` | TransferCurrency |
| `currency.autogain.calculated` | `CurrencyAutogainCalculatedEvent` | GetBalance/BatchGetBalances (lazy), CurrencyAutogainTaskService (task) |
| `currency.earn-cap.reached` | `CurrencyEarnCapReachedEvent` | CreditCurrency when earn cap limits amount |
| `currency.wallet-cap.reached` | `CurrencyWalletCapReachedEvent` | CreditCurrency/TransferCurrency when wallet cap truncates with cap_and_lose |
| `currency.exchange-rate.updated` | `CurrencyExchangeRateUpdatedEvent` | UpdateExchangeRate |
| `currency.hold.created` | `CurrencyHoldCreatedEvent` | CreateHold |
| `currency.hold.captured` | `CurrencyHoldCapturedEvent` | CaptureHold |
| `currency.hold.released` | `CurrencyHoldReleasedEvent` | ReleaseHold |

Schema also defines `currency.expired` and `currency.hold.expired` — not yet implemented.

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `currency.credited` | `HandleCurrencyCreditedAsync` | Invalidate `ICurrencyDataCache` for `evt.OwnerId` |
| `currency.debited` | `HandleCurrencyDebitedAsync` | Invalidate `ICurrencyDataCache` for `evt.OwnerId` |
| `currency.transferred` | `HandleCurrencyTransferredAsync` | Invalidate `ICurrencyDataCache` for both `SourceOwnerId` and `TargetOwnerId` |

Self-subscription only — no external event consumption.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<CurrencyService>` | Structured logging |
| `CurrencyServiceConfiguration` | All 18 config properties |
| `IStateStoreFactory` | Constructor-cached into 13 typed store fields (factory not stored as field) |
| `IDistributedLockProvider` | Distributed locks |
| `IMessageBus` | Event publishing |
| `ITelemetryProvider` | Telemetry spans |
| `IEntitySessionRegistry` | Client event publishing to wallet owner sessions |
| `ICurrencyDataCache` | In-memory actor variable provider cache (invalidated by self-subscribed events) |
| `IEventConsumer` | Self-subscribes to credited/debited/transferred for cache invalidation |
| `CurrencyAutogainTaskService` | Hosted singleton background worker |
| `CurrencyProviderFactory` | Singleton `IVariableProviderFactory` for `${currency.*}` ABML namespace |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateCurrencyDefinition | POST /currency/definition/create | admin | def, def-code, all-defs, base-currency | currency.definition.created |
| GetCurrencyDefinition | POST /currency/definition/get | user | - | - |
| ListCurrencyDefinitions | POST /currency/definition/list | user | - | - |
| UpdateCurrencyDefinition | POST /currency/definition/update | admin | def | currency.definition.updated |
| CreateWallet | POST /currency/wallet/create | developer | wallet, wallet-owner | currency.wallet.created |
| GetWallet | POST /currency/wallet/get | user | - | - |
| GetOrCreateWallet | POST /currency/wallet/get-or-create | developer | wallet, wallet-owner | currency.wallet.created |
| FreezeWallet | POST /currency/wallet/freeze | admin | wallet | currency.wallet.frozen |
| UnfreezeWallet | POST /currency/wallet/unfreeze | admin | wallet | currency.wallet.unfrozen |
| CloseWallet | POST /currency/wallet/close | admin | wallet, bal, tx | currency.wallet.closed, currency.credited |
| GetBalance | POST /currency/balance/get | user | bal (autogain only) | currency.autogain.calculated |
| BatchGetBalances | POST /currency/balance/batch-get | user | bal (autogain only) | currency.autogain.calculated |
| CreditCurrency | POST /currency/credit | developer | bal, tx, idempotency | currency.credited, currency.earn-cap.reached, currency.wallet-cap.reached |
| DebitCurrency | POST /currency/debit | developer | bal, tx, idempotency | currency.debited |
| TransferCurrency | POST /currency/transfer | developer | bal (x2), tx, idempotency | currency.transferred, currency.wallet-cap.reached |
| BatchCreditCurrency | POST /currency/batch-credit | developer | bal, tx, idempotency | currency.credited |
| BatchDebitCurrency | POST /currency/batch-debit | developer | bal, tx, idempotency | currency.debited |
| CalculateConversion | POST /currency/convert/calculate | user | - | - |
| ExecuteConversion | POST /currency/convert/execute | developer | bal, tx, idempotency | currency.debited, currency.credited |
| GetExchangeRate | POST /currency/exchange-rate/get | user | - | - |
| UpdateExchangeRate | POST /currency/exchange-rate/update | admin | def | currency.exchange-rate.updated |
| GetTransaction | POST /currency/transaction/get | developer | - | - |
| GetTransactionHistory | POST /currency/transaction/history | user | - | - |
| GetTransactionsByReference | POST /currency/transaction/by-reference | developer | - | - |
| GetGlobalSupply | POST /currency/stats/global-supply | user | - | - |
| GetWalletDistribution | POST /currency/stats/wallet-distribution | admin | - | - |
| EscrowDeposit | POST /currency/escrow/deposit | developer | bal, tx, idempotency | currency.debited |
| EscrowRelease | POST /currency/escrow/release | developer | bal, tx, idempotency | currency.credited |
| EscrowRefund | POST /currency/escrow/refund | developer | bal, tx, idempotency | currency.credited |
| CreateHold | POST /currency/hold/create | developer | hold, hold-wallet, idempotency | currency.hold.created |
| CaptureHold | POST /currency/hold/capture | developer | hold, bal, tx, hold-wallet, idempotency | currency.hold.captured, currency.debited |
| ReleaseHold | POST /currency/hold/release | developer | hold, hold-wallet | currency.hold.released |
| GetHold | POST /currency/hold/get | developer | - | - |

---

## Methods

### CreateCurrencyDefinition
POST /currency/definition/create | Roles: [admin]

```
READ definitions:def-code:{code}                   -> 409 if exists (code uniqueness)
IF isBaseCurrency
  READ definitions:base-currency:{scope}           -> 409 if exists (one base per scope)
WRITE definitions:def:{definitionId}               <- new CurrencyDefinitionModel from request
WRITE definitions:def-code:{code}                  <- definitionId
LOCK index:all-defs                                // AddToListAsync with retries
  READ definitions:all-defs
  WRITE definitions:all-defs                       <- append definitionId
IF isBaseCurrency
  WRITE definitions:base-currency:{scope}          <- "{definitionId}:{code}"
PUBLISH currency.definition.created { definitionId, code, name, scope, precision, isActive }
RETURN (200, CurrencyDefinitionResponse)
```

### GetCurrencyDefinition
POST /currency/definition/get | Roles: [user]

```
// ResolveCurrencyDefinitionAsync: by code or by ID
IF code provided
  READ definitions:def-code:{code}                 -> 404 if null
READ definitions:def:{definitionId}                -> 404 if null
RETURN (200, CurrencyDefinitionResponse)
```

### ListCurrencyDefinitions
POST /currency/definition/list | Roles: [user]

```
READ definitions:all-defs                          // JSON list of all definition IDs
FOREACH definitionId in list
  READ definitions:def:{definitionId}              // skip nulls
  // Filter in-memory: scope, isBaseCurrency, realmId, includeInactive
RETURN (200, ListCurrencyDefinitionsResponse)
```

### UpdateCurrencyDefinition
POST /currency/definition/update | Roles: [admin]

```
READ definitions:def:{definitionId}                -> 404 if null
// Apply partial updates to mutable fields (name, description, caps, autogain, etc.)
// Immutable: code, scope, precision, isBaseCurrency
IF exchangeRateToBase changed
  // Set ExchangeRateUpdatedAt (no separate exchange-rate.updated event)
WRITE definitions:def:{definitionId}               <- mutated model (no ETag)
PUBLISH currency.definition.updated { definitionId, code, name, scope, precision, isActive, modifiedAt }
RETURN (200, CurrencyDefinitionResponse)
```

### CreateWallet
POST /currency/wallet/create | Roles: [developer]

```
// ownerKey = "{ownerId}:{ownerType}" or "{ownerId}:{ownerType}:{realmId}"
READ wallets:wallet-owner:{ownerKey}               -> 409 if exists (one wallet per owner)
WRITE wallets:wallet:{walletId}                    <- new WalletModel { Status = Active }
WRITE wallets:wallet-owner:{ownerKey}              <- walletId
PUBLISH currency.wallet.created { walletId, ownerId, ownerType, realmId, status }
RETURN (200, WalletResponse)
```

### GetWallet
POST /currency/wallet/get | Roles: [user]

```
// ResolveWalletAsync: by walletId or by ownerId+ownerType+realmId
IF owner lookup
  READ wallets:wallet-owner:{ownerKey}             -> 404 if null
READ wallets:wallet:{walletId}                     -> 404 if null
// GetAllBalancesForWalletAsync
READ balances:bal-wallet:{walletId}                // list of currencyDefIds
FOREACH currencyDefId
  READ balances:bal:{walletId}:{currencyDefId}
  READ definitions:def:{currencyDefId}             // for currencyCode
  // GetTotalHeldAmountAsync: reads hold index + holds (with cache)
RETURN (200, WalletWithBalancesResponse)
```

### GetOrCreateWallet
POST /currency/wallet/get-or-create | Roles: [developer]

```
READ wallets:wallet-owner:{ownerKey}
IF found
  // Load wallet + balances (same as GetWallet)
  RETURN (200, GetOrCreateWalletResponse { created: false })
// Delegate to CreateWalletAsync
IF create returns 409                              // race condition
  // Re-read wallet-owner index and return existing
RETURN (200, GetOrCreateWalletResponse { created: true })
```

### FreezeWallet
POST /currency/wallet/freeze | Roles: [admin]

```
READ wallets:wallet:{walletId} [with ETag]         -> 404 if null
IF status == Frozen                                -> 409
// Set status = Frozen, frozenReason, frozenAt
ETAG-WRITE wallets:wallet:{walletId}               -> 409 if ETag mismatch
PUBLISH currency.wallet.frozen { walletId, ownerId, reason }
PUSH CurrencyWalletFrozenClientEvent to owner sessions
RETURN (200, WalletResponse)
```

### UnfreezeWallet
POST /currency/wallet/unfreeze | Roles: [admin]

```
READ wallets:wallet:{walletId} [with ETag]         -> 404 if null
IF status != Frozen                                -> 400
// Set status = Active, clear frozen fields
ETAG-WRITE wallets:wallet:{walletId}               -> 409 if ETag mismatch
PUBLISH currency.wallet.unfrozen { walletId, ownerId }
PUSH CurrencyWalletUnfrozenClientEvent to owner sessions
RETURN (200, WalletResponse)
```

### CloseWallet
POST /currency/wallet/close | Roles: [admin]

```
READ wallets:wallet:{walletId} [with ETag]         -> 404 if null
IF status == Closed                                -> 400
READ wallets:wallet:{transferRemainingTo}           -> 404 if null
LOCK wallet:{walletId}                             -> 409 if fails
  // GetAllBalancesForWalletAsync
  READ balances:bal-wallet:{walletId}
  FOREACH currencyDefId
    READ balances:bal:{walletId}:{currencyDefId}
    // GetTotalHeldAmountAsync
    IF heldAmount > 0                              -> 400 (active holds exist)
  FOREACH balance with Amount > 0
    // InternalCreditAsync -> delegates to CreditCurrencyAsync with BypassEarnCap
    // Each credit acquires its own balance-level lock, publishes currency.credited
  // Set status = Closed
  ETAG-WRITE wallets:wallet:{walletId}             -> 409 if ETag mismatch
PUBLISH currency.wallet.closed { walletId, ownerId, balancesTransferredTo }
RETURN (200, CloseWalletResponse { transferredBalances })
```

### GetBalance
POST /currency/balance/get | Roles: [user]

```
READ wallets:wallet:{walletId}                     -> 404 if null
READ definitions:def:{currencyDefinitionId}        -> 404 if null
// GetOrCreateBalanceAsync: check cache, fallback MySQL, create empty if absent
READ balance-cache:bal:{walletId}:{currencyDefId}
IF cache miss
  READ balances:bal:{walletId}:{currencyDefId}
  IF null -> create empty balance
IF autogainEnabled
  // ApplyAutogainIfNeededAsync
  LOCK balance:{walletId}:{currencyDefId}          // skip if lock unavailable
    // Re-read balance under lock, calculate periods, apply gain
    WRITE balances:bal:{walletId}:{currencyDefId}
    PUBLISH currency.autogain.calculated { walletId, periodsApplied, amountGained }
    PUSH CurrencyBalanceChangedClientEvent to owner sessions
// ResetEarnCapsIfNeeded (in-memory only, NOT persisted)
// GetTotalHeldAmountAsync
RETURN (200, GetBalanceResponse { amount, lockedAmount, effectiveAmount, earnCapInfo, autogainInfo })
```

### BatchGetBalances
POST /currency/balance/batch-get | Roles: [user]

```
FOREACH query in queries (sequential)
  // GetOrCreateBalanceAsync (cache then MySQL)
  // GetDefinitionByIdAsync
  IF autogainEnabled
    // GetWalletByIdAsync, ApplyAutogainIfNeededAsync
  // GetTotalHeldAmountAsync
RETURN (200, BatchGetBalancesResponse { balances })
```

### CreditCurrency
POST /currency/credit | Roles: [developer]

```
READ idempotency:{idempotencyKey}                  -> 409 if exists
READ wallets:wallet:{walletId}                     -> 404 if null, 400 if frozen
READ definitions:def:{currencyDefinitionId}        -> 404 if null
LOCK balance:{walletId}:{currencyDefId}            -> 409 if fails
  // GetOrCreateBalanceAsync
  // ResetEarnCapsIfNeeded
  IF !bypassEarnCap AND earn cap applies
    // Apply daily/weekly earn cap, reduce creditAmount
    IF capped
      PUBLISH currency.earn-cap.reached { capType, capAmount, attemptedAmount, limitedAmount }
    IF creditAmount <= 0 after cap                 -> 400
  IF perWalletCap
    IF Reject AND newBalance > cap                 -> 400
    IF CapAndLose AND newBalance > cap
      // Truncate creditAmount
      PUBLISH currency.wallet-cap.reached { capAmount, overflowBehavior, amountLost }
  // balance.Amount += creditAmount, update earn tracking counters
  WRITE balances:bal:{walletId}:{currencyDefId}    <- updated balance
  WRITE balance-cache:bal:{walletId}:{currencyDefId}
  // RecordTransactionAsync
  WRITE transactions:tx:{transactionId}            <- new TransactionModel
  LOCK index:tx-wallet:{walletId}
    // AddToListAsync
  IF referenceType provided
    LOCK index:tx-ref:{refType}:{refId}
      // AddToListAsync
  WRITE idempotency:{idempotencyKey}               <- transactionId (TTL)
PUBLISH currency.credited { transactionId, walletId, ownerId, amount, transactionType, newBalance, earnCapApplied, walletCapApplied }
PUSH CurrencyBalanceChangedClientEvent to owner sessions
RETURN (200, CreditCurrencyResponse { transaction, newBalance, earnCapApplied, walletCapApplied })
```

### DebitCurrency
POST /currency/debit | Roles: [developer]

```
READ idempotency:{idempotencyKey}                  -> 409 if exists
READ wallets:wallet:{walletId}                     -> 404 if null, 400 if frozen
READ definitions:def:{currencyDefinitionId}        -> 404 if null
LOCK balance:{walletId}:{currencyDefId}            -> 409 if fails
  // GetOrCreateBalanceAsync
  IF !allowNegative
    // GetTotalHeldAmountAsync
    IF effectiveBalance < amount                   -> 400 (insufficient funds)
  // balance.Amount -= amount
  WRITE balances:bal:{walletId}:{currencyDefId}    <- updated balance
  WRITE balance-cache:bal:{walletId}:{currencyDefId}
  // RecordTransactionAsync (same pattern as CreditCurrency)
  WRITE idempotency:{idempotencyKey}               <- transactionId (TTL)
PUBLISH currency.debited { transactionId, walletId, ownerId, amount, transactionType, newBalance }
PUSH CurrencyBalanceChangedClientEvent to owner sessions (negative delta)
RETURN (200, DebitCurrencyResponse { transaction, newBalance })
```

### TransferCurrency
POST /currency/transfer | Roles: [developer]

```
READ idempotency:{idempotencyKey}                  -> 409 if exists
READ wallets:wallet:{sourceWalletId}               -> 404 if null, 400 if frozen
READ wallets:wallet:{targetWalletId}               -> 404 if null, 400 if frozen
READ definitions:def:{currencyDefinitionId}        -> 404 if null, 400 if not transferable
// Deterministic lock ordering: sort keys lexicographically to prevent deadlock
LOCK balance:{firstLockKey}                        -> 409 if fails
  LOCK balance:{secondLockKey}                     -> 409 if fails
    // GetOrCreateBalanceAsync (source)
    // GetTotalHeldAmountAsync (source)
    IF effectiveBalance < amount                   -> 400
    // GetOrCreateBalanceAsync (target)
    IF perWalletCap AND Reject AND overflow        -> 400
    IF perWalletCap AND CapAndLose AND overflow
      // Truncate creditAmount
      PUBLISH currency.wallet-cap.reached { ... }
    // Update both balances
    WRITE balances:bal:{sourceWalletId}:{currencyDefId}
    WRITE balance-cache:bal:{sourceWalletId}:{currencyDefId}
    WRITE balances:bal:{targetWalletId}:{currencyDefId}
    WRITE balance-cache:bal:{targetWalletId}:{currencyDefId}
    // RecordTransactionAsync (single tx with source+target snapshots)
    WRITE idempotency:{idempotencyKey}             <- transactionId (TTL)
PUBLISH currency.transferred { transactionId, sourceWalletId, targetWalletId, amount }
PUSH CurrencyBalanceChangedClientEvent to source owner (negative delta)
PUSH CurrencyBalanceChangedClientEvent to target owner (positive delta)
RETURN (200, TransferCurrencyResponse { transaction, sourceNewBalance, targetNewBalance, targetCapApplied })
```

### BatchCreditCurrency
POST /currency/batch-credit | Roles: [developer]

```
READ idempotency:{idempotencyKey}                  -> 409 if batch already submitted
FOREACH operation (sequential, index i)
  READ idempotency:{idempotencyKey}:{i}
  IF already complete
    READ transactions:tx:{existingTxId}            // return prior result
  ELSE
    // Delegate to CreditCurrencyAsync with sub-key "{idempotencyKey}:{i}"
    // Each credit acquires its own locks/idempotency independently
WRITE idempotency:{idempotencyKey}                 <- new Guid (TTL)
RETURN (200, BatchCreditResponse { results })
// Individual failures reflected in results[i].success / results[i].error
```

### BatchDebitCurrency
POST /currency/batch-debit | Roles: [developer]

```
READ idempotency:{idempotencyKey}                  -> 409 if batch already submitted
FOREACH operation (sequential, index i)
  READ idempotency:{idempotencyKey}:{i}
  IF already complete
    READ transactions:tx:{existingTxId}
  ELSE
    // Delegate to DebitCurrencyAsync with sub-key "{idempotencyKey}:{i}"
WRITE idempotency:{idempotencyKey}                 <- new Guid (TTL)
RETURN (200, BatchDebitResponse { results })
```

### CalculateConversion
POST /currency/convert/calculate | Roles: [user]

```
READ definitions:def:{fromCurrencyId}              -> 404 if null
READ definitions:def:{toCurrencyId}                -> 404 if null
// CalculateEffectiveRate: fromRate / toRate (base currency rate = 1.0)
IF either lacks ExchangeRateToBase                 -> 400
// toAmount = fromAmount * effectiveRate, rounded to ConversionRoundingPrecision
RETURN (200, CalculateConversionResponse { toAmount, effectiveRate, baseCurrency, conversionPath })
```

### ExecuteConversion
POST /currency/convert/execute | Roles: [developer]

```
READ idempotency:{idempotencyKey}                  -> 409 if exists
READ wallets:wallet:{walletId}                     -> 404 if null, 400 if frozen
READ definitions:def:{fromCurrencyId}              -> 404 if null
READ definitions:def:{toCurrencyId}                -> 404 if null
// CalculateEffectiveRate                          -> 400 if missing rates
IF toCurrency has PerWalletCap with Reject
  // Pre-validate: GetOrCreateBalanceAsync for target currency
  IF newBalance would exceed cap                   -> 400
// Saga: debit source, credit target, compensate on failure
// Step 1: Debit source
// Delegates to DebitCurrencyAsync("{idempotencyKey}:debit", Conversion_debit)
IF debit fails                                     -> forward error status
// Step 2: Credit target
// Delegates to CreditCurrencyAsync("{idempotencyKey}:credit", Conversion_credit, BypassEarnCap)
IF credit fails
  // Compensate: CreditCurrencyAsync("{idempotencyKey}:compensate", BypassEarnCap)
  -> forward credit error status
WRITE idempotency:{idempotencyKey}                 <- debitTransactionId (TTL)
RETURN (200, ExecuteConversionResponse { debitTransaction, creditTransaction, effectiveRate })
```

### GetExchangeRate
POST /currency/exchange-rate/get | Roles: [user]

```
READ definitions:def:{fromCurrencyId}              -> 404 if null
READ definitions:def:{toCurrencyId}                -> 404 if null
// CalculateEffectiveRate                          -> 400 if missing rates
RETURN (200, GetExchangeRateResponse { rate, inverseRate, baseCurrency, fromRateToBase, toRateToBase })
```

### UpdateExchangeRate
POST /currency/exchange-rate/update | Roles: [admin]

```
// Retry loop up to ExchangeRateUpdateMaxRetries
FOREACH attempt
  READ definitions:def:{currencyDefinitionId} [with ETag]  -> 404 if null
  IF isBaseCurrency                                -> 400
  // Set ExchangeRateToBase, ExchangeRateUpdatedAt
  ETAG-WRITE definitions:def:{currencyDefinitionId}
  IF ETag mismatch -> retry
  // On success:
  // FindBaseCurrencyCodeAsync
  READ definitions:base-currency:{scope}
  PUBLISH currency.exchange-rate.updated { currencyDefinitionId, currencyCode, previousRate, newRate, baseCurrencyCode }
  RETURN (200, UpdateExchangeRateResponse { definition, previousRate })
// All retries exhausted                           -> 409
```

### GetTransaction
POST /currency/transaction/get | Roles: [developer]

```
READ transactions:tx:{transactionId}               -> 404 if null
RETURN (200, TransactionResponse)
```

### GetTransactionHistory
POST /currency/transaction/history | Roles: [user]

```
READ wallets:wallet:{walletId}                     -> 404 if null
READ transactions:tx-wallet:{walletId}             // JSON list of txIds
// BulkGetAsync for all transaction keys
// Filter in-memory: currencyDefinitionId, transactionTypes, date range
// effectiveFromDate = max(fromDate, now - TransactionRetentionDays)
// Reverse chronological order, then Skip(offset).Take(limit)
RETURN (200, GetTransactionHistoryResponse { transactions, totalCount })
```

### GetTransactionsByReference
POST /currency/transaction/by-reference | Roles: [developer]

```
READ transactions:tx-ref:{referenceType}:{referenceId}  // JSON list of txIds
// BulkGetAsync for all transaction keys
RETURN (200, GetTransactionsByReferenceResponse { transactions })
```

### GetGlobalSupply
POST /currency/stats/global-supply | Roles: [user]

```
// STUB: returns all zeros
READ definitions:def:{currencyDefinitionId}        -> 404 if null
RETURN (200, GetGlobalSupplyResponse { totalSupply: 0, inCirculation: 0, inEscrow: 0, totalMinted: 0, totalBurned: 0, supplyCap })
```

### GetWalletDistribution
POST /currency/stats/wallet-distribution | Roles: [admin]

```
// STUB: returns all zeros
READ definitions:def:{currencyDefinitionId}        -> 404 if null
RETURN (200, GetWalletDistributionResponse { all fields: 0 })
```

### EscrowDeposit
POST /currency/escrow/deposit | Roles: [developer]

```
// Thin wrapper: delegates to DebitCurrencyAsync
// TransactionType = EscrowDeposit, referenceType = "escrow", referenceId = escrowId
RETURN (debitStatus, EscrowDepositResponse { transaction, newBalance })
```

### EscrowRelease
POST /currency/escrow/release | Roles: [developer]

```
// Thin wrapper: delegates to CreditCurrencyAsync
// TransactionType = EscrowRelease, referenceType = "escrow", referenceId = escrowId, BypassEarnCap
RETURN (creditStatus, EscrowReleaseResponse { transaction, newBalance })
```

### EscrowRefund
POST /currency/escrow/refund | Roles: [developer]

```
// Thin wrapper: delegates to CreditCurrencyAsync
// TransactionType = EscrowRefund, referenceType = "escrow", referenceId = escrowId, BypassEarnCap
RETURN (creditStatus, EscrowRefundResponse { transaction, newBalance })
```

### CreateHold
POST /currency/hold/create | Roles: [developer]

```
READ idempotency:{idempotencyKey}                  -> 409 if exists
READ wallets:wallet:{walletId}                     -> 404 if null
READ definitions:def:{currencyDefinitionId}        -> 404 if null
LOCK balance:{walletId}:{currencyDefId}            -> 409 if fails
  // GetOrCreateBalanceAsync
  // GetTotalHeldAmountAsync (reads hold index + holds, with Redis cache)
  // effectiveBalance = amount - totalHeld
  IF effectiveBalance < holdAmount                 -> 400
  // Clamp expiresAt to now + HoldMaxDurationDays
  WRITE holds:hold:{holdId}                        <- new HoldModel { Status = Active }
  WRITE holds-cache:hold:{holdId}                  // Redis TTL cache
  LOCK index:hold-wallet:{walletId}:{currencyDefId}
    // AddToListAsync
  WRITE idempotency:{idempotencyKey}               <- holdId (TTL)
PUBLISH currency.hold.created { holdId, walletId, ownerId, currencyDefinitionId, amount, expiresAt }
RETURN (200, HoldResponse)
```

### CaptureHold
POST /currency/hold/capture | Roles: [developer]

```
READ idempotency:{idempotencyKey}                  -> 409 if exists
LOCK hold:{holdId}                                 -> 409 if fails
  READ holds:hold:{holdId} [with ETag]             -> 404 if null
  IF status != Active                              -> 400
  IF captureAmount > holdAmount                    -> 400
  // Debit captured amount: delegates to DebitCurrencyAsync("{idempotencyKey}:hold-capture", Fee)
  IF debit fails                                   -> forward error status
  // Set hold status = Captured, capturedAmount, completedAt
  ETAG-WRITE holds:hold:{holdId}                   -> 409 if ETag mismatch
  WRITE holds-cache:hold:{holdId}
  LOCK index:hold-wallet:{walletId}:{currencyDefId}
    // RemoveFromListAsync (prune completed hold from active index)
  WRITE idempotency:{idempotencyKey}               <- transactionId (TTL)
  READ wallets:wallet:{walletId}                   // for ownerId in event
  PUBLISH currency.hold.captured { holdId, walletId, ownerId, holdAmount, capturedAmount, amountReleased, transactionId }
RETURN (200, CaptureHoldResponse { hold, transaction, newBalance, amountReleased })
```

### ReleaseHold
POST /currency/hold/release | Roles: [developer]

```
LOCK hold:{holdId}                                 -> 409 if fails
  READ holds:hold:{holdId} [with ETag]             -> 404 if null
  IF status != Active                              -> 400
  // Set hold status = Released, completedAt
  ETAG-WRITE holds:hold:{holdId}                   -> 409 if ETag mismatch
  WRITE holds-cache:hold:{holdId}
  LOCK index:hold-wallet:{walletId}:{currencyDefId}
    // RemoveFromListAsync
  READ wallets:wallet:{walletId}                   // for ownerId in event
  PUBLISH currency.hold.released { holdId, walletId, ownerId, currencyDefinitionId, amount }
RETURN (200, HoldResponse)
// No balance modification — funds simply become available again
```

### GetHold
POST /currency/hold/get | Roles: [developer]

```
READ holds-cache:hold:{holdId}                     // Redis cache check
IF cache hit
  RETURN (200, HoldResponse)
READ holds:hold:{holdId}                           -> 404 if null
WRITE holds-cache:hold:{holdId}                    // Populate cache on miss
RETURN (200, HoldResponse)
```

---

## Background Services

### CurrencyAutogainTaskService
**Interval**: `config.AutogainTaskIntervalMs` (default 60000ms)
**Startup Delay**: `config.AutogainTaskStartupDelaySeconds` (default 15s)
**Active when**: `config.AutogainProcessingMode == Task`

```
// Periodic loop
READ definitions:all-defs                          // all definition IDs
FOREACH definition where autogainEnabled
  READ balances:bal-currency:{currencyDefId}       // reverse index: all walletIds
  FOREACH walletId in batches of AutogainBatchSize
    LOCK balance:{walletId}:{currencyDefId}        // skip if unavailable
      READ balances:bal:{walletId}:{currencyDefId}
      // Calculate periods elapsed since LastAutogainAt
      // Simple: gain = periods * autogainAmount
      // Compound: gain = balance * ((1 + rate)^periods - 1)
      // Cap enforcement: clamp to autogainCap
      IF gain > 0
        WRITE balances:bal:{walletId}:{currencyDefId}
        WRITE balance-cache:bal:{walletId}:{currencyDefId}
        PUBLISH currency.autogain.calculated { walletId, periodsApplied, amountGained, newBalance }
        PUSH CurrencyBalanceChangedClientEvent to owner sessions
```
