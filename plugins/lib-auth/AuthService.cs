using BCrypt.Net;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.Subscription;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Auth service implementation focused on authentication, token management, and OAuth provider integration.
/// Follows schema-first architecture - implements generated IAuthService interface.
/// </summary>
[BannouService("auth", typeof(IAuthService), lifetime: ServiceLifetime.Scoped)]
public partial class AuthService : IAuthService
{
    private readonly IAccountClient _accountClient;
    private readonly ISubscriptionClient _subscriptionClient;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;

    // Helper services for better separation of concerns and testability
    private readonly ITokenService _tokenService;
    private readonly ISessionService _sessionService;
    private readonly IOAuthProviderService _oauthService;

    private const string REDIS_STATE_STORE = "auth-statestore";
    private const string SESSION_INVALIDATED_TOPIC = "session.invalidated";
    private const string SESSION_UPDATED_TOPIC = "session.updated";
    private const string DEFAULT_CONNECT_URL = "ws://localhost:5014/connect";

    // OAuth provider URLs
    private const string DISCORD_TOKEN_URL = "https://discord.com/api/oauth2/token";
    private const string DISCORD_USER_URL = "https://discord.com/api/users/@me";
    private const string GOOGLE_TOKEN_URL = "https://oauth2.googleapis.com/token";
    private const string GOOGLE_USER_URL = "https://www.googleapis.com/oauth2/v2/userinfo";
    private const string TWITCH_TOKEN_URL = "https://id.twitch.tv/oauth2/token";
    private const string TWITCH_USER_URL = "https://api.twitch.tv/helix/users";
    private const string STEAM_AUTH_URL = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/";

    public AuthService(
        IAccountClient accountClient,
        ISubscriptionClient subscriptionClient,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        AuthServiceConfiguration configuration,
        ILogger<AuthService> logger,
        IHttpClientFactory httpClientFactory,
        ITokenService tokenService,
        ISessionService sessionService,
        IOAuthProviderService oauthService,
        IEventConsumer eventConsumer)
    {
        _accountClient = accountClient;
        _subscriptionClient = subscriptionClient;
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _configuration = configuration;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _tokenService = tokenService;
        _sessionService = sessionService;
        _oauthService = oauthService;

        // Register event handlers via partial class (AuthServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);

        // JWT config comes from core AppConfiguration (BANNOU_JWT_*), validated at startup in Program.cs
        var jwtConfig = Program.Configuration;
        _logger.LogInformation("AuthService initialized with JwtSecret length: {Length}, Issuer: {Issuer}, Audience: {Audience}, MockProviders: {MockProviders}",
            jwtConfig.JwtSecret?.Length ?? 0, jwtConfig.JwtIssuer, jwtConfig.JwtAudience, _configuration.MockProviders);
    }

