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
| lib-state (IStateStoreFactory) | All session data, refresh tokens, OAuth links, password reset tokens, and edge revocation entries in Redis |
| lib-messaging (IMessageBus) | Publishing session lifecycle events and audit events |
| lib-account (IAccountClient) | Account CRUD: lookup by email, create, get by ID, update password, add auth methods |
| AppConfiguration (DI singleton) | JWT secret, issuer, audience, and ServiceDomain via constructor-injected config |
| IHttpClientFactory | HTTP calls to OAuth providers (Discord, Google, Twitch, Steam) and CloudFlare KV API |

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
| `session:{sessionKey}` | `SessionDataModel` | JwtExpirationMinutes * 60 | Active session data (accountId, email, roles, authorizations, expiry, jti) |
| `account-sessions:{accountId}` | `List<string>` | JwtExpirationMinutes * 60 + 300 | Index of session keys for an account (lazy-cleaned on read) |
| `session-id-index:{sessionId}` | `string` (sessionKey) | JwtExpirationMinutes * 60 | Reverse lookup: human-facing session ID to internal session key |
| `refresh_token:{token}` | `string` (accountId) | SessionTokenTtlDays in seconds | Maps refresh token to account ID |
| `oauth-link:{provider}:{providerId}` | `string` (accountId) | **None** | Maps OAuth provider identity to account (cleaned up on account deletion) |
| `account-oauth-links:{accountId}` | `List<string>` | **None** | Reverse index of OAuth link keys for account (for cleanup on deletion) |
| `login-attempts:{normalizedEmail}` | counter (Redis `INCR`) | LoginLockoutMinutes * 60 | Failed login attempt counter for rate limiting (via `ICacheableStateStore.IncrementAsync`) |
| `password-reset:{token}` | `PasswordResetData` | PasswordResetTokenTtlMinutes * 60 | Pending password reset (accountId, email, expiry) |

**Store**: `edge-revocation` (Backend: Redis) - Used when EdgeRevocationEnabled=true

