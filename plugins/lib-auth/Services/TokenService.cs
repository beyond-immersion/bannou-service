using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.Subscription;
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
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ISubscriptionClient _subscriptionClient;
    private readonly ISessionService _sessionService;
    private readonly AuthServiceConfiguration _configuration;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<TokenService> _logger;
    private const string REDIS_STATE_STORE = "auth-statestore";

    /// <summary>
    /// Initializes a new instance of TokenService.
    /// </summary>
    public TokenService(
        IStateStoreFactory stateStoreFactory,
        ISubscriptionClient subscriptionClient,
        ISessionService sessionService,
        AuthServiceConfiguration configuration,
        IMessageBus messageBus,
        ILogger<TokenService> logger)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _subscriptionClient = subscriptionClient ?? throw new ArgumentNullException(nameof(subscriptionClient));
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string> GenerateAccessTokenAsync(AccountResponse account, CancellationToken cancellationToken = default)
    {
        if (account == null)
            throw new ArgumentNullException(nameof(account));

        // Use core app configuration for JWT settings (validated at startup in Program.cs)
        var jwtConfig = Program.Configuration;

        _logger.LogDebug("Generating access token for account {AccountId}", account.AccountId);

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
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "GenerateAccessToken",
                ex.GetType().Name,
                ex.Message,
                dependency: "subscription",
                endpoint: "post:/auth/login",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            throw;
        }

        // Store session data in Redis
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

        await _sessionService.SaveSessionAsync(sessionKey, sessionData, _configuration.JwtExpirationMinutes * 60, cancellationToken);

        // Maintain indexes
        await _sessionService.AddSessionToAccountIndexAsync(account.AccountId.ToString(), sessionKey, cancellationToken);
        await _sessionService.AddSessionIdReverseIndexAsync(sessionId, sessionKey, _configuration.JwtExpirationMinutes * 60, cancellationToken);

        // Generate JWT using core app configuration
        var key = Encoding.UTF8.GetBytes(jwtConfig.JwtSecret!);
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
            Issuer = jwtConfig.JwtIssuer,
            Audience = jwtConfig.JwtAudience
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
        var stringStore = _stateStoreFactory.GetStore<string>(REDIS_STATE_STORE);
        await stringStore.SaveAsync(
            redisKey,
            accountId,
            new StateOptions { Ttl = (int)TimeSpan.FromDays(7).TotalSeconds }, // 7 days
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string?> ValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            var stringStore = _stateStoreFactory.GetStore<string>(REDIS_STATE_STORE);
            return await stringStore.GetAsync(redisKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate refresh token");
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "ValidateRefreshToken",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                endpoint: "post:/auth/refresh",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task RemoveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            var stringStore = _stateStoreFactory.GetStore<string>(REDIS_STATE_STORE);
            await stringStore.DeleteAsync(redisKey, cancellationToken);
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
            // Use core app configuration for JWT settings (validated at startup in Program.cs)
            var jwtConfig = Program.Configuration;
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
                    SessionId = Guid.Parse(sessionKey),
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
            catch (ArgumentException ex)
            {
                // ArgumentException is thrown for malformed JWT tokens (e.g., invalid format)
                _logger.LogWarning(ex, "JWT token is malformed");
                return (StatusCodes.Unauthorized, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "ValidateToken",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                endpoint: "post:/auth/validate",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> ExtractSessionKeyFromJwtAsync(string jwt)
    {
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
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "ExtractSessionKeyFromJwt",
                ex.GetType().Name,
                ex.Message,
                endpoint: "post:/auth/validate");
            return null;
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