    /// <summary>
    /// Gets the effective WebSocket URL for client connections.
    /// Priority: AUTH_CONNECT_URL > ws://BANNOU_SERVICE_DOMAIN/connect > default localhost
    /// </summary>
    private Uri EffectiveConnectUrl
    {
        get
        {
            // If explicit ConnectUrl is configured, use it
            if (!string.IsNullOrWhiteSpace(_configuration.ConnectUrl) &&
                _configuration.ConnectUrl != DEFAULT_CONNECT_URL)
            {
                return new Uri(_configuration.ConnectUrl);
            }

            // If ServiceDomain is configured, derive WebSocket URL from it
            var serviceDomain = Program.Configuration?.ServiceDomain;
            if (!string.IsNullOrWhiteSpace(serviceDomain))
            {
                return new Uri($"wss://{serviceDomain}/connect");
            }

            // Default to localhost
            return new Uri(DEFAULT_CONNECT_URL);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthResponse?)> LoginAsync(
        LoginRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing login request for email: {Email}", body.Email);

            if (string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
            {
                return (StatusCodes.BadRequest, null);
            }

            // Lookup account by email via AccountClient
            _logger.LogInformation("Looking up account by email via AccountClient: {Email}", body.Email);

            AccountResponse account;
            try
            {
                account = await _accountClient.GetAccountByEmailAsync(new GetAccountByEmailRequest { Email = body.Email }, cancellationToken);
                _logger.LogInformation("Account found via service call: {AccountId}", account?.AccountId);

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
                await PublishLoginFailedEventAsync(body.Email, AuthLoginFailedReason.Account_not_found);
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
                await PublishLoginFailedEventAsync(body.Email, AuthLoginFailedReason.Invalid_credentials, account.AccountId);
                return (StatusCodes.Unauthorized, null);
            }

            _logger.LogInformation("Password verification successful for email: {Email}", body.Email);

            // Generate tokens (returns both accessToken and sessionId for event publishing)
            var (accessToken, sessionId) = await GenerateAccessTokenAsync(account, cancellationToken);

            var refreshToken = GenerateRefreshToken();

            // Store refresh token
            await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

            _logger.LogInformation("Successfully authenticated user: {Email} (ID: {AccountId})",
                body.Email, account.AccountId);

            // Publish audit event for successful login
            await PublishLoginSuccessfulEventAsync(account.AccountId, body.Email, sessionId);

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = account.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = EffectiveConnectUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email: {Email}", body.Email);
            await PublishErrorEventAsync("Login", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, RegisterResponse?)> RegisterAsync(
        RegisterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing registration request for username: {Username}", body.Username);

            if (string.IsNullOrWhiteSpace(body.Username) || string.IsNullOrWhiteSpace(body.Password))
            {
                return (StatusCodes.BadRequest, null);
            }

            // Hash password before storing
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(body.Password, workFactor: 12);
            _logger.LogInformation("Password hashed successfully for registration");

            // Create account via AccountClient service call
            _logger.LogInformation("Creating account via AccountClient for registration: {Email}", body.Email);

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
                _logger.LogInformation("Account created successfully via service call: {AccountId}", accountResult?.AccountId);

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
            var (accessToken, sessionId) = await GenerateAccessTokenAsync(accountResult, cancellationToken);
            var refreshToken = GenerateRefreshToken();

            // Store refresh token
            await StoreRefreshTokenAsync(accountResult.AccountId.ToString(), refreshToken, cancellationToken);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for username: {Username}", body.Username);
            await PublishErrorEventAsync("Register", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthResponse?)> CompleteOAuthAsync(
        Provider provider,
        OAuthCallbackRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
            OAuthUserInfo? userInfo = provider switch
            {
                Provider.Discord => await ExchangeDiscordCodeAsync(body.Code, cancellationToken),
                Provider.Google => await ExchangeGoogleCodeAsync(body.Code, cancellationToken),
                Provider.Twitch => await ExchangeTwitchCodeAsync(body.Code, cancellationToken),
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
            var account = await FindOrCreateOAuthAccountAsync(provider, userInfo, cancellationToken);
            if (account == null)
            {
                _logger.LogError("Failed to find or create account for OAuth user: {ProviderId}", userInfo.ProviderId);
                await PublishErrorEventAsync("CompleteOAuth", "account_creation_failed", "Failed to find or create account for OAuth user", dependency: "account", details: new { Provider = provider.ToString(), ProviderId = userInfo.ProviderId });
                return (StatusCodes.InternalServerError, null);
            }

            // Generate tokens (returns both accessToken and sessionId for event publishing)
            var (accessToken, sessionId) = await GenerateAccessTokenAsync(account, cancellationToken);
            var refreshToken = GenerateRefreshToken();
            await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

            _logger.LogInformation("OAuth authentication successful for account {AccountId} via {Provider}",
                account.AccountId, provider);

            // Publish audit event for successful OAuth login
            await PublishOAuthLoginSuccessfulEventAsync(account.AccountId, provider.ToString().ToLower(), userInfo.ProviderId, sessionId, isNewAccount: false);

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = account.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = EffectiveConnectUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth callback for provider: {Provider}", provider);
            await PublishErrorEventAsync("CompleteOAuth", ex.GetType().Name, ex.Message, details: new { Provider = provider.ToString() });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthResponse?)> VerifySteamAuthAsync(
        SteamVerifyRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
            var steamId = await ValidateSteamTicketAsync(body.Ticket, cancellationToken);
            if (string.IsNullOrEmpty(steamId))
            {
                _logger.LogWarning("Steam ticket validation failed");
                return (StatusCodes.Unauthorized, null);
            }

            _logger.LogInformation("Steam ticket validated successfully for SteamID: {SteamId}", steamId);

            // Find or create account linked to this Steam identity
            var userInfo = new OAuthUserInfo
            {
                ProviderId = steamId,
                DisplayName = $"Steam_{steamId.Substring(steamId.Length - 6)}", // Last 6 chars of Steam ID
                Email = null // Steam doesn't provide email
            };

            var account = await FindOrCreateOAuthAccountAsync(Provider.Discord, userInfo, cancellationToken, "steam");
            if (account == null)
            {
                _logger.LogError("Failed to find or create account for Steam user: {SteamId}", steamId);
                await PublishErrorEventAsync("VerifySteamAuth", "account_creation_failed", "Failed to find or create account for Steam user", dependency: "account", details: new { SteamId = steamId });
                return (StatusCodes.InternalServerError, null);
            }

            // Generate tokens (returns both accessToken and sessionId for event publishing)
            var (accessToken, sessionId) = await GenerateAccessTokenAsync(account, cancellationToken);
            var refreshToken = GenerateRefreshToken();
            await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

            _logger.LogInformation("Steam authentication successful for account {AccountId}", account.AccountId);

            // Publish audit event for successful Steam login
            await PublishSteamLoginSuccessfulEventAsync(account.AccountId, steamId, sessionId, isNewAccount: false);

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = account.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = EffectiveConnectUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Steam authentication verification");
            await PublishErrorEventAsync("VerifySteamAuth", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, AuthResponse?)> RefreshTokenAsync(
        string jwt,
        RefreshRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing token refresh request");

            if (string.IsNullOrWhiteSpace(body.RefreshToken))
            {
                return (StatusCodes.BadRequest, null);
            }

            // Validate refresh token
            var accountId = await ValidateRefreshTokenAsync(body.RefreshToken, cancellationToken);
            if (string.IsNullOrEmpty(accountId))
            {
                return (StatusCodes.Forbidden, null);
            }

            // Lookup account by ID via AccountClient
            _logger.LogInformation("Looking up account by ID via AccountClient: {AccountId}", accountId);

            AccountResponse account;
            try
            {
                account = await _accountClient.GetAccountAsync(new GetAccountRequest { AccountId = Guid.Parse(accountId) }, cancellationToken);
                _logger.LogInformation("Account found for refresh: {AccountId}", account?.AccountId);

                if (account == null)
                {
                    _logger.LogWarning("No account found for ID: {AccountId}", accountId);
                    return (StatusCodes.Unauthorized, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lookup account by ID via AccountClient");
                await PublishErrorEventAsync("RefreshToken", ex.GetType().Name, ex.Message, dependency: "account");
                return (StatusCodes.InternalServerError, null);
            }

            // Generate new tokens (sessionId not used for refresh - no audit event needed)
            var (accessToken, _) = await GenerateAccessTokenAsync(account, cancellationToken);
            var newRefreshToken = GenerateRefreshToken();

            // Store new refresh token and remove old one
            await StoreRefreshTokenAsync(accountId, newRefreshToken, cancellationToken);
            await RemoveRefreshTokenAsync(body.RefreshToken, cancellationToken);

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = account.AccountId,
                AccessToken = accessToken,
                RefreshToken = newRefreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = EffectiveConnectUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            await PublishErrorEventAsync("RefreshToken", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }


    /// <inheritdoc/>
    public async Task<(StatusCodes, InitOAuthResponse?)> InitOAuthAsync(
        Provider provider,
        string redirectUri,
        string? state,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement for sync method
        try
        {
            _logger.LogInformation("Initializing OAuth for provider: {Provider}", provider);

            string? authUrl = null;
            var encodedState = HttpUtility.UrlEncode(state ?? Guid.NewGuid().ToString());

            switch (provider)
            {
                case Provider.Discord:
                    if (string.IsNullOrWhiteSpace(_configuration.DiscordClientId))
                    {
                        _logger.LogError("Discord Client ID not configured");
                        await PublishErrorEventAsync("InitOAuth", "configuration_error", "Discord Client ID not configured");
                        return (StatusCodes.InternalServerError, null);
                    }
                    var discordRedirectUri = HttpUtility.UrlEncode(redirectUri ?? _configuration.DiscordRedirectUri);
                    authUrl = $"https://discord.com/oauth2/authorize?client_id={_configuration.DiscordClientId}&response_type=code&redirect_uri={discordRedirectUri}&scope=identify%20email&state={encodedState}";
                    break;

                case Provider.Google:
                    if (string.IsNullOrWhiteSpace(_configuration.GoogleClientId))
                    {
                        _logger.LogError("Google Client ID not configured");
                        await PublishErrorEventAsync("InitOAuth", "configuration_error", "Google Client ID not configured");
                        return (StatusCodes.InternalServerError, null);
                    }
                    var googleRedirectUri = HttpUtility.UrlEncode(redirectUri ?? _configuration.GoogleRedirectUri);
                    authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={_configuration.GoogleClientId}&response_type=code&redirect_uri={googleRedirectUri}&scope=openid%20email%20profile&state={encodedState}";
                    break;

                case Provider.Twitch:
                    if (string.IsNullOrWhiteSpace(_configuration.TwitchClientId))
                    {
                        _logger.LogError("Twitch Client ID not configured");
                        await PublishErrorEventAsync("InitOAuth", "configuration_error", "Twitch Client ID not configured");
                        return (StatusCodes.InternalServerError, null);
                    }
                    var twitchRedirectUri = HttpUtility.UrlEncode(redirectUri ?? _configuration.TwitchRedirectUri);
                    authUrl = $"https://id.twitch.tv/oauth2/authorize?client_id={_configuration.TwitchClientId}&response_type=code&redirect_uri={twitchRedirectUri}&scope=user:read:email&state={encodedState}";
                    break;

                default:
                    _logger.LogWarning("Unknown OAuth provider: {Provider}", provider);
                    return (StatusCodes.BadRequest, null);
            }

            _logger.LogDebug("Generated OAuth URL for {Provider}: {Url}", provider, authUrl);
            return (StatusCodes.OK, new InitOAuthResponse { AuthorizationUrl = new Uri(authUrl) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing OAuth for provider: {Provider}", provider);
            await PublishErrorEventAsync("InitOAuth", ex.GetType().Name, ex.Message, details: new { Provider = provider.ToString() });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> LogoutAsync(
        string jwt,
        LogoutRequest? body,
        CancellationToken cancellationToken = default)
    {
        try
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

            // Extract session_key from JWT claims to identify which session to logout
            var sessionKey = await ExtractSessionKeyFromJWT(jwt);
            if (sessionKey == null)
            {
                _logger.LogWarning("Could not extract session_key from JWT for logout");
                return StatusCodes.Unauthorized;
            }

            var invalidatedSessions = new List<string>();

            if (body?.AllSessions == true)
            {
                _logger.LogInformation("AllSessions logout requested for account: {AccountId}", validateResponse.AccountId);

                // Get all sessions for the account using our new efficient method
                var accountSessions = await GetAccountSessionsAsync(validateResponse.AccountId.ToString(), cancellationToken);

                if (accountSessions.Count > 0)
                {
                    // Get session keys from index to delete all sessions
                    var indexKey = $"account-sessions:{validateResponse.AccountId}";
                    var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(REDIS_STATE_STORE);
                    var sessionKeys = await sessionIndexStore.GetAsync(indexKey, cancellationToken);

                    if (sessionKeys != null && sessionKeys.Count > 0)
                    {
                        // Delete all sessions
                        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);
                        var deleteTasks = sessionKeys.Select(key =>
                            sessionStore.DeleteAsync($"session:{key}", cancellationToken));
                        await Task.WhenAll(deleteTasks);

                        // Remove the account sessions index
                        await sessionIndexStore.DeleteAsync(indexKey, cancellationToken);

                        invalidatedSessions.AddRange(sessionKeys);

                        _logger.LogInformation("All {SessionCount} sessions logged out for account: {AccountId}",
                            sessionKeys.Count, validateResponse.AccountId);
                    }
                }
                else
                {
                    _logger.LogInformation("No active sessions found for account: {AccountId}", validateResponse.AccountId);
                }
            }
            else
            {
                // Logout current session only
                var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);
                await sessionStore.DeleteAsync($"session:{sessionKey}", cancellationToken);

                // Remove session from account index
                await RemoveSessionFromAccountIndexAsync(validateResponse.AccountId.ToString(), sessionKey, cancellationToken);

                invalidatedSessions.Add(sessionKey);

                _logger.LogInformation("Session logged out successfully for account: {AccountId}", validateResponse.AccountId);
            }

            // Publish session invalidation event for Connect service to disconnect clients
            if (invalidatedSessions.Count > 0)
            {
                await PublishSessionInvalidatedEventAsync(
                    validateResponse.AccountId,
                    invalidatedSessions,
                    SessionInvalidatedEventReason.Logout);
            }

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            await PublishErrorEventAsync("Logout", ex.GetType().Name, ex.Message);
            return StatusCodes.InternalServerError;
        }
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> TerminateSessionAsync(
        string jwt,
        TerminateSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionId = body.SessionId;
            _logger.LogInformation("Terminating session: {SessionId}", sessionId);

            // Find and remove the session from Redis
            // Since we store sessions with session_key, we need to find sessions by session_id
            var sessionKey = await FindSessionKeyBySessionIdAsync(sessionId.ToString(), cancellationToken);

            if (sessionKey == null)
            {
                _logger.LogWarning("Session {SessionId} not found for termination", sessionId);
                return StatusCodes.NotFound;
            }

            // Get session data to find account ID for index cleanup
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);
            var sessionData = await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);

            // Remove the session data from Redis
            await sessionStore.DeleteAsync($"session:{sessionKey}", cancellationToken);

            // Remove session from account index if we found the session data
            if (sessionData != null)
            {
                await RemoveSessionFromAccountIndexAsync(sessionData.AccountId.ToString(), sessionKey, cancellationToken);
            }

            // Remove reverse index entry
            await RemoveSessionIdReverseIndexAsync(sessionId.ToString(), cancellationToken);

            // Publish SessionInvalidatedEvent to disconnect WebSocket clients (like LogoutAsync does)
            if (sessionData != null)
            {
                await PublishSessionInvalidatedEventAsync(
                    sessionData.AccountId,
                    new List<string> { sessionKey },
                    SessionInvalidatedEventReason.Admin_action);
            }

            _logger.LogInformation("Session {SessionId} terminated successfully", sessionId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating session: {SessionId}", body.SessionId);
            await PublishErrorEventAsync("TerminateSession", ex.GetType().Name, ex.Message);
            return StatusCodes.InternalServerError;
        }
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> RequestPasswordResetAsync(
        PasswordResetRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                // Generate secure reset token
                var resetToken = GenerateSecureToken();
                var resetTokenTtlMinutes = _configuration.PasswordResetTokenTtlMinutes > 0
                    ? _configuration.PasswordResetTokenTtlMinutes
                    : 60; // Default 1 hour

                var resetData = new PasswordResetData
                {
                    AccountId = account.AccountId,
                    Email = account.Email,
                    ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(resetTokenTtlMinutes)
                };

                // Store reset token in Redis with TTL
                var resetStore = _stateStoreFactory.GetStore<PasswordResetData>(REDIS_STATE_STORE);
                await resetStore.SaveAsync(
                    $"password-reset:{resetToken}",
                    resetData,
                    new StateOptions { Ttl = resetTokenTtlMinutes * 60 },
                    cancellationToken);

                // Send password reset email (mock implementation logs to console)
                await SendPasswordResetEmailAsync(account.Email, resetToken, resetTokenTtlMinutes, cancellationToken);

                _logger.LogInformation("Password reset token generated for account {AccountId}, expires in {Minutes} minutes",
                    account.AccountId, resetTokenTtlMinutes);
            }

            // Always return success to prevent email enumeration attacks
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting password reset for email: {Email}", body.Email);
            await PublishErrorEventAsync("RequestPasswordReset", ex.GetType().Name, ex.Message);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Generate a cryptographically secure token for password reset.
    /// </summary>
    private static string GenerateSecureToken()
    {
        var tokenBytes = new byte[32]; // 256 bits
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    /// <summary>
    /// Send password reset email. Currently implements a mock that logs to console.
    /// Can be replaced with actual SMTP integration later.
    /// </summary>
    private async Task SendPasswordResetEmailAsync(string email, string resetToken, int expiresInMinutes, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Satisfy async requirement for sync method
        // Mock email implementation - logs to console
        // In production, this would integrate with SendGrid, AWS SES, or similar
        if (string.IsNullOrWhiteSpace(_configuration.PasswordResetBaseUrl))
        {
            throw new InvalidOperationException("PasswordResetBaseUrl configuration is not set. Cannot generate password reset link.");
        }
        var resetUrl = $"{_configuration.PasswordResetBaseUrl}?token={resetToken}";

        _logger.LogInformation(
            "=== PASSWORD RESET EMAIL (MOCK) ===\n" +
            "To: {Email}\n" +
            "Subject: Password Reset Request\n" +
            "Body:\n" +
            "You requested a password reset for your account.\n" +
            "Click the link below to reset your password:\n" +
            "{ResetUrl}\n" +
            "This link will expire in {ExpiresInMinutes} minutes.\n" +
            "If you did not request this reset, please ignore this email.\n" +
            "=== END EMAIL ===",
            email, resetUrl, expiresInMinutes);
    }

    /// <inheritdoc/>
    public async Task<StatusCodes> ConfirmPasswordResetAsync(
        PasswordResetConfirmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing password reset confirmation");

            if (string.IsNullOrWhiteSpace(body.Token) || string.IsNullOrWhiteSpace(body.NewPassword))
            {
                _logger.LogWarning("Invalid password reset confirmation request - missing token or password");
                return StatusCodes.BadRequest;
            }

            // Look up the reset token in Redis
            var resetStore = _stateStoreFactory.GetStore<PasswordResetData>(REDIS_STATE_STORE);
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
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(body.NewPassword, workFactor: 12);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming password reset");
            await PublishErrorEventAsync("ConfirmPasswordReset", ex.GetType().Name, ex.Message);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Data stored for password reset tokens.
    /// </summary>
    internal class PasswordResetData
    {
        public Guid AccountId { get; set; }
        public string Email { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, SessionsResponse?)> GetSessionsAsync(
        string jwt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sessions requested");

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

            _logger.LogInformation("Getting sessions for account: {AccountId}", validateResponse.AccountId);

            // Use efficient account-to-sessions index with bulk state operations
            var sessions = await GetAccountSessionsAsync(validateResponse.AccountId.ToString(), cancellationToken);

            _logger.LogInformation("Returning {SessionCount} session(s) for account: {AccountId}",
                sessions.Count, validateResponse.AccountId);

            return (StatusCodes.OK, new SessionsResponse
            {
                Sessions = sessions
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions");
            await PublishErrorEventAsync("GetSessions", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(
        string jwt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Validating JWT token");

            if (string.IsNullOrWhiteSpace(jwt))
            {
                _logger.LogWarning("JWT token is null or empty");
                return (StatusCodes.Unauthorized, null);
            }

            // Validate JWT signature and extract claims
            // Use core app configuration for JWT settings (validated at startup in Program.cs)
            var jwtConfig = Program.Configuration;
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(jwtConfig.JwtSecret!);

            try
            {
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = jwtConfig.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtConfig.JwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                var principal = tokenHandler.ValidateToken(jwt, tokenValidationParameters, out var validatedToken);

                // Extract session_key from JWT claims
                var sessionKeyClaimType = "session_key";
                var sessionKeyClaim = principal.FindFirst(sessionKeyClaimType);
                if (sessionKeyClaim == null)
                {
                    _logger.LogWarning("JWT token does not contain session_key claim");
                    return (StatusCodes.Unauthorized, null);
                }

                var sessionKey = sessionKeyClaim.Value;

                _logger.LogDebug("Validating session from JWT, SessionKey: {SessionKey}", sessionKey);

                // Debug logging for session lookup
                _logger.LogDebug("Looking up session state for SessionKey: {SessionKey}", sessionKey);

                // Lookup session data from Redis
                var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);
                var sessionData = await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);

                if (sessionData == null)
                {
                    _logger.LogWarning("Session not found, SessionKey: {SessionKey}", sessionKey);
                    return (StatusCodes.Unauthorized, null);
                }

                _logger.LogDebug("Session loaded for AccountId {AccountId}, SessionKey: {SessionKey}, ExpiresAt: {ExpiresAt}",
                    sessionData.AccountId, sessionKey, sessionData.ExpiresAt);

                // Check if session has expired
                if (sessionData.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("Session expired, SessionKey: {SessionKey}, ExpiredAt: {ExpiresAt}", sessionKey, sessionData.ExpiresAt);
                    return (StatusCodes.Unauthorized, null);
                }

                // Validate session data integrity - null roles or authorizations indicates data corruption
                if (sessionData.Roles == null || sessionData.Authorizations == null)
                {
                    _logger.LogError(
                        "Session data corrupted - null Roles or Authorizations. SessionKey: {SessionKey}, AccountId: {AccountId}, RolesNull: {RolesNull}, AuthNull: {AuthNull}",
                        sessionKey, sessionData.AccountId, sessionData.Roles == null, sessionData.Authorizations == null);
                    await PublishErrorEventAsync("ValidateToken", "session_data_corrupted", "Session has null Roles or Authorizations - data integrity failure");
                    return (StatusCodes.Unauthorized, null);
                }

                _logger.LogDebug("Token validated successfully, AccountId: {AccountId}, SessionKey: {SessionKey}", sessionData.AccountId, sessionKey);

                // Return session information
                // IMPORTANT: Return sessionKey (not sessionData.SessionId) so Connect service
                // tracks connections by the same key used in account-sessions index and
                // published in SessionInvalidatedEvent for proper WebSocket disconnection
                return (StatusCodes.OK, new ValidateTokenResponse
                {
                    Valid = true,
                    AccountId = sessionData.AccountId,
                    SessionId = Guid.Parse(sessionKey),
                    Roles = sessionData.Roles,
                    Authorizations = sessionData.Authorizations,
                    RemainingTime = (int)(sessionData.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds
                });
            }
            catch (SecurityTokenValidationException ex)
            {
                _logger.LogWarning(ex, "JWT token validation failed");
                return (StatusCodes.Unauthorized, null);
            }
            catch (SecurityTokenMalformedException ex)
            {
                _logger.LogWarning(ex, "JWT token is malformed");
                return (StatusCodes.Unauthorized, null);
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogWarning(ex, "JWT security token error");
                return (StatusCodes.Unauthorized, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during JWT validation");
                await PublishErrorEventAsync("ValidateToken", ex.GetType().Name, ex.Message);
                return (StatusCodes.InternalServerError, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
            await PublishErrorEventAsync("ValidateToken", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Internal model for session data stored in Redis
    /// </summary>
    private class SessionDataModel
    {
        public Guid AccountId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
        public List<string> Authorizations { get; set; } = new List<string>();
        public string SessionId { get; set; } = string.Empty;

        // Store as Unix epoch timestamps (long) to avoid System.Text.Json DateTimeOffset serialization issues
        public long CreatedAtUnix { get; set; }
        public long ExpiresAtUnix { get; set; }

        // Expose as DateTimeOffset for code convenience (not serialized)
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset CreatedAt
        {
            get => DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix);
            set => CreatedAtUnix = value.ToUnixTimeSeconds();
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset ExpiresAt
        {
            get => DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix);
            set => ExpiresAtUnix = value.ToUnixTimeSeconds();
        }
    }

    private string HashPassword(string password)
    {
        // Simplified password hashing - use BCrypt in production
        // Use core app configuration for JWT secret (validated at startup in Program.cs)
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(password + Program.Configuration.JwtSecret));
    }

    private bool VerifyPassword(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        return HashPassword(password) == hash;
    }

    /// <summary>
    /// Generates an access token and creates a session in Redis.
    /// Returns both the JWT and the sessionId for event publishing.
    /// </summary>
    private async Task<(string accessToken, string sessionId)> GenerateAccessTokenAsync(AccountResponse account, CancellationToken cancellationToken = default)
    {
        // Validate inputs
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        if (_configuration == null)
            throw new InvalidOperationException("AuthServiceConfiguration is null");

        // Use core app configuration for JWT settings (validated at startup in Program.cs)
        var jwtConfig = Program.Configuration;

        if (string.IsNullOrWhiteSpace(jwtConfig.JwtSecret))
            throw new InvalidOperationException("JWT secret is not configured");

        if (string.IsNullOrWhiteSpace(jwtConfig.JwtIssuer))
            throw new InvalidOperationException("JWT issuer is not configured");

        if (string.IsNullOrWhiteSpace(jwtConfig.JwtAudience))
            throw new InvalidOperationException("JWT audience is not configured");

        _logger.LogDebug("Generating access token for account {AccountId} with JWT config: Secret={SecretLength}, Issuer={Issuer}, Audience={Audience}",
            account.AccountId, jwtConfig.JwtSecret?.Length, jwtConfig.JwtIssuer, jwtConfig.JwtAudience);

        // Generate opaque session key for JWT Redis key security
        var sessionKey = Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid().ToString();

        // Fetch current subscriptions/authorizations for the account
        var authorizations = new List<string>();
        try
        {
            var subscriptionsResponse = await _subscriptionClient.QueryCurrentSubscriptionsAsync(
                new QueryCurrentSubscriptionsRequest { AccountId = account.AccountId },
                cancellationToken);

            if (subscriptionsResponse?.Subscriptions != null)
            {
                authorizations = subscriptionsResponse.Subscriptions.Select(s => s.StubName).ToList();
                _logger.LogDebug("Fetched {Count} authorizations for account {AccountId}: {Authorizations}",
                    authorizations.Count, account.AccountId, string.Join(", ", authorizations));
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // No subscriptions for this account - this is valid (new account)
            _logger.LogDebug("No subscriptions found for account {AccountId}", account.AccountId);
        }
        catch (Exception ex)
        {
            // Subscription service unavailable - logins must fail
            _logger.LogError(ex, "Failed to fetch subscriptions for account {AccountId} - login rejected", account.AccountId);
            throw;
        }

        // Validate account data integrity before creating session
        if (account.Roles == null || account.Roles.Count == 0)
        {
            _logger.LogWarning(
                "Account {AccountId} has null or empty Roles - possible data integrity issue. Session will have limited permissions.",
                account.AccountId);
        }

        // Store session data in Redis with opaque key
        var sessionData = new SessionDataModel
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            Roles = account.Roles?.ToList() ?? new List<string>(),
            Authorizations = authorizations,
            SessionId = sessionId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_configuration.JwtExpirationMinutes)
        };

        _logger.LogDebug("Saving session for AccountId {AccountId}, SessionKey: {SessionKey}, ExpiresAt: {ExpiresAt}",
            account.AccountId, sessionKey, sessionData.ExpiresAt);

        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);
        await sessionStore.SaveAsync(
            $"session:{sessionKey}",
            sessionData,
            new StateOptions { Ttl = _configuration.JwtExpirationMinutes * 60 },
            cancellationToken);

        // Verify round-trip for debugging serialization issues
        var verifyData = await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);
        _logger.LogDebug("Session round-trip verification for SessionKey {SessionKey}: Success={Success}, ExpiresAtUnix={ExpiresAtUnix}",
            sessionKey, verifyData != null, verifyData?.ExpiresAtUnix ?? -1);

        // Maintain account-to-sessions index for efficient GetSessions implementation
        await AddSessionToAccountIndexAsync(account.AccountId.ToString(), sessionKey, cancellationToken);

        // Maintain reverse index (session_id -> session_key) for TerminateSession functionality
        await AddSessionIdReverseIndexAsync(sessionId, sessionKey, _configuration.JwtExpirationMinutes * 60, cancellationToken);

        // JWT contains only opaque session key - no sensitive data
        // jwtConfig already retrieved above; validated at startup in Program.cs
        var key = Encoding.UTF8.GetBytes(jwtConfig.JwtSecret!);


        try
        {
            // Use JwtSecurityTokenHandler - the standard JWT implementation
            var tokenHandler = new JwtSecurityTokenHandler();

            var claims = new List<Claim>
            {
                new Claim("session_key", sessionKey), // Opaque key for Redis lookup
                new Claim(ClaimTypes.NameIdentifier, account.AccountId.ToString()), // Standard subject claim
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, account.AccountId.ToString()), // Standard subject claim
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // JWT ID for tracking
                new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat,
                    new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64) // Issued at time
            };

            // IMPORTANT: Roles are NOT stored in JWT claims!
            //
            // WHY: Roles are server-managed authorization decisions, not client-negotiable identity attributes.
            // Roles are assigned by administrators (via dashboard) or auto-assigned based on server-side
            // configuration (e.g., AdminEmailDomain). Clients have no choice in their authorization level.
            //
            // HOW ROLES WORK:
            // 1. Roles stored in AccountModel (MySQL via lib-state) - assigned by Account service
            // 2. Roles copied to SessionDataModel (Redis) during session creation - stored with session data
            // 3. JWT contains only opaque "session_key" claim - points to Redis session data
            // 4. ValidateTokenAsync reads roles from Redis session data (NOT from JWT claims)
            // 5. Connect service receives roles from ValidateTokenAsync response
            // 6. Permission service compiles capabilities based on role from session
            //
            // This architecture:
            // - Prevents clients from seeing/manipulating authorization levels in JWT
            // - Allows server to revoke/change roles without reissuing JWTs
            // - Maintains clean separation: JWT = authentication, Redis session = authorization
            // - Follows principle: roles are server-side policy, not client identity
            //
            // DO NOT add role claims to JWT! Use ValidateTokenAsync to retrieve roles from Redis.

            var symmetricKey = new SymmetricSecurityKey(key);
            var signingCredentials = new SigningCredentials(symmetricKey, SecurityAlgorithms.HmacSha256Signature);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddMinutes(_configuration.JwtExpirationMinutes),
                NotBefore = DateTime.UtcNow,
                IssuedAt = DateTime.UtcNow,
                SigningCredentials = signingCredentials,
                Issuer = jwtConfig.JwtIssuer,
                Audience = jwtConfig.JwtAudience
            };

            var jwt = tokenHandler.CreateToken(tokenDescriptor);
            var jwtString = tokenHandler.WriteToken(jwt);
            return (jwtString, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate security token for session with ID {SessionKey}", sessionKey);
            throw;
        }
    }

    private string GenerateRefreshToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    private async Task StoreRefreshTokenAsync(string accountId, string refreshToken, CancellationToken cancellationToken)
    {
        var redisKey = $"refresh_token:{refreshToken}";
        var refreshStore = _stateStoreFactory.GetStore<StringWrapper>(REDIS_STATE_STORE);
        await refreshStore.SaveAsync(
            redisKey,
            new StringWrapper { Value = accountId },
            new StateOptions { Ttl = (int)TimeSpan.FromDays(7).TotalSeconds }, // 7 days
            cancellationToken);
    }

    private async Task<string?> ValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            var refreshStore = _stateStoreFactory.GetStore<StringWrapper>(REDIS_STATE_STORE);
            var wrapper = await refreshStore.GetAsync(redisKey, cancellationToken);
            return wrapper?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate refresh token from Redis state store");
            return null;
        }
    }

    private async Task RemoveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            var refreshStore = _stateStoreFactory.GetStore<StringWrapper>(REDIS_STATE_STORE);
            await refreshStore.DeleteAsync(redisKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove refresh token from state store");
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "RemoveRefreshToken",
                "state_cleanup_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: null,
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Find session key by session ID using the reverse index.
    /// </summary>
    private async Task<string?> FindSessionKeyBySessionIdAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            // Use the reverse index to find the session key
            var indexStore = _stateStoreFactory.GetStore<StringWrapper>(REDIS_STATE_STORE);
            var wrapper = await indexStore.GetAsync($"session-id-index:{sessionId}", cancellationToken);

            if (wrapper != null && !string.IsNullOrEmpty(wrapper.Value))
            {
                _logger.LogDebug("Found session key {SessionKey} for session ID {SessionId}", wrapper.Value, sessionId);
                return wrapper.Value;
            }

            _logger.LogWarning("No session key found in reverse index for session ID: {SessionId}", sessionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding session key for session ID: {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// Add reverse index entry mapping session_id to session_key.
    /// </summary>
    private async Task AddSessionIdReverseIndexAsync(string sessionId, string sessionKey, int ttlSeconds, CancellationToken cancellationToken)
    {
        try
        {
            var indexStore = _stateStoreFactory.GetStore<StringWrapper>(REDIS_STATE_STORE);
            await indexStore.SaveAsync(
                $"session-id-index:{sessionId}",
                new StringWrapper { Value = sessionKey },
                new StateOptions { Ttl = ttlSeconds },
                cancellationToken);

            _logger.LogDebug("Added reverse index for session ID {SessionId} -> session key {SessionKey}", sessionId, sessionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add reverse index for session ID {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "AddSessionIdReverseIndex",
                "state_index_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: $"sessionId={sessionId}",
                stack: ex.StackTrace);
            // Don't throw - session creation should succeed even if index update fails
        }
    }

    /// <summary>
    /// Remove reverse index entry for a session ID.
    /// </summary>
    private async Task RemoveSessionIdReverseIndexAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var indexStore = _stateStoreFactory.GetStore<StringWrapper>(REDIS_STATE_STORE);
            await indexStore.DeleteAsync($"session-id-index:{sessionId}", cancellationToken);

            _logger.LogDebug("Removed reverse index for session ID {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove reverse index for session ID {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "RemoveSessionIdReverseIndex",
                "state_cleanup_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: $"sessionId={sessionId}",
                stack: ex.StackTrace);
            // Don't throw - operation should continue even if cleanup fails
        }
    }

    /// <summary>
    /// Extract session_key from JWT token without full validation (for logout operations)
    /// </summary>
    private async Task<string?> ExtractSessionKeyFromJWT(string jwt)
    {
        await Task.CompletedTask; // Satisfy async requirement for sync method
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jsonToken = tokenHandler.ReadJwtToken(jwt);

            var sessionKeyClaim = jsonToken?.Claims?.FirstOrDefault(c => c.Type == "session_key");
            return sessionKeyClaim?.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting session_key from JWT");
            return null;
        }
    }

    /// <summary>
    /// Add session key to account's session index for efficient GetSessions
    /// </summary>
    private async Task AddSessionToAccountIndexAsync(string accountId, string sessionKey, CancellationToken cancellationToken)
    {
        try
        {
            var indexKey = $"account-sessions:{accountId}";
            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(REDIS_STATE_STORE);

            // Get existing session list
            var existingSessions = await sessionIndexStore.GetAsync(indexKey, cancellationToken) ?? new List<string>();

            // Add new session if not already present
            if (!existingSessions.Contains(sessionKey))
            {
                existingSessions.Add(sessionKey);

                // Save updated list with TTL slightly longer than session TTL to handle clock skew
                var accountIndexTtlSeconds = (_configuration.JwtExpirationMinutes * 60) + 300; // +5 minutes buffer
                await sessionIndexStore.SaveAsync(
                    indexKey,
                    existingSessions,
                    new StateOptions { Ttl = accountIndexTtlSeconds },
                    cancellationToken);

                _logger.LogDebug("Added session to account index, AccountId: {AccountId}, SessionKey: {SessionKey}, TotalSessions: {Count}",
                    accountId, sessionKey, existingSessions.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add session to account index, AccountId: {AccountId}, SessionKey: {SessionKey}", accountId, sessionKey);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "AddSessionToAccountIndex",
                "state_index_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: $"accountId={accountId},sessionKey={sessionKey}",
                stack: ex.StackTrace);
            // Don't throw - session creation should succeed even if index update fails
        }
    }

    /// <summary>
    /// Remove session key from account's session index
    /// </summary>
    private async Task RemoveSessionFromAccountIndexAsync(string accountId, string sessionKey, CancellationToken cancellationToken)
    {
        try
        {
            var indexKey = $"account-sessions:{accountId}";
            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(REDIS_STATE_STORE);

            // Get existing session list
            var existingSessions = await sessionIndexStore.GetAsync(indexKey, cancellationToken);

            if (existingSessions != null && existingSessions.Contains(sessionKey))
            {
                existingSessions.Remove(sessionKey);

                if (existingSessions.Count > 0)
                {
                    // Save updated list
                    var accountIndexTtlSeconds = (_configuration.JwtExpirationMinutes * 60) + 300; // +5 minutes buffer
                    await sessionIndexStore.SaveAsync(
                        indexKey,
                        existingSessions,
                        new StateOptions { Ttl = accountIndexTtlSeconds },
                        cancellationToken);
                }
                else
                {
                    // Remove empty index
                    await sessionIndexStore.DeleteAsync(indexKey, cancellationToken);
                }

                _logger.LogDebug("Removed session {SessionKey} from account index for account {AccountId}", sessionKey, accountId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove session {SessionKey} from account index for account {AccountId}", sessionKey, accountId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "RemoveSessionFromAccountIndex",
                "state_index_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: $"accountId={accountId},sessionKey={sessionKey}",
                stack: ex.StackTrace);
            // Don't throw - logout should succeed even if index update fails
        }
    }

    /// <summary>
    /// Get all active sessions for an account using efficient bulk operations
    /// </summary>
    private async Task<List<SessionInfo>> GetAccountSessionsAsync(string accountId, CancellationToken cancellationToken)
    {
        try
        {
            var indexKey = $"account-sessions:{accountId}";
            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(REDIS_STATE_STORE);
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);

            // Get session keys from account index
            var sessionKeys = await sessionIndexStore.GetAsync(indexKey, cancellationToken);

            if (sessionKeys == null || sessionKeys.Count == 0)
            {
                _logger.LogDebug("No sessions found in index for account {AccountId}", accountId);
                return new List<SessionInfo>();
            }

            // Use efficient parallel operations to get all session data
            var sessionDataTasks = sessionKeys.Select(async key =>
            {
                try
                {
                    var sessionData = await sessionStore.GetAsync($"session:{key}", cancellationToken);

                    return new { SessionKey = key, SessionData = (SessionDataModel?)sessionData };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to retrieve session data for key: {SessionKey}", key);
                    return new { SessionKey = key, SessionData = (SessionDataModel?)null };
                }
            });

            var sessionResults = await Task.WhenAll(sessionDataTasks);

            var sessions = new List<SessionInfo>();
            var expiredSessionKeys = new List<string>();

            foreach (var result in sessionResults)
            {
                if (result.SessionData != null)
                {
                    // Check if session is still valid
                    if (result.SessionData.ExpiresAt > DateTimeOffset.UtcNow)
                    {
                        sessions.Add(new SessionInfo
                        {
                            SessionId = Guid.Parse(result.SessionData.SessionId),
                            CreatedAt = result.SessionData.CreatedAt,
                            LastActive = result.SessionData.CreatedAt, // We don't track last active time yet
                            DeviceInfo = new DeviceInfo
                            {
                                DeviceType = DeviceInfoDeviceType.Desktop, // Default for now
                                Platform = "Unknown", // We don't store device info yet
                                Browser = "Unknown"
                            }
                        });
                    }
                    else
                    {
                        // Session expired, add to cleanup list
                        expiredSessionKeys.Add(result.SessionKey);
                        _logger.LogDebug("Found expired session {SessionKey} for account {AccountId}", result.SessionKey, accountId);
                    }
                }
                else
                {
                    // Session data not found (may have been deleted), remove from index
                    expiredSessionKeys.Add(result.SessionKey);
                }
            }

            // Clean up expired sessions from index
            if (expiredSessionKeys.Count > 0)
            {
                // Await the cleanup to avoid potential issues with background tasks
                foreach (var expiredKey in expiredSessionKeys)
                {
                    await RemoveSessionFromAccountIndexAsync(accountId, expiredKey, cancellationToken);
                }
            }

            _logger.LogDebug("Retrieved {ActiveSessionCount} active sessions for account {AccountId} (cleaned up {ExpiredCount} expired)",
                sessions.Count, accountId, expiredSessionKeys.Count);

            return sessions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sessions for account {AccountId}", accountId);
            throw; // Don't mask state store failures - empty list should mean "no sessions", not "error"
        }
    }

    #endregion

    #region OAuth Helper Methods

    /// <summary>
    /// User info retrieved from OAuth provider
    /// </summary>
    private class OAuthUserInfo
    {
        public string ProviderId { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
    }

    /// <summary>
    /// Response from Discord token exchange
    /// </summary>
    private class DiscordTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    /// <summary>
    /// Response from Discord user info endpoint
    /// </summary>
    private class DiscordUserResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("avatar")]
        public string? Avatar { get; set; }

        [JsonPropertyName("global_name")]
        public string? GlobalName { get; set; }
    }

    /// <summary>
    /// Response from Google token exchange
    /// </summary>
    private class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }
    }

    /// <summary>
    /// Response from Google user info endpoint
    /// </summary>
    private class GoogleUserResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("picture")]
        public string? Picture { get; set; }
    }

    /// <summary>
    /// Response from Twitch token exchange
    /// </summary>
    private class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }
    }

    /// <summary>
    /// Response from Twitch users endpoint
    /// </summary>
    private class TwitchUsersResponse
    {
        [JsonPropertyName("data")]
        public List<TwitchUser>? Data { get; set; }
    }

    private class TwitchUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("login")]
        public string Login { get; set; } = string.Empty;

        [JsonPropertyName("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("profile_image_url")]
        public string? ProfileImageUrl { get; set; }
    }

    /// <summary>
    /// Response from Steam ticket validation
    /// </summary>
    private class SteamAuthResponse
    {
        [JsonPropertyName("response")]
        public SteamAuthResponseData? Response { get; set; }
    }

    private class SteamAuthResponseData
    {
        [JsonPropertyName("params")]
        public SteamAuthParams? Params { get; set; }

        [JsonPropertyName("error")]
        public SteamAuthError? Error { get; set; }
    }

    private class SteamAuthParams
    {
        [JsonPropertyName("result")]
        public string Result { get; set; } = string.Empty;

        [JsonPropertyName("steamid")]
        public string SteamId { get; set; } = string.Empty;

        [JsonPropertyName("ownersteamid")]
        public string OwnerSteamId { get; set; } = string.Empty;

        [JsonPropertyName("vacbanned")]
        public bool VacBanned { get; set; }

        [JsonPropertyName("publisherbanned")]
        public bool PublisherBanned { get; set; }
    }

    private class SteamAuthError
    {
        [JsonPropertyName("errorcode")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("errordesc")]
        public string ErrorDesc { get; set; } = string.Empty;
    }

    /// <summary>
    /// Exchange Discord authorization code for user info
    /// </summary>
    private async Task<OAuthUserInfo?> ExchangeDiscordCodeAsync(string code, CancellationToken cancellationToken)
    {
        // IMPLEMENTATION TENETS: Validate OAuth provider is configured before use
        var discordClientId = _configuration.DiscordClientId;
        var discordClientSecret = _configuration.DiscordClientSecret;
        var discordRedirectUri = _configuration.DiscordRedirectUri;

        if (discordClientId == null || discordClientSecret == null || discordRedirectUri == null)
        {
            _logger.LogError("Discord OAuth not configured - set AUTH_DISCORD_CLIENT_ID, AUTH_DISCORD_CLIENT_SECRET, and AUTH_DISCORD_REDIRECT_URI");
            return null;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            // Exchange code for access token
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", discordClientId },
                { "client_secret", discordClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", discordRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(DISCORD_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Discord token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = BannouJson.Deserialize<DiscordTokenResponse>(tokenJson);
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                _logger.LogWarning("Discord token response missing access_token");
                return null;
            }

            // Get user info
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, DISCORD_USER_URL);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

            var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                var errorContent = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Discord user info request failed: {Status} - {Error}", userResponse.StatusCode, errorContent);
                return null;
            }

            var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
            var userData = BannouJson.Deserialize<DiscordUserResponse>(userJson);
            if (userData == null)
            {
                _logger.LogWarning("Failed to parse Discord user response");
                return null;
            }

            return new OAuthUserInfo
            {
                ProviderId = userData.Id,
                Email = userData.Email,
                DisplayName = userData.GlobalName ?? userData.Username,
                AvatarUrl = userData.Avatar != null ? $"https://cdn.discordapp.com/avatars/{userData.Id}/{userData.Avatar}.png" : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging Discord authorization code");
            return null;
        }
    }

    /// <summary>
    /// Exchange Google authorization code for user info
    /// </summary>
    private async Task<OAuthUserInfo?> ExchangeGoogleCodeAsync(string code, CancellationToken cancellationToken)
    {
        // IMPLEMENTATION TENETS: Validate OAuth provider is configured before use
        var googleClientId = _configuration.GoogleClientId;
        var googleClientSecret = _configuration.GoogleClientSecret;
        var googleRedirectUri = _configuration.GoogleRedirectUri;

        if (googleClientId == null || googleClientSecret == null || googleRedirectUri == null)
        {
            _logger.LogError("Google OAuth not configured - set AUTH_GOOGLE_CLIENT_ID, AUTH_GOOGLE_CLIENT_SECRET, and AUTH_GOOGLE_REDIRECT_URI");
            return null;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            // Exchange code for access token
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", googleClientId },
                { "client_secret", googleClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", googleRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(GOOGLE_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Google token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = BannouJson.Deserialize<GoogleTokenResponse>(tokenJson);
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                _logger.LogWarning("Google token response missing access_token");
                return null;
            }

            // Get user info
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, GOOGLE_USER_URL);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

            var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                var errorContent = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Google user info request failed: {Status} - {Error}", userResponse.StatusCode, errorContent);
                return null;
            }

            var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
            var userData = BannouJson.Deserialize<GoogleUserResponse>(userJson);
            if (userData == null)
            {
                _logger.LogWarning("Failed to parse Google user response");
                return null;
            }

            return new OAuthUserInfo
            {
                ProviderId = userData.Id,
                Email = userData.Email,
                DisplayName = userData.Name,
                AvatarUrl = userData.Picture
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging Google authorization code");
            return null;
        }
    }

    /// <summary>
    /// Exchange Twitch authorization code for user info
    /// </summary>
    private async Task<OAuthUserInfo?> ExchangeTwitchCodeAsync(string code, CancellationToken cancellationToken)
    {
        // IMPLEMENTATION TENETS: Validate OAuth provider is configured before use
        var twitchClientId = _configuration.TwitchClientId;
        var twitchClientSecret = _configuration.TwitchClientSecret;
        var twitchRedirectUri = _configuration.TwitchRedirectUri;

        if (twitchClientId == null || twitchClientSecret == null || twitchRedirectUri == null)
        {
            _logger.LogError("Twitch OAuth not configured - set AUTH_TWITCH_CLIENT_ID, AUTH_TWITCH_CLIENT_SECRET, and AUTH_TWITCH_REDIRECT_URI");
            return null;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            // Exchange code for access token
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", twitchClientId },
                { "client_secret", twitchClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", twitchRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(TWITCH_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Twitch token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = BannouJson.Deserialize<TwitchTokenResponse>(tokenJson);
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                _logger.LogWarning("Twitch token response missing access_token");
                return null;
            }

            // Get user info
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, TWITCH_USER_URL);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            userRequest.Headers.Add("Client-Id", twitchClientId);

            var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                var errorContent = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Twitch user info request failed: {Status} - {Error}", userResponse.StatusCode, errorContent);
                return null;
            }

            var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
            var usersData = BannouJson.Deserialize<TwitchUsersResponse>(userJson);
            var userData = usersData?.Data?.FirstOrDefault();
            if (userData == null)
            {
                _logger.LogWarning("Failed to parse Twitch user response");
                return null;
            }

            return new OAuthUserInfo
            {
                ProviderId = userData.Id,
                Email = userData.Email,
                DisplayName = userData.DisplayName,
                AvatarUrl = userData.ProfileImageUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exchanging Twitch authorization code");
            return null;
        }
    }

    /// <summary>
    /// Validate Steam Session Ticket and retrieve SteamID
    /// </summary>
    private async Task<string?> ValidateSteamTicketAsync(string ticket, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            var requestUrl = $"{STEAM_AUTH_URL}?key={_configuration.SteamApiKey}&appid={_configuration.SteamAppId}&ticket={ticket}";

            var response = await httpClient.GetAsync(requestUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Steam ticket validation failed: {Status} - {Error}", response.StatusCode, errorContent);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var steamResponse = BannouJson.Deserialize<SteamAuthResponse>(responseJson);

            if (steamResponse?.Response?.Error != null)
            {
                _logger.LogWarning("Steam API returned error: {ErrorCode} - {ErrorDesc}",
                    steamResponse.Response.Error.ErrorCode, steamResponse.Response.Error.ErrorDesc);
                return null;
            }

            if (steamResponse?.Response?.Params == null || steamResponse.Response.Params.Result != "OK")
            {
                _logger.LogWarning("Steam ticket validation returned unexpected result");
                return null;
            }

            // Check for bans
            if (steamResponse.Response.Params.VacBanned || steamResponse.Response.Params.PublisherBanned)
            {
                _logger.LogWarning("Steam user is banned: VacBanned={VacBanned}, PublisherBanned={PublisherBanned}",
                    steamResponse.Response.Params.VacBanned, steamResponse.Response.Params.PublisherBanned);
                return null;
            }

            return steamResponse.Response.Params.SteamId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Steam ticket");
            return null;
        }
    }

    /// <summary>
    /// Find or create an account linked to an OAuth provider identity
    /// </summary>
    private async Task<AccountResponse?> FindOrCreateOAuthAccountAsync(
        Provider provider,
        OAuthUserInfo userInfo,
        CancellationToken cancellationToken,
        string? providerOverride = null)
    {
        var providerName = providerOverride ?? provider.ToString().ToLower();
        var oauthLinkKey = $"oauth-link:{providerName}:{userInfo.ProviderId}";
        var oauthLinkStore = _stateStoreFactory.GetStore<GuidWrapper>(REDIS_STATE_STORE);

        try
        {
            // Check if we have an existing link for this OAuth identity
            var wrapper = await oauthLinkStore.GetAsync(oauthLinkKey, cancellationToken);
            var existingAccountId = wrapper?.Value;

            if (existingAccountId.HasValue && existingAccountId.Value != Guid.Empty)
            {
                // Found existing account link, retrieve the account
                try
                {
                    var account = await _accountClient.GetAccountAsync(
                        new GetAccountRequest { AccountId = existingAccountId.Value },
                        cancellationToken);
                    _logger.LogInformation("Found existing account {AccountId} for {Provider} user {ProviderId}",
                        account.AccountId, providerName, userInfo.ProviderId);
                    return account;
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    // Account was deleted, remove stale link
                    _logger.LogWarning("Linked account {AccountId} not found, removing stale OAuth link", existingAccountId.Value);
                    await oauthLinkStore.DeleteAsync(oauthLinkKey, cancellationToken);
                }
            }

            // Create new account for this OAuth identity
            var createRequest = new CreateAccountRequest
            {
                Email = userInfo.Email ?? $"{providerName}_{userInfo.ProviderId}@oauth.local",
                DisplayName = userInfo.DisplayName ?? $"{providerName}_user",
                EmailVerified = userInfo.Email != null, // Consider OAuth-provided emails as verified
                PasswordHash = null // OAuth accounts don't have passwords
            };

            AccountResponse? newAccount;
            try
            {
                newAccount = await _accountClient.CreateAccountAsync(createRequest, cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Email already exists - try to find by email
                if (!string.IsNullOrEmpty(userInfo.Email))
                {
                    try
                    {
                        newAccount = await _accountClient.GetAccountByEmailAsync(
                            new GetAccountByEmailRequest { Email = userInfo.Email },
                            cancellationToken);
                        _logger.LogInformation("Found existing account by email {Email} for {Provider} user",
                            userInfo.Email, providerName);
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Email conflict but couldn't find account: {Email}", userInfo.Email);
                        return null;
                    }
                }
                else
                {
                    _logger.LogError("Account creation conflict but no email to search by");
                    return null;
                }
            }

            if (newAccount == null)
            {
                _logger.LogError("Failed to create account for {Provider} user {ProviderId}", providerName, userInfo.ProviderId);
                return null;
            }

            // Store the OAuth link
            await oauthLinkStore.SaveAsync(
                oauthLinkKey,
                new GuidWrapper { Value = newAccount.AccountId },
                cancellationToken: cancellationToken);

            _logger.LogInformation("Created new account {AccountId} and linked to {Provider} user {ProviderId}",
                newAccount.AccountId, providerName, userInfo.ProviderId);

            return newAccount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding or creating OAuth account for {Provider} user {ProviderId}",
                providerName, userInfo.ProviderId);
            return null;
        }
    }

    /// <summary>
    /// Handle mock OAuth authentication for testing
    /// </summary>
    private async Task<(StatusCodes, AuthResponse?)> HandleMockOAuthAsync(Provider provider, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Using mock OAuth provider for {Provider}", provider);

        var mockProviderId = provider switch
        {
            Provider.Discord => _configuration.MockDiscordId,
            Provider.Google => _configuration.MockGoogleId,
            Provider.Twitch => "mock-twitch-user-id-12345",
            _ => Guid.NewGuid().ToString()
        };

        var userInfo = new OAuthUserInfo
        {
            ProviderId = mockProviderId,
            Email = $"mock-{provider.ToString().ToLower()}@test.local",
            DisplayName = $"Mock {provider} User"
        };

        var account = await FindOrCreateOAuthAccountAsync(provider, userInfo, cancellationToken);
        if (account == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        // Mock providers don't need audit events - discard sessionId
        var (accessToken, _) = await GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = GenerateRefreshToken();
        await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

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

        var userInfo = new OAuthUserInfo
        {
            ProviderId = _configuration.MockSteamId,
            Email = null, // Steam doesn't provide email
            DisplayName = $"Steam_{_configuration.MockSteamId.Substring(_configuration.MockSteamId.Length - 6)}"
        };

        // Use a fake provider for Steam since it's not in the enum
        var account = await FindOrCreateOAuthAccountAsync(Provider.Discord, userInfo, cancellationToken, "steam");
        if (account == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        // Mock providers don't need audit events - discard sessionId
        var (accessToken, _) = await GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = GenerateRefreshToken();
        await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

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
    /// Called when a pub/sub event is received. Routes to appropriate event handlers.
    /// </summary>
    public async Task OnEventReceivedAsync<T>(string topic, T eventData) where T : class
    {
        _logger.LogDebug("Received event on topic {Topic}, DataType: {DataType}", topic, typeof(T).Name);

        switch (topic)
        {
            case "account.deleted":
                if (eventData is AccountDeletedEvent deletedEvent)
                {
                    await HandleAccountDeletedEventAsync(deletedEvent);
                }
                else
                {
                    _logger.LogError("Failed to cast event data to AccountDeletedEvent, ActualType: {ActualType}", eventData?.GetType().FullName ?? "null");
                }
                break;
            default:
                _logger.LogWarning("Received unknown event topic: {Topic}", topic);
                break;
        }
    }

    /// <summary>
    /// Handles account deleted events to invalidate all sessions for the deleted account.
    /// This ensures security by preventing continued access after account deletion.
    /// </summary>
    private async Task HandleAccountDeletedEventAsync(AccountDeletedEvent eventData)
    {
        try
        {
            _logger.LogInformation("Received account deleted event {EventId} for account: {AccountId}",
                eventData.EventId, eventData.AccountId);

            // Invalidate all sessions for the deleted account
            await InvalidateAllSessionsForAccountAsync(eventData.AccountId);

            _logger.LogInformation("Successfully invalidated all sessions for deleted account: {AccountId}",
                eventData.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process account deleted event {EventId} for account: {AccountId}",
                eventData.EventId, eventData.AccountId);

            // Don't re-throw - log the error and continue
            // The generated controller will handle HTTP response for the event endpoint
        }
    }

    /// <summary>
    /// Public wrapper for invalidating all sessions for a specific account.
    /// Called by AuthEventsController when account.deleted event is received.
    /// </summary>
    public async Task InvalidateAccountSessionsAsync(Guid accountId)
    {
        await InvalidateAllSessionsForAccountAsync(accountId, SessionInvalidatedEventReason.Account_deleted);
    }

    /// <summary>
    /// Invalidate all sessions for a specific account.
    /// Used when account is deleted to ensure security.
    /// Publishes SessionInvalidatedEvent to notify Connect service to disconnect clients.
    /// </summary>
    private async Task InvalidateAllSessionsForAccountAsync(Guid accountId, SessionInvalidatedEventReason reason = SessionInvalidatedEventReason.Account_deleted)
    {
        try
        {
            // Get session keys directly from the account index
            var indexKey = $"account-sessions:{accountId}";
            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(REDIS_STATE_STORE);
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);

            var sessionKeys = await sessionIndexStore.GetAsync(indexKey, CancellationToken.None);

            if (sessionKeys == null || !sessionKeys.Any())
            {
                _logger.LogDebug("No sessions found for account {AccountId}", accountId);
                return;
            }

            _logger.LogDebug("Invalidating {SessionCount} sessions for account {AccountId}", sessionKeys.Count, accountId);

            // Remove each session from Redis
            foreach (var sessionKey in sessionKeys)
            {
                try
                {
                    // Delete the session data
                    await sessionStore.DeleteAsync($"session:{sessionKey}", CancellationToken.None);
                    _logger.LogDebug("Deleted session {SessionKey} for account {AccountId}", sessionKey, accountId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete session {SessionKey} for account {AccountId}", sessionKey, accountId);
                    // Continue with other sessions even if one fails
                }
            }

            // Remove the account-to-sessions index
            await sessionIndexStore.DeleteAsync(indexKey, CancellationToken.None);

            _logger.LogInformation("Invalidated {SessionCount} sessions for account {AccountId}",
                sessionKeys.Count, accountId);

            // Publish session invalidation event for Connect service to disconnect clients
            await PublishSessionInvalidatedEventAsync(accountId, sessionKeys, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate sessions for account {AccountId}", accountId);
            throw; // Re-throw to let the event handler know about the failure
        }
    }

    /// <summary>
    /// Publish SessionInvalidatedEvent to notify Connect service to disconnect affected WebSocket clients.
    /// </summary>
    private async Task PublishSessionInvalidatedEventAsync(Guid accountId, List<string> sessionIds, SessionInvalidatedEventReason reason)
    {
        try
        {
            var eventModel = new SessionInvalidatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                SessionIds = sessionIds,
                Reason = reason,
                DisconnectClients = true
            };

            await _messageBus.TryPublishAsync(SESSION_INVALIDATED_TOPIC, eventModel);
            _logger.LogInformation("Published SessionInvalidatedEvent for account {AccountId}: {SessionCount} sessions, reason: {Reason}",
                accountId, sessionIds.Count, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish SessionInvalidatedEvent for account {AccountId}", accountId);
            // Don't throw - session invalidation succeeded, event publishing failure shouldn't fail the operation
        }
    }

    /// <summary>
    /// Propagate role changes to all active sessions for an account.
    /// Called when account.updated event is received with role changes.
    /// </summary>
    public async Task PropagateRoleChangesAsync(Guid accountId, List<string> newRoles, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Propagating role changes for account {AccountId}: {Roles}",
                accountId, string.Join(", ", newRoles));

            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(REDIS_STATE_STORE);
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);

            var sessionKeys = await sessionIndexStore.GetAsync($"account-sessions:{accountId}", cancellationToken);

            if (sessionKeys == null || !sessionKeys.Any())
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

                        await sessionStore.SaveAsync($"session:{sessionKey}", session, cancellationToken: cancellationToken);

                        // Publish session.updated event for Permission service
                        await PublishSessionUpdatedEventAsync(
                            accountId,
                            session.SessionId,
                            newRoles,
                            session.Authorizations,
                            SessionUpdatedEventReason.Role_changed,
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to propagate role changes for account {AccountId}", accountId);
            throw;
        }
    }

    /// <summary>
    /// Propagate subscription/authorization changes to all active sessions for an account.
    /// Called when subscription.updated event is received.
    /// </summary>
    public async Task PropagateSubscriptionChangesAsync(Guid accountId, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Propagating subscription changes for account {AccountId}", accountId);

            // Fetch fresh authorizations from Subscription service
            var authorizations = new List<string>();
            var subscriptionsResponse = await _subscriptionClient.QueryCurrentSubscriptionsAsync(
                new QueryCurrentSubscriptionsRequest { AccountId = accountId },
                cancellationToken);

            if (subscriptionsResponse?.Subscriptions != null)
            {
                authorizations = subscriptionsResponse.Subscriptions.Select(s => s.StubName).ToList();
            }

            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(REDIS_STATE_STORE);
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(REDIS_STATE_STORE);

            var sessionKeys = await sessionIndexStore.GetAsync($"account-sessions:{accountId}", cancellationToken);

            if (sessionKeys == null || !sessionKeys.Any())
            {
                _logger.LogDebug("No sessions found for account {AccountId} to propagate subscription changes", accountId);
                return;
            }

            foreach (var sessionKey in sessionKeys)
            {
                try
                {
                    var session = await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);

                    if (session != null)
                    {
                        session.Authorizations = authorizations;

                        await sessionStore.SaveAsync($"session:{sessionKey}", session, cancellationToken: cancellationToken);

                        // Publish session.updated event for Permission service
                        await PublishSessionUpdatedEventAsync(
                            accountId,
                            session.SessionId,
                            session.Roles,
                            authorizations,
                            SessionUpdatedEventReason.Authorization_changed,
                            cancellationToken);

                        _logger.LogDebug("Updated authorizations for session {SessionKey}", sessionKey);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update authorizations for session {SessionKey}", sessionKey);
                }
            }

            _logger.LogInformation("Propagated subscription changes to {Count} sessions for account {AccountId}: {Authorizations}",
                sessionKeys.Count, accountId, string.Join(", ", authorizations));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to propagate subscription changes for account {AccountId}", accountId);
            throw;
        }
    }

    /// <summary>
    /// Publish SessionUpdatedEvent to notify Permission service about role/authorization changes.
    /// </summary>
    private async Task PublishSessionUpdatedEventAsync(
        Guid accountId,
        string sessionId,
        List<string> roles,
        List<string> authorizations,
        SessionUpdatedEventReason reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var eventModel = new SessionUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                SessionId = Guid.Parse(sessionId),
                Roles = roles,
                Authorizations = authorizations,
                Reason = reason
            };

            await _messageBus.TryPublishAsync(SESSION_UPDATED_TOPIC, eventModel, cancellationToken: cancellationToken);
            _logger.LogDebug("Published SessionUpdatedEvent for session {SessionId}, reason: {Reason}",
                sessionId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish SessionUpdatedEvent for session {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "PublishSessionUpdatedEvent",
                "event_publishing_failed",
                ex.Message,
                dependency: "messaging",
                endpoint: null,
                details: $"sessionId={sessionId},reason={reason}",
                stack: ex.StackTrace);
            // Don't throw - session update succeeded, event publishing failure shouldn't fail the operation
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Auth service permissions...");
        await AuthPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

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

    /// <summary>
    /// Publish AuthLoginSuccessfulEvent for security audit trail.
    /// </summary>
    private async Task PublishLoginSuccessfulEventAsync(Guid accountId, string username, string sessionId)
    {
        try
        {
            var eventModel = new AuthLoginSuccessfulEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                Username = username,
                SessionId = Guid.Parse(sessionId)
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
    private async Task PublishRegistrationSuccessfulEventAsync(Guid accountId, string username, string email, string sessionId)
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
                SessionId = Guid.Parse(sessionId)
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
    private async Task PublishOAuthLoginSuccessfulEventAsync(Guid accountId, string provider, string providerUserId, string sessionId, bool isNewAccount)
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
                SessionId = Guid.Parse(sessionId),
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
    private async Task PublishSteamLoginSuccessfulEventAsync(Guid accountId, string steamId, string sessionId, bool isNewAccount)
    {
        try
        {
            var eventModel = new AuthSteamLoginSuccessfulEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                SteamId = steamId,
                SessionId = Guid.Parse(sessionId),
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

    #endregion
}

/// <summary>
/// Wrapper class for storing string values in IStateStore (since value types need a class wrapper).
/// </summary>
internal class StringWrapper
{
    public string Value { get; set; } = "";
}

/// <summary>
/// Wrapper class for storing Guid? values in IStateStore (since value types need a class wrapper).
/// </summary>
internal class GuidWrapper
{
    public Guid? Value { get; set; }
}
