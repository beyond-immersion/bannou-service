using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
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
    private readonly Mock<IAccountClient> _mockAccountClient;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<OAuthProviderService>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly OAuthProviderService _service;

    public OAuthProviderServiceTests()
    {
        // Configure JWT settings in Program.Configuration (used by auth services)
        TestConfigurationHelper.ConfigureJwt();

        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockAccountClient = new Mock<IAccountClient>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
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

        // Setup state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);

        // Setup default HttpClient
        _httpClient = new HttpClient();
        _mockHttpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(_httpClient);

        _service = new OAuthProviderService(
            _mockStateStoreFactory.Object,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            _configuration,
            _mockMessageBus.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
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
        Assert.NotNull(result);
        Assert.Equal(existingAccountId, result.AccountId);
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
        Assert.NotNull(result);
        _mockAccountClient.Verify(c => c.CreateAccountAsync(
            It.Is<CreateAccountRequest>(r => r.Email == "new@example.com"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
