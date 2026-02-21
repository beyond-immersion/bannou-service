using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Auth.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Auth.Tests;

/// <summary>
/// Unit tests for EdgeRevocationService.
/// Tests token and account revocation workflows, retry logic, and provider coordination.
/// </summary>
public class EdgeRevocationServiceTests
{
    private const string STATE_STORE = "edge-revocation-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<TokenRevocationEntry>> _mockTokenStore;
    private readonly Mock<IStateStore<AccountRevocationEntry>> _mockAccountStore;
    private readonly Mock<IStateStore<List<string>>> _mockIndexStore;
    private readonly Mock<IStateStore<FailedEdgePushEntry>> _mockFailedStore;
    private readonly Mock<IEdgeRevocationProvider> _mockProvider;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<EdgeRevocationService>> _mockLogger;
    private readonly AuthServiceConfiguration _configuration;
    private readonly EdgeRevocationService _service;

    public EdgeRevocationServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockTokenStore = new Mock<IStateStore<TokenRevocationEntry>>();
        _mockAccountStore = new Mock<IStateStore<AccountRevocationEntry>>();
        _mockIndexStore = new Mock<IStateStore<List<string>>>();
        _mockFailedStore = new Mock<IStateStore<FailedEdgePushEntry>>();
        _mockProvider = new Mock<IEdgeRevocationProvider>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<EdgeRevocationService>>();

