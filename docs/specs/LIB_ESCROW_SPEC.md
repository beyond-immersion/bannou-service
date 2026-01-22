# lib-escrow Service Specification

> **Version**: 1.0.0
> **Status**: Implementation Ready
> **Created**: 2026-01-22
> **Dependencies**: lib-state, lib-messaging
> **Dependent Plugins**: lib-currency, lib-inventory (when implemented)

This document is the implementation-ready specification for the `lib-escrow` service. It provides a generic multi-party escrow system that can hold currency, items, or both.

---

## Table of Contents

1. [Overview](#overview)
2. [Plugin Configuration](#plugin-configuration)
3. [Core Concepts](#core-concepts)
4. [Asset Types](#asset-types)
5. [Escrow Agreements](#escrow-agreements)
6. [Parties and Roles](#parties-and-roles)
7. [Trust Modes](#trust-modes)
8. [Lifecycle and State Machine](#lifecycle-and-state-machine)
9. [API Endpoints](#api-endpoints)
10. [Events](#events)
11. [State Stores](#state-stores)
12. [Error Codes](#error-codes)
13. [Integration Patterns](#integration-patterns)

---

## 1. Overview

lib-escrow provides a generic, multi-party escrow system that can hold any combination of assets:

- **Currency amounts** - Via integration with lib-currency
- **Item instances** - Via integration with lib-inventory (when implemented)
- **Mixed deposits** - Currency AND items in the same escrow

### Core Features

- **Flexible trust modes**: From full multi-party consent to trusted arbiters
- **Token-based consent**: Cryptographic tokens prevent unauthorized actions
- **Multi-party support**: 2-party trades, N-party deals, auction settlements
- **Asset agnostic**: Works with any lockable/transferable asset type
- **Resolution options**: Release, refund, split, arbiter-decided

### What lib-escrow Does NOT Do

- Manage currencies (lib-currency)
- Manage inventories (lib-inventory)
- Provide trade UI/UX (game layer)
- Market/auction mechanics (lib-market)

### Plugin Dependency Pattern

```
┌─────────────────────────────────────────┐
│           Game / Application            │
└─────────────────────────────────────────┘
                    │
         ┌──────────┼──────────┐
         ▼          ▼          ▼
    lib-market  lib-trade  lib-quest
         │          │          │
         └──────────┼──────────┘
                    │
         ┌──────────┴──────────┐
         ▼                     ▼
   lib-currency          lib-inventory
         │                     │
         └──────────┬──────────┘
                    │
                    ▼
              lib-escrow  ◄─── You are here
                    │
         ┌──────────┴──────────┐
         ▼                     ▼
     lib-state          lib-messaging
```

lib-escrow sits BELOW lib-currency and lib-inventory, providing the foundational escrow mechanics that those plugins use to implement their locking/holding operations.

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

### 3.1 Asset-Agnostic Design

lib-escrow doesn't know what currencies or items ARE - it only knows how to hold "asset references" and orchestrate multi-party agreements. The asset-owning plugins (lib-currency, lib-inventory) implement the actual lock/unlock/transfer operations.

```
Escrow creates agreement with asset requirements
         │
         ▼
Party deposits asset reference
         │
         ▼
Escrow calls asset plugin to LOCK the asset
         │
         ▼
[...escrow lifecycle...]
         │
         ▼
Escrow resolves → calls asset plugin to TRANSFER or UNLOCK
```

### 3.2 Pull vs Push Model

lib-escrow uses a **callback/integration pattern** where asset plugins register handlers:

```yaml
AssetHandlerRegistration:
  assetType: string          # "currency" | "item" | custom
  lockHandler: delegate      # Called to lock assets on deposit
  unlockHandler: delegate    # Called to unlock assets on refund
  transferHandler: delegate  # Called to transfer assets on release
```

### 3.3 Idempotency

All mutating operations require idempotency keys. lib-escrow maintains its own idempotency cache separate from asset plugins to prevent duplicate escrow operations.

---

## 4. Asset Types

Assets are represented as polymorphic references that the owning plugin can interpret.

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
        description: "Currency amount from lib-currency"
    - item:
        description: "Item instance from lib-inventory"
    - item_stack:
        description: "Stackable items (quantity from template)"

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
  # SOURCE TRACKING
  # ═══════════════════════════════════════════════════════════════

  # Where this asset came from (for refunds)
  sourceOwnerId: uuid
  sourceOwnerType: string

  # For currency: wallet ID
  # For items: inventory ID or container ID
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

The central entity tracking a multi-party escrow.

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
        description: "Release based on external condition verification"
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
  # STATUS
  # ═══════════════════════════════════════════════════════════════

  status: EscrowStatus
  values:
    - pending_deposits:
        description: "Waiting for parties to deposit"
    - partially_funded:
        description: "Some but not all deposits received"
    - funded:
        description: "All deposits received, awaiting consent"
    - pending_consent:
        description: "Some consents received, waiting for more"
    - pending_condition:
        description: "For conditional: waiting for external verification"
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

  # Lock references (for unlock on refund)
  assetLockReferences: [AssetLockReference]
```

```yaml
# Schema: AssetLockReference
# Tracks locks placed on assets for refund/release
AssetLockReference:
  assetType: string
  lockId: uuid

  # Plugin-specific lock reference
  pluginLockData: object
  nullable: true
```

```yaml
# Schema: ReleaseAllocation
# Defines who gets what on release
ReleaseAllocation:
  recipientPartyId: uuid
  recipientPartyType: string

  # Which assets this recipient should receive
  assets: [EscrowAsset]

  # Where to deliver (wallet ID, inventory ID, etc.)
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

  consentedAt: timestamp

  # Token used (for audit)
  releaseTokenUsed: string
  nullable: true

  notes: string
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
5. When requiredConsentsForRelease reached → assets released

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
3. Service calls complete/refund directly with service credentials

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
                                           │
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
              ┌────────────────────────────┼────────────────────────────┐
              │                            │                            │
              ▼                            ▼                            ▼
       ┌────────────┐              ┌─────────────┐              ┌────────────┐
       │  DISPUTED  │              │   PENDING   │              │  PENDING   │
       │            │◄─────────────│   CONSENT   │              │ CONDITION  │
       └──────┬─────┘              └──────┬──────┘              └──────┬─────┘
              │                           │                            │
              │    ┌──────────────────────┼──────────────────────┐     │
              │    │                      │                      │     │
              ▼    ▼                      ▼                      ▼     ▼
       ┌────────────┐              ┌─────────────┐              ┌────────────┐
       │ REFUNDED   │              │  RELEASING  │              │ EXPIRED    │
       │            │              │ (transient) │              │ (refunded) │
       └────────────┘              └──────┬──────┘              └────────────┘
                                          │
                                          ▼
                                   ┌─────────────┐
                                   │  RELEASED   │
                                   └─────────────┘
```

### State Transitions

| From | To | Trigger |
|------|-----|---------|
| created | pending_deposits | Initial state |
| pending_deposits | partially_funded | First deposit received |
| pending_deposits | cancelled | Cancel before deposits |
| pending_deposits | expired | Timeout |
| partially_funded | funded | All expected deposits received |
| partially_funded | cancelled | Cancel (refunds partial) |
| partially_funded | expired | Timeout (refunds partial) |
| funded | pending_consent | Awaiting party consents |
| funded | pending_condition | For conditional type |
| funded | disputed | Party raises dispute |
| pending_consent | released | Required consents reached |
| pending_consent | refunded | All consent to refund |
| pending_consent | disputed | Party raises dispute |
| pending_consent | expired | Timeout |
| pending_condition | released | Condition verified |
| pending_condition | refunded | Condition failed |
| pending_condition | expired | Timeout |
| disputed | released | Arbiter releases |
| disputed | refunded | Arbiter refunds |
| disputed | split | Arbiter splits |

---

## 9. API Endpoints

### 9.1 Escrow Lifecycle

```yaml
# ══════════════════════════════════════════════════════════════════
# ESCROW CREATION
# ══════════════════════════════════════════════════════════════════

/escrow/create:
  method: POST
  access: authenticated
  description: Create a new escrow agreement
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
    releaseAllocations: [              # Optional, can derive from deposits
      {
        recipientPartyId: uuid
        recipientPartyType: string
        assets: [EscrowAsset]
        destinationContainerId: uuid
      }
    ]
    requiredConsentsForRelease: integer
    expiresAt: timestamp               # Optional, uses default
    referenceType: string
    referenceId: uuid
    description: string
    metadata: object
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    depositTokens: [                   # For full_consent mode
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

/escrow/get:
  method: POST
  access: authenticated
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
  access: user
  description: List escrows for a party
  request:
    partyId: uuid
    partyType: string
    status: [EscrowStatus]             # Filter by status
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

### 9.2 Deposits

```yaml
# ══════════════════════════════════════════════════════════════════
# DEPOSIT OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/deposit:
  method: POST
  access: authenticated
  description: Deposit assets into escrow
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
    assets: EscrowAssetBundle
    depositToken: string               # Required for full_consent
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    deposit: EscrowDeposit
    fullyFunded: boolean
    releaseTokens: [                   # Issued when fully funded
      { partyId: uuid, partyType: string, releaseToken: string }
    ]
  errors:
    - ESCROW_NOT_FOUND
    - NOT_A_DEPOSITOR
    - ESCROW_NOT_ACCEPTING_DEPOSITS
    - INVALID_DEPOSIT_TOKEN
    - DEPOSIT_TOKEN_ALREADY_USED
    - ASSET_LOCK_FAILED
    - DEPOSIT_EXCEEDS_EXPECTED
    - DEPOSIT_ASSETS_MISMATCH

/escrow/deposit/validate:
  method: POST
  access: authenticated
  description: Validate a deposit without executing
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
  access: authenticated
  description: Get deposit status for a party
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
  response:
    expectedAssets: [EscrowAsset]
    depositedAssets: [EscrowAsset]
    fulfilled: boolean
    depositToken: string               # If not yet used
    depositDeadline: timestamp
```

### 9.3 Consent and Resolution

```yaml
# ══════════════════════════════════════════════════════════════════
# CONSENT OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/consent:
  method: POST
  access: authenticated
  description: Record party consent for release or refund
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
    consentType: EscrowConsentType
    releaseToken: string               # Required for full_consent
    notes: string
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    consentRecorded: boolean
    triggered: boolean                 # Did this consent trigger completion?
    newStatus: EscrowStatus
  errors:
    - ESCROW_NOT_FOUND
    - NOT_A_PARTY
    - ESCROW_NOT_AWAITING_CONSENT
    - INVALID_RELEASE_TOKEN
    - RELEASE_TOKEN_ALREADY_USED
    - ALREADY_CONSENTED
    - CONSENT_NOT_REQUIRED

/escrow/consent/status:
  method: POST
  access: authenticated
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

### 9.4 Completion

```yaml
# ══════════════════════════════════════════════════════════════════
# COMPLETION OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/release:
  method: POST
  access: authenticated
  description: Force release (for trusted modes or after consent)
  request:
    escrowId: uuid
    initiatorServiceId: string         # For initiator_trusted
    notes: string
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
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
    - RELEASE_FAILED

/escrow/refund:
  method: POST
  access: authenticated
  description: Force refund (for trusted modes or consent)
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
  access: authenticated
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
  access: authenticated
  description: Raise a dispute on funded escrow
  request:
    escrowId: uuid
    partyId: uuid
    partyType: string
    reason: string
    releaseToken: string               # Proves party identity
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
  errors:
    - ESCROW_NOT_FOUND
    - NOT_A_PARTY
    - ESCROW_NOT_DISPUTABLE
    - ALREADY_DISPUTED
```

### 9.5 Arbiter Operations

```yaml
# ══════════════════════════════════════════════════════════════════
# ARBITER OPERATIONS
# ══════════════════════════════════════════════════════════════════

/escrow/resolve:
  method: POST
  access: authenticated
  description: Arbiter resolves disputed escrow
  request:
    escrowId: uuid
    arbiterId: uuid
    arbiterType: string
    resolution: EscrowResolution
    splitAllocations: [                # For split resolution
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

### 9.6 Condition Verification

```yaml
# ══════════════════════════════════════════════════════════════════
# CONDITIONAL ESCROW
# ══════════════════════════════════════════════════════════════════

/escrow/verify-condition:
  method: POST
  access: authenticated
  description: Verify condition for conditional escrow
  request:
    escrowId: uuid
    conditionMet: boolean
    verifierId: uuid
    verifierType: string
    verificationData: object           # Proof/evidence
    idempotencyKey: string
  response:
    escrow: EscrowAgreement
    triggered: boolean                 # Did this trigger release/refund?
  errors:
    - ESCROW_NOT_FOUND
    - ESCROW_NOT_CONDITIONAL
    - NOT_AUTHORIZED_TO_VERIFY
    - ALREADY_VERIFIED
```

### 9.7 Asset Handler Registration

```yaml
# ══════════════════════════════════════════════════════════════════
# ASSET HANDLER REGISTRATION (Internal)
# ══════════════════════════════════════════════════════════════════

/escrow/handler/register:
  method: POST
  access: admin
  description: Register asset type handler
  request:
    assetType: string
    pluginId: string
    lockEndpoint: string               # Called to lock assets
    unlockEndpoint: string             # Called to unlock assets
    transferEndpoint: string           # Called to transfer assets
  response:
    registered: boolean
  errors:
    - ASSET_TYPE_ALREADY_REGISTERED

/escrow/handler/list:
  method: POST
  access: admin
  description: List registered asset handlers
  request: {}
  response:
    handlers: [
      {
        assetType: string
        pluginId: string
        lockEndpoint: string
        unlockEndpoint: string
        transferEndpoint: string
      }
    ]
```

---

## 10. Events

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
    referenceType: string
    referenceId: uuid
    createdAt: timestamp

escrow.deposit_received:
  topic: "escrow.deposit.received"
  payload:
    escrowId: uuid
    partyId: uuid
    partyType: string
    depositId: uuid
    assetSummary: string              # Human-readable summary
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

escrow.consent_received:
  topic: "escrow.consent.received"
  payload:
    escrowId: uuid
    partyId: uuid
    partyType: string
    consentType: string
    consentsReceived: integer
    consentsRequired: integer
    consentedAt: timestamp

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
    reason: string                    # consent | expired | cancelled | disputed
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
    status: string                    # Status when expired
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
# ASSET OPERATION EVENTS
# ══════════════════════════════════════════════════════════════════

escrow.asset.locked:
  topic: "escrow.asset.locked"
  payload:
    escrowId: uuid
    depositId: uuid
    assetType: string
    assetSummary: string
    lockId: uuid
    lockedAt: timestamp

escrow.asset.unlocked:
  topic: "escrow.asset.unlocked"
  payload:
    escrowId: uuid
    assetType: string
    assetSummary: string
    lockId: uuid
    reason: string                    # refund | cancel | expired
    unlockedAt: timestamp

escrow.asset.transferred:
  topic: "escrow.asset.transferred"
  payload:
    escrowId: uuid
    fromPartyId: uuid
    fromPartyType: string
    toPartyId: uuid
    toPartyType: string
    assetType: string
    assetSummary: string
    transferredAt: timestamp
```

---

## 11. State Stores

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
  purpose: Set of escrow IDs by status for expiration processing
  type: sorted_set
  score: expiresAt

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

escrow-asset-locks:
  backend: mysql
  table: escrow_asset_locks
  indexes:
    - escrowId
    - depositId
    - lockId (unique)

escrow-handler-registry:
  backend: mysql
  table: escrow_handler_registry
  indexes:
    - assetType (unique)
```

---

## 12. Error Codes

```yaml
# ══════════════════════════════════════════════════════════════════
# ESCROW ERRORS
# ══════════════════════════════════════════════════════════════════

ESCROW_NOT_FOUND:
  message: "Escrow agreement not found"
  details: { escrowId }

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

ASSET_LOCK_FAILED:
  message: "Failed to lock asset for escrow"
  details: { assetType, reason }

DEPOSIT_EXCEEDS_EXPECTED:
  message: "Deposit exceeds expected amount"
  details: { deposited, expected }

DEPOSIT_ASSETS_MISMATCH:
  message: "Deposit assets do not match expected"
  details: { expected, provided }

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

# ══════════════════════════════════════════════════════════════════
# COMPLETION ERRORS
# ══════════════════════════════════════════════════════════════════

ESCROW_NOT_RELEASABLE:
  message: "Escrow cannot be released in current state"
  details: { escrowId, currentStatus, requiredConsents, receivedConsents }

NOT_AUTHORIZED_TO_RELEASE:
  message: "Not authorized to release this escrow"
  details: { escrowId, trustMode }

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

NOT_AUTHORIZED_TO_VERIFY:
  message: "Not authorized to verify condition"
  details: { escrowId }

ALREADY_VERIFIED:
  message: "Condition has already been verified"
  details: { escrowId, verifiedAt }

# ══════════════════════════════════════════════════════════════════
# HANDLER ERRORS
# ══════════════════════════════════════════════════════════════════

ASSET_TYPE_ALREADY_REGISTERED:
  message: "Asset type handler already registered"
  details: { assetType, existingPluginId }

ASSET_HANDLER_NOT_FOUND:
  message: "No handler registered for asset type"
  details: { assetType }
```

---

## 13. Integration Patterns

### 13.1 lib-currency Integration

lib-currency registers as an asset handler and implements lock/unlock/transfer:

```yaml
# lib-currency registers on startup
assetType: "currency"
pluginId: "lib-currency"

# Lock handler (called on deposit)
lockEndpoint: "/currency/escrow/lock"
request:
  walletId: uuid
  currencyDefinitionId: uuid
  amount: decimal
  escrowId: uuid
  idempotencyKey: string
response:
  lockId: uuid
  success: boolean

# Unlock handler (called on refund/cancel)
unlockEndpoint: "/currency/escrow/unlock"
request:
  lockId: uuid
  escrowId: uuid
  idempotencyKey: string
response:
  success: boolean

# Transfer handler (called on release)
transferEndpoint: "/currency/escrow/transfer"
request:
  lockId: uuid
  targetWalletId: uuid
  escrowId: uuid
  idempotencyKey: string
response:
  transactionId: uuid
  success: boolean
```

### 13.2 lib-inventory Integration (Future)

```yaml
assetType: "item"
pluginId: "lib-inventory"

lockEndpoint: "/inventory/escrow/lock"
unlockEndpoint: "/inventory/escrow/unlock"
transferEndpoint: "/inventory/escrow/transfer"

# Similar patterns but with item-specific fields
```

### 13.3 Mixed Escrow Example

A player trade involving both currency and items:

```yaml
# Create escrow with mixed deposits
escrow:
  escrowType: two_party
  trustMode: full_consent
  parties:
    - { partyId: player_a, role: depositor_recipient }
    - { partyId: player_b, role: depositor_recipient }
  expectedDeposits:
    - partyId: player_a
      expectedAssets:
        - { assetType: currency, currencyCode: gold, currencyAmount: 5000 }
    - partyId: player_b
      expectedAssets:
        - { assetType: item, itemInstanceId: legendary_sword_123 }
        - { assetType: item_stack, itemTemplateId: health_potion, itemQuantity: 10 }
  releaseAllocations:
    - recipientPartyId: player_a
      assets:
        - { assetType: item, itemInstanceId: legendary_sword_123 }
        - { assetType: item_stack, itemTemplateId: health_potion, itemQuantity: 10 }
    - recipientPartyId: player_b
      assets:
        - { assetType: currency, currencyCode: gold, currencyAmount: 5000 }
```

### 13.4 Sequence: Two-Party Trade

```
Player A                lib-escrow              lib-currency         lib-inventory
    │                        │                        │                    │
    │─── create trade ──────►│                        │                    │
    │◄── depositTokens ──────│                        │                    │
    │                        │                        │                    │
    │─── deposit gold ──────►│                        │                    │
    │                        │──── lock gold ────────►│                    │
    │                        │◄─── lockId ────────────│                    │
    │◄── deposit ok ─────────│                        │                    │
    │                        │                        │                    │
    │                   Player B deposits item        │                    │
    │                        │                        │                    │
    │                        │───────── lock item ────────────────────────►│
    │                        │◄──────── lockId ───────────────────────────│
    │                        │                        │                    │
    │◄── fullyFunded ────────│ (releaseTokens issued) │                    │
    │                        │                        │                    │
    │─── consent release ───►│                        │                    │
    │◄── consent recorded ───│                        │                    │
    │                        │                        │                    │
    │                   Player B consents             │                    │
    │                        │                        │                    │
    │                        │──── transfer gold ────►│ (A→B)              │
    │                        │◄─── transactionId ─────│                    │
    │                        │                        │                    │
    │                        │───────── transfer item ────────────────────►│ (B→A)
    │                        │◄──────── success ──────────────────────────│
    │                        │                        │                    │
    │◄── released ───────────│                        │                    │
```

---

## Appendix A: Example Escrow Types

### A.1 Simple Player Trade

```yaml
escrowType: two_party
trustMode: full_consent
requiredConsentsForRelease: -1  # All parties
```

### A.2 NPC Vendor Sale

```yaml
escrowType: two_party
trustMode: initiator_trusted    # Game controls completion
initiatorServiceId: "game-session"
# Player deposits payment, NPC "deposits" item
# Game verifies and completes atomically
```

### A.3 Auction Settlement

```yaml
escrowType: auction
trustMode: initiator_trusted
initiatorServiceId: "lib-market"
# Winner's bid already escrowed
# On auction end: release to seller, refund others
```

### A.4 Arbitrated Trade

```yaml
escrowType: two_party
trustMode: single_party_trusted
trustedPartyId: gm_account_123
trustedPartyType: account
parties:
  - { partyId: player_a, role: depositor_recipient }
  - { partyId: player_b, role: depositor_recipient }
  - { partyId: gm_account_123, role: arbiter }
```

### A.5 Quest Reward Escrow

```yaml
escrowType: conditional
trustMode: initiator_trusted
initiatorServiceId: "lib-quest"
# Quest system creates escrow with reward
# On quest completion: verifies condition, releases reward
```

---

*This specification is implementation-ready. lib-currency and lib-inventory should implement the asset handler interface to integrate with lib-escrow.*
