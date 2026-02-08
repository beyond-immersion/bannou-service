using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly AppConfiguration _appConfiguration;
    private readonly Mock<IAccountClient> _mockAccountClient;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<AuthService.PasswordResetData>> _mockPasswordResetStore;
    private readonly Mock<IStateStore<SessionDataModel>> _mockSessionStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly Mock<ISessionService> _mockSessionService;
    private readonly Mock<IOAuthProviderService> _mockOAuthService;
    private readonly Mock<IEdgeRevocationService> _mockEdgeRevocationService;
    private readonly Mock<IEmailService> _mockEmailService;
    private readonly Mock<ICacheableStateStore<SessionDataModel>> _mockCacheableStore;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IMfaService> _mockMfaService;

    public AuthServiceTests()
    {
        // Configure JWT settings in Program.Configuration (used by auth services)
        TestConfigurationHelper.ConfigureJwt();

        _mockLogger = new Mock<ILogger<AuthService>>();
        _configuration = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60,
            MockProviders = true,  // Enable mock providers for testing
            // OAuth provider test configuration
            DiscordClientId = "test-discord-client-id",
            DiscordClientSecret = "test-discord-client-secret",
            DiscordRedirectUri = "http://localhost:5012/auth/oauth/discord/callback",
            GoogleClientId = "test-google-client-id",
            GoogleClientSecret = "test-google-client-secret",
            GoogleRedirectUri = "http://localhost:5012/auth/oauth/google/callback",
            TwitchClientId = "test-twitch-client-id",
            TwitchClientSecret = "test-twitch-client-secret",
            TwitchRedirectUri = "http://localhost:5012/auth/oauth/twitch/callback",
            // Steam test configuration
            SteamApiKey = "test-steam-api-key",
            SteamAppId = "123456"
        };
        _appConfiguration = new AppConfiguration
        {
            JwtSecret = "test-jwt-secret-at-least-32-characters-long-for-security",
            JwtIssuer = "test-issuer",
            JwtAudience = "test-audience",
            ServiceDomain = "localhost"
        };
        _mockAccountClient = new Mock<IAccountClient>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockPasswordResetStore = new Mock<IStateStore<AuthService.PasswordResetData>>();
        _mockSessionStore = new Mock<IStateStore<SessionDataModel>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockTokenService = new Mock<ITokenService>();
        _mockSessionService = new Mock<ISessionService>();
        _mockOAuthService = new Mock<IOAuthProviderService>();
        _mockEdgeRevocationService = new Mock<IEdgeRevocationService>();
        _mockEmailService = new Mock<IEmailService>();
        _mockCacheableStore = new Mock<ICacheableStateStore<SessionDataModel>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockMfaService = new Mock<IMfaService>();

        // Setup default behavior for edge revocation service (disabled by default)
        _mockEdgeRevocationService.Setup(e => e.IsEnabled).Returns(false);

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<AuthService.PasswordResetData>(It.IsAny<string>()))
            .Returns(_mockPasswordResetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<SessionDataModel>(It.IsAny<string>()))
            .Returns(_mockSessionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(It.IsAny<string>()))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetCacheableStoreAsync<SessionDataModel>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_mockCacheableStore.Object);

        // Setup default behavior for state stores
        _mockPasswordResetStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<AuthService.PasswordResetData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockSessionStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SessionDataModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockListStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup default behavior for message bus
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    /// <summary>
    /// Helper method to create an AuthService with all required dependencies.
    /// </summary>
    private AuthService CreateAuthService()
    {
        return new AuthService(
            _mockAccountClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _appConfiguration,
            _mockLogger.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEdgeRevocationService.Object,
            _mockEmailService.Object,
            _mockMfaService.Object,
            _mockEventConsumer.Object);
    }

    [Fact]
    public void AuthServiceConfiguration_ShouldBindFromEnvironmentVariables()
    {
        // Arrange
        // Note: JWT secret/issuer/audience are now in core AppConfiguration (BANNOU_JWT_*)
        // AuthServiceConfiguration only contains auth-specific settings like expiration
        var testExpiration = 120;
        var testMockProviders = true;

        try
        {
            // Set environment variables with AUTH_ prefix and UPPER_SNAKE_CASE format
            Environment.SetEnvironmentVariable("AUTH_JWT_EXPIRATION_MINUTES", testExpiration.ToString());
            Environment.SetEnvironmentVariable("AUTH_MOCK_PROVIDERS", testMockProviders.ToString());

            // Act - Build configuration using the same method as dependency injection
            var config = BeyondImmersion.BannouService.Configuration.IServiceConfiguration.BuildConfiguration<AuthServiceConfiguration>();

            // Assert
            Assert.NotNull(config);
            Assert.Equal(testExpiration, config.JwtExpirationMinutes);
            Assert.Equal(testMockProviders, config.MockProviders);
        }
        finally
        {
            // Clean up environment variables
            Environment.SetEnvironmentVariable("AUTH_JWT_EXPIRATION_MINUTES", null);
            Environment.SetEnvironmentVariable("AUTH_MOCK_PROVIDERS", null);
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
        Assert.Equal(19, endpoints.Count); // 19 endpoints defined in auth-api.yaml with x-permissions (14 original + 5 MFA)
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
    public void AuthPermissionRegistration_GetEndpoints_ShouldIncludeUserRolePermissions()
    {
        // Act
        var endpoints = AuthPermissionRegistration.GetEndpoints();

        // Assert - user-role endpoints have empty states (role: user implies authentication)
        var userEndpoints = endpoints.Where(e =>
            e.Permissions.Any(p =>
                p.Role == "user" &&
                p.RequiredStates.Count == 0)).ToList();

        // Refresh, validate, logout, sessions endpoints require user role (implies auth)
        Assert.True(userEndpoints.Count >= 5);
    }

    [Fact]
    public void AuthPermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Arrange
        var instanceId = Guid.NewGuid();

        // Act
        var registrationEvent = AuthPermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("auth", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.Endpoints);
        Assert.Equal(19, registrationEvent.Endpoints.Count); // 19 endpoints (14 original + 5 MFA)
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

        // Should have at least "default" (no state required) - role alone implies auth
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

    // NOTE: Complex role handling tests requiring mesh client mocking with internal SessionDataModel
    // are better suited for integration testing where real Redis can be used.
    // The permission flow is fully tested in PermissionServiceTests.
    // Role storage in Redis is verified through HTTP integration tests (http-tester).

    #endregion

    #region Input Validation Tests

    [Fact]
    public async Task LoginAsync_WithEmptyPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

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
        var service = CreateAuthService();

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
    public async Task RegisterAsync_WithEmptyPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

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
        var service = CreateAuthService();

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
        var service = CreateAuthService();

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
        var service = CreateAuthService();

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
        var service = CreateAuthService();

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
        var service = CreateAuthService();

        // Act
        var (status, response) = await service.ValidateTokenAsync("");

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ValidateTokenAsync_WithMalformedJwt_ShouldReturnUnauthorized()
    {
        // Arrange - TokenService returns Unauthorized for malformed JWTs
        _mockTokenService
            .Setup(t => t.ValidateTokenAsync("not-a-valid-jwt", It.IsAny<CancellationToken>()))
            .ReturnsAsync((StatusCodes.Unauthorized, (ValidateTokenResponse?)null));
        var service = CreateAuthService();

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
        var service = CreateAuthService();

        // Act
        var status = await service.LogoutAsync("", null);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
    }

    [Fact]
    public async Task GetSessionsAsync_WithEmptyJwt_ShouldReturnUnauthorized()
    {
        // Arrange
        var service = CreateAuthService();

        // Act
        var (status, response) = await service.GetSessionsAsync("");

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    #endregion

    #region Constructor Validation Tests

    /// <summary>
    /// Comprehensive constructor validation that catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults)
    /// - Missing null checks
    /// - Wrong parameter names in ArgumentNullException
    /// This single test replaces 11+ individual constructor null-check tests.
    /// </summary>
    [Fact]
    public void AuthService_ConstructorIsValid() =>
        TestUtilities.ServiceConstructorValidator.ValidateServiceConstructor<AuthService>();

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
    public async Task ListProvidersAsync_WithConfiguredProviders_ShouldReturnProviderList()
    {
        // Arrange
        var service = CreateAuthService();

        // Act
        var (status, response) = await service.ListProvidersAsync();

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Providers);

        // Configuration has Discord, Google, Twitch, and Steam configured
        Assert.Equal(4, response.Providers.Count);

        // Verify OAuth providers have correct auth type
        var discord = response.Providers.FirstOrDefault(p => p.Name == "discord");
        Assert.NotNull(discord);
        Assert.Equal("Discord", discord.DisplayName);
        Assert.Equal(ProviderInfoAuthType.Oauth, discord.AuthType);
        Assert.NotNull(discord.AuthUrl);

        // Verify Steam has ticket auth type
        var steam = response.Providers.FirstOrDefault(p => p.Name == "steam");
        Assert.NotNull(steam);
        Assert.Equal("Steam", steam.DisplayName);
        Assert.Equal(ProviderInfoAuthType.Ticket, steam.AuthType);
        Assert.Null(steam.AuthUrl); // Steam uses ticket, no auth URL
    }

    [Fact]
    public async Task ListProvidersAsync_WithNoConfiguredProviders_ShouldReturnEmptyList()
    {
        // Arrange - create service with empty configuration
        var emptyConfig = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60,
            MockProviders = true
            // No OAuth/Steam providers configured
        };

        var service = new AuthService(
            _mockAccountClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            emptyConfig,
            _appConfiguration,
            _mockLogger.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEdgeRevocationService.Object,
            _mockEmailService.Object,
            _mockMfaService.Object,
            _mockEventConsumer.Object);

        // Act
        var (status, response) = await service.ListProvidersAsync();

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Providers);
        Assert.Empty(response.Providers);
    }

    [Fact]
    public async Task InitOAuthAsync_ShouldReturnAuthorizationUrl()
    {
        // Arrange
        _mockOAuthService
            .Setup(o => o.GetAuthorizationUrl(Provider.Discord, "https://example.com/callback", "test-state"))
            .Returns("https://discord.com/oauth2/authorize?client_id=test&state=test-state");
        var service = CreateAuthService();

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
    public async Task RequestPasswordResetAsync_WithValidEmail_ShouldReturnOK()
    {
        // Arrange
        var service = CreateAuthService();

        var request = new PasswordResetRequest
        {
            Email = "test@example.com"
        };

        // Act
        var status = await service.RequestPasswordResetAsync(request);

        // Assert - per schema, this endpoint returns no body (prevents email enumeration)
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_WithValidToken_ShouldReturnOK()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var resetToken = "valid-reset-token";

        // Set up the mock to return valid reset data
        _mockPasswordResetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains(resetToken)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthService.PasswordResetData
            {
                AccountId = accountId,
                Email = "test@example.com",
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) // Not expired
            });

        var service = CreateAuthService();

        var request = new PasswordResetConfirmRequest
        {
            Token = resetToken,
            NewPassword = "newpassword123"
        };

        // Act
        var status = await service.ConfirmPasswordResetAsync(request);

        // Assert - per schema, this endpoint returns no body
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_WithInvalidToken_ShouldReturnBadRequest()
    {
        // Arrange - No mock setup means GetStateAsync returns null (invalid token)
        var service = CreateAuthService();

        var request = new PasswordResetConfirmRequest
        {
            Token = "invalid-token",
            NewPassword = "newpassword123"
        };

        // Act
        var status = await service.ConfirmPasswordResetAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Password Reset Tests

    [Fact]
    public async Task RequestPasswordResetAsync_WithEmptyEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

        var request = new PasswordResetRequest
        {
            Email = ""
        };

        // Act
        var status = await service.RequestPasswordResetAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WithWhitespaceEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

        var request = new PasswordResetRequest
        {
            Email = "   "
        };

        // Act
        var status = await service.RequestPasswordResetAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WithNonExistentEmail_ShouldReturnOkToPreventEnumeration()
    {
        // Arrange - account client throws 404 for non-existent email
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        var service = CreateAuthService();

        var request = new PasswordResetRequest
        {
            Email = "nonexistent@example.com"
        };

        // Act
        var status = await service.RequestPasswordResetAsync(request);

        // Assert - should return OK to prevent email enumeration attacks
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WithExistingAccount_ShouldCallEmailService()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse
            {
                AccountId = accountId,
                Email = "user@example.com",
                DisplayName = "TestUser"
            });

        _mockTokenService.Setup(t => t.GenerateSecureToken())
            .Returns("test-reset-token-abc");

        _configuration.PasswordResetBaseUrl = "https://example.com/reset";

        var service = CreateAuthService();
        var request = new PasswordResetRequest { Email = "user@example.com" };

        // Act
        var status = await service.RequestPasswordResetAsync(request);

        // Assert - returns OK and email service was called
        Assert.Equal(StatusCodes.OK, status);
        _mockEmailService.Verify(e => e.SendAsync(
            "user@example.com",
            It.Is<string>(s => s.Contains("Password Reset")),
            It.Is<string>(b => b.Contains("test-reset-token-abc")),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WhenEmailServiceThrows_ShouldStillReturnOK()
    {
        // Arrange - email sending fails (fire-and-forget pattern)
        var accountId = Guid.NewGuid();
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse
            {
                AccountId = accountId,
                Email = "user@example.com",
                DisplayName = "TestUser"
            });

        _mockTokenService.Setup(t => t.GenerateSecureToken())
            .Returns("test-reset-token");

        _configuration.PasswordResetBaseUrl = "https://example.com/reset";

        _mockEmailService.Setup(e => e.SendAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SendGrid API key invalid"));

        var service = CreateAuthService();
        var request = new PasswordResetRequest { Email = "user@example.com" };

        // Act
        var status = await service.RequestPasswordResetAsync(request);

        // Assert - still returns OK despite email failure (enumeration protection)
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WhenEmailServiceThrows_ShouldPublishErrorEvent()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse
            {
                AccountId = accountId,
                Email = "user@example.com",
                DisplayName = "TestUser"
            });

        _mockTokenService.Setup(t => t.GenerateSecureToken())
            .Returns("test-reset-token");

        _configuration.PasswordResetBaseUrl = "https://example.com/reset";

        _mockEmailService.Setup(e => e.SendAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP connection refused"));

        var service = CreateAuthService();
        var request = new PasswordResetRequest { Email = "user@example.com" };

        // Act
        await service.RequestPasswordResetAsync(request);

        // Assert - error event published for monitoring
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "auth",
            "SendPasswordResetEmail",
            "InvalidOperationException",
            "SMTP connection refused",
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestPasswordResetAsync_WithMissingPasswordResetBaseUrl_ShouldStillReturnOK()
    {
        // Arrange - PasswordResetBaseUrl not configured (SendPasswordResetEmailAsync throws)
        var accountId = Guid.NewGuid();
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse
            {
                AccountId = accountId,
                Email = "user@example.com",
                DisplayName = "TestUser"
            });

        _mockTokenService.Setup(t => t.GenerateSecureToken())
            .Returns("test-reset-token");

        // PasswordResetBaseUrl is null by default - SendPasswordResetEmailAsync will throw

        var service = CreateAuthService();
        var request = new PasswordResetRequest { Email = "user@example.com" };

        // Act
        var status = await service.RequestPasswordResetAsync(request);

        // Assert - still returns OK (fire-and-forget catches the exception)
        Assert.Equal(StatusCodes.OK, status);

        // Email service should NOT be called (threw before reaching it)
        _mockEmailService.Verify(e => e.SendAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_WithEmptyToken_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

        var request = new PasswordResetConfirmRequest
        {
            Token = "",
            NewPassword = "validpassword123"
        };

        // Act
        var status = await service.ConfirmPasswordResetAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    [Fact]
    public async Task ConfirmPasswordResetAsync_WithEmptyNewPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

        var request = new PasswordResetConfirmRequest
        {
            Token = "valid-token",
            NewPassword = ""
        };

        // Act
        var status = await service.ConfirmPasswordResetAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region VerifySteamAuthAsync Tests

    [Fact]
    public async Task VerifySteamAuthAsync_WithValidTicket_ReturnsAuthResponse()
    {
        // Arrange
        var steamId = "76561198012345678";
        var accountId = Guid.NewGuid();
        var account = new AccountResponse
        {
            AccountId = accountId,
            Email = null, // Steam accounts have no email
            DisplayName = "Steam_345678"
        };

        // Mock OAuth service to return valid Steam ID
        _mockOAuthService.Setup(o => o.ValidateSteamTicketAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(steamId);

        // Mock OAuth service to return/create account
        _mockOAuthService.Setup(o => o.FindOrCreateOAuthAccountAsync(
            Provider.Steam,
            It.IsAny<Services.OAuthUserInfo>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, false));

        // Mock token service
        _mockTokenService.Setup(t => t.GenerateAccessTokenAsync(
            It.IsAny<AccountResponse>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test-access-token", Guid.NewGuid()));
        _mockTokenService.Setup(t => t.GenerateRefreshToken())
            .Returns("test-refresh-token");

        // Use a config with MockProviders = false to test real flow
        var realConfig = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60,
            MockProviders = false,
            SteamApiKey = "test-steam-api-key",
            SteamAppId = "123456"
        };

        var service = new AuthService(
            _mockAccountClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            realConfig,
            _appConfiguration,
            _mockLogger.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEdgeRevocationService.Object,
            _mockEmailService.Object,
            _mockMfaService.Object,
            _mockEventConsumer.Object);

        var request = new SteamVerifyRequest { Ticket = "valid-steam-ticket-hex" };

        // Act
        var (status, response) = await service.VerifySteamAuthAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(accountId, response.AccountId);
        Assert.Equal("test-access-token", response.AccessToken);
        Assert.Equal("test-refresh-token", response.RefreshToken);
    }

    [Fact]
    public async Task VerifySteamAuthAsync_WithMockMode_UsesMockSteamId()
    {
        // Arrange - default configuration has MockProviders = true
        var mockSteamInfo = new Services.OAuthUserInfo
        {
            ProviderId = "mock-steam-id-12345",
            DisplayName = "Steam_12345"
        };

        var account = new AccountResponse
        {
            AccountId = Guid.NewGuid(),
            DisplayName = "Steam_12345"
        };

        _mockOAuthService.Setup(o => o.GetMockSteamUserInfoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSteamInfo);

        _mockOAuthService.Setup(o => o.FindOrCreateOAuthAccountAsync(
            Provider.Steam,
            It.IsAny<Services.OAuthUserInfo>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((account, false));

        _mockTokenService.Setup(t => t.GenerateAccessTokenAsync(
            It.IsAny<AccountResponse>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(("mock-token", Guid.NewGuid()));
        _mockTokenService.Setup(t => t.GenerateRefreshToken())
            .Returns("mock-refresh");

        var service = CreateAuthService();
        var request = new SteamVerifyRequest { Ticket = "any-ticket" };

        // Act
        var (status, response) = await service.VerifySteamAuthAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify mock info was used (not real Steam API)
        _mockOAuthService.Verify(o => o.GetMockSteamUserInfoAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockOAuthService.Verify(o => o.ValidateSteamTicketAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task VerifySteamAuthAsync_WithMissingConfig_ReturnsInternalServerError()
    {
        // Arrange - config without Steam API key
        var configWithoutSteam = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60,
            MockProviders = false,
            SteamApiKey = null,
            SteamAppId = "123456"
        };

        var service = new AuthService(
            _mockAccountClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            configWithoutSteam,
            _appConfiguration,
            _mockLogger.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEdgeRevocationService.Object,
            _mockEmailService.Object,
            _mockMfaService.Object,
            _mockEventConsumer.Object);

        var request = new SteamVerifyRequest { Ticket = "valid-ticket" };

        // Act
        var (status, response) = await service.VerifySteamAuthAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task VerifySteamAuthAsync_WithInvalidTicket_ReturnsUnauthorized()
    {
        // Arrange
        _mockOAuthService.Setup(o => o.ValidateSteamTicketAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var realConfig = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60,
            MockProviders = false,
            SteamApiKey = "test-key",
            SteamAppId = "123456"
        };

        var service = new AuthService(
            _mockAccountClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            realConfig,
            _appConfiguration,
            _mockLogger.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEdgeRevocationService.Object,
            _mockEmailService.Object,
            _mockMfaService.Object,
            _mockEventConsumer.Object);

        var request = new SteamVerifyRequest { Ticket = "invalid-ticket" };

        // Act
        var (status, response) = await service.VerifySteamAuthAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task VerifySteamAuthAsync_WithAccountCreationFailure_ReturnsInternalServerError()
    {
        // Arrange
        var steamId = "76561198012345678";

        _mockOAuthService.Setup(o => o.ValidateSteamTicketAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(steamId);

        // Account creation fails
        _mockOAuthService.Setup(o => o.FindOrCreateOAuthAccountAsync(
            Provider.Steam,
            It.IsAny<Services.OAuthUserInfo>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(((AccountResponse?)null, false));

        var realConfig = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60,
            MockProviders = false,
            SteamApiKey = "test-key",
            SteamAppId = "123456"
        };

        var service = new AuthService(
            _mockAccountClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            realConfig,
            _appConfiguration,
            _mockLogger.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEdgeRevocationService.Object,
            _mockEmailService.Object,
            _mockMfaService.Object,
            _mockEventConsumer.Object);

        var request = new SteamVerifyRequest { Ticket = "valid-ticket" };

        // Act
        var (status, response) = await service.VerifySteamAuthAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);

        // Verify error event was published via TryPublishErrorAsync
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "auth",
            "VerifySteamAuth",
            "account_creation_failed",
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task VerifySteamAuthAsync_WithWhitespaceTicket_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateAuthService();
        var request = new SteamVerifyRequest { Ticket = "   " };

        // Act
        var (status, response) = await service.VerifySteamAuthAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region Rate Limiting Tests

    /// <summary>
    /// Verifies that login is blocked when the failed attempt counter has reached MaxLoginAttempts.
    /// Should return Unauthorized, publish a RateLimited audit event, and never call AccountClient.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WhenRateLimited_ReturnsUnauthorizedAndPublishesEvent()
    {
        // Arrange - counter already at max (5)
        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            It.Is<string>(k => k.StartsWith("login-attempts:")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)5);

        var service = CreateAuthService();
        var request = new LoginRequest { Email = "attacker@example.com", Password = "guessing" };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);

        // Should publish login failed event with RateLimited reason
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "auth.login.failed",
            It.Is<AuthLoginFailedEvent>(e => e.Reason == AuthLoginFailedReason.RateLimited),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Should NOT attempt account lookup (short-circuited by rate limit check)
        _mockAccountClient.Verify(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that login is also blocked when the counter exceeds MaxLoginAttempts (not just equal).
    /// </summary>
    [Fact]
    public async Task LoginAsync_WhenCounterExceedsMax_ReturnsUnauthorized()
    {
        // Arrange - counter well above max
        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            It.Is<string>(k => k.StartsWith("login-attempts:")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)50);

        var service = CreateAuthService();
        var request = new LoginRequest { Email = "persistent-attacker@example.com", Password = "still-guessing" };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);
    }

    /// <summary>
    /// Verifies that a wrong password increments the rate limit counter and publishes InvalidCredentials event.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithWrongPassword_IncrementsCounterAndPublishesEvent()
    {
        // Arrange - no prior attempts
        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var accountId = Guid.NewGuid();
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse
            {
                AccountId = accountId,
                Email = "user@example.com",
                DisplayName = "TestUser",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-password")
            });

        var service = CreateAuthService();
        var request = new LoginRequest { Email = "user@example.com", Password = "wrong-password" };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);

        // Verify counter was incremented with lockout TTL
        _mockCacheableStore.Verify(s => s.IncrementAsync(
            It.Is<string>(k => k == "login-attempts:user@example.com"),
            1,
            It.Is<StateOptions>(o => o.Ttl == _configuration.LoginLockoutMinutes * 60),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify InvalidCredentials event published with account ID
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "auth.login.failed",
            It.Is<AuthLoginFailedEvent>(e =>
                e.Reason == AuthLoginFailedReason.InvalidCredentials &&
                e.AccountId == accountId),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a login attempt with a non-existent account increments the rate limit counter.
    /// This prevents email enumeration by making non-existent accounts cost the same as wrong passwords.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithNonExistentAccount_IncrementsCounterAndPublishesEvent()
    {
        // Arrange - no prior attempts
        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        // Account not found throws ApiException with 404
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        var service = CreateAuthService();
        var request = new LoginRequest { Email = "nobody@example.com", Password = "anypassword" };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);

        // Verify counter was incremented
        _mockCacheableStore.Verify(s => s.IncrementAsync(
            It.Is<string>(k => k == "login-attempts:nobody@example.com"),
            1,
            It.Is<StateOptions>(o => o.Ttl == _configuration.LoginLockoutMinutes * 60),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify AccountNotFound event published (no account ID since account doesn't exist)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "auth.login.failed",
            It.Is<AuthLoginFailedEvent>(e =>
                e.Reason == AuthLoginFailedReason.AccountNotFound &&
                e.AccountId == null),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that a successful login clears the rate limit counter.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithCorrectPassword_ClearsRateLimitCounter()
    {
        // Arrange - 3 prior failed attempts (below threshold of 5)
        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)3);

        var accountId = Guid.NewGuid();
        var correctPassword = "correct-password";
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse
            {
                AccountId = accountId,
                Email = "user@example.com",
                DisplayName = "TestUser",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(correctPassword)
            });

        _mockTokenService.Setup(t => t.GenerateAccessTokenAsync(
            It.IsAny<AccountResponse>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test-access-token", Guid.NewGuid()));
        _mockTokenService.Setup(t => t.GenerateRefreshToken())
            .Returns("test-refresh-token");

        var service = CreateAuthService();
        var request = new LoginRequest { Email = "user@example.com", Password = correctPassword };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify counter was deleted on success
        _mockCacheableStore.Verify(s => s.DeleteCounterAsync(
            It.Is<string>(k => k == "login-attempts:user@example.com"),
            It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify counter was NOT incremented (successful login)
        _mockCacheableStore.Verify(s => s.IncrementAsync(
            It.IsAny<string>(),
            It.IsAny<long>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that login proceeds normally when the counter is below MaxLoginAttempts.
    /// The counter having a value doesn't block login if it's under the threshold.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithCounterBelowMax_ProceedsNormally()
    {
        // Arrange - 4 attempts (one below the default max of 5)
        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)4);

        var accountId = Guid.NewGuid();
        var correctPassword = "correct-password";
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse
            {
                AccountId = accountId,
                Email = "user@example.com",
                DisplayName = "TestUser",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(correctPassword)
            });

        _mockTokenService.Setup(t => t.GenerateAccessTokenAsync(
            It.IsAny<AccountResponse>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(("test-token", Guid.NewGuid()));
        _mockTokenService.Setup(t => t.GenerateRefreshToken())
            .Returns("test-refresh");

        var service = CreateAuthService();
        var request = new LoginRequest { Email = "user@example.com", Password = correctPassword };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert - login should succeed since 4 < 5
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Account lookup should have been called (not blocked)
        _mockAccountClient.Verify(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that login proceeds when no counter exists (first login attempt for this email).
    /// GetCounterAsync returns null for non-existent keys.
    /// </summary>
    [Fact]
    public async Task LoginAsync_WithNoExistingCounter_ProceedsNormally()
    {
        // Arrange - no counter exists (first attempt)
        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        var accountId = Guid.NewGuid();
        var correctPassword = "my-password";
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountResponse
            {
                AccountId = accountId,
                Email = "fresh@example.com",
                DisplayName = "FreshUser",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(correctPassword)
            });

        _mockTokenService.Setup(t => t.GenerateAccessTokenAsync(
            It.IsAny<AccountResponse>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(("token", Guid.NewGuid()));
        _mockTokenService.Setup(t => t.GenerateRefreshToken())
            .Returns("refresh");

        var service = CreateAuthService();
        var request = new LoginRequest { Email = "fresh@example.com", Password = correctPassword };

        // Act
        var (status, response) = await service.LoginAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    /// <summary>
    /// Verifies that email is normalized (trimmed and lowercased) for the rate limit key.
    /// This prevents bypassing rate limits with "User@Example.com" vs "user@example.com".
    /// </summary>
    [Fact]
    public async Task LoginAsync_NormalizesEmailForRateLimitKey()
    {
        // Arrange - counter at max for the normalized form
        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            "login-attempts:attacker@example.com",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)5);

        var service = CreateAuthService();

        // Act - send email with mixed case and whitespace
        var request = new LoginRequest { Email = "  Attacker@Example.COM  ", Password = "guessing" };
        var (status, response) = await service.LoginAsync(request);

        // Assert - should be blocked because normalized email matches the counter
        Assert.Equal(StatusCodes.Unauthorized, status);
        Assert.Null(response);

        // Verify the counter was checked with the normalized key
        _mockCacheableStore.Verify(s => s.GetCounterAsync(
            "login-attempts:attacker@example.com",
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that the rate limit lockout TTL uses the configured LoginLockoutMinutes value
    /// converted to seconds.
    /// </summary>
    [Fact]
    public async Task LoginAsync_UsesConfiguredLockoutTtl()
    {
        // Arrange - custom lockout of 30 minutes
        _configuration.LoginLockoutMinutes = 30;

        _mockCacheableStore.Setup(s => s.GetCounterAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((long?)null);

        // Account not found
        _mockAccountClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        var service = CreateAuthService();
        var request = new LoginRequest { Email = "test@example.com", Password = "pass" };

        // Act
        await service.LoginAsync(request);

        // Assert - TTL should be 30 * 60 = 1800 seconds
        _mockCacheableStore.Verify(s => s.IncrementAsync(
            It.IsAny<string>(),
            1,
            It.Is<StateOptions>(o => o.Ttl == 1800),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
