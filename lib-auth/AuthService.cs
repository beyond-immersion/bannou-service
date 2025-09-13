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
            var accessToken = GenerateAccessToken(account);
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
            var accessToken = GenerateAccessToken(accountResult);
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
    public Task<(StatusCodes, AuthResponse?)> CompleteOAuthAsync(
        Provider2 provider,
        OAuthCallbackRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing OAuth callback for provider: {Provider}", provider);

            if (string.IsNullOrWhiteSpace(body.Code))
            {
                return Task.FromResult<(StatusCodes, AuthResponse?)>((StatusCodes.BadRequest, null));
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

            var accessToken = GenerateAccessToken(mockAccount);
            var refreshToken = GenerateRefreshToken();

            return Task.FromResult<(StatusCodes, AuthResponse?)>((StatusCodes.OK, new AuthResponse
            {
                AccountId = mockAccount.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = JWT_EXPIRATION_MINUTES * 60
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth callback for provider: {Provider}", provider);
            return Task.FromResult<(StatusCodes, AuthResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <inheritdoc/>
    public Task<(StatusCodes, AuthResponse?)> VerifySteamAuthAsync(
        SteamVerifyRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing Steam authentication verification");

            if (string.IsNullOrWhiteSpace(body.Ticket))
            {
                return Task.FromResult<(StatusCodes, AuthResponse?)>((StatusCodes.BadRequest, null));
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

            var accessToken = GenerateAccessToken(mockAccount);
            var refreshToken = GenerateRefreshToken();

            return Task.FromResult<(StatusCodes, AuthResponse?)>((StatusCodes.OK, new AuthResponse
            {
                AccountId = mockAccount.AccountId,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresIn = JWT_EXPIRATION_MINUTES * 60
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Steam authentication verification");
            return Task.FromResult<(StatusCodes, AuthResponse?)>((StatusCodes.InternalServerError, null));
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
            var accessToken = GenerateAccessToken(account);
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
    public Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // TODO: Extract JWT token from HTTP context/headers
            // For now, return mock validation
            _logger.LogInformation("Token validation requested");

            return Task.FromResult<(StatusCodes, ValidateTokenResponse?)>((StatusCodes.OK, new ValidateTokenResponse
            {
                Valid = true,
                AccountId = Guid.NewGuid(),
                RemainingTime = JWT_EXPIRATION_MINUTES * 60
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
            return Task.FromResult<(StatusCodes, ValidateTokenResponse?)>((StatusCodes.InternalServerError, null));
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

    private string GenerateAccessToken(AccountResponse account)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(JWT_SECRET);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, account.AccountId.ToString()),
            new Claim(ClaimTypes.Email, account.Email ?? ""),
            new Claim(ClaimTypes.Name, account.DisplayName ?? "")
        };

        // Add roles
        if (account.Roles != null)
        {
            foreach (var role in account.Roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

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
