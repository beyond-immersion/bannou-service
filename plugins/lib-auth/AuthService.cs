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
            var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);

            var refreshToken = _tokenService.GenerateRefreshToken();

            // Store refresh token
            await _tokenService.StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

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
            var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(accountResult, cancellationToken);
            var refreshToken = _tokenService.GenerateRefreshToken();

            // Store refresh token
            await _tokenService.StoreRefreshTokenAsync(accountResult.AccountId.ToString(), refreshToken, cancellationToken);

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
            var account = await _oauthService.FindOrCreateOAuthAccountAsync(provider, userInfo, cancellationToken);
            if (account == null)
            {
                _logger.LogError("Failed to find or create account for OAuth user: {ProviderId}", userInfo.ProviderId);
                await PublishErrorEventAsync("CompleteOAuth", "account_creation_failed", "Failed to find or create account for OAuth user", dependency: "account", details: new { Provider = provider.ToString(), ProviderId = userInfo.ProviderId });
                return (StatusCodes.InternalServerError, null);
            }

            // Generate tokens (returns both accessToken and sessionId for event publishing)
            var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

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
            var steamId = await _oauthService.ValidateSteamTicketAsync(body.Ticket, cancellationToken);
            if (string.IsNullOrEmpty(steamId))
            {
                _logger.LogWarning("Steam ticket validation failed");
                return (StatusCodes.Unauthorized, null);
            }

            _logger.LogInformation("Steam ticket validated successfully for SteamID: {SteamId}", steamId);

            // Find or create account linked to this Steam identity
            var userInfo = new Services.OAuthUserInfo
            {
                ProviderId = steamId,
                DisplayName = $"Steam_{steamId.Substring(steamId.Length - 6)}", // Last 6 chars of Steam ID
                Email = null // Steam doesn't provide email
            };

            var account = await _oauthService.FindOrCreateOAuthAccountAsync(Provider.Discord, userInfo, cancellationToken, "steam");
            if (account == null)
            {
                _logger.LogError("Failed to find or create account for Steam user: {SteamId}", steamId);
                await PublishErrorEventAsync("VerifySteamAuth", "account_creation_failed", "Failed to find or create account for Steam user", dependency: "account", details: new { SteamId = steamId });
                return (StatusCodes.InternalServerError, null);
            }

            // Generate tokens (returns both accessToken and sessionId for event publishing)
            var (accessToken, sessionId) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
            var refreshToken = _tokenService.GenerateRefreshToken();
            await _tokenService.StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

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
            var accountId = await _tokenService.ValidateRefreshTokenAsync(body.RefreshToken, cancellationToken);
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
            var (accessToken, _) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
            var newRefreshToken = _tokenService.GenerateRefreshToken();

            // Store new refresh token and remove old one
            await _tokenService.StoreRefreshTokenAsync(accountId, newRefreshToken, cancellationToken);
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
            var sessionKey = await _tokenService.ExtractSessionKeyFromJwtAsync(jwt);
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
                var accountSessions = await _sessionService.GetAccountSessionsAsync(validateResponse.AccountId.ToString(), cancellationToken);

                if (accountSessions.Count > 0)
                {
                    // Get session keys from index to delete all sessions
                    var indexKey = $"account-sessions:{validateResponse.AccountId}";
                    var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
                    var sessionKeys = await sessionIndexStore.GetAsync(indexKey, cancellationToken);

                    if (sessionKeys != null && sessionKeys.Count > 0)
                    {
                        // Delete all sessions
                        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);
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
                var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);
                await sessionStore.DeleteAsync($"session:{sessionKey}", cancellationToken);

                // Remove session from account index
                await _sessionService.RemoveSessionFromAccountIndexAsync(validateResponse.AccountId.ToString(), sessionKey, cancellationToken);

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
            var sessionKey = await _sessionService.FindSessionKeyBySessionIdAsync(sessionId.ToString(), cancellationToken);

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
                await _sessionService.RemoveSessionFromAccountIndexAsync(sessionData.AccountId.ToString(), sessionKey, cancellationToken);
            }

            // Remove reverse index entry
            await _sessionService.RemoveSessionIdReverseIndexAsync(sessionId.ToString(), cancellationToken);

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
                var resetToken = _tokenService.GenerateSecureToken();
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
                var resetStore = _stateStoreFactory.GetStore<PasswordResetData>(StateStoreDefinitions.Auth);
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
            var sessions = await _sessionService.GetAccountSessionsAsync(validateResponse.AccountId.ToString(), cancellationToken);

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
                var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);
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
                AuthType = ProviderInfoAuthType.Oauth,
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
                AuthType = ProviderInfoAuthType.Oauth,
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
                AuthType = ProviderInfoAuthType.Oauth,
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
                AuthType = ProviderInfoAuthType.Ticket,
                AuthUrl = null // Steam uses session tickets from game client, not browser redirect
            });
        }

        _logger.LogInformation("Returning {ProviderCount} available authentication provider(s)", providers.Count);

        return (StatusCodes.OK, new ProvidersResponse
        {
            Providers = providers
        });
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

    // Token generation and session management methods have been extracted to ITokenService and ISessionService
    // for improved testability and separation of concerns.

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
        var account = await _oauthService.FindOrCreateOAuthAccountAsync(provider, userInfo, cancellationToken);
        if (account == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        // Mock providers don't need audit events - discard sessionId
        var (accessToken, _) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

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
        // Use a fake provider for Steam since it's not in the enum
        var account = await _oauthService.FindOrCreateOAuthAccountAsync(Provider.Discord, userInfo, cancellationToken, "steam");
        if (account == null)
        {
            return (StatusCodes.InternalServerError, null);
        }

        // Mock providers don't need audit events - discard sessionId
        var (accessToken, _) = await _tokenService.GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = _tokenService.GenerateRefreshToken();
        await _tokenService.StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

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
            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);

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

            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);

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

            var sessionIndexStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
            var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);

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
