# Auth Plugin Deep Dive

> **Plugin**: lib-auth
> **Schema**: schemas/auth-api.yaml
> **Version**: 4.0.0
> **State Store**: auth-statestore (Redis)

## Overview

The Auth plugin is the internet-facing authentication and session management service. It handles email/password login, OAuth provider integration (Discord, Google, Twitch), Steam session ticket verification, JWT token generation/validation, password reset flows, and session lifecycle management. It is the primary gateway between external users and the internal service mesh - after authenticating, clients receive a JWT and a WebSocket connect URL to establish persistent connections.

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (IStateStoreFactory) | All session data, refresh tokens, OAuth links, and password reset tokens in Redis |
| lib-messaging (IMessageBus) | Publishing session lifecycle events and audit events |
| lib-account (IAccountClient) | Account CRUD: lookup by email, create, get by ID, update password |
| lib-subscription (ISubscriptionClient) | Fetches active subscriptions during token generation and subscription change propagation |
| AppConfiguration (DI singleton) | JWT secret, issuer, audience, and ServiceDomain via constructor-injected config |

**External NuGet dependencies:**
- `Microsoft.IdentityModel.Tokens` (8.15.0) - JWT creation and validation
- `BCrypt.Net-Next` (4.0.3) - Password hashing

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-connect | Calls `/auth/validate` via IAuthClient to validate JWTs on WebSocket connection establishment |
| lib-connect | Subscribes to `session.invalidated` to disconnect WebSocket clients when sessions are terminated |
| lib-permission | Subscribes to `session.updated` to recompile capability manifests when roles/authorizations change |

## State Storage

**Store**: `auth-statestore` (Backend: Redis)

All keys use the `auth` prefix and have explicit TTLs since the data is ephemeral.

