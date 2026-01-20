using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Account;
using BeyondImmersion.BannouService.Achievement;
using BeyondImmersion.BannouService.Achievement.Sync;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;

namespace BeyondImmersion.BannouService.Achievement.Tests;

/// <summary>
/// Unit tests for SteamAchievementSync.
/// Tests focus on account linking queries and Steam Web API call handling.
/// </summary>
public class SteamAchievementSyncTests : IDisposable
{
    private readonly Mock<IAccountClient> _mockAccountClient;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly Mock<ILogger<SteamAchievementSync>> _mockLogger;
    private readonly AchievementServiceConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public SteamAchievementSyncTests()
    {
        _mockAccountClient = new Mock<IAccountClient>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _mockLogger = new Mock<ILogger<SteamAchievementSync>>();

        _configuration = new AchievementServiceConfiguration
        {
            SteamApiKey = "test-api-key",
            SteamAppId = "480", // Spacewar test app
            MockPlatformSync = false
        };

        // Create HttpClient that will be disposed in Dispose()
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _mockHttpClientFactory
            .Setup(f => f.CreateClient("SteamApi"))
            .Returns(_httpClient);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private SteamAchievementSync CreateSync()
    {
        return new SteamAchievementSync(
            _configuration,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);
    }

    private void SetupHttpResponse(HttpStatusCode statusCode, string content)
    {
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
    }

    private static IReadOnlyDictionary<string, IEnumerable<string>> EmptyHeaders
        => new Dictionary<string, IEnumerable<string>>();

    #region Constructor Tests

    [Fact]
    public void Constructor_ThrowsOnNullConfiguration()
    {
        Assert.Throws<ArgumentNullException>(() => new SteamAchievementSync(
            null!,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullAccountClient()
    {
        Assert.Throws<ArgumentNullException>(() => new SteamAchievementSync(
            _configuration,
            null!,
            _mockHttpClientFactory.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullHttpClientFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new SteamAchievementSync(
            _configuration,
            _mockAccountClient.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_ThrowsOnNullLogger()
    {
        Assert.Throws<ArgumentNullException>(() => new SteamAchievementSync(
            _configuration,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            null!));
    }

    [Fact]
    public void Platform_ReturnsSteam()
    {
        var sync = CreateSync();
        Assert.Equal(Platform.Steam, sync.Platform);
    }

    #endregion

    #region IsLinkedAsync Tests

    [Fact]
    public async Task IsLinkedAsync_WithSteamAuthMethod_ReturnsTrue()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient
            .Setup(c => c.GetAuthMethodsAsync(
                It.Is<GetAuthMethodsRequest>(r => r.AccountId == accountId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthMethodsResponse
            {
                AuthMethods = new List<AuthMethodInfo>
                {
                    new AuthMethodInfo
                    {
                        Provider = AuthProvider.Steam,
                        ExternalId = "76561198012345678",
                        LinkedAt = DateTimeOffset.UtcNow
                    }
                }
            });

        var sync = CreateSync();

        // Act
        var result = await sync.IsLinkedAsync(accountId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsLinkedAsync_WithoutSteamAuthMethod_ReturnsFalse()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient
            .Setup(c => c.GetAuthMethodsAsync(
                It.Is<GetAuthMethodsRequest>(r => r.AccountId == accountId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthMethodsResponse
            {
                AuthMethods = new List<AuthMethodInfo>
                {
                    new AuthMethodInfo
                    {
                        Provider = AuthProvider.Google,
                        ExternalId = "google-id",
                        LinkedAt = DateTimeOffset.UtcNow
                    }
                }
            });

        var sync = CreateSync();

        // Act
        var result = await sync.IsLinkedAsync(accountId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsLinkedAsync_AccountNotFound_ReturnsFalse()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient
            .Setup(c => c.GetAuthMethodsAsync(
                It.IsAny<GetAuthMethodsRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Account not found", 404, string.Empty, EmptyHeaders, null));

        var sync = CreateSync();

        // Act
        var result = await sync.IsLinkedAsync(accountId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsLinkedAsync_AccountServiceError_ReturnsFalse()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient
            .Setup(c => c.GetAuthMethodsAsync(
                It.IsAny<GetAuthMethodsRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Service unavailable"));

        var sync = CreateSync();

        // Act
        var result = await sync.IsLinkedAsync(accountId);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetExternalIdAsync Tests

    [Fact]
    public async Task GetExternalIdAsync_WithSteamAuthMethod_ReturnsSteamId()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var expectedSteamId = "76561198012345678";
        _mockAccountClient
            .Setup(c => c.GetAuthMethodsAsync(
                It.Is<GetAuthMethodsRequest>(r => r.AccountId == accountId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthMethodsResponse
            {
                AuthMethods = new List<AuthMethodInfo>
                {
                    new AuthMethodInfo
                    {
                        Provider = AuthProvider.Steam,
                        ExternalId = expectedSteamId,
                        LinkedAt = DateTimeOffset.UtcNow
                    }
                }
            });

        var sync = CreateSync();

        // Act
        var result = await sync.GetExternalIdAsync(accountId);

        // Assert
        Assert.Equal(expectedSteamId, result);
    }

    [Fact]
    public async Task GetExternalIdAsync_WithoutSteamAuthMethod_ReturnsNull()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient
            .Setup(c => c.GetAuthMethodsAsync(
                It.Is<GetAuthMethodsRequest>(r => r.AccountId == accountId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthMethodsResponse
            {
                AuthMethods = new List<AuthMethodInfo>
                {
                    new AuthMethodInfo
                    {
                        Provider = AuthProvider.Discord,
                        ExternalId = "discord-id",
                        LinkedAt = DateTimeOffset.UtcNow
                    }
                }
            });

        var sync = CreateSync();

        // Act
        var result = await sync.GetExternalIdAsync(accountId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetExternalIdAsync_SteamMethodWithEmptyExternalId_ReturnsNull()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient
            .Setup(c => c.GetAuthMethodsAsync(
                It.Is<GetAuthMethodsRequest>(r => r.AccountId == accountId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AuthMethodsResponse
            {
                AuthMethods = new List<AuthMethodInfo>
                {
                    new AuthMethodInfo
                    {
                        Provider = AuthProvider.Steam,
                        ExternalId = string.Empty, // Empty ID
                        LinkedAt = DateTimeOffset.UtcNow
                    }
                }
            });

        var sync = CreateSync();

        // Act
        var result = await sync.GetExternalIdAsync(accountId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetExternalIdAsync_AccountNotFound_ReturnsNull()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountClient
            .Setup(c => c.GetAuthMethodsAsync(
                It.IsAny<GetAuthMethodsRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Account not found", 404, string.Empty, EmptyHeaders, null));

        var sync = CreateSync();

        // Act
        var result = await sync.GetExternalIdAsync(accountId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region UnlockAsync Configuration Validation Tests

    [Fact]
    public async Task UnlockAsync_MissingSteamApiKey_ReturnsFailure()
    {
        // Arrange
        var config = new AchievementServiceConfiguration
        {
            SteamApiKey = string.Empty,
            SteamAppId = "480"
        };

        var sync = new SteamAchievementSync(
            config,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Steam API key not configured", result.ErrorMessage);
    }

    [Fact]
    public async Task UnlockAsync_MissingSteamAppId_ReturnsFailure()
    {
        // Arrange
        var config = new AchievementServiceConfiguration
        {
            SteamApiKey = "test-key",
            SteamAppId = string.Empty
        };

        var sync = new SteamAchievementSync(
            config,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Steam App ID not configured", result.ErrorMessage);
    }

    #endregion

    #region UnlockAsync Mock Mode Tests

    [Fact]
    public async Task UnlockAsync_MockModeEnabled_ReturnsSuccessWithoutApiCall()
    {
        // Arrange
        _configuration.MockPlatformSync = true;
        var sync = CreateSync();

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.True(result.Success);
        Assert.StartsWith("mock-", result.SyncId);

        // Verify no HTTP call was made
        _mockHttpMessageHandler
            .Protected()
            .Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region UnlockAsync API Call Tests

    [Fact]
    public async Task UnlockAsync_SuccessfulApiCall_ReturnsSuccess()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, """{"response":{"result":1}}""");
        var sync = CreateSync();

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.True(result.Success);
        Assert.StartsWith("steam-", result.SyncId);
    }

    [Fact]
    public async Task UnlockAsync_ApiReturnsError_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, """{"response":{"result":2,"error":"Achievement not found"}}""");
        var sync = CreateSync();

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "INVALID_ACHIEVEMENT");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Achievement not found", result.ErrorMessage);
    }

    [Fact]
    public async Task UnlockAsync_RateLimited_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.TooManyRequests, "Rate limited");
        var sync = CreateSync();

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("rate limit", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnlockAsync_HttpError_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.InternalServerError, "Internal Server Error");
        var sync = CreateSync();

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("500", result.ErrorMessage);
    }

    [Fact]
    public async Task UnlockAsync_MalformedResponse_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, "not json");
        var sync = CreateSync();

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("parse", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnlockAsync_MissingResponseField_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, """{"other":"data"}""");
        var sync = CreateSync();

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("response", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnlockAsync_MissingResultField_ReturnsFailure()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, """{"response":{"other":"data"}}""");
        var sync = CreateSync();

        // Act
        var result = await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.False(result.Success);
        Assert.Contains("result", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region SetProgressAsync Tests

    [Fact]
    public async Task SetProgressAsync_MockModeEnabled_ReturnsSuccessWithoutApiCall()
    {
        // Arrange
        _configuration.MockPlatformSync = true;
        var sync = CreateSync();

        // Act
        var result = await sync.SetProgressAsync("76561198012345678", "STAT_KILLS", 50, 100);

        // Assert
        Assert.True(result.Success);
        Assert.StartsWith("mock-", result.SyncId);

        // Verify no HTTP call was made
        _mockHttpMessageHandler
            .Protected()
            .Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SetProgressAsync_SuccessfulApiCall_ReturnsSuccess()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, """{"response":{"result":1}}""");
        var sync = CreateSync();

        // Act
        var result = await sync.SetProgressAsync("76561198012345678", "STAT_KILLS", 50, 100);

        // Assert
        Assert.True(result.Success);
        Assert.StartsWith("steam-", result.SyncId);
    }

    [Fact]
    public async Task SetProgressAsync_MissingSteamApiKey_ReturnsFailure()
    {
        // Arrange
        var config = new AchievementServiceConfiguration
        {
            SteamApiKey = string.Empty,
            SteamAppId = "480"
        };

        var sync = new SteamAchievementSync(
            config,
            _mockAccountClient.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);

        // Act
        var result = await sync.SetProgressAsync("76561198012345678", "STAT_KILLS", 50, 100);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Steam API key not configured", result.ErrorMessage);
    }

    #endregion

    #region HTTP Request Verification Tests

    [Fact]
    public async Task UnlockAsync_SendsCorrectRequestParameters()
    {
        // Arrange
        var capturedMethod = HttpMethod.Get;
        var capturedUri = string.Empty;
        var capturedContent = string.Empty;
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                capturedMethod = req.Method;
                capturedUri = req.RequestUri?.ToString() ?? string.Empty;
                // Read content before it gets disposed by the caller
                if (req.Content is not null)
                {
                    capturedContent = await req.Content.ReadAsStringAsync();
                }
            })
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("""{"response":{"result":1}}""", Encoding.UTF8, "application/json")
            });

        var sync = CreateSync();

        // Act
        await sync.UnlockAsync("76561198012345678", "TEST_ACHIEVEMENT");

        // Assert
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Contains("partner.steam-api.com", capturedUri);
        Assert.Contains("ISteamUserStats/SetUserStatsForGame", capturedUri);

        // Verify form content
        Assert.Contains("key=test-api-key", capturedContent);
        Assert.Contains("steamid=76561198012345678", capturedContent);
        Assert.Contains("appid=480", capturedContent);
        Assert.Contains("name%5B0%5D=TEST_ACHIEVEMENT", capturedContent); // URL encoded name[0]
        Assert.Contains("value%5B0%5D=1", capturedContent); // URL encoded value[0]
    }

    [Fact]
    public async Task SetProgressAsync_SendsProgressValueInRequest()
    {
        // Arrange
        var capturedContent = string.Empty;
        _mockHttpMessageHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                // Read content before it gets disposed by the caller
                if (req.Content is not null)
                {
                    capturedContent = await req.Content.ReadAsStringAsync();
                }
            })
            .ReturnsAsync(() => new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("""{"response":{"result":1}}""", Encoding.UTF8, "application/json")
            });

        var sync = CreateSync();

        // Act
        await sync.SetProgressAsync("76561198012345678", "STAT_KILLS", 75, 100);

        // Assert
        Assert.Contains("value%5B0%5D=75", capturedContent); // URL encoded value[0]=75
    }

    #endregion
}
