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

Uses the Steam Web API `ISteamUserAuth/AuthenticateUserTicket` endpoint via `IOAuthProviderService.ValidateSteamTicketAsync`. Validates the ticket, checks VAC/publisher bans, then finds-or-creates an account. Steam doesn't provide an email, so accounts get a synthetic email like `steam_{steamId}@oauth.local`.

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

Password reset generates tokens and constructs reset URLs correctly, but the actual email delivery is a mock that logs to the console. The method signature and flow are complete - only the SMTP/provider integration (SendGrid, AWS SES) is missing. The `PasswordResetBaseUrl` configuration exists for constructing the reset link.

### Audit Event Consumers

Auth publishes 6 audit event types (login successful/failed, registration, OAuth, Steam, password reset) but no service subscribes to them. They exist for future security monitoring (brute force detection, anomaly alerts) but currently publish to topics nobody listens on.

## Potential Extensions

- **Rate limiting for login attempts**: The `auth.login.failed` events exist for brute force detection, but no service consumes them. A rate-limiting mechanism (per-IP or per-account) could consume these events and block repeated failures.
- **Email delivery integration**: Replace the mock `SendPasswordResetEmailAsync` with actual SMTP/API-based email delivery.
- **Token revocation list**: Currently, invalidating a session deletes it from Redis. A revocation list would allow checking validity even if Redis data is lost/expired.
- **Multi-factor authentication**: The schema and service have no MFA concept. A TOTP or WebAuthn flow could be added as a second factor after password verification.
- **OAuth token refresh**: The service exchanges OAuth codes for access tokens but doesn't store or refresh them. For ongoing provider API access (e.g., Discord presence), OAuth refresh tokens would need to be persisted.

## Tenet Violations (Fix Immediately)

1. **[FOUNDATION]** Missing ArgumentNullException null checks on constructor-injected dependencies in `AuthService`.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, lines 53-62
   - **Violation**: T6 (Service Implementation Pattern) requires `?? throw new ArgumentNullException(nameof(...))` on all constructor parameters. All 10 dependencies are assigned directly without null guards.
   - **Fix**: Add `?? throw new ArgumentNullException(nameof(param))` to each assignment (e.g., `_accountClient = accountClient ?? throw new ArgumentNullException(nameof(accountClient));`).

2. **[FOUNDATION]** Missing ArgumentNullException null checks on constructor-injected dependencies in `TokenService`.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/TokenService.cs`, lines 42-48
   - **Violation**: T6 (Service Implementation Pattern). All 7 dependencies are assigned directly without null guards.
   - **Fix**: Add `?? throw new ArgumentNullException(nameof(param))` to each assignment.

3. **[FOUNDATION]** Missing ArgumentNullException null checks on constructor-injected dependencies in `SessionService`.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/SessionService.cs`, lines 30-33
   - **Violation**: T6 (Service Implementation Pattern). All 4 dependencies are assigned directly without null guards.
   - **Fix**: Add `?? throw new ArgumentNullException(nameof(param))` to each assignment.