| Key Pattern | Data Type | TTL | Purpose |
|-------------|-----------|-----|---------|
| `session:{sessionKey}` | `SessionDataModel` | JwtExpirationMinutes * 60 | Active session data (accountId, email, roles, authorizations, expiry) |
| `account-sessions:{accountId}` | `List<string>` | JwtExpirationMinutes * 60 + 300 | Index of session keys for an account (lazy-cleaned on read) |
| `session-id-index:{sessionId}` | `string` (sessionKey) | JwtExpirationMinutes * 60 | Reverse lookup: human-facing session ID to internal session key |
| `refresh_token:{token}` | `string` (accountId) | SessionTokenTtlDays in seconds | Maps refresh token to account ID |
| `oauth-link:{provider}:{providerId}` | `string` (accountId) | **None** | Maps OAuth provider identity to account (cleaned up on account deletion) |
| `account-oauth-links:{accountId}` | `List<string>` | **None** | Reverse index of OAuth link keys for account (for cleanup on deletion) |
| `password-reset:{token}` | `PasswordResetData` | PasswordResetTokenTtlMinutes * 60 | Pending password reset (accountId, email, expiry) |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `session.invalidated` | `SessionInvalidatedEvent` | Logout, account deletion, admin action, security revocation |
| `session.updated` | `SessionUpdatedEvent` | Role changes propagated to sessions, subscription changes propagated |
| `auth.login.successful` | `AuthLoginSuccessfulEvent` | Successful email/password login |
| `auth.login.failed` | `AuthLoginFailedEvent` | Failed login attempt (brute force detection) |
| `auth.registration.successful` | `AuthRegistrationSuccessfulEvent` | New account registered |
| `auth.oauth.successful` | `AuthOAuthLoginSuccessfulEvent` | Successful OAuth provider login |
| `auth.steam.successful` | `AuthSteamLoginSuccessfulEvent` | Successful Steam ticket verification |
| `auth.password-reset.successful` | `AuthPasswordResetSuccessfulEvent` | Password reset completed |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `account.deleted` | `HandleAccountDeletedAsync` | Invalidates all sessions, cleans up OAuth links via reverse index, and publishes `session.invalidated` |
| `account.updated` | `HandleAccountUpdatedAsync` | If `changedFields` contains "roles", propagates new roles to all active sessions and publishes `session.updated` per session |
| `subscription.updated` | `HandleSubscriptionUpdatedAsync` | Re-fetches subscriptions from Subscription service, updates all sessions with new authorizations, publishes `session.updated` per session |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `JwtExpirationMinutes` | `AUTH_JWT_EXPIRATION_MINUTES` | 60 | How long access tokens and sessions are valid |
| `SessionTokenTtlDays` | `AUTH_SESSION_TOKEN_TTL_DAYS` | 7 | How long refresh tokens remain valid |
| `PasswordResetTokenTtlMinutes` | `AUTH_PASSWORD_RESET_TOKEN_TTL_MINUTES` | 30 | How long password reset tokens remain valid |
| `PasswordResetBaseUrl` | `AUTH_PASSWORD_RESET_BASE_URL` | required | Base URL for password reset links in emails |
| `ConnectUrl` | `AUTH_CONNECT_URL` | `ws://localhost:5014/connect` | WebSocket URL returned to clients after authentication |
| `MockProviders` | `AUTH_MOCK_PROVIDERS` | false | Enable mock OAuth for testing (bypasses real provider calls) |
| `MockDiscordId` | `AUTH_MOCK_DISCORD_ID` | - | Mock Discord user ID for testing |
| `MockGoogleId` | `AUTH_MOCK_GOOGLE_ID` | - | Mock Google user ID for testing |
| `MockTwitchId` | `AUTH_MOCK_TWITCH_ID` | - | Mock Twitch user ID for testing |
| `MockSteamId` | `AUTH_MOCK_STEAM_ID` | - | Mock Steam ID for testing |
| `DiscordClientId` | `AUTH_DISCORD_CLIENT_ID` | null | Discord OAuth application client ID |
| `DiscordClientSecret` | `AUTH_DISCORD_CLIENT_SECRET` | null | Discord OAuth application secret |
| `DiscordRedirectUri` | `AUTH_DISCORD_REDIRECT_URI` | null | Discord OAuth callback URL (derived from ServiceDomain if not set) |
| `GoogleClientId` | `AUTH_GOOGLE_CLIENT_ID` | null | Google OAuth client ID |
| `GoogleClientSecret` | `AUTH_GOOGLE_CLIENT_SECRET` | null | Google OAuth client secret |
| `GoogleRedirectUri` | `AUTH_GOOGLE_REDIRECT_URI` | null | Google OAuth callback URL (derived from ServiceDomain if not set) |
| `TwitchClientId` | `AUTH_TWITCH_CLIENT_ID` | null | Twitch OAuth client ID |
| `TwitchClientSecret` | `AUTH_TWITCH_CLIENT_SECRET` | null | Twitch OAuth client secret |
| `TwitchRedirectUri` | `AUTH_TWITCH_REDIRECT_URI` | null | Twitch OAuth callback URL (derived from ServiceDomain if not set) |
| `SteamApiKey` | `AUTH_STEAM_API_KEY` | null | Steam Web API key for ticket validation |
| `SteamAppId` | `AUTH_STEAM_APP_ID` | null | Steam App ID for ticket validation |
| `BcryptWorkFactor` | `AUTH_BCRYPT_WORK_FACTOR` | 12 | BCrypt work factor for password hashing (existing hashes at other factors still validate) |

