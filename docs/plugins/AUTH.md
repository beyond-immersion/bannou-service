# Auth Plugin Deep Dive

> **Plugin**: lib-auth
> **Schema**: schemas/auth-api.yaml
> **Version**: 4.0.0
> **Layer**: AppFoundation
> **State Store**: auth-statestore (Redis), edge-revocation-statestore (Redis)
> **Implementation Map**: [docs/maps/AUTH.md](../maps/AUTH.md)
> **Short**: Internet-facing authentication (email, OAuth, Steam, JWT, MFA, session management)

---

## Overview

The Auth plugin is the internet-facing authentication and session management service (L1 AppFoundation). Handles email/password login, OAuth provider integration (Discord, Google, Twitch), Steam session ticket verification, JWT token generation/validation, password reset flows, TOTP-based MFA, and session lifecycle management. It is the primary gateway between external users and the internal service mesh -- after authenticating, clients receive a JWT and a WebSocket connect URL to establish persistent connections via lib-connect.

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-connect | Calls `/auth/validate` via IAuthClient to validate JWTs on WebSocket connection establishment |
| lib-connect | Subscribes to `session.invalidated` to disconnect WebSocket clients when sessions are terminated |
| lib-permission | Subscribes to `session.updated` to recompile capability manifests when roles/authorizations change |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `JwtExpirationMinutes` | `AUTH_JWT_EXPIRATION_MINUTES` | 60 | How long access tokens and sessions are valid |
| `SessionTokenTtlDays` | `AUTH_SESSION_TOKEN_TTL_DAYS` | 7 | How long refresh tokens remain valid |
| `EmailProvider` | `AUTH_EMAIL_PROVIDER` | none | Email delivery provider enum: `none` (console logging), `sendgrid` (SendGrid API), `smtp` (SMTP via MailKit), `ses` (AWS SES v2) |
| `EmailFromAddress` | `AUTH_EMAIL_FROM_ADDRESS` | null | Sender email address for outgoing emails. Required when EmailProvider is not 'none'. |
| `EmailFromName` | `AUTH_EMAIL_FROM_NAME` | null | Display name for outgoing emails (e.g., 'Bannou Support'). Optional. |
| `SesAccessKeyId` | `AUTH_SES_ACCESS_KEY_ID` | null | AWS access key ID for SES API. Required when EmailProvider is 'ses'. |
| `SesSecretAccessKey` | `AUTH_SES_SECRET_ACCESS_KEY` | null | AWS secret access key for SES API. Required when EmailProvider is 'ses'. |
| `SesRegion` | `AUTH_SES_REGION` | us-east-1 | AWS region for SES API (e.g., us-east-1, eu-west-1) |
| `SendGridApiKey` | `AUTH_SENDGRID_API_KEY` | null | SendGrid API key. Required when EmailProvider is 'sendgrid'. |
| `SmtpHost` | `AUTH_SMTP_HOST` | null | SMTP server hostname. Required when EmailProvider is 'smtp'. |
| `SmtpPort` | `AUTH_SMTP_PORT` | 587 | SMTP server port (587 for STARTTLS, 465 for implicit SSL, 25 for unencrypted) |
| `SmtpUsername` | `AUTH_SMTP_USERNAME` | null | SMTP authentication username. Optional if server allows anonymous relay. |
| `SmtpPassword` | `AUTH_SMTP_PASSWORD` | null | SMTP authentication password. Optional if server allows anonymous relay. |
| `SmtpUseSsl` | `AUTH_SMTP_USE_SSL` | true | Use SSL/TLS when connecting to SMTP server |
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
| `MfaEncryptionKey` | `AUTH_MFA_ENCRYPTION_KEY` | null | AES-256-GCM encryption key for TOTP secrets at rest. Must be >= 32 characters. Validated at runtime when MFA operations are attempted. |
| `MfaIssuerName` | `AUTH_MFA_ISSUER_NAME` | Bannou | Issuer name displayed in authenticator apps (appears in the TOTP URI) |
| `MfaChallengeTtlMinutes` | `AUTH_MFA_CHALLENGE_TTL_MINUTES` | 5 | TTL for MFA challenge and setup tokens in Redis (1-30 minutes) |

**Note:** JWT core settings (`JwtSecret`, `JwtIssuer`, `JwtAudience`) are NOT in AuthServiceConfiguration. They live in the app-wide `AppConfiguration` singleton, which is constructor-injected into `TokenService`, `AuthService`, and `OAuthProviderService`. This is because JWT is cross-cutting platform infrastructure (`BANNOU_JWT_*`), not auth-specific config - nodes without the auth plugin still need JWT settings to validate tokens on authenticated endpoints.

## Visual Aid

