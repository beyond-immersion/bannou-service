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
| lib-subscription (ISubscriptionClient) | Fetches active subscriptions during token generation to embed authorizations in session |
| IHttpClientFactory | HTTP calls to external OAuth providers (Discord, Google, Twitch, Steam APIs) |
| Program.Configuration (static) | JWT secret, issuer, audience, and ServiceDomain (not from DI-injected config) |

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
| `oauth-link:{provider}:{providerId}` | `string` (accountId) | **None** | Maps OAuth provider identity to account (persists indefinitely) |
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
| `account.deleted` | `HandleAccountDeletedAsync` | Invalidates all sessions for the deleted account and publishes `session.invalidated` |
| `account.updated` | `HandleAccountUpdatedAsync` | If `changedFields` contains "roles", propagates new roles to all active sessions and publishes `session.updated` per session |
| `subscription.updated` | `HandleSubscriptionUpdatedAsync` | Re-fetches subscriptions from Subscription service, updates all sessions with new authorizations, publishes `session.updated` per session |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `JwtExpirationMinutes` | `AUTH_JWT_EXPIRATION_MINUTES` | 60 | How long access tokens and sessions are valid |
| `SessionTokenTtlDays` | `AUTH_SESSION_TOKEN_TTL_DAYS` | 30 | How long refresh tokens remain valid |
| `PasswordResetTokenTtlMinutes` | `AUTH_PASSWORD_RESET_TOKEN_TTL_MINUTES` | 60 | How long password reset tokens remain valid |
| `PasswordResetBaseUrl` | `AUTH_PASSWORD_RESET_BASE_URL` | required | Base URL for password reset links in emails |
| `ConnectUrl` | `AUTH_CONNECT_URL` | `ws://localhost:5014/connect` | WebSocket URL returned to clients after authentication |
| `MockProviders` | `AUTH_MOCK_PROVIDERS` | false | Enable mock OAuth for testing (bypasses real provider calls) |
| `MockDiscordId` | `AUTH_MOCK_DISCORD_ID` | - | Mock Discord user ID for testing |
| `MockGoogleId` | `AUTH_MOCK_GOOGLE_ID` | - | Mock Google user ID for testing |
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

**Note:** JWT core settings (`JwtSecret`, `JwtIssuer`, `JwtAudience`) are NOT in AuthServiceConfiguration. They live in the app-wide `Program.Configuration` and are accessed via static reference.

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AuthService>` | Structured logging |
| `AuthServiceConfiguration` | Typed access to auth-specific config (OAuth, mock, TTLs) |
| `IAccountClient` | Service mesh client for account CRUD operations |
| `ISubscriptionClient` | Service mesh client for subscription queries |
| `IStateStoreFactory` | Redis state store access |
| `IMessageBus` | Event publishing |
| `IHttpClientFactory` | HTTP clients for OAuth provider API calls |
| `ITokenService` | JWT generation, refresh token management, token validation |
| `ISessionService` | Session CRUD, account-session indexing, invalidation, event publishing |
| `IOAuthProviderService` | OAuth code exchange, user info retrieval, account linking |
| `IEventConsumer` | Registers handlers for account.deleted, account.updated, subscription.updated |

## API Endpoints (Implementation Notes)

### Authentication (login, register)

Login verifies password with `BCrypt.Verify` against the hash stored in Account service. Registration hashes with `BCrypt.HashPassword(workFactor: 12)` and creates the account. Both flows generate a JWT + refresh token and return a `ConnectUrl` for WebSocket connection. Failed logins publish audit events for brute force detection but always return 401 (no information leakage about whether the account exists).

### OAuth (init, callback)

`InitOAuth` builds the authorization URL manually with a switch statement. `CompleteOAuth` exchanges the authorization code for user info via the provider's API, then finds-or-creates an account linked to that OAuth identity. The `oauth-link:{provider}:{providerId}` Redis key is the link between external identity and internal account.

### Steam

Uses the Steam Web API `ISteamUserAuth/AuthenticateUserTicket` endpoint. Validates the ticket, checks VAC/publisher bans, then finds-or-creates an account. Steam doesn't provide an email, so accounts get a synthetic email like `steam_{steamId}@oauth.local`.

### Tokens (validate, refresh)

`ValidateToken` verifies the JWT signature, extracts the `session_key` claim, loads session data from Redis, checks expiry, and returns the session's roles and authorizations. `RefreshToken` validates the refresh token against Redis, loads the account fresh from Account service, generates a new access token + new refresh token, and rotates the old refresh token out.

### Password (reset, confirm)

`RequestPasswordReset` always returns 200 regardless of whether the account exists (prevents email enumeration). If the account exists, it generates a secure token, stores it in Redis with TTL, and "sends" an email (currently a mock that logs to console). `ConfirmPasswordReset` validates the token, hashes the new password, and updates it via Account service.

### Sessions (list, terminate)

`GetSessions` validates the caller's JWT then returns all active sessions for their account. Expired sessions are lazily cleaned during this read. `TerminateSession` uses the session-id reverse index to find the session key, deletes it, and publishes `session.invalidated` for WebSocket disconnection.

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
                              └─ JWT (contains session_key claim)
                                     │
                                     ▼
                              ValidateToken (called by Connect on WS upgrade)
                                     │
                                     ├─ Verify JWT signature
                                     ├─ Extract session_key
                                     ├─ Load session from Redis
                                     └─ Return roles + authorizations + remaining time

account.deleted event ──► InvalidateAllSessions ──► session.invalidated event
                                                          │
                                                          ▼
                                                    Connect: disconnect WS
```

