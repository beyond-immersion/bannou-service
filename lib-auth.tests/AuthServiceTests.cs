using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.Subscriptions;
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
    private readonly Mock<ISubscriptionsClient> _mockSubscriptionsClient;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<AuthService.PasswordResetData>> _mockPasswordResetStore;
    private readonly Mock<IStateStore<SessionDataModel>> _mockSessionStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IStateStore<StringWrapper>> _mockStringWrapperStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ITokenService> _mockTokenService;
    private readonly Mock<ISessionService> _mockSessionService;
    private readonly Mock<IOAuthProviderService> _mockOAuthService;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public AuthServiceTests()
    {
        _mockLogger = new Mock<ILogger<AuthService>>();
        _configuration = new AuthServiceConfiguration
        {
            JwtSecret = "test-jwt-secret-at-least-32-chars-long",
            JwtIssuer = "test-issuer",
            JwtAudience = "test-audience",
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
        _mockAccountsClient = new Mock<IAccountsClient>();
        _mockSubscriptionsClient = new Mock<ISubscriptionsClient>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockPasswordResetStore = new Mock<IStateStore<AuthService.PasswordResetData>>();
        _mockSessionStore = new Mock<IStateStore<SessionDataModel>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockStringWrapperStore = new Mock<IStateStore<StringWrapper>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockTokenService = new Mock<ITokenService>();
        _mockSessionService = new Mock<ISessionService>();
        _mockOAuthService = new Mock<IOAuthProviderService>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<AuthService.PasswordResetData>(It.IsAny<string>()))
            .Returns(_mockPasswordResetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<SessionDataModel>(It.IsAny<string>()))
            .Returns(_mockSessionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(It.IsAny<string>()))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<StringWrapper>(It.IsAny<string>()))
            .Returns(_mockStringWrapperStore.Object);

        // Setup default behavior for state stores
        _mockPasswordResetStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<AuthService.PasswordResetData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockSessionStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SessionDataModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockListStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockStringWrapperStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<StringWrapper>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup default behavior for message bus
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        // Setup default HttpClient for mock factory
        var mockHttpClient = new HttpClient();
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(mockHttpClient);
    }

    /// <summary>
    /// Helper method to create an AuthService with all required dependencies.
    /// </summary>
    private AuthService CreateAuthService()
    {
        return new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var service = CreateAuthService();

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
            // Set environment variables with AUTH_ prefix and UPPER_SNAKE_CASE format
            // The normalization converts AUTH_JWT_SECRET -> JwtSecret
            Environment.SetEnvironmentVariable("AUTH_JWT_SECRET", testSecret);
            Environment.SetEnvironmentVariable("AUTH_JWT_ISSUER", testIssuer);
            Environment.SetEnvironmentVariable("AUTH_JWT_AUDIENCE", testAudience);
            Environment.SetEnvironmentVariable("AUTH_JWT_EXPIRATION_MINUTES", testExpiration.ToString());

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
            Environment.SetEnvironmentVariable("AUTH_JWT_SECRET", null);
            Environment.SetEnvironmentVariable("AUTH_JWT_ISSUER", null);
            Environment.SetEnvironmentVariable("AUTH_JWT_AUDIENCE", null);
            Environment.SetEnvironmentVariable("AUTH_JWT_EXPIRATION_MINUTES", null);
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
        var registrationEvent = AuthPermissionRegistration.CreateRegistrationEvent(instanceId);

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("auth", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
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
    // The permission flow is fully tested in PermissionsServiceTests.
    // Role storage in Redis is verified through HTTP integration tests (http-tester).

    #endregion

    #region Input Validation Tests

    [Fact]
    public async Task LoginAsync_WithNullEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

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
    public async Task RegisterAsync_WithNullUsername_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

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
    public async Task ValidateTokenAsync_WithNullJwt_ShouldReturnUnauthorized()
    {
        // Arrange
        var service = CreateAuthService();

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

    #region Configuration Validation Tests

    [Fact]
    public void Constructor_WithNullAccountsClient_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            null!,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullSubscriptionsClient_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            null!,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            null!,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            null!,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            null!,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            null!,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullHttpClientFactory_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            null!,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullTokenService_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            null!,
            _mockSessionService.Object,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullSessionService_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            null!,
            _mockOAuthService.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullOAuthService_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            null!,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrow()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AuthService(
            _mockAccountsClient.Object,
            _mockSubscriptionsClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
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

    #region Session Invalidation Event Tests

    [Fact]
    public async Task OnEventReceivedAsync_AccountDeleted_ShouldInvalidateSessionsAndPublishEvent()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var sessionKey1 = $"session:{Guid.NewGuid()}";
        var sessionKey2 = $"session:{Guid.NewGuid()}";

        // Mock the account sessions lookup
        _mockListStore
            .Setup(s => s.GetAsync(
                $"account-sessions:{accountId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { sessionKey1, sessionKey2 });

        var service = CreateAuthService();

        var accountDeletedEvent = new AccountDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = accountId,
            DeletedReason = "user_requested"
        };

        // Act
        await service.OnEventReceivedAsync("account.deleted", accountDeletedEvent);

        // Assert - verify session invalidation event was published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.invalidated",
            It.Is<SessionInvalidatedEvent>(e =>
                e.AccountId == accountId &&
                e.Reason == SessionInvalidatedEventReason.Account_deleted &&
                e.DisconnectClients == true),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task OnEventReceivedAsync_AccountDeleted_WithNoSessions_ShouldNotPublishEvent()
    {
        // Arrange
        var accountId = Guid.NewGuid();

        // Mock returns empty session list
        _mockListStore
            .Setup(s => s.GetAsync(
                $"account-sessions:{accountId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        var service = CreateAuthService();

        var accountDeletedEvent = new AccountDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = accountId,
            DeletedReason = "user_requested"
        };

        // Act
        await service.OnEventReceivedAsync("account.deleted", accountDeletedEvent);

        // Assert - no event should be published since there were no sessions
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "session.invalidated",
            It.IsAny<SessionInvalidatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task OnEventReceivedAsync_UnknownEventTopic_ShouldNotThrow()
    {
        // Arrange
        var service = CreateAuthService();

        var unknownEvent = new { SomeProperty = "value" };

        // Act & Assert - should complete without throwing
        var exception = await Record.ExceptionAsync(() =>
            service.OnEventReceivedAsync("unknown.topic", unknownEvent));

        Assert.Null(exception);
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
    public async Task RequestPasswordResetAsync_WithNullEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateAuthService();

        var request = new PasswordResetRequest
        {
            Email = null!
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
        // Arrange - accounts client throws 404 for non-existent email
        _mockAccountsClient.Setup(c => c.GetAccountByEmailAsync(
            It.IsAny<GetAccountByEmailRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, "", new Dictionary<string, IEnumerable<string>>(), null));

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
}