```
Login ──► Password OK?
              │
              ├─ MFA disabled ──► TokenService.GenerateAccessTokenAsync()
              │                        │
              │                        ├─ session:{key} ◄── SessionDataModel
              │                        ├─ account-sessions:{accountId} ◄── [key1, ...]
              │                        ├─ session-id-index:{sessionId} ──► sessionKey
              │                        └─ JWT (session_key + jti claims)
              │
              └─ MFA enabled ──► MfaService.CreateMfaChallengeAsync()
                                      │
                                      ├─ mfa-challenge-{token} ◄── MfaChallengeData
                                      └─ Return LoginResponse { requiresMfa: true }
                                              │
                                              ▼
                                      /auth/mfa/verify (TOTP or recovery code)
                                              │
                                              └─ TokenService.GenerateAccessTokenAsync()

Register/OAuth ──► TokenService.GenerateAccessTokenAsync()
                        │
                        └─ (same session flow as above)
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

No Auth-specific stubs remain. Auth publishes 12 well-typed audit events (login success/fail, registration, OAuth x3, Steam, password reset, MFA enabled/disabled/verified/failed) and per-email rate limiting is production-ready via Redis counters. The remaining consumer gap — IP-level cross-account correlation, anomaly detection, and admin alerting — is an **Analytics (L4) responsibility**, tracked externally:
<!-- AUDIT:EXTERNAL:2026-03-03:https://github.com/beyond-immersion/bannou-service/issues/142 -->

## Potential Extensions

No extensions currently planned.

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks

1. **Account-sessions index lazy cleanup**: The account-sessions index uses Redis Sets for atomic add/remove operations (no read-modify-write races), but expired sessions are only removed from the set when someone calls `GetAccountSessionsAsync`. Active accounts accumulate stale entries until explicitly listed.

2. **Password reset always returns 200**: By design for email enumeration prevention. No way for legitimate users to know if the reset email was sent.

3. **RefreshTokenAsync ignores the JWT parameter**: The refresh token alone is the credential for obtaining a new access token. Validating the (possibly expired) JWT would defeat the purpose of the refresh flow.

4. **Edge revocation is best-effort**: When `EdgeRevocationEnabled=true`, token revocations are pushed to configured edge providers (CloudFlare, OpenResty) but failures don't block session invalidation. Failed pushes are stored in a retry set and retried on subsequent revocation operations. If max retries are exceeded, the failure is logged but the session is still invalidated. This design prioritizes session invalidation reliability over edge propagation completeness.

5. **Edge revocation entries auto-expire**: Token-level revocations expire when the original JWT would have expired. Account-level revocations expire after `JwtExpirationMinutes * 60 + 300` seconds (JWT lifetime + 5-minute buffer). After this period, all access tokens issued before the revocation timestamp have naturally expired, making the revocation entry unnecessary.

6. **SessionDataModel and EdgeRevocationModels use Unix timestamp storage**: `SessionDataModel`, `TokenRevocationEntry`, and `AccountRevocationEntry` store all timestamps as `long` Unix epoch properties (e.g., `CreatedAtUnix`, `ExpiresAtUnix`) with `[JsonIgnore]` computed `DateTimeOffset` accessors. This avoids `System.Text.Json` `DateTimeOffset` serialization quirks in Redis. The `LastActiveAtUnix` field defaults to `0` for sessions created before the field was introduced; `GetAccountSessionsAsync` falls back to `CreatedAt` when `LastActiveAtUnix == 0`.

7. **InitOAuth is a manual GET controller endpoint**: Unlike all other Auth endpoints which are generated POST endpoints routed via WebSocket, `InitOAuth` is manually implemented in `AuthController.cs` as `[HttpGet("auth/oauth/{provider}/init")]` returning a 302 redirect. This is because OAuth authorization flows require browser redirects, not JSON responses.

8. **ConnectUrl is overridden by ServiceDomain when set**: The `EffectiveConnectUrl` property in `AuthService` ignores the configured `ConnectUrl` value when `AppConfiguration.ServiceDomain` is non-empty, deriving `wss://{ServiceDomain}/connect` instead. The `AUTH_CONNECT_URL` config value only takes effect when `BANNOU_SERVICE_DOMAIN` is unset. This means in production (where ServiceDomain is always set), the `ConnectUrl` configuration property is effectively dead.

### Design Considerations (Requires Planning)

1. **DeviceInfo returns hardcoded placeholders**: `SessionService.GetAccountSessionsAsync` returns hardcoded device information (`Platform: "Unknown"`, `Browser: "Unknown"`, `DeviceType: Desktop`) because device capture is unimplemented. The constants `UNKNOWN_PLATFORM` and `UNKNOWN_BROWSER` are defined at the top of `SessionService.cs`. Requires design decisions: what device information to capture, how to extract it (User-Agent parsing, WebSocket handshake headers, client-reported data), and privacy implications of device fingerprinting.
<!-- AUDIT:NEEDS_DESIGN:2026-02-22:https://github.com/beyond-immersion/bannou-service/issues/449 -->

2. **Account merge session handling** (Auth-side impact of Account #137): Account merge is a complex cross-service operation (40+ services reference accounts). Auth's specific responsibility: handle a new `account.merged` event by invalidating all sessions for the source account (same as `account.deleted` path) and optionally refreshing target account sessions with merged roles/authorizations. The merge itself is an Account-layer orchestration problem; Auth's handler is straightforward. Low priority - post-launch compliance feature.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/137 -->

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

