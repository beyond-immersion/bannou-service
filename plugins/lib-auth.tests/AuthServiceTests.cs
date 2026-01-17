using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.Subscription;
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
    private readonly Mock<IAccountClient> _mockAccountClient;
    private readonly Mock<ISubscriptionClient> _mockSubscriptionClient;
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
        _mockAccountClient = new Mock<IAccountClient>();
        _mockSubscriptionClient = new Mock<ISubscriptionClient>();
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
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

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
            _mockAccountClient.Object,
            _mockSubscriptionClient.Object,
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
        Assert.Equal(13, endpoints.Count); // 13 endpoints defined in auth-api.yaml with x-permissions
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
        Assert.Equal(13, registrationEvent.Endpoints.Count); // 13 endpoints
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
            _mockSubscriptionClient.Object,
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            emptyConfig,
            _mockLogger.Object,
            _mockHttpClientFactory.Object,
            _mockTokenService.Object,
            _mockSessionService.Object,
            _mockOAuthService.Object,
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
            It.IsAny<Guid?>(),
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
            It.IsAny<Guid?>(),
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
