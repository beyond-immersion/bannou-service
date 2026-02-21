using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
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
    private readonly ISessionService _sessionService;
    private readonly AuthServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<TokenService> _logger;

    /// <summary>
    /// Initializes a new instance of TokenService.
    /// </summary>
    public TokenService(
        IStateStoreFactory stateStoreFactory,
        ISessionService sessionService,
        AuthServiceConfiguration configuration,
        AppConfiguration appConfiguration,
        IMessageBus messageBus,
        ILogger<TokenService> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _sessionService = sessionService;
        _configuration = configuration;
        _appConfiguration = appConfiguration;
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<(string accessToken, Guid sessionId)> GenerateAccessTokenAsync(AccountResponse account, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Generating access token for account {AccountId}", account.AccountId);

        // Generate opaque session key for JWT Redis key security
        var sessionKey = Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid();

        // Authorizations are empty at session creation - subscription state is handled
        // by downstream services that subscribe to subscription.updated events directly
        var authorizations = new List<string>();

        // Generate JTI for edge revocation tracking (used below in claims)
        var jti = Guid.NewGuid().ToString();

        // Store session data in Redis
        var now = DateTimeOffset.UtcNow;
        var sessionData = new SessionDataModel
        {
            AccountId = account.AccountId,
            Email = account.Email,
            DisplayName = account.DisplayName,
            Roles = account.Roles?.ToList() ?? new List<string>(),
            Authorizations = authorizations,
            SessionId = sessionId,
            Jti = jti,
            CreatedAt = now,
            LastActiveAt = now,
            ExpiresAt = now.AddMinutes(_configuration.JwtExpirationMinutes)
        };

        await _sessionService.SaveSessionAsync(sessionKey, sessionData, _configuration.JwtExpirationMinutes * 60, cancellationToken);

        // Maintain indexes
        await _sessionService.AddSessionToAccountIndexAsync(account.AccountId, sessionKey, cancellationToken);
        await _sessionService.AddSessionIdReverseIndexAsync(sessionId, sessionKey, _configuration.JwtExpirationMinutes * 60, cancellationToken);

        if (string.IsNullOrWhiteSpace(_appConfiguration.JwtSecret))
        {
            throw new InvalidOperationException("JWT secret not configured");
        }

        var key = Encoding.UTF8.GetBytes(_appConfiguration.JwtSecret);
        var tokenHandler = new JwtSecurityTokenHandler();

        var claims = new List<Claim>
        {
            new Claim("session_key", sessionKey),
            new Claim(ClaimTypes.NameIdentifier, account.AccountId.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, account.AccountId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti),
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
            Issuer = _appConfiguration.JwtIssuer,
            Audience = _appConfiguration.JwtAudience
        };

        var jwt = tokenHandler.CreateToken(tokenDescriptor);
        var jwtString = tokenHandler.WriteToken(jwt);
        return (jwtString, sessionId);
    }

    /// <inheritdoc/>
    public string GenerateRefreshToken()
    {
        var tokenBytes = new byte[32]; // 256 bits of cryptographic randomness
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        return Convert.ToHexString(tokenBytes).ToLowerInvariant();
    }

    /// <inheritdoc/>
    public async Task StoreRefreshTokenAsync(Guid accountId, string refreshToken, CancellationToken cancellationToken = default)
    {
        var redisKey = $"refresh_token:{refreshToken}";
        var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Auth);
        // Storage boundary: state store requires string value type (Guid is a value type)
        await stringStore.SaveAsync(
            redisKey,
            accountId.ToString(),
            new StateOptions { Ttl = (int)TimeSpan.FromDays(_configuration.SessionTokenTtlDays).TotalSeconds },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Guid?> ValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var redisKey = $"refresh_token:{refreshToken}";
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Auth);
            var storedAccountId = await stringStore.GetAsync(redisKey, cancellationToken);
            // Storage boundary: parse once at read boundary
            if (string.IsNullOrEmpty(storedAccountId) || !Guid.TryParse(storedAccountId, out var accountId))
            {
                return null;
            }
            return accountId;
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
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Auth);
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
            var key = Encoding.UTF8.GetBytes(_appConfiguration.JwtSecret ?? throw new InvalidOperationException("JWT secret not configured"));

            try
            {
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = _appConfiguration.JwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = _appConfiguration.JwtAudience,
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

                // Validate session data integrity - null roles or authorizations indicates data corruption
                if (sessionData.Roles == null || sessionData.Authorizations == null)
                {
                    _logger.LogError(
                        "Session data corrupted - null Roles or Authorizations. SessionKey: {SessionKey}, AccountId: {AccountId}, RolesNull: {RolesNull}, AuthNull: {AuthNull}",
                        sessionKey, sessionData.AccountId, sessionData.Roles == null, sessionData.Authorizations == null);
                    await _messageBus.TryPublishErrorAsync(
                        "auth",
                        "ValidateToken",
                        "session_data_corrupted",
                        "Session has null Roles or Authorizations - data integrity failure",
                        endpoint: "post:/auth/validate",
                        cancellationToken: cancellationToken);
                    return (StatusCodes.Unauthorized, null);
                }

                // Update last activity timestamp and re-save with remaining TTL
                var remainingSeconds = (int)(sessionData.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds;
                sessionData.LastActiveAt = DateTimeOffset.UtcNow;
                await _sessionService.SaveSessionAsync(sessionKey, sessionData, remainingSeconds, cancellationToken);

                // Return sessionKey as SessionId so Connect service tracks connections by the same
                // key used in account-sessions index and published in SessionInvalidatedEvent
                return (StatusCodes.OK, new ValidateTokenResponse
                {
                    Valid = true,
                    AccountId = sessionData.AccountId,
                    SessionKey = Guid.Parse(sessionKey),
                    Roles = sessionData.Roles,
                    Authorizations = sessionData.Authorizations,
                    RemainingTime = remainingSeconds
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
    public string GenerateSecureToken()
    {
        var tokenBytes = new byte[32]; // 256 bits
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