4. **[FOUNDATION]** Missing ArgumentNullException null checks on constructor-injected dependencies in `OAuthProviderService`.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/OAuthProviderService.cs`, lines 49-55
   - **Violation**: T6 (Service Implementation Pattern). All 7 dependencies are assigned directly without null guards.
   - **Fix**: Add `?? throw new ArgumentNullException(nameof(param))` to each assignment.

5. **[IMPLEMENTATION]** Hardcoded constant `DEFAULT_CONNECT_URL = "ws://localhost:5014/connect"` used as comparison baseline.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, line 38
   - **Violation**: T21 (Configuration-First). The hardcoded URL is used to detect whether the user has customized the config (line 81: `_configuration.ConnectUrl != DEFAULT_CONNECT_URL`), but this duplicates the default already in the generated config class. If the schema default changes, this constant goes stale.
   - **Fix**: Remove the `DEFAULT_CONNECT_URL` constant and adjust the `EffectiveConnectUrl` logic to only check `!string.IsNullOrWhiteSpace(_configuration.ConnectUrl)` and ServiceDomain, since the generated config already provides the default.

6. **[IMPLEMENTATION]** Hardcoded fallback `60` minutes for password reset TTL.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, lines 734-736
   - **Violation**: T21 (Configuration-First). `var resetTokenTtlMinutes = _configuration.PasswordResetTokenTtlMinutes > 0 ? _configuration.PasswordResetTokenTtlMinutes : 60;` -- the fallback `60` is a hardcoded tunable. The generated config already has a default of `30`, so if the value is somehow `0`, the service should fail fast rather than silently use a different default.
   - **Fix**: Remove the fallback and use `_configuration.PasswordResetTokenTtlMinutes` directly. If zero is invalid, throw at startup or validate at the start of the method.

7. **[IMPLEMENTATION]** Hardcoded string `"mock-twitch-user-id-12345"` for Twitch mock provider ID.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/OAuthProviderService.cs`, line 586
   - **Violation**: T21 (Configuration-First). Discord and Google mock IDs use configuration (`MockDiscordId`, `MockGoogleId`), but Twitch uses a hardcoded string. A `MockTwitchId` config property should exist.
   - **Fix**: Add `MockTwitchId` to the auth configuration schema, use it in the switch expression instead of the hardcoded literal.

8. **[IMPLEMENTATION]** Hardcoded string `"000000"` as Steam mock ID fallback.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/OAuthProviderService.cs`, line 604
   - **Violation**: T21 (Configuration-First). `var mockId = _configuration.MockSteamId ?? "000000";` -- the config property `MockSteamId` is non-nullable with a default of `"76561198000000000"`, so this null-coalesce is dead code AND the fallback is a hardcoded magic string.
   - **Fix**: Remove the `?? "000000"` since `MockSteamId` always has a value.

9. **[IMPLEMENTATION]** Missing `ApiException` catch in `RefreshTokenAsync` account lookup.
   - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, lines 480-485
   - **Violation**: T7 (Error Handling). The account lookup catch block uses only `catch (Exception ex)` without a preceding `catch (ApiException ex)` to distinguish expected API failures (e.g., 404 account not found) from unexpected infrastructure failures.
   - **Fix**: Add `catch (ApiException ex) when (ex.StatusCode == 404)` to handle account not found explicitly before the generic `catch (Exception ex)`.

10. **[IMPLEMENTATION]** Missing `CancellationToken` parameter on `InvalidateAccountSessionsAsync`.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, line 1072
    - **Violation**: T16 (Naming Conventions) / consistency. The method signature is `Task InvalidateAccountSessionsAsync(Guid accountId)` but does not accept a `CancellationToken`, unlike all other async methods in the service. The underlying `InvalidateAllSessionsForAccountAsync` accepts one with a default.
    - **Fix**: Add `CancellationToken cancellationToken = default` parameter and pass it through.

11. **[QUALITY]** Logging sensitive information -- password reset token in mock email log.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, lines 786-797
    - **Violation**: T10 (Logging Standards). The mock email log includes the full password reset URL with the security token (`{ResetUrl}` which contains the token as a query parameter). While acceptable for development mocks, this should use LogDebug rather than LogInformation to prevent token leakage in production log aggregation.
    - **Fix**: Change `_logger.LogInformation` to `_logger.LogDebug` for the mock email output, or redact the token from the URL in the log.

12. **[QUALITY]** Excessive `LogInformation` usage for routine operations.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, multiple lines (105, 113, 119, 157, 167, 197, 206, 209, 223, 255, 466)
    - **Violation**: T10 (Logging Standards). Operations like "Looking up account by email", "Account found via service call", "Password hashed successfully", and "Processing login request" are routine debug-level operations, not significant state changes or business decisions. They should use `LogDebug`.
    - **Fix**: Change routine operation entry/progress logs from `LogInformation` to `LogDebug`. Reserve `LogInformation` for significant state changes (successful authentication, new account created, session invalidated).

13. **[QUALITY]** Missing XML documentation on `PasswordResetData` class members.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, lines 865-870
    - **Violation**: T19 (XML Documentation). The class has a `<summary>` but the `Email` property at line 868 (`public string Email { get; set; } = "";`) lacks `<summary>` documentation. Actually -- re-checking, `AccountId` (line 867) and `ExpiresAt` (line 869) also lack documentation. Only the class itself has a summary.
    - **Fix**: Add `<summary>` XML comments to all three properties of `PasswordResetData`.

14. **[QUALITY]** Missing XML documentation on `AuthServicePlugin.PluginName` and `DisplayName` properties.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthServicePlugin.cs`, lines 13-14
    - **Violation**: T19 (XML Documentation). Public properties `PluginName` and `DisplayName` lack `<summary>` tags. The `ConfigureServices` method also lacks `<param>` documentation.
    - **Fix**: Add `<summary>` to the properties, and `<param name="services">` to the method, or use `<inheritdoc/>` if base class documentation is sufficient.