**Note:** JWT core settings (`JwtSecret`, `JwtIssuer`, `JwtAudience`) are NOT in AuthServiceConfiguration. They live in the app-wide `AppConfiguration` singleton, which is constructor-injected into `TokenService`, `AuthService`, and `OAuthProviderService`. This is because JWT is cross-cutting platform infrastructure (`BANNOU_JWT_*`), not auth-specific config - nodes without the auth plugin still need JWT settings to validate tokens on authenticated endpoints.

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AuthService>` | Structured logging |
| `AuthServiceConfiguration` | Typed access to auth-specific config (OAuth, mock, TTLs) |
| `AppConfiguration` | App-wide config: JWT secret/issuer/audience, ServiceDomain, EffectiveAppId |
| `IAccountClient` | Service mesh client for account CRUD operations |
| `ISubscriptionClient` | Service mesh client for subscription queries |
| `IStateStoreFactory` | Redis state store access (sessions, password resets, account-session indexes) |
| `IMessageBus` | Audit event publishing |
| `ITokenService` | JWT generation, refresh token management, token validation |
| `ISessionService` | Session CRUD, account-session indexing, invalidation, session lifecycle event publishing |
| `IOAuthProviderService` | OAuth URL construction, code exchange, user info retrieval, account linking, Steam ticket validation |
| `IEventConsumer` | Registers handlers for account.deleted, account.updated, subscription.updated |

## API Endpoints (Implementation Notes)

### Authentication (login, register)

Login verifies password with `BCrypt.Verify` against the hash stored in Account service. Registration hashes with `BCrypt.HashPassword(workFactor: 12)` and creates the account. Both flows generate a JWT + refresh token via `ITokenService` and return a `ConnectUrl` for WebSocket connection. Failed logins publish audit events for brute force detection but always return 401 (no information leakage about whether the account exists).

### OAuth (init, callback)

`InitOAuth` delegates to `OAuthProviderService.GetAuthorizationUrl` which builds the provider-specific authorization URL with appropriate client ID, redirect URI (with ServiceDomain fallback), and state parameter. `CompleteOAuth` exchanges the authorization code for user info via the provider's API through `IOAuthProviderService`, then finds-or-creates an account linked to that OAuth identity. The `oauth-link:{provider}:{providerId}` Redis key is the link between external identity and internal account.

### Steam

Uses the Steam Web API `ISteamUserAuth/AuthenticateUserTicket` endpoint via `IOAuthProviderService.ValidateSteamTicketAsync`. Validates the ticket, checks VAC/publisher bans, then finds-or-creates an account. Steam doesn't provide an email, so accounts are created with `Email = null`.

### Tokens (validate, refresh)

`ValidateToken` delegates to `TokenService.ValidateTokenAsync` which verifies the JWT signature, extracts the `session_key` claim, loads session data from Redis via `ISessionService`, validates data integrity (null checks on roles/authorizations), checks expiry, and returns the session's roles and authorizations. `RefreshToken` validates the refresh token against Redis, loads the account fresh from Account service, generates a new access token + new refresh token, and rotates the old refresh token out.

### Password (reset, confirm)

`RequestPasswordReset` always returns 200 regardless of whether the account exists (prevents email enumeration). If the account exists, it generates a cryptographically secure token via `GenerateSecureToken()`, stores it in Redis with TTL, and "sends" an email (currently a mock that logs to console). `ConfirmPasswordReset` validates the token, hashes the new password, and updates it via Account service.

### Sessions (list, terminate)

`GetSessions` validates the caller's JWT then returns all active sessions for their account. Expired sessions are lazily cleaned during this read. `TerminateSession` uses the session-id reverse index to find the session key, deletes it, and publishes `session.invalidated` via `ISessionService` for WebSocket disconnection.

### Logout

`LogoutAsync` validates the JWT via `ValidateTokenAsync` and uses the session key from the response directly. Supports single-session logout (deletes the current session) or all-sessions logout (fetches the account-sessions index and deletes all). Publishes `session.invalidated` via `ISessionService` after cleanup.

## Visual Aid

```
Login/Register/OAuth ──► TokenService.GenerateAccessTokenAsync()
                              │
                              ├─ SubscriptionClient.QueryCurrent() ──► authorizations
                              │
                              ├─ session:{key} ◄── SessionDataModel (roles, auths, expiry)
                              │
                              ├─ account-sessions:{accountId} ◄── [key1, key2, ...]
                              │
                              ├─ session-id-index:{sessionId} ──► sessionKey
                              │
                              └─ JWT (contains only session_key claim - opaque Redis key)
                                     │
                                     ▼
                              TokenService.ValidateTokenAsync (called by Connect on WS upgrade)
                                     │
                                     ├─ Verify JWT signature
                                     ├─ Extract session_key (opaque Redis lookup key)
                                     ├─ Load session from Redis via SessionService
                                     ├─ Validate data integrity (null roles/auths = corruption)
                                     └─ Return roles + authorizations + remaining time

