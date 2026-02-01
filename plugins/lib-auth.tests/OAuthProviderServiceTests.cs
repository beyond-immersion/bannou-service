using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.Web;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for OAuthProviderService.
/// Tests OAuth provider integrations including Discord, Google, Twitch, and Steam.
/// </summary>
public class OAuthProviderServiceTests : IDisposable
{
    private const string STATE_STORE = "auth-statestore";

    private readonly HttpClient _httpClient;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IAccountClient> _mockAccountClient;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpMessageHandler> _mockHttpHandler;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OAuthProviderService>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly AppConfiguration _appConfiguration;
    private readonly OAuthProviderService _service;
    private readonly List<HttpClient> _createdHttpClients = new();
    private readonly List<HttpResponseMessage> _createdResponses = new();

    public OAuthProviderServiceTests()
    {
        // Configure JWT settings in Program.Configuration (used by auth services)
        TestConfigurationHelper.ConfigureJwt();

        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockAccountClient = new Mock<IAccountClient>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpHandler = new Mock<HttpMessageHandler>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<OAuthProviderService>>();

        _configuration = new AuthServiceConfiguration
        {
            JwtExpirationMinutes = 60,
            MockProviders = false,
            // Discord configuration
            DiscordClientId = "test-discord-client-id",
            DiscordClientSecret = "test-discord-client-secret",
            DiscordRedirectUri = "http://localhost:5012/auth/oauth/discord/callback",
            // Google configuration
            GoogleClientId = "test-google-client-id",
            GoogleClientSecret = "test-google-client-secret",
            GoogleRedirectUri = "http://localhost:5012/auth/oauth/google/callback",
            // Twitch configuration
            TwitchClientId = "test-twitch-client-id",
            TwitchClientSecret = "test-twitch-client-secret",
            TwitchRedirectUri = "http://localhost:5012/auth/oauth/twitch/callback",
            // Steam configuration
            SteamApiKey = "test-steam-api-key",
            SteamAppId = "123456"
        };
        _appConfiguration = new AppConfiguration
        {
            ServiceDomain = "localhost"
        };

        // Setup state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockListStore.Object);

        // Setup default HttpClient
        _httpClient = new HttpClient();
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);