15. **[QUALITY]** Missing XML documentation on `AuthController.InitOAuth` method parameters.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthController.cs`, lines 16-20
    - **Violation**: T19 (XML Documentation). The `InitOAuth` method has a `<summary>` but lacks `<param>` tags for `provider`, `redirectUri`, `state`, and `cancellationToken`, and lacks a `<returns>` tag.
    - **Fix**: Add `<param>` and `<returns>` XML documentation.

16. **[IMPLEMENTATION]** `SendPasswordResetEmailAsync` has unused `cancellationToken` parameter.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, line 775
    - **Violation**: T21 (Configuration-First) / code quality. The `cancellationToken` parameter is accepted but never used in the method body (the method is synchronous with `await Task.CompletedTask`). This is a dead parameter.
    - **Fix**: When email integration is added, wire the cancellation token through. For now, suppress or remove (since the method signature is internal and can be changed).

17. **[IMPLEMENTATION]** Hardcoded strings `"Unknown"` for device info in `SessionService.GetAccountSessionsAsync`.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/SessionService.cs`, lines 87-88
    - **Violation**: T21 (Configuration-First). `Platform = "Unknown"` and `Browser = "Unknown"` are hardcoded magic strings. While device capture is unimplemented, these should at minimum be class-level constants for discoverability.
    - **Fix**: Extract to named constants (e.g., `private const string UNKNOWN_PLATFORM = "Unknown";`) or use a configuration property if these are intended to be configurable defaults.

18. **[IMPLEMENTATION]** `SessionDataModel.Email` property uses empty string default with no validation.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/ISessionService.cs`, line 141
    - **Violation**: CLAUDE.md rule (no `= ""` / empty string defaults without justification). `public string Email { get; set; } = string.Empty;` -- if email is required for a session, this should throw on missing values; if optional, it should be `string?`. The empty string hides potential null-source bugs.
    - **Fix**: Make the property nullable (`string?`) since OAuth/Steam accounts may not have emails, and handle null explicitly in consuming code.

19. **[IMPLEMENTATION]** `SessionDataModel.SessionId` property uses empty string default with no validation.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/ISessionService.cs`, line 161
    - **Violation**: CLAUDE.md rule (no empty string defaults without justification). `public string SessionId { get; set; } = string.Empty;` -- SessionId is always set during token generation and is a GUID string. Should be typed as `Guid` per T25, or at minimum should be nullable to surface missing-value bugs rather than hiding them as empty strings.
    - **Fix**: Change to `public Guid SessionId { get; set; }` to use proper GUID type per T25 (Internal Model Type Safety), and adjust all consuming code that uses `string` SessionId to use `Guid.ToString()` at boundaries only.