account.deleted event ──► SessionService.InvalidateAllSessions ──► session.invalidated event
                                                                          │
                                                                          ▼
                                                                    Connect: disconnect WS
```

## Stubs & Unimplemented Features

### Email Sending (SendPasswordResetEmailAsync)
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/141 -->

Password reset generates tokens and constructs reset URLs correctly, but the actual email delivery is a mock that logs to the console. The method signature and flow are complete - only the SMTP/provider integration (SendGrid, AWS SES) is missing. The `PasswordResetBaseUrl` configuration exists for constructing the reset link.

### Audit Event Consumers
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/142 -->

Auth publishes 6 audit event types (login successful/failed, registration, OAuth, Steam, password reset) but no service subscribes to them. They exist for future security monitoring (brute force detection, anomaly alerts) but currently publish to topics nobody listens on.

## Potential Extensions

- **Rate limiting for login attempts**: Covered by [#142](https://github.com/beyond-immersion/bannou-service/issues/142) (Audit Event Consumers).
- **Email delivery integration**: Covered by [#141](https://github.com/beyond-immersion/bannou-service/issues/141) (Email Sending).
- **Token revocation list**: Currently, invalidating a session deletes it from Redis. A revocation list would allow checking validity even if Redis data is lost/expired.
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/143 -->
- **Multi-factor authentication**: The schema and service have no MFA concept. A TOTP or WebAuthn flow could be added as a second factor after password verification.
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/149 -->
- **OAuth token refresh**: The service exchanges OAuth codes for access tokens but doesn't store or refresh them. For ongoing provider API access (e.g., Discord presence), OAuth refresh tokens would need to be persisted.
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/150 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks

1. **ValidateTokenResponse.SessionId contains the session key, not the session ID**: Returns the internal Redis lookup key rather than the human-facing session identifier. This aligns with how Connect service tracks WebSocket connections, but the field name is misleading.

2. **Account-sessions index lazy cleanup**: Expired sessions are only removed from the account index when someone calls `GetAccountSessionsAsync`. Active accounts accumulate stale entries until explicitly listed.

3. **Password reset always returns 200**: By design for email enumeration prevention. No way for legitimate users to know if the reset email was sent.

4. **RefreshTokenAsync ignores the JWT parameter**: The refresh token alone is the credential for obtaining a new access token. Validating the (possibly expired) JWT would defeat the purpose of the refresh flow.

5. **DeviceInfo always returns "Unknown" placeholders**: `SessionService.GetAccountSessionsAsync` returns hardcoded device information (`Platform: "Unknown"`, `Browser: "Unknown"`, `DeviceType: Desktop`) because device capture is unimplemented. The constants `UNKNOWN_PLATFORM` and `UNKNOWN_BROWSER` exist for future implementation (SessionService.cs:21-23).

### Design Considerations (Requires Planning)

No design considerations pending.

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Completed

- **2026-01-30**: Removed "SendPasswordResetEmailAsync unused cancellation token" from Design Considerations. This is not a design issue - it's a natural consequence of the synchronous mock implementation and will be addressed when email integration is implemented (tracked by [#141](https://github.com/beyond-immersion/bannou-service/issues/141)).

- **2026-01-30**: Made `Email` properly nullable across Account and Auth services ([#151](https://github.com/beyond-immersion/bannou-service/issues/151)). OAuth/Steam accounts that don't provide email now honestly have `Email = null` instead of synthetic placeholder emails like `steam_123@oauth.local`. Changes:
  - `AccountResponse.Email` is now `string?` (nullable in schema)
  - `CreateAccountRequest.email` is now optional
  - `SessionDataModel.Email` is now `string?`
  - `AccountModel.Email` is now `string?`
  - Account lifecycle events (`AccountCreatedEvent`, etc.) have nullable `Email`
  - Removed synthetic email generation in `OAuthProviderService`
  - Password reset flow validates email exists before proceeding
