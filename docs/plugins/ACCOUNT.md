# Account Plugin Deep Dive

> **Plugin**: lib-account
> **Schema**: schemas/account-api.yaml
> **Version**: 2.0.0
> **Layer**: AppFoundation
> **State Store**: account-statestore (MySQL), account-lock (Redis)

## Overview

The Account plugin is an internal-only CRUD service (L1 AppFoundation) for managing user accounts. It is never exposed directly to the internet -- all external account operations go through the Auth service, which calls Account via lib-mesh. Handles account creation, lookup (by ID, email, or OAuth provider), updates, soft-deletion, and authentication method management (linking/unlinking OAuth providers). Email is optional -- accounts created via OAuth or Steam may have no email address, identified solely by their linked authentication methods.

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | All persistence - account records, email indices, provider indices, auth methods. Uses IJsonQueryableStateStore for paginated listing via MySQL JSON queries |
| lib-state (IDistributedLockProvider) | Distributed locks for email uniqueness during account creation and email change (prevents TOCTOU race conditions) |
| lib-messaging (IMessageBus) | Publishing lifecycle events (created/updated/deleted) and error events |
| Permission service (via AccountPermissionRegistration) | Registers its endpoint permission matrix on startup via DI-based `IPermissionRegistry` |

The Account plugin does **not** call any other service via lib-mesh clients. It is a leaf node that is called by others.

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-auth (AuthService) | Primary consumer. Calls `by-email`, `create`, `get`, `password/update` via IAccountClient for login, registration, token refresh, and password reset flows |
| lib-auth (OAuthProviderService) | Calls `get`, `create`, `by-email`, `auth-methods/add` via IAccountClient for OAuth account creation and linking |
| lib-auth | Subscribes to `account.deleted` to invalidate all sessions and cleanup OAuth links for deleted accounts |
| lib-auth | Subscribes to `account.updated` to propagate role changes to active sessions |
| lib-achievement (SteamAchievementSync) | Calls `auth-methods/list` via IAccountClient to look up Steam external IDs for platform sync |

## State Storage

**Store**: `account-statestore` (Backend: MySQL)

All data types are stored in the same logical store, differentiated by key prefix.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `account-{accountId}` | `AccountModel` | Primary account record (email [nullable], display name, password hash, roles, verification status, timestamps, metadata) |
| `email-index-{email_lowercase}` | `string` (accountId) | Email-to-account lookup index for login flows. Only created when email is non-null. |
| `provider-index-{provider}:{externalId}` | `string` (accountId) | OAuth provider-to-account lookup index |
| `auth-methods-{accountId}` | `List<AuthMethodInfo>` | OAuth methods linked to an account (provider, external ID, method ID, link timestamp) |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `account.created` | `AccountCreatedEvent` | After successful account creation (includes accountId, email [nullable], roles, authMethods, timestamps) |
| `account.updated` | `AccountUpdatedEvent` | After any field change on an account (includes full current state, authMethods, + changedFields list) |
| `account.deleted` | `AccountDeletedEvent` | After soft-deletion (includes final account state, authMethods, + deletion reason) |

All lifecycle events include `authMethods` to enable identification of OAuth/Steam accounts when email is null.

All events are published via `_messageBus.TryPublishAsync()` which handles buffering and retry internally.

### Consumed Events

