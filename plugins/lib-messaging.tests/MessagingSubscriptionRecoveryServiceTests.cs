using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Messaging.Tests;

/// <summary>
/// Unit tests for MessagingSubscriptionRecoveryService.
/// Tests the background service that recovers external subscriptions on startup
/// and periodically refreshes TTL on persisted subscriptions.
/// </summary>
public class MessagingSubscriptionRecoveryServiceTests
{
    private readonly Mock<ILogger<MessagingSubscriptionRecoveryService>> _mockLogger;
    private readonly Mock<ILogger<MessagingService>> _mockServiceLogger;
    private readonly MessagingServiceConfiguration _configuration;
    private readonly MessagingService _messagingService;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IMessageSubscriber> _mockMessageSubscriber;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<ICacheableStateStore<ExternalSubscriptionData>> _mockSubscriptionStore;

    public MessagingSubscriptionRecoveryServiceTests()
    {
        _mockLogger = new Mock<ILogger<MessagingSubscriptionRecoveryService>>();
        _mockServiceLogger = new Mock<ILogger<MessagingService>>();
        _configuration = new MessagingServiceConfiguration
        {
            SubscriptionRecoveryStartupDelaySeconds = 0, // No delay for tests
            SubscriptionTtlRefreshIntervalHours = 1
        };

        _mockMessageBus = new Mock<IMessageBus>();
        _mockMessageSubscriber = new Mock<IMessageSubscriber>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockSubscriptionStore = new Mock<ICacheableStateStore<ExternalSubscriptionData>>();

        _mockStateStoreFactory
            .Setup(x => x.GetCacheableStore<ExternalSubscriptionData>(StateStoreDefinitions.MessagingExternalSubs))
            .Returns(_mockSubscriptionStore.Object);

        _mockSubscriptionStore
            .Setup(x => x.GetSetAsync<ExternalSubscriptionData>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ExternalSubscriptionData>());

        _messagingService = new MessagingService(
            _mockServiceLogger.Object,
            _configuration,
            new AppConfiguration { AppId = "test-app" },
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            _mockHttpClientFactory.Object,
            _mockStateStoreFactory.Object);
    }

    private MessagingSubscriptionRecoveryService CreateService()
    {
        return new MessagingSubscriptionRecoveryService(
            _mockLogger.Object,
            _messagingService,
            _configuration);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<MessagingSubscriptionRecoveryService>();
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_RecoversSubs_ThenWaitsForCancellation()
    {
        // Arrange
        using var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        // Give it a moment to complete recovery
        await Task.Delay(100);
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        // Assert - Recovery was called (GetSetAsync was invoked)
        _mockSubscriptionStore.Verify(
            x => x.GetSetAsync<ExternalSubscriptionData>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecoveryThrows_ContinuesRunning()
    {
        // Arrange - Make recovery throw
        _mockSubscriptionStore
            .Setup(x => x.GetSetAsync<ExternalSubscriptionData>(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("State store unavailable"));

        using var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Act - Service should not crash
        await service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ExitsGracefully()
    {
        // Arrange
        using var service = CreateService();
        using var cts = new CancellationTokenSource();

        // Act
        await service.StartAsync(cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        var exception = await Record.ExceptionAsync(() => service.StopAsync(CancellationToken.None));

        // Assert
        Assert.Null(exception);
    }

    #endregion
}
