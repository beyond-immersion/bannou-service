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
| lib-state (IStateStoreFactory) | All persistence - account records, email indices, provider indices, auth methods, and the pagination index |
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
| `accounts-list` | `List<string>` (accountIds) | Ordered list of all account IDs for paginated listing |

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
| `IndexUpdateMaxRetries` | `ACCOUNT_INDEX_UPDATE_MAX_RETRIES` | 3 | Maximum retry attempts when updating the `accounts-list` index with optimistic concurrency |
| `ListBatchSize` | `ACCOUNT_LIST_BATCH_SIZE` | 100 | Number of accounts loaded per batch when applying filters in the list endpoint |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AccountService>` | Structured logging for all operations |
| `AccountServiceConfiguration` | Typed access to configuration properties above |
| `IStateStoreFactory` | Creates typed state store instances for reading/writing account data |
| `IMessageBus` | Publishes lifecycle and error events to RabbitMQ |
| `IEventConsumer` | Event handler registration (currently unused - no subscriptions) |
| `AccountEventPublisher` | **Exists but unused** - see Stubs section below |
| `AccountPermissionRegistration` | Generated class that registers the service's permission matrix via messaging event on startup |

## API Endpoints (Implementation Notes)

### Account Management (CRUD)

Standard CRUD operations (`create`, `get`, `update`, `delete`, `list`) on account records with optimistic concurrency via ETags on all mutation operations. The `list` endpoint has two execution paths:

- **Unfiltered path**: Loads only the page of account IDs needed from the `accounts-list` index (newest-first ordering via list reversal), then loads each account individually.
- **Filtered path**: Scans all accounts in batches of `ListBatchSize`, applies in-memory filters (email, displayName, provider, verified status), then paginates the results.

The `delete` operation is a soft-delete (sets `DeletedAt` timestamp) but also cleans up the email index and removes the ID from the pagination list.

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
accounts-list: [ "guid-1", "guid-2", "guid-3", ... ]
       │
       ▼ (each entry is an account ID)
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
On Delete: email-index removed, accounts-list cleaned   │
           provider-index entries LEFT ORPHANED ◄───────┘
```

## Stubs & Unimplemented Features

### AccountEventPublisher (unused class)

`AccountEventPublisher.cs` exists as a type-safe event publisher class extending `EventPublisherBase`. It provides `PublishAccountCreatedAsync`, `PublishAccountUpdatedAsync`, and `PublishAccountDeletedAsync` methods. However, **AccountService does not use this class**. Instead, it has its own private inline methods (`PublishAccountCreatedEventAsync`, `PublishAccountUpdatedEventAsync`, `PublishAccountDeletedEventAsync`) that publish events directly via `_messageBus.TryPublishAsync()`.

The `AccountEventPublisher` is not injected into the service constructor and is never instantiated. It appears to be scaffolding for a planned pattern where services delegate event publishing to dedicated publisher classes, but the service was implemented before that pattern was adopted.

### IEventConsumer Registration (no handlers)

The constructor injects `IEventConsumer` and calls `RegisterEventConsumers`, but the events schema declares no subscriptions. This is infrastructure wiring that exists for future use if Account needs to react to external events.

## Potential Extensions

- **Account search/query**: The current `list` endpoint loads all accounts into memory for filtering. A proper query mechanism using the MySQL backend's query capabilities (via the state store's query API) would be more efficient for large account volumes.
- **Account merge**: No mechanism exists to merge two accounts (e.g., when a user registers with email then later tries to register with the same OAuth provider under a different email). The data model supports multiple auth methods per account, but there's no merge workflow.
- **Audit trail**: Account mutations publish events but don't maintain a per-account change history. An extension could store a changelog for compliance/debugging.
- **Email change**: There is no endpoint for changing an account's email address. The email index would need to be atomically swapped (delete old index, create new index, update account record) with proper concurrency handling.
- **Bulk operations**: No batch create/update/delete endpoints exist. The only batch-adjacent operation is the `list` endpoint.
- **Use AccountEventPublisher**: The unused publisher class could replace the inline publishing methods, consolidating event construction logic and making the service implementation slimmer.

## Known Quirks & Caveats

1. **Unix epoch timestamp storage**: `AccountModel` stores timestamps as `long` Unix epoch values (`CreatedAtUnix`, `UpdatedAtUnix`, `DeletedAtUnix`) with `[JsonIgnore]` computed `DateTimeOffset` properties. This works around System.Text.Json serialization issues with `DateTimeOffset` but means the raw state store data shows epoch numbers, not human-readable dates.

2. **Password hash in by-email response**: The `GetAccountByEmailAsync` endpoint intentionally includes `PasswordHash` in its response, unlike other endpoints. This is specifically for the Auth service's login flow where it needs to verify the password. If Account's by-email endpoint is ever exposed more broadly, this would be a security concern.

3. **Soft-delete with index cleanup**: Deleting an account is a soft-delete (the record remains with `DeletedAt` set), but the email index and accounts-list entries are removed immediately. This means a deleted account cannot be found by email lookup or listed, but can still be loaded directly by ID (and will return 404 because of the `DeletedAt` check).

4. **OAuthProvider to AuthProvider fallback**: `MapOAuthProviderToAuthProvider` has a default case that maps unknown providers to `AuthProvider.Google`. This is a silent fallback that could mask bugs if new OAuth providers are added without updating the mapping.

5. **Admin auto-assignment is creation-time only**: The `ShouldAssignAdminRole` check runs only during `CreateAccountAsync`. If the `AdminEmails` or `AdminEmailDomain` configuration changes after an account is created, existing accounts are not retroactively promoted or demoted.

6. **accounts-list ordering**: New accounts are appended to the end of the list, and the list endpoint reverses it for newest-first display. This means the list grows unboundedly and reversal is an O(n) operation on the full list for every unfiltered page request.

7. **No provider index cleanup on account deletion**: When an account is soft-deleted, the email index is removed but `provider-index-` entries are not cleaned up. This means an OAuth lookup for a deleted account's provider ID will find the account ID, load the account, and then return 404 due to the `DeletedAt` check. It works correctly but leaves orphaned index entries.