## Stubs & Unimplemented Features

### Email Sending (SendPasswordResetEmailAsync)

Password reset generates tokens and constructs reset URLs correctly, but the actual email delivery is a mock that logs to the console. The method signature and flow are complete - only the SMTP/provider integration (SendGrid, AWS SES) is missing. The `PasswordResetBaseUrl` configuration exists for constructing the reset link.

### Audit Event Consumers

Auth publishes 6 audit event types (login successful/failed, registration, OAuth, Steam, password reset) but no service subscribes to them. They exist for future security monitoring (brute force detection, anomaly alerts) but currently publish to topics nobody listens on.

### ITokenService.ValidateTokenAsync (unused)

TokenService has a complete `ValidateTokenAsync` implementation that is never called. AuthService has its own inline version of the same logic in its `ValidateTokenAsync` method. The TokenService version was created during the service extraction but the AuthService method was never refactored to delegate to it.

## Potential Extensions

- **Rate limiting for login attempts**: The `auth.login.failed` events exist for brute force detection, but no service consumes them. A rate-limiting mechanism (per-IP or per-account) could consume these events and block repeated failures.
- **Email delivery integration**: Replace the mock `SendPasswordResetEmailAsync` with actual SMTP/API-based email delivery.
- **Token revocation list**: Currently, invalidating a session deletes it from Redis. A revocation list would allow checking validity even if Redis data is lost/expired.
- **Multi-factor authentication**: The schema and service have no MFA concept. A TOTP or WebAuthn flow could be added as a second factor after password verification.
- **OAuth token refresh**: The service exchanges OAuth codes for access tokens but doesn't store or refresh them. For ongoing provider API access (e.g., Discord presence), OAuth refresh tokens would need to be persisted.

## Known Quirks & Caveats

1. **Duplicate SessionDataModel class**: AuthService.cs contains a `private class SessionDataModel` (line 1168) that is identical to the public `SessionDataModel` in `ISessionService.cs`. The public version is what SessionService uses. The private version appears to be dead code from before the service extraction, but since AuthService accesses the state store directly in some methods (ValidateToken, Logout), it may be using this private type for deserialization. If the two ever diverge, sessions will silently fail to deserialize.

2. **Dead code: HashPassword/VerifyPassword**: Lines 1197-1208 contain a base64 "hash" function that concatenates the password with the JWT secret. This is never called - the actual login uses `BCrypt.Net.BCrypt.Verify()`. These methods would be a critical security vulnerability if they were ever used accidentally.

