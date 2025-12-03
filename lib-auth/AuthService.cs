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
using System.Security.Claims;
using System.Text;
using System.Text.Json;

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

        logger.LogInformation("AuthService constructor starting - dependencies being injected");
        logger.LogInformation("AccountsClient dependency successfully assigned");
        logger.LogInformation("DaprClient dependency successfully assigned");
        logger.LogInformation("Configuration dependency successfully assigned");

        try
        {
            _logger.LogInformation("AuthService initialized with JwtSecret length: {Length}, Issuer: {Issuer}, Audience: {Audience}",
                _configuration.JwtSecret?.Length ?? 0, _configuration.JwtIssuer, _configuration.JwtAudience);

            _logger.LogInformation("Testing AccountsClient type: {Type}", _accountsClient.GetType().FullName);

            _logger.LogInformation("AuthService constructor completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AuthService constructor completion");
            throw;
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

            // Lookup account by email via AccountsClient
            _logger.LogInformation("Looking up account by email via AccountsClient: {Email}", body.Email);

            AccountResponse account;
            try
            {
                account = await _accountsClient.GetAccountByEmailAsync(body.Email, cancellationToken);
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
                ConnectUrl = new Uri($"ws://localhost:8080/api/ws") // Connect service WebSocket endpoint
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
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = new Uri($"ws://localhost:8080/api/ws") // Connect service WebSocket endpoint
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
                ExpiresIn = _configuration.JwtExpirationMinutes * 60,
                ConnectUrl = new Uri($"ws://localhost:8080/api/ws") // Connect service WebSocket endpoint
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
                account = await _accountsClient.GetAccountAsync(Guid.Parse(accountId), cancellationToken);
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
                ConnectUrl = new Uri($"ws://localhost:8080/api/ws") // Connect service WebSocket endpoint
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

                _logger.LogInformation("Session logged out successfully for account: {AccountId}", validateResponse.AccountId);
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
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
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

            _logger.LogInformation("Session {SessionId} terminated successfully", sessionId);
            return (StatusCodes.NoContent, null);
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
            await _accountsClient.UpdatePasswordHashAsync(resetData.AccountId, new UpdatePasswordRequest
            {
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

                _logger.LogDebug("Added session {SessionKey} to account index for account {AccountId}", sessionKey, accountId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add session {SessionKey} to account index for account {AccountId}", sessionKey, accountId);
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

    #region Event Handlers

    /// <summary>
    /// Called when a Dapr event is received. Routes to appropriate event handlers.
    /// </summary>
    public async Task OnEventReceivedAsync<T>(string topic, T eventData) where T : class
    {
        _logger.LogInformation("AuthService received event {Topic} with data type {DataType}", topic, typeof(T).Name);

        switch (topic)
        {
            case "account.deleted":
                if (eventData is AccountDeletedEvent deletedEvent)
                {
                    await HandleAccountDeletedEventAsync(deletedEvent);
                }
                else
                {
                    _logger.LogWarning("Received account.deleted event but data type is {DataType}, expected AccountDeletedEvent", typeof(T).Name);
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
    /// Invalidate all sessions for a specific account.
    /// Used when account is deleted to ensure security.
    /// </summary>
    private async Task InvalidateAllSessionsForAccountAsync(Guid accountId)
    {
        try
        {
            // Get session keys directly from the account index
            var indexKey = $"account-sessions:{accountId}";
            var sessionKeys = await _daprClient.GetStateAsync<List<string>>(
                REDIS_STATE_STORE,
                indexKey,
                cancellationToken: CancellationToken.None);

            if (sessionKeys == null || !sessionKeys.Any())
            {
                _logger.LogInformation("No sessions found for account {AccountId}", accountId);
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate sessions for account {AccountId}", accountId);
            throw; // Re-throw to let the event handler know about the failure
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
