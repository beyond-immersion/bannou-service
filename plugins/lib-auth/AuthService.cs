using BCrypt.Net;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Auth service implementation focused on authentication, token management, and OAuth provider integration.
/// Follows schema-first architecture - implements generated IAuthService interface.
/// </summary>
[BannouService("auth", typeof(IAuthService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.AppFoundation)]
public partial class AuthService : IAuthService
{
    private readonly IAccountClient _accountClient;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;

    // Helper services for better separation of concerns and testability
    private readonly ITokenService _tokenService;
    private readonly ISessionService _sessionService;
    private readonly IOAuthProviderService _oauthService;
    private readonly IEdgeRevocationService _edgeRevocationService;
    private readonly IEmailService _emailService;
    private readonly IMfaService _mfaService;

    public AuthService(
        IAccountClient accountClient,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        AuthServiceConfiguration configuration,
        AppConfiguration appConfiguration,
        ILogger<AuthService> logger,
        ITokenService tokenService,
        ISessionService sessionService,
        IOAuthProviderService oauthService,
        IEdgeRevocationService edgeRevocationService,
        IEmailService emailService,
        IMfaService mfaService,
        IEventConsumer eventConsumer)
    {
        _accountClient = accountClient;
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _configuration = configuration;
        _appConfiguration = appConfiguration;
        _logger = logger;
        _tokenService = tokenService;
        _sessionService = sessionService;
        _oauthService = oauthService;
        _edgeRevocationService = edgeRevocationService;
        _emailService = emailService;
        _mfaService = mfaService;

        // Register event handlers via partial class (AuthServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);

        _logger.LogInformation("AuthService initialized with JwtSecret length: {Length}, Issuer: {Issuer}, Audience: {Audience}, MockProviders: {MockProviders}",
            _appConfiguration.JwtSecret?.Length ?? 0, _appConfiguration.JwtIssuer, _appConfiguration.JwtAudience, _configuration.MockProviders);
    }

    /// <summary>
    /// Gets the effective WebSocket URL for client connections.
    /// Priority: BANNOU_SERVICE_DOMAIN (production) > AUTH_CONNECT_URL (configured default)
    /// </summary>
    private Uri EffectiveConnectUrl
    {
        get
        {
            // If ServiceDomain is configured, derive WebSocket URL from it (production pattern)
            var serviceDomain = _appConfiguration.ServiceDomain;
            if (!string.IsNullOrWhiteSpace(serviceDomain))
            {
                return new Uri($"wss://{serviceDomain}/connect");
            }

            // Use configured ConnectUrl (defaults to ws://localhost:5014/connect via schema)
            return new Uri(_configuration.ConnectUrl);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, LoginResponse?)> LoginAsync(
        LoginRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing login request for email: {Email}", body.Email);

        if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
        {
            return (StatusCodes.BadRequest, null);
        }

        // Rate limiting: check failed login attempts before any expensive operations
        var normalizedEmail = body.Email.Trim().ToLowerInvariant();
        var rateLimitKey = $"login-attempts:{normalizedEmail}";
        var authCacheStore = await _stateStoreFactory.GetCacheableStoreAsync<SessionDataModel>(StateStoreDefinitions.Auth, cancellationToken);
        var currentAttempts = await authCacheStore.GetCounterAsync(rateLimitKey, cancellationToken);

        if (currentAttempts.HasValue && currentAttempts.Value >= _configuration.MaxLoginAttempts)
        {
            _logger.LogWarning("Rate limit exceeded for email: {Email} ({Attempts} attempts)", body.Email, currentAttempts.Value);
            await PublishLoginFailedEventAsync(body.Email, AuthLoginFailedReason.RateLimited);
            // Return Unauthorized (not a dedicated 429) to avoid leaking rate-limit state to attackers.
            // The audit event carries the RateLimited reason for internal monitoring.
            return (StatusCodes.Unauthorized, null);
        }

        // Lookup account by email via AccountClient
        _logger.LogDebug("Looking up account by email via AccountClient: {Email}", body.Email);

        AccountResponse account;
        try
        {
            account = await _accountClient.GetAccountByEmailAsync(new GetAccountByEmailRequest { Email = body.Email }, cancellationToken);
            _logger.LogDebug("Account found via service call: {AccountId}", account?.AccountId);

            if (account == null)
            {
                _logger.LogWarning("No account found for email: {Email}", body.Email);
                return (StatusCodes.Unauthorized, null);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Account not found - return Unauthorized (don't reveal whether account exists)
            _logger.LogWarning("Account not found for email: {Email}", body.Email);
            await PublishLoginFailedEventAsync(body.Email, AuthLoginFailedReason.AccountNotFound);
            // Increment rate limit counter even for non-existent accounts to prevent enumeration
            await IncrementLoginAttemptCounterAsync(authCacheStore, rateLimitKey, cancellationToken);
            return (StatusCodes.Unauthorized, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to lookup account by email via AccountClient");
            await PublishErrorEventAsync("Login", ex.GetType().Name, ex.Message, dependency: "account");
            return (StatusCodes.InternalServerError, null);
        }

        // Verify password against stored hash
        if (string.IsNullOrWhiteSpace(account.PasswordHash))
        {
            _logger.LogWarning("Account has no password hash stored: {Email}", body.Email);
            return (StatusCodes.Unauthorized, null);
        }

        bool passwordValid = BCrypt.Net.BCrypt.Verify(body.Password, account.PasswordHash);

        if (!passwordValid)
        {
            _logger.LogWarning("Password verification failed for email: {Email}", body.Email);
            await PublishLoginFailedEventAsync(body.Email, AuthLoginFailedReason.InvalidCredentials, account.AccountId);
            await IncrementLoginAttemptCounterAsync(authCacheStore, rateLimitKey, cancellationToken);
            return (StatusCodes.Unauthorized, null);
        }

        // Login successful - clear rate limit counter
        await authCacheStore.DeleteCounterAsync(rateLimitKey, cancellationToken);
        _logger.LogDebug("Password verification successful for email: {Email}", body.Email);

        // Check if MFA is enabled for this account
        if (account.MfaEnabled)
        {
            var challengeToken = await _mfaService.CreateMfaChallengeAsync(account.AccountId, cancellationToken);
            _logger.LogInformation("MFA required for account {AccountId}, challenge issued", account.AccountId);

            return (StatusCodes.OK, new LoginResponse
            {
                AccountId = account.AccountId,
                RequiresMfa = true,
                MfaChallengeToken = challengeToken
            });
        }

        // No MFA - proceed with full token generation
        var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);

        var refreshToken = _tokenService.GenerateRefreshToken();

        // Store refresh token
        await _tokenService.StoreRefreshTokenAsync(account.AccountId, refreshToken, cancellationToken);

        _logger.LogInformation("Successfully authenticated user: {Email} (ID: {AccountId})",
            body.Email, account.AccountId);

        // Publish audit event for successful login
        await PublishLoginSuccessfulEventAsync(account.AccountId, body.Email, sessionId);

        return (StatusCodes.OK, new LoginResponse
        {
            AccountId = account.AccountId,
            RequiresMfa = false,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = EffectiveConnectUrl
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, RegisterResponse?)> RegisterAsync(
        RegisterRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing registration request for username: {Username}", body.Username);

        if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
        {
            return (StatusCodes.BadRequest, null);
        }

        // Hash password before storing
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(body.Password, workFactor: _configuration.BcryptWorkFactor);
        _logger.LogDebug("Password hashed successfully for registration");

        // Create account via AccountClient service call
        _logger.LogDebug("Creating account via AccountClient for registration: {Email}", body.Email);

        var createRequest = new CreateAccountRequest
        {
            Email = body.Email,
            DisplayName = body.Username,
            PasswordHash = passwordHash, // Store hashed password
            EmailVerified = false
        };

        AccountResponse? accountResult;
        try
        {
            accountResult = await _accountClient.CreateAccountAsync(createRequest, cancellationToken);
            _logger.LogDebug("Account created successfully via service call: {AccountId}", accountResult?.AccountId);

            if (accountResult == null)
            {
                _logger.LogWarning("AccountClient returned null response");
                return (StatusCodes.InternalServerError, null);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 409)
        {
            _logger.LogWarning("Account with email {Email} already exists", body.Email);
            return (StatusCodes.Conflict, null);
        }
        catch (ApiException ex) when (ex.StatusCode == 400)
        {
            _logger.LogWarning(ex, "Invalid account data for registration");
            return (StatusCodes.BadRequest, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create account via AccountClient");
            await PublishErrorEventAsync("Register", ex.GetType().Name, ex.Message, dependency: "account");
            return (StatusCodes.InternalServerError, null);
        }

        // Generate tokens (returns both accessToken and sessionId for event publishing)
        var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(accountResult, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();

        // Store refresh token
        await _tokenService.StoreRefreshTokenAsync(accountResult.AccountId, refreshToken, cancellationToken);

        _logger.LogInformation("Successfully registered user: {Username} with ID: {AccountId}",
            body.Username, accountResult.AccountId);

        // Publish audit event for successful registration
        await PublishRegistrationSuccessfulEventAsync(accountResult.AccountId, body.Username, body.Email, sessionId);

        return (StatusCodes.OK, new RegisterResponse
        {
            AccountId = accountResult.AccountId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ConnectUrl = EffectiveConnectUrl
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthResponse?)> CompleteOAuthAsync(
        Provider provider,
        OAuthCallbackRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing OAuth callback for provider: {Provider}", provider);

        if (string.IsNullOrWhiteSpace(body.Code))
        {
            _logger.LogWarning("OAuth callback missing authorization code");
            return (StatusCodes.BadRequest, null);
        }

        // Check for mock mode (for testing)
        if (_configuration.MockProviders)
        {
            return await HandleMockOAuthAsync(provider, cancellationToken);
        }

        // Exchange authorization code for tokens and get user info
        Services.OAuthUserInfo? userInfo = provider switch
        {
            Provider.Discord => await _oauthService.ExchangeDiscordCodeAsync(body.Code, cancellationToken),
            Provider.Google => await _oauthService.ExchangeGoogleCodeAsync(body.Code, cancellationToken),
            Provider.Twitch => await _oauthService.ExchangeTwitchCodeAsync(body.Code, cancellationToken),
            _ => null
        };

        if (userInfo == null)
        {
            _logger.LogWarning("Failed to get user info from OAuth provider: {Provider}", provider);
            return (StatusCodes.Unauthorized, null);
        }

        _logger.LogInformation("OAuth user info retrieved for provider {Provider}: ProviderId={ProviderId}, Email={Email}",
            provider, userInfo.ProviderId, userInfo.Email);

        // Find or create account linked to this OAuth identity
        var (account, isNewAccount) = await _oauthService.FindOrCreateOAuthAccountAsync(provider, userInfo, cancellationToken);
        if (account == null)
        {
            _logger.LogError("Failed to find or create account for OAuth user: {ProviderId}", userInfo.ProviderId);
            await PublishErrorEventAsync("CompleteOAuth", "account_creation_failed", "Failed to find or create account for OAuth user", dependency: "account", details: new { Provider = provider.ToString(), ProviderId = userInfo.ProviderId });
            return (StatusCodes.InternalServerError, null);
        }

        // Generate tokens (returns both accessToken and sessionId for event publishing)
        var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(account.AccountId, refreshToken, cancellationToken);

        _logger.LogInformation("OAuth authentication successful for account {AccountId} via {Provider}",
            account.AccountId, provider);

        // Publish audit event for successful OAuth login
        var providerId = userInfo.ProviderId ?? throw new InvalidOperationException($"OAuth user info missing ProviderId after successful exchange for provider {provider}");
        await PublishOAuthLoginSuccessfulEventAsync(account.AccountId, provider, providerId, sessionId, isNewAccount);

        return (StatusCodes.OK, new AuthResponse
        {
            AccountId = account.AccountId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = EffectiveConnectUrl
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthResponse?)> VerifySteamAuthAsync(
        SteamVerifyRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing Steam Session Ticket verification");

        if (string.IsNullOrWhiteSpace(body.Ticket))
        {
            _logger.LogWarning("Steam verification missing ticket");
            return (StatusCodes.BadRequest, null);
        }

        // Check for mock mode (for testing)
        if (_configuration.MockProviders)
        {
            return await HandleMockSteamAuthAsync(cancellationToken);
        }

        // Validate Steam configuration
        if (string.IsNullOrWhiteSpace(_configuration.SteamApiKey) ||
            string.IsNullOrWhiteSpace(_configuration.SteamAppId))
        {
            _logger.LogError("Steam API Key or App ID not configured");
            await PublishErrorEventAsync("VerifySteamAuth", "configuration_error", "Steam API Key or App ID not configured");
            return (StatusCodes.InternalServerError, null);
        }

        // Call Steam Web API to validate ticket
        var steamId = await _oauthService.ValidateSteamTicketAsync(body.Ticket, cancellationToken);
        if (string.IsNullOrEmpty(steamId))
        {
            _logger.LogWarning("Steam ticket validation failed");
            return (StatusCodes.Unauthorized, null);
        }

        _logger.LogInformation("Steam ticket validated successfully for SteamID: {SteamId}", steamId);

        // Find or create account linked to this Steam identity
        var suffix = steamId.Length >= 6 ? steamId.Substring(steamId.Length - 6) : steamId;
        var userInfo = new Services.OAuthUserInfo
        {
            ProviderId = steamId,
            DisplayName = $"Steam_{suffix}",
            Email = null // Steam doesn't provide email
        };

        var (account, isNewAccount) = await _oauthService.FindOrCreateOAuthAccountAsync(Provider.Steam, userInfo, cancellationToken);
        if (account == null)
        {
            _logger.LogError("Failed to find or create account for Steam user: {SteamId}", steamId);
            await PublishErrorEventAsync("VerifySteamAuth", "account_creation_failed", "Failed to find or create account for Steam user", dependency: "account", details: new { SteamId = steamId });
            return (StatusCodes.InternalServerError, null);
        }

        // Generate tokens (returns both accessToken and sessionId for event publishing)
        var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(account.AccountId, refreshToken, cancellationToken);

        _logger.LogInformation("Steam authentication successful for account {AccountId}", account.AccountId);

        // Publish audit event for successful Steam login
        await PublishSteamLoginSuccessfulEventAsync(account.AccountId, steamId, sessionId, isNewAccount);

        return (StatusCodes.OK, new AuthResponse
        {
            AccountId = account.AccountId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = EffectiveConnectUrl
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthResponse?)> RefreshTokenAsync(
        string jwt,
        RefreshRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Processing token refresh request");

        if (string.IsNullOrWhiteSpace(body.RefreshToken))
        {
            return (StatusCodes.BadRequest, null);
        }

        // The jwt parameter is generated from schema x-permissions but intentionally unused here.
        // Refresh tokens are designed to work when the access token has expired - validating
        // the JWT would defeat the purpose of the refresh flow. The refresh token alone is
        // the credential for obtaining a new access token.

        // Validate refresh token
        var accountId = await _tokenService.ValidateRefreshTokenAsync(body.RefreshToken, cancellationToken);
        if (!accountId.HasValue)
        {
            return (StatusCodes.Forbidden, null);
        }

        // Lookup account by ID via AccountClient
        _logger.LogDebug("Looking up account by ID via AccountClient: {AccountId}", accountId.Value);

        AccountResponse account;
        try
        {
            account = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = accountId.Value }, cancellationToken);
            _logger.LogDebug("Account found for refresh: {AccountId}", account?.AccountId);

            if (account == null)
            {
                _logger.LogWarning("No account found for ID: {AccountId}", accountId.Value);
                return (StatusCodes.Unauthorized, null);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Account not found - return Unauthorized (refresh token may be stale)
            _logger.LogWarning("Account not found for refresh token: {AccountId}", accountId.Value);
            return (StatusCodes.Unauthorized, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to lookup account by ID via AccountClient");
            await PublishErrorEventAsync("RefreshToken", ex.GetType().Name, ex.Message, dependency: "account");
            return (StatusCodes.InternalServerError, null);
        }

        // Generate new tokens (sessionId not used for refresh - no audit event needed)
        var (accessToken, _) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        // Store new refresh token and remove old one
        await _tokenService.StoreRefreshTokenAsync(accountId.Value, newRefreshToken, cancellationToken);
        await _tokenService.RemoveRefreshTokenAsync(body.RefreshToken, cancellationToken);

        return (StatusCodes.OK, new AuthResponse
        {
            AccountId = account.AccountId,
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = EffectiveConnectUrl
        });
    }


    /// <inheritdoc/>
    public async Task<(StatusCodes, InitOAuthResponse?)> InitOAuthAsync(
        Provider provider,
        string redirectUri,
        string? state,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing OAuth for provider: {Provider}", provider);

        var authUrl = _oauthService.GetAuthorizationUrl(provider, redirectUri, state);
        if (authUrl == null)
        {
            await PublishErrorEventAsync("InitOAuth", "configuration_error",
                $"OAuth provider {provider} is not properly configured - check client ID and redirect URI settings",
                details: new { Provider = provider.ToString() });
            return (StatusCodes.InternalServerError, null);
        }

        _logger.LogDebug("Generated OAuth URL for {Provider}", provider);
        return (StatusCodes.OK, new InitOAuthResponse { AuthorizationUrl = new Uri(authUrl) });
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> LogoutAsync(
        string jwt,
        LogoutRequest? body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing logout request. AllSessions: {AllSessions}", body?.AllSessions ?? false);

        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogWarning("JWT token is null or empty for logout");
            return StatusCodes.Unauthorized;
        }

        // Validate JWT and extract session key
        var (validateStatus, validateResponse) = await ValidateTokenAsync(jwt, cancellationToken);
        if (validateStatus != StatusCodes.OK || validateResponse == null || !validateResponse.Valid)
        {
            _logger.LogWarning("Invalid JWT token provided for logout");
            return StatusCodes.Unauthorized;
        }

        var sessionKey = validateResponse.SessionKey.ToString("N");

        var invalidatedSessions = new List<string>();
        var sessionsToRevoke = new List<(string jti, TimeSpan ttl)>();

        if (body?.AllSessions == true)
        {
            _logger.LogInformation("AllSessions logout requested for account: {AccountId}", validateResponse.AccountId);

            // Get session keys directly from the index (no need to load full session data first)
            var sessionKeys = await _sessionService.GetSessionKeysForAccountAsync(validateResponse.AccountId, cancellationToken);

            if (sessionKeys.Count > 0)
            {
                var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);

                // Load session data to get SessionIds for reverse index cleanup and JTIs for edge revocation, then delete
                foreach (var key in sessionKeys)
                {
                    var sessionData = await sessionStore.GetAsync($"session:{key}", cancellationToken);

                    // Collect JTI before deletion for edge revocation
                    if (sessionData?.Jti != null)
                    {
                        var remainingTtl = sessionData.ExpiresAt - DateTimeOffset.UtcNow;
                        if (remainingTtl > TimeSpan.Zero)
                        {
                            sessionsToRevoke.Add((sessionData.Jti, remainingTtl));
                        }
                    }

                    await sessionStore.DeleteAsync($"session:{key}", cancellationToken);

                    // Clean up reverse index if session data was still available
                    // Defensive: guard against corrupt Redis data (SessionId should always be populated)
                    if (sessionData != null && sessionData.SessionId != Guid.Empty)
                    {
                        await _sessionService.RemoveSessionIdReverseIndexAsync(sessionData.SessionId, cancellationToken);
                    }
                }

                // Remove the account sessions index
                await _sessionService.DeleteAccountSessionsIndexAsync(validateResponse.AccountId, cancellationToken);

                invalidatedSessions.AddRange(sessionKeys);

                _logger.LogInformation("All {SessionCount} sessions logged out for account: {AccountId}",
                    sessionKeys.Count, validateResponse.AccountId);
            }
            else
            {
                _logger.LogInformation("No active sessions found for account: {AccountId}", validateResponse.AccountId);
            }
        }
        else
        {
            // Logout current session only - load session data before deletion to get JTI
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);
            var sessionData = await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);

            // Collect JTI before deletion for edge revocation
            if (sessionData?.Jti != null)
            {
                var remainingTtl = sessionData.ExpiresAt - DateTimeOffset.UtcNow;
                if (remainingTtl > TimeSpan.Zero)
                {
                    sessionsToRevoke.Add((sessionData.Jti, remainingTtl));
                }
            }

            await sessionStore.DeleteAsync($"session:{sessionKey}", cancellationToken);

            // Remove session from account index
            await _sessionService.RemoveSessionFromAccountIndexAsync(validateResponse.AccountId, sessionKey, cancellationToken);

            invalidatedSessions.Add(sessionKey);

            _logger.LogInformation("Session logged out successfully for account: {AccountId}", validateResponse.AccountId);
        }

        // Push revocations to edge providers (defense-in-depth)
        if (_edgeRevocationService.IsEnabled && sessionsToRevoke.Count > 0)
        {
            _logger.LogDebug("Pushing {Count} token revocations to edge providers for logout, account {AccountId}",
                sessionsToRevoke.Count, validateResponse.AccountId);

            foreach (var (jti, ttl) in sessionsToRevoke)
            {
                try
                {
                    await _edgeRevocationService.RevokeTokenAsync(jti, validateResponse.AccountId, ttl, "Logout", cancellationToken);
                }
                catch (Exception ex)
                {
                    // Edge revocation failures should not block session invalidation
                    _logger.LogWarning(ex, "Failed to push edge revocation for JTI {Jti} during logout", jti);
                }
            }
        }

        // Publish session invalidation event for Connect service to disconnect clients
        if (invalidatedSessions.Count > 0)
        {
            await _sessionService.PublishSessionInvalidatedEventAsync(
                validateResponse.AccountId,
                invalidatedSessions,
                SessionInvalidatedEventReason.Logout,
                cancellationToken);
        }

        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> TerminateSessionAsync(
        string jwt,
        TerminateSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        var sessionId = body.SessionId;
        _logger.LogInformation("Terminating session: {SessionId}", sessionId);

        // Find and remove the session from Redis
        // Since we store sessions with session_key, we need to find sessions by session_id
        var sessionKey = await _sessionService.FindSessionKeyBySessionIdAsync(sessionId, cancellationToken);

        if (sessionKey == null)
        {
            _logger.LogWarning("Session {SessionId} not found for termination", sessionId);
            return StatusCodes.NotFound;
        }

        // Get session data to find account ID for index cleanup
        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);
        var sessionData = await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);

        // Remove the session data from Redis
        await sessionStore.DeleteAsync($"session:{sessionKey}", cancellationToken);

        // Remove session from account index if we found the session data
        if (sessionData != null)
        {
            await _sessionService.RemoveSessionFromAccountIndexAsync(sessionData.AccountId, sessionKey, cancellationToken);
        }

        // Remove reverse index entry
        await _sessionService.RemoveSessionIdReverseIndexAsync(sessionId, cancellationToken);

        // Push revocation to edge providers (defense-in-depth)
        if (_edgeRevocationService.IsEnabled && sessionData?.Jti != null)
        {
            var remainingTtl = sessionData.ExpiresAt - DateTimeOffset.UtcNow;
            if (remainingTtl > TimeSpan.Zero)
            {
                try
                {
                    await _edgeRevocationService.RevokeTokenAsync(
                        sessionData.Jti, sessionData.AccountId, remainingTtl, "AdminAction", cancellationToken);
                }
                catch (Exception ex)
                {
                    // Edge revocation failures should not block session termination
                    _logger.LogWarning(ex, "Failed to push edge revocation for JTI {Jti} during session termination", sessionData.Jti);
                }
            }
        }

        // Publish SessionInvalidatedEvent to disconnect WebSocket clients
        if (sessionData != null)
        {
            await _sessionService.PublishSessionInvalidatedEventAsync(
                sessionData.AccountId,
                new List<string> { sessionKey },
                SessionInvalidatedEventReason.AdminAction,
                cancellationToken);
        }

        _logger.LogInformation("Session {SessionId} terminated successfully", sessionId);
        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> RequestPasswordResetAsync(
        PasswordResetRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing password reset request for email: {Email}", body.Email);

        if (string.IsNullOrWhiteSpace(body.Email))
        {
            return StatusCodes.BadRequest;
        }

        // Verify account exists (but always return success to prevent email enumeration)
        AccountResponse? account = null;
        try
        {
            account = await _accountClient.GetAccountByEmailAsync(
                new GetAccountByEmailRequest { Email = body.Email },
                cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Account not found - log but return success to prevent enumeration
            _logger.LogInformation("Password reset requested for non-existent email: {Email}", body.Email);
        }

        if (account != null)
        {
            // Password reset requires an email. We looked up by email, so account.Email
            // should match body.Email. If somehow null (data corruption), skip silently
            // to maintain enumeration protection.
            var accountEmail = account.Email;
            if (string.IsNullOrEmpty(accountEmail))
            {
                _logger.LogWarning(
                    "Account {AccountId} found by email lookup but has null/empty email - possible data corruption",
                    account.AccountId);
                return StatusCodes.OK; // Enumeration protection: always return success
            }

            // Generate secure reset token
            var resetToken = _tokenService.GenerateSecureToken();
            var resetTokenTtlMinutes = _configuration.PasswordResetTokenTtlMinutes;

            var resetData = new PasswordResetData
            {
                AccountId = account.AccountId,
                Email = accountEmail,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(resetTokenTtlMinutes)
            };

            // Store reset token in Redis with TTL
            var resetStore = _stateStoreFactory.GetStore<PasswordResetData>(StateStoreDefinitions.Auth);
            await resetStore.SaveAsync(
                $"password-reset:{resetToken}",
                resetData,
                new StateOptions { Ttl = resetTokenTtlMinutes * 60 },
                cancellationToken);

            // Fire-and-forget email delivery: never let email failures affect the response.
            // Always return 200 for enumeration protection regardless of delivery outcome.
            try
            {
                await SendPasswordResetEmailAsync(accountEmail, resetToken, resetTokenTtlMinutes, cancellationToken);
            }
            catch (Exception emailEx)
            {
                _logger.LogError(emailEx, "Failed to send password reset email for account {AccountId}", account.AccountId);
                await PublishErrorEventAsync("SendPasswordResetEmail", emailEx.GetType().Name, emailEx.Message);
            }

            _logger.LogInformation("Password reset token generated for account {AccountId}, expires in {Minutes} minutes",
                account.AccountId, resetTokenTtlMinutes);
        }

        // Always return success to prevent email enumeration attacks
        return StatusCodes.OK;
    }

    /// <summary>
    /// Send password reset email via IEmailService abstraction.
    /// The concrete implementation (console, SendGrid, SMTP) is determined by AUTH_EMAIL_PROVIDER config.
    /// Caller must catch exceptions - email delivery failures must not affect the HTTP response.
    /// </summary>
    private async Task SendPasswordResetEmailAsync(string email, string resetToken, int expiresInMinutes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_configuration.PasswordResetBaseUrl))
        {
            throw new InvalidOperationException("PasswordResetBaseUrl configuration is not set. Cannot generate password reset link.");
        }
        var resetUrl = $"{_configuration.PasswordResetBaseUrl}?token={resetToken}";

        var subject = "Password Reset Request";
        var body = $"You requested a password reset for your account.\n" +
                    $"Click the link below to reset your password:\n" +
                    $"{resetUrl}\n" +
                    $"This link will expire in {expiresInMinutes} minutes.\n" +
                    $"If you did not request this reset, please ignore this email.";

        await _emailService.SendAsync(email, subject, body, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> ConfirmPasswordResetAsync(
        PasswordResetConfirmRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing password reset confirmation");

        if (string.IsNullOrWhiteSpace(body.Token) || string.IsNullOrWhiteSpace(body.NewPassword))
        {
            _logger.LogWarning("Invalid password reset confirmation request - missing token or password");
            return StatusCodes.BadRequest;
        }

        // Look up the reset token in Redis
        var resetStore = _stateStoreFactory.GetStore<PasswordResetData>(StateStoreDefinitions.Auth);
        var resetData = await resetStore.GetAsync($"password-reset:{body.Token}", cancellationToken);

        if (resetData == null)
        {
            _logger.LogWarning("Invalid or expired password reset token");
            return StatusCodes.BadRequest;
        }

        // Check if token has expired
        if (resetData.ExpiresAt < DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Password reset token has expired");
            // Clean up expired token
            await resetStore.DeleteAsync($"password-reset:{body.Token}", cancellationToken);
            return StatusCodes.BadRequest;
        }

        // Hash the new password
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(body.NewPassword, workFactor: _configuration.BcryptWorkFactor);

        // Update password via AccountClient
        await _accountClient.UpdatePasswordHashAsync(new UpdatePasswordRequest
        {
            AccountId = resetData.AccountId,
            PasswordHash = passwordHash
        }, cancellationToken);

        // Remove the used token
        await resetStore.DeleteAsync($"password-reset:{body.Token}", cancellationToken);

        _logger.LogInformation("Password reset successful for account {AccountId}", resetData.AccountId);

        // Publish audit event for successful password reset
        await PublishPasswordResetSuccessfulEventAsync(resetData.AccountId);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Data stored for password reset tokens.
    /// </summary>
    internal class PasswordResetData
    {
        public Guid AccountId { get; set; }
        public string? Email { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, SessionsResponse?)> GetSessionsAsync(
        string jwt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sessions requested");

        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogWarning("JWT token is null or empty for get sessions");
            return (StatusCodes.Unauthorized, null);
        }

        // Validate JWT and get account information
        var (validateStatus, validateResponse) = await ValidateTokenAsync(jwt, cancellationToken);
        if (validateStatus != StatusCodes.OK || validateResponse == null || !validateResponse.Valid)
        {
            _logger.LogWarning("Invalid JWT token provided for get sessions");
            return (StatusCodes.Unauthorized, null);
        }

        _logger.LogDebug("Getting sessions for account: {AccountId}", validateResponse.AccountId);

        // Use efficient account-to-sessions index with bulk state operations
        var sessions = await _sessionService.GetAccountSessionsAsync(validateResponse.AccountId, cancellationToken);

        _logger.LogDebug("Returning {SessionCount} session(s) for account: {AccountId}",
            sessions.Count, validateResponse.AccountId);

        return (StatusCodes.OK, new SessionsResponse
        {
            Sessions = sessions
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(
        string jwt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogWarning("JWT token is null or empty for validation");
            return (StatusCodes.Unauthorized, null);
        }

        return await _tokenService.ValidateTokenAsync(jwt, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ProvidersResponse?)> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing available authentication providers");
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must use await

        var providers = new List<ProviderInfo>();

        // Discord OAuth
        if (!string.IsNullOrEmpty(_configuration.DiscordClientId))
        {
            providers.Add(new ProviderInfo
            {
                Name = "discord",
                DisplayName = "Discord",
                AuthType = AuthType.Oauth,
                AuthUrl = new Uri($"/auth/oauth/discord/init", UriKind.Relative)
            });
        }

        // Google OAuth
        if (!string.IsNullOrEmpty(_configuration.GoogleClientId))
        {
            providers.Add(new ProviderInfo
            {
                Name = "google",
                DisplayName = "Google",
                AuthType = AuthType.Oauth,
                AuthUrl = new Uri($"/auth/oauth/google/init", UriKind.Relative)
            });
        }

        // Twitch OAuth
        if (!string.IsNullOrEmpty(_configuration.TwitchClientId))
        {
            providers.Add(new ProviderInfo
            {
                Name = "twitch",
                DisplayName = "Twitch",
                AuthType = AuthType.Oauth,
                AuthUrl = new Uri($"/auth/oauth/twitch/init", UriKind.Relative)
            });
        }

        // Steam (session ticket, not OAuth)
        if (!string.IsNullOrEmpty(_configuration.SteamApiKey))
        {
            providers.Add(new ProviderInfo
            {
                Name = "steam",
                DisplayName = "Steam",
                AuthType = AuthType.Ticket,
                AuthUrl = null // Steam uses session tickets from game client, not browser redirect
            });
        }

        _logger.LogInformation("Returning {ProviderCount} available authentication provider(s)", providers.Count);

        return (StatusCodes.OK, new ProvidersResponse
        {
            Providers = providers
        });
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, RevocationListResponse?)> GetRevocationListAsync(GetRevocationListRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting revocation list: IncludeTokens={IncludeTokens}, IncludeAccounts={IncludeAccounts}, Limit={Limit}",
            body.IncludeTokens, body.IncludeAccounts, body.Limit);

        if (!_edgeRevocationService.IsEnabled)
        {
            _logger.LogInformation("Edge revocation is disabled, returning empty list");
            return (StatusCodes.OK, new RevocationListResponse
            {
                RevokedTokens = new List<RevokedTokenEntry>(),
                RevokedAccounts = new List<RevokedAccountEntry>(),
                FailedPushCount = 0,
                TotalTokenCount = null
            });
        }

        var (tokens, accounts, failedCount, totalTokenCount) = await _edgeRevocationService.GetRevocationListAsync(
            body.IncludeTokens,
            body.IncludeAccounts,
            body.Limit,
            cancellationToken);

        _logger.LogInformation("Returning revocation list: {TokenCount} tokens, {AccountCount} accounts, {FailedCount} failed pushes",
            tokens.Count, accounts.Count, failedCount);

        return (StatusCodes.OK, new RevocationListResponse
        {
            RevokedTokens = tokens,
            RevokedAccounts = accounts,
            FailedPushCount = failedCount,
            TotalTokenCount = totalTokenCount
        });
    }

    #region MFA Endpoints

    /// <inheritdoc/>
    public async Task<(StatusCodes, MfaSetupResponse?)> SetupMfaAsync(
        string jwt,
        CancellationToken cancellationToken = default)
    {
        // Validate JWT and extract account
        var (validateStatus, validation) = await _tokenService.ValidateTokenAsync(jwt, cancellationToken);
        if (validateStatus != StatusCodes.OK || validation == null)
        {
            return (StatusCodes.Unauthorized, null);
        }

        // Get account to check current MFA status
        AccountResponse account;
        try
        {
            account = await _accountClient.GetAccountAsync(
                new GetAccountRequest { AccountId = validation.AccountId }, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return (StatusCodes.NotFound, null);
        }

        if (account.MfaEnabled)
        {
            _logger.LogWarning("MFA setup attempted but already enabled for account {AccountId}", account.AccountId);
            return (StatusCodes.Conflict, null);
        }

        // Generate TOTP secret and recovery codes
        var secret = _mfaService.GenerateSecret();
        var recoveryCodes = _mfaService.GenerateRecoveryCodes();

        // Encrypt secret and hash recovery codes for storage
        var encryptedSecret = _mfaService.EncryptSecret(secret);
        var hashedRecoveryCodes = _mfaService.HashRecoveryCodes(recoveryCodes);

        // Build otpauth:// URI for QR code
        var accountIdentifier = account.Email ?? account.AccountId.ToString();
        var totpUri = _mfaService.BuildTotpUri(secret, accountIdentifier);

        // Store setup data in Redis with TTL (pending confirmation)
        var setupToken = await _mfaService.CreateMfaSetupAsync(
            account.AccountId, encryptedSecret, hashedRecoveryCodes, cancellationToken);

        _logger.LogInformation("MFA setup initiated for account {AccountId}", account.AccountId);

        return (StatusCodes.OK, new MfaSetupResponse
        {
            SetupToken = setupToken,
            TotpUri = totpUri,
            RecoveryCodes = recoveryCodes
        });
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> EnableMfaAsync(
        string jwt,
        MfaEnableRequest body,
        CancellationToken cancellationToken = default)
    {
        // Validate JWT
        var (validateStatus, validation) = await _tokenService.ValidateTokenAsync(jwt, cancellationToken);
        if (validateStatus != StatusCodes.OK || validation == null)
        {
            return StatusCodes.Unauthorized;
        }

        // Consume setup token (single-use)
        var setupData = await _mfaService.ConsumeMfaSetupAsync(body.SetupToken, cancellationToken);
        if (setupData == null)
        {
            _logger.LogWarning("MFA enable failed: setup token not found or expired");
            return StatusCodes.BadRequest;
        }

        // Verify the setup token belongs to this account
        if (setupData.AccountId != validation.AccountId)
        {
            _logger.LogWarning("MFA enable failed: setup token account mismatch");
            return StatusCodes.BadRequest;
        }

        // Decrypt the secret to validate the TOTP code
        var secret = _mfaService.DecryptSecret(setupData.EncryptedSecret);
        if (!_mfaService.ValidateTotp(secret, body.TotpCode))
        {
            _logger.LogWarning("MFA enable failed: invalid TOTP code for account {AccountId}", validation.AccountId);
            return StatusCodes.BadRequest;
        }

        // Persist MFA settings to account via AccountClient
        await _accountClient.UpdateMfaAsync(new UpdateMfaRequest
        {
            AccountId = validation.AccountId,
            MfaEnabled = true,
            MfaSecret = setupData.EncryptedSecret,
            MfaRecoveryCodes = setupData.HashedRecoveryCodes
        }, cancellationToken);

        _logger.LogInformation("MFA enabled for account {AccountId}", validation.AccountId);
        await PublishMfaEnabledEventAsync(validation.AccountId);

        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> DisableMfaAsync(
        string jwt,
        MfaDisableRequest body,
        CancellationToken cancellationToken = default)
    {
        // Validate JWT
        var (validateStatus, validation) = await _tokenService.ValidateTokenAsync(jwt, cancellationToken);
        if (validateStatus != StatusCodes.OK || validation == null)
        {
            return StatusCodes.Unauthorized;
        }

        // Get account
        AccountResponse account;
        try
        {
            account = await _accountClient.GetAccountAsync(
                new GetAccountRequest { AccountId = validation.AccountId }, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return StatusCodes.NotFound;
        }

        if (!account.MfaEnabled)
        {
            return StatusCodes.NotFound;
        }

        // Require exactly one of totpCode or recoveryCode
        if (string.IsNullOrWhiteSpace(body.TotpCode) && string.IsNullOrWhiteSpace(body.RecoveryCode))
        {
            return StatusCodes.BadRequest;
        }

        // Verify the provided code
        if (!string.IsNullOrWhiteSpace(body.TotpCode))
        {
            var encryptedSecret = account.MfaSecret
                ?? throw new InvalidOperationException("Account has MFA enabled but no encrypted secret stored");
            var secret = _mfaService.DecryptSecret(encryptedSecret);
            if (!_mfaService.ValidateTotp(secret, body.TotpCode))
            {
                _logger.LogWarning("MFA disable failed: invalid TOTP code for account {AccountId}", account.AccountId);
                return StatusCodes.BadRequest;
            }
        }
        else if (!string.IsNullOrWhiteSpace(body.RecoveryCode))
        {
            var hashedCodes = account.MfaRecoveryCodes?.ToList()
                ?? throw new InvalidOperationException("Account has MFA enabled but no recovery codes stored");
            var (valid, _) = _mfaService.VerifyRecoveryCode(body.RecoveryCode, hashedCodes);
            if (!valid)
            {
                _logger.LogWarning("MFA disable failed: invalid recovery code for account {AccountId}", account.AccountId);
                return StatusCodes.BadRequest;
            }
        }

        // Clear MFA settings
        await _accountClient.UpdateMfaAsync(new UpdateMfaRequest
        {
            AccountId = account.AccountId,
            MfaEnabled = false,
            MfaSecret = null,
            MfaRecoveryCodes = null
        }, cancellationToken);

        _logger.LogInformation("MFA disabled by user for account {AccountId}", account.AccountId);
        await PublishMfaDisabledEventAsync(account.AccountId, MfaDisabledBy.Self, null);

        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> AdminDisableMfaAsync(
        AdminDisableMfaRequest body,
        CancellationToken cancellationToken = default)
    {
        // Get account
        AccountResponse account;
        try
        {
            account = await _accountClient.GetAccountAsync(
                new GetAccountRequest { AccountId = body.AccountId }, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return StatusCodes.NotFound;
        }

        if (!account.MfaEnabled)
        {
            return StatusCodes.NotFound;
        }

        // Admin override - no TOTP verification required
        await _accountClient.UpdateMfaAsync(new UpdateMfaRequest
        {
            AccountId = body.AccountId,
            MfaEnabled = false,
            MfaSecret = null,
            MfaRecoveryCodes = null
        }, cancellationToken);

        _logger.LogInformation("MFA disabled by admin for account {AccountId}, reason: {Reason}",
            body.AccountId, body.Reason ?? "(none)");
        await PublishMfaDisabledEventAsync(body.AccountId, MfaDisabledBy.Admin, body.Reason);

        return StatusCodes.OK;
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthResponse?)> VerifyMfaAsync(
        MfaVerifyRequest body,
        CancellationToken cancellationToken = default)
    {
        // Consume challenge token (single-use)
        var accountId = await _mfaService.ConsumeMfaChallengeAsync(body.ChallengeToken, cancellationToken);
        if (accountId == null)
        {
            _logger.LogWarning("MFA verify failed: challenge token not found or expired");
            return (StatusCodes.Unauthorized, null);
        }

        // Get account
        AccountResponse account;
        try
        {
            account = await _accountClient.GetAccountAsync(
                new GetAccountRequest { AccountId = accountId.Value }, cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogError("Account {AccountId} not found during MFA verify (should not happen)", accountId.Value);
            return (StatusCodes.InternalServerError, null);
        }

        // Require exactly one of totpCode or recoveryCode
        if (string.IsNullOrWhiteSpace(body.TotpCode) && string.IsNullOrWhiteSpace(body.RecoveryCode))
        {
            return (StatusCodes.BadRequest, null);
        }

        MfaVerificationMethod method;
        int? recoveryCodesRemaining = null;

        if (!string.IsNullOrWhiteSpace(body.TotpCode))
        {
            method = MfaVerificationMethod.Totp;
            var encryptedSecret = account.MfaSecret
                ?? throw new InvalidOperationException("Account has MFA enabled but no encrypted secret stored");
            var secret = _mfaService.DecryptSecret(encryptedSecret);
            if (!_mfaService.ValidateTotp(secret, body.TotpCode))
            {
                _logger.LogWarning("MFA verify failed: invalid TOTP code for account {AccountId}", accountId.Value);
                await PublishMfaFailedEventAsync(accountId.Value, MfaVerificationMethod.Totp, MfaFailedReason.InvalidCode);
                return (StatusCodes.BadRequest, null);
            }
        }
        else
        {
            method = MfaVerificationMethod.RecoveryCode;
            var hashedCodes = account.MfaRecoveryCodes?.ToList()
                ?? throw new InvalidOperationException("Account has MFA enabled but no recovery codes stored");
            var (valid, matchIndex) = _mfaService.VerifyRecoveryCode(body.RecoveryCode ?? string.Empty, hashedCodes);

            if (!valid)
            {
                _logger.LogWarning("MFA verify failed: invalid recovery code for account {AccountId}", accountId.Value);
                await PublishMfaFailedEventAsync(accountId.Value, MfaVerificationMethod.RecoveryCode, MfaFailedReason.InvalidCode);
                return (StatusCodes.BadRequest, null);
            }

            // Remove used recovery code and update account (with retry on conflict)
            hashedCodes.RemoveAt(matchIndex);
            recoveryCodesRemaining = hashedCodes.Count;

            if (hashedCodes.Count == 0)
            {
                _logger.LogWarning("Account {AccountId} has used all recovery codes", accountId.Value);
            }

            // Update recovery codes in account (retry on 409 Conflict)
            var updated = false;
            for (var attempt = 0; attempt < 3 && !updated; attempt++)
            {
                try
                {
                    await _accountClient.UpdateMfaAsync(new UpdateMfaRequest
                    {
                        AccountId = accountId.Value,
                        MfaEnabled = true,
                        MfaSecret = account.MfaSecret,
                        MfaRecoveryCodes = hashedCodes
                    }, cancellationToken);
                    updated = true;
                }
                catch (ApiException ex) when (ex.StatusCode == 409 && attempt < 2)
                {
                    // Re-fetch account for fresh recovery codes, re-verify, and retry
                    _logger.LogDebug("Conflict on recovery code update, retrying (attempt {Attempt})", attempt + 1);
                    account = await _accountClient.GetAccountAsync(
                        new GetAccountRequest { AccountId = accountId.Value }, cancellationToken);
                    hashedCodes = account.MfaRecoveryCodes?.ToList() ?? new List<string>();
                    var (reValid, reIndex) = _mfaService.VerifyRecoveryCode(body.RecoveryCode ?? string.Empty, hashedCodes);
                    if (reValid)
                    {
                        hashedCodes.RemoveAt(reIndex);
                        recoveryCodesRemaining = hashedCodes.Count;
                    }
                }
            }
        }

        // MFA verified - generate full tokens
        var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(accountId.Value, refreshToken, cancellationToken);

        _logger.LogInformation("MFA verification successful for account {AccountId} via {Method}", accountId.Value, method);
        await PublishMfaVerifiedEventAsync(accountId.Value, method, sessionId, recoveryCodesRemaining);
        await PublishLoginSuccessfulEventAsync(accountId.Value, account.Email ?? accountId.Value.ToString(), sessionId);

        return (StatusCodes.OK, new AuthResponse
        {
            AccountId = accountId.Value,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = EffectiveConnectUrl
        });
    }

    #endregion

    #region Private Helper Methods

    // Token generation and session management methods use ITokenService and ISessionService.
    // SessionDataModel is defined in ISessionService.cs (BeyondImmersion.BannouService.Auth.Services namespace).

    /// <summary>
    /// Increments the failed login attempt counter for rate limiting.
    /// Counter TTL is set to LoginLockoutMinutes so it auto-expires after the lockout window.
    /// </summary>
    private async Task IncrementLoginAttemptCounterAsync(
        ICacheableStateStore<SessionDataModel> cacheStore,
        string rateLimitKey,
        CancellationToken cancellationToken)
    {
        var lockoutTtlSeconds = _configuration.LoginLockoutMinutes * 60;
        await cacheStore.IncrementAsync(rateLimitKey, 1, new StateOptions { Ttl = lockoutTtlSeconds }, cancellationToken);
    }

    #endregion

    #region OAuth Mock Methods

    // OAuth response models and code exchange methods have been extracted to IOAuthProviderService
    // for improved testability and separation of concerns.

    /// <summary>
    /// Handle mock OAuth authentication for testing
    /// </summary>
    private async Task<(StatusCodes, AuthResponse?)> HandleMockOAuthAsync(Provider provider, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using mock OAuth provider for {Provider}", provider);

        var userInfo = await _oauthService.GetMockUserInfoAsync(provider, cancellationToken);
        var (account, _) = await _oauthService.FindOrCreateOAuthAccountAsync(provider, userInfo, cancellationToken);
        if (account == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        // Mock providers don't need audit events - discard sessionId
        var (accessToken, _) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(account.AccountId, refreshToken, cancellationToken);

        return (StatusCodes.OK, new AuthResponse
        {
            AccountId = account.AccountId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = EffectiveConnectUrl
        });
    }

    /// <summary>
    /// Handle mock Steam authentication for testing
    /// </summary>
    private async Task<(StatusCodes, AuthResponse?)> HandleMockSteamAuthAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using mock Steam provider");

        var userInfo = await _oauthService.GetMockSteamUserInfoAsync(cancellationToken);
        var (account, _) = await _oauthService.FindOrCreateOAuthAccountAsync(Provider.Steam, userInfo, cancellationToken);
        if (account == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        // Mock providers don't need audit events - discard sessionId
        var (accessToken, _) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(account.AccountId, refreshToken, cancellationToken);

        return (StatusCodes.OK, new AuthResponse
        {
            AccountId = account.AccountId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = EffectiveConnectUrl
        });
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Public wrapper for invalidating all sessions for a specific account.
    /// Called by AuthServiceEvents.HandleAccountDeletedAsync when account.deleted event is received.
    /// </summary>
    /// <param name="accountId">The account ID whose sessions should be invalidated.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InvalidateAccountSessionsAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        await _sessionService.InvalidateAllSessionsForAccountAsync(accountId, SessionInvalidatedEventReason.AccountDeleted, cancellationToken);
    }


    /// <summary>
    /// Propagate role changes to all active sessions for an account.
    /// Called when account.updated event is received with role changes.
    /// </summary>
    public async Task PropagateRoleChangesAsync(Guid accountId, List<string> newRoles, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Propagating role changes for account {AccountId}: {Roles}",
            accountId, string.Join(", ", newRoles));

        var sessionKeys = await _sessionService.GetSessionKeysForAccountAsync(accountId, cancellationToken);
        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);

        if (sessionKeys.Count == 0)
        {
            _logger.LogDebug("No sessions found for account {AccountId} to propagate role changes", accountId);
            return;
        }

        foreach (var sessionKey in sessionKeys)
        {
            try
            {
                var session = await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);

                if (session != null)
                {
                    session.Roles = newRoles;

                    // Preserve remaining TTL so sessions still expire on schedule
                    var remainingSeconds = (int)(session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds;
                    if (remainingSeconds <= 0)
                    {
                        _logger.LogDebug("Session {SessionKey} already expired, skipping role update", sessionKey);
                        continue;
                    }

                    await sessionStore.SaveAsync($"session:{sessionKey}", session,
                        new StateOptions { Ttl = remainingSeconds }, cancellationToken);

                    // Publish session.updated event for Permission service
                    await _sessionService.PublishSessionUpdatedEventAsync(
                        accountId,
                        session.SessionId,
                        newRoles,
                        session.Authorizations,
                        SessionUpdatedEventReason.RoleChanged,
                        cancellationToken);

                    _logger.LogDebug("Updated roles for session {SessionKey}", sessionKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update roles for session {SessionKey}", sessionKey);
            }
        }

        _logger.LogInformation("Propagated role changes to {Count} sessions for account {AccountId}",
            sessionKeys.Count, accountId);
    }

    /// <summary>
    /// Propagates email changes to all active sessions for an account.
    /// Updates the Email field in session data without publishing session.updated events
    /// (email changes do not affect permissions/capabilities).
    /// </summary>
    /// <param name="accountId">The account whose sessions should be updated.</param>
    /// <param name="newEmail">The new email address (null if email was removed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PropagateEmailChangeAsync(Guid accountId, string? newEmail, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Propagating email change for account {AccountId}", accountId);

        var sessionKeys = await _sessionService.GetSessionKeysForAccountAsync(accountId, cancellationToken);
        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);

        if (sessionKeys.Count == 0)
        {
            _logger.LogDebug("No sessions found for account {AccountId} to propagate email change", accountId);
            return;
        }

        var updatedCount = 0;
        foreach (var sessionKey in sessionKeys)
        {
            try
            {
                var session = await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);

                if (session != null)
                {
                    // Preserve remaining TTL so sessions still expire on schedule
                    var remainingSeconds = (int)(session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds;
                    if (remainingSeconds <= 0)
                    {
                        _logger.LogDebug("Session {SessionKey} already expired, skipping email update", sessionKey);
                        continue;
                    }

                    session.Email = newEmail;
                    await sessionStore.SaveAsync($"session:{sessionKey}", session,
                        new StateOptions { Ttl = remainingSeconds }, cancellationToken);

                    updatedCount++;
                    _logger.LogDebug("Updated email for session {SessionKey}", sessionKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update email for session {SessionKey}", sessionKey);
            }
        }

        _logger.LogInformation("Propagated email change to {Count} sessions for account {AccountId}",
            updatedCount, accountId);
    }

    #endregion

    #region Permission Registration

    #endregion

    #region Error Event Publishing

    /// <summary>
    /// Publishes an error event for unexpected/internal failures.
    /// Does NOT publish for validation errors or expected failure cases.
    /// </summary>
    private async Task PublishErrorEventAsync(
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        object? details = null)
    {
        await _messageBus.TryPublishErrorAsync(
            serviceName: "auth",
            operation: operation,
            errorType: errorType,
            message: message,
            dependency: dependency,
            details: details);
    }

    // Auth audit event topics
    private const string AUTH_LOGIN_SUCCESSFUL_TOPIC = "auth.login.successful";
    private const string AUTH_LOGIN_FAILED_TOPIC = "auth.login.failed";
    private const string AUTH_REGISTRATION_SUCCESSFUL_TOPIC = "auth.registration.successful";
    private const string AUTH_OAUTH_SUCCESSFUL_TOPIC = "auth.oauth.successful";
    private const string AUTH_STEAM_SUCCESSFUL_TOPIC = "auth.steam.successful";
    private const string AUTH_PASSWORD_RESET_SUCCESSFUL_TOPIC = "auth.password-reset.successful";
    private const string AUTH_MFA_ENABLED_TOPIC = "auth.mfa.enabled";
    private const string AUTH_MFA_DISABLED_TOPIC = "auth.mfa.disabled";
    private const string AUTH_MFA_VERIFIED_TOPIC = "auth.mfa.verified";
    private const string AUTH_MFA_FAILED_TOPIC = "auth.mfa.failed";

    /// <summary>
    /// Publish AuthLoginSuccessfulEvent for security audit trail.
    /// </summary>
    private async Task PublishLoginSuccessfulEventAsync(Guid accountId, string username, Guid sessionId)
    {
        try
        {
            var eventModel = new AuthLoginSuccessfulEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                Username = username,
                SessionId = sessionId
            };

            await _messageBus.TryPublishAsync(AUTH_LOGIN_SUCCESSFUL_TOPIC, eventModel);
            _logger.LogDebug("Published AuthLoginSuccessfulEvent for account {AccountId}, session {SessionId}", accountId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthLoginSuccessfulEvent for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Publish AuthLoginFailedEvent for brute force detection and security monitoring.
    /// </summary>
    private async Task PublishLoginFailedEventAsync(string username, AuthLoginFailedReason reason, Guid? accountId = null)
    {
        try
        {
            var eventModel = new AuthLoginFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Username = username,
                Reason = reason,
                AccountId = accountId
            };

            await _messageBus.TryPublishAsync(AUTH_LOGIN_FAILED_TOPIC, eventModel);
            _logger.LogDebug("Published AuthLoginFailedEvent for username {Username}, reason: {Reason}", username, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthLoginFailedEvent for username {Username}", username);
        }
    }

    /// <summary>
    /// Publish AuthRegistrationSuccessfulEvent for user onboarding analytics.
    /// </summary>
    private async Task PublishRegistrationSuccessfulEventAsync(Guid accountId, string username, string email, Guid sessionId)
    {
        try
        {
            var eventModel = new AuthRegistrationSuccessfulEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                Username = username,
                Email = email,
                SessionId = sessionId
            };

            await _messageBus.TryPublishAsync(AUTH_REGISTRATION_SUCCESSFUL_TOPIC, eventModel);
            _logger.LogDebug("Published AuthRegistrationSuccessfulEvent for account {AccountId}, session {SessionId}", accountId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthRegistrationSuccessfulEvent for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Publish AuthOAuthLoginSuccessfulEvent for OAuth provider analytics.
    /// </summary>
    private async Task PublishOAuthLoginSuccessfulEventAsync(Guid accountId, Provider provider, string providerUserId, Guid sessionId, bool isNewAccount)
    {
        try
        {
            var eventModel = new AuthOAuthLoginSuccessfulEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                Provider = provider,
                ProviderUserId = providerUserId,
                SessionId = sessionId,
                IsNewAccount = isNewAccount
            };

            await _messageBus.TryPublishAsync(AUTH_OAUTH_SUCCESSFUL_TOPIC, eventModel);
            _logger.LogDebug("Published AuthOAuthLoginSuccessfulEvent for account {AccountId} via {Provider}, session {SessionId}", accountId, provider, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthOAuthLoginSuccessfulEvent for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Publish AuthSteamLoginSuccessfulEvent for platform login analytics.
    /// </summary>
    private async Task PublishSteamLoginSuccessfulEventAsync(Guid accountId, string steamId, Guid sessionId, bool isNewAccount)
    {
        try
        {
            var eventModel = new AuthSteamLoginSuccessfulEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                SteamId = steamId,
                SessionId = sessionId,
                IsNewAccount = isNewAccount
            };

            await _messageBus.TryPublishAsync(AUTH_STEAM_SUCCESSFUL_TOPIC, eventModel);
            _logger.LogDebug("Published AuthSteamLoginSuccessfulEvent for account {AccountId}, session {SessionId}", accountId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthSteamLoginSuccessfulEvent for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Publish AuthPasswordResetSuccessfulEvent for security audit trail.
    /// </summary>
    private async Task PublishPasswordResetSuccessfulEventAsync(Guid accountId)
    {
        try
        {
            var eventModel = new AuthPasswordResetSuccessfulEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId
            };

            await _messageBus.TryPublishAsync(AUTH_PASSWORD_RESET_SUCCESSFUL_TOPIC, eventModel);
            _logger.LogDebug("Published AuthPasswordResetSuccessfulEvent for account {AccountId}", accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthPasswordResetSuccessfulEvent for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Publishes an MFA enabled audit event.
    /// </summary>
    private async Task PublishMfaEnabledEventAsync(Guid accountId)
    {
        try
        {
            var eventModel = new AuthMfaEnabledEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId
            };
            await _messageBus.TryPublishAsync(AUTH_MFA_ENABLED_TOPIC, eventModel);
            _logger.LogDebug("Published AuthMfaEnabledEvent for account {AccountId}", accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthMfaEnabledEvent for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Publishes an MFA disabled audit event.
    /// </summary>
    private async Task PublishMfaDisabledEventAsync(Guid accountId, MfaDisabledBy disabledBy, string? adminReason)
    {
        try
        {
            var eventModel = new AuthMfaDisabledEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                DisabledBy = disabledBy,
                AdminReason = adminReason
            };
            await _messageBus.TryPublishAsync(AUTH_MFA_DISABLED_TOPIC, eventModel);
            _logger.LogDebug("Published AuthMfaDisabledEvent for account {AccountId}, by {DisabledBy}", accountId, disabledBy);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthMfaDisabledEvent for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Publishes an MFA verification success audit event.
    /// </summary>
    private async Task PublishMfaVerifiedEventAsync(Guid accountId, MfaVerificationMethod method, Guid sessionId, int? recoveryCodesRemaining)
    {
        try
        {
            var eventModel = new AuthMfaVerifiedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                Method = method,
                SessionId = sessionId,
                RecoveryCodesRemaining = recoveryCodesRemaining
            };
            await _messageBus.TryPublishAsync(AUTH_MFA_VERIFIED_TOPIC, eventModel);
            _logger.LogDebug("Published AuthMfaVerifiedEvent for account {AccountId}, method {Method}", accountId, method);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthMfaVerifiedEvent for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Publishes an MFA verification failure audit event.
    /// </summary>
    private async Task PublishMfaFailedEventAsync(Guid accountId, MfaVerificationMethod method, MfaFailedReason reason)
    {
        try
        {
            var eventModel = new AuthMfaFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                Method = method,
                Reason = reason
            };
            await _messageBus.TryPublishAsync(AUTH_MFA_FAILED_TOPIC, eventModel);
            _logger.LogDebug("Published AuthMfaFailedEvent for account {AccountId}, reason {Reason}", accountId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish AuthMfaFailedEvent for account {AccountId}", accountId);
        }
    }

    #endregion
}
