# Account Plugin Deep Dive

> **Plugin**: lib-account
> **Schema**: schemas/account-api.yaml
> **Version**: 2.0.0
> **State Store**: account-statestore (MySQL)

## Overview

The Account plugin is an internal-only CRUD service for managing user accounts. It is never exposed directly to the internet - all external account operations go through the Auth service, which calls Account via lib-mesh. The plugin handles account creation, lookup (by ID, email, or OAuth provider), updates, soft-deletion, and authentication method management (linking/unlinking OAuth providers).

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | All persistence - account records, email indices, provider indices, auth methods. Uses IJsonQueryableStateStore for paginated listing via MySQL JSON queries |
| lib-messaging (IMessageBus) | Publishing lifecycle events (created/updated/deleted) and error events |
| Permission service (via AccountPermissionRegistration) | Registers its endpoint permission matrix on startup via a messaging event |

The Account plugin does **not** call any other service via lib-mesh clients. It is a leaf node that is called by others.

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-auth | Primary consumer. Calls `by-email`, `create`, `get`, `password/update` via IAccountClient for login, registration, token refresh, and password reset flows |
| lib-auth | Subscribes to `account.deleted` to invalidate all sessions for deleted accounts |
| lib-auth | Subscribes to `account.updated` to propagate role changes to active sessions |
| lib-achievement (SteamAchievementSync) | Calls `auth-methods/list` via IAccountClient to look up Steam external IDs for platform sync |

## State Storage

**Store**: `account-statestore` (Backend: MySQL)

All data types are stored in the same logical store, differentiated by key prefix.

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `account-{accountId}` | `AccountModel` | Primary account record (email, display name, password hash, roles, verification status, timestamps, metadata) |
| `email-index-{email_lowercase}` | `string` (accountId) | Email-to-account lookup index for login flows |
| `provider-index-{provider}:{externalId}` | `string` (accountId) | OAuth provider-to-account lookup index |
| `auth-methods-{accountId}` | `List<AuthMethodInfo>` | OAuth methods linked to an account (provider, external ID, method ID, link timestamp) |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `account.created` | `AccountCreatedEvent` | After successful account creation (includes accountId, email, roles, timestamps) |
| `account.updated` | `AccountUpdatedEvent` | After any field change on an account (includes full current state + changedFields list) |
| `account.deleted` | `AccountDeletedEvent` | After soft-deletion (includes final account state + deletion reason) |

All events are published via `_messageBus.TryPublishAsync()` which handles buffering and retry internally.

### Consumed Events

This plugin does not consume external events. The events schema explicitly declares `x-event-subscriptions: []`.

Note: `IEventConsumer` is injected and `RegisterEventConsumers` is called in the constructor, but no handlers are registered. This is scaffolding for future use.

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `AdminEmails` | `ACCOUNT_ADMIN_EMAILS` | null (optional) | Comma-separated list of email addresses that automatically receive the "admin" role on account creation |
| `AdminEmailDomain` | `ACCOUNT_ADMIN_EMAIL_DOMAIN` | null (optional) | Email domain suffix (e.g., "@company.com") that grants automatic admin role to all matching addresses |
| `ListBatchSize` | `ACCOUNT_LIST_BATCH_SIZE` | 100 | Number of accounts loaded per batch when applying provider filter in the list endpoint |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AccountService>` | Structured logging for all operations |
| `AccountServiceConfiguration` | Typed access to configuration properties above |
| `IStateStoreFactory` | Creates typed state store instances for reading/writing account data |
| `IMessageBus` | Publishes lifecycle and error events to RabbitMQ |
| `IEventConsumer` | Event handler registration (currently unused - no subscriptions) |
| `AccountPermissionRegistration` | Generated class that registers the service's permission matrix via messaging event on startup |

## API Endpoints (Implementation Notes)

### Account Management (CRUD)

Standard CRUD operations (`create`, `get`, `update`, `delete`, `list`) on account records with optimistic concurrency via ETags on all mutation operations. The `list` endpoint has two execution paths:

- **Standard path** (no provider filter): Uses `IJsonQueryableStateStore.JsonQueryPagedAsync()` to query account records directly from MySQL with server-side pagination, filtering (email, displayName, verified), and sorting (newest-first via `$.CreatedAtUnix` descending). The `$.AccountId exists` condition acts as a type discriminator to match only account records in the shared store.
- **Provider-filtered path** (rare admin operation): Queries all matching accounts from MySQL, then loads auth methods for each and filters by provider in-memory. Auth methods are stored in separate keys so provider filtering cannot be a single JSON query.

The `delete` operation is a soft-delete (sets `DeletedAt` timestamp) but also cleans up the email index and provider index entries.

### Account Lookup

- `by-email`: Looks up via the `email-index-` key, then loads the full account. **Notably includes `PasswordHash` in the response** - this is intentional for the Auth service's password verification flow. The regular `get` endpoint does not expose the password hash.
- `by-provider`: Looks up via the `provider-index-{provider}:{externalId}` key pattern.

### Authentication Methods

Add/remove OAuth provider links. Adding a method creates both the auth method entry in the `auth-methods-{accountId}` list and a `provider-index-` entry for reverse lookup. Removing a method cleans up both. The remove operation uses ETag-based optimistic concurrency on the auth methods list.

### Profile & Password

- `profile/update`: User-facing endpoint (not admin-only) for updating display name and metadata.
- `password/update`: Stores a pre-hashed password received from the Auth service. Account service never handles raw passwords.
- `verification/update`: Sets the `IsVerified` flag.

## State Store Key Relationships

```
account-{id} ──────────────────────────────────────────┐
  ├─ AccountId (Guid)                                   │
  ├─ Email ──► email-index-{email} ──► account ID      │
  ├─ PasswordHash                                       │ Same store,
  ├─ DisplayName                                        │ different
  ├─ IsVerified                                         │ key prefixes
  ├─ Roles[]                                            │
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
On Delete: email-index removed,                         │
           provider-index + auth-methods removed ◄──────┘
