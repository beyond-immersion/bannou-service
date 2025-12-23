using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Subscriptions;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Implementation of token management operations.
/// Handles JWT generation, validation, and refresh token management.
/// </summary>
public class TokenService : ITokenService
{
    private readonly DaprClient _daprClient;
    private readonly ISubscriptionsClient _subscriptionsClient;
    private readonly ISessionService _sessionService;
    private readonly AuthServiceConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;
    private const string REDIS_STATE_STORE = "auth-statestore";

    /// <summary>
    /// Initializes a new instance of TokenService.
    /// </summary>
    public TokenService(
        DaprClient daprClient,
        ISubscriptionsClient subscriptionsClient,
        ISessionService sessionService,
        AuthServiceConfiguration configuration,
        ILogger<TokenService> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _subscriptionsClient = subscriptionsClient ?? throw new ArgumentNullException(nameof(subscriptionsClient));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string> GenerateAccessTokenAsync(AccountResponse account, CancellationToken cancellationToken = default)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        if (string.IsNullOrWhiteSpace(_configuration.JwtSecret))
            throw new InvalidOperationException("JWT secret is not configured");

        if (string.IsNullOrWhiteSpace(_configuration.JwtIssuer))
            throw new InvalidOperationException("JWT issuer is not configured");

        if (string.IsNullOrWhiteSpace(_configuration.JwtAudience))
            throw new InvalidOperationException("JWT audience is not configured");

        _logger.LogDebug("Generating access token for account {AccountId}", account.AccountId);

        // Generate opaque session key for JWT Redis key security
        var sessionKey = Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid().ToString();

        // Fetch current subscriptions/authorizations for the account
        var authorizations = new List<string>();
        try
        {
            var subscriptionsResponse = await _subscriptionsClient.GetCurrentSubscriptionsAsync(
                new GetCurrentSubscriptionsRequest { AccountId = account.AccountId },
                cancellationToken);

            if (subscriptionsResponse?.Authorizations != null)
            {
                authorizations = subscriptionsResponse.Authorizations.ToList();
                _logger.LogDebug("Fetched {Count} authorizations for account {AccountId}",
                    authorizations.Count, account.AccountId);
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No subscriptions found for account {AccountId}", account.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch subscriptions for account {AccountId} - login rejected", account.AccountId);
            throw;
        }

        // Store session data in Redis
        var sessionData = new SessionDataModel
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName ?? string.Empty,
            Roles = account.Roles?.ToList() ?? new List<string>(),
            Authorizations = authorizations,
            SessionId = sessionId,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_configuration.JwtExpirationMinutes)
        };

        await _sessionService.SaveSessionAsync(sessionKey, sessionData, _configuration.JwtExpirationMinutes * 60, cancellationToken);

        // Maintain indexes
        await _sessionService.AddSessionToAccountIndexAsync(account.AccountId.ToString(), sessionKey, cancellationToken);
        await _sessionService.AddSessionIdReverseIndexAsync(sessionId, sessionKey, _configuration.JwtExpirationMinutes * 60, cancellationToken);

        // Generate JWT
        var key = Encoding.UTF8.GetBytes(_configuration.JwtSecret);
        var tokenHandler = new JwtSecurityTokenHandler();

        var claims = new List<Claim>
        {
            new Claim("session_key", sessionKey),
            new Claim(ClaimTypes.NameIdentifier, account.AccountId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, account.AccountId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64)
        };

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
        return tokenHandler.WriteToken(jwt);
    }

    /// <inheritdoc/>
    public string GenerateRefreshToken()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <inheritdoc/>
    public async Task StoreRefreshTokenAsync(string accountId, string refreshToken, CancellationToken cancellationToken = default)
    {
        var redisKey = $"refresh_token:{refreshToken}";
        await _daprClient.SaveStateAsync(
            REDIS_STATE_STORE,
            redisKey,
            accountId,
            metadata: new Dictionary<string, string> { { "ttl", "604800" } }, // 7 days
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string?> ValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            return await _daprClient.GetStateAsync<string>(REDIS_STATE_STORE, redisKey, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate refresh token");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task RemoveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
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

    /// <inheritdoc/>
    public async Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(string jwt, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jwt))
            {
                _logger.LogWarning("JWT token is null or empty");
                return (StatusCodes.Unauthorized, null);
            }

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

                var principal = tokenHandler.ValidateToken(jwt, tokenValidationParameters, out _);

                var sessionKeyClaim = principal.FindFirst("session_key");
                if (sessionKeyClaim == null)
                {
                    _logger.LogWarning("JWT token does not contain session_key claim");
                    return (StatusCodes.Unauthorized, null);
                }

                var sessionKey = sessionKeyClaim.Value;
                var sessionData = await _sessionService.GetSessionAsync(sessionKey, cancellationToken);

                if (sessionData == null)
                {
                    _logger.LogWarning("Session not found for key: {SessionKey}", sessionKey);
                    return (StatusCodes.Unauthorized, null);
                }

                if (sessionData.ExpiresAt < DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("Session expired for key: {SessionKey}", sessionKey);
                    return (StatusCodes.Unauthorized, null);
                }

                return (StatusCodes.OK, new ValidateTokenResponse
                {
                    Valid = true,
                    AccountId = sessionData.AccountId,
                    SessionId = sessionKey,
                    Roles = sessionData.Roles ?? new List<string>(),
                    Authorizations = sessionData.Authorizations ?? new List<string>(),
                    RemainingTime = (int)(sessionData.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds
                });
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogWarning(ex, "JWT token validation failed");
                return (StatusCodes.Unauthorized, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public Task<string?> ExtractSessionKeyFromJwtAsync(string jwt)
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

    /// <inheritdoc/>
    public string GenerateSecureToken()
    {
        var tokenBytes = new byte[32]; // 256 bits
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