| Key Pattern | Data Type | TTL | Purpose |
|-------------|-----------|-----|---------|
| `token:{jti}` | `TokenRevocationEntry` | Remaining token TTL | Revoked token entry with accountId, reason, expiry |
| `token-index` | `List<string>` | **None** | Index of all revoked token JTIs |
| `account:{accountId}` | `AccountRevocationEntry` | JwtExpirationMinutes * 60 + 300 | Account-level revocation (all tokens issued before a timestamp) |
| `account-index` | `List<string>` | **None** | Index of all revoked account IDs |
| `failed:{providerId}:token:{jti}` | `FailedEdgePushEntry` | **None** | Failed edge push awaiting retry |
| `failed:{providerId}:account:{accountId}` | `FailedEdgePushEntry` | **None** | Failed account revocation push awaiting retry |
| `failed-push-index` | `List<string>` | **None** | Index of all failed push keys |

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `session.invalidated` | `SessionInvalidatedEvent` | Logout, account deletion, admin action, security revocation |
| `session.updated` | `SessionUpdatedEvent` | Role changes propagated to sessions |
| `auth.login.successful` | `AuthLoginSuccessfulEvent` | Successful email/password login |
| `auth.login.failed` | `AuthLoginFailedEvent` | Failed login attempt (brute force detection) |
| `auth.registration.successful` | `AuthRegistrationSuccessfulEvent` | New account registered |
| `auth.oauth.successful` | `AuthOAuthLoginSuccessfulEvent` | Successful OAuth provider login |
| `auth.steam.successful` | `AuthSteamLoginSuccessfulEvent` | Successful Steam ticket verification |
| `auth.password-reset.successful` | `AuthPasswordResetSuccessfulEvent` | Password reset completed |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `account.deleted` | `HandleAccountDeletedAsync` | Invalidates all sessions, cleans up OAuth links via reverse index, pushes edge revocations if enabled, and publishes `session.invalidated` |
| `account.updated` | `HandleAccountUpdatedAsync` | If `changedFields` contains "roles", propagates new roles to all active sessions and publishes `session.updated` per session |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `JwtExpirationMinutes` | `AUTH_JWT_EXPIRATION_MINUTES` | 60 | How long access tokens and sessions are valid |
| `SessionTokenTtlDays` | `AUTH_SESSION_TOKEN_TTL_DAYS` | 7 | How long refresh tokens remain valid |
| `PasswordResetTokenTtlMinutes` | `AUTH_PASSWORD_RESET_TOKEN_TTL_MINUTES` | 30 | How long password reset tokens remain valid |
| `PasswordResetBaseUrl` | `AUTH_PASSWORD_RESET_BASE_URL` | null | Base URL for password reset links in emails (throws `InvalidOperationException` at runtime if null when password reset is attempted) |
| `ConnectUrl` | `AUTH_CONNECT_URL` | `ws://localhost:5014/connect` | WebSocket URL returned to clients after authentication (only used when `BANNOU_SERVICE_DOMAIN` is unset; see Quirk #9) |
| `MockProviders` | `AUTH_MOCK_PROVIDERS` | false | Enable mock OAuth for testing (bypasses real provider calls) |
| `MockDiscordId` | `AUTH_MOCK_DISCORD_ID` | `mock-discord-123456` | Mock Discord user ID for testing |
| `MockGoogleId` | `AUTH_MOCK_GOOGLE_ID` | `mock-google-123456` | Mock Google user ID for testing |
| `MockTwitchId` | `AUTH_MOCK_TWITCH_ID` | `mock-twitch-123456` | Mock Twitch user ID for testing |
| `MockSteamId` | `AUTH_MOCK_STEAM_ID` | `76561198000000000` | Mock Steam ID for testing |
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
| `MaxLoginAttempts` | `AUTH_MAX_LOGIN_ATTEMPTS` | 5 | Maximum failed login attempts before lockout (per email, uses Redis counter) |
| `LoginLockoutMinutes` | `AUTH_LOGIN_LOCKOUT_MINUTES` | 15 | Duration in minutes to lock out an email after exceeding MaxLoginAttempts |
| `BcryptWorkFactor` | `AUTH_BCRYPT_WORK_FACTOR` | 12 | BCrypt work factor for password hashing (existing hashes at other factors still validate) |
| `EdgeRevocationEnabled` | `AUTH_EDGE_REVOCATION_ENABLED` | false | Master switch for edge-layer token revocation (CloudFlare, OpenResty) |
| `EdgeRevocationTimeoutSeconds` | `AUTH_EDGE_REVOCATION_TIMEOUT_SECONDS` | 5 | Timeout for edge provider push operations (1-30 seconds) |
| `EdgeRevocationMaxRetryAttempts` | `AUTH_EDGE_REVOCATION_MAX_RETRY_ATTEMPTS` | 3 | Max retry attempts before giving up on failed pushes (1-10) |
| `CloudflareEdgeEnabled` | `AUTH_CLOUDFLARE_EDGE_ENABLED` | false | Enable CloudFlare Workers KV edge revocation |
| `CloudflareAccountId` | `AUTH_CLOUDFLARE_ACCOUNT_ID` | null | CloudFlare account ID (required when CloudflareEdgeEnabled=true) |
| `CloudflareKvNamespaceId` | `AUTH_CLOUDFLARE_KV_NAMESPACE_ID` | null | CloudFlare KV namespace ID for storing revoked tokens |
| `CloudflareApiToken` | `AUTH_CLOUDFLARE_API_TOKEN` | null | CloudFlare API token with Workers KV write permissions |
| `OpenrestyEdgeEnabled` | `AUTH_OPENRESTY_EDGE_ENABLED` | false | Enable OpenResty/NGINX edge revocation (reads from Redis directly) |

**Note:** JWT core settings (`JwtSecret`, `JwtIssuer`, `JwtAudience`) are NOT in AuthServiceConfiguration. They live in the app-wide `AppConfiguration` singleton, which is constructor-injected into `TokenService`, `AuthService`, and `OAuthProviderService`. This is because JWT is cross-cutting platform infrastructure (`BANNOU_JWT_*`), not auth-specific config - nodes without the auth plugin still need JWT settings to validate tokens on authenticated endpoints.

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AuthService>` | Structured logging |
| `AuthServiceConfiguration` | Typed access to auth-specific config (OAuth, mock, TTLs) |
| `AppConfiguration` | App-wide config: JWT secret/issuer/audience, ServiceDomain, EffectiveAppId |
| `IAccountClient` | Service mesh client for account CRUD operations |
| `IStateStoreFactory` | Redis state store access (sessions, password resets, account-session indexes, edge revocations) |
| `IMessageBus` | Audit event publishing |
| `ITokenService` | JWT generation, refresh token management, token validation |
| `ISessionService` | Session CRUD, account-session indexing, invalidation, session lifecycle event publishing, edge revocation coordination |
| `IOAuthProviderService` | OAuth URL construction, code exchange, user info retrieval, account linking, Steam ticket validation |
| `IEdgeRevocationService` | Coordinates token revocation across edge providers (CloudFlare, OpenResty) |
| `IEdgeRevocationProvider` (collection) | CloudflareEdgeProvider and OpenrestyEdgeProvider, injected into EdgeRevocationService via `IEnumerable<IEdgeRevocationProvider>` |
| `IEmailService` | Email sending abstraction (default: `ConsoleEmailService` logs to console; swap for SendGrid/SES in production) |
| `IHttpClientFactory` | Used by OAuthProviderService for OAuth API calls and CloudflareEdgeProvider for KV writes |
| `IEventConsumer` | Registers handlers for account.deleted, account.updated |

## API Endpoints (Implementation Notes)

### Authentication (login, register)

Login enforces per-email rate limiting via Redis counters before attempting authentication. If `MaxLoginAttempts` (default 5) is exceeded, the endpoint returns 401 for `LoginLockoutMinutes` (default 15 minutes) with no information leakage about whether the lockout is active or the account doesn't exist. On successful login, the counter is cleared. Login verifies password with `BCrypt.Verify` against the hash stored in Account service. Registration hashes with `BCrypt.HashPassword(workFactor: _configuration.BcryptWorkFactor)` (default 12) and creates the account. Both flows generate a JWT + refresh token via `ITokenService` and return a `ConnectUrl` for WebSocket connection. Failed logins increment the rate limit counter and publish audit events.

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

### Providers (list-providers)

`ListProvidersAsync` returns the list of available authentication providers based on which OAuth clients are configured. Checks for non-empty `DiscordClientId`, `GoogleClientId`, `TwitchClientId`, and `SteamApiKey` in configuration. Returns provider name, display name, auth type (oauth vs ticket), and init URL. Steam has `AuthUrl = null` because it uses session tickets from the game client, not browser redirects.

### InitOAuth (manual controller GET endpoint)

`InitOAuth` is implemented in the manual `AuthController.cs` partial class as an `[HttpGet]` endpoint, not via the generated interface. It receives `provider`, `redirectUri`, and optional `state` as query/route parameters, delegates to `AuthService.InitOAuthAsync`, and returns a 302 redirect to the OAuth provider's authorization URL. This is the only GET endpoint in Auth (browser-facing OAuth redirect flow exception to POST-only pattern).

### Revocation List (Edge Synchronization)

`GetRevocationListAsync` returns the current token revocation list for edge provider synchronization or admin monitoring. Delegates to `IEdgeRevocationService.GetRevocationListAsync`. Returns token-level revocations (by JTI) and account-level revocations (all tokens issued before a timestamp). Used by edge providers (CloudFlare Workers, OpenResty Lua scripts) to maintain their local blocklists.

## Visual Aid

```
Login/Register/OAuth ──► TokenService.GenerateAccessTokenAsync()
                              │
                              ├─ session:{key} ◄── SessionDataModel (accountId, roles, jti, expiry)
                              │
                              ├─ account-sessions:{accountId} ◄── [key1, key2, ...]
                              │
                              ├─ session-id-index:{sessionId} ──► sessionKey
                              │
                              └─ JWT (contains session_key + jti claims)
                                     │
                                     ▼
                              TokenService.ValidateTokenAsync (called by Connect on WS upgrade)
                                     │
                                     ├─ Verify JWT signature (issuer, audience, expiry)
                                     ├─ Extract session_key claim
                                     ├─ Load session from Redis via SessionService
                                     ├─ Validate data integrity (null roles = corruption)
                                     ├─ Update LastActiveAt timestamp
                                     └─ Return roles + authorizations + remaining time

account.deleted event ──► SessionService.InvalidateAllSessionsForAccountAsync
                              │
                              ├─ Collect JTIs from sessions
                              ├─ Delete session:{key} entries
                              ├─ Delete account-sessions:{accountId} index
                              ├─ Push to EdgeRevocationService (if enabled)
                              │       ├─ Store in edge-revocation Redis
                              │       └─ Push to CloudFlare/OpenResty providers
                              │
                              └─ Publish session.invalidated event
                                     │
                                     ▼
                               Connect: disconnect WS clients
```

## Stubs & Unimplemented Features

### Email Delivery Provider
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/141 -->

Password reset uses the `IEmailService` abstraction (defined in `bannou-service/Services/`) to send emails. The default `ConsoleEmailService` logs the email subject, recipient, and body to the console via `ILogger.LogDebug`. The flow is complete end-to-end - only a production `IEmailService` implementation (SendGrid, AWS SES, etc.) is needed, registered in `AuthServicePlugin.cs` to replace `ConsoleEmailService`. The `PasswordResetBaseUrl` configuration exists for constructing the reset link.

### Audit Event Consumers
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/142 -->

Auth publishes 6 audit event types (login successful/failed, registration, OAuth, Steam, password reset) but no service subscribes to them. They exist for future security monitoring (brute force detection, anomaly alerts) but currently publish to topics nobody listens on.

## Potential Extensions

- **Email delivery integration**: Covered by [#141](https://github.com/beyond-immersion/bannou-service/issues/141) (Email Delivery Provider).
- **Multi-factor authentication**: The schema and service have no MFA concept. A TOTP or WebAuthn flow could be added as a second factor after password verification.
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/149 -->
- **OAuth token refresh**: The service exchanges OAuth codes for access tokens but doesn't store or refresh them. For ongoing provider API access (e.g., Discord presence), OAuth refresh tokens would need to be persisted.
<!-- AUDIT:NEEDS_DESIGN:2026-01-30:https://github.com/beyond-immersion/bannou-service/issues/150 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Stale OAuth link not removed from reverse index on 404**: In `OAuthProviderService.FindOrCreateOAuthAccountAsync`, when a linked account returns 404, the `oauth-link:{provider}:{providerId}` key is deleted from the link store (line 431), but the corresponding entry is not removed from the `account-oauth-links:{accountId}` reverse index list. This leaves an orphaned entry in the reverse index. Impact is minor: on account deletion, `CleanupOAuthLinksForAccountAsync` will attempt to delete a non-existent link key (silent no-op), but the reverse index list slowly accumulates stale entries for accounts with repeatedly orphaned OAuth links.

### Intentional Quirks

1. **Account-sessions index lazy cleanup**: Expired sessions are only removed from the account index when someone calls `GetAccountSessionsAsync`. Active accounts accumulate stale entries until explicitly listed.

2. **Password reset always returns 200**: By design for email enumeration prevention. No way for legitimate users to know if the reset email was sent.

3. **RefreshTokenAsync ignores the JWT parameter**: The refresh token alone is the credential for obtaining a new access token. Validating the (possibly expired) JWT would defeat the purpose of the refresh flow.

4. **DeviceInfo always returns "Unknown" placeholders**: `SessionService.GetAccountSessionsAsync` returns hardcoded device information (`Platform: "Unknown"`, `Browser: "Unknown"`, `DeviceType: Desktop`) because device capture is unimplemented. The constants `UNKNOWN_PLATFORM` and `UNKNOWN_BROWSER` are defined at the top of `SessionService.cs`.

5. **Edge revocation is best-effort**: When `EdgeRevocationEnabled=true`, token revocations are pushed to configured edge providers (CloudFlare, OpenResty) but failures don't block session invalidation. Failed pushes are stored in a retry set and retried on subsequent revocation operations. If max retries are exceeded, the failure is logged but the session is still invalidated. This design prioritizes session invalidation reliability over edge propagation completeness.

6. **Edge revocation entries auto-expire**: Token-level revocations expire when the original JWT would have expired. Account-level revocations expire after `JwtExpirationMinutes * 60 + 300` seconds (JWT lifetime + 5-minute buffer). After this period, all access tokens issued before the revocation timestamp have naturally expired, making the revocation entry unnecessary.

7. **SessionDataModel and EdgeRevocationModels use Unix timestamp storage**: `SessionDataModel`, `TokenRevocationEntry`, and `AccountRevocationEntry` store all timestamps as `long` Unix epoch properties (e.g., `CreatedAtUnix`, `ExpiresAtUnix`) with `[JsonIgnore]` computed `DateTimeOffset` accessors. This avoids `System.Text.Json` `DateTimeOffset` serialization quirks in Redis. The `LastActiveAtUnix` field defaults to `0` for sessions created before the field was introduced; `GetAccountSessionsAsync` falls back to `CreatedAt` when `LastActiveAtUnix == 0`.

8. **InitOAuth is a manual GET controller endpoint**: Unlike all other Auth endpoints which are generated POST endpoints routed via WebSocket, `InitOAuth` is manually implemented in `AuthController.cs` as `[HttpGet("auth/oauth/{provider}/init")]` returning a 302 redirect. This is because OAuth authorization flows require browser redirects, not JSON responses.

9. **ConnectUrl is overridden by ServiceDomain when set**: The `EffectiveConnectUrl` property in `AuthService` ignores the configured `ConnectUrl` value when `AppConfiguration.ServiceDomain` is non-empty, deriving `wss://{ServiceDomain}/connect` instead. The `AUTH_CONNECT_URL` config value only takes effect when `BANNOU_SERVICE_DOMAIN` is unset. This means in production (where ServiceDomain is always set), the `ConnectUrl` configuration property is effectively dead.

### Design Considerations (Requires Planning)

No design considerations pending.

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.