        _configuration = new AuthServiceConfiguration
        {
            EdgeRevocationEnabled = true,
            EdgeRevocationTimeoutSeconds = 5,
            EdgeRevocationMaxRetryAttempts = 3
        };

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<TokenRevocationEntry>(STATE_STORE))
            .Returns(_mockTokenStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<AccountRevocationEntry>(STATE_STORE))
            .Returns(_mockAccountStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<FailedEdgePushEntry>(STATE_STORE))
            .Returns(_mockFailedStore.Object);

        // Setup provider
        _mockProvider.Setup(p => p.ProviderId).Returns("test-provider");
        _mockProvider.Setup(p => p.IsEnabled).Returns(true);
        _mockProvider.Setup(p => p.PushTokenRevocationAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockProvider.Setup(p => p.PushAccountRevocationAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup index store to return fresh empty lists by default (factory pattern prevents shared state)
        _mockIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new List<string>());

        _service = new EdgeRevocationService(
            _mockStateStoreFactory.Object,
            new[] { _mockProvider.Object },
            _mockMessageBus.Object,
            _configuration,
            _mockLogger.Object);
    }

    #region IsEnabled Tests

    [Fact]
    public void IsEnabled_WhenConfigEnabled_ShouldReturnTrue()
    {
        // Arrange - configuration set in constructor with EdgeRevocationEnabled = true

        // Act
        var result = _service.IsEnabled;

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsEnabled_WhenConfigDisabled_ShouldReturnFalse()
    {
        // Arrange
        var config = new AuthServiceConfiguration { EdgeRevocationEnabled = false };
        var service = new EdgeRevocationService(
            _mockStateStoreFactory.Object,
            new[] { _mockProvider.Object },
            _mockMessageBus.Object,
            config,
            _mockLogger.Object);

        // Act
        var result = service.IsEnabled;

        // Assert
        Assert.False(result);
    }

    #endregion

    #region RevokeTokenAsync Tests

    [Fact]
    public async Task RevokeTokenAsync_WhenEnabled_ShouldStoreEntryAndPushToProviders()
    {
        // Arrange
        var jti = "test-jti-123";
        var accountId = Guid.NewGuid();
        var ttl = TimeSpan.FromMinutes(60);
        var reason = "test-revocation";

        // Act
        await _service.RevokeTokenAsync(jti, accountId, ttl, reason);

        // Assert - verify token entry was saved
        _mockTokenStore.Verify(s => s.SaveAsync(
            $"token:{jti}",
            It.Is<TokenRevocationEntry>(e => e.Jti == jti && e.AccountId == accountId && e.Reason == reason),
            It.Is<StateOptions>(o => o.Ttl == (int)ttl.TotalSeconds),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - verify index was updated
        _mockIndexStore.Verify(s => s.SaveAsync(
            "token-index",
            It.Is<List<string>>(l => l.Contains(jti)),
            null,
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - verify provider was called
        _mockProvider.Verify(p => p.PushTokenRevocationAsync(jti, accountId, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeTokenAsync_WhenDisabled_ShouldDoNothing()
    {
        // Arrange
        var config = new AuthServiceConfiguration { EdgeRevocationEnabled = false };
        var service = new EdgeRevocationService(
            _mockStateStoreFactory.Object,
            new[] { _mockProvider.Object },
            _mockMessageBus.Object,
            config,
            _mockLogger.Object);

        var jti = "test-jti-123";
        var accountId = Guid.NewGuid();

        // Act
        await service.RevokeTokenAsync(jti, accountId, TimeSpan.FromMinutes(60), "test");

        // Assert - nothing should be called
        _mockTokenStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<TokenRevocationEntry>(), It.IsAny<StateOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockProvider.Verify(p => p.PushTokenRevocationAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RevokeTokenAsync_WhenProviderFails_ShouldAddToFailedPushes()
    {
        // Arrange
        var jti = "test-jti-123";
        var accountId = Guid.NewGuid();

        _mockProvider.Setup(p => p.PushTokenRevocationAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await _service.RevokeTokenAsync(jti, accountId, TimeSpan.FromMinutes(60), "test");

        // Assert - failed push should be recorded
        _mockFailedStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains("failed:") && k.Contains(jti)),
            It.Is<FailedEdgePushEntry>(e => e.Type == "token" && e.Jti == jti && e.ProviderId == "test-provider"),
            null,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RevokeAccountAsync Tests

    [Fact]
    public async Task RevokeAccountAsync_WhenEnabled_ShouldStoreEntryAndPushToProviders()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var issuedBefore = DateTimeOffset.UtcNow;
        var reason = "account-revocation";

        // Act
        await _service.RevokeAccountAsync(accountId, issuedBefore, reason);

        // Assert - verify account entry was saved with TTL
        _mockAccountStore.Verify(s => s.SaveAsync(
            $"account:{accountId}",
            It.Is<AccountRevocationEntry>(e => e.AccountId == accountId && e.Reason == reason),
            It.Is<StateOptions?>(o => o != null && o.Ttl > 0),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - verify index was updated
        _mockIndexStore.Verify(s => s.SaveAsync(
            "account-index",
            It.Is<List<string>>(l => l.Contains(accountId.ToString())),
            null,
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - verify provider was called
        _mockProvider.Verify(p => p.PushAccountRevocationAsync(accountId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RevokeAccountAsync_WhenDisabled_ShouldDoNothing()
    {
        // Arrange
        var config = new AuthServiceConfiguration { EdgeRevocationEnabled = false };
        var service = new EdgeRevocationService(
            _mockStateStoreFactory.Object,
            new[] { _mockProvider.Object },
            _mockMessageBus.Object,
            config,
            _mockLogger.Object);

        var accountId = Guid.NewGuid();

        // Act
        await service.RevokeAccountAsync(accountId, DateTimeOffset.UtcNow, "test");

        // Assert - nothing should be called
        _mockAccountStore.Verify(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<AccountRevocationEntry>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockProvider.Verify(p => p.PushAccountRevocationAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetRevocationListAsync Tests

    [Fact]
    public async Task GetRevocationListAsync_WithTokensAndAccounts_ShouldReturnBoth()
    {
        // Arrange
        var jti = "test-jti-123";
        var accountId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("token-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { jti });
        _mockIndexStore.Setup(s => s.GetAsync("account-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { accountId.ToString() });
        _mockIndexStore.Setup(s => s.GetAsync("failed-push-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var tokenEntry = new TokenRevocationEntry
        {
            Jti = jti,
            AccountId = accountId,
            RevokedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            Reason = "test"
        };
        _mockTokenStore.Setup(s => s.GetAsync($"token:{jti}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tokenEntry);

        var accountEntry = new AccountRevocationEntry
        {
            AccountId = accountId,
            IssuedBefore = DateTimeOffset.UtcNow,
            RevokedAt = DateTimeOffset.UtcNow,
            Reason = "test"
        };
        _mockAccountStore.Setup(s => s.GetAsync($"account:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(accountEntry);

        // Act
        var (tokens, accounts, failedCount, totalCount) = await _service.GetRevocationListAsync(true, true, 100);

        // Assert
        Assert.Single(tokens);
        Assert.Single(accounts);
        Assert.Equal(0, failedCount);
        Assert.Equal(1, totalCount);
        Assert.Equal(jti, tokens[0].Jti);
        Assert.Equal(accountId, accounts[0].AccountId);
    }

    [Fact]
    public async Task GetRevocationListAsync_WithLimit_ShouldRespectLimit()
    {
        // Arrange
        var jtis = new List<string> { "jti-1", "jti-2", "jti-3" };
        _mockIndexStore.Setup(s => s.GetAsync("token-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(jtis);
        _mockIndexStore.Setup(s => s.GetAsync("failed-push-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        foreach (var jti in jtis)
        {
            _mockTokenStore.Setup(s => s.GetAsync($"token:{jti}", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TokenRevocationEntry { Jti = jti, AccountId = Guid.NewGuid(), Reason = "test" });
        }

        // Act
        var (tokens, _, _, totalCount) = await _service.GetRevocationListAsync(true, false, 2);

        // Assert
        Assert.Equal(2, tokens.Count);
        Assert.Equal(3, totalCount);
    }

    #endregion
}