```

## Stubs & Unimplemented Features

### IEventConsumer Registration (no handlers)

The constructor injects `IEventConsumer` and calls `RegisterEventConsumers`, but the events schema declares no subscriptions. This is infrastructure wiring that exists for future use if Account needs to react to external events.

## Potential Extensions

- **Account merge**: No mechanism exists to merge two accounts (e.g., when a user registers with email then later tries to register with the same OAuth provider under a different email). The data model supports multiple auth methods per account, but there's no merge workflow.
- **Audit trail**: Account mutations publish events but don't maintain a per-account change history. An extension could store a changelog for compliance/debugging.
- **Email change**: There is no endpoint for changing an account's email address. The email index would need to be atomically swapped (delete old index, create new index, update account record) with proper concurrency handling.
- **Bulk operations**: No batch create/update/delete endpoints exist. The only batch-adjacent operation is the `list` endpoint.

## Known Quirks & Caveats

1. **Unix epoch timestamp storage (intentional)**: `AccountModel` stores timestamps as `long` Unix epoch values (`CreatedAtUnix`, `UpdatedAtUnix`, `DeletedAtUnix`) with `[JsonIgnore]` computed `DateTimeOffset` properties. This is a deliberate workaround for System.Text.Json's inconsistent `DateTimeOffset` serialization across platforms. The computed properties provide ergonomic access in code while the epoch values serialize reliably. Raw state store data shows epoch numbers, not human-readable dates - this is acceptable since the store is never queried directly by operators.

2. **Password hash in by-email response (intentional, internal-only)**: `GetAccountByEmailAsync` includes `PasswordHash` in its response, unlike other endpoints. This exists specifically for the Auth service's `BCrypt.Verify` call during login. The Account service is internal-only (never internet-facing) and `by-email` requires admin-level permissions, so this is not a security concern within the architecture. The regular `get` endpoint omits the hash.

3. **Soft-delete with immediate index removal (correct behavior)**: Deleting an account sets `DeletedAt` but immediately removes the email index and provider index entries. This is the intended behavior: deleted accounts disappear from lookup paths (can't log in, can't be listed) but remain loadable by ID for audit/recovery. The `DeletedAt` check returns 404 for direct loads, and the `$.DeletedAtUnix notExists` query condition excludes them from listings.

4. **Admin auto-assignment is creation-time only**: The `ShouldAssignAdminRole` check runs only during `CreateAccountAsync`. Changing `AdminEmails` or `AdminEmailDomain` configuration does not retroactively promote or demote existing accounts. This is acceptable - operational role changes for existing accounts should use the explicit `update` endpoint rather than implicit config-driven mutation.
