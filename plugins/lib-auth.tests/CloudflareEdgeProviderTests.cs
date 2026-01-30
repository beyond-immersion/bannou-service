using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for CloudflareEdgeProvider.
/// Tests CloudFlare KV API integration for edge revocation.
/// </summary>
public class CloudflareEdgeProviderTests
{
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly Mock<ILogger<CloudflareEdgeProvider>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly CloudflareEdgeProvider _provider;

    public CloudflareEdgeProviderTests()
    {
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        _mockLogger = new Mock<ILogger<CloudflareEdgeProvider>>();

        _configuration = new AuthServiceConfiguration
        {
            CloudflareEdgeEnabled = true,
            CloudflareAccountId = "test-account-id",
            CloudflareKvNamespaceId = "test-namespace-id",
            CloudflareApiToken = "test-api-token"
        };

        // Setup HTTP client factory with mocked handler
        var httpClient = new HttpClient(_mockHttpMessageHandler.Object)
        {
            BaseAddress = new Uri("https://api.cloudflare.com")
        };
        _mockHttpClientFactory.Setup(f => f.CreateClient("cloudflare-kv")).Returns(httpClient);

        _provider = new CloudflareEdgeProvider(
            _configuration,
            _mockHttpClientFactory.Object,
            _mockLogger.Object);
    }

    #region ProviderId Tests

    [Fact]
    public void ProviderId_ShouldReturnCloudflare()
    {
        // Act
        var result = _provider.ProviderId;

        // Assert
        Assert.Equal("cloudflare", result);
    }

    #endregion

    #region IsEnabled Tests

    [Fact]
    public void IsEnabled_WhenAllConfigSet_ShouldReturnTrue()
    {
        // Act
        var result = _provider.IsEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsEnabled_WhenConfigDisabled_ShouldReturnFalse()
    {
        // Arrange
        var config = new AuthServiceConfiguration
        {
            CloudflareEdgeEnabled = false,
            CloudflareAccountId = "test",
            CloudflareKvNamespaceId = "test",
            CloudflareApiToken = "test"
        };
        var provider = new CloudflareEdgeProvider(config, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var result = provider.IsEnabled;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsEnabled_WhenMissingAccountId_ShouldReturnFalse()
    {
        // Arrange
        var config = new AuthServiceConfiguration
        {
            CloudflareEdgeEnabled = true,
            CloudflareAccountId = null,
            CloudflareKvNamespaceId = "test",
            CloudflareApiToken = "test"
        };
        var provider = new CloudflareEdgeProvider(config, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var result = provider.IsEnabled;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsEnabled_WhenMissingNamespaceId_ShouldReturnFalse()
    {
        // Arrange
        var config = new AuthServiceConfiguration
        {
            CloudflareEdgeEnabled = true,
            CloudflareAccountId = "test",
            CloudflareKvNamespaceId = null,
            CloudflareApiToken = "test"
        };
        var provider = new CloudflareEdgeProvider(config, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var result = provider.IsEnabled;

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsEnabled_WhenMissingApiToken_ShouldReturnFalse()
    {
        // Arrange
        var config = new AuthServiceConfiguration
        {
            CloudflareEdgeEnabled = true,
            CloudflareAccountId = "test",
            CloudflareKvNamespaceId = "test",
            CloudflareApiToken = null
        };
        var provider = new CloudflareEdgeProvider(config, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var result = provider.IsEnabled;

        // Assert
        Assert.False(result);
    }

    #endregion

    #region PushTokenRevocationAsync Tests

    [Fact]
    public async Task PushTokenRevocationAsync_WhenDisabled_ShouldReturnTrue()
    {
        // Arrange
        var config = new AuthServiceConfiguration { CloudflareEdgeEnabled = false };
        var provider = new CloudflareEdgeProvider(config, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var result = await provider.PushTokenRevocationAsync("test-jti", Guid.NewGuid(), TimeSpan.FromMinutes(60));

        // Assert
        Assert.True(result);
        // No HTTP calls should be made
        _mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task PushTokenRevocationAsync_WhenApiSucceeds_ShouldReturnTrue()
    {
        // Arrange
        var jti = "test-jti-123";
        var accountId = Guid.NewGuid();
        var ttl = TimeSpan.FromMinutes(60);

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Put &&
                    req.RequestUri != null &&
                    req.RequestUri.ToString().Contains($"token%3A{jti}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var result = await _provider.PushTokenRevocationAsync(jti, accountId, ttl);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PushTokenRevocationAsync_WhenApiFails_ShouldReturnFalse()
    {
        // Arrange
        var jti = "test-jti-123";

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Internal server error")
            });

        // Act
        var result = await _provider.PushTokenRevocationAsync(jti, Guid.NewGuid(), TimeSpan.FromMinutes(60));

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task PushTokenRevocationAsync_WhenException_ShouldReturnFalse()
    {
        // Arrange
        var jti = "test-jti-123";

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection failed"));

        // Act
        var result = await _provider.PushTokenRevocationAsync(jti, Guid.NewGuid(), TimeSpan.FromMinutes(60));

        // Assert
        Assert.False(result);
    }

    #endregion

    #region PushAccountRevocationAsync Tests

    [Fact]
    public async Task PushAccountRevocationAsync_WhenDisabled_ShouldReturnTrue()
    {
        // Arrange
        var config = new AuthServiceConfiguration { CloudflareEdgeEnabled = false };
        var provider = new CloudflareEdgeProvider(config, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Act
        var result = await provider.PushAccountRevocationAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task PushAccountRevocationAsync_WhenApiSucceeds_ShouldReturnTrue()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var issuedBefore = DateTimeOffset.UtcNow;

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Put &&
                    req.RequestUri != null &&
                    req.RequestUri.ToString().Contains($"account%3A{accountId}")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var result = await _provider.PushAccountRevocationAsync(accountId, issuedBefore);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region PushBatchAsync Tests

    [Fact]
    public async Task PushBatchAsync_WhenDisabled_ShouldReturnFullCount()
    {
        // Arrange
        var config = new AuthServiceConfiguration { CloudflareEdgeEnabled = false };
        var provider = new CloudflareEdgeProvider(config, _mockHttpClientFactory.Object, _mockLogger.Object);
        var entries = new List<FailedEdgePushEntry>
        {
            new FailedEdgePushEntry { Type = "token", Jti = "jti-1", TtlSeconds = 3600 },
            new FailedEdgePushEntry { Type = "token", Jti = "jti-2", TtlSeconds = 3600 }
        };

        // Act
        var result = await provider.PushBatchAsync(entries);

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task PushBatchAsync_WithMixedEntries_ShouldProcessAll()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var entries = new List<FailedEdgePushEntry>
        {
            new FailedEdgePushEntry { Type = "token", Jti = "jti-1", AccountId = accountId, TtlSeconds = 3600 },
            new FailedEdgePushEntry { Type = "account", AccountId = accountId, IssuedBeforeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() }
        };

        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        var result = await _provider.PushBatchAsync(entries);

        // Assert
        Assert.Equal(2, result);
    }

    [Fact]
    public async Task PushBatchAsync_WhenEmpty_ShouldReturnZero()
    {
        // Arrange
        var entries = new List<FailedEdgePushEntry>();

        // Act
        var result = await _provider.PushBatchAsync(entries);

        // Assert
        Assert.Equal(0, result);
    }

    #endregion
}
