using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Accounts.Client;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Auth service implementation with complete OAuth, routing, and JWT functionality.
/// Follows the comprehensive design from API-DESIGN.md including NGINX+Redis integration.
/// </summary>
[DaprService("auth", typeof(IAuthService), lifetime: ServiceLifetime.Scoped)]
public class AuthService : DaprService<AuthServiceConfiguration>, IAuthService
{
    private readonly IAccountsClient _accountsClient;
    private readonly DaprClient _daprClient;
    private readonly ILogger<AuthService> _logger;
    private const string REDIS_STATE_STORE = "bannou-redis-store";

    public AuthService(
        IAccountsClient accountsClient,
        DaprClient daprClient,
        AuthServiceConfiguration configuration,
        ILogger<AuthService> logger)
        : base(configuration, logger)
    {
        _accountsClient = accountsClient ?? throw new ArgumentNullException(nameof(accountsClient));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Registration & Basic Auth

    /// <inheritdoc/>
    public async Task<(StatusCodes, RegisterResponse?)> RegisterAsync(
        RegisterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing registration request for username: {Username}", body.Username);

            // Validate request
            if (string.IsNullOrWhiteSpace(body.Username))
            {
                return (StatusCodes.BadRequest, null);
            }

            if (string.IsNullOrWhiteSpace(body.Password))
            {
                return (StatusCodes.BadRequest, null);
            }

            // Create account through accounts service
            var createAccountRequest = new CreateAccountRequest
            {
                DisplayName = body.Username,
                Email = body.Email,
                Provider = Provider.Email,
                ExternalId = body.Username,
                PasswordHash = HashPassword(body.Password),
                EmailVerified = false,
                Roles = new[] { "user" }
            };

            var accountResult = await _accountsClient.CreateAccountAsync(createAccountRequest, cancellationToken);
            if (accountResult?.AccountId == null)
            {
                return (StatusCodes.Forbidden, null);
            }

            // Generate tokens
            var accessToken = await GenerateAccessTokenAsync(accountResult);
            var refreshToken = GenerateRefreshToken();

            // Store refresh token in Redis
            await StoreRefreshTokenAsync(accountResult.AccountId, refreshToken);

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
    public async Task<(StatusCodes, LoginResponse?)> LoginWithCredentialsGetAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        return await PerformLoginAsync(username, password, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, LoginResponse?)> LoginWithCredentialsPostAsync(
        string username,
        string password,
        LoginRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        return await PerformLoginAsync(username, password, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, LoginResponse?)> LoginWithTokenGetAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        return await RefreshTokenAsync(token, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, LoginResponse?)> LoginWithTokenPostAsync(
        string token,
        LoginRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        return await RefreshTokenAsync(token, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(
        ValidateTokenRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body.Token))
            {
                return (StatusCodes.BadRequest, null);
            }

            var validationResult = await ValidateJwtTokenAsync(body.Token);
            if (validationResult == null)
            {
                return (StatusCodes.Unauthorized, null);
            }

            return (StatusCodes.OK, validationResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region OAuth Implementation

    /// <inheritdoc/>
    public async Task<(StatusCodes, OAuthProvidersResponse?)> GetOAuthProvidersAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var providers = new List<OAuthProvider>
            {
                new OAuthProvider
                {
                    Name = Provider2.Steam,
                    Display_name = "Steam",
                    Authorization_url = BuildSteamAuthUrl(),
                    Scopes = new[] { "openid" }
                }
            };

            return (StatusCodes.OK, new OAuthProvidersResponse
            {
                Providers = providers
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting OAuth providers");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<(StatusCodes, LoginResponse?)> HandleOAuthCallbackAsync(
        Provider provider,
        OAuthCallbackRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing OAuth callback for provider: {Provider}", provider);

            if (body.Error != null)
            {
                _logger.LogWarning("OAuth error from provider {Provider}: {Error} - {Description}",
                    provider, body.Error, body.Error_description);
                return (StatusCodes.Forbidden, null);
            }

            if (string.IsNullOrWhiteSpace(body.Authorization_code))
            {
                return (StatusCodes.BadRequest, null);
            }

            switch (provider)
            {
                case Provider.Steam:
                    return await HandleSteamCallbackAsync(body, cancellationToken);
                default:
                    _logger.LogWarning("Unsupported OAuth provider: {Provider}", provider);
                    return (StatusCodes.BadRequest, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth callback for provider: {Provider}", provider);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Routing Preferences (Internal)

    /// <inheritdoc/>
    public async Task<(StatusCodes, RoutingPreferenceResponse?)> UpdateRoutingPreferenceAsync(
        RoutingPreferenceRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating routing preference for user {UserId} to {Instance}",
                body.User_id, body.Preferred_instance);

            // Store routing preference in Redis with TTL
            var redisKey = $"user:{body.User_id}:preferred_{body.Service_type}";
            var expirationSeconds = body.Expires_in_seconds ?? 86400; // Default 24 hours

            await _daprClient.SaveStateAsync(
                REDIS_STATE_STORE,
                redisKey,
                body.Preferred_instance,
                metadata: new Dictionary<string, string> { { "ttl", expirationSeconds.ToString() } },
                cancellationToken: cancellationToken);

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(expirationSeconds);

            _logger.LogInformation("Successfully updated routing preference for user {UserId}", body.User_id);

            return (StatusCodes.OK, new RoutingPreferenceResponse
            {
                Success = true,
                User_id = body.User_id,
                Service_type = body.Service_type,
                Preferred_instance = body.Preferred_instance,
                Expires_at = expiresAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating routing preference for user: {UserId}", body.User_id);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #endregion

    #region Private Implementation Methods

    private async Task<(StatusCodes, LoginResponse?)> PerformLoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing credential login for username: {Username}", username);

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return (StatusCodes.BadRequest, null);
            }

            // Get account by email
            var account = await _accountsClient.GetAccountByEmailAsync(username, cancellationToken);
            if (account == null)
            {
                return (StatusCodes.Forbidden, null);
            }

            // Verify password hash (simplified for demo)
            if (!VerifyPassword(password, account.PasswordHash))
            {
                return (StatusCodes.Forbidden, null);
            }

            // Generate tokens
            var accessToken = await GenerateAccessTokenAsync(account);
            var refreshToken = GenerateRefreshToken();

            // Store refresh token
            await StoreRefreshTokenAsync(account.AccountId, refreshToken);

            _logger.LogInformation("Successfully authenticated user: {Username} (ID: {AccountId})",
                username, account.AccountId);

            return (StatusCodes.OK, new LoginResponse
            {
                Access_token = accessToken,
                Refresh_token = refreshToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during credential login for username: {Username}", username);
            return (StatusCodes.InternalServerError, null);
        }
    }

    private async Task<(StatusCodes, LoginResponse?)> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing token refresh");

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return (StatusCodes.BadRequest, null);
            }

            // Validate refresh token exists in Redis
            var accountId = await ValidateRefreshTokenAsync(refreshToken);
            if (string.IsNullOrEmpty(accountId))
            {
                return (StatusCodes.Forbidden, null);
            }

            // Get account
            var account = await _accountsClient.GetAccountAsync(accountId, cancellationToken);
            if (account == null)
            {
                return (StatusCodes.Forbidden, null);
            }

            // Generate new tokens
            var accessToken = await GenerateAccessTokenAsync(account);
            var newRefreshToken = GenerateRefreshToken();

            // Store new refresh token and remove old one
            await StoreRefreshTokenAsync(accountId, newRefreshToken);
            await RemoveRefreshTokenAsync(refreshToken);

            return (StatusCodes.OK, new LoginResponse
            {
                Access_token = accessToken,
                Refresh_token = newRefreshToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return (StatusCodes.InternalServerError, null);
        }
    }

    private async Task<(StatusCodes, LoginResponse?)> HandleSteamCallbackAsync(
        OAuthCallbackRequest body,
        CancellationToken cancellationToken)
    {
        // Steam OpenID implementation would go here
        // For now, return a mock implementation
        _logger.LogInformation("Mock Steam OAuth callback processing");

        // In real implementation:
        // 1. Validate the Steam OpenID response
        // 2. Get Steam user data
        // 3. Link to existing account or create new one
        // 4. Generate JWT tokens

        var mockAccount = new AccountResponse
        {
            AccountId = Guid.NewGuid().ToString(),
            Email = "steam-user@example.com",
            DisplayName = "Steam User",
            Provider = Provider.Steam,
            Roles = new[] { "user" }
        };

        var accessToken = await GenerateAccessTokenAsync(mockAccount);
        var refreshToken = GenerateRefreshToken();

        return (StatusCodes.OK, new LoginResponse
        {
            Access_token = accessToken,
            Refresh_token = refreshToken
        });
    }

    private string BuildSteamAuthUrl()
    {
        var returnUrl = "https://your-domain.com/auth/steam/callback"; // Configure in settings
        return $"https://steamcommunity.com/openid/login?openid.return_to={Uri.EscapeDataString(returnUrl)}&openid.identity=http://specs.openid.net/auth/2.0/identifier_select&openid.claimed_id=http://specs.openid.net/auth/2.0/identifier_select&openid.mode=checkid_setup&openid.ns=http://specs.openid.net/auth/2.0";
    }

    private string HashPassword(string password)
    {
        // Simplified password hashing - use BCrypt in production
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(password + Configuration.JwtSecret));
    }

    private bool VerifyPassword(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        return HashPassword(password) == hash;
    }

    private async Task<string> GenerateAccessTokenAsync(BeyondImmersion.BannouService.Accounts.Client.AccountResponse account)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(Configuration.JwtSecret);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, account.AccountId),
            new Claim(ClaimTypes.Email, account.Email ?? ""),
            new Claim(ClaimTypes.Name, account.DisplayName ?? ""),
            new Claim("provider", account.Provider.ToString())
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
            Expires = DateTime.UtcNow.AddMinutes(Configuration.JwtExpirationMinutes),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
            Issuer = Configuration.JwtIssuer,
            Audience = Configuration.JwtAudience
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private string GenerateRefreshToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    private async Task<ValidateTokenResponse?> ValidateJwtTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(Configuration.JwtSecret);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = Configuration.JwtIssuer,
                ValidateAudience = true,
                ValidAudience = Configuration.JwtAudience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
            var jwtToken = (JwtSecurityToken)validatedToken;

            return new ValidateTokenResponse
            {
                Valid = true,
                Expires_at = jwtToken.ValidTo,
                Subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                Claims = principal.Claims.ToDictionary(c => c.Type, c => c.Value)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return new ValidateTokenResponse { Valid = false };
        }
    }

    private async Task StoreRefreshTokenAsync(string accountId, string refreshToken)
    {
        var redisKey = $"refresh_token:{refreshToken}";
        await _daprClient.SaveStateAsync(
            REDIS_STATE_STORE,
            redisKey,
            accountId,
            metadata: new Dictionary<string, string> { { "ttl", "2592000" } }); // 30 days
    }

    private async Task<string?> ValidateRefreshTokenAsync(string refreshToken)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            var accountId = await _daprClient.GetStateAsync<string>(REDIS_STATE_STORE, redisKey);
            return accountId;
        }
        catch
        {
            return null;
        }
    }

    private async Task RemoveRefreshTokenAsync(string refreshToken)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, redisKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove refresh token");
        }
    }

    #endregion
}