3. **Dead code: OnEventReceivedAsync**: A generic event routing method (line 1286) that only handles `account.deleted` via manual type checking. This is completely redundant with the typed `IEventConsumer` registration in `AuthServiceEvents.cs`. It appears to be from a previous event handling pattern that was never removed.

4. **Steam uses Provider.Discord enum**: `VerifySteamAuthAsync` (line 425) and the mock (line 1258) pass `Provider.Discord` to `FindOrCreateOAuthAccountAsync` with a `"steam"` provider override string. The Provider enum lacks a Steam variant, so Discord is used as a placeholder. The override string is what's actually used for the Redis key, so it works correctly, but it's semantically wrong and fragile.

5. **Duplicate OAuth URL construction**: `InitOAuthAsync` builds OAuth authorization URLs with its own switch statement. `OAuthProviderService.GetAuthorizationUrl` does the same thing. The two implementations differ subtly: InitOAuth falls back to `_configuration.DiscordRedirectUri` directly (no ServiceDomain fallback), while OAuthProviderService uses `GetEffectiveRedirectUri` which tries ServiceDomain. This means InitOAuth and CompleteOAuth can construct different redirect URIs, which would cause OAuth failures.

6. **ValidateTokenAsync not delegated to TokenService**: AuthService has its own inline `ValidateTokenAsync` (line 971) that duplicates logic from `TokenService.ValidateTokenAsync`. The AuthService version is what's actually called. The TokenService version is dead code. Both access `Program.Configuration.JwtSecret!` with a null-forgiving operator.

7. **JWT config split across two sources**: OAuth config, TTLs, and mock settings come from the DI-injected `AuthServiceConfiguration`. JWT secret/issuer/audience come from the static `Program.Configuration`. This split means JWT settings can't be overridden per-service and aren't visible in the auth configuration schema.

8. **SessionId field returns sessionKey, not sessionId**: `ValidateTokenResponse.SessionId` (line 1063) is set to `Guid.Parse(sessionKey)`, not to `sessionData.SessionId`. A comment explains this is intentional for Connect service WebSocket tracking, but it means the field name is misleading - what clients see as "SessionId" is actually the internal key.

9. **OAuth links have no TTL**: The `oauth-link:{provider}:{providerId}` key persists indefinitely in Redis. If an account is deleted, the link remains orphaned until the next login attempt for that OAuth identity (which detects the stale link and cleans it up). Meanwhile, orphaned links accumulate in Redis.

10. **BCrypt work factor hardcoded to 12**: The work factor for password hashing (lines 228, 889) is hardcoded rather than configurable. Changing it requires a code change and redeploy. Existing hashes at the old work factor continue to validate (BCrypt stores the factor in the hash), but new passwords always use 12.

11. **Refresh token is a GUID, not cryptographically random**: `GenerateRefreshToken()` uses `Guid.NewGuid().ToString("N")`. While GUIDs are unique, they're not designed to be cryptographically unpredictable in the same way that `GenerateSecureToken()` (used for password reset) is. The 256-bit `GenerateSecureToken` method exists but isn't used for refresh tokens.

12. **Account-sessions index lazy cleanup only**: Expired sessions are only removed from the `account-sessions:{accountId}` list when someone calls `GetAccountSessionsAsync`. If nobody lists sessions, expired entries accumulate in the list until the list's own TTL expires. The TTL is JwtExpiration + 5 minutes, but gets reset with each new login (since `AddSessionToAccountIndexAsync` re-saves the whole list), so active accounts accumulate stale entries.

13. **Logout validates the full token before extracting session key**: `LogoutAsync` calls `ValidateTokenAsync` and then separately calls `ExtractSessionKeyFromJwtAsync`. The validate step already extracts and validates the session_key claim - the second extraction is redundant. If the JWT is expired but the user wants to logout, this pattern prevents logout of expired sessions.

14. **Password reset always returns 200**: By design (email enumeration prevention), but this means there's no way for a legitimate user to know if the reset email was sent. The mock implementation logs to console, so in production without email integration, password resets silently succeed without doing anything useful.
