using BCrypt.Net;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Dapr;
using Dapr.Client;
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
[DaprService("auth", typeof(IAuthService), lifetime: ServiceLifetime.Scoped)]
public class AuthService : IAuthService
{
    private readonly IAccountsClient _accountsClient;
    private readonly DaprClient _daprClient;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private const string REDIS_STATE_STORE = "statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string SESSION_INVALIDATED_TOPIC = "session.invalidated";

    // OAuth provider URLs
    private const string DISCORD_TOKEN_URL = "https://discord.com/api/oauth2/token";
    private const string DISCORD_USER_URL = "https://discord.com/api/users/@me";
    private const string GOOGLE_TOKEN_URL = "https://oauth2.googleapis.com/token";
    private const string GOOGLE_USER_URL = "https://www.googleapis.com/oauth2/v2/userinfo";
    private const string TWITCH_TOKEN_URL = "https://id.twitch.tv/oauth2/token";
    private const string TWITCH_USER_URL = "https://api.twitch.tv/helix/users";
    private const string STEAM_AUTH_URL = "https://partner.steam-api.com/ISteamUserAuth/AuthenticateUserTicket/v1/";

    public AuthService(
        IAccountsClient accountsClient,
        DaprClient daprClient,
        AuthServiceConfiguration configuration,
        ILogger<AuthService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _accountsClient = accountsClient ?? throw new ArgumentNullException(nameof(accountsClient));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

        _logger.LogInformation("AuthService initialized with JwtSecret length: {Length}, Issuer: {Issuer}, Audience: {Audience}, MockProviders: {MockProviders}",
            _configuration.JwtSecret?.Length ?? 0, _configuration.JwtIssuer, _configuration.JwtAudience, _configuration.MockProviders);
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

            // Lookup account by email via AccountsClient
            _logger.LogInformation("Looking up account by email via AccountsClient: {Email}", body.Email);

            AccountResponse account;
            try
            {
                account = await _accountsClient.GetAccountByEmailAsync(new GetAccountByEmailRequest { Email = body.Email }, cancellationToken);
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
                return (StatusCodes.Unauthorized, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lookup account by email via AccountsClient");
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
                return (StatusCodes.Unauthorized, null);
            }

            _logger.LogInformation("Password verification successful for email: {Email}", body.Email);

            // Generate tokens
            var accessToken = await GenerateAccessTokenAsync(account, cancellationToken);

            var refreshToken = GenerateRefreshToken();

            // Store refresh token
            await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

            _logger.LogInformation("Successfully authenticated user: {Email} (ID: {AccountId})",
                body.Email, account.AccountId);

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = account.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = new Uri(_configuration.ConnectUrl)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for email: {Email}", body.Email);
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

            // Create account via AccountsClient service call
            _logger.LogInformation("Creating account via AccountsClient for registration: {Email}", body.Email ?? $"{body.Username}@example.com");

            var createRequest = new CreateAccountRequest
            {
                Email = body.Email ?? $"{body.Username}@example.com",
                DisplayName = body.Username,
                PasswordHash = passwordHash, // Store hashed password
                EmailVerified = false
            };

            AccountResponse? accountResult;
            try
            {
                accountResult = await _accountsClient.CreateAccountAsync(createRequest, cancellationToken);
                _logger.LogInformation("Account created successfully via service call: {AccountId}", accountResult?.AccountId);

                if (accountResult == null)
                {
                    _logger.LogWarning("AccountsClient returned null response");
                    return (StatusCodes.InternalServerError, null);
                }
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                _logger.LogWarning("Account with email {Email} already exists", body.Email ?? body.Username);
                return (StatusCodes.Conflict, null);
            }
            catch (ApiException ex) when (ex.StatusCode == 400)
            {
                _logger.LogWarning(ex, "Invalid account data for registration");
                return (StatusCodes.BadRequest, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create account via AccountsClient");
                return (StatusCodes.InternalServerError, null);
            }

            // Generate tokens
            var accessToken = await GenerateAccessTokenAsync(accountResult, cancellationToken);
            var refreshToken = GenerateRefreshToken();

            // Store refresh token
            await StoreRefreshTokenAsync(accountResult.AccountId.ToString(), refreshToken, cancellationToken);

            _logger.LogInformation("Successfully registered user: {Username} with ID: {AccountId}",
                body.Username, accountResult.AccountId);

            return (StatusCodes.OK, new RegisterResponse
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for username: {Username}", body.Username);
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
                return (StatusCodes.InternalServerError, null);
            }

            // Generate tokens
            var accessToken = await GenerateAccessTokenAsync(account, cancellationToken);
            var refreshToken = GenerateRefreshToken();
            await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

            _logger.LogInformation("OAuth authentication successful for account {AccountId} via {Provider}",
                account.AccountId, provider);

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = account.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = new Uri(_configuration.ConnectUrl)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth callback for provider: {Provider}", provider);
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
                return (StatusCodes.InternalServerError, null);
            }

            // Generate tokens
            var accessToken = await GenerateAccessTokenAsync(account, cancellationToken);
            var refreshToken = GenerateRefreshToken();
            await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

            _logger.LogInformation("Steam authentication successful for account {AccountId}", account.AccountId);

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = account.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = new Uri(_configuration.ConnectUrl)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Steam authentication verification");
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

            // Lookup account by ID via AccountsClient
            _logger.LogInformation("Looking up account by ID via AccountsClient: {AccountId}", accountId);

            AccountResponse account;
            try
            {
                account = await _accountsClient.GetAccountAsync(new GetAccountRequest { AccountId = Guid.Parse(accountId) }, cancellationToken);
                _logger.LogInformation("Account found for refresh: {AccountId}", account?.AccountId);

                if (account == null)
                {
                    _logger.LogWarning("No account found for ID: {AccountId}", accountId);
                    return (StatusCodes.Unauthorized, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to lookup account by ID via AccountsClient");
                return (StatusCodes.InternalServerError, null);
            }

            // Generate new tokens
            var accessToken = await GenerateAccessTokenAsync(account, cancellationToken);
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
                ConnectUrl = new Uri(_configuration.ConnectUrl)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return (StatusCodes.InternalServerError, null);
        }
    }


    /// <inheritdoc/>
    public Task<(StatusCodes, object?)> InitOAuthAsync(
        Provider provider,
        string redirectUri,
        string? state,
        CancellationToken cancellationToken = default)
    {
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
                        return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
                    }
                    var discordRedirectUri = HttpUtility.UrlEncode(redirectUri ?? _configuration.DiscordRedirectUri);
                    authUrl = $"https://discord.com/oauth2/authorize?client_id={_configuration.DiscordClientId}&response_type=code&redirect_uri={discordRedirectUri}&scope=identify%20email&state={encodedState}";
                    break;

                case Provider.Google:
                    if (string.IsNullOrWhiteSpace(_configuration.GoogleClientId))
                    {
                        _logger.LogError("Google Client ID not configured");
                        return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
                    }
                    var googleRedirectUri = HttpUtility.UrlEncode(redirectUri ?? _configuration.GoogleRedirectUri);
                    authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?client_id={_configuration.GoogleClientId}&response_type=code&redirect_uri={googleRedirectUri}&scope=openid%20email%20profile&state={encodedState}";
                    break;

                case Provider.Twitch:
                    if (string.IsNullOrWhiteSpace(_configuration.TwitchClientId))
                    {
                        _logger.LogError("Twitch Client ID not configured");
                        return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
                    }
                    var twitchRedirectUri = HttpUtility.UrlEncode(redirectUri ?? _configuration.TwitchRedirectUri);
                    authUrl = $"https://id.twitch.tv/oauth2/authorize?client_id={_configuration.TwitchClientId}&response_type=code&redirect_uri={twitchRedirectUri}&scope=user:read:email&state={encodedState}";
                    break;

                default:
                    _logger.LogWarning("Unknown OAuth provider: {Provider}", provider);
                    return Task.FromResult<(StatusCodes, object?)>((StatusCodes.BadRequest, null));
            }

            _logger.LogDebug("Generated OAuth URL for {Provider}: {Url}", provider, authUrl);
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, new { AuthorizationUrl = authUrl }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing OAuth for provider: {Provider}", provider);
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <inheritdoc/>
    public Task<(StatusCodes, object?)> InitSteamAuthAsync(
        string returnUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Steam authentication info requested");

            // Steam uses Session Tickets, not OAuth/OpenID flow
            // The game client calls ISteamUser::GetAuthTicketForWebApi("bannou")
            // Then sends the ticket to POST /auth/steam/verify
            var response = new
            {
                Message = "Steam uses Session Tickets, not browser-based OAuth",
                Endpoint = "/auth/steam/verify",
                Method = "POST",
                ClientSide = "Call ISteamUser::GetAuthTicketForWebApi(\"bannou\") in your game client",
                Documentation = "https://partner.steamgames.com/doc/features/auth#web_api"
            };

            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error providing Steam authentication info");
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, object?)> LogoutAsync(
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
                return (StatusCodes.Unauthorized, null);
            }

            // Validate JWT and extract session key
            var (validateStatus, validateResponse) = await ValidateTokenAsync(jwt, cancellationToken);
            if (validateStatus != StatusCodes.OK || validateResponse == null || !validateResponse.Valid)
            {
                _logger.LogWarning("Invalid JWT token provided for logout");
                return (StatusCodes.Unauthorized, null);
            }

            // Extract session_key from JWT claims to identify which session to logout
            var sessionKey = await ExtractSessionKeyFromJWT(jwt);
            if (sessionKey == null)
            {
                _logger.LogWarning("Could not extract session_key from JWT for logout");
                return (StatusCodes.Unauthorized, null);
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
                    var sessionKeys = await _daprClient.GetStateAsync<List<string>>(
                        REDIS_STATE_STORE,
                        indexKey,
                        cancellationToken: cancellationToken);

                    if (sessionKeys != null && sessionKeys.Count > 0)
                    {
                        // Delete all sessions
                        var deleteTasks = sessionKeys.Select(key =>
                            _daprClient.DeleteStateAsync(REDIS_STATE_STORE, $"session:{key}", cancellationToken: cancellationToken));
                        await Task.WhenAll(deleteTasks);

                        // Remove the account sessions index
                        await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, indexKey, cancellationToken: cancellationToken);

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
                await _daprClient.DeleteStateAsync(
                    REDIS_STATE_STORE,
                    $"session:{sessionKey}",
                    cancellationToken: cancellationToken);

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

            return (StatusCodes.OK, new { Message = "Logout successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, object?)> TerminateSessionAsync(
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
                return (StatusCodes.NotFound, null);
            }

            // Get session data to find account ID for index cleanup
            var sessionData = await _daprClient.GetStateAsync<SessionDataModel>(
                REDIS_STATE_STORE,
                $"session:{sessionKey}",
                cancellationToken: cancellationToken);

            // Remove the session data from Redis
            await _daprClient.DeleteStateAsync(
                REDIS_STATE_STORE,
                $"session:{sessionKey}",
                cancellationToken: cancellationToken);

            // Remove session from account index if we found the session data
            if (sessionData != null)
            {
                await RemoveSessionFromAccountIndexAsync(sessionData.AccountId.ToString(), sessionKey, cancellationToken);
            }

            // Remove reverse index entry
            await RemoveSessionIdReverseIndexAsync(sessionId.ToString(), cancellationToken);

            _logger.LogInformation("Session {SessionId} terminated successfully", sessionId);
            return (StatusCodes.NoContent, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating session: {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, object?)> RequestPasswordResetAsync(
        PasswordResetRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing password reset request for email: {Email}", body.Email);

            if (string.IsNullOrWhiteSpace(body.Email))
            {
                return (StatusCodes.BadRequest, new { Error = "Email is required" });
            }

            // Verify account exists (but always return success to prevent email enumeration)
            AccountResponse? account = null;
            try
            {
                account = await _accountsClient.GetAccountByEmailAsync(
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
                await _daprClient.SaveStateAsync(
                    REDIS_STATE_STORE,
                    $"password-reset:{resetToken}",
                    resetData,
                    metadata: new Dictionary<string, string> { { "ttl", (resetTokenTtlMinutes * 60).ToString() } },
                    cancellationToken: cancellationToken);

                // Send password reset email (mock implementation logs to console)
                await SendPasswordResetEmailAsync(account.Email, resetToken, resetTokenTtlMinutes, cancellationToken);

                _logger.LogInformation("Password reset token generated for account {AccountId}, expires in {Minutes} minutes",
                    account.AccountId, resetTokenTtlMinutes);
            }

            // Always return success to prevent email enumeration attacks
            return (StatusCodes.OK, new { Message = "If the email exists, a password reset link has been sent" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting password reset for email: {Email}", body.Email);
            return (StatusCodes.InternalServerError, null);
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
    private Task SendPasswordResetEmailAsync(string email, string resetToken, int expiresInMinutes, CancellationToken cancellationToken)
    {
        // Mock email implementation - logs to console
        // In production, this would integrate with SendGrid, AWS SES, or similar
        var resetUrl = $"{_configuration.PasswordResetBaseUrl ?? "https://example.com/reset-password"}?token={resetToken}";

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

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, object?)> ConfirmPasswordResetAsync(
        PasswordResetConfirmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing password reset confirmation with token: {TokenPrefix}...",
                body.Token?.Length > 10 ? body.Token.Substring(0, 10) : body.Token);

            if (string.IsNullOrWhiteSpace(body.Token) || string.IsNullOrWhiteSpace(body.NewPassword))
            {
                _logger.LogWarning("Invalid password reset confirmation request - missing token or password");
                return (StatusCodes.BadRequest, null);
            }

            // Look up the reset token in Redis
            var resetData = await _daprClient.GetStateAsync<PasswordResetData>(
                REDIS_STATE_STORE,
                $"password-reset:{body.Token}",
                cancellationToken: cancellationToken);

            if (resetData == null)
            {
                _logger.LogWarning("Invalid or expired password reset token");
                return (StatusCodes.BadRequest, new { Error = "Invalid or expired reset token" });
            }

            // Check if token has expired
            if (resetData.ExpiresAt < DateTimeOffset.UtcNow)
            {
                _logger.LogWarning("Password reset token has expired");
                // Clean up expired token
                await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, $"password-reset:{body.Token}", cancellationToken: cancellationToken);
                return (StatusCodes.BadRequest, new { Error = "Reset token has expired" });
            }

            // Hash the new password
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(body.NewPassword, workFactor: 12);

            // Update password via AccountsClient
            await _accountsClient.UpdatePasswordHashAsync(new UpdatePasswordRequest
            {
                AccountId = resetData.AccountId,
                PasswordHash = passwordHash
            }, cancellationToken);

            // Remove the used token
            await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, $"password-reset:{body.Token}", cancellationToken: cancellationToken);

            _logger.LogInformation("Password reset successful for account {AccountId}", resetData.AccountId);
            return (StatusCodes.OK, new { Message = "Password reset successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming password reset");
            return (StatusCodes.InternalServerError, null);
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
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_configuration.JwtSecret);

            try
            {
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _configuration.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = _configuration.JwtAudience,
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

                // Lookup session data from Redis using session_key
                var sessionData = await _daprClient.GetStateAsync<SessionDataModel>(
                    REDIS_STATE_STORE,
                    $"session:{sessionKey}",
                    cancellationToken: cancellationToken);

                if (sessionData == null)
                {
                    _logger.LogWarning("Session not found in Redis for session_key: {SessionKey}", sessionKey);
                    return (StatusCodes.Unauthorized, null);
                }

                // Check if session has expired
                if (sessionData.ExpiresAt < DateTime.UtcNow)
                {
                    _logger.LogWarning("Session has expired for session_key: {SessionKey}", sessionKey);
                    return (StatusCodes.Unauthorized, null);
                }

                _logger.LogInformation("JWT token validated successfully for account: {AccountId}", sessionData.AccountId);

                // Return session information
                // IMPORTANT: Return sessionKey (not sessionData.SessionId) so Connect service
                // tracks connections by the same key used in account-sessions index and
                // published in SessionInvalidatedEvent for proper WebSocket disconnection
                return (StatusCodes.OK, new ValidateTokenResponse
                {
                    Valid = true,
                    AccountId = sessionData.AccountId,
                    SessionId = sessionKey,
                    Roles = sessionData.Roles ?? new List<string>(),
                    RemainingTime = (int)(sessionData.ExpiresAt - DateTime.UtcNow).TotalSeconds
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
                return (StatusCodes.InternalServerError, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
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
        public string DisplayName { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new List<string>();
        public string SessionId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    private string HashPassword(string password)
    {
        // Simplified password hashing - use BCrypt in production
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(password + _configuration.JwtSecret));
    }

    private bool VerifyPassword(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        return HashPassword(password) == hash;
    }

    private async Task<string> GenerateAccessTokenAsync(AccountResponse account, CancellationToken cancellationToken = default)
    {
        // Validate inputs
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        if (_configuration == null)
            throw new InvalidOperationException("AuthServiceConfiguration is null");

        if (string.IsNullOrWhiteSpace(_configuration.JwtSecret))
            throw new InvalidOperationException("JWT secret is not configured");

        if (string.IsNullOrWhiteSpace(_configuration.JwtIssuer))
            throw new InvalidOperationException("JWT issuer is not configured");

        if (string.IsNullOrWhiteSpace(_configuration.JwtAudience))
            throw new InvalidOperationException("JWT audience is not configured");

        _logger.LogDebug("Generating access token for account {AccountId} with JWT config: Secret={SecretLength}, Issuer={Issuer}, Audience={Audience}",
            account.AccountId, _configuration.JwtSecret?.Length, _configuration.JwtIssuer, _configuration.JwtAudience);

        // Generate opaque session key (per API-DESIGN.md security pattern)
        var sessionKey = Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid().ToString();

        // Store session data in Redis with opaque key
        var sessionData = new SessionDataModel
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName ?? string.Empty,
            Roles = account.Roles?.ToList() ?? new List<string>(),
            SessionId = sessionId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_configuration.JwtExpirationMinutes)
        };

        await _daprClient.SaveStateAsync(
            REDIS_STATE_STORE,
            $"session:{sessionKey}",
            sessionData,
            metadata: new Dictionary<string, string> { { "ttl", (_configuration.JwtExpirationMinutes * 60).ToString() } },
            cancellationToken: cancellationToken);

        // Maintain account-to-sessions index for efficient GetSessions implementation
        await AddSessionToAccountIndexAsync(account.AccountId.ToString(), sessionKey, cancellationToken);

        // Maintain reverse index (session_id -> session_key) for TerminateSession functionality
        await AddSessionIdReverseIndexAsync(sessionId, sessionKey, _configuration.JwtExpirationMinutes * 60, cancellationToken);

        // JWT contains only opaque session key - no sensitive data
        var jwtSecret = _configuration.JwtSecret ?? throw new InvalidOperationException("JWT secret is not configured");
        var key = Encoding.UTF8.GetBytes(jwtSecret);


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
            // 1. Roles stored in AccountModel (MySQL via Dapr) - assigned by Accounts service
            // 2. Roles copied to SessionDataModel (Redis) during session creation - stored with session data
            // 3. JWT contains only opaque "session_key" claim - points to Redis session data
            // 4. ValidateTokenAsync reads roles from Redis session data (NOT from JWT claims)
            // 5. Connect service receives roles from ValidateTokenAsync response
            // 6. Permissions service compiles capabilities based on role from session
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
                Issuer = _configuration.JwtIssuer,
                Audience = _configuration.JwtAudience
            };

            var jwt = tokenHandler.CreateToken(tokenDescriptor);
            var jwtString = tokenHandler.WriteToken(jwt);
            return jwtString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to generate security token for session with ID {sessionKey}.");
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
        await _daprClient.SaveStateAsync(
            REDIS_STATE_STORE,
            redisKey,
            accountId,
            metadata: new Dictionary<string, string> { { "ttl", "604800" } }, // 7 days
            cancellationToken: cancellationToken);
    }

    private async Task<string?> ValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            var accountId = await _daprClient.GetStateAsync<string>(REDIS_STATE_STORE, redisKey, cancellationToken: cancellationToken);
            return accountId;
        }
        catch
        {
            return null;
        }
    }

    private async Task RemoveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, redisKey, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove refresh token");
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
            var sessionKey = await _daprClient.GetStateAsync<string>(
                REDIS_STATE_STORE,
                $"session-id-index:{sessionId}",
                cancellationToken: cancellationToken);

            if (!string.IsNullOrEmpty(sessionKey))
            {
                _logger.LogDebug("Found session key {SessionKey} for session ID {SessionId}", sessionKey, sessionId);
                return sessionKey;
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
            await _daprClient.SaveStateAsync(
                REDIS_STATE_STORE,
                $"session-id-index:{sessionId}",
                sessionKey,
                metadata: new Dictionary<string, string> { { "ttl", ttlSeconds.ToString() } },
                cancellationToken: cancellationToken);

            _logger.LogDebug("Added reverse index for session ID {SessionId} -> session key {SessionKey}", sessionId, sessionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add reverse index for session ID {SessionId}", sessionId);
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
            await _daprClient.DeleteStateAsync(
                REDIS_STATE_STORE,
                $"session-id-index:{sessionId}",
                cancellationToken: cancellationToken);

            _logger.LogDebug("Removed reverse index for session ID {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove reverse index for session ID {SessionId}", sessionId);
            // Don't throw - operation should continue even if cleanup fails
        }
    }

    /// <summary>
    /// Extract session_key from JWT token without full validation (for logout operations)
    /// </summary>
    private Task<string?> ExtractSessionKeyFromJWT(string jwt)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jsonToken = tokenHandler.ReadJwtToken(jwt);

            var sessionKeyClaim = jsonToken?.Claims?.FirstOrDefault(c => c.Type == "session_key");
            return Task.FromResult(sessionKeyClaim?.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting session_key from JWT");
            return Task.FromResult<string?>(null);
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
            _logger.LogInformation("[DIAG] AddSessionToAccountIndexAsync called with accountId={AccountId}, sessionKey={SessionKey}, indexKey={IndexKey}",
                accountId, sessionKey, indexKey);

            // Get existing session list
            var existingSessions = await _daprClient.GetStateAsync<List<string>>(
                REDIS_STATE_STORE,
                indexKey,
                cancellationToken: cancellationToken) ?? new List<string>();

            // Add new session if not already present
            if (!existingSessions.Contains(sessionKey))
            {
                existingSessions.Add(sessionKey);

                // Save updated list with TTL slightly longer than session TTL to handle clock skew
                var accountIndexTtl = (_configuration.JwtExpirationMinutes * 60) + 300; // +5 minutes buffer
                await _daprClient.SaveStateAsync(
                    REDIS_STATE_STORE,
                    indexKey,
                    existingSessions,
                    metadata: new Dictionary<string, string> { { "ttl", accountIndexTtl.ToString() } },
                    cancellationToken: cancellationToken);

                _logger.LogInformation("[DIAG] SAVED session index: indexKey={IndexKey}, sessions={SessionsJson}",
                    indexKey, System.Text.Json.JsonSerializer.Serialize(existingSessions));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DIAG] FAILED to add session {SessionKey} to account index for account {AccountId}", sessionKey, accountId);
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

            // Get existing session list
            var existingSessions = await _daprClient.GetStateAsync<List<string>>(
                REDIS_STATE_STORE,
                indexKey,
                cancellationToken: cancellationToken);

            if (existingSessions != null && existingSessions.Contains(sessionKey))
            {
                existingSessions.Remove(sessionKey);

                if (existingSessions.Count > 0)
                {
                    // Save updated list
                    var accountIndexTtl = (_configuration.JwtExpirationMinutes * 60) + 300; // +5 minutes buffer
                    await _daprClient.SaveStateAsync(
                        REDIS_STATE_STORE,
                        indexKey,
                        existingSessions,
                        metadata: new Dictionary<string, string> { { "ttl", accountIndexTtl.ToString() } },
                        cancellationToken: cancellationToken);
                }
                else
                {
                    // Remove empty index
                    await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, indexKey, cancellationToken: cancellationToken);
                }

                _logger.LogDebug("Removed session {SessionKey} from account index for account {AccountId}", sessionKey, accountId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove session {SessionKey} from account index for account {AccountId}", sessionKey, accountId);
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

            // Get session keys from account index
            var sessionKeys = await _daprClient.GetStateAsync<List<string>>(
                REDIS_STATE_STORE,
                indexKey,
                cancellationToken: cancellationToken);

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
                    var sessionData = await _daprClient.GetStateAsync<SessionDataModel>(
                        REDIS_STATE_STORE,
                        $"session:{key}",
                        cancellationToken: cancellationToken);

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
                    if (result.SessionData.ExpiresAt > DateTime.UtcNow)
                    {
                        sessions.Add(new SessionInfo
                        {
                            SessionId = result.SessionData.SessionId,
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
            return new List<SessionInfo>();
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
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            // Exchange code for access token
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", _configuration.DiscordClientId },
                { "client_secret", _configuration.DiscordClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", _configuration.DiscordRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(DISCORD_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Discord token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = JsonSerializer.Deserialize<DiscordTokenResponse>(tokenJson);
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
            var userData = JsonSerializer.Deserialize<DiscordUserResponse>(userJson);
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
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            // Exchange code for access token
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", _configuration.GoogleClientId },
                { "client_secret", _configuration.GoogleClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", _configuration.GoogleRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(GOOGLE_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Google token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = JsonSerializer.Deserialize<GoogleTokenResponse>(tokenJson);
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
            var userData = JsonSerializer.Deserialize<GoogleUserResponse>(userJson);
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
        try
        {
            using var httpClient = _httpClientFactory.CreateClient();

            // Exchange code for access token
            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "client_id", _configuration.TwitchClientId },
                { "client_secret", _configuration.TwitchClientSecret },
                { "code", code },
                { "grant_type", "authorization_code" },
                { "redirect_uri", _configuration.TwitchRedirectUri }
            });

            var tokenResponse = await httpClient.PostAsync(TWITCH_TOKEN_URL, tokenRequest, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Twitch token exchange failed: {Status} - {Error}", tokenResponse.StatusCode, errorContent);
                return null;
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var tokenData = JsonSerializer.Deserialize<TwitchTokenResponse>(tokenJson);
            if (tokenData == null || string.IsNullOrEmpty(tokenData.AccessToken))
            {
                _logger.LogWarning("Twitch token response missing access_token");
                return null;
            }

            // Get user info
            using var userRequest = new HttpRequestMessage(HttpMethod.Get, TWITCH_USER_URL);
            userRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            userRequest.Headers.Add("Client-Id", _configuration.TwitchClientId);

            var userResponse = await httpClient.SendAsync(userRequest, cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                var errorContent = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Twitch user info request failed: {Status} - {Error}", userResponse.StatusCode, errorContent);
                return null;
            }

            var userJson = await userResponse.Content.ReadAsStringAsync(cancellationToken);
            var usersData = JsonSerializer.Deserialize<TwitchUsersResponse>(userJson);
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
            var steamResponse = JsonSerializer.Deserialize<SteamAuthResponse>(responseJson);

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

        try
        {
            // Check if we have an existing link for this OAuth identity
            var existingAccountId = await _daprClient.GetStateAsync<Guid?>(
                REDIS_STATE_STORE,
                oauthLinkKey,
                cancellationToken: cancellationToken);

            if (existingAccountId.HasValue && existingAccountId.Value != Guid.Empty)
            {
                // Found existing account link, retrieve the account
                try
                {
                    var account = await _accountsClient.GetAccountAsync(
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
                    await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, oauthLinkKey, cancellationToken: cancellationToken);
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
                newAccount = await _accountsClient.CreateAccountAsync(createRequest, cancellationToken);
            }
            catch (ApiException ex) when (ex.StatusCode == 409)
            {
                // Email already exists - try to find by email
                if (!string.IsNullOrEmpty(userInfo.Email))
                {
                    try
                    {
                        newAccount = await _accountsClient.GetAccountByEmailAsync(
                            new GetAccountByEmailRequest { Email = userInfo.Email },
                            cancellationToken);
                        _logger.LogInformation("Found existing account by email {Email} for {Provider} user",
                            userInfo.Email, providerName);
                    }
                    catch
                    {
                        _logger.LogError("Email conflict but couldn't find account: {Email}", userInfo.Email);
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
            await _daprClient.SaveStateAsync(
                REDIS_STATE_STORE,
                oauthLinkKey,
                newAccount.AccountId,
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

        var accessToken = await GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = GenerateRefreshToken();
        await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

        return (StatusCodes.OK, new AuthResponse
        {
            AccountId = account.AccountId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = new Uri(_configuration.ConnectUrl)
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

        var accessToken = await GenerateAccessTokenAsync(account, cancellationToken);
        var refreshToken = GenerateRefreshToken();
        await StoreRefreshTokenAsync(account.AccountId.ToString(), refreshToken, cancellationToken);

        return (StatusCodes.OK, new AuthResponse
        {
            AccountId = account.AccountId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = _configuration.JwtExpirationMinutes * 60,
            ConnectUrl = new Uri(_configuration.ConnectUrl)
        });
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Called when a Dapr event is received. Routes to appropriate event handlers.
    /// </summary>
    public async Task OnEventReceivedAsync<T>(string topic, T eventData) where T : class
    {
        _logger.LogInformation("[DIAG] AuthService received event {Topic} with data type {DataType}", topic, typeof(T).Name);

        // Diagnostic: log raw event data for debugging CI issues
        try
        {
            var eventJson = System.Text.Json.JsonSerializer.Serialize(eventData);
            _logger.LogInformation("[DIAG] Raw event data JSON: {EventJson}", eventJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DIAG] Failed to serialize event data for logging");
        }

        switch (topic)
        {
            case "account.deleted":
                if (eventData is AccountDeletedEvent deletedEvent)
                {
                    _logger.LogInformation("[DIAG] Cast to AccountDeletedEvent successful. EventId={EventId}, AccountId={AccountId}, AccountIdString={AccountIdString}",
                        deletedEvent.EventId, deletedEvent.AccountId, deletedEvent.AccountId.ToString());
                    await HandleAccountDeletedEventAsync(deletedEvent);
                }
                else
                {
                    _logger.LogWarning("[DIAG] Cast to AccountDeletedEvent FAILED. Actual type: {ActualType}", eventData?.GetType().FullName ?? "null");
                }
                break;
            default:
                _logger.LogWarning("AuthService received unknown event topic: {Topic}", topic);
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
            _logger.LogInformation("[DIAG] InvalidateAllSessionsForAccountAsync called with accountId={AccountId}, indexKey={IndexKey}",
                accountId, indexKey);

            var sessionKeys = await _daprClient.GetStateAsync<List<string>>(
                REDIS_STATE_STORE,
                indexKey,
                cancellationToken: CancellationToken.None);

            _logger.LogInformation("[DIAG] GetStateAsync for indexKey={IndexKey} returned: {SessionKeysJson}",
                indexKey, sessionKeys == null ? "null" : System.Text.Json.JsonSerializer.Serialize(sessionKeys));

            if (sessionKeys == null || !sessionKeys.Any())
            {
                _logger.LogInformation("[DIAG] No sessions found for account {AccountId} with indexKey={IndexKey} - EARLY RETURN", accountId, indexKey);
                return;
            }

            // Remove each session from Redis
            foreach (var sessionKey in sessionKeys)
            {
                try
                {
                    // Delete the session data
                    await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, $"session:{sessionKey}");
                    _logger.LogDebug("Deleted session {SessionKey} for account {AccountId}", sessionKey, accountId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete session {SessionKey} for account {AccountId}", sessionKey, accountId);
                    // Continue with other sessions even if one fails
                }
            }

            // Remove the account-to-sessions index
            await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, indexKey);

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

            await _daprClient.PublishEventAsync(PUBSUB_NAME, SESSION_INVALIDATED_TOPIC, eventModel);
            _logger.LogInformation("Published SessionInvalidatedEvent for account {AccountId}: {SessionCount} sessions, reason: {Reason}",
                accountId, sessionIds.Count, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish SessionInvalidatedEvent for account {AccountId}", accountId);
            // Don't throw - session invalidation succeeded, event publishing failure shouldn't fail the operation
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Overrides the default IDaprService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Auth service permissions...");
        await AuthPermissionRegistration.RegisterViaEventAsync(_daprClient, _logger);
    }

    #endregion
}
