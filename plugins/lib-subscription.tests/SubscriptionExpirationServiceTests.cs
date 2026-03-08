using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Subscription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Subscription.Tests;

/// <summary>
/// Unit tests for SubscriptionExpirationService.
/// Tests background subscription expiration checking and event publishing.
/// </summary>
public class SubscriptionExpirationServiceTests
{
    private const string STATE_STORE = "subscription-statestore";

    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceProvider> _mockScopedServiceProvider;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<List<Guid>>> _mockIndexStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ISubscriptionService> _mockSubscriptionService;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<ILogger<SubscriptionExpirationService>> _mockLogger;
    private readonly SubscriptionServiceConfiguration _configuration;

    public SubscriptionExpirationServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopedServiceProvider = new Mock<IServiceProvider>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockIndexStore = new Mock<IStateStore<List<Guid>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockSubscriptionService = new Mock<ISubscriptionService>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLogger = new Mock<ILogger<SubscriptionExpirationService>>();
        _configuration = new SubscriptionServiceConfiguration();

        // Setup the service provider chain for DI scopes
        _mockServiceProvider.Setup(sp => sp.GetService(typeof(IServiceScopeFactory)))
            .Returns(_mockScopeFactory.Object);

        _mockScopeFactory.Setup(f => f.CreateScope())
            .Returns(_mockScope.Object);

        _mockScope.Setup(s => s.ServiceProvider)
            .Returns(_mockScopedServiceProvider.Object);

        _mockScopedServiceProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Returns(_mockStateStoreFactory.Object);

        _mockScopedServiceProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(_mockMessageBus.Object);

        _mockScopedServiceProvider.Setup(sp => sp.GetService(typeof(ISubscriptionService)))
            .Returns(_mockSubscriptionService.Object);