20. **[IMPLEMENTATION]** `PasswordResetData.Email` property uses empty string literal default.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/AuthService.cs`, line 868
    - **Violation**: CLAUDE.md rule (no empty string defaults without justification). `public string Email { get; set; } = "";` -- Email is always populated from `account.Email` (which could itself be null for OAuth accounts). Should be nullable to surface bugs.
    - **Fix**: Change to `public string? Email { get; set; }` and handle null in consuming code, or validate non-null before storing.

21. **[IMPLEMENTATION]** Multiple `OAuthProviderService` response model properties use `= string.Empty` defaults.
    - **File**: `/home/lysander/repos/bannou/plugins/lib-auth/Services/OAuthProviderService.cs`, lines 763, 766, 781, 799, 805, 830, 835, 850, 852, 883, 886, 889, 903
    - **Violation**: CLAUDE.md rule. Private DTO classes like `DiscordTokenResponse`, `DiscordUserResponse`, etc. use `= string.Empty` on required fields (e.g., `AccessToken`, `Id`, `Username`). However, these are external service response DTOs where the properties may legitimately come back as null from the JSON deserialization -- this is the "External Service Defensive Coding" acceptable pattern from CLAUDE.md, but it lacks the required error log when unexpected null is encountered.
    - **Fix**: These are acceptable as defensive coding for external services, but each should have validation after deserialization (which exists via the `if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))` checks). No fix needed -- these follow the acceptable pattern with post-deserialization validation.

## Known Quirks & Caveats

1. **JWT config in AppConfiguration, not AuthServiceConfiguration**: JWT settings (`JwtSecret`, `JwtIssuer`, `JwtAudience`) live in the app-wide `AppConfiguration` singleton rather than `AuthServiceConfiguration`. This is intentional — nodes without the auth plugin still need JWT settings to validate tokens on authenticated endpoints. JWT is cross-cutting platform infrastructure (`BANNOU_JWT_*`), not auth-specific config. `TokenService`, `AuthService`, and `OAuthProviderService` all receive `AppConfiguration` via constructor injection.

2. **ValidateTokenResponse.SessionId contains the session key, not the session ID**: `ValidateTokenResponse.SessionId` is set to `Guid.Parse(sessionKey)` - the internal Redis lookup key - not `sessionData.SessionId` (the human-facing identifier). This is intentional so Connect service tracks WebSocket connections by the same key used in `account-sessions` index and `SessionInvalidatedEvent`, but the field name is misleading.

3. **Account-sessions index lazy cleanup only**: Expired sessions are only removed from the `account-sessions:{accountId}` list when someone calls `GetAccountSessionsAsync`. If nobody lists sessions, expired entries accumulate in the list until the list's own TTL expires. The TTL is JwtExpiration + 5 minutes, but gets reset with each new login (since `AddSessionToAccountIndexAsync` re-saves the whole list), so active accounts accumulate stale entries.

4. **Password reset always returns 200**: By design (email enumeration prevention), but this means there's no way for a legitimate user to know if the reset email was sent. The mock implementation logs to console, so in production without email integration, password resets silently succeed without doing anything useful.

5. **SessionInfo.LastActive updated on every token validation**: `SessionDataModel.LastActiveAtUnix` is updated each time `ValidateTokenAsync` successfully validates a session. This adds a Redis write to the validation path (which is otherwise read-only). The write preserves the remaining TTL so session expiry is unaffected. Sessions created before this field was introduced (where `LastActiveAtUnix` defaults to 0) fall back to `CreatedAt` for the `LastActive` display value.

6. **`RefreshTokenAsync` accepts but intentionally ignores the JWT parameter**: The `jwt` parameter is generated from the schema's `x-permissions: [{ role: user }]` declaration but is intentionally unused. Refresh tokens are designed to work when the access token has expired - validating the JWT would defeat the purpose of the refresh flow. The refresh token alone is the credential for obtaining a new access token.

7. **Hardcoded device info for all sessions**: `SessionService.GetAccountSessionsAsync` populates `DeviceInfo` with `DeviceType = Desktop`, `Platform = "Unknown"`, `Browser = "Unknown"` for every session. No device information is captured during authentication flows. Future work: capture user agent / platform during login and store in `SessionDataModel`, enabling device-specific JWT binding and accurate session reporting.
