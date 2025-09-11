using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Controllers.Generated;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Service implementation for authentication operations.
/// Implements the schema-first generated interface methods.
/// </summary>
[DaprService("auth", typeof(IAuthService), lifetime: ServiceLifetime.Scoped)]
public class AuthService : DaprService<AuthServiceConfiguration>, IAuthService
{
    private readonly IAccountsService _accountsService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IAccountsService accountsService,
        AuthServiceConfiguration configuration,
        ILogger<AuthService> logger)
        : base(configuration, logger)
    {
        _accountsService = accountsService ?? throw new ArgumentNullException(nameof(accountsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ActionResult<RegisterResponse>> RegisterAsync(
        RegisterRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing registration request for username: {Username}", body.Username);

            // Validate request
            if (string.IsNullOrWhiteSpace(body.Username))
            {
                return new BadRequestObjectResult(new AuthErrorResponse
                {
                    Error = AuthErrorResponseError.INVALID_REQUEST,
                    Message = "Username is required"
                });
            }

            if (string.IsNullOrWhiteSpace(body.Password))
            {
                return new BadRequestObjectResult(new AuthErrorResponse
                {
                    Error = AuthErrorResponseError.INVALID_REQUEST,
                    Message = "Password is required"
                });
            }

            // Create account through accounts service
            var createAccountRequest = new CreateAccountRequest
            {
                DisplayName = body.Username,
                Email = body.Email,
                Provider = Provider.Email,
                ExternalId = body.Username,
                PasswordHash = HashPassword(body.Password), // Simple hash implementation needed
                EmailVerified = false,
                Roles = new[] { "user" }
            };

            var createResult = await _accountsService.CreateAccountAsync(createAccountRequest, cancellationToken);
            if (createResult.Result is not OkObjectResult okResult || okResult.Value is not AccountResponse account)
            {
                return new BadRequestObjectResult(new AuthErrorResponse
                {
                    Error = AuthErrorResponseError.USER_EXISTS,
                    Message = "Failed to create account - user may already exist"
                });
            }

            // Generate tokens
            var accessToken = GenerateAccessToken(account);
            var refreshToken = GenerateRefreshToken();

            _logger.LogInformation("Successfully registered user: {Username} with ID: {AccountId}", body.Username, account.AccountId);

            return new RegisterResponse
            {
                Access_token = accessToken,
                Refresh_token = refreshToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during registration for username: {Username}", body.Username);
            return new ObjectResult(new AuthErrorResponse
            {
                Error = AuthErrorResponseError.INTERNAL_ERROR,
                Message = "An error occurred during registration"
            }) { StatusCode = 500 };
        }
    }

    /// <inheritdoc/>
    public async Task<ActionResult<LoginResponse>> LoginWithCredentialsGetAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing credential login (GET) for username: {Username}", username);

            var loginResult = await PerformLogin(username, password, cancellationToken);
            return loginResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during credential login (GET) for username: {Username}", username);
            return new ObjectResult(new AuthErrorResponse
            {
                Error = AuthErrorResponseError.INTERNAL_ERROR,
                Message = "An error occurred during login"
            }) { StatusCode = 500 };
        }
    }

    /// <inheritdoc/>
    public async Task<ActionResult<LoginResponse>> LoginWithCredentialsPostAsync(
        string username,
        string password,
        LoginRequest? body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing credential login (POST) for username: {Username}", username);

            var loginResult = await PerformLogin(username, password, cancellationToken);
            return loginResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during credential login (POST) for username: {Username}", username);
            return new ObjectResult(new AuthErrorResponse
            {
                Error = AuthErrorResponseError.INTERNAL_ERROR,
                Message = "An error occurred during login"
            }) { StatusCode = 500 };
        }
    }

    /// <inheritdoc/>
    public async Task<ActionResult<LoginResponse>> LoginWithTokenGetAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing token refresh (GET)");

            // Validate refresh token (simplified implementation)
            if (string.IsNullOrWhiteSpace(token))
            {
                return new BadRequestObjectResult(new AuthErrorResponse
                {
                    Error = AuthErrorResponseError.TOKEN_INVALID,
                    Message = "Refresh token is required"
                });
            }

            // In a real implementation, you would validate the refresh token against database
            // For now, we'll return a mock response
            var accessToken = GenerateMockAccessToken();
            var newRefreshToken = GenerateRefreshToken();

            return new LoginResponse
            {
                Access_token = accessToken,
                Refresh_token = newRefreshToken
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh (GET)");
            return new ObjectResult(new AuthErrorResponse
            {
                Error = AuthErrorResponseError.INTERNAL_ERROR,
                Message = "An error occurred during token refresh"
            }) { StatusCode = 500 };
        }
    }

    /// <inheritdoc/>
    public async Task<ActionResult<LoginResponse>> LoginWithTokenPostAsync(
        string token,
        LoginRequest? body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing token refresh (POST)");

            // Same logic as GET version
            return await LoginWithTokenGetAsync(token, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh (POST)");
            return new ObjectResult(new AuthErrorResponse
            {
                Error = AuthErrorResponseError.INTERNAL_ERROR,
                Message = "An error occurred during token refresh"
            }) { StatusCode = 500 };
        }
    }

    /// <inheritdoc/>
    public async Task<ActionResult<ValidateTokenResponse>> ValidateTokenAsync(
        ValidateTokenRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing token validation request");

            if (string.IsNullOrWhiteSpace(body.Token))
            {
                return new BadRequestObjectResult(new AuthErrorResponse
                {
                    Error = AuthErrorResponseError.TOKEN_INVALID,
                    Message = "Token is required"
                });
            }

            // In a real implementation, you would validate the JWT token
            // For now, we'll return a mock validation response
            var isValid = ValidateJwtToken(body.Token);

            return new ValidateTokenResponse
            {
                Valid = isValid,
                Expires_at = DateTimeOffset.UtcNow.AddHours(1), // Mock expiration
                Subject = "mock-user-id",
                Claims = new { role = "user", scope = "api" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token validation");
            return new ObjectResult(new AuthErrorResponse
            {
                Error = AuthErrorResponseError.INTERNAL_ERROR,
                Message = "An error occurred during token validation"
            }) { StatusCode = 500 };
        }
    }

    // Private helper methods
    private async Task<ActionResult<LoginResponse>> PerformLogin(string username, string password, CancellationToken cancellationToken)
    {
        // Validate credentials
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return new BadRequestObjectResult(new AuthErrorResponse
            {
                Error = AuthErrorResponseError.MISSING_CREDENTIALS,
                Message = "Username and password are required"
            });
        }

        // Try to get account by email (assuming username is email)
        var accountResult = await _accountsService.GetAccountByEmailAsync(username, cancellationToken);
        if (accountResult.Result is not OkObjectResult okResult || okResult.Value is not AccountResponse account)
        {
            return new UnauthorizedObjectResult(new AuthErrorResponse
            {
                Error = AuthErrorResponseError.AUTHENTICATION_FAILED,
                Message = "Invalid credentials"
            });
        }

        // In a real implementation, you would verify the password hash
        // For now, we'll accept any password for demo purposes
        var accessToken = GenerateAccessToken(account);
        var refreshToken = GenerateRefreshToken();

        _logger.LogInformation("Successfully authenticated user: {Username} (ID: {AccountId})", username, account.AccountId);

        return new LoginResponse
        {
            Access_token = accessToken,
            Refresh_token = refreshToken
        };
    }

    private string HashPassword(string password)
    {
        // Simplified password hashing - in production use BCrypt or similar
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password + "salt"));
    }

    private string GenerateAccessToken(AccountResponse account)
    {
        // Simplified JWT generation - in production use proper JWT library
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{{\"sub\":\"{account.AccountId}\",\"email\":\"{account.Email}\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"));
        return $"eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.{payload}.mock-signature";
    }

    private string GenerateMockAccessToken()
    {
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{{\"sub\":\"mock-user\",\"exp\":{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}}"));
        return $"eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.{payload}.mock-signature";
    }

    private string GenerateRefreshToken()
    {
        // Generate a random refresh token
        return Guid.NewGuid().ToString("N");
    }

    private bool ValidateJwtToken(string token)
    {
        // Simplified token validation - in production use proper JWT validation
        return !string.IsNullOrWhiteSpace(token) && token.StartsWith("eyJ");
    }
}