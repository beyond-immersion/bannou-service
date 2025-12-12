using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Servicedata;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Subscriptions;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BeyondImmersion.BannouService.Subscriptions.Tests;

/// <summary>
/// Unit tests for SubscriptionsService.
/// Tests business logic with mocked DaprClient and IServicedataClient.
/// </summary>
public class SubscriptionsServiceTests
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<SubscriptionsService>> _mockLogger;
    private readonly SubscriptionsServiceConfiguration _configuration;
    private readonly Mock<IServicedataClient> _mockServicedataClient;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private const string STATE_STORE = "subscriptions-statestore";

    public SubscriptionsServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<SubscriptionsService>>();
        _configuration = new SubscriptionsServiceConfiguration();
        _mockServicedataClient = new Mock<IServicedataClient>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
    }

    private SubscriptionsService CreateService()
    {
        return new SubscriptionsService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _configuration,
            _mockServicedataClient.Object,
            _mockErrorEventEmitter.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SubscriptionsService(
            null!,
            _mockLogger.Object,
            _configuration,
            _mockServicedataClient.Object,
            _mockErrorEventEmitter.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SubscriptionsService(
            _mockDaprClient.Object,
            null!,
            _configuration,
            _mockServicedataClient.Object,
            _mockErrorEventEmitter.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SubscriptionsService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _mockServicedataClient.Object,
            _mockErrorEventEmitter.Object));
    }

    [Fact]
    public void Constructor_WithNullServicedataClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SubscriptionsService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            _configuration,
            null!,
            _mockErrorEventEmitter.Object));
    }

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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { subscriptionId.ToString() });

        // Mock: Subscription data
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { activeSubId.ToString(), inactiveSubId.ToString() });

        // Mock: Active subscription
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{activeSubId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = activeSubId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Inactive subscription
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{inactiveSubId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = inactiveSubId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "fantasia",
                DisplayName = "Fantasia",
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
        Assert.Equal("arcadia", response.Subscriptions.First().StubName);
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { activeSubId.ToString(), expiredSubId.ToString() });

        // Mock: Active subscription (future expiration)
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{activeSubId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = activeSubId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Mock: Expired subscription
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{expiredSubId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = expiredSubId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "fantasia",
                DisplayName = "Fantasia",
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
        Assert.Equal("arcadia", response.Subscriptions.First().StubName);
    }

    [Fact]
    public async Task GetAccountSubscriptionsAsync_ShouldReturnEmptyList_WhenNoSubscriptions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var request = new GetAccountSubscriptionsRequest { AccountId = accountId };

        // Mock: No subscriptions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
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

    #region GetCurrentSubscriptionsAsync Tests

    [Fact]
    public async Task GetCurrentSubscriptionsAsync_ShouldReturnActiveNonExpiredOnly()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var activeSubId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new GetCurrentSubscriptionsRequest { AccountId = accountId };

        // Mock: Account subscription index
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { activeSubId.ToString() });

        // Mock: Active subscription
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{activeSubId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = activeSubId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Equal(accountId, response.AccountId);
        Assert.Single(response.Authorizations);
        Assert.Equal("arcadia:authorized", response.Authorizations.First());
    }

    [Fact]
    public async Task GetCurrentSubscriptionsAsync_ShouldExcludeExpiredSubscriptions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var expiredSubId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new GetCurrentSubscriptionsRequest { AccountId = accountId };

        // Mock: Account subscription index
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { expiredSubId.ToString() });

        // Mock: Expired subscription
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{expiredSubId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = expiredSubId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(), // Expired
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-60).ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Authorizations);
    }

    [Fact]
    public async Task GetCurrentSubscriptionsAsync_ShouldExcludeInactiveSubscriptions()
    {
        // Arrange
        var service = CreateService();
        var accountId = Guid.NewGuid();
        var inactiveSubId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var request = new GetCurrentSubscriptionsRequest { AccountId = accountId };

        // Mock: Account subscription index
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { inactiveSubId.ToString() });

        // Mock: Inactive subscription
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{inactiveSubId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = inactiveSubId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = false, // Inactive
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.GetCurrentSubscriptionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);
        Assert.NotNull(response);
        Assert.Empty(response.Authorizations);
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
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
        Assert.Equal("arcadia", response.StubName);
    }

    [Fact]
    public async Task GetSubscriptionAsync_ShouldReturnNotFound_WhenNotExists()
    {
        // Arrange
        var service = CreateService();
        var subscriptionId = Guid.NewGuid();
        var request = new GetSubscriptionRequest { SubscriptionId = subscriptionId };

        // Mock: Subscription doesn't exist
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
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
        _mockServicedataClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "arcadia",
                DisplayName = "Arcadia",
                IsActive = true
            });

        // Mock: No existing subscriptions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Mock: Service subscriptions index
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"service-subscriptions:{serviceId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);
        Assert.NotNull(response);
        Assert.Equal("arcadia", response.StubName);
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
        _mockServicedataClient
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
        _mockServicedataClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "arcadia",
                DisplayName = "Arcadia",
                IsActive = true
            });

        // Mock: Existing subscriptions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { existingSubId.ToString() });

        // Mock: Existing active subscription for same service
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{existingSubId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = existingSubId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
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
        _mockServicedataClient
            .Setup(c => c.GetServiceAsync(
                It.Is<GetServiceRequest>(r => r.ServiceId == serviceId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceInfo
            {
                ServiceId = serviceId,
                StubName = "arcadia",
                DisplayName = "Arcadia",
                IsActive = true
            });

        // Mock: No existing subscriptions
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"account-subscriptions:{accountId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Mock: Service subscriptions index
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>?>(
                STATE_STORE,
                $"service-subscriptions:{serviceId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        // Act
        var (statusCode, response) = await service.CreateSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Created, statusCode);

        // Verify event published
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "subscription.updated",
            It.Is<SubscriptionUpdatedEvent>(e =>
                e.AccountId == accountId &&
                e.Action == SubscriptionUpdatedEventAction.Created),
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionDataModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedModel = data);

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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionDataModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedModel = data);

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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        // Act
        var (statusCode, response) = await service.CancelSubscriptionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, statusCode);

        // Verify event published with Cancelled action
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "subscription.updated",
            It.Is<SubscriptionUpdatedEvent>(e =>
                e.SubscriptionId == subscriptionId &&
                e.Action == SubscriptionUpdatedEventAction.Cancelled),
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-25).ToUnixTimeSeconds(),
                ExpirationDateUnix = originalExpiration,
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-25).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionDataModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedModel = data);

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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
                IsActive = false,
                CancelledAtUnix = DateTimeOffset.UtcNow.AddDays(-5).ToUnixTimeSeconds(),
                CancellationReason = "Old reason",
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionDataModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedModel = data);

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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
                StartDateUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds(),
                ExpirationDateUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(), // Expired
                IsActive = true,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds()
            });

        // Capture saved model
        SubscriptionDataModel? savedModel = null;
        _mockDaprClient
            .Setup(d => d.SaveStateAsync(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<SubscriptionDataModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, SubscriptionDataModel, StateOptions?, IReadOnlyDictionary<string, string>?, CancellationToken>(
                (store, key, data, options, metadata, ct) => savedModel = data);

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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<SubscriptionDataModel?>(
                STATE_STORE,
                $"subscription:{subscriptionId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubscriptionDataModel
            {
                SubscriptionId = subscriptionId.ToString(),
                AccountId = accountId.ToString(),
                ServiceId = serviceId.ToString(),
                StubName = "arcadia",
                DisplayName = "Arcadia",
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

public class SubscriptionsConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new SubscriptionsServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void Configuration_StateStoreName_ShouldHaveDefault()
    {
        // Arrange
        var config = new SubscriptionsServiceConfiguration();

        // Act & Assert
        Assert.Equal("subscriptions-statestore", config.StateStoreName);
    }

    [Fact]
    public void Configuration_AuthorizationSuffix_ShouldHaveDefault()
    {
        // Arrange
        var config = new SubscriptionsServiceConfiguration();

        // Act & Assert
        Assert.Equal("authorized", config.AuthorizationSuffix);
    }
}
