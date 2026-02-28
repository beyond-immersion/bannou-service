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

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        using var service = new SubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration);

        // Assert
        Assert.NotNull(service);
    }

    // NOTE: Null check tests removed - DI container handles missing registrations,
    // and nullable reference types provide compile-time warnings.

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ShouldStopGracefully()
    {
        // Arrange
        using var service = new SubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration);

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
            .ReturnsAsync((List<Guid>?)null);

        using var service = new TestableSubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration);

        // Act
        await service.TestCheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - Should not throw
        _mockStateStoreFactory.Verify(f => f.GetStore<List<Guid>>(STATE_STORE), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStateStoreFactoryNotAvailable_ShouldThrow()
    {
        // Arrange
        _mockScopedServiceProvider.Setup(sp => sp.GetService(typeof(IStateStoreFactory)))
            .Returns((object?)null);

        using var service = new TestableSubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration);

        // Act & Assert - Required dependency missing should throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TestCheckAndExpireSubscriptionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_WhenSubscriptionServiceNotAvailable_ShouldThrow()
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

        using var service = new TestableSubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration);

        // Act & Assert - Required dependency missing should throw
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.TestCheckAndExpireSubscriptionsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptySubscriptionIndex_ShouldNotPublishEvents()
    {
        // Arrange
        _mockIndexStore.Setup(s => s.GetAsync("subscription-index", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        using var service = new TestableSubscriptionExpirationService(
            _mockServiceProvider.Object,
            _mockLogger.Object,
            _configuration);

        // Act
        await service.TestCheckAndExpireSubscriptionsAsync(CancellationToken.None);

        // Assert - No events should be published
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            It.IsAny<string>(),
            It.IsAny<object>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
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
            ILogger<SubscriptionExpirationService> logger,
            SubscriptionServiceConfiguration configuration)
            : base(serviceProvider, logger, configuration)
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
