# Auth Implementation Map

> **Plugin**: lib-auth
> **Schema**: schemas/auth-api.yaml
> **Layer**: AppFoundation
> **Deep Dive**: [docs/plugins/AUTH.md](../plugins/AUTH.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-auth |
| Layer | L1 AppFoundation |
| Endpoints | 19 (18 service-interface + 1 x-controller-only) |
| State Stores | auth-statestore (Redis), edge-revocation-statestore (Redis) |
| Events Published | 12 (session.invalidated, session.updated, auth.login.successful, auth.login.failed, auth.registration.successful, auth.oauth.successful, auth.steam.successful, auth.password-reset.successful, auth.mfa.enabled, auth.mfa.disabled, auth.mfa.verified, auth.mfa.failed) |
| Events Consumed | 2 (account.deleted, account.updated) |
| Client Events | 7 (auth.device-login, auth.suspicious-login, auth.external-account-linked, auth.password-changed, auth.mfa-enabled, auth.mfa-disabled, auth.session-terminated) |
| Background Services | 0 |

---

## State

**Store**: `auth-statestore` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `session:{sessionKey}` | `SessionDataModel` | Active session data (accountId, email, roles, authorizations, expiry, jti, deviceInfo, ipAddress). TTL = SessionTokenTtlDays * 86400 |
| `session-id-index:{sessionId}` | `string` (sessionKey) | Reverse lookup: human-facing session ID to internal session key. TTL = session TTL + 300s |
| `account-sessions:{accountId}` | Redis Set (`string`) | Per-account index of active session keys. No TTL; lazily pruned on read |
| `refresh_token:{refreshToken}` | `string` (accountId) | Maps refresh token to account ID. TTL = SessionTokenTtlDays * 86400 |
| `oauth-link:{provider}:{providerId}` | `string` (accountId) | Maps OAuth provider identity to account. No TTL |
| `account-oauth-links:{accountId}` | `List<string>` | Reverse index of OAuth link keys for account cleanup. No TTL |
| `login-attempts:{normalizedEmail}` | Counter (int) | Failed login attempt counter for rate limiting. TTL = LoginLockoutMinutes * 60 |
| `password-reset:{token}` | `PasswordResetData` | Pending password reset (accountId, email, expiry). TTL = PasswordResetTokenTtlMinutes * 60 |
| `mfa-challenge-{token}` | `MfaChallengeData` | MFA challenge token (accountId, deviceInfo, expiry). TTL = MfaChallengeTtlMinutes * 60 |
| `mfa-setup-{token}` | `MfaSetupData` | Pending MFA setup (encrypted secret, hashed recovery codes). TTL = MfaChallengeTtlMinutes * 60 |

**Store**: `edge-revocation-statestore` (Backend: Redis) — only active when EdgeRevocationEnabled=true

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `token:{jti}` | `TokenRevocationEntry` | Revoked token entry. TTL = remaining JWT lifetime |
| `account:{accountId}` | `AccountRevocationEntry` | Account-level revocation (all tokens before timestamp). TTL = JwtExpirationMinutes * 60 + 300 |
| `token-index` | `List<string>` | Index of all revoked token JTIs. No TTL |
| `account-index` | `List<string>` | Index of all revoked account IDs. No TTL |
| `failed:{providerId}:token:{jti}` | `FailedEdgePushEntry` | Failed edge push awaiting retry. No TTL |
| `failed:{providerId}:account:{accountId}` | `FailedEdgePushEntry` | Failed account revocation push. No TTL |
| `failed-push-index` | `List<string>` | Index of all failed push keys. No TTL |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Auth store (sessions, tokens, rate limits, MFA) and EdgeRevocation store |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 12 typed audit events |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation |
| lib-account (IAccountClient) | L1 | Hard | Account CRUD: lookup by email/ID, create, update password/MFA, link auth methods |
| IEntitySessionRegistry | L1 | Hard | Push client events to account WebSocket sessions (hosted by Connect) |
| AppConfiguration | — | Hard | Cross-cutting JWT config (BANNOU_JWT_*) and ServiceDomain |
| IHttpClientFactory | — | Hard | OAuth provider HTTP calls and CloudFlare KV API (injected into helper services) |
| IHttpContextAccessor | — | Hard | Client IP extraction from X-Forwarded-For/X-Real-IP headers (reverse proxy aware) |

**Notes:**
- Auth subscribes to `account.deleted` for session invalidation per FOUNDATION TENETS's Account Deletion Cleanup Obligation. Sessions are ephemeral, TTL-bounded state — no data integrity violation occurs if the event is missed (sessions time out naturally). Auth is L1→L1, making this the simplest case of the account cleanup pattern.
- Auth is a leaf service at L1: calls only Account (L1) via service mesh. No L2+ dependencies.
- IEntitySessionRegistry is a shared DI interface hosted by Connect, not a generated service client.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `session.invalidated` | `SessionInvalidatedEvent` | Logout, session termination, account deletion (via SessionService) |
| `session.updated` | `SessionUpdatedEvent` | Role propagation to sessions (via SessionService) |
| `auth.login.successful` | `AuthLoginSuccessfulEvent` | Successful credential login or MFA verification |
| `auth.login.failed` | `AuthLoginFailedEvent` | Failed login (wrong credentials, rate limited, account not found) |
| `auth.registration.successful` | `AuthRegistrationSuccessfulEvent` | New account registered |
| `auth.oauth.successful` | `AuthOAuthLoginSuccessfulEvent` | Successful OAuth login (Discord, Google, Twitch) |
| `auth.steam.successful` | `AuthSteamLoginSuccessfulEvent` | Successful Steam ticket verification |
| `auth.password-reset.successful` | `AuthPasswordResetSuccessfulEvent` | Password reset completed |
| `auth.mfa.enabled` | `AuthMfaEnabledEvent` | MFA successfully enabled |
| `auth.mfa.disabled` | `AuthMfaDisabledEvent` | MFA disabled (self or admin, includes reason) |
| `auth.mfa.verified` | `AuthMfaVerifiedEvent` | Successful MFA verification (TOTP or recovery code) |
| `auth.mfa.failed` | `AuthMfaFailedEvent` | Failed MFA attempt (invalid code) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `account.deleted` | `HandleAccountDeletedAsync` | Invalidates all sessions (via SessionService), cleans up OAuth links (via OAuthProviderService), pushes edge revocations if enabled |
| `account.updated` | `HandleAccountUpdatedAsync` | If "roles" changed: propagates new roles to all active sessions, publishes `session.updated` per session. If "email" changed: updates email in all sessions (no event — email doesn't affect permissions) |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<AuthService>` | Structured logging |
| `AuthServiceConfiguration` | Auth-specific config (OAuth, MFA, TTLs, rate limits, edge revocation) |
| `AppConfiguration` | App-wide config: JwtSecret, JwtIssuer, JwtAudience, ServiceDomain |
| `IAccountClient` | Service mesh client for account CRUD |
| `IStateStoreFactory` | Redis state store access |
| `IMessageBus` | Event publishing |
| `IEntitySessionRegistry` | Client events to account WebSocket sessions |
| `ITelemetryProvider` | Activity span creation |
| `ITokenService` | JWT generation/validation, refresh token management |
| `ISessionService` | Session CRUD, account-session indexing, invalidation, session event publishing |
| `IOAuthProviderService` | OAuth code exchange, user info retrieval, account linking, Steam ticket validation |
| `IEdgeRevocationService` | Token/account revocation push to edge providers (CloudFlare, OpenResty) |
| `IEmailService` | Password reset email delivery (None/SendGrid/SMTP/SES — selected at startup) |
| `IMfaService` | TOTP secret generation/encryption, code validation, recovery code management |
| `IEventConsumer` | Registers handlers for account.deleted, account.updated |
| `IHttpContextAccessor` | Client IP extraction from request headers (X-Forwarded-For, X-Real-IP, RemoteIpAddress) |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| Login | POST /auth/login | anonymous | session, session-id-index, account-sessions, refresh_token, login-attempts | auth.login.successful / auth.login.failed |
| Register | POST /auth/register | anonymous | session, session-id-index, account-sessions, refresh_token | auth.registration.successful |
| InitOAuth | GET /auth/oauth/{provider}/init | anonymous | - | - |
| CompleteOAuth | POST /auth/oauth/{provider}/callback | anonymous | session, session-id-index, account-sessions, refresh_token, oauth-link | auth.oauth.successful |
| VerifySteamAuth | POST /auth/steam/verify | anonymous | session, session-id-index, account-sessions, refresh_token, oauth-link | auth.steam.successful |
| RefreshToken | POST /auth/refresh | user | session, session-id-index, account-sessions, refresh_token | - |
| ValidateToken | POST /auth/validate | user | session (TTL refresh) | - |
| Logout | POST /auth/logout | user | session, session-id-index, account-sessions | session.invalidated |
| GetSessions | POST /auth/sessions/list | user | account-sessions (lazy cleanup) | - |
| TerminateSession | POST /auth/sessions/terminate | user | session, session-id-index, account-sessions | session.invalidated |
| GetRevocationList | POST /auth/revocation-list | admin | - | - |
| RequestPasswordReset | POST /auth/password/reset | anonymous | password-reset | - |
| ConfirmPasswordReset | POST /auth/password/confirm | anonymous | password-reset | auth.password-reset.successful |
| ListProviders | POST /auth/providers | anonymous | - | - |
| SetupMfa | POST /auth/mfa/setup | user | mfa-setup | - |
| EnableMfa | POST /auth/mfa/enable | user | mfa-setup (consumed) | auth.mfa.enabled |
| DisableMfa | POST /auth/mfa/disable | user | - | auth.mfa.disabled |
| AdminDisableMfa | POST /auth/mfa/admin-disable | admin | - | auth.mfa.disabled |
| VerifyMfa | POST /auth/mfa/verify | anonymous | mfa-challenge (consumed), session, refresh_token | auth.mfa.verified / auth.mfa.failed, auth.login.successful |

---

## Methods

### Login
POST /auth/login | Roles: [anonymous]

```
IF body.Email or body.Password is empty -> 400
clientIp = GetClientIpAddress() // X-Forwarded-For -> X-Real-IP -> RemoteIpAddress
// Rate limiting via Redis counter
READ counter login-attempts:{normalizedEmail} -> 401 if >= MaxLoginAttempts
 PUBLISH auth.login.failed { reason: RateLimited, ipAddress: clientIp }
CALL IAccountClient.GetAccountByEmailAsync(email) -> 401 if 404 (ApiException)
 PUBLISH auth.login.failed { reason: AccountNotFound, ipAddress: clientIp }
 INCREMENT login-attempts:{normalizedEmail} (TTL: LoginLockoutMinutes * 60)
IF account.PasswordHash is empty -> 401
IF !BCrypt.Verify(password, hash)
 INCREMENT login-attempts:{normalizedEmail}
 PUBLISH auth.login.failed { reason: InvalidCredentials, accountId, attemptCount, ipAddress: clientIp }
 PUSH auth.suspicious-login to account sessions { attemptCount, ipAddress: clientIp }
 RETURN (401, null)
DELETE counter login-attempts:{normalizedEmail}
IF account.MfaEnabled
 // see helper: MfaService.CreateMfaChallengeAsync
 WRITE auth:mfa-challenge-{token} <- MfaChallengeData { accountId, deviceInfo, ipAddress: clientIp } (TTL: MfaChallengeTtlMinutes * 60)
 RETURN (200, LoginResponse { requiresMfa: true, mfaChallengeToken })
// No MFA — generate tokens
// see helper: TokenService.GenerateAccessTokenAsync (passes body.DeviceInfo, clientIp)
WRITE auth:session:{sessionKey} <- SessionDataModel { ..., deviceInfo, ipAddress: clientIp } (TTL: SessionTokenTtlDays * 86400)
WRITE auth:session-id-index:{sessionId} <- sessionKey (TTL: session TTL + 300)
ADD auth:account-sessions:{accountId} <- sessionKey (Redis Set SADD)
WRITE auth:refresh_token:{refreshToken} <- accountId (TTL: SessionTokenTtlDays * 86400)
PUBLISH auth.login.successful { accountId, username, sessionId, deviceInfo, ipAddress: clientIp }
PUSH auth.device-login to account sessions { loginSessionId, ipAddress: clientIp }
RETURN (200, LoginResponse { accountId, accessToken, refreshToken, expiresIn, connectUrl })
```

### Register
POST /auth/register | Roles: [anonymous]

```
IF body.Username or body.Password is empty -> 400
// Hash password
BCrypt.HashPassword(body.Password, workFactor: config.BcryptWorkFactor)
CALL IAccountClient.CreateAccountAsync(email, displayName, passwordHash)
 -> 409 if email exists (ApiException)
 -> 400 if invalid data (ApiException)
// see helper: TokenService.GenerateAccessTokenAsync (passes body.DeviceInfo)
WRITE auth:session:{sessionKey} <- SessionDataModel { ..., deviceInfo } (TTL: SessionTokenTtlDays * 86400)
WRITE auth:session-id-index:{sessionId} <- sessionKey (TTL: session TTL + 300)
ADD auth:account-sessions:{accountId} <- sessionKey (Redis Set SADD)
WRITE auth:refresh_token:{refreshToken} <- accountId (TTL: SessionTokenTtlDays * 86400)
PUBLISH auth.registration.successful { accountId, username, email, sessionId, deviceInfo }
RETURN (200, RegisterResponse { accountId, accessToken, refreshToken, connectUrl })
```

### InitOAuth
GET /auth/oauth/{provider}/init | Roles: [anonymous] | x-controller-only (302 redirect)

```
// Manual controller endpoint — browser-facing OAuth redirect (exception)
// see helper: OAuthProviderService.GetAuthorizationUrl
// Builds provider-specific authorization URL with client ID, redirect URI, state param
IF authUrl is null -> 500 (provider not configured)
RETURN (200, InitOAuthResponse { authorizationUrl })
// Controller wraps as 302 redirect to authorizationUrl
```

### CompleteOAuth
POST /auth/oauth/{provider}/callback | Roles: [anonymous]

```
IF body.Code is empty -> 400
IF config.MockProviders
 // see helper: OAuthProviderService.GetMockUserInfoAsync
 // Returns deterministic mock user info, skips real provider calls
IF provider is Discord/Google/Twitch
 // see helper: OAuthProviderService.Exchange{Provider}CodeAsync
 // HTTP call to provider API: exchange code for access token, fetch user info
 IF userInfo is null -> 401
// see helper: OAuthProviderService.FindOrCreateOAuthAccountAsync
READ auth:oauth-link:{provider}:{providerId}
IF exists
 CALL IAccountClient.GetAccountAsync(accountId)
ELSE
 CALL IAccountClient.GetAccountByAuthMethodAsync({provider}:{providerId})
 IF not found
 CALL IAccountClient.CreateAccountAsync(email, displayName)
 // isNewAccount = true
 WRITE auth:oauth-link:{provider}:{providerId} <- accountId
 WRITE auth:account-oauth-links:{accountId} <- append link key
IF account is null -> 500
// see helper: TokenService.GenerateAccessTokenAsync (passes body.DeviceInfo)
WRITE auth:session:{sessionKey} <- SessionDataModel { ..., deviceInfo } (TTL: SessionTokenTtlDays * 86400)
WRITE auth:session-id-index:{sessionId} <- sessionKey (TTL: session TTL + 300)
ADD auth:account-sessions:{accountId} <- sessionKey (Redis Set SADD)
WRITE auth:refresh_token:{refreshToken} <- accountId (TTL: SessionTokenTtlDays * 86400)
PUBLISH auth.oauth.successful { accountId, provider, providerUserId, sessionId, isNewAccount, deviceInfo }
PUSH auth.device-login to account sessions { loginSessionId }
IF isNewAccount
 PUSH auth.external-account-linked to account sessions { provider }
RETURN (200, AuthResponse { accountId, accessToken, refreshToken, expiresIn, connectUrl })
```

### VerifySteamAuth
POST /auth/steam/verify | Roles: [anonymous]

```
IF body.Ticket is empty -> 400
IF config.MockProviders
 // see helper: OAuthProviderService.GetMockSteamUserInfoAsync
IF SteamApiKey or SteamAppId not configured -> 500
// see helper: OAuthProviderService.ValidateSteamTicketAsync
// HTTP call to Steam Web API ISteamUserAuth/AuthenticateUserTicket
IF steamId is empty -> 401
// see helper: OAuthProviderService.FindOrCreateOAuthAccountAsync (provider=Steam)
// Same find-or-create flow as CompleteOAuth, email=null for Steam
WRITE auth:oauth-link:Steam:{steamId} <- accountId
WRITE auth:account-oauth-links:{accountId} <- append link key
// see helper: TokenService.GenerateAccessTokenAsync (passes body.DeviceInfo)
WRITE auth:session:{sessionKey} <- SessionDataModel { ..., deviceInfo } (TTL: SessionTokenTtlDays * 86400)
WRITE auth:session-id-index:{sessionId} <- sessionKey (TTL: session TTL + 300)
ADD auth:account-sessions:{accountId} <- sessionKey (Redis Set SADD)
WRITE auth:refresh_token:{refreshToken} <- accountId (TTL: SessionTokenTtlDays * 86400)
PUBLISH auth.steam.successful { accountId, steamId, sessionId, isNewAccount, deviceInfo }
PUSH auth.device-login to account sessions { loginSessionId }
IF isNewAccount
 PUSH auth.external-account-linked to account sessions { provider: Steam }
RETURN (200, AuthResponse { accountId, accessToken, refreshToken, expiresIn, connectUrl })
```

### RefreshToken
POST /auth/refresh | Roles: [user]

```
// jwt parameter intentionally unused — refresh token alone is the credential
IF body.RefreshToken is empty -> 400
// see helper: TokenService.ValidateRefreshTokenAsync
READ auth:refresh_token:{body.RefreshToken} -> 403 if null
CALL IAccountClient.GetAccountAsync(accountId) -> 401 if 404 (ApiException)
// see helper: TokenService.GenerateAccessTokenAsync (new session, passes body.DeviceInfo if provided)
WRITE auth:session:{sessionKey} <- SessionDataModel { ..., deviceInfo: body.DeviceInfo } (TTL: SessionTokenTtlDays * 86400)
WRITE auth:session-id-index:{sessionId} <- sessionKey (TTL: session TTL + 300)
ADD auth:account-sessions:{accountId} <- sessionKey (Redis Set SADD)
// Store new refresh token
WRITE auth:refresh_token:{newRefreshToken} <- accountId (TTL: SessionTokenTtlDays * 86400)
// Remove old refresh token
DELETE auth:refresh_token:{body.RefreshToken}
RETURN (200, AuthResponse { accountId, accessToken, refreshToken, expiresIn, connectUrl })
```

### ValidateToken
POST /auth/validate | Roles: [user]

```
IF jwt is empty -> 401
// see helper: TokenService.ValidateTokenAsync
// Verifies JWT signature (issuer, audience, expiry)
// Extracts session_key claim from JWT
READ auth:session:{sessionKey} -> 401 if null
IF session.Roles is null (data corruption) -> 401
IF session expired -> 401
// Update LastActiveAt and refresh TTL
WRITE auth:session:{sessionKey} <- session (TTL: remaining seconds)
RETURN (200, ValidateTokenResponse { accountId, sessionKey, roles, authorizations, remainingTime })
```

### Logout
POST /auth/logout | Roles: [user]

```
IF jwt is empty -> 401
CALL self.ValidateTokenAsync(jwt) -> 401 if invalid
IF body.AllSessions == true
 // see helper: SessionService.GetSessionKeysForAccountAsync
 READ SET auth:account-sessions:{accountId}
 FOREACH sessionKey in sessionKeys
 READ auth:session:{sessionKey}
 // Collect JTI for edge revocation
 DELETE auth:session:{sessionKey}
 DELETE auth:session-id-index:{sessionId}
 DELETE SET auth:account-sessions:{accountId}
ELSE
 // Single session logout
 READ auth:session:{sessionKey}
 DELETE auth:session:{sessionKey}
 REMOVE auth:account-sessions:{accountId} <- sessionKey (Redis Set SREM)
// Edge revocation (best-effort, if enabled)
IF EdgeRevocationEnabled and JTIs collected
 FOREACH (jti, ttl) in sessionsToRevoke
 WRITE edge:token:{jti} <- TokenRevocationEntry (TTL: remaining JWT lifetime)
 // Push to CloudFlare/OpenResty providers
PUBLISH session.invalidated { accountId, sessionIds, reason: Logout }
PUSH auth.session-terminated to account sessions { terminatedSessionId, reason: Logout }
RETURN (200)
```

### GetSessions
POST /auth/sessions/list | Roles: [user]

```
IF jwt is empty -> 401
CALL self.ValidateTokenAsync(jwt) -> 401 if invalid
// see helper: SessionService.GetAccountSessionsAsync
READ SET auth:account-sessions:{accountId}
FOREACH sessionKey in set
 READ auth:session:{sessionKey}
 IF null -> remove stale key from set (lazy cleanup)
 ELSE -> build SessionInfo (sessionId, createdAt, lastActive, deviceInfo)
RETURN (200, SessionsResponse { sessions })
```

### TerminateSession
POST /auth/sessions/terminate | Roles: [user]

```
// see helper: SessionService.FindSessionKeyBySessionIdAsync
READ auth:session-id-index:{body.SessionId} -> 404 if null
READ auth:session:{sessionKey}
DELETE auth:session:{sessionKey}
IF sessionData != null
 REMOVE auth:account-sessions:{accountId} <- sessionKey (Redis Set SREM)
DELETE auth:session-id-index:{body.SessionId}
// Edge revocation (best-effort, if enabled)
IF EdgeRevocationEnabled and sessionData.Jti != null
 WRITE edge:token:{jti} <- TokenRevocationEntry (TTL: remaining JWT lifetime)
IF sessionData != null
 PUBLISH session.invalidated { accountId, [sessionKey], reason: AdminAction }
 PUSH auth.session-terminated to account sessions { terminatedSessionId, reason: AdminAction }
RETURN (200)
```

### GetRevocationList
POST /auth/revocation-list | Roles: [admin]

```
IF !EdgeRevocationService.IsEnabled
 RETURN (200, RevocationListResponse { revokedTokens: [], revokedAccounts: [] })
// see helper: EdgeRevocationService.GetRevocationListAsync
IF body.IncludeTokens
 READ edge:token-index
 FOREACH jti in index (limited by body.Limit)
 READ edge:token:{jti}
 // Skip null entries (expired)
IF body.IncludeAccounts
 READ edge:account-index
 FOREACH accountId in index (limited by body.Limit)
 READ edge:account:{accountId}
 // Skip null entries (expired)
RETURN (200, RevocationListResponse { revokedTokens, revokedAccounts })
```

### RequestPasswordReset
POST /auth/password/reset | Roles: [anonymous]

```
IF body.Email is empty -> 400
CALL IAccountClient.GetAccountByEmailAsync(email)
 // 404 is silently swallowed — always returns 200 (enumeration protection)
IF account found AND account.Email is non-empty
 // see helper: TokenService.GenerateSecureToken
 WRITE auth:password-reset:{token} <- PasswordResetData { accountId, email, expiresAt }
 (TTL: PasswordResetTokenTtlMinutes * 60)
 // Fire-and-forget email — failures logged but never affect response
 // see helper: IEmailService.SendAsync
 // Throws InvalidOperationException if PasswordResetBaseUrl is null
RETURN (200)
// Always 200 regardless of account existence
```

### ConfirmPasswordReset
POST /auth/password/confirm | Roles: [anonymous]

```
IF body.Token or body.NewPassword is empty -> 400
READ auth:password-reset:{body.Token} -> 400 if null
IF resetData.ExpiresAt < now
 DELETE auth:password-reset:{body.Token} -> 400
BCrypt.HashPassword(body.NewPassword, workFactor: config.BcryptWorkFactor)
CALL IAccountClient.UpdatePasswordHashAsync(accountId, passwordHash)
DELETE auth:password-reset:{body.Token}
// Invalidate all sessions for security
// see helper: SessionService.InvalidateAllSessionsForAccountAsync
FOREACH session in account sessions
 DELETE auth:session:{sessionKey}
 DELETE auth:session-id-index:{sessionId}
DELETE SET auth:account-sessions:{accountId}
PUBLISH session.invalidated { accountId, sessionIds, reason: SecurityRevocation }
// Edge revocation (if enabled)
PUBLISH auth.password-reset.successful { accountId }
PUSH auth.password-changed to account sessions
RETURN (200)
```

### ListProviders
POST /auth/providers | Roles: [anonymous]

```
// Check which OAuth providers are configured
IF config.DiscordClientId is non-empty -> add Discord (OAuth, /auth/oauth/discord/init)
IF config.GoogleClientId is non-empty -> add Google (OAuth, /auth/oauth/google/init)
IF config.TwitchClientId is non-empty -> add Twitch (OAuth, /auth/oauth/twitch/init)
IF config.SteamApiKey is non-empty -> add Steam (Ticket, authUrl: null)
RETURN (200, ProvidersResponse { providers })
```

### SetupMfa
POST /auth/mfa/setup | Roles: [user]

```
// see helper: TokenService.ValidateTokenAsync
IF jwt invalid -> 401
CALL IAccountClient.GetAccountAsync(accountId) -> 404 if not found
IF account.MfaEnabled -> 409
// see helper: MfaService
// Generate TOTP secret (Base32), 10 recovery codes (xxxx-xxxx format)
// Encrypt secret with AES-256-GCM (key from MfaEncryptionKey config)
// Hash recovery codes with BCrypt
// Build otpauth:// URI for QR code
WRITE auth:mfa-setup-{token} <- MfaSetupData { accountId, encryptedSecret, hashedRecoveryCodes }
 (TTL: MfaChallengeTtlMinutes * 60)
RETURN (200, MfaSetupResponse { setupToken, totpUri, recoveryCodes })
```

### EnableMfa
POST /auth/mfa/enable | Roles: [user]

```
// see helper: TokenService.ValidateTokenAsync
IF jwt invalid -> 401
// Consume setup token (single-use)
// see helper: MfaService.ConsumeMfaSetupAsync
READ auth:mfa-setup-{body.SetupToken} -> 400 if null
DELETE auth:mfa-setup-{body.SetupToken}
IF setupData.AccountId != caller accountId -> 400
// Decrypt secret, validate TOTP code
IF !MfaService.ValidateTotp(secret, body.TotpCode) -> 400
// Persist MFA to account
CALL IAccountClient.UpdateMfaAsync(accountId, enabled: true, secret, recoveryCodes)
PUBLISH auth.mfa.enabled { accountId }
PUSH auth.mfa-enabled to account sessions
RETURN (200)
```

### DisableMfa
POST /auth/mfa/disable | Roles: [user]

```
// see helper: TokenService.ValidateTokenAsync
IF jwt invalid -> 401
CALL IAccountClient.GetAccountAsync(accountId) -> 404 if not found
IF !account.MfaEnabled -> 404
IF neither TotpCode nor RecoveryCode provided -> 400
IF body.TotpCode provided
 // Decrypt secret, validate TOTP
 IF invalid -> 400
ELSE IF body.RecoveryCode provided
 // Verify against BCrypt-hashed codes
 IF invalid -> 400
CALL IAccountClient.UpdateMfaAsync(accountId, enabled: false, secret: null, codes: null)
PUBLISH auth.mfa.disabled { accountId, disabledBy: Self }
PUSH auth.mfa-disabled to account sessions { disabledBy: Self }
RETURN (200)
```

### AdminDisableMfa
POST /auth/mfa/admin-disable | Roles: [admin]

```
CALL IAccountClient.GetAccountAsync(body.AccountId) -> 404 if not found
IF !account.MfaEnabled -> 404
// Admin override — no TOTP verification required
CALL IAccountClient.UpdateMfaAsync(accountId, enabled: false, secret: null, codes: null)
PUBLISH auth.mfa.disabled { accountId, disabledBy: Admin, adminReason }
PUSH auth.mfa-disabled to account sessions { disabledBy: Admin }
RETURN (200)
```

### VerifyMfa
POST /auth/mfa/verify | Roles: [anonymous]

```
// Consume challenge token (single-use — deleted BEFORE verification)
// see helper: MfaService.ConsumeMfaChallengeAsync -> (accountId, deviceInfo)
READ auth:mfa-challenge-{body.ChallengeToken} -> 401 if null
DELETE auth:mfa-challenge-{body.ChallengeToken}
// deviceInfo preserved from original login request via MfaChallengeData
CALL IAccountClient.GetAccountAsync(accountId) -> 500 if 404 (should not happen)
IF neither TotpCode nor RecoveryCode provided -> 400
IF body.TotpCode provided
 // Decrypt secret, validate TOTP
 IF invalid
 PUBLISH auth.mfa.failed { accountId, method: Totp, reason: InvalidCode }
 RETURN (400, null)
ELSE IF body.RecoveryCode provided
 // Verify against BCrypt-hashed codes
 IF invalid
 PUBLISH auth.mfa.failed { accountId, method: RecoveryCode, reason: InvalidCode }
 RETURN (400, null)
 // Remove used recovery code, update account (retry up to 3x on 409 Conflict)
 CALL IAccountClient.UpdateMfaAsync(accountId, enabled: true, secret, remainingCodes)
// MFA verified — generate full tokens
// see helper: TokenService.GenerateAccessTokenAsync (passes challengeDeviceInfo)
WRITE auth:session:{sessionKey} <- SessionDataModel { ..., deviceInfo: challengeDeviceInfo } (TTL: SessionTokenTtlDays * 86400)
WRITE auth:session-id-index:{sessionId} <- sessionKey (TTL: session TTL + 300)
ADD auth:account-sessions:{accountId} <- sessionKey (Redis Set SADD)
WRITE auth:refresh_token:{refreshToken} <- accountId (TTL: SessionTokenTtlDays * 86400)
PUBLISH auth.mfa.verified { accountId, method, sessionId, recoveryCodesRemaining, deviceInfo }
PUBLISH auth.login.successful { accountId, username, sessionId, deviceInfo }
PUSH auth.device-login to account sessions { loginSessionId }
RETURN (200, AuthResponse { accountId, accessToken, refreshToken, expiresIn, connectUrl })
```

---

## Background Services

No background services. Edge revocation retry is triggered inline on each revocation call, not by a background worker.