        // Setup state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
            .Returns(_mockIndexStore.Object);
    }

    /// <summary>
    /// Creates a SubscriptionExpirationService with all mocked dependencies.
    /// </summary>
    private SubscriptionExpirationService CreateService()
    {
        return new SubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockTelemetryProvider.Object,
            _mockLogger.Object,
            _configuration);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        using var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    // NOTE: Null check tests removed - DI container handles missing registrations,
    // and nullable reference types provide compile-time warnings.

    #endregion

    #region CheckAndExpireSubscriptionsAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ShouldStopGracefully()
    {
        // Arrange
        using var service = CreateService();

        using var cts = new CancellationTokenSource();

        // Cancel immediately
        cts.Cancel();

        // Act & Assert - Should not throw
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CheckAndExpireSubscriptions_WhenNoSubscriptions_ShouldLogDebug()
    {
        // Arrange
        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        using var service = CreateService();

        // Act - call internal method directly via InternalsVisibleTo
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - Should not throw
        _mockStateStoreFactory.Verify(f => f.GetStore<List<Guid>>(STATE_STORE), Times.Once);
    }

    [Fact]
    public async Task CheckAndExpireSubscriptions_WhenStateStoreFactoryNotAvailable_ShouldThrow()
    {
        // Arrange
        _mockScopedServiceProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Returns((object?)null);

        using var service = CreateService();

        // Act & Assert - Required dependency missing should throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAndExpireSubscriptionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAndExpireSubscriptions_WhenSubscriptionServiceNotAvailable_ShouldThrow()
    {
        // Arrange
        _mockScopedServiceProvider.Setup(sp => sp.GetService(typeof(ISubscriptionService)))
            .Returns((object?)null);

        // Setup index to return data so the worker tries to resolve ISubscriptionService
        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { Guid.NewGuid() });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        mockSubscriptionStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = Guid.NewGuid(),
                IsActive = true,
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
                StubName = "test"
            });
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        using var service = CreateService();

        // Act & Assert - Required dependency missing should throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAndExpireSubscriptionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CheckAndExpireSubscriptions_WithEmptySubscriptionIndex_ShouldNotPublishEvents()
    {
        // Arrange
        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        using var service = CreateService();

        // Act
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - No events should be published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndExpireSubscriptions_WithExpiredSubscription_CallsExpireAndCleansIndex()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        mockSubscriptionStore.Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                IsActive = true,
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeSeconds(),
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeSeconds()
            });
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        // Mock: ExpireSubscriptionAsync succeeds
        _mockSubscriptionService.Setup(s => s.ExpireSubscriptionAsync(subscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Mock: CleanupSubscriptionIndexAsync succeeds
        _mockIndexStore.Setup(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { subscriptionId }, "etag1"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("subscription-index", It.IsAny<List<Guid>>(), "etag1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");

        using var service = CreateService();

        // Act
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert
        _mockSubscriptionService.Verify(s => s.ExpireSubscriptionAsync(subscriptionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndExpireSubscriptions_InactiveSubscription_RemovedFromIndexWithoutExpiring()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        mockSubscriptionStore.Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = Guid.NewGuid(),
                IsActive = false, // Already inactive
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
                StubName = "test-game",
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-31).ToUnixTimeSeconds()
            });
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        // Mock: Cleanup index
        _mockIndexStore.Setup(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { subscriptionId }, "etag1"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("subscription-index", It.IsAny<List<Guid>>(), "etag1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");

        using var service = CreateService();

        // Act
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - ExpireSubscriptionAsync should NOT be called (already inactive)
        _mockSubscriptionService.Verify(s => s.ExpireSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndExpireSubscriptions_DeletedSubscription_RemovedFromIndex()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        // Subscription no longer exists in store
        mockSubscriptionStore.Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        // Mock: Cleanup index
        _mockIndexStore.Setup(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { subscriptionId }, "etag1"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("subscription-index", It.IsAny<List<Guid>>(), "etag1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");

        using var service = CreateService();

        // Act
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - ExpireSubscriptionAsync should NOT be called
        _mockSubscriptionService.Verify(s => s.ExpireSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify index cleanup was attempted
        _mockIndexStore.Verify(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CleanupSubscriptionIndexAsync Retry Logic

    [Fact]
    public async Task CleanupSubscriptionIndex_ETagConflict_RetriesSuccessfully()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var secondSubId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        // Subscription was deleted
        mockSubscriptionStore.Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        // Mock: First TrySaveAsync fails (etag conflict), second succeeds
        var callCount = 0;
        _mockIndexStore.Setup(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return (new List<Guid> { subscriptionId, secondSubId }, "etag1");
                return (new List<Guid> { subscriptionId, secondSubId }, "etag2");
            });

        _mockIndexStore.Setup(s => s.TrySaveAsync("subscription-index", It.IsAny<List<Guid>>(), "etag1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null); // ETag conflict on first attempt

        _mockIndexStore.Setup(s => s.TrySaveAsync("subscription-index", It.IsAny<List<Guid>>(), "etag2", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag3"); // Success on second attempt

        using var service = CreateService();

        // Act
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - Should have retried and succeeded
        _mockIndexStore.Verify(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task CleanupSubscriptionIndex_AllRetriesFail_DoesNotThrow()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        mockSubscriptionStore.Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        // Mock: Every TrySaveAsync fails (etag conflict all 3 attempts)
        _mockIndexStore.Setup(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { subscriptionId }, "stale-etag"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("subscription-index", It.IsAny<List<Guid>>(), "stale-etag", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null); // Always fails

        using var service = CreateService();

        // Act - Should not throw even after all retries fail
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - All 3 retry attempts were made
        _mockIndexStore.Verify(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()),
            Times.Exactly(3));
        _mockIndexStore.Verify(s => s.TrySaveAsync("subscription-index", It.IsAny<List<Guid>>(), "stale-etag", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task CleanupSubscriptionIndex_NothingToRemove_SkipsWrite()
    {
        // Arrange
        var activeSubId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeSubId });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        // Active subscription that hasn't expired
        mockSubscriptionStore.Setup(s => s.GetAsync($"subscription:{activeSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = activeSubId,
                AccountId = Guid.NewGuid(),
                IsActive = true,
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                StubName = "test-game",
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        using var service = CreateService();

        // Act
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - No index cleanup needed (no IDs to remove)
        _mockIndexStore.Verify(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockIndexStore.Verify(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CleanupSubscriptionIndex_EmptyIndexOnRead_ReturnsEarly()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        mockSubscriptionStore.Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        // Mock: GetWithETagAsync returns null/empty (another worker already cleaned it)
        _mockIndexStore.Setup(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)null, (string?)null));

        using var service = CreateService();

        // Act
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - TrySaveAsync should never be called since index is empty
        _mockIndexStore.Verify(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region UnlimitedSubscription Index Removal

    [Fact]
    public async Task CheckAndExpireSubscriptions_UnlimitedSubscription_RemovedFromIndex()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();

        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        var mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        mockSubscriptionStore.Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = Guid.NewGuid(),
                IsActive = true,
                ExpirationDateUnix = null, // Unlimited subscription
                StubName = "test-game",
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(mockSubscriptionStore.Object);

        // Mock: Cleanup index
        _mockIndexStore.Setup(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid> { subscriptionId }, "etag1"));
        _mockIndexStore.Setup(s => s.TrySaveAsync("subscription-index", It.IsAny<List<Guid>>(), "etag1", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");

        using var service = CreateService();

        // Act
        await service.CheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - Should NOT expire, but should clean from index
        _mockSubscriptionService.Verify(s => s.ExpireSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockIndexStore.Verify(s => s.GetWithETagAsync("subscription-index", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