        _service = new OAuthProviderService(
            _mockStateStoreFactory.Object,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            _configuration,
            _appConfiguration,
            _mockMessageBus.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();

        foreach (var client in _createdHttpClients)
        {
            client.Dispose();
        }
        _createdHttpClients.Clear();

        foreach (var response in _createdResponses)
        {
            response.Dispose();
        }
        _createdResponses.Clear();

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Creates an HttpClient with a mocked handler that returns the specified response.
    /// </summary>
    private HttpClient CreateMockedHttpClient(HttpStatusCode statusCode, string? content = null)
    {
        var response = new HttpResponseMessage(statusCode);
        _createdResponses.Add(response);

        if (content != null)
        {
            response.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(_mockHttpHandler.Object);
        _createdHttpClients.Add(httpClient);
        return httpClient;
    }

    /// <summary>
    /// Creates an HttpClient that throws when SendAsync is called.
    /// </summary>
    private HttpClient CreateThrowingHttpClient(Exception exception)
    {
        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);

        var httpClient = new HttpClient(_mockHttpHandler.Object);
        _createdHttpClients.Add(httpClient);
        return httpClient;
    }

    /// <summary>
    /// Creates an OAuthProviderService with the specified HttpClient for testing.
    /// </summary>
    private OAuthProviderService CreateServiceWithMockedHttp(HttpClient httpClient, AuthServiceConfiguration? configOverride = null)
    {
        var mockFactory = new Mock<IHttpClientFactory>();
        mockFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        return new OAuthProviderService(
            _mockStateStoreFactory.Object,
            _mockAccountClient.Object,
            mockFactory.Object,
            configOverride ?? _configuration,
            _appConfiguration,
            _mockMessageBus.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Helper class providing Steam Web API response templates for testing.
    /// </summary>
    private static class SteamTestResponses
    {
        /// <summary>
        /// Returns a successful Steam ticket validation response.
        /// </summary>
        public static string Success(string steamId = "76561198012345678") => $$"""
            {
                "response": {
                    "params": {
                        "result": "OK",
                        "steamid": "{{steamId}}",
                        "ownersteamid": "{{steamId}}",
                        "vacbanned": false,
                        "publisherbanned": false
                    }
                }
            }
            """;

        /// <summary>
        /// Returns a response for a VAC-banned user.
        /// </summary>
        public static string VacBanned(string steamId = "76561198012345678") => $$"""
            {
                "response": {
                    "params": {
                        "result": "OK",
                        "steamid": "{{steamId}}",
                        "ownersteamid": "{{steamId}}",
                        "vacbanned": true,
                        "publisherbanned": false
                    }
                }
            }
            """;

        /// <summary>
        /// Returns a response for a publisher-banned user.
        /// </summary>
        public static string PublisherBanned(string steamId = "76561198012345678") => $$"""
            {
                "response": {
                    "params": {
                        "result": "OK",
                        "steamid": "{{steamId}}",
                        "ownersteamid": "{{steamId}}",
                        "vacbanned": false,
                        "publisherbanned": true
                    }
                }
            }
            """;

        /// <summary>
        /// Returns a Steam API error response.
        /// </summary>
        public static string ApiError(int errorCode, string errorDesc) => $$"""
            {
                "response": {
                    "error": {
                        "errorcode": {{errorCode}},
                        "errordesc": "{{errorDesc}}"
                    }
                }
            }
            """;

        /// <summary>
        /// Returns a response with an invalid (non-"OK") result.
        /// </summary>
        public static string InvalidResult() => """
            {
                "response": {
                    "params": {
                        "result": "FAIL",
                        "steamid": "",
                        "ownersteamid": "",
                        "vacbanned": false,
                        "publisherbanned": false
                    }
                }
            }
            """;

        /// <summary>
        /// Returns an empty/malformed response.
        /// </summary>
        public static string MalformedResponse() => """
            {
                "response": {}
            }
            """;
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<OAuthProviderService>();
        Assert.NotNull(_service);
    }

    #endregion

    #region IsMockEnabled Tests

    [Fact]
    public void IsMockEnabled_WhenConfigurationSaysTrue_ShouldReturnTrue()
    {
        // Arrange
        var configWithMock = new AuthServiceConfiguration { MockProviders = true };
        var service = new OAuthProviderService(
            _mockStateStoreFactory.Object,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            configWithMock,
            _appConfiguration,
            _mockMessageBus.Object,
            _mockLogger.Object);

        // Act & Assert
        Assert.True(service.IsMockEnabled);
    }

    [Fact]
    public void IsMockEnabled_WhenConfigurationSaysFalse_ShouldReturnFalse()
    {
        // Assert - Default configuration has MockProviders = false
        Assert.False(_service.IsMockEnabled);
    }

    #endregion

    #region GetAuthorizationUrl Tests

    [Fact]
    public void GetAuthorizationUrl_ForDiscord_ShouldReturnValidUrl()
    {
        // Act
        var url = _service.GetAuthorizationUrl(Provider.Discord, null, "test-state");

        // Assert
        Assert.NotNull(url);

        // Parse and validate URL structure
        var uri = new Uri(url);
        Assert.Equal("discord.com", uri.Host);
        Assert.Equal("/oauth2/authorize", uri.AbsolutePath);

        // Parse and validate query parameters
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("test-discord-client-id", queryParams["client_id"]);
        Assert.Equal("code", queryParams["response_type"]);
        Assert.Equal("test-state", queryParams["state"]);
    }

    [Fact]
    public void GetAuthorizationUrl_ForGoogle_ShouldReturnValidUrl()
    {
        // Act
        var url = _service.GetAuthorizationUrl(Provider.Google, null, "test-state");

        // Assert
        Assert.NotNull(url);

        // Parse and validate URL structure
        var uri = new Uri(url);
        Assert.Equal("accounts.google.com", uri.Host);
        Assert.Equal("/o/oauth2/v2/auth", uri.AbsolutePath);

        // Parse and validate query parameters
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("test-google-client-id", queryParams["client_id"]);
        Assert.Equal("code", queryParams["response_type"]);
        Assert.Equal("test-state", queryParams["state"]);
    }

    [Fact]
    public void GetAuthorizationUrl_ForTwitch_ShouldReturnValidUrl()
    {
        // Act
        var url = _service.GetAuthorizationUrl(Provider.Twitch, null, "test-state");

        // Assert
        Assert.NotNull(url);

        // Parse and validate URL structure
        var uri = new Uri(url);
        Assert.Equal("id.twitch.tv", uri.Host);
        Assert.Equal("/oauth2/authorize", uri.AbsolutePath);

        // Parse and validate query parameters
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("test-twitch-client-id", queryParams["client_id"]);
        Assert.Equal("code", queryParams["response_type"]);
        Assert.Equal("test-state", queryParams["state"]);
    }

    [Fact]
    public void GetAuthorizationUrl_WithCustomRedirectUri_ShouldUseIt()
    {
        // Arrange
        var customRedirectUri = "http://custom.example.com/callback";

        // Act
        var url = _service.GetAuthorizationUrl(Provider.Discord, customRedirectUri, null);

        // Assert
        Assert.NotNull(url);

        // Parse and validate the redirect_uri query parameter
        var uri = new Uri(url);
        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal(customRedirectUri, queryParams["redirect_uri"]);
    }

    [Fact]
    public void GetAuthorizationUrl_WithoutState_ShouldStillReturnUrl()
    {
        // Act
        var url = _service.GetAuthorizationUrl(Provider.Discord, null, null);

        // Assert
        Assert.NotNull(url);

        // Verify URL is still valid and well-formed
        var uri = new Uri(url);
        Assert.Equal("discord.com", uri.Host);
        Assert.Equal("/oauth2/authorize", uri.AbsolutePath);
    }

    #endregion

    #region GetMockUserInfoAsync Tests

    [Fact]
    public async Task GetMockUserInfoAsync_ForDiscord_ShouldReturnMockInfo()
    {
        // Act
        var result = await _service.GetMockUserInfoAsync(Provider.Discord);

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.ProviderId));
        Assert.False(string.IsNullOrEmpty(result.Email));
        Assert.False(string.IsNullOrEmpty(result.DisplayName));
    }

    [Fact]
    public async Task GetMockUserInfoAsync_ForGoogle_ShouldReturnMockInfo()
    {
        // Act
        var result = await _service.GetMockUserInfoAsync(Provider.Google);

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.ProviderId));
        Assert.False(string.IsNullOrEmpty(result.Email));
    }

    [Fact]
    public async Task GetMockUserInfoAsync_ForTwitch_ShouldReturnMockInfo()
    {
        // Act
        var result = await _service.GetMockUserInfoAsync(Provider.Twitch);

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.ProviderId));
        Assert.False(string.IsNullOrEmpty(result.DisplayName));
    }

    #endregion

    #region GetMockSteamUserInfoAsync Tests

    [Fact]
    public async Task GetMockSteamUserInfoAsync_ShouldReturnMockInfo()
    {
        // Act
        var result = await _service.GetMockSteamUserInfoAsync();

        // Assert
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.ProviderId));
        Assert.False(string.IsNullOrEmpty(result.DisplayName));
    }

    #endregion

    #region FindOrCreateOAuthAccountAsync Tests

    [Fact]
    public async Task FindOrCreateOAuthAccountAsync_WithExistingLink_ShouldReturnLinkedAccount()
    {
        // Arrange
        var userInfo = new OAuthUserInfo
        {
            ProviderId = "test-provider-id",
            Email = "test@example.com",
            DisplayName = "Test User"
        };

        var existingAccountId = Guid.NewGuid();
        var existingAccount = new AccountResponse
        {
            AccountId = existingAccountId,
            Email = "test@example.com",
            DisplayName = "Test User"
        };

        // OAuth link exists in state store
        var oauthLinkKey = "oauth-link:discord:test-provider-id";
        _mockStringStore.Setup(s => s.GetAsync(oauthLinkKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccountId.ToString());

        // Account exists
        _mockAccountClient.Setup(c => c.GetAccountAsync(
            It.Is<GetAccountRequest>(r => r.AccountId == existingAccountId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);

        // Act
        var result = await _service.FindOrCreateOAuthAccountAsync(Provider.Discord, userInfo, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Account);
        Assert.False(result.IsNewAccount);
        Assert.Equal(existingAccountId, result.Account.AccountId);
    }

    [Fact]
    public async Task FindOrCreateOAuthAccountAsync_WithNoExistingLink_ShouldCreateNewAccount()
    {
        // Arrange
        var userInfo = new OAuthUserInfo
        {
            ProviderId = "new-provider-id",
            Email = "new@example.com",
            DisplayName = "New User"
        };

        var newAccount = new AccountResponse
        {
            AccountId = Guid.NewGuid(),
            Email = "new@example.com",
            DisplayName = "New User"
        };

        // No OAuth link in state store
        var oauthLinkKey = "oauth-link:discord:new-provider-id";
        _mockStringStore.Setup(s => s.GetAsync(oauthLinkKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Create account succeeds
        _mockAccountClient.Setup(c => c.CreateAccountAsync(
            It.IsAny<CreateAccountRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAccount);

        // State store saves the link
        _mockStringStore.Setup(s => s.SaveAsync(
            oauthLinkKey,
            It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        var result = await _service.FindOrCreateOAuthAccountAsync(Provider.Discord, userInfo, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Account);
        Assert.True(result.IsNewAccount);
        _mockAccountClient.Verify(c => c.CreateAccountAsync(
            It.Is<CreateAccountRequest>(r => r.Email == "new@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindOrCreateOAuthAccountAsync_WithExistingLink_ShouldSyncAuthMethod()
    {
        // Arrange
        var userInfo = new OAuthUserInfo
        {
            ProviderId = "sync-provider-id",
            Email = "sync@example.com",
            DisplayName = "Sync User"
        };

        var existingAccountId = Guid.NewGuid();
        var existingAccount = new AccountResponse
        {
            AccountId = existingAccountId,
            Email = "sync@example.com",
            DisplayName = "Sync User"
        };

        // OAuth link exists in state store
        var oauthLinkKey = "oauth-link:discord:sync-provider-id";
        _mockStringStore.Setup(s => s.GetAsync(oauthLinkKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccountId.ToString());

        // Account exists
        _mockAccountClient.Setup(c => c.GetAccountAsync(
            It.Is<GetAccountRequest>(r => r.AccountId == existingAccountId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);

        // AddAuthMethod succeeds
        _mockAccountClient.Setup(c => c.AddAuthMethodAsync(
            It.IsAny<AddAuthMethodRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthMethodResponse());

        // Act
        await _service.FindOrCreateOAuthAccountAsync(Provider.Discord, userInfo, CancellationToken.None);

        // Assert - verify AddAuthMethodAsync was called with correct parameters
        _mockAccountClient.Verify(c => c.AddAuthMethodAsync(
            It.Is<AddAuthMethodRequest>(r =>
                r.AccountId == existingAccountId &&
                r.Provider == OAuthProvider.Discord &&
                r.ExternalId == "sync-provider-id"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindOrCreateOAuthAccountAsync_WithNewAccount_ShouldSyncAuthMethod()
    {
        // Arrange
        var userInfo = new OAuthUserInfo
        {
            ProviderId = "new-sync-provider-id",
            Email = "newsync@example.com",
            DisplayName = "New Sync User"
        };

        var newAccountId = Guid.NewGuid();
        var newAccount = new AccountResponse
        {
            AccountId = newAccountId,
            Email = "newsync@example.com",
            DisplayName = "New Sync User"
        };

        // No existing OAuth link
        var oauthLinkKey = "oauth-link:google:new-sync-provider-id";
        _mockStringStore.Setup(s => s.GetAsync(oauthLinkKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Create account succeeds
        _mockAccountClient.Setup(c => c.CreateAccountAsync(
            It.IsAny<CreateAccountRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(newAccount);

        // State store saves the link
        _mockStringStore.Setup(s => s.SaveAsync(
            oauthLinkKey,
            It.IsAny<string>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // AddAuthMethod succeeds
        _mockAccountClient.Setup(c => c.AddAuthMethodAsync(
            It.IsAny<AddAuthMethodRequest>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthMethodResponse());

        // Act
        var result = await _service.FindOrCreateOAuthAccountAsync(Provider.Google, userInfo, CancellationToken.None);

        // Assert - verify AddAuthMethodAsync was called with correct parameters
        Assert.NotNull(result.Account);
        _mockAccountClient.Verify(c => c.AddAuthMethodAsync(
            It.Is<AddAuthMethodRequest>(r =>
                r.AccountId == newAccountId &&
                r.Provider == OAuthProvider.Google &&
                r.ExternalId == "new-sync-provider-id"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindOrCreateOAuthAccountAsync_AuthMethodSync409_ShouldStillSucceed()
    {
        // Arrange - auth method already exists (idempotent case on repeat login)
        var userInfo = new OAuthUserInfo
        {
            ProviderId = "repeat-login-id",
            Email = "repeat@example.com",
            DisplayName = "Repeat User"
        };

        var existingAccountId = Guid.NewGuid();
        var existingAccount = new AccountResponse
        {
            AccountId = existingAccountId,
            Email = "repeat@example.com",
            DisplayName = "Repeat User"
        };

        var oauthLinkKey = "oauth-link:twitch:repeat-login-id";
        _mockStringStore.Setup(s => s.GetAsync(oauthLinkKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccountId.ToString());

        _mockAccountClient.Setup(c => c.GetAccountAsync(
            It.Is<GetAccountRequest>(r => r.AccountId == existingAccountId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);

        // AddAuthMethod returns 409 (already linked)
        _mockAccountClient.Setup(c => c.AddAuthMethodAsync(
            It.IsAny<AddAuthMethodRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Conflict", 409));

        // Act
        var result = await _service.FindOrCreateOAuthAccountAsync(Provider.Twitch, userInfo, CancellationToken.None);

        // Assert - method should succeed despite 409 from sync
        Assert.NotNull(result.Account);
        Assert.Equal(existingAccountId, result.Account.AccountId);
    }

    [Fact]
    public async Task FindOrCreateOAuthAccountAsync_AuthMethodSyncFailure_ShouldStillSucceed()
    {
        // Arrange - Account service unavailable for sync (best-effort)
        var userInfo = new OAuthUserInfo
        {
            ProviderId = "resilient-id",
            Email = "resilient@example.com",
            DisplayName = "Resilient User"
        };

        var existingAccountId = Guid.NewGuid();
        var existingAccount = new AccountResponse
        {
            AccountId = existingAccountId,
            Email = "resilient@example.com",
            DisplayName = "Resilient User"
        };

        var oauthLinkKey = "oauth-link:steam:resilient-id";
        _mockStringStore.Setup(s => s.GetAsync(oauthLinkKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccountId.ToString());

        _mockAccountClient.Setup(c => c.GetAccountAsync(
            It.Is<GetAccountRequest>(r => r.AccountId == existingAccountId),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAccount);

        // AddAuthMethod throws a general exception (service unavailable)
        _mockAccountClient.Setup(c => c.AddAuthMethodAsync(
            It.IsAny<AddAuthMethodRequest>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service unavailable"));

        // Act
        var result = await _service.FindOrCreateOAuthAccountAsync(Provider.Steam, userInfo, CancellationToken.None);

        // Assert - method should succeed despite sync failure (best-effort)
        Assert.NotNull(result.Account);
        Assert.Equal(existingAccountId, result.Account.AccountId);
    }

    #endregion

    #region ValidateSteamTicketAsync Tests

    [Fact]
    public async Task ValidateSteamTicketAsync_WithValidTicket_ReturnsSteamId()
    {
        // Arrange
        var expectedSteamId = "76561198012345678";
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, SteamTestResponses.Success(expectedSteamId));
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        var result = await service.ValidateSteamTicketAsync("valid-ticket-hex");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedSteamId, result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_WithVacBannedUser_ReturnsNull()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, SteamTestResponses.VacBanned());
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        var result = await service.ValidateSteamTicketAsync("vac-banned-ticket");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_WithPublisherBannedUser_ReturnsNull()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, SteamTestResponses.PublisherBanned());
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        var result = await service.ValidateSteamTicketAsync("publisher-banned-ticket");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_WithSteamApiError_ReturnsNull()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, SteamTestResponses.ApiError(102, "Ticket for other app"));
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        var result = await service.ValidateSteamTicketAsync("wrong-app-ticket");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_WithInvalidResult_ReturnsNull()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, SteamTestResponses.InvalidResult());
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        var result = await service.ValidateSteamTicketAsync("invalid-ticket");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_WithHttpFailure_ReturnsNull()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.InternalServerError, "Steam API unavailable");
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        var result = await service.ValidateSteamTicketAsync("some-ticket");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_WithNetworkException_ReturnsNullAndPublishesError()
    {
        // Arrange
        var httpClient = CreateThrowingHttpClient(new HttpRequestException("Connection refused"));
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        var result = await service.ValidateSteamTicketAsync("some-ticket");

        // Assert
        Assert.Null(result);

        // Verify error event was published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "auth",
            "ValidateSteamTicket",
            "HttpRequestException",
            It.Is<string>(msg => msg.Contains("Connection refused")),
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
    public async Task ValidateSteamTicketAsync_WithMissingSteamApiKey_ReturnsNull()
    {
        // Arrange
        var configWithoutKey = new AuthServiceConfiguration
        {
            SteamApiKey = null,
            SteamAppId = "123456"
        };
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, SteamTestResponses.Success());
        var service = CreateServiceWithMockedHttp(httpClient, configWithoutKey);

        // Act
        var result = await service.ValidateSteamTicketAsync("some-ticket");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_WithMissingSteamAppId_ReturnsNull()
    {
        // Arrange
        var configWithoutAppId = new AuthServiceConfiguration
        {
            SteamApiKey = "test-api-key",
            SteamAppId = null
        };
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, SteamTestResponses.Success());
        var service = CreateServiceWithMockedHttp(httpClient, configWithoutAppId);

        // Act
        var result = await service.ValidateSteamTicketAsync("some-ticket");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_WithMalformedResponse_ReturnsNull()
    {
        // Arrange
        var httpClient = CreateMockedHttpClient(HttpStatusCode.OK, SteamTestResponses.MalformedResponse());
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        var result = await service.ValidateSteamTicketAsync("some-ticket");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateSteamTicketAsync_ShouldCallCorrectSteamUrl()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        _createdResponses.Add(response);
        response.Content = new StringContent(SteamTestResponses.Success(), Encoding.UTF8, "application/json");

        _mockHttpHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(response);

        var httpClient = new HttpClient(_mockHttpHandler.Object);
        _createdHttpClients.Add(httpClient);
        var service = CreateServiceWithMockedHttp(httpClient);

        // Act
        await service.ValidateSteamTicketAsync("test-ticket-hex");

        // Assert
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Get, capturedRequest.Method);

        var requestUrl = capturedRequest.RequestUri?.ToString();
        Assert.NotNull(requestUrl);
        Assert.Contains("partner.steam-api.com", requestUrl);
        Assert.Contains("ISteamUserAuth/AuthenticateUserTicket", requestUrl);
        Assert.Contains("key=test-steam-api-key", requestUrl);
        Assert.Contains("appid=123456", requestUrl);
        Assert.Contains("ticket=test-ticket-hex", requestUrl);
    }

    #endregion
}