This plugin does not consume external events. The events schema explicitly declares `x-event-subscriptions: []`.

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `AdminEmails` | `ACCOUNT_ADMIN_EMAILS` | null (optional) | Comma-separated list of email addresses that automatically receive the "admin" role on account creation |
| `AdminEmailDomain` | `ACCOUNT_ADMIN_EMAIL_DOMAIN` | null (optional) | Email domain suffix (e.g., "@company.com") that grants automatic admin role to all matching addresses |
| `DefaultPageSize` | `ACCOUNT_DEFAULT_PAGE_SIZE` | 20 | Default page size for list operations when not specified in request |
| `MaxPageSize` | `ACCOUNT_MAX_PAGE_SIZE` | 100 | Maximum allowed page size for list operations (requests capped to this value) |
| `ListBatchSize` | `ACCOUNT_LIST_BATCH_SIZE` | 100 | Number of accounts loaded per batch when applying provider filter in the list endpoint |
| `CreateLockExpirySeconds` | `ACCOUNT_CREATE_LOCK_EXPIRY_SECONDS` | 10 | Lock expiry in seconds for distributed email uniqueness lock during account creation (min: 1, max: 60) |
| `EmailChangeLockExpirySeconds` | `ACCOUNT_EMAIL_CHANGE_LOCK_EXPIRY_SECONDS` | 10 | Lock expiry in seconds for email uniqueness check during email change (min: 1, max: 60) |
| `ProviderFilterMaxScanSize` | `ACCOUNT_PROVIDER_FILTER_MAX_SCAN_SIZE` | 10000 | Maximum number of accounts to scan when filtering by provider in the admin-only list endpoint (min: 100, max: 100000) |
| `AutoManageAnonymousRole` | `ACCOUNT_AUTO_MANAGE_ANONYMOUS_ROLE` | true | When true, automatically manages "anonymous" role: adds it if roles would be empty, removes it when adding non-anonymous roles |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AccountService>` | Structured logging for all operations |
| `AccountServiceConfiguration` | Typed access to configuration properties above |
| `IStateStoreFactory` | Creates typed state store instances for reading/writing account data |
| `IMessageBus` | Publishes lifecycle and error events to RabbitMQ |
| `IDistributedLockProvider` | Distributed locks for email uniqueness during account creation and email change |
| `ITelemetryProvider` | Telemetry span instrumentation for helper methods (auth method fetch, event publishing, provider-filtered listing) |
| `AccountPermissionRegistration` | Generated class that registers the service's permission matrix via `IPermissionRegistry` on startup |

## API Endpoints (Implementation Notes)

### Account Management (CRUD)

Standard CRUD operations (`create`, `get`, `update`, `delete`, `list`) on account records with optimistic concurrency via ETags on all mutation operations. Account creation accepts nullable email - when null (for OAuth/Steam accounts), no email index is created and the account is identifiable only via provider lookup or direct ID. The `list` endpoint has two execution paths:

- **Standard path** (no provider filter): Uses `IJsonQueryableStateStore.JsonQueryPagedAsync()` to query account records directly from MySQL with server-side pagination, filtering (email, displayName, verified), and sorting (newest-first via `$.CreatedAtUnix` descending). The `$.AccountId exists` condition acts as a type discriminator to match only account records in the shared store.
- **Provider-filtered path** (rare admin operation): Queries matching accounts from MySQL using `JsonQueryPagedAsync` with a configurable scan cap (`ProviderFilterMaxScanSize`, default 10000), then loads auth methods for each and filters by provider in-memory. Auth methods are stored in separate keys so provider filtering cannot be a single JSON query. Logs a warning if the scan cap truncates results.

The `delete` operation is a soft-delete (sets `DeletedAt` timestamp) but also cleans up the email index (if email exists) and provider index entries.

### Account Lookup

- `by-email`: Looks up via the `email-index-` key, then loads the full account. **Notably includes `PasswordHash` in the response** - this is intentional for the Auth service's password verification flow. The regular `get` endpoint does not expose the password hash. Only works for accounts with email addresses.
- `by-provider`: Looks up via the `provider-index-{provider}:{externalId}` key pattern. This is the primary lookup method for OAuth/Steam accounts that may not have email addresses.

### Authentication Methods

`auth-methods/list` returns all linked OAuth methods for an account (verifies account exists and is not deleted first). Add/remove OAuth provider links via `auth-methods/add` and `auth-methods/remove`. Adding a method validates: (1) non-empty ExternalId required, (2) provider not already linked on this account (same provider+externalId), (3) provider:externalId not claimed by another active account (stale indexes from soft-deleted accounts are detected and overwritten). Both `AddAuthMethodAsync` and `RemoveAuthMethodAsync` use ETag-based optimistic concurrency (`GetWithETagAsync` + `TrySaveAsync`) on the auth methods list, returning `Conflict` on concurrent modification. Creates both the auth method entry in the `auth-methods-{accountId}` list and a `provider-index-` entry for reverse lookup. Removing a method includes orphan prevention (see Intentional Quirks #7) and cleans up both the auth method list and provider index.

### Bulk Operations

- `batch-get`: Retrieves multiple accounts by ID in parallel using `Task.WhenAll` over direct key lookups (not JSON queries). Returns a `BatchGetAccountsResponse` with separate `accounts` (found), `notFound` (missing or soft-deleted), and `failed` (fetch errors) lists. Auth methods are loaded in a second parallel pass for found accounts. Max 100 IDs per call (schema-enforced via `maxItems: 100`).
- `count`: Uses `IJsonQueryableStateStore.JsonCountAsync()` for a pure SQL `SELECT COUNT(*)` with the same filter conditions as `list` (email, displayName, verified), plus a `role` filter that uses `JSON_CONTAINS` on the `$.Roles` array. The `BuildAccountQueryConditions` helper automatically includes the type discriminator and soft-delete exclusion conditions.
- `roles/bulk-update`: Adds and/or removes roles from up to 100 accounts. Processes sequentially with per-account ETag-based optimistic concurrency. Returns partial success: `succeeded` and `failed` lists with error reasons. Publishes `account.updated` events individually for each changed account. No-op (roles already match) counts as success without publishing an event.

### Profile, Password & MFA

- `profile/update`: User-facing endpoint (not admin-only) for updating display name and metadata. Returns early without saving or publishing an event if nothing changed.
- `password/update`: Stores a pre-hashed password received from the Auth service. Account service never handles raw passwords.
- `verification/update`: Sets the `IsVerified` flag.
- `mfa/update`: Updates MFA settings (enabled flag, encrypted TOTP secret, hashed recovery codes). Auth service owns the encryption/hashing; Account stores opaque ciphertext and hashes. Publishes `account.updated` with `changedFields: ["mfaEnabled", "mfaSecret", "mfaRecoveryCodes"]`.

### Email Change

- `email/update`: Admin-only endpoint for changing an account's email address. Uses a distributed lock on the new email (`account-email:{normalizedNewEmail}` key in `account-lock` Redis store) to coordinate with `CreateAccountAsync` and prevent TOCTOU race conditions. The operation atomically: (1) validates the new email is not already taken, (2) creates the new email index, (3) updates the account record with ETag concurrency (setting the new email, resetting `IsVerified` to false), and (4) deletes the old email index. If the ETag save fails, the new email index is rolled back. Also handles the case where the account had no previous email (OAuth/Steam accounts) — in that case, only the new index is created. Publishes `account.updated` with `["email", "isVerified"]` in `changedFields`.

## State Store Key Relationships

```
account-{id} ──────────────────────────────────────────┐
  ├─ AccountId (Guid)                                   │
  ├─ Email? ──► email-index-{email} ──► account ID     │  (nullable: OAuth/Steam
  ├─ PasswordHash?                                      │   accounts may have no email)
  ├─ DisplayName                                        │ Same store,
  ├─ IsVerified                                         │ different
  ├─ Roles[]                                            │ key prefixes
  ├─ MfaEnabled, MfaSecret?, MfaRecoveryCodes?           │
  ├─ Metadata{}                                         │
  └─ CreatedAtUnix / UpdatedAtUnix / DeletedAtUnix      │
                                                        │
