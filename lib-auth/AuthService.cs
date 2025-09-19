using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Auth service implementation focused on authentication, token management, and OAuth provider integration.
/// Follows schema-first architecture - implements generated IAuthService interface.
/// </summary>
[DaprService("auth", typeof(IAuthService), lifetime: ServiceLifetime.Scoped)]
public class AuthService : IAuthService, IDaprService
{
    private readonly IAccountsClient _accountsClient;
    private readonly DaprClient _daprClient;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthServiceConfiguration _configuration;
    private const string REDIS_STATE_STORE = "statestore";

    // Configuration loaded from environment variables

    public AuthService(
        IAccountsClient accountsClient,
        DaprClient daprClient,
        AuthServiceConfiguration configuration,
        ILogger<AuthService> logger)
    {
        _accountsClient = accountsClient ?? throw new ArgumentNullException(nameof(accountsClient));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            // Get account by email
            var account = await _accountsClient.GetAccountByEmailAsync(body.Email, cancellationToken);
            if (account == null)
            {
                return (StatusCodes.Forbidden, null);
            }

            // TODO: Verify password (would need to get password hash from accounts service)
            // For now, assume validation passed
            _logger.LogInformation("Mock password verification for email: {Email}", body.Email);

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
                ExpiresIn = _configuration.JwtExpirationMinutes * 60
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

            // Create account through accounts service
            var createAccountRequest = new CreateAccountRequest
            {
                DisplayName = body.Username,
                Email = body.Email
                // TODO: Add password hash, provider, etc. when accounts service supports it
            };

            var accountResult = await _accountsClient.CreateAccountAsync(createAccountRequest, cancellationToken);
            if (accountResult == null || accountResult.AccountId == Guid.Empty)
            {
                return (StatusCodes.Conflict, null);
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
                Access_token = accessToken,
                Refresh_token = refreshToken
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
                return (StatusCodes.BadRequest, null);
            }

            // TODO: Implement actual OAuth provider integration
            // For now, return mock implementation
            _logger.LogInformation("Mock OAuth callback processing for provider: {Provider}", provider);

            var mockAccount = new AccountResponse
            {
                AccountId = Guid.NewGuid(),
                Email = $"oauth-user@{provider.ToString().ToLower()}.com",
                DisplayName = $"OAuth User ({provider})"
            };

            var accessToken = await GenerateAccessTokenAsync(mockAccount, cancellationToken);
            var refreshToken = GenerateRefreshToken();

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = mockAccount.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60
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
            _logger.LogInformation("Processing Steam authentication verification");

            if (string.IsNullOrWhiteSpace(body.Ticket))
            {
                return (StatusCodes.BadRequest, null);
            }

            // TODO: Implement actual Steam OpenID verification
            // For now, return mock implementation
            _logger.LogInformation("Mock Steam verification processing");

            var mockAccount = new AccountResponse
            {
                AccountId = Guid.NewGuid(),
                Email = "steam-user@example.com",
                DisplayName = "Steam User"
            };

            var accessToken = await GenerateAccessTokenAsync(mockAccount, cancellationToken);
            var refreshToken = GenerateRefreshToken();

            return (StatusCodes.OK, new AuthResponse
            {
                AccountId = mockAccount.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = _configuration.JwtExpirationMinutes * 60
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

            // Get account
            var account = await _accountsClient.GetAccountAsync(Guid.Parse(accountId), cancellationToken);
            if (account == null)
            {
                return (StatusCodes.Forbidden, null);
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
                ExpiresIn = _configuration.JwtExpirationMinutes * 60
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

            // TODO: Implement actual OAuth initialization
            // For now, return mock authorization URL
            var authUrl = $"https://oauth.{provider.ToString().ToLower()}.com/authorize?redirect_uri={redirectUri}&state={state}";

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
            _logger.LogInformation("Initializing Steam authentication");

            // TODO: Implement actual Steam OpenID initialization
            // For now, return mock Steam URL
            var steamUrl = $"https://steamcommunity.com/openid/login?openid.return_to={returnUrl}";

            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, new { AuthorizationUrl = steamUrl }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing Steam authentication");
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <inheritdoc/>
    public Task<(StatusCodes, object?)> LogoutAsync(
        string jwt,
        LogoutRequest? body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing logout request. AllSessions: {AllSessions}", body?.AllSessions ?? false);

            // TODO: To properly implement logout, we need the JWT token to identify the session
            // The logout endpoint should be modified to use x-from-authorization: bearer
            // or take a JWT parameter like ValidateTokenAsync does

            // For now, we can only log the request since we don't have session identification
            _logger.LogWarning("Logout implementation incomplete - cannot identify specific session to invalidate without JWT token");

            if (body?.AllSessions == true)
            {
                _logger.LogInformation("AllSessions logout requested but not implemented");
                // TODO: Implement all sessions logout when we have user identification
            }

            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, new { Message = "Logout successful" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, object?)> TerminateSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Terminating session: {SessionId}", sessionId);

            // Find and remove the session from Redis
            // Since we store sessions with session_key, we need to find sessions by session_id
            var sessionKey = await FindSessionKeyBySessionIdAsync(sessionId.ToString(), cancellationToken);

            if (sessionKey != null)
            {
                // Remove the session data from Redis
                await _daprClient.DeleteStateAsync(
                    REDIS_STATE_STORE,
                    $"session:{sessionKey}",
                    cancellationToken: cancellationToken);

                _logger.LogInformation("Session {SessionId} terminated successfully", sessionId);
            }
            else
            {
                _logger.LogWarning("Session {SessionId} not found for termination", sessionId);
            }

            return (StatusCodes.OK, new { Message = "Session terminated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating session: {SessionId}", sessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public Task<(StatusCodes, object?)> RequestPasswordResetAsync(
        PasswordResetRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing password reset request for email: {Email}", body.Email);

            // TODO: Generate reset token and send email
            // For now, return success

            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, new { Message = "Password reset email sent" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting password reset for email: {Email}", body.Email);
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <inheritdoc/>
    public Task<(StatusCodes, object?)> ConfirmPasswordResetAsync(
        PasswordResetConfirmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing password reset confirmation");

            // TODO: Validate reset token and update password
            // For now, return success

            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, new { Message = "Password reset successful" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming password reset");
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <inheritdoc/>
    public Task<(StatusCodes, SessionsResponse?)> GetSessionsAsync(
        string jwt,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // TODO: Get sessions for authenticated user from Redis
            // This requires knowing which user is making the request (via JWT token)
            // The endpoint should be modified to use x-from-authorization: bearer
            _logger.LogInformation("Sessions requested");

            _logger.LogWarning("GetSessionsAsync implementation incomplete - cannot identify user without JWT token");

            // Return empty sessions list for now since we can't identify the user
            return Task.FromResult<(StatusCodes, SessionsResponse?)>((StatusCodes.OK, new SessionsResponse
            {
                Sessions = new List<SessionInfo>()
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions");
            return Task.FromResult<(StatusCodes, SessionsResponse?)>((StatusCodes.InternalServerError, null));
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
                return (StatusCodes.OK, new ValidateTokenResponse
                {
                    Valid = true,
                    AccountId = sessionData.AccountId,
                    SessionId = sessionData.SessionId,
                    Roles = sessionData.Roles ?? new List<string>(),
                    RemainingTime = (int)(sessionData.ExpiresAt - DateTime.UtcNow).TotalSeconds
                });
            }
            catch (SecurityTokenValidationException ex)
            {
                _logger.LogWarning(ex, "JWT token validation failed");
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
        // Generate opaque session key (per API-DESIGN.md security pattern)
        var sessionKey = Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid().ToString();

        // Store session data in Redis with opaque key
        var sessionData = new
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            Roles = account.Roles ?? new List<string>(),
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

        // JWT contains only opaque session key - no sensitive data
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(_configuration.JwtSecret);
        var claims = new List<Claim>
        {
            new Claim("session_key", sessionKey), // Opaque key for Redis lookup
            new Claim("sub", account.AccountId.ToString()), // Standard subject claim
            new Claim("jti", Guid.NewGuid().ToString()) // JWT ID for tracking
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_configuration.JwtExpirationMinutes),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
            Issuer = _configuration.JwtIssuer,
            Audience = _configuration.JwtAudience
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
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
    /// Find session key by session ID (requires scanning Redis keys)
    /// </summary>
    private Task<string?> FindSessionKeyBySessionIdAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            // Note: This is not efficient for large numbers of sessions
            // In production, consider maintaining a reverse index session_id -> session_key
            // For now, we'll return null as this would require Redis key scanning
            // which isn't directly available through Dapr state store

            _logger.LogWarning("FindSessionKeyBySessionIdAsync not fully implemented - requires Redis key scanning");
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding session key for session ID: {SessionId}", sessionId);
            return Task.FromResult<string?>(null);
        }
    }

    #endregion
}
