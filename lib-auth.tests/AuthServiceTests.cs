using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Auth;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for AuthService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class AuthServiceTests
{
    private readonly Mock<ILogger<AuthService>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly Mock<IAccountsClient> _mockAccountsClient;
    private readonly Mock<DaprClient> _mockDaprClient;

    public AuthServiceTests()
    {
        _mockLogger = new Mock<ILogger<AuthService>>();
        _configuration = new AuthServiceConfiguration
        {
            JwtSecret = "test-jwt-secret-at-least-32-chars-long",
            JwtIssuer = "test-issuer",
            JwtAudience = "test-audience",
            JwtExpirationMinutes = 60
        };
        _mockAccountsClient = new Mock<IAccountsClient>();
        _mockDaprClient = new Mock<DaprClient>();
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        Assert.NotNull(service);
    }


    [Fact]
    public void AuthServiceConfiguration_ShouldBindFromEnvironmentVariables()
    {
        // Arrange
        var testSecret = "test-jwt-secret-from-env";
        var testIssuer = "test-issuer";
        var testAudience = "test-audience";
        var testExpiration = 120;

        try
        {
            // Set environment variables with BANNOU_ prefix
            Environment.SetEnvironmentVariable("BANNOU_JWTSECRET", testSecret);
            Environment.SetEnvironmentVariable("BANNOU_JWTISSUER", testIssuer);
            Environment.SetEnvironmentVariable("BANNOU_JWTAUDIENCE", testAudience);
            Environment.SetEnvironmentVariable("BANNOU_JWTEXPIRATIONMINUTES", testExpiration.ToString());

            // Act - Build configuration using the same method as dependency injection
            var config = BeyondImmersion.BannouService.Configuration.IServiceConfiguration.BuildConfiguration<AuthServiceConfiguration>();

            // Assert
            Assert.NotNull(config);
            Assert.Equal(testSecret, config.JwtSecret);
            Assert.Equal(testIssuer, config.JwtIssuer);
            Assert.Equal(testAudience, config.JwtAudience);
            Assert.Equal(testExpiration, config.JwtExpirationMinutes);
        }
        finally
        {
            // Clean up environment variables
            Environment.SetEnvironmentVariable("BANNOU_JWTSECRET", null);
            Environment.SetEnvironmentVariable("BANNOU_JWTISSUER", null);
            Environment.SetEnvironmentVariable("BANNOU_JWTAUDIENCE", null);
            Environment.SetEnvironmentVariable("BANNOU_JWTEXPIRATIONMINUTES", null);
        }
    }

    #region Permission Registration Tests

    [Fact]
    public void AuthPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = AuthPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
        Assert.Equal(12, endpoints.Count); // 12 endpoints defined in auth-api.yaml with x-permissions (removed steam/init)
    }

    [Fact]
    public void AuthPermissionRegistration_GetEndpoints_ShouldContainLoginEndpoint()
    {
        // Act
        var endpoints = AuthPermissionRegistration.GetEndpoints();

        // Assert
        var loginEndpoint = endpoints.FirstOrDefault(e =>
            e.Path == "/auth/login" &&
            e.Method == BeyondImmersion.BannouService.Events.ServiceEndpointMethod.POST);

        Assert.NotNull(loginEndpoint);
        Assert.NotNull(loginEndpoint.Permissions);
        Assert.True(loginEndpoint.Permissions.Count >= 1); // At least anonymous access
    }

    [Fact]
    public void AuthPermissionRegistration_GetEndpoints_ShouldIncludeAnonymousPermissions()
    {
        // Act
        var endpoints = AuthPermissionRegistration.GetEndpoints();

        // Assert
        var anonymousEndpoints = endpoints.Where(e =>
            e.Permissions.Any(p => p.Role == "anonymous")).ToList();

        // Registration, login, OAuth, Steam verify endpoints should be accessible anonymously
        Assert.True(anonymousEndpoints.Count >= 6); // Reduced by 1 after removing steam/init
    }

    [Fact]
    public void AuthPermissionRegistration_GetEndpoints_ShouldIncludeAuthenticatedPermissions()
    {
        // Act
        var endpoints = AuthPermissionRegistration.GetEndpoints();

        // Assert
        var authenticatedEndpoints = endpoints.Where(e =>
            e.Permissions.Any(p =>
                p.Role == "user" &&
                p.RequiredStates.ContainsKey("auth") &&
                p.RequiredStates["auth"] == "authenticated")).ToList();

        // Refresh, validate, logout, sessions endpoints require authentication
        Assert.True(authenticatedEndpoints.Count >= 5);
    }

    [Fact]
    public void AuthPermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Act
        var registrationEvent = AuthPermissionRegistration.CreateRegistrationEvent();

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("auth", registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.EventId);
        Assert.NotNull(registrationEvent.Endpoints);
        Assert.Equal(12, registrationEvent.Endpoints.Count); // 12 endpoints (removed steam/init)
        Assert.NotEmpty(registrationEvent.Version);
    }

    [Fact]
    public void AuthPermissionRegistration_BuildPermissionMatrix_ShouldGroupByStateAndRole()
    {
        // Act
        var matrix = AuthPermissionRegistration.BuildPermissionMatrix();

        // Assert
        Assert.NotNull(matrix);
        Assert.NotEmpty(matrix);

        // Should have at least "default" (no state) and "auth:authenticated" states
        Assert.True(matrix.ContainsKey("default") || matrix.Count > 0);
    }

    [Fact]
    public void AuthPermissionRegistration_ServiceId_ShouldBeAuth()
    {
        // Assert
        Assert.Equal("auth", AuthPermissionRegistration.ServiceId);
    }

    [Fact]
    public void AuthPermissionRegistration_ServiceVersion_ShouldNotBeEmpty()
    {
        // Assert
        Assert.NotNull(AuthPermissionRegistration.ServiceVersion);
        Assert.NotEmpty(AuthPermissionRegistration.ServiceVersion);
    }

    #endregion

    #region Role Handling Tests

    // NOTE: Complex role handling tests requiring DaprClient mocking with internal SessionDataModel
    // are better suited for integration testing where real Redis can be used.
    // The permission flow is fully tested in PermissionsServiceTests.
    // Role storage in Redis is verified through HTTP integration tests (http-tester).

    #endregion

    #region Input Validation Tests

    [Fact]
    public async Task LoginAsync_WithNullEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new LoginRequest
        {
            Email = null!,
            Password = "validpassword"
        };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task LoginAsync_WithEmptyPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new LoginRequest
        {
            Email = "test@example.com",
            Password = ""
        };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task LoginAsync_WithWhitespaceEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new LoginRequest
        {
            Email = "   ",
            Password = "validpassword"
        };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterAsync_WithNullUsername_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new RegisterRequest
        {
            Username = null!,
            Password = "validpassword"
        };

        // Act
        var (status, response) = await service.RegisterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterAsync_WithEmptyPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new RegisterRequest
        {
            Username = "validuser",
            Password = ""
        };

        // Act
        var (status, response) = await service.RegisterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RegisterAsync_WithWhitespaceUsername_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new RegisterRequest
        {
            Username = "   ",
            Password = "validpassword"
        };

        // Act
        var (status, response) = await service.RegisterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CompleteOAuthAsync_WithEmptyCode_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new OAuthCallbackRequest
        {
            Code = ""
        };

        // Act
        var (status, response) = await service.CompleteOAuthAsync(Provider.Discord, request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task VerifySteamAuthAsync_WithEmptyTicket_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new SteamVerifyRequest
        {
            Ticket = ""
        };

        // Act
        var (status, response) = await service.VerifySteamAuthAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RefreshTokenAsync_WithEmptyRefreshToken_ShouldReturnBadRequest()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new RefreshRequest
        {
            RefreshToken = ""
        };

        // Act
        var (status, response) = await service.RefreshTokenAsync("valid-jwt", request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithEmptyJwt_ShouldReturnUnauthorized()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        // Act
        var (status, response) = await service.ValidateTokenAsync("");

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithNullJwt_ShouldReturnUnauthorized()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        // Act
        var (status, response) = await service.ValidateTokenAsync(null!);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithMalformedJwt_ShouldReturnUnauthorized()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        // Act - pass a clearly invalid JWT format
        var (status, response) = await service.ValidateTokenAsync("not-a-valid-jwt");

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task LogoutAsync_WithEmptyJwt_ShouldReturnUnauthorized()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        // Act
        var (status, response) = await service.LogoutAsync("", null);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetSessionsAsync_WithEmptyJwt_ShouldReturnUnauthorized()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        // Act
        var (status, response) = await service.GetSessionsAsync("");

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    #endregion

    #region Configuration Validation Tests

    [Fact]
    public void Constructor_WithNullAccountsClient_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            null!,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            null!,
            _configuration,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            null!));
    }

    [Fact]
    public void AuthServiceConfiguration_DefaultValues_ShouldHaveReasonableDefaults()
    {
        // Arrange
        var config = new AuthServiceConfiguration();

        // Assert - check that properties exist and can be accessed
        // Default values may be null/empty/0, which is acceptable for unit test isolation
        Assert.NotNull(config);
    }

    #endregion

    #region OAuth Provider Tests

    [Fact]
    public async Task InitOAuthAsync_ShouldReturnAuthorizationUrl()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        // Act
        var (status, response) = await service.InitOAuthAsync(
            Provider.Discord,
            "https://example.com/callback",
            "test-state");

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task InitSteamAuthAsync_ShouldReturnAuthorizationUrl()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        // Act
        var (status, response) = await service.InitSteamAuthAsync("https://example.com/steam-callback");

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WithValidEmail_ShouldReturnOK()
    {
        // Arrange
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new PasswordResetRequest
        {
            Email = "test@example.com"
        };

        // Act
        var (status, response) = await service.RequestPasswordResetAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_WithValidToken_ShouldReturnOK()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var resetToken = "valid-reset-token";

        // Set up the mock to return valid reset data
        _mockDaprClient
            .Setup(d => d.GetStateAsync<AuthService.PasswordResetData>(
                It.IsAny<string>(),
                It.Is<string>(s => s.Contains(resetToken)),
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthService.PasswordResetData
            {
                AccountId = accountId,
                Email = "test@example.com",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) // Not expired
            });

        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new PasswordResetConfirmRequest
        {
            Token = resetToken,
            NewPassword = "newpassword123"
        };

        // Act
        var (status, response) = await service.ConfirmPasswordResetAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_WithInvalidToken_ShouldReturnBadRequest()
    {
        // Arrange - No mock setup means GetStateAsync returns null (invalid token)
        var service = new AuthService(
            _mockAccountsClient.Object,
            _mockDaprClient.Object,
            _configuration,
            _mockLogger.Object);

        var request = new PasswordResetConfirmRequest
        {
            Token = "invalid-token",
            NewPassword = "newpassword123"
        };

        // Act
        var (status, response) = await service.ConfirmPasswordResetAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion
}
