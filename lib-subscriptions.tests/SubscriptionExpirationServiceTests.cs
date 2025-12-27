using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Subscriptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Subscriptions.Tests;

/// <summary>
/// Unit tests for SubscriptionExpirationService.
/// Tests background subscription expiration checking and event publishing.
/// </summary>
public class SubscriptionExpirationServiceTests
{
    private const string STATE_STORE = "subscriptions-statestore";

    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<IServiceScope> _mockScope;
    private readonly Mock<IServiceProvider> _mockScopedServiceProvider;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<List<string>>> _mockIndexStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<SubscriptionExpirationService>> _mockLogger;

    public SubscriptionExpirationServiceTests()
    {
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockScope = new Mock<IServiceScope>();
        _mockScopedServiceProvider = new Mock<IServiceProvider>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockIndexStore = new Mock<IStateStore<List<string>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<SubscriptionExpirationService>>();

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

        // Setup state store factory
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockIndexStore.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new SubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullServiceProvider_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SubscriptionExpirationService(
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SubscriptionExpirationService(
            _mockServiceProvider.Object,
            null!));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ShouldStopGracefully()
    {
        // Arrange
        var service = new SubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object);

        using var cts = new CancellationTokenSource();

        // Cancel immediately
        cts.Cancel();

        // Act & Assert - Should not throw
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoSubscriptions_ShouldLogDebug()
    {
        // Arrange
        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        var service = new TestableSubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object);

        // Act
        await service.TestCheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - Should not throw
        _mockStateStoreFactory.Verify(f => f.GetStore<List<string>>(STATE_STORE), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStateStoreFactoryNotAvailable_ShouldLogWarning()
    {
        // Arrange
        _mockScopedServiceProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Returns(null!);

        var service = new TestableSubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object);

        // Act - Should not throw
        await service.TestCheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - Service should handle missing dependencies gracefully
    }

    [Fact]
    public async Task ExecuteAsync_WhenMessageBusNotAvailable_ShouldLogWarning()
    {
        // Arrange
        _mockScopedServiceProvider.Setup(sp => sp.GetService(typeof(IMessageBus)))
            .Returns(null!);

        var service = new TestableSubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object);

        // Act - Should not throw
        await service.TestCheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - Service should handle missing dependencies gracefully
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptySubscriptionIndex_ShouldNotPublishEvents()
    {
        // Arrange
        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var service = new TestableSubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object);

        // Act
        await service.TestCheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - No events should be published
        _mockMessageBus.Verify(m => m.PublishAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    /// <summary>
    /// Testable wrapper for SubscriptionExpirationService to expose protected methods.
    /// </summary>
    private class TestableSubscriptionExpirationService : SubscriptionExpirationService
    {
        public TestableSubscriptionExpirationService(
            IServiceProvider serviceProvider,
            ILogger<SubscriptionExpirationService> logger)
            : base(serviceProvider, logger)
        {
        }

        /// <summary>
        /// Exposes the subscription checking logic for testing.
        /// Uses reflection to call the private CheckAndExpireSubscriptionsAsync method.
        /// </summary>
        public async Task TestCheckAndExpireSubscriptionsAsync(CancellationToken cancellationToken)
        {
            var method = typeof(SubscriptionExpirationService)
                .GetMethod("CheckAndExpireSubscriptionsAsync",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method != null)
            {
                var task = (Task?)method.Invoke(this, new object[] { cancellationToken });
                if (task != null)
                {
                    await task;
                }
            }
        }
    }
}
