using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
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
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<SubscriptionService>> _mockLogger;
    private readonly SubscriptionServiceConfiguration _configuration;
    private readonly Mock<IGameServiceClient> _mockServiceClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private const string STATE_STORE = "subscription-statestore";

    public SubscriptionServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockSubscriptionStore = new Mock<IStateStore<SubscriptionDataModel>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<SubscriptionService>>();
        _configuration = new SubscriptionServiceConfiguration();
        _mockServiceClient = new Mock<IGameServiceClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<SubscriptionDataModel>(STATE_STORE))
            .Returns(_mockSubscriptionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockListStore.Object);

        // Setup default behavior for stores
        _mockSubscriptionStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<SubscriptionDataModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockListStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup default behavior for message bus
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private SubscriptionService CreateService()
    {
        return new SubscriptionService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockServiceClient.Object,
            _mockEventConsumer.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    /// </summary>
    [Fact]
    public void SubscriptionService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<SubscriptionService>();

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
            .ReturnsAsync(new List<string> { subscriptionId.ToString() });

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
        Assert.Equal(1, response.TotalCount);
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
            .ReturnsAsync(new List<string> { activeSubId.ToString(), inactiveSubId.ToString() });

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
        Assert.Equal(1, response.TotalCount);
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
            .ReturnsAsync(new List<string> { activeSubId.ToString(), expiredSubId.ToString() });

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
        Assert.Equal(1, response.TotalCount);
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
            .ReturnsAsync((List<string>?)null);

        // Act
        var (statusCode, response) = await service.GetAccountSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Subscriptions);
        Assert.Equal(0, response.TotalCount);
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
            .ReturnsAsync(new List<string> { activeSubId.ToString() });

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
        Assert.Equal(1, response.TotalCount);
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
            .ReturnsAsync(new List<string> { expiredSubId.ToString() });

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
        Assert.Equal(0, response.TotalCount);
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
            .ReturnsAsync(new List<string> { inactiveSubId.ToString() });

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
        Assert.Equal(0, response.TotalCount);
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
            .ReturnsAsync(new List<string>());

        // Mock: Service subscriptions index
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

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
            .ThrowsAsync(new ApiException("Not found", 404, "", new Dictionary<string, IEnumerable<string>>(), null));

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
            .ReturnsAsync(new List<string> { existingSubId.ToString() });

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
            .ReturnsAsync(new List<string>());

        // Mock: Service subscriptions index
        _mockListStore
            .Setup(s => s.GetAsync($"service-subscriptions:{serviceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify event published via IMessageBus
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "subscription.updated",
            It.Is<SubscriptionUpdatedEvent>(e =>
                e.AccountId == accountId &&
                e.Action == SubscriptionUpdatedEventAction.Created),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
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
                e.Action == SubscriptionUpdatedEventAction.Cancelled),
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
        var result = await service.ExpireSubscriptionAsync(subscriptionId.ToString(), CancellationToken.None);

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
        var result = await service.ExpireSubscriptionAsync(subscriptionId.ToString(), CancellationToken.None);

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
        var result = await service.ExpireSubscriptionAsync(subscriptionId.ToString(), CancellationToken.None);

        // Assert
        Assert.False(result);
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

    [Fact]
    public void Configuration_AuthorizationSuffix_ShouldHaveDefault()
    {
        // Arrange
        var config = new SubscriptionServiceConfiguration();

        // Act & Assert
        Assert.Equal("authorized", config.AuthorizationSuffix);
    }
}
