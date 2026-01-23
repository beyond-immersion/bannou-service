# lib-escrow Service Specification

> **Version**: 2.0.0
> **Status**: Implementation Ready
> **Created**: 2026-01-22
> **Updated**: 2026-01-23
> **Dependencies**: lib-state, lib-messaging, lib-currency, lib-inventory, lib-contract
> **Dependent Plugins**: lib-market, lib-trade, lib-quest (future)

This document is the implementation-ready specification for the `lib-escrow` service. It provides a generic multi-party escrow system with full asset custody, capable of holding currency, items, and contracts.

---

## Table of Contents

1. [Overview](#overview)
2. [Plugin Configuration](#plugin-configuration)
3. [Core Concepts](#core-concepts)
4. [Asset Types and Custody](#asset-types-and-custody)
5. [Escrow Agreements](#escrow-agreements)
6. [Parties and Roles](#parties-and-roles)
7. [Trust Modes](#trust-modes)
8. [Lifecycle and State Machine](#lifecycle-and-state-machine)
9. [Contract Integration](#contract-integration)
10. [Finalization Flow](#finalization-flow)
11. [Periodic Validation](#periodic-validation)
12. [Dynamic Asset Handler Registration](#dynamic-asset-handler-registration)
13. [API Endpoints](#api-endpoints)
14. [Events](#events)
15. [State Stores](#state-stores)
16. [Error Codes](#error-codes)
17. [Integration Examples](#integration-examples)

---

## 1. Overview

lib-escrow is a **full-custody orchestration layer** that sits ABOVE the foundational asset plugins. It creates its own wallets, containers, and contract locks to take complete possession of assets during multi-party agreements.

### Core Features

- **Full custody**: Assets leave depositor possession entirely during escrow
- **Entity-based ownership**: Escrow agreements own wallets and containers as first-class entities
- **Contract integration**: Contracts can serve as conditions (terms), prizes (transferable assets), or both
- **Flexible trust modes**: From full multi-party consent to trusted arbiters
- **Token-based consent**: Cryptographic tokens prevent unauthorized actions
- **Multi-party support**: 2-party trades, N-party deals, auction settlements
- **Periodic validation**: Ensures held assets remain valid throughout escrow lifetime
- **Dynamic extensibility**: Custom asset types via handler registration
- **Finalization hooks**: Contract prebound APIs can distribute fees before release

### What lib-escrow Does NOT Do

- Manage currencies (lib-currency provides wallets and transfers)
- Manage inventories (lib-inventory provides containers and item movement)
- Manage agreements (lib-contract provides milestones and terms)
- Provide trade UI/UX (game layer)
- Market/auction mechanics (lib-market, future)

### Plugin Dependency Diagram

```
┌─────────────────────────────────────────┐
│           Game / Application            │
└─────────────────────────────────────────┘
                    │
         ┌──────────┼──────────┐
         ▼          ▼          ▼
    lib-market  lib-trade  lib-quest (future)
         │          │          │
         └──────────┼──────────┘
                    │
                    ▼
             ┌────────────┐
             │ lib-escrow │  ◄─── You are here (orchestration layer)
             └──────┬─────┘
                    │
         ┌──────────┼──────────┐
         ▼          ▼          ▼
  lib-currency  lib-inventory  lib-contract (foundational plugins)
         │          │          │
         └──────────┼──────────┘
                    │
         ┌──────────┴──────────┐
         ▼                     ▼
     lib-state          lib-messaging (infrastructure)
```

lib-escrow sits ABOVE lib-currency, lib-inventory, and lib-contract. It orchestrates these foundational plugins by creating owned entities (wallets, containers) and performing standard transfers. The foundational plugins have no knowledge of lib-escrow.

---

## 2. Plugin Configuration

```yaml
# Schema: EscrowPluginConfiguration
# Environment prefix: ESCROW_

EscrowPluginConfiguration:
  # ═══════════════════════════════════════════════════════════════
  # TIMEOUTS
  # ═══════════════════════════════════════════════════════════════

  # Default escrow expiration if not specified on creation
  # Environment: ESCROW_DEFAULT_TIMEOUT
  defaultTimeout: duration
  default: "P7D"  # 7 days

  # Maximum allowed escrow duration
  # Environment: ESCROW_MAX_TIMEOUT
  maxTimeout: duration
  default: "P30D"  # 30 days

  # Grace period after expiration before auto-refund
  # Environment: ESCROW_EXPIRATION_GRACE_PERIOD
  expirationGracePeriod: duration
  default: "PT1H"  # 1 hour

  # ═══════════════════════════════════════════════════════════════
  # TOKEN CONFIGURATION
  # ═══════════════════════════════════════════════════════════════

  # Token generation algorithm
  # Environment: ESCROW_TOKEN_ALGORITHM
  tokenAlgorithm: TokenAlgorithm
  default: hmac_sha256
  values:
    - hmac_sha256:
        description: "HMAC-SHA256 based tokens"
    - random_bytes:
        description: "Cryptographically random tokens"

  # Token length in bytes (before encoding)
  # Environment: ESCROW_TOKEN_LENGTH
  tokenLength: integer
  default: 32

  # Token secret for HMAC (must be set in production)
  # Environment: ESCROW_TOKEN_SECRET
  tokenSecret: string
  sensitive: true

  # ═══════════════════════════════════════════════════════════════
  # PROCESSING
  # ═══════════════════════════════════════════════════════════════

  # How often to check for expired escrows
  # Environment: ESCROW_EXPIRATION_CHECK_INTERVAL
  expirationCheckInterval: duration
  default: "PT1M"

  # Batch size for expiration processing
  # Environment: ESCROW_EXPIRATION_BATCH_SIZE
  expirationBatchSize: integer
  default: 100

  # How often to validate held assets
  # Environment: ESCROW_VALIDATION_CHECK_INTERVAL
  validationCheckInterval: duration
  default: "PT5M"

  # ═══════════════════════════════════════════════════════════════
  # LIMITS
  # ═══════════════════════════════════════════════════════════════

  # Maximum parties per escrow
  # Environment: ESCROW_MAX_PARTIES
  maxParties: integer
  default: 10

  # Maximum asset lines per deposit
  # Environment: ESCROW_MAX_ASSETS_PER_DEPOSIT
  maxAssetsPerDeposit: integer
  default: 50

  # Maximum concurrent pending escrows per party
  # Environment: ESCROW_MAX_PENDING_PER_PARTY
  maxPendingPerParty: integer
  default: 100
```

---

## 3. Core Concepts

### 3.1 Full Custody Model

lib-escrow takes **complete physical possession** of assets. This is not locking or holds - assets are transferred entirely out of depositor ownership.

```
┌─────────────────┐         ┌─────────────────┐         ┌─────────────────┐
│   Depositor     │         │   Escrow Entity  │         │   Recipient     │
│                 │         │                  │         │                 │
│  Wallet: 500g   │──debit──►│  Wallet: 500g    │──credit─►│  Wallet: +500g  │
│  Inventory: ⚔️   │──move───►│  Container: ⚔️   │──move───►│  Inventory: ⚔️  │
│  Contract: 📜   │──lock───►│  Locked: 📜      │──xfer───►│  Contract: 📜   │
└─────────────────┘         └─────────────────┘         └─────────────────┘
       DEPOSIT                  HELD IN ESCROW                 RELEASE
```

### 3.2 Escrow as Entity Owner

Each escrow agreement is a first-class entity that owns infrastructure:

- **Wallet**: Created via `/currency/wallet/create` with `ownerType=escrow`, `ownerId=agreementId`
- **Container**: Created via `/inventory/create-container` with `ownerType=escrow`, `ownerId=agreementId`
- **Contract locks**: Established via lib-contract's locking mechanism

These are real, queryable entities in their respective systems. The escrow service orchestrates transfers into and out of them.

### 3.3 Asset Transfer Mechanics

| Asset Type | Deposit Operation | Release Operation | Refund Operation |
|-----------|-------------------|-------------------|------------------|
| **Currency** | `/currency/transfer` (depositor wallet → escrow wallet) | `/currency/transfer` (escrow wallet → recipient wallet) | `/currency/transfer` (escrow wallet → depositor wallet) |
| **Item** | `/inventory/transfer` (depositor container → escrow container) | `/inventory/transfer` (escrow container → recipient container) | `/inventory/transfer` (escrow container → depositor container) |
| **Contract** | `/contract/lock` (escrow becomes guardian) | `/contract/transfer-party` (reassign to recipient) | `/contract/unlock` (return to original state) |
| **Custom** | Registered deposit endpoint | Registered release endpoint | Registered refund endpoint |

### 3.4 Idempotency

All mutating operations require idempotency keys. lib-escrow maintains its own idempotency cache to prevent duplicate operations.

### 3.5 Soulbound Item Rejection

Items with `soulbound=true` or `tradeable=false` are **rejected at deposit time** with error `ASSET_DEPOSIT_REJECTED`. This is a hard rejection - soulbound items cannot enter escrow regardless of trust mode.

---

## 4. Asset Types and Custody

Assets are represented as polymorphic references identifying what is held in escrow.

```yaml
# Schema: EscrowAsset
EscrowAsset:
  # ═══════════════════════════════════════════════════════════════
  # ASSET IDENTIFICATION
  # ═══════════════════════════════════════════════════════════════

  # Discriminator for asset type
  assetType: AssetType
  values:
    - currency:
        description: "Currency amount held in escrow's wallet"
    - item:
        description: "Item instance held in escrow's container"
    - item_stack:
        description: "Stackable items (quantity) held in escrow's container"
    - contract:
        description: "Contract instance locked under escrow guardianship"
    - custom:
        description: "Custom asset type via registered handler"

  # ═══════════════════════════════════════════════════════════════
  # CURRENCY ASSETS
  # ═══════════════════════════════════════════════════════════════

  # For assetType=currency
  currencyDefinitionId: uuid
  nullable: true

  currencyCode: string
  nullable: true
  notes: "Denormalized for display/logging"

  currencyAmount: decimal
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # ITEM ASSETS
  # ═══════════════════════════════════════════════════════════════

  # For assetType=item (unique instance)
  itemInstanceId: uuid
  nullable: true

  # For display/logging
  itemName: string
  nullable: true

  # For assetType=item_stack (stackable)
  itemTemplateId: uuid
  nullable: true

  itemTemplateName: string
  nullable: true
  notes: "Denormalized for display/logging"

  itemQuantity: integer
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # CONTRACT ASSETS
  # ═══════════════════════════════════════════════════════════════

  # For assetType=contract
  contractInstanceId: uuid
  nullable: true

  contractTemplateCode: string
  nullable: true
  notes: "Denormalized for display/logging"

  contractDescription: string
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # CUSTOM ASSETS
  # ═══════════════════════════════════════════════════════════════

  # For assetType=custom
  customAssetType: string
  nullable: true
  notes: "Registered handler type identifier"

  customAssetId: string
  nullable: true

  customAssetData: object
  nullable: true
  notes: "Handler-specific data passed to deposit/release/refund endpoints"

  # ═══════════════════════════════════════════════════════════════
  # SOURCE TRACKING
  # ═══════════════════════════════════════════════════════════════

  # Where this asset came from (for refunds)
  sourceOwnerId: uuid
  sourceOwnerType: string

  # For currency: source wallet ID
  # For items: source container ID
  # For contracts: original party being reassigned
  sourceContainerId: uuid
  nullable: true
```

```yaml
# Schema: EscrowAssetBundle
# Groups multiple assets for a single deposit or release
EscrowAssetBundle:
  bundleId: uuid

  assets: [EscrowAsset]

  # Summary for display
  description: string
  nullable: true

  # Total "value" for comparison (optional, game-defined)
  estimatedValue: decimal
  nullable: true
  notes: "Optional valuation for UI display"
```

---

## 5. Escrow Agreements

The central entity tracking a multi-party escrow with full custody.

```yaml
# Schema: EscrowAgreement
EscrowAgreement:
  id: uuid

  # ═══════════════════════════════════════════════════════════════
  # TYPE
  # ═══════════════════════════════════════════════════════════════

  escrowType: EscrowType
  values:
    - two_party:
        description: "Simple buyer/seller or trader escrow"
    - multi_party:
        description: "N parties with complex deposit/receive rules"
    - conditional:
        description: "Release based on external condition or contract fulfillment"
    - auction:
        description: "Winner-takes-all with refunds to losers"

  # ═══════════════════════════════════════════════════════════════
  # TRUST MODE
  # ═══════════════════════════════════════════════════════════════

  trustMode: EscrowTrustMode
  default: full_consent
  values:
    - full_consent:
        description: |
          All parties must explicitly consent using tokens.
          Deposit token required to deposit.
          Release token required to consent to release.
    - initiator_trusted:
        description: |
          The service/entity that created the escrow can complete
          it unilaterally. Used for system-managed escrows.
    - single_party_trusted:
        description: |
          A designated party can complete unilaterally.
          Useful for arbiters or game masters.

  # For single_party_trusted: which party has authority
  trustedPartyId: uuid
  nullable: true

  trustedPartyType: string
  nullable: true

  # For initiator_trusted: which service created this
  initiatorServiceId: string
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # OWNED INFRASTRUCTURE
  # ═══════════════════════════════════════════════════════════════

  # Escrow's own wallet (created on agreement creation)
  escrowWalletId: uuid
  notes: "Created via /currency/wallet/create with ownerType=escrow"

  # Escrow's own container (created on agreement creation)
  escrowContainerId: uuid
  notes: "Created via /inventory/create-container with ownerType=escrow"

  # ═══════════════════════════════════════════════════════════════
  # PARTIES
  # ═══════════════════════════════════════════════════════════════

  parties: [EscrowParty]

  # How many parties must consent for release
  # -1 = all parties with consent_required=true
  requiredConsentsForRelease: integer
  default: -1

  # ═══════════════════════════════════════════════════════════════
  # EXPECTED DEPOSITS
  # ═══════════════════════════════════════════════════════════════

  # What deposits are expected from each party
  # Used for validation and status tracking
  expectedDeposits: [ExpectedDeposit]

  # ═══════════════════════════════════════════════════════════════
  # ACTUAL DEPOSITS
  # ═══════════════════════════════════════════════════════════════

  deposits: [EscrowDeposit]

  # ═══════════════════════════════════════════════════════════════
  # RELEASE ALLOCATIONS
  # ═══════════════════════════════════════════════════════════════

  # How assets should be distributed on release
  # If null, derives from expectedDeposits and party roles
  releaseAllocations: [ReleaseAllocation]
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # CONTRACT BINDING
  # ═══════════════════════════════════════════════════════════════

  # If this escrow's conditions are governed by a contract
  boundContractId: uuid
  nullable: true
  notes: |
    When set, escrow listens to contract milestone events.
    contract.fulfilled → triggers release.
    contract.failed (permanent) → triggers refund.
    Manual release checks contract completion status (not an override).

  # ═══════════════════════════════════════════════════════════════
  # STATUS
  # ═══════════════════════════════════════════════════════════════

  status: EscrowStatus
  values:
    - pending_deposits:
        description: "Waiting for parties to deposit"
    - partially_funded:
        description: "Some but not all deposits received"
    - funded:
        description: "All deposits received, awaiting consent/condition"
    - pending_consent:
        description: "Some consents received, waiting for more"
    - pending_condition:
        description: "For conditional: waiting for contract fulfillment or external verification"
    - finalizing:
        description: "Running contract finalizer prebound APIs (transient)"
    - releasing:
        description: "Release in progress (transient)"
    - released:
        description: "Assets transferred to recipients"
    - refunding:
        description: "Refund in progress (transient)"
    - refunded:
        description: "Assets returned to depositors"
    - disputed:
        description: "In dispute, arbiter must resolve"
    - expired:
        description: "Timed out without completion"
    - cancelled:
        description: "Cancelled before funding complete"
    - validation_failed:
        description: "Held assets changed, awaiting re-affirmation or violation resolution"

  # ═══════════════════════════════════════════════════════════════
  # VALIDATION
  # ═══════════════════════════════════════════════════════════════

  lastValidatedAt: timestamp
  nullable: true

  validationFailures: [ValidationFailure]
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # CONSENTS
  # ═══════════════════════════════════════════════════════════════

  consents: [EscrowConsent]

  # ═══════════════════════════════════════════════════════════════
  # TIMING
  # ═══════════════════════════════════════════════════════════════

  createdAt: timestamp
  createdBy: uuid
  createdByType: string

  # When all expected deposits were received
  fundedAt: timestamp
  nullable: true

  # Auto-refund if not completed by this time
  expiresAt: timestamp

  # When the escrow reached terminal state
  completedAt: timestamp
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # REFERENCE
  # ═══════════════════════════════════════════════════════════════

  # What this escrow is for (for querying and display)
  referenceType: string
  nullable: true
  examples: ["trade", "auction", "contract", "bet", "quest_reward"]

  referenceId: uuid
  nullable: true

  description: string
  nullable: true

  # Game/application specific metadata
  metadata: object
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # RESOLUTION
  # ═══════════════════════════════════════════════════════════════

  resolution: EscrowResolution
  nullable: true
  values:
    - released:
        description: "Assets went to designated recipients"
    - refunded:
        description: "Assets returned to depositors"
    - split:
        description: "Arbiter split assets between parties"
    - expired_refunded:
        description: "Timed out, auto-refunded"
    - cancelled_refunded:
        description: "Cancelled, deposits refunded"
    - violation_refunded:
        description: "Validation failure caused refund"

  resolutionNotes: string
  nullable: true
```

---

## 6. Parties and Roles

```yaml
# Schema: EscrowParty
EscrowParty:
  # ═══════════════════════════════════════════════════════════════
  # IDENTIFICATION
  # ═══════════════════════════════════════════════════════════════

  partyId: uuid
  partyType: string
  examples: ["account", "character", "npc", "guild", "system"]

  # Display name for UI/logging
  displayName: string
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # ROLE
  # ═══════════════════════════════════════════════════════════════

  role: EscrowPartyRole
  values:
    - depositor:
        description: "Deposits assets into escrow"
    - recipient:
        description: "Receives assets when released"
    - depositor_recipient:
        description: "Both deposits and can receive (typical for trades)"
    - arbiter:
        description: "Can resolve disputes, doesn't deposit or receive"
    - observer:
        description: "Can view status but cannot act"

  # ═══════════════════════════════════════════════════════════════
  # CONSENT REQUIREMENTS
  # ═══════════════════════════════════════════════════════════════

  # Whether this party's consent is required for release
  consentRequired: boolean
  default: true
  notes: "Arbiters and observers typically have this false"

  # ═══════════════════════════════════════════════════════════════
  # PARTY INFRASTRUCTURE REFERENCES
  # ═══════════════════════════════════════════════════════════════

  # Party's wallet (for currency deposits/receipts)
  walletId: uuid
  nullable: true

  # Party's container (for item deposits/receipts)
  containerId: uuid
  nullable: true

  # ═══════════════════════════════════════════════════════════════
  # TOKENS (for full_consent mode)
  # ═══════════════════════════════════════════════════════════════

  depositToken: string
  nullable: true
  notes: "Issued on escrow creation, required when depositing"

  depositTokenUsed: boolean
  default: false

  depositTokenUsedAt: timestamp
  nullable: true

  releaseToken: string
  nullable: true
  notes: "Issued when fully funded, required when consenting to release"

  releaseTokenUsed: boolean
  default: false

  releaseTokenUsedAt: timestamp
  nullable: true
```

```yaml
# Schema: ExpectedDeposit
# Defines what a party should deposit
ExpectedDeposit:
  partyId: uuid
  partyType: string

  # Expected assets from this party
  expectedAssets: [EscrowAsset]

  # Is this deposit optional?
  optional: boolean
  default: false

  # Deadline for this specific deposit (if different from escrow expiry)
  depositDeadline: timestamp
  nullable: true

  # Has this party fulfilled their deposit requirement?
  fulfilled: boolean
  default: false
```

```yaml
# Schema: EscrowDeposit
# Records an actual deposit
EscrowDeposit:
  id: uuid
  escrowId: uuid

  partyId: uuid
  partyType: string

  # What was deposited
  assets: EscrowAssetBundle

  depositedAt: timestamp

  # Token used (for audit)
  depositTokenUsed: string
  nullable: true

  # Idempotency key for this deposit
  idempotencyKey: string
```

```yaml
# Schema: ReleaseAllocation
# Defines who gets what on release
ReleaseAllocation:
  recipientPartyId: uuid
  recipientPartyType: string

  # Which assets this recipient should receive
  assets: [EscrowAsset]

  # Where to deliver (wallet ID, container ID, etc.)
  destinationWalletId: uuid
  nullable: true

  destinationContainerId: uuid
  nullable: true
```

```yaml
# Schema: EscrowConsent
# Records a party's consent decision
EscrowConsent:
  partyId: uuid
  partyType: string

  consentType: EscrowConsentType
  values:
    - release:
        description: "Agrees to release assets to recipients"
    - refund:
        description: "Agrees to refund assets to depositors"
    - dispute:
        description: "Raises a dispute"
    - reaffirm:
        description: "Re-affirms after validation failure"

  consentedAt: timestamp

  # Token used (for audit)
  releaseTokenUsed: string
  nullable: true

  notes: string
  nullable: true
```

```yaml
# Schema: ValidationFailure
# Records a validation check failure
ValidationFailure:
  detectedAt: timestamp

  assetType: string
  assetDescription: string

  failureType: ValidationFailureType
  values:
    - asset_missing:
        description: "Asset no longer exists in escrow's custody"
    - asset_mutated:
        description: "Asset properties changed (e.g., item durability, contract terms)"
    - asset_expired:
        description: "Asset has a time-based expiration that triggered"
    - balance_mismatch:
        description: "Wallet balance doesn't match expected held amount"

  # Which party's deposit is affected
  affectedPartyId: uuid
  affectedPartyType: string

  details: object
  nullable: true
```

---

## 7. Trust Modes

### 7.1 Full Consent (Default)

All parties must explicitly consent using cryptographic tokens.

**Flow:**
1. Escrow created → deposit tokens issued to each depositor
2. Each depositor uses their deposit token to deposit assets
3. When fully funded → release tokens issued to parties with consentRequired=true
4. Each required party uses their release token to consent
5. When requiredConsentsForRelease reached → finalization begins

**Security:**
- Tokens are single-use
- Tokens are tied to specific escrow and party
- Tokens are time-limited (expire with escrow)
- Token hashes stored, not raw tokens

### 7.2 Initiator Trusted

The service that created the escrow can complete it unilaterally.

**Use Cases:**
- Quest rewards (quest service creates and completes)
- Auction settlements (auction service manages)
- System-managed contracts

**Flow:**
1. Service creates escrow with `trustMode: initiator_trusted`
2. Service directs parties to deposit (may use deposit tokens or not)
3. Service calls release/refund directly with service credentials

### 7.3 Single Party Trusted

A designated party (arbiter) can complete the escrow unilaterally.

**Use Cases:**
- Disputed trades with GM arbiter
- Escorted trades (NPC witness)
- Contractual agreements with enforcer

**Flow:**
1. Escrow created with `trustedPartyId` pointing to arbiter
2. Parties deposit normally
3. Arbiter can release, refund, or split at any time

---

## 8. Lifecycle and State Machine

```
                                    ┌─────────────┐
                                    │   CREATED   │
                                    └──────┬──────┘
                                           │ (wallet + container created)
                              ┌────────────┼────────────┐
                              │            │            │
                              ▼            ▼            ▼
                    ┌─────────────┐  ┌───────────┐  ┌──────────┐
                    │  CANCELLED  │  │  PENDING  │  │ EXPIRED  │
                    │  (no deps)  │  │ DEPOSITS  │  │(timeout) │
                    └─────────────┘  └─────┬─────┘  └──────────┘
                                           │
                              ┌────────────┼────────────┐
                              │            │            │
                              ▼            ▼            ▼
                    ┌─────────────┐  ┌───────────┐  ┌──────────┐
                    │  CANCELLED  │  │ PARTIALLY │  │ EXPIRED  │
                    │ (refunded)  │  │  FUNDED   │  │(refunded)│
                    └─────────────┘  └─────┬─────┘  └──────────┘
                                           │
                                           ▼
                                    ┌─────────────┐
                                    │   FUNDED    │
                                    └──────┬──────┘
                                           │
              ┌────────────────────────────┼─────────────────────────────┐
              │                            │                             │
              ▼                            ▼                             ▼
       ┌────────────┐              ┌─────────────┐              ┌────────────────┐
       │  DISPUTED  │              │   PENDING   │              │    PENDING     │
       │            │◄─────────────│   CONSENT   │              │   CONDITION    │
       └──────┬─────┘              └──────┬──────┘              └───────┬────────┘
              │                           │                             │
              │    ┌──────────────────────┼────────────────────────┐    │
              │    │                      │                        │    │
              ▼    ▼                      ▼                        ▼    ▼
       ┌────────────┐              ┌─────────────┐              ┌────────────┐
       │ REFUNDED   │              │ FINALIZING  │              │ EXPIRED    │
       │            │              │ (transient) │              │ (refunded) │
       └────────────┘              └──────┬──────┘              └────────────┘
                                          │
                                          ▼
                                   ┌─────────────┐
                                   │  RELEASING  │
                                   │ (transient) │
                                   └──────┬──────┘
                                          │
                                          ▼
                                   ┌─────────────┐
                                   │  RELEASED   │
                                   └─────────────┘


Validation can interrupt any active state:

       ┌─────────────────────┐
       │  Any active state   │ (funded, pending_consent, pending_condition)
       └──────────┬──────────┘
                  │ (validation fails)
                  ▼
       ┌─────────────────────┐
       │  VALIDATION_FAILED  │
       └──────────┬──────────┘
                  │
         ┌────────┼────────┐
         │        │        │
         ▼        ▼        ▼
    (reaffirm) (refund) (violation)
```

### State Transitions

| From | To | Trigger |
|------|-----|---------|
| created | pending_deposits | Initial state after wallet/container creation |
| pending_deposits | partially_funded | First deposit received |
| pending_deposits | cancelled | Cancel before deposits |
| pending_deposits | expired | Timeout |
| partially_funded | funded | All expected deposits received |
| partially_funded | cancelled | Cancel (refunds partial) |
| partially_funded | expired | Timeout (refunds partial) |
| funded | pending_consent | Awaiting party consents (non-conditional) |
| funded | pending_condition | For conditional type |
| funded | disputed | Party raises dispute |
| pending_consent | finalizing | Required consents reached |
| pending_consent | refunded | All consent to refund |
| pending_consent | disputed | Party raises dispute |
| pending_consent | expired | Timeout |
| pending_condition | finalizing | Contract fulfilled or condition verified |
| pending_condition | refunded | Contract permanently failed or condition failed |
| pending_condition | expired | Timeout |
| finalizing | releasing | Finalizer APIs complete successfully |
| finalizing | refunded | Finalizer APIs indicate failure |
| releasing | released | All transfers complete |
| disputed | released | Arbiter releases |
| disputed | refunded | Arbiter refunds |
| disputed | split | Arbiter splits |
| any active | validation_failed | Periodic validation detects change |
| validation_failed | previous active | All parties re-affirm |
| validation_failed | refunded | Violation resolution triggers refund |

---

## 9. Contract Integration

lib-escrow integrates with lib-contract in three distinct ways:

### 9.1 Contract as Condition (Bound Contract)

When `boundContractId` is set on an escrow, the contract governs release:

```yaml
# Escrow listens to these events for its bound contract:
contract.fulfilled:
  action: Transition to finalizing → releasing → released

contract.breached (permanent):
  action: Transition to refunding → refunded

contract.terminated:
  action: Transition to refunding → refunded
```

**Manual release with bound contract**: When a party triggers manual release on an escrow with `boundContractId`, the escrow checks the contract's current status via `/contract/instance/get-status`. If the contract is not in `fulfilled` status, the release is rejected. This is NOT an override - the manual trigger is a convenience for when the event was missed, not a way to bypass the contract.

### 9.2 Contract as Prize (Deposited Asset)

Contract instances can be deposited as `assetType=contract`. When deposited:

1. lib-escrow calls `/contract/lock` with `guardianId=escrowId`, `guardianType=escrow`
2. The contract becomes locked: no party can modify, terminate, or transfer it
3. On release: lib-escrow calls `/contract/transfer-party` to reassign the relevant party role to the recipient
4. On refund: lib-escrow calls `/contract/unlock` to restore the contract to its original state

**Example**: Land deed (a contract between owner and realm) held in escrow during a property sale. The deed contract is locked so the seller can't void it, then transferred to the buyer on completion.

### 9.3 Contract as Both Condition AND Prize

An escrow can have a `boundContractId` (conditions) AND hold contracts as deposited assets (prizes). These are independent:

- The bound contract governs WHEN release happens
- The deposited contracts are WHAT gets released

**Example**: A property sale where:
- Bound contract: "Buyer pays X gold within 7 days" (milestones: payment_deposited, inspection_complete)
- Deposited contract: The land deed itself (transferred to buyer on release)
- Deposited currency: The purchase price (transferred to seller on release)

---

## 10. Finalization Flow

When an escrow transitions to `finalizing`, a multi-step process runs before assets are released:

### 10.1 Finalization Steps

```
1. Escrow conditions met (contract fulfilled / consent reached / condition verified)
         │
         ▼
2. Status → FINALIZING
         │
         ▼
3. If boundContractId exists:
   - Read contract's finalizer prebound APIs (onFulfill/onTerminate)
   - Substitute template variables including {{EscrowWalletId}} and {{EscrowContainerId}}
   - Execute finalizer APIs (these can move fees/items to third parties)
         │
         ▼
4. If any finalizer fails critically:
   - Status → REFUNDING (abort release, return all assets)
         │
         ▼
5. Status → RELEASING
   - Transfer remaining assets from escrow wallet/container to recipients
   - Transfer/unlock deposited contracts to recipients
         │
         ▼
6. Status → RELEASED
   - Cleanup: close wallet, delete container
   - Publish escrow.released event
```

### 10.2 Template Variables for Finalizer APIs

When executing a bound contract's finalizer prebound APIs, lib-escrow provides these additional context variables:

| Variable | Description |
|----------|-------------|
| `{{EscrowWalletId}}` | The escrow agreement's wallet ID |
| `{{EscrowContainerId}}` | The escrow agreement's container ID |
| `{{EscrowAgreementId}}` | The escrow agreement's ID |

This allows contract terms to specify fee distribution from the escrow's own holdings:

```yaml
# Example: Contract milestone onFulfill takes 5% broker fee
serviceName: "currency"
endpoint: "/currency/transfer"
payloadTemplate: |
  {
    "source_wallet_id": "{{EscrowWalletId}}",
    "target_wallet_id": "{{contract.terms.broker_wallet_id}}",
    "currency_id": "{{contract.terms.fee_currency_id}}",
    "amount": {{contract.terms.broker_fee_amount}},
    "reference_id": "{{EscrowAgreementId}}",
    "reference_type": "escrow_fee"
  }
```

```yaml
# Example: Move specific items to a third party (lawyer's cut)
serviceName: "inventory"
endpoint: "/inventory/transfer"
payloadTemplate: |
  {
    "source_container_id": "{{EscrowContainerId}}",
    "target_container_id": "{{contract.terms.lawyer_container_id}}",
    "item_instance_id": "{{contract.terms.fee_item_id}}",
    "reference_id": "{{EscrowAgreementId}}",
    "reference_type": "escrow_fee"
  }
```

### 10.3 Fee Handling Design

Fees are NOT a built-in escrow feature. Instead, they are implemented through contract terms:

1. The contract template specifies that each party must deposit X extra (fee amount) into escrow
2. The contract's `onFulfill` prebound APIs specify where the fee goes (broker, lawyer, guild treasury, etc.)
3. During finalization, these APIs execute FIRST, moving fees from the escrow's wallet/container to third parties
4. The remaining assets in escrow are then released to the designated recipients

This means:
- lib-escrow has no fee logic
- Fees are just contract terms
- Fee distribution is just prebound API execution
- Any fee structure is possible (flat, percentage, multi-party splits)

---

## 11. Periodic Validation

lib-escrow periodically validates that held assets remain intact and unchanged.

### 11.1 What Gets Validated

| Asset Type | Validation Check |
|-----------|-----------------|
| **Currency** | Query escrow wallet balance matches expected total for each currency |
| **Items** | Query escrow container, verify each expected item instance still exists |
| **Item Stacks** | Verify stack quantity hasn't decreased |
| **Contracts** | Query contract status, verify still locked and valid (not externally voided) |
| **Custom** | Call registered validate endpoint |

### 11.2 Validation Frequency

Configured via `validationCheckInterval` (default: 5 minutes). Only active escrows are validated (status in: funded, pending_consent, pending_condition).

### 11.3 Failure Handling

When validation detects a discrepancy:

1. Escrow transitions to `validation_failed` status
2. A `ValidationFailure` record is created identifying:
   - Which asset changed
   - How it changed (missing, mutated, expired, balance mismatch)
   - Which party's deposit is affected
3. Event `escrow.validation.failed` is published
4. Both parties receive notification

### 11.4 Resolution Options

After validation failure, two paths:

**Re-affirmation**: All parties consent (type=reaffirm) to continue with the changed state. The escrow returns to its previous active state. This handles cases where both sides agree the change is acceptable.

**Violation**: The party whose assets changed is considered in violation. The other party can:
- Trigger refund (get their own deposits back)
- The violating party's changed/missing assets are handled as-is (if items disappeared, they don't get refunded what no longer exists)

### 11.5 What Causes Validation Failures

- Item with durability reaching 0 (broken) while in escrow container
- Item with time-limited existence expiring
- External admin action removing funds from escrow wallet (shouldn't happen, but detected)
- Contract being forcibly terminated by admin while locked
- Custom asset handler reporting invalid state

---

## 12. Dynamic Asset Handler Registration

### 12.1 Built-in Asset Types

lib-escrow has hardcoded knowledge of three asset types:

| Type | Deposit Mechanism | Release Mechanism | Validate Mechanism |
|------|-------------------|-------------------|-------------------|
| `currency` | `/currency/transfer` into escrow wallet | `/currency/transfer` out of escrow wallet | Check wallet balance |
| `item` / `item_stack` | `/inventory/transfer` into escrow container | `/inventory/transfer` out of escrow container | Check container contents |
| `contract` | `/contract/lock` with escrow as guardian | `/contract/transfer-party` to recipient | Check contract status |

### 12.2 Custom Asset Types

For games with asset types beyond currency/items/contracts, lib-escrow provides a handler registration system.

```yaml
# Schema: AssetHandlerRegistration
AssetHandlerRegistration:
  assetType: string
  notes: "Unique identifier, e.g., 'guild_ownership', 'land_plot', 'character_slot'"

  pluginId: string
  notes: "Which plugin implements this handler"

  # Called when depositing this asset type into escrow
  depositEndpoint: string
  notes: "Must accept: { assetId, assetData, escrowId, idempotencyKey } and take custody"

  # Called when releasing this asset type to recipient
  releaseEndpoint: string
  notes: "Must accept: { assetId, assetData, recipientId, recipientType, escrowId, idempotencyKey }"

  # Called when refunding this asset type to depositor
  refundEndpoint: string
  notes: "Must accept: { assetId, assetData, depositorId, depositorType, escrowId, idempotencyKey }"

  # Called during periodic validation to check asset is still valid
  validateEndpoint: string
  notes: "Must accept: { assetId, assetData, escrowId } and return { valid, details }"
```

**Example: Guild Ownership Handler**

```yaml
assetType: "guild_ownership"
pluginId: "lib-guild"
depositEndpoint: "/guild/escrow/deposit"
  # Takes custody: sets guild owner to escrow entity, prevents member changes
releaseEndpoint: "/guild/escrow/release"
  # Transfers: sets guild owner to recipient
refundEndpoint: "/guild/escrow/refund"
  # Returns: sets guild owner back to depositor
validateEndpoint: "/guild/escrow/validate"
  # Checks: guild still exists, hasn't been disbanded
```

### 12.3 Handler Contract

All custom handlers must implement these guarantees:

1. **Deposit**: Take full custody. The original owner loses all control.
2. **Release**: Transfer custody to the specified recipient. Escrow loses control.
3. **Refund**: Return custody to the original depositor. Escrow loses control.
4. **Validate**: Report whether the asset is still in the expected state.
5. **Idempotency**: All operations must be idempotent using the provided key.
6. **Atomicity**: Each operation either fully succeeds or fully fails (no partial states).

---

## 13. API Endpoints

### 13.1 Escrow Lifecycle

```yaml
# ══════════════════════════════════════════════════════════════════
# ESCROW CREATION
# ══════════════════════════════════════════════════════════════════

/escrow/create:
  method: POST
  x-permissions:
    - role: developer
      states: {}
  description: |
    Create a new escrow agreement. Creates the escrow's wallet and container,
    issues deposit tokens, and begins accepting deposits.
  request:
    escrowType: EscrowType
    trustMode: EscrowTrustMode
    trustedPartyId: uuid              # For single_party_trusted
    trustedPartyType: string
    parties: [
      {
        partyId: uuid
        partyType: string
        displayName: string
        role: EscrowPartyRole
        consentRequired: boolean
        walletId: uuid                # Party's wallet for currency ops
        containerId: uuid             # Party's container for item ops
      }
    ]
    expectedDeposits: [
      {
        partyId: uuid
        partyType: string
        expectedAssets: [EscrowAsset]
        optional: boolean
        depositDeadline: timestamp
      }
    ]
    releaseAllocations: [             # Optional, can derive from deposits
      {
        recipientPartyId: uuid
        recipientPartyType: string
        assets: [EscrowAsset]
        destinationWalletId: uuid
        destinationContainerId: uuid
      }
    ]
    boundContractId: uuid             # Optional: contract governing conditions
    requiredConsentsForRelease: integer
    expiresAt: timestamp              # Optional, uses default
    referenceType: string
    referenceId: uuid
    description: string
    metadata: object
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    depositTokens: [                  # For full_consent mode
      { partyId: uuid, partyType: string, depositToken: string }
    ]
  errors:
    - INVALID_PARTY_CONFIGURATION
    - INVALID_TRUST_CONFIGURATION
    - TOO_MANY_PARTIES
    - INVALID_EXPECTED_DEPOSITS
    - EXPIRES_TOO_SOON
    - EXPIRES_TOO_LATE
    - MAX_PENDING_ESCROWS_EXCEEDED
    - WALLET_CREATION_FAILED
    - CONTAINER_CREATION_FAILED
    - BOUND_CONTRACT_NOT_FOUND

/escrow/get:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: Get escrow details
  request:
    escrowId: uuid
  response:
    escrow: EscrowAgreement
  errors:
    - ESCROW_NOT_FOUND
    - NOT_AUTHORIZED

/escrow/list:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: List escrows for a party
  request:
    partyId: uuid
    partyType: string
    status: [EscrowStatus]            # Filter by status
    referenceType: string
    referenceId: uuid
    fromDate: timestamp
    toDate: timestamp
    limit: integer
    offset: integer
  response:
    escrows: [EscrowAgreement]
    totalCount: integer
```

### 13.2 Deposits

```yaml
# ══════════════════════════════════════════════════════════════════
# DEPOSIT OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/deposit:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: |
    Deposit assets into escrow. Transfers currency from party wallet to escrow wallet,
    moves items from party container to escrow container, or locks contracts.
    Rejects soulbound/non-tradeable items.
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
    assets: EscrowAssetBundle
    depositToken: string              # Required for full_consent
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    deposit: EscrowDeposit
    fullyFunded: boolean
    releaseTokens: [                  # Issued when fully funded
      { partyId: uuid, partyType: string, releaseToken: string }
    ]
  errors:
    - ESCROW_NOT_FOUND
    - NOT_A_DEPOSITOR
    - ESCROW_NOT_ACCEPTING_DEPOSITS
    - INVALID_DEPOSIT_TOKEN
    - DEPOSIT_TOKEN_ALREADY_USED
    - ASSET_DEPOSIT_REJECTED
    - DEPOSIT_EXCEEDS_EXPECTED
    - DEPOSIT_ASSETS_MISMATCH
    - INSUFFICIENT_BALANCE
    - ITEM_NOT_FOUND
    - ITEM_SOULBOUND
    - CONTRACT_NOT_FOUND
    - CONTRACT_NOT_TRANSFERABLE
    - TRANSFER_FAILED

/escrow/deposit/validate:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: Validate a deposit without executing (dry run)
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
    assets: EscrowAssetBundle
  response:
    valid: boolean
    errors: [string]
    warnings: [string]

/escrow/deposit/status:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: Get deposit status for a party
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
  response:
    expectedAssets: [EscrowAsset]
    depositedAssets: [EscrowAsset]
    fulfilled: boolean
    depositToken: string              # If not yet used
    depositDeadline: timestamp
```

### 13.3 Consent and Resolution

```yaml
# ══════════════════════════════════════════════════════════════════
# CONSENT OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/consent:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: Record party consent for release, refund, or re-affirmation
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
    consentType: EscrowConsentType
    releaseToken: string              # Required for full_consent
    notes: string
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    consentRecorded: boolean
    triggered: boolean                # Did this consent trigger completion?
    newStatus: EscrowStatus
  errors:
    - ESCROW_NOT_FOUND
    - NOT_A_PARTY
    - ESCROW_NOT_AWAITING_CONSENT
    - INVALID_RELEASE_TOKEN
    - RELEASE_TOKEN_ALREADY_USED
    - ALREADY_CONSENTED
    - CONSENT_NOT_REQUIRED
    - BOUND_CONTRACT_NOT_FULFILLED

/escrow/consent/status:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: Get consent status for escrow
  request:
    escrowId: uuid
  response:
    partiesRequiringConsent: [
      {
        partyId: uuid
        partyType: string
        displayName: string
        consentGiven: boolean
        consentType: EscrowConsentType
        consentedAt: timestamp
      }
    ]
    consentsReceived: integer
    consentsRequired: integer
    canRelease: boolean
    canRefund: boolean
```

### 13.4 Completion

```yaml
# ══════════════════════════════════════════════════════════════════
# COMPLETION OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/release:
  method: POST
  x-permissions:
    - role: developer
      states: {}
  description: |
    Trigger release (for trusted modes or after consent).
    If boundContractId is set, checks contract status first (must be fulfilled).
    Runs finalization flow before releasing remaining assets.
  request:
    escrowId: uuid
    initiatorServiceId: string        # For initiator_trusted
    notes: string
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    finalizerResults: [               # Results of contract finalizer APIs
      {
        endpoint: string
        success: boolean
        error: string
      }
    ]
    releases: [
      {
        recipientPartyId: uuid
        assets: EscrowAssetBundle
        success: boolean
        error: string
      }
    ]
  errors:
    - ESCROW_NOT_FOUND
    - ESCROW_NOT_RELEASABLE
    - NOT_AUTHORIZED_TO_RELEASE
    - BOUND_CONTRACT_NOT_FULFILLED
    - FINALIZATION_FAILED
    - RELEASE_FAILED

/escrow/refund:
  method: POST
  x-permissions:
    - role: developer
      states: {}
  description: Trigger refund (for trusted modes or consent)
  request:
    escrowId: uuid
    initiatorServiceId: string
    reason: string
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    refunds: [
      {
        depositorPartyId: uuid
        assets: EscrowAssetBundle
        success: boolean
        error: string
      }
    ]
  errors:
    - ESCROW_NOT_FOUND
    - ESCROW_NOT_REFUNDABLE
    - NOT_AUTHORIZED_TO_REFUND
    - REFUND_FAILED

/escrow/cancel:
  method: POST
  x-permissions:
    - role: developer
      states: {}
  description: Cancel escrow before fully funded
  request:
    escrowId: uuid
    reason: string
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    refunds: [
      {
        depositorPartyId: uuid
        assets: EscrowAssetBundle
        success: boolean
        error: string
      }
    ]
  errors:
    - ESCROW_NOT_FOUND
    - ESCROW_ALREADY_FUNDED
    - NOT_AUTHORIZED_TO_CANCEL

/escrow/dispute:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: Raise a dispute on funded escrow
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
    reason: string
    releaseToken: string              # Proves party identity
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
  errors:
    - ESCROW_NOT_FOUND
    - NOT_A_PARTY
    - ESCROW_NOT_DISPUTABLE
    - ALREADY_DISPUTED
```

### 13.5 Arbiter Operations

```yaml
# ══════════════════════════════════════════════════════════════════
# ARBITER OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/resolve:
  method: POST
  x-permissions:
    - role: developer
      states: {}
  description: Arbiter resolves disputed escrow
  request:
    escrowId: uuid
    arbiterId: uuid
    arbiterType: string
    resolution: EscrowResolution
    splitAllocations: [               # For split resolution
      {
        partyId: uuid
        partyType: string
        assets: [EscrowAsset]
      }
    ]
    notes: string
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    transfers: [
      {
        partyId: uuid
        assets: EscrowAssetBundle
        success: boolean
        error: string
      }
    ]
  errors:
    - ESCROW_NOT_FOUND
    - NOT_AN_ARBITER
    - ESCROW_NOT_DISPUTED
    - INVALID_SPLIT_AMOUNTS
    - RESOLUTION_FAILED
```

### 13.6 Condition Verification

```yaml
# ══════════════════════════════════════════════════════════════════
# CONDITIONAL ESCROW (for non-contract conditions)
# ══════════════════════════════════════════════════════════════════

/escrow/verify-condition:
  method: POST
  x-permissions:
    - role: developer
      states: {}
  description: |
    Verify condition for conditional escrow (non-contract path).
    For escrows with boundContractId, use contract milestones instead.
  request:
    escrowId: uuid
    conditionMet: boolean
    verifierId: uuid
    verifierType: string
    verificationData: object          # Proof/evidence
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    triggered: boolean                # Did this trigger release/refund?
  errors:
    - ESCROW_NOT_FOUND
    - ESCROW_NOT_CONDITIONAL
    - ESCROW_HAS_BOUND_CONTRACT
    - NOT_AUTHORIZED_TO_VERIFY
    - ALREADY_VERIFIED
```

### 13.7 Validation

```yaml
# ══════════════════════════════════════════════════════════════════
# VALIDATION OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/validate:
  method: POST
  x-permissions:
    - role: admin
      states: {}
  description: Manually trigger validation on an active escrow
  request:
    escrowId: uuid
  response:
    valid: boolean
    failures: [ValidationFailure]
    escrow: EscrowAgreement

/escrow/reaffirm:
  method: POST
  x-permissions:
    - role: user
      states: {}
  description: Re-affirm after validation failure (party accepts changed state)
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
    releaseToken: string
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    allReaffirmed: boolean            # Did all parties reaffirm? Returns to active state
  errors:
    - ESCROW_NOT_FOUND
    - ESCROW_NOT_VALIDATION_FAILED
    - NOT_A_PARTY
    - ALREADY_REAFFIRMED
```

### 13.8 Asset Handler Registration

```yaml
# ══════════════════════════════════════════════════════════════════
# DYNAMIC ASSET HANDLER REGISTRATION
# ══════════════════════════════════════════════════════════════════

/escrow/handler/register:
  method: POST
  x-permissions:
    - role: admin
      states: {}
  description: Register a custom asset type handler
  request:
    assetType: string
    pluginId: string
    depositEndpoint: string
    releaseEndpoint: string
    refundEndpoint: string
    validateEndpoint: string
  response:
    registered: boolean
  errors:
    - ASSET_TYPE_ALREADY_REGISTERED
    - ASSET_TYPE_RESERVED
    - INVALID_ENDPOINT_CONFIGURATION

/escrow/handler/list:
  method: POST
  x-permissions:
    - role: admin
      states: {}
  description: List registered asset handlers (includes built-in)
  request: {}
  response:
    handlers: [
      {
        assetType: string
        pluginId: string
        builtIn: boolean
        depositEndpoint: string
        releaseEndpoint: string
        refundEndpoint: string
        validateEndpoint: string
      }
    ]

/escrow/handler/deregister:
  method: POST
  x-permissions:
    - role: admin
      states: {}
  description: Remove a custom asset handler registration
  request:
    assetType: string
  response:
    deregistered: boolean
  errors:
    - ASSET_TYPE_NOT_FOUND
    - ASSET_TYPE_RESERVED
    - ACTIVE_ESCROWS_EXIST
```

---

## 14. Events

All events published to lib-messaging.

```yaml
# ══════════════════════════════════════════════════════════════════
# LIFECYCLE EVENTS
# ══════════════════════════════════════════════════════════════════

escrow.created:
  topic: "escrow.created"
  payload:
    escrowId: uuid
    escrowType: string
    trustMode: string
    parties: [{ partyId, partyType, role }]
    expectedDepositCount: integer
    expiresAt: timestamp
    boundContractId: uuid
    referenceType: string
    referenceId: uuid
    createdAt: timestamp

escrow.deposit.received:
  topic: "escrow.deposit.received"
  payload:
    escrowId: uuid
    partyId: uuid
    partyType: string
    depositId: uuid
    assetSummary: string
    depositsReceived: integer
    depositsExpected: integer
    fullyFunded: boolean
    depositedAt: timestamp

escrow.funded:
  topic: "escrow.funded"
  payload:
    escrowId: uuid
    totalDeposits: integer
    fundedAt: timestamp

escrow.consent.received:
  topic: "escrow.consent.received"
  payload:
    escrowId: uuid
    partyId: uuid
    partyType: string
    consentType: string
    consentsReceived: integer
    consentsRequired: integer
    consentedAt: timestamp

escrow.finalizing:
  topic: "escrow.finalizing"
  payload:
    escrowId: uuid
    boundContractId: uuid
    finalizerCount: integer
    startedAt: timestamp

escrow.released:
  topic: "escrow.released"
  payload:
    escrowId: uuid
    recipients: [{ partyId, partyType, assetSummary }]
    resolution: string
    completedAt: timestamp

escrow.refunded:
  topic: "escrow.refunded"
  payload:
    escrowId: uuid
    depositors: [{ partyId, partyType, assetSummary }]
    reason: string
    resolution: string
    completedAt: timestamp

escrow.disputed:
  topic: "escrow.disputed"
  payload:
    escrowId: uuid
    disputedBy: uuid
    disputedByType: string
    reason: string
    disputedAt: timestamp

escrow.resolved:
  topic: "escrow.resolved"
  payload:
    escrowId: uuid
    arbiterId: uuid
    arbiterType: string
    resolution: string
    notes: string
    resolvedAt: timestamp

escrow.expired:
  topic: "escrow.expired"
  payload:
    escrowId: uuid
    status: string
    autoRefunded: boolean
    expiredAt: timestamp

escrow.cancelled:
  topic: "escrow.cancelled"
  payload:
    escrowId: uuid
    cancelledBy: uuid
    cancelledByType: string
    reason: string
    depositsRefunded: integer
    cancelledAt: timestamp

# ══════════════════════════════════════════════════════════════════
# VALIDATION EVENTS
# ══════════════════════════════════════════════════════════════════

escrow.validation.failed:
  topic: "escrow.validation.failed"
  payload:
    escrowId: uuid
    failures: [{ assetType, failureType, affectedPartyId, details }]
    detectedAt: timestamp

escrow.validation.reaffirmed:
  topic: "escrow.validation.reaffirmed"
  payload:
    escrowId: uuid
    reaffirmedBy: uuid
    reaffirmedByType: string
    allReaffirmed: boolean
    reaffirmedAt: timestamp
```

---

## 15. State Stores

```yaml
# ══════════════════════════════════════════════════════════════════
# REDIS (Hot Data)
# ══════════════════════════════════════════════════════════════════

escrow-tokens:
  backend: redis
  prefix: "escrow:token"
  key_pattern: "{tokenHash}"
  purpose: Token validation (hashed tokens → escrow/party info)
  ttl: 2592000  # 30 days (max escrow lifetime)

escrow-idempotency:
  backend: redis
  prefix: "escrow:idemp"
  key_pattern: "{idempotencyKey}"
  purpose: Idempotency key deduplication
  ttl: 86400  # 24 hours

escrow-party-pending:
  backend: redis
  prefix: "escrow:pending"
  key_pattern: "{partyId}:{partyType}"
  purpose: Count pending escrows per party for limits
  type: counter

escrow-status-index:
  backend: redis
  prefix: "escrow:status"
  key_pattern: "{status}"
  purpose: Set of escrow IDs by status for expiration/validation processing
  type: sorted_set
  score: expiresAt

escrow-active-validation:
  backend: redis
  prefix: "escrow:validate"
  key_pattern: "{escrowId}"
  purpose: Track active escrows requiring periodic validation
  type: set

# ══════════════════════════════════════════════════════════════════
# MYSQL (Persistent)
# ══════════════════════════════════════════════════════════════════

escrow-agreements:
  backend: mysql
  table: escrow_agreements
  indexes:
    - status, expiresAt
    - referenceType, referenceId
    - createdBy, createdByType
    - boundContractId

escrow-parties:
  backend: mysql
  table: escrow_parties
  indexes:
    - escrowId
    - partyId, partyType

escrow-expected-deposits:
  backend: mysql
  table: escrow_expected_deposits
  indexes:
    - escrowId
    - partyId, partyType

escrow-deposits:
  backend: mysql
  table: escrow_deposits
  indexes:
    - escrowId
    - partyId, partyType
    - idempotencyKey (unique)

escrow-consents:
  backend: mysql
  table: escrow_consents
  indexes:
    - escrowId
    - partyId, partyType

escrow-validation-failures:
  backend: mysql
  table: escrow_validation_failures
  indexes:
    - escrowId
    - detectedAt

escrow-handler-registry:
  backend: mysql
  table: escrow_handler_registry
  indexes:
    - assetType (unique)
```

---

## 16. Error Codes

```yaml
# ══════════════════════════════════════════════════════════════════
# CREATION ERRORS
# ══════════════════════════════════════════════════════════════════

INVALID_PARTY_CONFIGURATION:
  message: "Invalid party configuration"
  details: { issue }

INVALID_TRUST_CONFIGURATION:
  message: "Invalid trust mode configuration"
  details: { trustMode, issue }

TOO_MANY_PARTIES:
  message: "Exceeds maximum parties per escrow"
  details: { provided, max }

INVALID_EXPECTED_DEPOSITS:
  message: "Invalid expected deposit configuration"
  details: { issue }

EXPIRES_TOO_SOON:
  message: "Expiration time is too soon"
  details: { provided, minimum }

EXPIRES_TOO_LATE:
  message: "Expiration time exceeds maximum"
  details: { provided, maximum }

MAX_PENDING_ESCROWS_EXCEEDED:
  message: "Party has too many pending escrows"
  details: { partyId, current, max }

WALLET_CREATION_FAILED:
  message: "Failed to create escrow wallet"
  details: { error }

CONTAINER_CREATION_FAILED:
  message: "Failed to create escrow container"
  details: { error }

BOUND_CONTRACT_NOT_FOUND:
  message: "Bound contract not found"
  details: { contractId }

# ══════════════════════════════════════════════════════════════════
# DEPOSIT ERRORS
# ══════════════════════════════════════════════════════════════════

NOT_A_DEPOSITOR:
  message: "Party is not a depositor in this escrow"
  details: { partyId, escrowId }

ESCROW_NOT_ACCEPTING_DEPOSITS:
  message: "Escrow is not accepting deposits"
  details: { escrowId, currentStatus }

INVALID_DEPOSIT_TOKEN:
  message: "Invalid or expired deposit token"
  details: { escrowId }

DEPOSIT_TOKEN_ALREADY_USED:
  message: "Deposit token has already been used"
  details: { escrowId, usedAt }

ASSET_DEPOSIT_REJECTED:
  message: "Asset cannot be deposited into escrow"
  details: { assetType, reason }

DEPOSIT_EXCEEDS_EXPECTED:
  message: "Deposit exceeds expected amount"
  details: { deposited, expected }

DEPOSIT_ASSETS_MISMATCH:
  message: "Deposit assets do not match expected"
  details: { expected, provided }

INSUFFICIENT_BALANCE:
  message: "Insufficient currency balance for deposit"
  details: { walletId, currencyId, available, required }

ITEM_NOT_FOUND:
  message: "Item instance not found in depositor's container"
  details: { itemInstanceId, containerId }

ITEM_SOULBOUND:
  message: "Soulbound items cannot be deposited into escrow"
  details: { itemInstanceId, templateCode }

CONTRACT_NOT_FOUND:
  message: "Contract instance not found"
  details: { contractInstanceId }

CONTRACT_NOT_TRANSFERABLE:
  message: "Contract template does not allow transfers"
  details: { contractInstanceId, templateCode }

TRANSFER_FAILED:
  message: "Asset transfer to escrow failed"
  details: { assetType, error }

# ══════════════════════════════════════════════════════════════════
# CONSENT ERRORS
# ══════════════════════════════════════════════════════════════════

NOT_A_PARTY:
  message: "Entity is not a party in this escrow"
  details: { partyId, escrowId }

ESCROW_NOT_AWAITING_CONSENT:
  message: "Escrow is not awaiting consent"
  details: { escrowId, currentStatus }

INVALID_RELEASE_TOKEN:
  message: "Invalid or expired release token"
  details: { escrowId }

RELEASE_TOKEN_ALREADY_USED:
  message: "Release token has already been used"
  details: { escrowId, usedAt }

ALREADY_CONSENTED:
  message: "Party has already given consent"
  details: { partyId, consentType, consentedAt }

CONSENT_NOT_REQUIRED:
  message: "Consent is not required from this party"
  details: { partyId }

BOUND_CONTRACT_NOT_FULFILLED:
  message: "Bound contract is not in fulfilled status"
  details: { contractId, currentStatus }

# ══════════════════════════════════════════════════════════════════
# COMPLETION ERRORS
# ══════════════════════════════════════════════════════════════════

ESCROW_NOT_RELEASABLE:
  message: "Escrow cannot be released in current state"
  details: { escrowId, currentStatus, requiredConsents, receivedConsents }

NOT_AUTHORIZED_TO_RELEASE:
  message: "Not authorized to release this escrow"
  details: { escrowId, trustMode }

FINALIZATION_FAILED:
  message: "Contract finalizer API execution failed"
  details: { escrowId, failedEndpoint, error }

RELEASE_FAILED:
  message: "Failed to release escrow assets"
  details: { escrowId, failedTransfers }

ESCROW_NOT_REFUNDABLE:
  message: "Escrow cannot be refunded in current state"
  details: { escrowId, currentStatus }

NOT_AUTHORIZED_TO_REFUND:
  message: "Not authorized to refund this escrow"
  details: { escrowId, trustMode }

REFUND_FAILED:
  message: "Failed to refund escrow assets"
  details: { escrowId, failedRefunds }

ESCROW_ALREADY_FUNDED:
  message: "Cannot cancel fully funded escrow"
  details: { escrowId, fundedAt }

NOT_AUTHORIZED_TO_CANCEL:
  message: "Not authorized to cancel this escrow"
  details: { escrowId }

# ══════════════════════════════════════════════════════════════════
# DISPUTE ERRORS
# ══════════════════════════════════════════════════════════════════

ESCROW_NOT_DISPUTABLE:
  message: "Escrow cannot be disputed in current state"
  details: { escrowId, currentStatus }

ALREADY_DISPUTED:
  message: "Escrow is already in disputed state"
  details: { escrowId, disputedAt }

NOT_AN_ARBITER:
  message: "Entity is not an arbiter in this escrow"
  details: { partyId, escrowId }

ESCROW_NOT_DISPUTED:
  message: "Escrow is not in disputed state"
  details: { escrowId, currentStatus }

INVALID_SPLIT_AMOUNTS:
  message: "Split amounts do not equal total escrowed"
  details: { total, splitSum }

RESOLUTION_FAILED:
  message: "Failed to execute resolution"
  details: { escrowId, failedOperations }

# ══════════════════════════════════════════════════════════════════
# CONDITIONAL ERRORS
# ══════════════════════════════════════════════════════════════════

ESCROW_NOT_CONDITIONAL:
  message: "Escrow is not a conditional type"
  details: { escrowId, escrowType }

ESCROW_HAS_BOUND_CONTRACT:
  message: "Cannot manually verify condition on contract-bound escrow"
  details: { escrowId, boundContractId }

NOT_AUTHORIZED_TO_VERIFY:
  message: "Not authorized to verify condition"
  details: { escrowId }

ALREADY_VERIFIED:
  message: "Condition has already been verified"
  details: { escrowId, verifiedAt }

# ══════════════════════════════════════════════════════════════════
# VALIDATION ERRORS
# ══════════════════════════════════════════════════════════════════

ESCROW_NOT_VALIDATION_FAILED:
  message: "Escrow is not in validation_failed state"
  details: { escrowId, currentStatus }

ALREADY_REAFFIRMED:
  message: "Party has already re-affirmed"
  details: { partyId, reaffirmedAt }

# ══════════════════════════════════════════════════════════════════
# HANDLER ERRORS
# ══════════════════════════════════════════════════════════════════

ASSET_TYPE_ALREADY_REGISTERED:
  message: "Asset type handler already registered"
  details: { assetType, existingPluginId }

ASSET_TYPE_RESERVED:
  message: "Cannot register/deregister built-in asset type"
  details: { assetType }

INVALID_ENDPOINT_CONFIGURATION:
  message: "Handler endpoint configuration is invalid"
  details: { endpoint, issue }

ASSET_TYPE_NOT_FOUND:
  message: "No handler registered for asset type"
  details: { assetType }

ACTIVE_ESCROWS_EXIST:
  message: "Cannot deregister handler with active escrows using this type"
  details: { assetType, activeCount }

ASSET_HANDLER_NOT_FOUND:
  message: "No handler registered for asset type"
  details: { assetType }
```

---

## 17. Integration Examples

### 17.1 Simple Player Trade (Currency for Item)

```yaml
# Player A has 500 gold, wants Player B's Iron Sword
escrow:
  escrowType: two_party
  trustMode: full_consent
  parties:
    - { partyId: player_a, partyType: character, role: depositor_recipient, walletId: wallet_a, containerId: inv_a }
    - { partyId: player_b, partyType: character, role: depositor_recipient, walletId: wallet_b, containerId: inv_b }
  expectedDeposits:
    - partyId: player_a
      expectedAssets:
        - { assetType: currency, currencyCode: gold, currencyAmount: 500 }
    - partyId: player_b
      expectedAssets:
        - { assetType: item, itemInstanceId: iron_sword_123 }
  releaseAllocations:
    - recipientPartyId: player_a
      assets:
        - { assetType: item, itemInstanceId: iron_sword_123 }
    - recipientPartyId: player_b
      assets:
        - { assetType: currency, currencyCode: gold, currencyAmount: 500 }
```

### 17.2 Sequence: Two-Party Trade

```
Player A              lib-escrow           lib-currency        lib-inventory
    │                      │                     │                    │
    │── create trade ─────►│                     │                    │
    │                      │── create wallet ───►│ (ownerType=escrow) │
    │                      │◄── walletId ────────│                    │
    │                      │── create container ─────────────────────►│ (ownerType=escrow)
    │                      │◄── containerId ─────────────────────────│
    │◄── depositTokens ───│                     │                    │
    │                      │                     │                    │
    │── deposit 500g ─────►│                     │                    │
    │                      │── transfer ────────►│ (A wallet → escrow wallet)
    │                      │◄── success ─────────│                    │
    │◄── deposit ok ───────│                     │                    │
    │                      │                     │                    │
    │                 Player B deposits sword    │                    │
    │                      │                     │                    │
    │                      │── transfer ─────────────────────────────►│ (B inv → escrow inv)
    │                      │◄── success ─────────────────────────────│
    │                      │                     │                    │
    │◄── fullyFunded ──────│ (releaseTokens)    │                    │
    │                      │                     │                    │
    │── consent release ──►│                     │                    │
    │                      │                     │                    │
    │                 Player B consents          │                    │
    │                      │                     │                    │
    │                      │── transfer ────────►│ (escrow wallet → B wallet)
    │                      │◄── success ─────────│                    │
    │                      │── transfer ─────────────────────────────►│ (escrow inv → A inv)
    │                      │◄── success ─────────────────────────────│
    │                      │── close wallet ────►│                    │
    │                      │── delete container ─────────────────────►│
    │                      │                     │                    │
    │◄── released ─────────│                     │                    │
```

### 17.3 Contract-Bound Escrow with Fees (Property Sale)

```yaml
# Seller has a land deed (contract). Buyer pays 10000 gold.
# Broker takes 5% fee from escrow on completion.

# Step 1: Contract created with escrow terms
contract:
  templateCode: "property_sale"
  milestones:
    - code: "buyer_deposits_payment"
    - code: "seller_deposits_deed"
    - code: "inspection_period"  # 3 days
  onFulfill:
    # Broker fee: 500 gold from escrow to broker's wallet
    - serviceName: "currency"
      endpoint: "/currency/transfer"
      payloadTemplate: |
        {
          "source_wallet_id": "{{EscrowWalletId}}",
          "target_wallet_id": "{{contract.terms.broker_wallet_id}}",
          "currency_id": "{{contract.terms.currency_id}}",
          "amount": 500,
          "reference_type": "escrow_fee"
        }

# Step 2: Escrow created with bound contract
escrow:
  escrowType: conditional
  trustMode: full_consent
  boundContractId: contract_123
  parties:
    - { partyId: buyer, role: depositor_recipient, walletId: buyer_wallet }
    - { partyId: seller, role: depositor_recipient, walletId: seller_wallet }
  expectedDeposits:
    - partyId: buyer
      expectedAssets:
        - { assetType: currency, currencyCode: gold, currencyAmount: 10000 }
    - partyId: seller
      expectedAssets:
        - { assetType: contract, contractInstanceId: land_deed_456 }
  releaseAllocations:
    - recipientPartyId: buyer
      assets:
        - { assetType: contract, contractInstanceId: land_deed_456 }
    - recipientPartyId: seller
      assets:
        # Seller gets 9500 (10000 - 500 broker fee, taken during finalization)
        - { assetType: currency, currencyCode: gold, currencyAmount: 9500 }
```

**Flow:**
1. Escrow created → wallet + container created
2. Buyer deposits 10000 gold → transferred to escrow wallet
3. Seller deposits land deed → contract locked under escrow guardianship
4. All milestones complete → `contract.fulfilled` event received
5. **Finalization**: Broker fee (500g) transferred from escrow wallet to broker
6. **Release**: Remaining 9500g to seller, deed transferred to buyer
7. Cleanup: wallet closed, container deleted

### 17.4 Validation Failure Example

```
Escrow holds: 3x Health Potion (expires in 24h), 500 gold

After 24h:
  1. Validation check runs
  2. Health Potions no longer exist in escrow container (expired)
  3. Status → validation_failed
  4. Event: escrow.validation.failed
  5. Both parties notified

Resolution options:
  A) Both parties reaffirm (accept: "trade 500g for nothing" now)
  B) Affected depositor is in violation, other party refunds their own deposit
```

### 17.5 Custom Asset Type: Guild Ownership

```yaml
# Register guild ownership handler
/escrow/handler/register:
  assetType: "guild_ownership"
  pluginId: "lib-guild"
  depositEndpoint: "/guild/escrow/deposit"
  releaseEndpoint: "/guild/escrow/release"
  refundEndpoint: "/guild/escrow/refund"
  validateEndpoint: "/guild/escrow/validate"

# Use in escrow
escrow:
  expectedDeposits:
    - partyId: guild_leader
      expectedAssets:
        - assetType: custom
          customAssetType: "guild_ownership"
          customAssetId: guild_123
          customAssetData: { guildName: "Iron Hawks" }
```

---

## Appendix A: Contract Lock/Unlock Requirements

For `assetType=contract` to work, lib-contract needs these capabilities (to be added to contract schema):

```yaml
/contract/lock:
  method: POST
  access: developer
  description: Lock a contract under guardian custody (prevents modification/termination)
  request:
    contractInstanceId: uuid
    guardianId: uuid
    guardianType: string              # "escrow"
    idempotencyKey: string
  response:
    locked: boolean
  errors:
    - CONTRACT_NOT_FOUND
    - CONTRACT_NOT_TRANSFERABLE
    - CONTRACT_ALREADY_LOCKED

/contract/unlock:
  method: POST
  access: developer
  description: Unlock a contract from guardian custody
  request:
    contractInstanceId: uuid
    guardianId: uuid
    guardianType: string
    idempotencyKey: string
  response:
    unlocked: boolean
  errors:
    - CONTRACT_NOT_FOUND
    - CONTRACT_NOT_LOCKED
    - NOT_GUARDIAN

/contract/transfer-party:
  method: POST
  access: developer
  description: Transfer a party role to a new entity (for escrow release)
  request:
    contractInstanceId: uuid
    fromEntityId: uuid
    fromEntityType: string
    toEntityId: uuid
    toEntityType: string
    guardianId: uuid                  # Must be current guardian to transfer locked contracts
    guardianType: string
    idempotencyKey: string
  response:
    transferred: boolean
  errors:
    - CONTRACT_NOT_FOUND
    - PARTY_NOT_FOUND
    - CONTRACT_NOT_TRANSFERABLE
    - NOT_GUARDIAN
```

These endpoints will need to be added to `schemas/contract-api.yaml` before lib-escrow can hold contract assets.

---

*This specification is implementation-ready. lib-currency and lib-inventory require no changes (escrow uses their existing transfer APIs). lib-contract requires the lock/unlock/transfer-party endpoints defined in Appendix A.*
