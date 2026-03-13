# Account Implementation Map

> **Plugin**: lib-account
> **Schema**: schemas/account-api.yaml
> **Layer**: L1 AppFoundation
> **Deep Dive**: [docs/plugins/ACCOUNT.md](../plugins/ACCOUNT.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-account |
| Layer | L1 AppFoundation |
| Endpoints | 18 |
| State Stores | account-statestore (MySQL), account-lock (Redis) |
| Events Published | 3 (account.created, account.updated, account.deleted) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 1 (AccountRetentionWorker) |

---

## State

**Store**: `account-statestore` (Backend: MySQL)

All data types stored in the same logical store, differentiated by key prefix.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `account-{accountId}` | `AccountModel` | Primary account record (email, display name, password hash, roles, verification, MFA, metadata, timestamps) |
| `email-index-{email_lowercase}` | `string` (accountId) | Email-to-account lookup index for login flows. Only created when email is non-null |
| `provider-index-{provider}:{externalId}` | `string` (accountId) | OAuth provider-to-account reverse lookup index |
| `auth-methods-{accountId}` | `List<AuthMethodInfo>` | OAuth methods linked to an account (provider, external ID, method ID, link timestamp) |

**Lock Store**: `account-lock` (Backend: Redis)

| Key Pattern | Purpose |
|-------------|---------|
| `account-email:{normalizedEmail}` | Distributed lock for email uniqueness during creation and email change |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | All persistence -- account records, email indices, provider indices, auth methods. Uses IJsonQueryableStateStore for paginated listing via MySQL JSON queries |
| lib-state (IDistributedLockProvider) | L0 | Hard | Distributed locks for email uniqueness during account creation and email change |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing lifecycle events (created/updated/deleted) and error events |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Telemetry span instrumentation for helper methods |

Account is a leaf node -- it does not call any other service via lib-mesh clients.

**Account Deletion Cleanup Obligation (per FOUNDATION TENETS)**: Account is exempt from lib-resource reference registration (privacy — no centralized tracking of account references). Instead, every service that stores account-owned data MUST subscribe to `account.deleted` and clean up all data for the deleted account. This is mandatory, not optional. See in FOUNDATION.md for the full pattern and reference implementation (lib-collection).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `account.created` | `AccountCreatedEvent` | After successful account creation (includes accountId, email, roles, authMethods, timestamps) |
| `account.updated` | `AccountUpdatedEvent` | After any field change (includes full current state, authMethods, changedFields list) |
| `account.deleted` | `AccountDeletedEvent` | After soft-deletion (includes final account state, authMethods, deletion reason) |

All lifecycle events include `authMethods` to enable identification of OAuth/Steam accounts when email is null.

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<AccountService>` | Structured logging |
| `AccountServiceConfiguration` | Typed configuration access (9 properties) |
| `IStateStoreFactory` | State store access -- produces 4 typed stores cached in constructor |
| `IMessageBus` | Event publishing (lifecycle + error events) |
| `IDistributedLockProvider` | Distributed locks for email uniqueness |
| `ITelemetryProvider` | Telemetry spans for async helpers |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| ListAccounts | POST /account/list | admin | - | - |
| CreateAccount | POST /account/create | [] | account, email-index | account.created |
| GetAccount | POST /account/get | [] | - | - |
| UpdateAccount | POST /account/update | [] | account | account.updated |
| DeleteAccount | POST /account/delete | [] | account, email-index, provider-index, auth-methods | account.deleted |
| GetAccountByEmail | POST /account/by-email | [] | - | - |
| GetAuthMethods | POST /account/auth-methods/list | [] | - | - |
| AddAuthMethod | POST /account/auth-methods/add | [] | auth-methods, provider-index | account.updated |
| RemoveAuthMethod | POST /account/auth-methods/remove | [] | auth-methods, provider-index | account.updated |
| GetAccountByProvider | POST /account/by-provider | [] | - | - |
| UpdateProfile | POST /account/profile/update | user | account | account.updated |
| UpdatePasswordHash | POST /account/password/update | [] | account | account.updated |
| UpdateMfa | POST /account/mfa/update | [] | account | account.updated |
| BatchGetAccounts | POST /account/batch-get | [] | - | - |
| CountAccounts | POST /account/count | [] | - | - |
| BulkUpdateRoles | POST /account/roles/bulk-update | admin | account | account.updated |
| UpdateVerificationStatus | POST /account/verification/update | [] | account | account.updated |
| UpdateEmail | POST /account/email/update | [] | account, email-index | account.updated |

---

## Methods

### ListAccounts
POST /account/list | Roles: [admin]

// Two execution paths based on provider filter presence

IF request.Provider is null
 // Standard path: fully server-side via MySQL JSON queries
 QUERY account-statestore WHERE $.AccountId exists AND $.DeletedAtUnix not exists
 [+ optional: $.Email contains, $.DisplayName contains, $.IsVerified equals]
 ORDER BY $.CreatedAtUnix DESC, PAGED(page, pageSize)
 FOREACH account in results (parallel)
 READ auth-methods-{accountId}
 RETURN (200, AccountListResponse)
ELSE
 // Provider-filtered path: server query + in-memory filter
 // Auth methods stored in separate keys, so provider filter cannot be a single JSON query
 QUERY account-statestore WHERE $.AccountId exists AND $.DeletedAtUnix not exists
 [+ optional filters] LIMIT config.ProviderFilterMaxScanSize
 FOREACH batch in results (batched by config.ListBatchSize)
 FOREACH account in batch (parallel)
 READ auth-methods-{accountId}
 FILTER accounts WHERE authMethods contains provider
 // Sort by CreatedAt descending, paginate in-memory
 RETURN (200, AccountListResponse)

---

### CreateAccount
POST /account/create | Roles: []

IF request.Email is not null
 LOCK account-lock:account-email:{normalizedEmail} -> 409 if lock fails
 READ email-index-{normalizedEmail} -> 409 if exists
 // Fall through to core creation (inside lock scope)

// Core creation (shared by locked email path and unlocked OAuth/Steam path)
// Assign default "user" role if no roles provided
// Auto-assign "admin" role if email matches config.AdminEmails or config.AdminEmailDomain
WRITE account-{newId} <- AccountModel from request
IF request.Email is not null
 WRITE email-index-{normalizedEmail} <- accountId
PUBLISH account.created { accountId, email, roles, authMethods, createdAt }
RETURN (200, AccountResponse)

---

### GetAccount
POST /account/get | Roles: []

READ account-{accountId} -> 404 if null
IF account.DeletedAt has value -> 404
READ auth-methods-{accountId}
RETURN (200, AccountResponse)
// Note: PasswordHash is NOT included in the response

---

### UpdateAccount
POST /account/update | Roles: []

READ account-{accountId} [with ETag] -> 404 if null or deleted
// Track changed fields: displayName, roles, metadata
// Anonymous role auto-management if config.AutoManageAnonymousRole:
// removes "anonymous" when adding non-anonymous roles,
// adds "anonymous" if roles would be empty
ETAG-WRITE account-{accountId} <- updated AccountModel -> 409 if ETag mismatch
IF changedFields is not empty
 PUBLISH account.updated { changedFields }
READ auth-methods-{accountId}
RETURN (200, AccountResponse)

---

### DeleteAccount
POST /account/delete | Roles: []

READ account-{accountId} [with ETag] -> 404 if null
// Soft-delete: set DeletedAt timestamp
ETAG-WRITE account-{accountId} <- soft-deleted model -> 409 if ETag mismatch
IF account.Email exists
 DELETE email-index-{normalizedEmail}
READ auth-methods-{accountId}
IF authMethods exist
 FOREACH method in authMethods
 DELETE provider-index-{provider}:{externalId}
 DELETE auth-methods-{accountId}
PUBLISH account.deleted { account state, deletedReason }
RETURN (200)

---

### GetAccountByEmail
POST /account/by-email | Roles: []

READ email-index-{normalizedEmail} -> 404 if null
READ account-{accountId} -> 404 if null
IF account.DeletedAt has value -> 404
READ auth-methods-{accountId}
RETURN (200, AccountResponse)
// Note: PasswordHash IS included (Auth service needs it for login verification)

---

### GetAuthMethods
POST /account/auth-methods/list | Roles: []

READ account-{accountId} -> 404 if null or deleted
READ auth-methods-{accountId}
RETURN (200, AuthMethodsResponse)

---

### AddAuthMethod
POST /account/auth-methods/add | Roles: []

READ account-{accountId} -> 404 if null or deleted
READ auth-methods-{accountId} [with ETag]
IF request.ExternalId is empty -> 400
// Map OAuthProvider (request enum) -> AuthProvider (storage enum)
IF mappedProvider+externalId already linked on this account -> 409

// Check if another account owns this provider:externalId
READ provider-index-{provider}:{externalId}
IF owned by another account
 READ account-{existingOwner}
 IF owner is active (not deleted) -> 409
 // Owner is deleted: orphaned index, safe to overwrite

// Create new auth method entry
ETAG-WRITE auth-methods-{accountId} <- updated list -> 409 if ETag mismatch
WRITE provider-index-{provider}:{externalId} <- accountId
PUBLISH account.updated { changedFields: ["authMethods"] }
RETURN (200, AuthMethodResponse)

---

### RemoveAuthMethod
POST /account/auth-methods/remove | Roles: []

READ account-{accountId} -> 404 if null or deleted
READ auth-methods-{accountId} [with ETag]
// Find method by methodId -> 404 if not found

// Orphan prevention: reject if last auth method and no password
IF !hasPassword AND remainingMethods == 0 -> 400

ETAG-WRITE auth-methods-{accountId} <- updated list -> 409 if ETag mismatch
DELETE provider-index-{provider}:{externalId}
PUBLISH account.updated { changedFields: ["authMethods"] }
RETURN (200)

---

### GetAccountByProvider
POST /account/by-provider | Roles: []

READ provider-index-{provider}:{externalId} -> 404 if null
READ account-{accountId} -> 404 if null
IF account.DeletedAt has value -> 404
READ auth-methods-{accountId}
RETURN (200, AccountResponse)

---

### UpdateProfile
POST /account/profile/update | Roles: [user]

READ account-{accountId} [with ETag] -> 404 if null or deleted
// Track changed fields: displayName, metadata
IF no fields changed
 READ auth-methods-{accountId}
 RETURN (200, AccountResponse) // early return, no save or event
ETAG-WRITE account-{accountId} <- updated AccountModel -> 409 if ETag mismatch
PUBLISH account.updated { changedFields }
READ auth-methods-{accountId}
RETURN (200, AccountResponse)

---

### UpdatePasswordHash
POST /account/password/update | Roles: []

READ account-{accountId} [with ETag] -> 404 if null or deleted
// Store pre-hashed password from Auth service (Account never handles raw passwords)
ETAG-WRITE account-{accountId} <- updated AccountModel -> 409 if ETag mismatch
PUBLISH account.updated { changedFields: ["passwordHash"] }
RETURN (200)

---

### UpdateMfa
POST /account/mfa/update | Roles: []

READ account-{accountId} [with ETag] -> 404 if null or deleted
// Update: mfaEnabled, mfaSecret (AES-256-GCM ciphertext), mfaRecoveryCodes (BCrypt hashes)
// Auth service encrypts/hashes; Account stores opaque values
ETAG-WRITE account-{accountId} <- updated AccountModel -> 409 if ETag mismatch
PUBLISH account.updated { changedFields: ["mfaEnabled", "mfaSecret", "mfaRecoveryCodes"] }
RETURN (200)

---

### BatchGetAccounts
POST /account/batch-get | Roles: []

// Max 100 IDs per call (schema-enforced maxItems: 100)
FOREACH accountId in request.AccountIds (parallel)
 READ account-{accountId}
 // Categorize: found, notFound (null or deleted), failed (exception)

// Second parallel pass: load auth methods for found accounts
FOREACH (accountId, account) in foundAccounts (parallel)
 READ auth-methods-{accountId}
 // Per-item error handling: auth method fetch failure -> failed list

RETURN (200, BatchGetAccountsResponse { accounts, notFound, failed })

---

### CountAccounts
POST /account/count | Roles: []

COUNT account-statestore WHERE $.AccountId exists AND $.DeletedAtUnix not exists
 [+ optional: $.Email contains, $.DisplayName contains, $.IsVerified equals]
 [+ optional: $.Roles JSON_CONTAINS role]
RETURN (200, CountAccountsResponse { count })

---

### BulkUpdateRoles
POST /account/roles/bulk-update | Roles: [admin]

IF neither addRoles nor removeRoles specified -> 400

// Process sequentially (ETag concurrency requires read-modify-write per account)
FOREACH accountId in request.AccountIds
 READ account-{accountId} [with ETag]
 IF null or deleted -> add to failed list, continue
 // Compute new roles: add addRoles, remove removeRoles
 // Anonymous role auto-management if config.AutoManageAnonymousRole
 IF roles unchanged -> add to succeeded list, continue (no event)
 ETAG-WRITE account-{accountId} <- updated AccountModel
 IF ETag mismatch -> add to failed list ("Concurrent modification"), continue
 PUBLISH account.updated { changedFields: ["roles"] }
 // Per-account exception handling -> add to failed list

RETURN (200, BulkUpdateRolesResponse { succeeded, failed })

---

### UpdateVerificationStatus
POST /account/verification/update | Roles: []

READ account-{accountId} [with ETag] -> 404 if null or deleted
// Set IsVerified flag
ETAG-WRITE account-{accountId} <- updated AccountModel -> 409 if ETag mismatch
PUBLISH account.updated { changedFields: ["isVerified"] }
RETURN (200)

---

### UpdateEmail
POST /account/email/update | Roles: []

LOCK account-lock:account-email:{normalizedNewEmail} -> 409 if lock fails
 READ email-index-{normalizedNewEmail} -> 409 if exists (email taken)
 READ account-{accountId} [with ETag] -> 404 if null or deleted
 IF newEmail == oldEmail (unchanged)
 READ auth-methods-{accountId}
 RETURN (200, AccountResponse) // no-op

 // Create new index BEFORE saving account (rollback on ETag failure)
 WRITE email-index-{normalizedNewEmail} <- accountId
 // Update: set new email, reset IsVerified to false
 ETAG-WRITE account-{accountId} <- updated AccountModel
 IF ETag mismatch
 DELETE email-index-{normalizedNewEmail} // rollback new index
 RETURN (409)

 // Success: delete old email index (if account previously had email)
 IF oldEmail exists
 DELETE email-index-{oldNormalizedEmail}
 PUBLISH account.updated { changedFields: ["email", "isVerified"] }
 READ auth-methods-{accountId}
 RETURN (200, AccountResponse)

---

## Background Services

### AccountRetentionWorker

**File**: `plugins/lib-account/Services/AccountRetentionWorker.cs`
**Registration**: `AccountServicePlugin.ConfigureServices` → `AddHostedService<AccountRetentionWorker>()`

Permanently purges soft-deleted account records after the configured retention period.

**Configuration**:
| Property | Default | Purpose |
|----------|---------|---------|
| `RetentionPeriodDays` | 30 | Days after soft-deletion before permanent purge |
| `RetentionCleanupIntervalSeconds` | 86400 | Interval between cycles (default: 24h) |
| `RetentionCleanupStartupDelaySeconds` | 60 | Delay before first cycle after startup |

**Cycle pseudocode**:
```
cutoffUnix = NOW - RetentionPeriodDays
QUERY account-statestore WHERE $.AccountId EXISTS
                           AND $.DeletedAtUnix EXISTS
                           AND $.DeletedAtUnix < cutoffUnix

FOR EACH expired account:
    DELETE account-{id}    // indexes + auth-methods already removed at soft-delete time
    LOG purged account {id}
```

**Notes**:
- No events published — `account.deleted` was already published at soft-delete time
- Per-item error isolation — one corrupt record does not block the cycle
- Only hard-deletes the account record; email/provider indexes and auth methods are already cleaned up by `DeleteAccountAsync`