auth-methods-{id}: [ AuthMethodInfo, ... ] ─────────────┤
  ├─ MethodId (Guid)                                    │
  ├─ Provider ──► provider-index-{provider}:{extId}     │
  ├─ ExternalId    ──► account ID                       │
  └─ LinkedAt                                           │
                                                        │
Listing: JsonQueryPagedAsync with $.AccountId exists    │
         discriminator queries account records directly │
                                                        │
On Delete: email-index removed (if exists),             │
           provider-index + auth-methods removed ◄──────┘
```

## Stubs & Unimplemented Features

*No stubs remaining.*

## Potential Extensions

- **Account merge**: No mechanism exists to merge two accounts (e.g., when a user registers with email then later tries to register with the same OAuth provider under a different email). The data model supports multiple auth methods per account, but there's no merge workflow.
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/137 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No known bugs.*

### Intentional Quirks

1. **Nullable email for OAuth/Steam accounts**: The `Email` field is nullable to support accounts created solely through OAuth or Steam authentication, which may not provide an email address. When email is null, no `email-index-` entry is created. The `authMethods` field in events enables downstream services to identify such accounts by their linked providers.

2. **Unix epoch timestamp storage**: `AccountModel` stores timestamps as `long` Unix epoch values with `[JsonIgnore]` computed `DateTimeOffset` properties. Deliberate workaround for System.Text.Json's inconsistent `DateTimeOffset` serialization.

3. **Auto-managed anonymous role**: When `AutoManageAnonymousRole` is true (default), both `UpdateAccountAsync` and `BulkUpdateRolesAsync` automatically manage the "anonymous" role: removing roles that would leave zero roles adds "anonymous", and adding a non-anonymous role removes "anonymous" if present. This logic is **not** applied during `CreateAccountAsync` (which defaults to "user"). This ensures accounts always have at least one role for permission resolution.

4. **Default "user" role on creation**: When an account is created with no roles specified, the "user" role is automatically assigned (AccountService.cs:345). This ensures newly registered accounts have basic authenticated API access.

5. **Password hash exposed in by-email response only**: The `GetAccountByEmailAsync` endpoint includes `PasswordHash` in the response (AccountService.cs:648), while the standard `GetAccountAsync` does not. This is intentional - the Auth service needs the hash for password verification during login, but general account lookups should not expose it.

6. **Provider index key format**: Provider indices use the format `provider-index-{provider}:{externalId}` where provider is the enum value (e.g., `provider-index-Discord:123456`). The colon separator is intentional to create a pseudo-hierarchical key space.

7. **Auth method removal prevents account orphaning**: The `RemoveAuthMethodAsync` endpoint includes a safety check (AccountService.cs:1068-1078) that rejects removal of the last auth method if the account has no password. This prevents accounts from becoming completely inaccessible. Returns `BadRequest` if removal would leave no authentication mechanism.

8. **Provider index ownership validation with stale detection**: When adding an auth method, `AddAuthMethodAsync` checks if another account already owns the provider:externalId combination. If the owning account is soft-deleted (stale index from incomplete cleanup), the orphaned index is overwritten with a log message. Only returns `Conflict` if the owning account is still active.

### Design Considerations (Requires Planning)

None identified.

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

*No active work items.*
