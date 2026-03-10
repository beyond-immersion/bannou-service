# Account Plugin Deep Dive

> **Plugin**: lib-account
> **Schema**: schemas/account-api.yaml
> **Version**: 2.0.0
> **Layer**: AppFoundation
> **State Store**: account-statestore (MySQL), account-lock (Redis)
> **Implementation Map**: [docs/maps/ACCOUNT.md](../maps/ACCOUNT.md)
> **Short**: Internal user account CRUD (never internet-facing; external access via Auth only)

---

## Overview

The Account plugin is an internal-only CRUD service (L1 AppFoundation) for managing user accounts. It is never exposed directly to the internet -- all external account operations go through the Auth service, which calls Account via lib-mesh. Handles account creation, lookup (by ID, email, or OAuth provider), updates, soft-deletion, and authentication method management (linking/unlinking OAuth providers). Email is optional -- accounts created via OAuth or Steam may have no email address, identified solely by their linked authentication methods.

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-auth (AuthService) | Primary consumer. Calls `by-email`, `create`, `get`, `password/update` via IAccountClient for login, registration, token refresh, and password reset flows |
| lib-auth (OAuthProviderService) | Calls `get`, `create`, `by-email`, `auth-methods/add` via IAccountClient for OAuth account creation and linking |
| lib-auth | Subscribes to `account.deleted` to invalidate all sessions and cleanup OAuth links for deleted accounts |
| lib-auth | Subscribes to `account.updated` to propagate role changes to active sessions |
| lib-achievement (SteamAchievementSync) | Calls `auth-methods/list` via IAccountClient to look up Steam external IDs for platform sync |
| lib-collection (CollectionService) | Subscribes to `account.deleted` to clean up all account-owned collections (Account Deletion Cleanup Obligation per FOUNDATION TENETS) |

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

## Visual Aid

```
account-{id} ──────────────────────────────────────────┐
 ├─ AccountId (Guid) │
 ├─ Email? ──► email-index-{email} ──► account ID │ (nullable: OAuth/Steam
 ├─ PasswordHash? │ accounts may have no email)
 ├─ DisplayName │ Same store,
 ├─ IsVerified │ different
 ├─ Roles[] │ key prefixes
 ├─ MfaEnabled, MfaSecret?, MfaRecoveryCodes? │
 ├─ Metadata{} (client-only opaque data per tenets) │
 └─ CreatedAtUnix / UpdatedAtUnix / DeletedAtUnix │
 │
auth-methods-{id}: [ AuthMethodInfo, ... ] ─────────────┤
 ├─ MethodId (Guid) │
 ├─ Provider ──► provider-index-{provider}:{extId} │
 ├─ ExternalId ──► account ID │
 └─ LinkedAt │
 │
Listing: JsonQueryPagedAsync with $.AccountId exists │
 discriminator queries account records directly │
 │
On Delete: email-index removed (if exists), │
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

4. **Default "user" role on creation**: When an account is created with no roles specified, the "user" role is automatically assigned in `CreateAccountCoreAsync`. This ensures newly registered accounts have basic authenticated API access.

5. **Password hash exposed in by-email response only**: The `GetAccountByEmailAsync` endpoint includes `PasswordHash` in the response, while the standard `GetAccountAsync` does not. This is intentional - the Auth service needs the hash for password verification during login, but general account lookups should not expose it.

6. **Provider index key format**: Provider indices use the format `provider-index-{provider}:{externalId}` where provider is the enum value (e.g., `provider-index-Discord:123456`). The colon separator is intentional to create a pseudo-hierarchical key space.

7. **Auth method removal prevents account orphaning**: The `RemoveAuthMethodAsync` endpoint includes a safety check that rejects removal of the last auth method if the account has no password. This prevents accounts from becoming completely inaccessible. Returns `BadRequest` if removal would leave no authentication mechanism.

8. **Provider index ownership validation with stale detection**: When adding an auth method, `AddAuthMethodAsync` checks if another account already owns the provider:externalId combination. If the owning account is soft-deleted (stale index from incomplete cleanup), the orphaned index is overwritten with a log message. Only returns `Conflict` if the owning account is still active.

9. **Soft-delete is NOT deprecation**: Account deletion uses a soft-delete pattern (`DeletedAt` timestamp) for practical data retention and audit purposes. This is distinct from the deprecation lifecycle defined in IMPLEMENTATION TENETS. Accounts are identity instances, not definitions or templates referenced by other entities -- they fall squarely in's "immediate hard delete" category. The soft-delete exists for data retention policy compliance and to support stale index detection (quirk #8), not as a deprecation-before-delete workflow.

10. **Metadata field is client-only per FOUNDATION TENETS**: The `Metadata` dictionary on `AccountModel` uses `additionalProperties: true` and is strictly client-opaque pass-through storage. No Bannou plugin reads specific keys from account metadata by convention. The service stores and returns it unchanged without inspection.

### Design Considerations (Requires Planning)

None identified.

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

*No active work items.*
