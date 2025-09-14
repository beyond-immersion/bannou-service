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
public class AuthService : IAuthService
{
    private readonly IAccountsClient _accountsClient;
    private readonly DaprClient _daprClient;
    private readonly ILogger<AuthService> _logger;
    private readonly AuthServiceConfiguration _configuration;
    private const string REDIS_STATE_STORE = "bannou-redis-store";

    // Hardcoded configuration for now - can be moved to config later
    private const string JWT_SECRET = "your-256-bit-secret-key-here-must-be-32-chars";
    private const string JWT_ISSUER = "bannou-auth-service";
    private const string JWT_AUDIENCE = "bannou-clients";
    private const int JWT_EXPIRATION_MINUTES = 60;

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
                ExpiresIn = JWT_EXPIRATION_MINUTES * 60
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
                ExpiresIn = JWT_EXPIRATION_MINUTES * 60
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
                ExpiresIn = JWT_EXPIRATION_MINUTES * 60
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
                ExpiresIn = JWT_EXPIRATION_MINUTES * 60
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
        LogoutRequest? body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing logout request");

            // TODO: Invalidate session based on user context
            // For now, return success

            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, new { Message = "Logout successful" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <inheritdoc/>
    public Task<(StatusCodes, object?)> TerminateSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Terminating session: {SessionId}", sessionId);

            // TODO: Remove session from Redis
            // For now, return success

            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.OK, new { Message = "Session terminated successfully" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error terminating session: {SessionId}", sessionId);
            return Task.FromResult<(StatusCodes, object?)>((StatusCodes.InternalServerError, null));
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            // TODO: Get sessions for authenticated user from Redis
            // For now, return mock sessions
            _logger.LogInformation("Sessions requested");

            return Task.FromResult<(StatusCodes, SessionsResponse?)>((StatusCodes.OK, new SessionsResponse
            {
                Sessions = new List<SessionInfo>
                {
                    new SessionInfo
                    {
                        SessionId = Guid.NewGuid(),
                        CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
                        LastActive = DateTimeOffset.UtcNow.AddMinutes(-5),
                        DeviceInfo = new DeviceInfo()
                    }
                }
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sessions");
            return Task.FromResult<(StatusCodes, SessionsResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    #region Private Helper Methods

    private string HashPassword(string password)
    {
        // Simplified password hashing - use BCrypt in production
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(password + JWT_SECRET));
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
            ExpiresAt = DateTime.UtcNow.AddMinutes(JWT_EXPIRATION_MINUTES)
        };

        await _daprClient.SaveStateAsync(
            REDIS_STATE_STORE,
            $"session:{sessionKey}",
            sessionData,
            metadata: new Dictionary<string, string> { { "ttl", (JWT_EXPIRATION_MINUTES * 60).ToString() } },
            cancellationToken: cancellationToken);

        // JWT contains only opaque session key - no sensitive data
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(JWT_SECRET);
        var claims = new List<Claim>
        {
            new Claim("session_key", sessionKey), // Opaque key for Redis lookup
            new Claim("sub", account.AccountId.ToString()), // Standard subject claim
            new Claim("jti", Guid.NewGuid().ToString()) // JWT ID for tracking
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(JWT_EXPIRATION_MINUTES),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
            Issuer = JWT_ISSUER,
            Audience = JWT_AUDIENCE
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

    #endregion
}
