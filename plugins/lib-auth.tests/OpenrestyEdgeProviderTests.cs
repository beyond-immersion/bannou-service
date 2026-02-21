using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for OpenrestyEdgeProvider.
/// Tests Redis state verification for OpenResty edge revocation.
/// </summary>
public class OpenrestyEdgeProviderTests
{
    private const string STATE_STORE = "edge-revocation-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<TokenRevocationEntry>> _mockTokenStore;
    private readonly Mock<IStateStore<AccountRevocationEntry>> _mockAccountStore;
    private readonly Mock<ILogger<OpenrestyEdgeProvider>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly OpenrestyEdgeProvider _provider;

    public OpenrestyEdgeProviderTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockTokenStore = new Mock<IStateStore<TokenRevocationEntry>>();
        _mockAccountStore = new Mock<IStateStore<AccountRevocationEntry>>();
        _mockLogger = new Mock<ILogger<OpenrestyEdgeProvider>>();

        _configuration = new AuthServiceConfiguration
        {
            OpenrestyEdgeEnabled = true
        };

        // Setup state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<TokenRevocationEntry>(STATE_STORE))
            .Returns(_mockTokenStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<AccountRevocationEntry>(STATE_STORE))
            .Returns(_mockAccountStore.Object);

        _provider = new OpenrestyEdgeProvider(
            _configuration,
            _mockStateStoreFactory.Object,
            _mockLogger.Object);
    }

    #region ProviderId Tests

    [Fact]
    public void ProviderId_ShouldReturnOpenresty()
    {
        // Act
        var result = _provider.ProviderId;

        // Assert
        Assert.Equal("openresty", result);
    }

    #endregion

    #region IsEnabled Tests

    [Fact]
    public void IsEnabled_WhenConfigEnabled_ShouldReturnTrue()
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
        var config = new AuthServiceConfiguration { OpenrestyEdgeEnabled = false };
        var provider = new OpenrestyEdgeProvider(config, _mockStateStoreFactory.Object, _mockLogger.Object);

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
        var config = new AuthServiceConfiguration { OpenrestyEdgeEnabled = false };
        var provider = new OpenrestyEdgeProvider(config, _mockStateStoreFactory.Object, _mockLogger.Object);

        // Act
        var result = await provider.PushTokenRevocationAsync("test-jti", Guid.NewGuid(), TimeSpan.FromMinutes(60));

        // Assert
        Assert.True(result);
        _mockTokenStore.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PushTokenRevocationAsync_WhenEntryExists_ShouldReturnTrue()
    {
        // Arrange
        var jti = "test-jti-123";
        var accountId = Guid.NewGuid();
        var entry = new TokenRevocationEntry
        {
            Jti = jti,
            AccountId = accountId,
            RevokedAt = DateTimeOffset.UtcNow,
            Reason = "test"
        };

        _mockTokenStore.Setup(s => s.GetAsync($"token:{jti}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _provider.PushTokenRevocationAsync(jti, accountId, TimeSpan.FromMinutes(60));

        // Assert
        Assert.True(result);
        _mockTokenStore.Verify(s => s.GetAsync($"token:{jti}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PushTokenRevocationAsync_WhenEntryNotFound_ShouldStillReturnTrue()
    {
        // Arrange
        var jti = "test-jti-123";
        _mockTokenStore.Setup(s => s.GetAsync($"token:{jti}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TokenRevocationEntry?)null);

        // Act
        var result = await _provider.PushTokenRevocationAsync(jti, Guid.NewGuid(), TimeSpan.FromMinutes(60));

        // Assert - returns true because entry might not be written yet (defense-in-depth)
        Assert.True(result);
    }

    [Fact]
    public async Task PushTokenRevocationAsync_WhenRedisError_ShouldReturnFalse()
    {
        // Arrange
        var jti = "test-jti-123";
        _mockTokenStore.Setup(s => s.GetAsync($"token:{jti}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

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
        var config = new AuthServiceConfiguration { OpenrestyEdgeEnabled = false };
        var provider = new OpenrestyEdgeProvider(config, _mockStateStoreFactory.Object, _mockLogger.Object);

        // Act
        var result = await provider.PushAccountRevocationAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        // Assert
        Assert.True(result);
        _mockAccountStore.Verify(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PushAccountRevocationAsync_WhenEntryExists_ShouldReturnTrue()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var issuedBefore = DateTimeOffset.UtcNow;
        var entry = new AccountRevocationEntry
        {
            AccountId = accountId,
            IssuedBefore = issuedBefore,
            RevokedAt = DateTimeOffset.UtcNow,
            Reason = "test"
        };

        _mockAccountStore.Setup(s => s.GetAsync($"account:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        // Act
        var result = await _provider.PushAccountRevocationAsync(accountId, issuedBefore);

        // Assert
        Assert.True(result);
        _mockAccountStore.Verify(s => s.GetAsync($"account:{accountId}", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PushAccountRevocationAsync_WhenRedisError_ShouldReturnFalse()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        _mockAccountStore.Setup(s => s.GetAsync($"account:{accountId}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));

        // Act
        var result = await _provider.PushAccountRevocationAsync(accountId, DateTimeOffset.UtcNow);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region PushBatchAsync Tests

    [Fact]
    public async Task PushBatchAsync_WhenDisabled_ShouldReturnFullCount()
    {
        // Arrange
        var config = new AuthServiceConfiguration { OpenrestyEdgeEnabled = false };
        var provider = new OpenrestyEdgeProvider(config, _mockStateStoreFactory.Object, _mockLogger.Object);
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

        _mockTokenStore.Setup(s => s.GetAsync("token:jti-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TokenRevocationEntry { Jti = "jti-1" });
        _mockAccountStore.Setup(s => s.GetAsync($"account:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountRevocationEntry { AccountId = accountId });

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
