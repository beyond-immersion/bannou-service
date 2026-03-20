# Currency Plugin Deep Dive

> **Plugin**: lib-currency
> **Schema**: schemas/currency-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: currency-definitions (MySQL), currency-wallets (MySQL), currency-balances (MySQL), currency-transactions (MySQL), currency-holds (MySQL), currency-balance-cache (Redis), currency-holds-cache (Redis), currency-idempotency (Redis)
> **Implementation Map**: [docs/maps/CURRENCY.md](../maps/CURRENCY.md)
> **Short**: Multi-currency economy (wallets, transfers, exchange rates, holds, escrow integration)

---

## Overview

Multi-currency management service (L2 GameFoundation) for game economies. Handles currency definitions with scope/realm restrictions, wallet lifecycle management, balance operations (credit/debit/transfer with idempotency-key deduplication), authorization holds (reserve/capture/release), currency conversion via exchange-rate-to-base pivot, and escrow integration (deposit/release/refund endpoints consumed by lib-escrow). Features three background workers: autogain for passive income, currency expiration for removing expired balances, and hold expiration for auto-releasing stale authorization holds. Transaction history has configurable retention. All mutating balance operations use distributed locks for multi-instance safety.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for definitions, wallets, balances, transactions, holds; Redis caching for balances and holds; Redis idempotency store |
| lib-state (`IDistributedLockProvider`) | Balance-level locks for atomic credit/debit/transfer/hold-creation; hold-level locks for capture serialization; wallet-level locks for close operations; index-level locks for list operations; autogain locks to prevent concurrent modification |
| lib-messaging (`IMessageBus`) | Publishing all currency events (balance, wallet lifecycle, autogain, cap, hold, exchange rate); error event publishing via TryPublishErrorAsync |
| lib-worldstate (`IWorldstateClient`, L2, **required future migration**) | Autogain worker MUST transition from real-time intervals to game-time via Worldstate's `GetElapsedGameTime` API. At the default 24:1 time ratio, real-time autogain dramatically under-credits compared to game-time. This affects the living economy: NPC passive income must track the simulated world's time, not server time. Migration requires adding an `AutogainTimeSource` config property (enum: `RealTime`, `GameTime`; default `GameTime` once Worldstate is implemented). Tracked in [#545](https://github.com/beyond-immersion/bannou-service/issues/545) (consolidated Currency+Seed game-time migration). |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-escrow | References currency as an AssetType; stores CurrencyDefinitionId, CurrencyCode, CurrencyAmount on escrow asset models for currency custody |
| lib-contract | Registers `currency_transfer` and `fee` clause types that route to `/currency/transfer` and `/currency/balance/get` endpoints via mesh invocation |
| lib-quest | Hard dependency via `ICurrencyClient` for built-in `currency` prerequisite checking |
| lib-license | Hard dependency via `ICurrencyClient` for cost verification on license board operations |
| lib-actor (planned) | `${currency.*}` variable provider via `IVariableProviderFactory` for NPC economic awareness in ABML behavior expressions ([#147](https://github.com/beyond-immersion/bannou-service/issues/147), in progress) |
| lib-actor (planned) | ABML economic action handlers (`economy_credit`, `economy_debit`, `economy_transfer`) calling `ICurrencyClient` for NPC economic actions ([#428](https://github.com/beyond-immersion/bannou-service/issues/428)) |
| lib-genesis (L2) | Implements `ICurrencyTransactionListener` to convert wallet credits into Seed growth via template-defined growth mappings, driving the entity awakening lifecycle. Genesis is the primary consumer of the listener interface — it filters to genesis-managed wallets via an in-memory `ConcurrentDictionary` (~microseconds, no network I/O) and buffers matched credits for a periodic growth flush worker that calls `Seed.RecordGrowthBatch` per entity. At 100K+ wallets, the listener adds negligible overhead to Currency's mutation paths. |

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `ownerType` | A (Entity Reference) | `EntityType` enum | All valid values are first-class Bannou entities (characters, accounts, guilds, etc.). Recently migrated from removed `WalletOwnerType` to shared EntityType enum. |
| `currencyCode` | B (Content Code) | Opaque string | Game-configurable currency identifier, unique per game. New currencies registered via API without schema changes (e.g., `gold`, `silver`, `divine_favor`, `dungeon_mana`). |
| `referenceType` | B (Content Code) | Opaque string | Caller-supplied classification of what triggered a transaction (e.g., `escrow`, `quest`, `trade`). Open-ended for extensibility across consuming services. |
| `scope` | C (System State) | `CurrencyScope` enum | Finite system-owned scope modes: `global`, `realm_specific`, `multi_realm`. Determines currency availability across realms. |
| `precision` | C (System State) | `CurrencyPrecision` enum | Finite system-owned decimal precision modes: `integer`, `decimal_2`, `decimal_4`, `decimal_8`, `decimal_full`. Immutable after creation. |
| `status` (wallet) | C (System State) | `WalletStatus` enum | Finite wallet lifecycle states: `active`, `frozen`, `closed`. System-owned transitions. |
| `transactionType` | C (System State) | `TransactionType` enum | Finite faucet/sink classification for currency flow tracking: `mint`, `quest_reward`, `loot_drop`, `vendor_sale`, `trade`, `gift`, `fee`, `tax`, `escrow_deposit`, `escrow_release`, `escrow_refund`, `conversion_debit`, `conversion_credit`, `admin_adjustment`, `system`, `other`. |
| `autogainMode` | C (System State) | `AutogainMode` enum | Finite calculation modes: `simple`, `compound`. System-owned operational modes. |
| `capOverflowBehavior` | C (System State) | `CapOverflowBehavior` enum | Finite overflow handling modes: `reject`, `cap_and_lose`, `cap_and_return`. |
| `expirationPolicy` | C (System State) | `ExpirationPolicy` enum | Finite expiration modes: `fixed_date`, `duration_from_earn`, `end_of_season`. |
| `capType` (EarnCapReachedEvent) | C (System State) | `EarnCapType` enum | Binary cap period: `daily`, `weekly`. |

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
| `currency.earn-cap.reached` | `CurrencyEarnCapReachedEvent` | Credit limited by daily/weekly earn cap |
| `currency.wallet-cap.reached` | `CurrencyWalletCapReachedEvent` | Credit hit wallet cap with cap_and_lose behavior |
| `currency.expired` | `CurrencyExpiredEvent` | Currency expired; balance zeroed by the currency expiration background worker |
| `currency.exchange-rate.updated` | `CurrencyExchangeRateUpdatedEvent` | Exchange rate changed |
| `currency.definition.created` | `CurrencyDefinitionCreatedEvent` | New currency definition created |
| `currency.definition.updated` | `CurrencyDefinitionUpdatedEvent` | Currency definition updated |
| `currency.wallet.created` | `CurrencyWalletCreatedEvent` | New wallet created |
| `currency.wallet.frozen` | `CurrencyWalletFrozenEvent` | Wallet frozen |
| `currency.wallet.unfrozen` | `CurrencyWalletUnfrozenEvent` | Wallet unfrozen |
| `currency.wallet.closed` | `CurrencyWalletClosedEvent` | Wallet permanently closed |
| `currency.hold.created` | `CurrencyHoldCreatedEvent` | Authorization hold created |
| `currency.hold.captured` | `CurrencyHoldCapturedEvent` | Hold captured (funds debited) |
| `currency.hold.released` | `CurrencyHoldReleasedEvent` | Hold released (funds available again) |
| `currency.hold.expired` | `CurrencyHoldExpiredEvent` | Hold auto-released by the hold expiration background worker when ExpiresAt is passed |
| `currency.balance.created` | `CurrencyBalanceCreatedEvent` | First-ever credit of a currency to a wallet creates a new balance record |
| `currency.balance.updated` | `CurrencyBalanceUpdatedEvent` | Balance record modified (credit, debit, transfer) |
| `currency.balance.deleted` | `CurrencyBalanceDeletedEvent` | Balance record permanently removed (account deletion cleanup, clean-deprecated sweep) |

### Consumed Events

| Topic | Source | Handler | Purpose |
|-------|--------|---------|---------|
| `account.deleted` | lib-account (L1) | `HandleAccountDeletedAsync` | CASCADE-delete all account-owned wallets with balances, holds, transactions, and indexes. Per FOUNDATION TENETS (Account Deletion Cleanup Obligation). |

### Published Client Events

| Event Name | Event Type | Trigger |
|------------|-----------|---------|
| `currency.balance_changed` | `CurrencyBalanceChangedEvent` | Balance mutation via credit, debit, transfer (both wallets), or autogain; includes signed delta, new balance, and transaction type discriminator |
| `currency.wallet_frozen` | `CurrencyWalletFrozenEvent` | Wallet frozen (escrow dispute, admin action); client should display frozen indicator and disable balance-mutating actions |
| `currency.wallet_unfrozen` | `CurrencyWalletUnfrozenEvent` | Wallet unfrozen; client should remove frozen indicator and re-enable balance-mutating actions |

Client events are published via `IEntitySessionRegistry.PublishToEntitySessionsAsync` using the wallet's `ownerId` with entity type `"currency"` for entity-session resolution. If zero sessions are registered for the owner, zero events are delivered (graceful degradation). Schema: `schemas/currency-client-events.yaml`.

**Coverage via delegation**: Batch credit/debit, escrow deposit/release/refund, currency conversion, wallet close transfers, and hold capture all delegate to `CreditCurrencyAsync`/`DebitCurrencyAsync`, so client events are automatically published for all these operations without separate publish points.

**Transfer produces two events**: `TransferCurrencyAsync` publishes a negative-delta event to the source wallet owner and a positive-delta event to the target wallet owner. The target's `amount` may be less than the source's absolute `amount` if a wallet cap truncated the credit.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultAllowNegative` | `CURRENCY_DEFAULT_ALLOW_NEGATIVE` | `false` | Default for currencies that do not specify allowNegative |
| `AutogainProcessingMode` | `CURRENCY_AUTOGAIN_PROCESSING_MODE` | `Lazy` | How autogain is calculated (enum: Lazy = on-demand at query, Task = background worker) |
| `AutogainTaskStartupDelaySeconds` | `CURRENCY_AUTOGAIN_TASK_STARTUP_DELAY_SECONDS` | `15` | Delay before first autogain background cycle |
| `AutogainTaskIntervalMs` | `CURRENCY_AUTOGAIN_TASK_INTERVAL_MS` | `60000` | Background task processing interval (1 minute) |
| `AutogainBatchSize` | `CURRENCY_AUTOGAIN_BATCH_SIZE` | `1000` | Wallets processed per batch in task mode |
| `CurrencyExpirationTaskStartupDelaySeconds` | `CURRENCY_EXPIRATION_TASK_STARTUP_DELAY_SECONDS` | `30` | Delay before first currency expiration background cycle |
| `CurrencyExpirationTaskIntervalMs` | `CURRENCY_EXPIRATION_TASK_INTERVAL_MS` | `3600000` | Currency expiration task processing interval (1 hour) |
| `CurrencyExpirationBatchSize` | `CURRENCY_EXPIRATION_BATCH_SIZE` | `500` | Balances processed per batch in the currency expiration task |
| `HoldExpirationTaskStartupDelaySeconds` | `CURRENCY_HOLD_EXPIRATION_TASK_STARTUP_DELAY_SECONDS` | `30` | Delay before first hold expiration background cycle |
| `HoldExpirationTaskIntervalMs` | `CURRENCY_HOLD_EXPIRATION_TASK_INTERVAL_MS` | `300000` | Hold expiration task processing interval (5 minutes) |
| `HoldExpirationBatchSize` | `CURRENCY_HOLD_EXPIRATION_BATCH_SIZE` | `500` | Holds processed per batch in the hold expiration task |
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
| `CurrencyServiceConfiguration` | Singleton | All 23 config properties (see Configuration section) |
| `IStateStoreFactory` | Singleton | Constructor-cached into 13 typed store fields (5 typed model stores + 6 string index stores + 1 idempotency store + 1 balance cache + 1 hold cache) per tenets |
| `IDistributedLockProvider` | Singleton | Balance locks (`currency-balance`), hold locks (`currency-hold`), wallet locks (`currency-wallet`), index locks (`currency-index`), autogain locks (`currency-autogain`) |
| `ITelemetryProvider` | Singleton | Telemetry span instrumentation for all async helper methods |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEntitySessionRegistry` | Singleton | Client event publishing to wallet owner WebSocket sessions |
| `ICurrencyDataCache` | Singleton | In-memory cache for ABML variable provider |
| `IEnumerable<ICurrencyTransactionListener>` | Singleton | DI-discovered listeners notified after wallet credit/debit mutations. Interface defined in `bannou-service/Providers/ICurrencyTransactionListener.cs`. Dispatched after successful balance changes (credit, debit, transfer, autogain, escrow operations). Fires for ALL wallet mutations — consumers are responsible for fast filtering to their wallets of interest (Genesis uses an in-memory ConcurrentDictionary for O(1) filtering). Listeners must write to distributed state only (per SERVICE-HIERARCHY DI Listener safety rules). Primary consumer: lib-genesis (buffers matched credits, flushes periodically to Seed growth via template-defined mappings). |
| `CurrencyAutogainTaskService` | Hosted (Singleton) | Background worker for proactive autogain |
| `CurrencyExpirationTaskService` | Hosted (Singleton) | Background worker that scans and expires balances with elapsed expiration policies |
| `HoldExpirationTaskService` | Hosted (Singleton) | Background worker that auto-releases authorization holds past their ExpiresAt timestamp |

Service lifetime is **Scoped** (per-request). All state store references are constructor-cached per tenets (factory is used in constructor only, not stored as a field). Background service is a hosted singleton.

---

## API Endpoints (Implementation Notes)

### Currency Definition Operations (4 endpoints)

- **CreateCurrencyDefinition** (`/currency/definition/create`): Validates code uniqueness via `def-code:` index. If `isBaseCurrency=true`, iterates all definitions to ensure only one base per scope. Generates UUID, saves model with all autogain/expiration/linkage/exchange-rate fields. Creates code-to-ID index. Adds to `all-defs` list. Publishes `currency.definition.created`.
- **GetCurrencyDefinition** (`/currency/definition/get`): Resolves by ID (direct lookup) or code (via `def-code:` index). Returns full definition response with all fields.
- **ListCurrencyDefinitions** (`/currency/definition/list`): Loads all definition IDs from `all-defs`. Iterates and loads each. Filters by `scope`, `isBaseCurrency`, `realmId` (global currencies always pass realm filter), and `includeDeprecated` (default: `false`). No pagination - returns all matching definitions.
- **UpdateCurrencyDefinition** (`/currency/definition/update`): Loads by ID. Applies partial updates to mutable fields (name, description, transferable, tradeable, allowNegative, caps, autogain settings, exchange rate, icon, displayFormat). Exchange rate update also sets `ExchangeRateUpdatedAt`. Publishes `currency.definition.updated`.
- **DeprecateCurrencyDefinition** (`/currency/definition/deprecate`): Sets `IsDeprecated=true`, `DeprecatedAt=now`, `DeprecationReason` from optional `reason` parameter. Idempotent — returns OK when already deprecated. Publishes `currency.definition.updated` with `changedFields` containing deprecation fields. No dedicated deprecation event per tenets.

### Wallet Operations (6 endpoints)

- **CreateWallet** (`/currency/wallet/create`): Builds owner key from `ownerId:ownerType[:realmId]`. Checks uniqueness via owner index. Creates wallet with Active status. Saves wallet and owner-to-wallet index. Publishes `currency.wallet.created`. Returns Conflict if owner already has wallet.
- **GetWallet** (`/currency/wallet/get`): Resolves by walletId (direct) or by ownerId+ownerType+realmId (via owner index). Loads all balance summaries for the wallet including locked amounts from active holds. Returns wallet with balances.
- **GetOrCreateWallet** (`/currency/wallet/get-or-create`): Attempts owner index lookup first. If found, returns existing wallet with balances and `created=false`. If not found, delegates to CreateWallet and returns with `created=true` and empty balances.
- **FreezeWallet** (`/currency/wallet/freeze`): Uses ETag-based optimistic concurrency (`GetWithETagAsync` + `TrySaveAsync`). Sets status to Frozen with reason and timestamp. Returns Conflict if already frozen or on concurrent modification. Publishes `currency.wallet.frozen`.
- **UnfreezeWallet** (`/currency/wallet/unfreeze`): Uses ETag-based optimistic concurrency. Requires current status is Frozen (returns BadRequest otherwise). Clears frozen fields, sets status to Active. Publishes `currency.wallet.unfrozen`.
- **CloseWallet** (`/currency/wallet/close`): Requires a `transferRemainingTo` destination wallet. Loads all balances for closing wallet. Credits each positive balance to destination wallet via `InternalCreditAsync` (bypasses earn caps). Sets wallet status to Closed. Publishes `currency.wallet.closed`. Returns transferred balance details.

### Balance Operations (7 endpoints)

- **GetBalance** (`/currency/balance/get`): Validates wallet and definition exist. Gets or creates balance record. Applies lazy autogain if currency is autogain-enabled (acquires `currency-autogain` lock on `{walletId}:{currencyDefId}`, skips if lock unavailable). Resets daily/weekly earn caps if periods elapsed. Calculates locked amount from active holds. Returns amount, lockedAmount, effectiveAmount, earnCapInfo, and autogainInfo.
- **BatchGetBalances** (`/currency/balance/batch-get`): Iterates query list. For each, gets balance, applies lazy autogain, calculates locked amounts. Returns list of balance results. No locking between items - individual consistency per balance.
- **CreditCurrency** (`/currency/credit`): Validates amount > 0. Checks idempotency. Validates wallet exists and is not frozen (returns 422 if frozen). Acquires distributed lock on `walletId:currencyDefId`. Resets earn caps. Enforces daily/weekly earn caps (publishes `earn-cap.reached` when limited). Enforces per-wallet cap with configurable overflow behavior (reject returns 422, cap_and_lose truncates and publishes `wallet-cap.reached`). Updates balance and earn-tracking counters. Records transaction. Records idempotency key. Publishes `currency.credited`. Returns transaction record and cap-applied info.
- **DebitCurrency** (`/currency/debit`): Validates amount > 0. Checks idempotency. Validates wallet not frozen. Acquires distributed lock. Checks sufficient funds (negative allowed if definition or transaction-level override permits). Debits balance. Records transaction. Publishes `currency.debited`. Returns 422 for insufficient funds.
- **TransferCurrency** (`/currency/transfer`): Validates both wallets exist and are not frozen. Validates currency is transferable. Acquires two distributed locks in deterministic order (string comparison of lock keys) to prevent deadlock. Checks source has sufficient funds. Applies wallet cap on target (reject or cap_and_lose). Updates both balances. Records single transaction with both source/target balance snapshots. Publishes `currency.transferred`.
- **BatchCreditCurrency** (`/currency/batch-credit`): Checks batch-level idempotency. Iterates operations sequentially, delegating each to CreditCurrencyAsync with sub-key `{batchKey}:{index}`. Collects per-operation success/failure results. Records batch-level idempotency key. Returns partial success results.
- **BatchDebitCurrency** (`/currency/batch-debit`): Checks batch-level idempotency. Iterates operations sequentially, delegating each to DebitCurrencyAsync with sub-key `{batchKey}:{index}`. Each operation supports `allowNegative` override and metadata. Collects per-operation success/failure results. Records batch-level idempotency key. Returns partial success results. Symmetric to BatchCredit.

### Conversion Operations (4 endpoints)

- **CalculateConversion** (`/currency/convert/calculate`): Looks up both currency definitions. Calculates effective rate via base-currency pivot: `fromRate / toRate` where each rate is `ExchangeRateToBase` (1.0 for base currency itself). Returns calculated amount rounded to 8 decimal places, effective rate, and two-step conversion path. Returns 422 if exchange rates are missing.
- **ExecuteConversion** (`/currency/convert/execute`): Checks idempotency. Validates wallet not frozen. Calculates rate. Pre-validates target currency wallet cap (if `PerWalletCap` set with `CapOverflowBehavior.Reject`, checks whether `toAmount` would exceed cap before debiting source). Debits source currency (transaction type `Conversion_debit`). Credits target currency (transaction type `Conversion_credit`) with `BypassEarnCap=true` (conversions are exchanges, not earnings). If credit fails after successful debit, issues a compensating credit to the source currency (idempotency key `{key}:compensate`, `BypassEarnCap=true`) to reverse the debit. Uses sub-keys for idempotency on debit/credit legs. Returns both transaction records and effective rate.
- **GetExchangeRate** (`/currency/exchange-rate/get`): Looks up both definitions. Calculates effective rate and inverse rate. Returns both rates plus individual rates-to-base. Returns 422 if rates undefined.
- **UpdateExchangeRate** (`/currency/exchange-rate/update`): Validates currency is not the base currency (returns BadRequest). Updates `ExchangeRateToBase` and `ExchangeRateUpdatedAt`. Publishes `currency.exchange-rate.updated` with previous and new rates. Returns updated definition.

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
 [Frozen] <--- All credit/debit/transfer operations return 422
 |
 UnfreezeWallet()
 |
 v
 [Active]
 |
 CloseWallet(transferRemainingTo)
 |
 +--- For each positive balance:
 | InternalCreditAsync(destination, amount, bypass_earn_cap)
 |
 v
 [Closed] <--- Terminal state, no further operations


Transaction Flow (Credit with Cap Enforcement)
=================================================

 CreditCurrencyAsync(walletId, currencyDefId, amount, idempotencyKey)
 |
 +--- Check idempotency key (Redis, TTL=3600s)
 | +-- Duplicate? -> return Conflict
 |
 +--- Validate wallet exists & not frozen
 +--- Validate currency definition exists
 |
 +--- Acquire distributed lock: "currency-balance:{walletId}:{currencyDefId}"
 | +-- Timeout 30s -> return Conflict
 |
 +--- Reset earn caps if period elapsed (daily/weekly)
 |
 +--- Earn Cap Check (if !bypassEarnCap):
 | +-- DailyEarnCap: remaining = cap - dailyEarned
 | +-- WeeklyEarnCap: remaining = cap - weeklyEarned
 | +-- Limited? -> publish earn-cap.reached, reduce amount
 | +-- Amount=0 after cap? -> return 422
 |
 +--- Wallet Cap Check (if perWalletCap set):
 | +-- newBalance > cap?
 | +-- Behavior=Reject -> return 422
 | +-- Behavior=CapAndLose -> truncate, publish wallet-cap.reached
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
 | sourceLockKey = "{sourceWalletId}:{currencyDefId}"
 | targetLockKey = "{targetWalletId}:{currencyDefId}"
 |
 +--- Sort keys lexicographically:
 | firstLockKey = min(source, target) <-- Deterministic order
 | secondLockKey = max(source, target) <-- prevents deadlock
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
 | | |
 CaptureHold ReleaseHold [Expired]
 | | |
 v v v
 Debit actual No balance Auto-release
 amount from change - via hold
 wallet funds free expiration
 | |
 v v
 status= status=
 Captured Released


Conversion System (Base Currency Pivot)
==========================================

 Currency A ---- ExchangeRateToBase=2.5 ----> [BASE]
 Currency B ---- ExchangeRateToBase=0.1 ----> [BASE]

 Convert 100 A -> B:
 effectiveRate = fromRate / toRate = 2.5 / 0.1 = 25.0
 toAmount = 100 * 25.0 = 2500 B

 ExecuteConversion:
 0. Pre-validate: target wallet cap on B (Reject behavior only)
 1. Debit 100 A (type=Conversion_debit)
 2. Credit 2500 B (type=Conversion_credit, BypassEarnCap=true)
 If credit fails: compensating credit 100 A (key="{key}:compensate")
 Both idempotent via sub-keys: "{key}:debit", "{key}:credit"


Autogain Background Task (CurrencyAutogainTaskService)
========================================================

 [Mode: "lazy"] [Mode: "task"]
 Autogain calculated on Background worker processes
 GetBalance/BatchGetBalances all eligible wallets proactively
 requests only |
 |
 +------- Loop (every AutogainTaskIntervalMs) ------+
 | |
 | 1. Load all-defs from state store |
 | 2. Filter: autogainEnabled=true |
 | 3. For each autogain currency: |
 | a. Load bal-currency:{defId} reverse index |
 | b. Process wallets in batches |
 | (AutogainBatchSize per batch) |
 | c. For each balance: |
 | - Acquire lock "currency-autogain" |
 | - Calculate periods elapsed |
 | - Apply gain (simple or compound) |
 | - Enforce autogain cap |
 | - Save balance |
 | - Publish autogain.calculated |
 | |
 +--------------------------------------------------+

 Autogain Modes:
 Simple: gain = periodsElapsed * autogainAmount
 Compound: gain = balance * ((1 + rate)^periods - 1)

 Cap enforcement:
 balance >= cap -> gain = 0
 balance + gain > cap -> gain = cap - balance


Escrow Integration Flow
=========================

 lib-escrow lib-currency
 | |
 |-- EscrowDeposit(amount) -------->|
 | (debit wallet for escrow) |-- DebitCurrency(Escrow_deposit)
 | | ref: "escrow", escrowId
 | |
 | [Escrow holds custody] |
 | |
 |-- EscrowRelease(amount) -------->|
 | (credit recipient) |-- CreditCurrency(Escrow_release)
 | | bypassEarnCap=true
 | OR |
 | |
 |-- EscrowRefund(amount) --------->|
 | (credit depositor back) |-- CreditCurrency(Escrow_refund)
 | | bypassEarnCap=true
```

---

## Stubs & Unimplemented Features

1. **Global supply analytics**: `GetGlobalSupply` returns all zeros. Design resolved (2026-03-20): two Redis atomic counters per currency (`supply:{defId}:net` for net circulation, `supply:{defId}:escrow` for escrow custody), maintained via `IRedisOperations` INCRBY/DECRBY on every credit/debit. Currency (L2) owns operational supply tracking — Analytics (L4, optional) handles game-design-dependent classifications like TotalMinted/TotalBurned. See [DIVINITY-GENERATION-ARCHITECTURE.md](../planning/DIVINITY-GENERATION-ARCHITECTURE.md) § "Analytics Is Wrong for Game Mechanics."
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/211 -->
2. **Wallet distribution analytics**: `GetWalletDistribution` returns all zeros. Design resolved (2026-03-20): background aggregation worker (`CurrencyDistributionAggregationWorker`) periodically scans balances per currency, computes percentiles and Gini coefficient, caches results in Redis. Serves from cache. Stale data acceptable for informational queries.
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/470 -->
3. **Global supply cap enforcement**: `GlobalSupplyCap` stored but never checked during credits. Design resolved (2026-03-20): check the `supply:{defId}:net` counter in `CreditCurrencyAsync` after earn cap and wallet cap checks. Atomic INCRBY returns new value — if exceeds cap, DECRBY to compensate and apply overflow behavior. Add `SupplyCapOverflowBehavior` field to definition schema (reuses `CapOverflowBehavior` enum: Reject / CapAndLose). Add `CurrencySupplyCapReachedEvent` to events schema.
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/471 -->
4. **Item linkage validation**: `LinkedToItem` and `LinkedItemTemplateId` are stored on definitions but `LinkedItemTemplateId` is not validated against lib-item at creation time. Design resolved (2026-03-20): add `IItemClient` dependency (L2→L2, valid), validate template existence in `CreateCurrencyDefinitionAsync` and `UpdateCurrencyDefinitionAsync` when `LinkedToItem=true`. No per-transaction enforcement — the linkage is a cross-entity reference, not a runtime gate. Display semantics (how the linkage is rendered) are game-specific client metadata. Removed `ItemLinkageMode` enum and `linkageMode` field from schema — display mode is not server infrastructure.
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/473 -->
5. **Transaction retention cleanup**: `TransactionRetentionDays` config exists but old transactions are only filtered at query time, never actually deleted. Transactions accumulate indefinitely. Design resolved (2026-03-20): `TransactionRetentionCleanupWorkerService` background worker using canonical polling loop pattern. Queries expired transactions via `IQueryableStateStore<TransactionModel>` with timestamp filter, deletes records and cleans wallet/reference indexes via `RemoveFromStringListAsync`. No archive — transactions are operational records, not entity identity/narrative data (T29). Config: `TransactionCleanupIntervalSeconds` (default 86400), `TransactionCleanupStartupDelaySeconds` (default 120), `TransactionCleanupBatchSize` (default 500). Last remaining item from #222.
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/222 -->

---

## Potential Extensions

1. **Universal value anchoring + location-scoped modifiers**: Design resolved (2026-03-20). Three-layer exchange rate architecture: (1) `UniversalValue` (double?, nullable) coexists with `ExchangeRateToBase` — UV ratio used when both currencies have UV, else existing pivot. (2) `ExchangeRateModifier` entity following the CalendarEvent structural pattern (scope: Realm/Location, time-bounded via `expiresAtGameTime`, source-tagged: Template/Divine/Admin/Faction, stackable multiplicatively). CRUD endpoints for modifiers, ABML action handler `economy_add_rate_modifier`. CalendarEvents are the temporal TRIGGER, modifiers are the economic EFFECT, god-actors connect them. (3) Effective rate = baseRate × product(activeModifiers). Modifiers cached in-memory (ConcurrentDictionary, event-invalidated). Buy/sell spread is NPC GOAP behavior, not Currency infrastructure. See [Economy System Guide §8](../guides/ECONOMY-SYSTEM.md#8-exchange-rate-extensions) and [#538](https://github.com/beyond-immersion/bannou-service/issues/538) (CalendarEvent pattern).
<!-- AUDIT:DESIGN_RESOLVED:2026-03-20:https://github.com/beyond-immersion/bannou-service/issues/478 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(No outstanding bugs.)*

### Intentional Quirks (Documented Behavior)

1. **Transfer debits full amount even when target cap truncates**: When the target wallet cap causes overflow with `cap_and_lose` behavior, the source is debited the full transfer amount but the target only receives the capped portion. The overflow amount is lost (burned) as a currency sink.

2. **First autogain access initializes without retroactive gain**: When a balance with autogain is first accessed (either lazy or task mode), `LastAutogainAt` is set to now without applying any retroactive gain. This prevents exploits from creating a balance and waiting before first access.

3. **Batch credit/debit is non-atomic**: Individual credit or debit operations in a batch can succeed or fail independently. A batch can return partial success (some items credited/debited, some failed). The batch-level idempotency key prevents replaying the entire batch. On retry of a partially-completed batch, already-completed sub-operations are detected via pre-check of sub-operation idempotency keys and reported as successful with the original transaction data (TOCTOU race during concurrent submissions possible but harmless — inner idempotency prevents double-processing).

4. **List indexes are read-modify-write under lock**: `AddToListAsync` acquires a per-key distributed lock, reads a JSON list from MySQL, appends, and saves. Lock contention is scoped to a single index key (e.g., `bal-currency:{defId}`). Index writes only occur on new balance creation (not on every credit/debit), so contention is limited to concurrent new-wallet-creation for the same currency. Lock timeout (`IndexLockTimeoutSeconds`, default 15s) and retries (`IndexLockMaxRetries`, default 3) are configurable. At very high scale (thousands of simultaneous new balances for one currency), this could bottleneck; migration to Redis sets or a dedicated index store would be the remedy.

5. **Balance cache has sub-millisecond stale window**: `SaveBalanceAsync` writes to MySQL then immediately updates Redis cache (write-through pattern). Between the MySQL write and Redis update, or if the Redis cache write silently fails (non-fatal), a concurrent reader could get the pre-mutation value. Cache TTL is 60 seconds (configurable via `BalanceCacheTtlSeconds`). For authoritative balance checks (e.g., pre-authorization), use the hold mechanism which bypasses cache and reads directly from MySQL under distributed lock.

6. **Read-only hold queries are eventually consistent**: All mutating balance operations (`CreateHoldAsync`, `CreditCurrencyAsync`, `DebitCurrencyAsync`, `TransferCurrencyAsync`) acquire the same `currency-balance:{walletId}:{currencyDefId}` distributed lock, so mutations are fully serialized — a hold created by one operation is guaranteed visible to the next mutation. However, read-only queries (`GetBalanceAsync`, `BatchGetBalancesAsync`) do NOT acquire the lock and may briefly see stale hold state if a `CreateHoldAsync` is mid-execution (hold saved to MySQL but index not yet updated). This window is sub-millisecond on a single node. For authoritative pre-authorization checks, use `CreateHoldAsync` itself (which reads under lock), not `GetBalanceAsync`.

7. **Conversion has transient cross-currency balance inconsistency**: `ExecuteConversionAsync` uses a two-step saga: debit source currency (under `currency-balance:{walletId}:{fromCurrencyId}` lock), then credit target currency (under `currency-balance:{walletId}:{toCurrencyId}` lock). Between the debit completing and the credit completing, a concurrent `GetBalance` would see the source reduced but the target not yet increased, making the wallet's total cross-currency value appear temporarily lower. This is NOT a double-spending risk — each individual balance operation is fully serialized under its own distributed lock, so no currency can be spent twice. The window is sub-millisecond (between one async call returning and the next starting). If the credit fails, a compensating credit with idempotency key `{key}:compensate` reverses the debit. This is inherent to the saga pattern without distributed transactions and is the correct architectural trade-off.

### Deprecation Lifecycle (Category B)

Currency definitions are **Category B entities** — balance records reference definitions by ID, and existing balances must continue to function after a definition is deprecated. The "instance" of a CurrencyDefinition is the CurrencyBalance record (per-wallet-per-currency binding), not the CurrencyWallet — wallets are currency-agnostic containers that hold balances of many currencies simultaneously.

- **Deprecation is one-way**: Once deprecated, a currency definition cannot be undeprecated. No undeprecate endpoint exists.
- **No delete endpoint**: Currency definitions persist forever. Deletion is via the clean-deprecated sweep only (when zero balances remain for this currency across all wallets).
- **Instance creation guard**: `CreditCurrencyAsync` checks `IsDeprecated` when `GetOrCreateBalanceAsync` returns `isNewBalance=true` — first-ever credit of a deprecated currency to a wallet is rejected with `BadRequest`. `ExecuteConversionAsync` inherits the guard via delegation to `CreditCurrencyAsync`. Modifications to *existing* balances (additional credits, debits, holds, conversions) are not instance creation and are not blocked by the tenet. A game design decision about whether to also restrict operations on existing balances of deprecated currencies (e.g., allow debits but block credits to drain the currency) is a separate concern from tenet compliance.
- **Instance entity**: The schema declares `instanceEntity: CurrencyBalance` — the per-wallet-per-currency balance record is the true "instance" of a currency definition. CurrencyBalance is an x-lifecycle entity with full lifecycle events (`currency.balance.created`, `currency.balance.updated`, `currency.balance.deleted`). The clean-deprecated sweep's `hasInstancesAsync` delegate checks the `bal-currency:{currencyDefId}` reverse index (which wallets have a balance of this currency).
- **Clean-deprecated sweep**: `CleanDeprecatedCurrencyDefinitionsAsync` uses `DeprecationCleanupHelper.ExecuteCleanupSweepAsync`. Sweeps deprecated definitions with zero remaining balances (checked via `bal-currency:{defId}` reverse index). Removes definition + code index + base currency index + all-defs entry. Publishes `currency.definition.deleted`. Uses shared `CleanDeprecatedRequest`/`CleanDeprecatedResponse`. Permissions: `role: admin`.
- **Storage model**: Currency definitions use triple-field deprecation: `IsDeprecated` (bool), `DeprecatedAt` (DateTimeOffset?), `DeprecationReason` (string?).
- **Idempotent deprecation**: Deprecating an already-deprecated definition returns `OK` (not `Conflict`).
- **List filtering**: `ListCurrencyDefinitions` includes `includeDeprecated` parameter (default: `false`).
- **Events**: Deprecation is communicated via `currency.definition.updated` with `changedFields` containing the deprecation fields (no dedicated deprecation event per tenets).

### Design Considerations (Requires Planning)

1. **Non-account entity deletion does not trigger wallet cleanup**: Account deletion path is implemented (2026-03-08) via `account.deleted` handler. Character/guild deletion still needs lib-resource integration (`x-references`) — balance disposition (transfer vs burn) and frozen wallet handling need design decisions. See [#556](https://github.com/beyond-immersion/bannou-service/issues/556).
<!-- AUDIT:NEEDS_DESIGN:2026-03-03:https://github.com/beyond-immersion/bannou-service/issues/556 -->

2. **Transaction retention only enforced at query time**: Transactions beyond `TransactionRetentionDays` are filtered out of history queries but remain in the MySQL store indefinitely. No background cleanup task exists to actually delete old transactions.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/222 -->

3. ~~**Autogain uses real-time, should use game-time**~~ ([#545](https://github.com/beyond-immersion/bannou-service/issues/545), resolved 2026-03-19): Design resolved. Add `AutogainTimeSource` config property (`$ref: TimeSource` from `common-api.yaml`, default: `GameTime`). Wallets with `realmId` (already in key pattern) use that realm's `GetElapsedGameTime`. Wallets without `realmId` (global/account wallets) fall back to real-time. Optional per-definition `autogainUseGameTime` nullable boolean override for fine-grained control. Both `Lazy` and `Task` modes use the same time source logic. Error handling: skip autogain for that wallet/cycle when Worldstate is unavailable.
<!-- AUDIT:RESOLVED:2026-03-19:https://github.com/beyond-immersion/bannou-service/issues/545 -->

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Pending Design

- [#545](https://github.com/beyond-immersion/bannou-service/issues/545) - Currency autogain must transition from real-time to game-time via Worldstate (blocked by Worldstate implementation). Consolidated issue covering both Currency and Seed background workers (supersedes #433). Design resolved (2026-03-19) — implementation blocked on Worldstate API.
- [#473](https://github.com/beyond-immersion/bannou-service/issues/473) - Item linkage: `ItemLinkageMode` enum and `linkageMode` field removed from schema (display semantics are client metadata per T29). `LinkedToItem` + `LinkedItemTemplateId` retained as server-validated cross-entity references. Remaining work: add `IItemClient` dependency, validate template existence at creation/update time.
- [#478](https://github.com/beyond-immersion/bannou-service/issues/478) - Universal value anchoring + location-scoped exchange rate modifiers. Three-layer architecture: (1) `UniversalValue` coexists with `ExchangeRateToBase` (UV used when both currencies have it, else existing pivot). (2) `ExchangeRateModifier` entity following the CalendarEvent (#538) structural pattern — scoped Realm/Location, time-bounded via `expiresAtGameTime`, source-tagged, stackable multiplicatively. CRUD endpoints + `economy_add_rate_modifier` ABML action. (3) Effective rate = baseRate × product(activeModifiers), cached in-memory. Buy/sell spread is NPC GOAP behavior, not infrastructure.
- [#478](https://github.com/beyond-immersion/bannou-service/issues/478) - Universal value anchoring + location-scoped modifiers. Design resolved (2026-03-20) — see Design Resolved section.
- [#556](https://github.com/beyond-immersion/bannou-service/issues/556) - Non-account entity deletion (characters, guilds) does not trigger wallet cleanup. Account deletion path complete (2026-03-08). Character/guild paths need lib-resource `x-references` integration design (balance disposition, frozen wallet handling).

### Design Resolved (Awaiting Implementation)

- [#211](https://github.com/beyond-immersion/bannou-service/issues/211) - Global supply analytics: Two Redis atomic counters per currency (`supply:{defId}:net`, `supply:{defId}:escrow`) maintained via `IRedisOperations` INCRBY/DECRBY on every credit/debit. Currency (L2) tracks operational supply; TotalMinted/TotalBurned are game-design classifications provided by Analytics via `IAccumulatedDataProvider` DI interface when Analytics is present, null when absent. See [#703](https://github.com/beyond-immersion/bannou-service/issues/703) (Analytics declarative accumulation engine). Counter initialization from balance sum on first use; periodic reconciliation worker.
- [#470](https://github.com/beyond-immersion/bannou-service/issues/470) - Wallet distribution analytics: Background aggregation worker (`CurrencyDistributionAggregationWorker`), periodic balance scan, percentile/Gini computation, Redis-cached results. Informational, stale-tolerant.
- [#471](https://github.com/beyond-immersion/bannou-service/issues/471) - Global supply cap enforcement: Check `supply:{defId}:net` counter in `CreditCurrencyAsync` alongside earn cap and wallet cap. Atomic INCRBY → check → compensating DECRBY if over cap. Add `SupplyCapOverflowBehavior` to definition schema. Add `CurrencySupplyCapReachedEvent`.
- [#222](https://github.com/beyond-immersion/bannou-service/issues/222) - Transaction retention cleanup: `TransactionRetentionCleanupWorkerService` background worker, queries expired transactions via `IQueryableStateStore<TransactionModel>`, deletes records + cleans wallet/reference indexes via `RemoveFromStringListAsync`. Config: `TransactionCleanupIntervalSeconds` (86400), `TransactionCleanupStartupDelaySeconds` (120), `TransactionCleanupBatchSize` (500). Last remaining item from #222 (3 of 4 tasks previously completed).

### Active
- **Batch lifecycle events** (2026-03-15): Switch to batch: true for high-frequency instance lifecycle events. Tracked via [#651](https://github.com/beyond-immersion/bannou-service/issues/651).

### Completed

- **2026-03-20**: Universal value anchoring + location-scoped exchange rate modifiers design resolved (#478). Three-layer architecture following the Worldstate CalendarEvent (#538) structural pattern: (1) `UniversalValue` (double?, nullable) coexists with `ExchangeRateToBase` — smooth migration, no breaking change. (2) `ExchangeRateModifier` entity with Realm/Location scope, game-time expiry, source tagging, multiplicative stacking. CalendarEvents are temporal triggers; modifiers are economic effects; god-actors connect them via `economy_add_rate_modifier` ABML action. (3) Effective rate = baseRate × product(activeModifiers), cached in-memory with event-invalidation. Buy/sell spread is NPC GOAP personality, not infrastructure. Variable provider: `${currency.exchange_rate.<from>.<to>}` for NPC arbitrage decisions.
- **2026-03-20**: Item linkage simplification (#473). Removed `ItemLinkageMode` enum and `linkageMode` field from schema — display semantics (visual rendering mode) are client metadata per T29, not server infrastructure. Retained `LinkedToItem` (bool) and `LinkedItemTemplateId` (Guid?) as server-validated cross-entity references. Remaining: add `IItemClient` dependency for creation-time template existence validation. No per-transaction enforcement — linkage is a reference, not a runtime gate.
- **2026-03-20**: Transaction retention cleanup design resolved (#222). `TransactionRetentionCleanupWorkerService` background worker using canonical polling loop (T6). Queries `IQueryableStateStore<TransactionModel>` with `Timestamp < retentionFloor`, deletes records + cleans `tx-wallet:` and `tx-ref:` indexes via `RemoveFromStringListAsync`. Per-item error isolation (T7). No archive — transactions are operational records (T29). Last remaining item from #222 (currency expiration, hold expiration, escrow expiration previously completed).
- **2026-03-20**: Analytics and supply tracking design resolved (#211, #470, #471). Core design: two Redis atomic counters per currency (`supply:{defId}:net` and `supply:{defId}:escrow`) maintained internally by Currency (L2) via `IRedisOperations` INCRBY/DECRBY on every credit/debit. InCirculation = net - escrow, O(1) reads. Supply cap enforcement checks net counter in CreditCurrencyAsync alongside existing earn cap and wallet cap. TotalMinted/TotalBurned are game-design-dependent classifications — not Currency's responsibility, owned by Analytics (L4, optional) or L5 extensions. Wallet distribution: background aggregation worker with Redis-cached percentiles/Gini. Follows the "Analytics is never in the critical path for game mechanics" principle from DIVINITY-GENERATION-ARCHITECTURE.md.
- **2026-03-19**: Category B deprecation compliance resolved. (1) Added instance creation guard in `CreditCurrencyAsync` — rejects with `BadRequest` when first-ever credit of a deprecated currency would create a new CurrencyBalance record. `ExecuteConversion` inherits the guard via delegation. (2) Confirmed `CleanDeprecatedCurrencyDefinitionsAsync` is fully implemented (uses `DeprecationCleanupHelper`, checks `bal-currency` reverse index, publishes `currency.definition.deleted`). Removed stale Stub #6 entry. `ICleanDeprecatedEntity` marker interface present.
- **2026-03-15**: Added CurrencyBalance as x-lifecycle entity with `instanceEntity: CurrencyBalance` on CurrencyDefinition. Wired up lifecycle event publishing: `currency.balance.created` (first credit), `currency.balance.updated` (credit/debit/transfer), `currency.balance.deleted` (account deletion cleanup). Resolves instance entity design gap.
- **2026-03-14**: Maintenance pass — removed 7 confirmed FIXED/IMPLEMENTED strikethrough items (currency expiration, hold expiration, client events, EarnCapResetTime fix, account deletion cleanup). Added Bug for missing Category B instance creation guard. Removed superseded #433 reference (replaced by #545). Reclassified account deletion Design Consideration to focus on remaining character/guild paths.
