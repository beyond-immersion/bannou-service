using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Subscription.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Subscription;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BeyondImmersion.BannouService.Subscription.Tests;

/// <summary>
/// Unit tests for SubscriptionService.
/// Tests business logic with mocked IStateStoreFactory and IGameServiceClient.
/// </summary>
public class SubscriptionServiceTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<SubscriptionDataModel>> _mockSubscriptionStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<ILogger<SubscriptionService>> _mockLogger;
    private readonly SubscriptionServiceConfiguration _configuration;
    private readonly Mock<IGameServiceClient> _mockServiceClient;
    private readonly Mock<IEntitySessionRegistry> _mockEntitySessionRegistry;
    private const string STATE_STORE = "subscription-statestore";

    public SubscriptionServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockLogger = new Mock<ILogger<SubscriptionService>>();
        _configuration = new SubscriptionServiceConfiguration
        {
            LockTimeoutSeconds = 10
        };
        _mockServiceClient = new Mock<IGameServiceClient>();
        _mockEntitySessionRegistry = new Mock<IEntitySessionRegistry>();

        // Setup default behavior for entity session registry
        _mockEntitySessionRegistry.Setup(r => r.PublishToEntitySessionsAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<BaseClientEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(_mockSubscriptionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
            .Returns(_mockListStore.Object);

        // Setup default behavior for stores
        _mockSubscriptionStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SubscriptionDataModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockListStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<Guid>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup default behavior for message bus
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup default behavior for distributed lock provider - always acquire successfully
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        mockLockResponse.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider.Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);
    }

    private SubscriptionService CreateService()
    {
        return new SubscriptionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object,
            _mockLogger.Object,
            _configuration,
            _mockServiceClient.Object,
            _mockEntitySessionRegistry.Object);
    }

    #region Constructor Tests

    #endregion

    #region GetAccountSubscriptionsAsync Tests

    [Fact]
    public async Task GetAccountSubscriptionsAsync_ShouldReturnAllSubscriptions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new GetAccountSubscriptionsRequest
        {
            AccountId = accountId,
            IncludeInactive = true,
            IncludeExpired = true
        };

        // Mock: Account subscription index
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        // Mock: Subscription data
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetAccountSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        Assert.Single(response.Subscriptions);
    }

    [Fact]
    public async Task GetAccountSubscriptionsAsync_ShouldFilterInactive_WhenIncludeInactiveFalse()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var activeSubId = Guid.NewGuid();
        var inactiveSubId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new GetAccountSubscriptionsRequest
        {
            AccountId = accountId,
            IncludeInactive = false,
            IncludeExpired = true
        };

        // Mock: Account subscription index
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeSubId, inactiveSubId });

        // Mock: Active subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{activeSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = activeSubId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Inactive subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{inactiveSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = inactiveSubId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game-2",
                DisplayName = "Test Game 2",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = false,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetAccountSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        Assert.Equal("test-game", response.Subscriptions.First().StubName);
    }

    [Fact]
    public async Task GetAccountSubscriptionsAsync_ShouldFilterExpired_WhenIncludeExpiredFalse()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var activeSubId = Guid.NewGuid();
        var expiredSubId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new GetAccountSubscriptionsRequest
        {
            AccountId = accountId,
            IncludeInactive = true,
            IncludeExpired = false
        };

        // Mock: Account subscription index
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeSubId, expiredSubId });

        // Mock: Active subscription (future expiration)
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{activeSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = activeSubId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Expired subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{expiredSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = expiredSubId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game-2",
                DisplayName = "Test Game 2",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(), // Expired
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetAccountSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);

        Assert.Equal("test-game", response.Subscriptions.First().StubName);
    }

    [Fact]
    public async Task GetAccountSubscriptionsAsync_ShouldReturnEmptyList_WhenNoSubscriptions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var request = new GetAccountSubscriptionsRequest { AccountId = accountId };

        // Mock: No subscriptions
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var (statusCode, response) = await service.GetAccountSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Subscriptions);

    }

    #endregion

    #region QueryCurrentSubscriptionsAsync Tests

    [Fact]
    public async Task QueryCurrentSubscriptionsAsync_ShouldReturnActiveNonExpiredOnly()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var activeSubId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new QueryCurrentSubscriptionsRequest { AccountId = accountId };

        // Mock: Account subscription index
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { activeSubId });

        // Mock: Active subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{activeSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = activeSubId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.QueryCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Subscriptions);
        Assert.Equal("test-game", response.Subscriptions.First().StubName);

    }

    [Fact]
    public async Task QueryCurrentSubscriptionsAsync_ShouldExcludeExpiredSubscriptions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var expiredSubId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new QueryCurrentSubscriptionsRequest { AccountId = accountId };

        // Mock: Account subscription index
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { expiredSubId });

        // Mock: Expired subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{expiredSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = expiredSubId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(), // Expired
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.QueryCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Subscriptions);

    }

    [Fact]
    public async Task QueryCurrentSubscriptionsAsync_ShouldExcludeInactiveSubscriptions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var inactiveSubId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new QueryCurrentSubscriptionsRequest { AccountId = accountId };

        // Mock: Account subscription index
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { inactiveSubId });

        // Mock: Inactive subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{inactiveSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = inactiveSubId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = false, // Inactive
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.QueryCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Subscriptions);

    }

    [Fact]
    public async Task QueryCurrentSubscriptionsAsync_ShouldReturnBadRequest_WhenNoFilterProvided()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryCurrentSubscriptionsRequest(); // No accountId or stubName

        // Act
        var (statusCode, response) = await service.QueryCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region GetSubscriptionAsync Tests

    [Fact]
    public async Task GetSubscriptionAsync_ShouldReturnSubscription_WhenExists()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new GetSubscriptionRequest { SubscriptionId = subscriptionId };

        // Mock: Subscription exists
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(subscriptionId, response.SubscriptionId);
        Assert.Equal("test-game", response.StubName);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var request = new GetSubscriptionRequest { SubscriptionId = subscriptionId };

        // Mock: Subscription doesn't exist
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);

        // Act
        var (statusCode, response) = await service.GetSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    #endregion

    #region CreateSubscriptionAsync Tests

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldReturnCreated_WhenValidRequest()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new CreateSubscriptionRequest
        {
            AccountId = accountId,
            ServiceId = serviceId,
            DurationDays = 30
        };

        // Mock: Service exists
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                IsActive = true
            });

        // Mock: No existing subscriptions
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Mock: Service subscriptions index
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("test-game", response.StubName);
        Assert.True(response.IsActive);
        Assert.NotNull(response.ExpirationDate);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldReturnNotFound_WhenServiceNotExists()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new CreateSubscriptionRequest
        {
            AccountId = accountId,
            ServiceId = serviceId
        };

        // Mock: Service doesn't exist
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldReturnConflict_WhenActiveSubscriptionExists()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var existingSubId = Guid.NewGuid();
        var request = new CreateSubscriptionRequest
        {
            AccountId = accountId,
            ServiceId = serviceId
        };

        // Mock: Service exists
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                IsActive = true
            });

        // Mock: Existing subscriptions
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { existingSubId });

        // Mock: Existing active subscription for same service
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{existingSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = existingSubId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldPublishSubscriptionUpdatedEvent()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new CreateSubscriptionRequest
        {
            AccountId = accountId,
            ServiceId = serviceId,
            DurationDays = 30
        };

        // Mock: Service exists
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                IsActive = true
            });

        // Mock: No existing subscriptions
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Mock: Service subscriptions index
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify event published via IMessageBus
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "subscription.updated",
            It.Is<SubscriptionUpdatedEvent>(e =>
                e.AccountId == accountId &&
                e.Action == SubscriptionAction.Created),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Verify client event pushed via IEntitySessionRegistry
        _mockEntitySessionRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            "account",
            accountId,
            It.Is<SubscriptionStatusChangedClientEvent>(e =>
                e.AccountId == accountId &&
                e.ServiceId == serviceId &&
                e.Action == SubscriptionAction.Created &&
                e.IsActive == true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateSubscriptionAsync Tests

    [Fact]
    public async Task UpdateSubscriptionAsync_ShouldUpdateExpirationDate()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var newExpiration = DateTimeOffset.UtcNow.AddDays(60);
        var request = new UpdateSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            ExpirationDate = newExpiration
        };

        // Mock: Subscription exists
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.UpdateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        Assert.Equal(newExpiration.ToUnixTimeSeconds(), savedModel.ExpirationDateUnix);
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var request = new UpdateSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            IsActive = false
        };

        // Mock: Subscription doesn't exist
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);

        // Act
        var (statusCode, response) = await service.UpdateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    #endregion

    #region CancelSubscriptionAsync Tests

    [Fact]
    public async Task CancelSubscriptionAsync_ShouldSetIsActiveFalse()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new CancelSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            AccountId = accountId,
            Reason = "User requested cancellation"
        };

        // Mock: Subscription exists
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.CancelSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.False(response.IsActive);
        Assert.NotNull(savedModel);
        Assert.False(savedModel.IsActive);
        Assert.NotNull(savedModel.CancelledAtUnix);
        Assert.Equal("User requested cancellation", savedModel.CancellationReason);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_ShouldPublishSubscriptionUpdatedEvent_WithCancelledAction()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new CancelSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            AccountId = accountId,
            Reason = "Test cancellation"
        };

        // Mock: Subscription exists
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.CancelSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify event published with Cancelled action via IMessageBus
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "subscription.updated",
            It.Is<SubscriptionUpdatedEvent>(e =>
                e.SubscriptionId == subscriptionId &&
                e.Action == SubscriptionAction.Cancelled),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelSubscriptionAsync_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var request = new CancelSubscriptionRequest { SubscriptionId = subscriptionId };

        // Mock: Subscription doesn't exist
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);

        // Act
        var (statusCode, response) = await service.CancelSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    #endregion

    #region RenewSubscriptionAsync Tests

    [Fact]
    public async Task RenewSubscriptionAsync_ShouldExtendExpiration()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var originalExpiration = DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeSeconds();
        var request = new RenewSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            ExtensionDays = 30
        };

        // Mock: Subscription exists
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-25).ToUnixTimeSeconds(),
                ExpirationDateUnix = originalExpiration,
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-25).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.RenewSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        Assert.True(savedModel.IsActive);
        Assert.NotNull(savedModel.ExpirationDateUnix);
        // Extension should be from current expiration (5 days from now) + 30 days = ~35 days from now
        var expectedMinExpiration = DateTimeOffset.UtcNow.AddDays(34).ToUnixTimeSeconds();
        Assert.True(savedModel.ExpirationDateUnix >= expectedMinExpiration);
    }

    [Fact]
    public async Task RenewSubscriptionAsync_ShouldReactivateCancelledSubscription()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new RenewSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            ExtensionDays = 30
        };

        // Mock: Cancelled subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
                IsActive = false,
                CancelledAtUnix = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeSeconds(),
                CancellationReason = "Old reason",
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.RenewSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        Assert.True(savedModel.IsActive);
        Assert.Null(savedModel.CancelledAtUnix);
        Assert.Null(savedModel.CancellationReason);
    }

    [Fact]
    public async Task RenewSubscriptionAsync_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var request = new RenewSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            ExtensionDays = 30
        };

        // Mock: Subscription doesn't exist
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);

        // Act
        var (statusCode, response) = await service.RenewSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, statusCode);
    }

    #endregion

    #region ExpireSubscriptionAsync Tests

    [Fact]
    public async Task ExpireSubscriptionAsync_ShouldSetIsActiveFalse()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        // Mock: Active subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(), // Expired
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var result = await service.ExpireSubscriptionAsync(subscriptionId, CancellationToken.None);

        // Assert
        Assert.True(result);
        Assert.NotNull(savedModel);
        Assert.False(savedModel.IsActive);
    }

    [Fact]
    public async Task ExpireSubscriptionAsync_ShouldReturnFalse_WhenNotExists()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();

        // Mock: Subscription doesn't exist
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((SubscriptionDataModel?)null);

        // Act
        var result = await service.ExpireSubscriptionAsync(subscriptionId, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExpireSubscriptionAsync_ShouldReturnFalse_WhenAlreadyInactive()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        // Mock: Already inactive subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                IsActive = false, // Already inactive
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var result = await service.ExpireSubscriptionAsync(subscriptionId, CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ExpireSubscriptionAsync_ShouldReturnFalse_WhenLockFails()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();

        // Mock: Lock acquisition fails
        var mockFailedLock = new Mock<ILockResponse>();
        mockFailedLock.Setup(l => l.Success).Returns(false);
        mockFailedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockFailedLock.Object);

        // Act
        var result = await service.ExpireSubscriptionAsync(subscriptionId, CancellationToken.None);

        // Assert
        Assert.False(result);

        // Verify no state store save was attempted
        _mockSubscriptionStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<SubscriptionDataModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region CreateSubscriptionAsync Lock and ExpirationDate Edge Cases

    [Fact]
    public async Task CreateSubscriptionAsync_ShouldReturnConflict_WhenLockFails()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new CreateSubscriptionRequest
        {
            AccountId = accountId,
            ServiceId = serviceId,
            DurationDays = 30
        };

        // Mock: Service exists (fetched before lock)
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                IsActive = true
            });

        // Mock: Lock acquisition fails
        var mockFailedLock = new Mock<ILockResponse>();
        mockFailedLock.Setup(l => l.Success).Returns(false);
        mockFailedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockFailedLock.Object);

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);

        // Verify no subscription was saved
        _mockSubscriptionStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<SubscriptionDataModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_ExpirationDateTakesPrecedenceOverDurationDays()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var explicitExpiration = DateTimeOffset.UtcNow.AddDays(90);
        var request = new CreateSubscriptionRequest
        {
            AccountId = accountId,
            ServiceId = serviceId,
            ExpirationDate = explicitExpiration,
            DurationDays = 30 // Should be ignored when ExpirationDate is provided
        };

        // Mock: Service exists
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                IsActive = true
            });

        // Mock: No existing subscriptions
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("subscription:")),
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        // ExpirationDate should be the explicit one (~90 days from now), not DurationDays-derived (~30 days)
        Assert.Equal(explicitExpiration.ToUnixTimeSeconds(), savedModel.ExpirationDateUnix);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_NoExpirationOrDuration_CreatesUnlimitedSubscription()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new CreateSubscriptionRequest
        {
            AccountId = accountId,
            ServiceId = serviceId
            // No ExpirationDate, no DurationDays
        };

        // Mock: Service exists
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                IsActive = true
            });

        // Mock: No existing subscriptions
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("subscription:")),
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Null(response.ExpirationDate);
        Assert.NotNull(savedModel);
        Assert.Null(savedModel.ExpirationDateUnix);
    }

    #endregion

    #region UpdateSubscriptionAsync Lock and Edge Cases

    [Fact]
    public async Task UpdateSubscriptionAsync_ShouldReturnConflict_WhenLockFails()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var request = new UpdateSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            IsActive = false
        };

        // Mock: Lock acquisition fails
        var mockFailedLock = new Mock<ILockResponse>();
        mockFailedLock.Setup(l => l.Success).Returns(false);
        mockFailedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockFailedLock.Object);

        // Act
        var (statusCode, response) = await service.UpdateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);

        // Verify no state store operations occurred
        _mockSubscriptionStore.Verify(s => s.GetAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_ShouldUpdateIsActive()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new UpdateSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            IsActive = false
        };

        // Mock: Subscription exists
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.UpdateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        Assert.False(savedModel.IsActive);
        Assert.NotNull(savedModel.UpdatedAtUnix);
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_ShouldUpdateBothFieldsTogether()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var newExpiration = DateTimeOffset.UtcNow.AddDays(60);
        var request = new UpdateSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            ExpirationDate = newExpiration,
            IsActive = false
        };

        // Mock: Subscription exists with original values
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.UpdateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        Assert.False(savedModel.IsActive);
        Assert.Equal(newExpiration.ToUnixTimeSeconds(), savedModel.ExpirationDateUnix);
        Assert.NotNull(savedModel.UpdatedAtUnix);
    }

    [Fact]
    public async Task UpdateSubscriptionAsync_ShouldPublishUpdatedEventWithCapturedData()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new UpdateSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            IsActive = false
        };

        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture published event
        SubscriptionUpdatedEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                "subscription.updated",
                It.IsAny<SubscriptionUpdatedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object, PublishOptions?, Guid?, CancellationToken>(
                (topic, evt, opts, corrId, ct) => capturedEvent = evt as SubscriptionUpdatedEvent)
            .ReturnsAsync(true);

        // Act
        var (statusCode, response) = await service.UpdateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(capturedEvent);
        Assert.Equal(subscriptionId, capturedEvent.SubscriptionId);
        Assert.Equal(accountId, capturedEvent.AccountId);
        Assert.Equal(SubscriptionAction.Updated, capturedEvent.Action);
        Assert.False(capturedEvent.IsActive);
    }

    #endregion

    #region RenewSubscriptionAsync Edge Cases

    [Fact]
    public async Task RenewSubscriptionAsync_ShouldReturnConflict_WhenLockFails()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var request = new RenewSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            ExtensionDays = 30
        };

        // Mock: Lock acquisition fails
        var mockFailedLock = new Mock<ILockResponse>();
        mockFailedLock.Setup(l => l.Success).Returns(false);
        mockFailedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockFailedLock.Object);

        // Act
        var (statusCode, response) = await service.RenewSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);
    }

    [Fact]
    public async Task RenewSubscriptionAsync_NewExpirationDate_SetsExactDate()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var exactNewExpiration = DateTimeOffset.UtcNow.AddDays(180);
        var request = new RenewSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            NewExpirationDate = exactNewExpiration
        };

        // Mock: Subscription exists with old expiration
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.RenewSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        Assert.Equal(exactNewExpiration.ToUnixTimeSeconds(), savedModel.ExpirationDateUnix);
        Assert.True(savedModel.IsActive);
    }

    [Fact]
    public async Task RenewSubscriptionAsync_ExtensionFromExpired_ExtendsFromNow()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var pastExpiration = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds();
        var request = new RenewSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            ExtensionDays = 30
        };

        // Mock: Expired subscription (expiration in the past)
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-40).ToUnixTimeSeconds(),
                ExpirationDateUnix = pastExpiration,
                IsActive = false,
                CancelledAtUnix = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeSeconds(),
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-40).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.RenewSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        Assert.True(savedModel.IsActive);
        Assert.Null(savedModel.CancelledAtUnix);
        Assert.Null(savedModel.CancellationReason);
        // Since expired, extension should be from now (~30 days from now), not from the past expiration
        var expectedMinExpiration = DateTimeOffset.UtcNow.AddDays(29).ToUnixTimeSeconds();
        var expectedMaxExpiration = DateTimeOffset.UtcNow.AddDays(31).ToUnixTimeSeconds();
        Assert.InRange(savedModel.ExpirationDateUnix ?? 0, expectedMinExpiration, expectedMaxExpiration);
    }

    [Fact]
    public async Task RenewSubscriptionAsync_WithNoExpirationDate_ExtendsFromNow()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new RenewSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            ExtensionDays = 30
        };

        // Mock: Subscription exists without expiration date (was unlimited)
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
                ExpirationDateUnix = null, // No expiration
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockSubscriptionStore
            .Setup(s => s.SaveAsync(
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SubscriptionDataModel, StateOptions?, CancellationToken>(
                (key, data, options, ct) => savedModel = data)
            .ReturnsAsync("etag");

        // Act
        var (statusCode, response) = await service.RenewSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(savedModel);
        // Without existing expiration, extension is from now
        var expectedMinExpiration = DateTimeOffset.UtcNow.AddDays(29).ToUnixTimeSeconds();
        var expectedMaxExpiration = DateTimeOffset.UtcNow.AddDays(31).ToUnixTimeSeconds();
        Assert.InRange(savedModel.ExpirationDateUnix ?? 0, expectedMinExpiration, expectedMaxExpiration);
    }

    #endregion

    #region QueryCurrentSubscriptionsAsync StubName Path

    [Fact]
    public async Task QueryCurrentSubscriptionsAsync_ByStubName_ReturnsMatchingSubscriptions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var request = new QueryCurrentSubscriptionsRequest { StubName = "arcadia" };

        // Mock: Service lookup by stub name
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.StubName == "arcadia"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "arcadia",
                DisplayName = "Arcadia",
                IsActive = true
            });

        // Mock: Service subscriptions index
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { subscriptionId });

        // Mock: Active subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{subscriptionId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId,
                AccountId = accountId,
                ServiceId = serviceId,
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.QueryCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Subscriptions);
        Assert.Equal("arcadia", response.Subscriptions.First().StubName);
        Assert.Single(response.AccountIds);
        Assert.Contains(accountId, response.AccountIds);
    }

    [Fact]
    public async Task QueryCurrentSubscriptionsAsync_ByStubName_ServiceNotFound_ReturnsEmpty()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryCurrentSubscriptionsRequest { StubName = "nonexistent-game" };

        // Mock: Service not found
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.StubName == "nonexistent-game"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));

        // Act
        var (statusCode, response) = await service.QueryCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Subscriptions);
        Assert.Empty(response.AccountIds);
    }

    [Fact]
    public async Task QueryCurrentSubscriptionsAsync_ByStubName_FiltersNonMatchingStubNames()
    {
        // Arrange
        var service = CreateService();
        var serviceId = Guid.NewGuid();
        var matchingSubId = Guid.NewGuid();
        var nonMatchingSubId = Guid.NewGuid();
        var request = new QueryCurrentSubscriptionsRequest { StubName = "arcadia" };

        // Mock: Service lookup
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.StubName == "arcadia"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "arcadia",
                DisplayName = "Arcadia",
                IsActive = true
            });

        // Mock: Service has two subscriptions
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { matchingSubId, nonMatchingSubId });

        // Mock: Matching subscription
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{matchingSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = matchingSubId,
                AccountId = Guid.NewGuid(),
                ServiceId = serviceId,
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Non-matching subscription (different stub name - edge case where index has stale data)
        _mockSubscriptionStore
            .Setup(s => s.GetAsync($"subscription:{nonMatchingSubId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = nonMatchingSubId,
                AccountId = Guid.NewGuid(),
                ServiceId = serviceId,
                StubName = "fantasia",
                DisplayName = "Fantasia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.QueryCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Single(response.Subscriptions);
        Assert.Equal("arcadia", response.Subscriptions.First().StubName);
    }

    #endregion

    #region CancelSubscriptionAsync Lock Failure

    [Fact]
    public async Task CancelSubscriptionAsync_ShouldReturnConflict_WhenLockFails()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var request = new CancelSubscriptionRequest
        {
            SubscriptionId = subscriptionId,
            Reason = "Test"
        };

        // Mock: Lock acquisition fails
        var mockFailedLock = new Mock<ILockResponse>();
        mockFailedLock.Setup(l => l.Success).Returns(false);
        mockFailedLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _mockLockProvider.Setup(l => l.LockAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockFailedLock.Object);

        // Act
        var (statusCode, response) = await service.CancelSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, statusCode);
        Assert.Null(response);
    }

    #endregion

    #region PublishSubscriptionClientEvent Exception Handling

    [Fact]
    public async Task CreateSubscriptionAsync_ClientEventPublishFailure_DoesNotAffectSuccess()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new CreateSubscriptionRequest
        {
            AccountId = accountId,
            ServiceId = serviceId,
            DurationDays = 30
        };

        // Mock: Service exists
        _mockServiceClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "test-game",
                DisplayName = "Test Game",
                IsActive = true
            });

        // Mock: No existing subscriptions
        _mockListStore
            .Setup(s => s.GetAsync($"account-subscriptions:{accountId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());

        // Mock: Entity session registry throws exception (client event publish failure)
        _mockEntitySessionRegistry
            .Setup(r => r.PublishToEntitySessionsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<BaseClientEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("WebSocket connection unavailable"));

        // Act - Should succeed despite client event failure
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert - Operation succeeds; client event failure is swallowed (best-effort)
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal("test-game", response.StubName);
        Assert.True(response.IsActive);
    }

    #endregion
}

public class SubscriptionConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new SubscriptionServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

}
