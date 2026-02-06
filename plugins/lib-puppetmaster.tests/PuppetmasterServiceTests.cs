using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Puppetmaster;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;

namespace BeyondImmersion.BannouService.Puppetmaster.Tests;

/// <summary>
/// Unit tests for PuppetmasterService.
/// </summary>
public class PuppetmasterServiceTests
{
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<PuppetmasterService>> _mockLogger;
    private readonly PuppetmasterServiceConfiguration _configuration;
    private readonly Mock<BehaviorDocumentCache> _mockCache;

    public PuppetmasterServiceTests()
    {
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<PuppetmasterService>>();
        _configuration = new PuppetmasterServiceConfiguration();

        // Create mock for the cache - need to provide all constructor args
        var mockScopeFactory = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockCacheLogger = new Mock<ILogger<BehaviorDocumentCache>>();

        _mockCache = new Mock<BehaviorDocumentCache>(
            mockScopeFactory.Object,
            mockHttpClientFactory.Object,
            mockCacheLogger.Object,
            _configuration);
    }

    private PuppetmasterService CreateService()
    {
        return new PuppetmasterService(
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockCache.Object);
    }

    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void PuppetmasterService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<PuppetmasterService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void PuppetmasterServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new PuppetmasterServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void PuppetmasterServiceConfiguration_HasExpectedDefaults()
    {
        // Arrange & Act
        var config = new PuppetmasterServiceConfiguration();

        // Assert - verify defaults from schema
        Assert.Equal(1000, config.BehaviorCacheMaxSize);
        Assert.Equal(3600, config.BehaviorCacheTtlSeconds);
        Assert.Equal(30, config.AssetDownloadTimeoutSeconds);
    }

    #endregion

    #region GetStatusAsync Tests

    [Fact]
    public async Task GetStatusAsync_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        _mockCache.Setup(c => c.CachedCount).Returns(5);

        // Act
        var (status, response) = await service.GetStatusAsync(
            new GetStatusRequest(),
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsCachedBehaviorCount()
    {
        // Arrange
        var service = CreateService();
        _mockCache.Setup(c => c.CachedCount).Returns(42);

        // Act
        var (status, response) = await service.GetStatusAsync(
            new GetStatusRequest(),
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(42, response.CachedBehaviorCount);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsHealthyStatus()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (status, response) = await service.GetStatusAsync(
            new GetStatusRequest(),
            CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.True(response.IsHealthy);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsZeroActiveWatchers()
    {
        // Arrange - watchers are stub until Phase 2d
        var service = CreateService();

        // Act
        var (status, response) = await service.GetStatusAsync(
            new GetStatusRequest(),
            CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(0, response.ActiveWatcherCount);
    }

    #endregion

    #region InvalidateBehaviorsAsync Tests

    [Fact]
    public async Task InvalidateBehaviorsAsync_WithNullRef_InvalidatesAll()
    {
        // Arrange
        var service = CreateService();
        _mockCache.Setup(c => c.InvalidateAll()).Returns(10);

        // Act
        var (status, response) = await service.InvalidateBehaviorsAsync(
            new InvalidateBehaviorsRequest { BehaviorRef = null },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(10, response.InvalidatedCount);
        _mockCache.Verify(c => c.InvalidateAll(), Times.Once);
    }

    [Fact]
    public async Task InvalidateBehaviorsAsync_WithEmptyRef_InvalidatesAll()
    {
        // Arrange
        var service = CreateService();
        _mockCache.Setup(c => c.InvalidateAll()).Returns(5);

        // Act
        var (status, response) = await service.InvalidateBehaviorsAsync(
            new InvalidateBehaviorsRequest { BehaviorRef = "" },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(5, response.InvalidatedCount);
        _mockCache.Verify(c => c.InvalidateAll(), Times.Once);
    }

    [Fact]
    public async Task InvalidateBehaviorsAsync_WithSpecificRef_InvalidatesSingle()
    {
        // Arrange
        var service = CreateService();
        var behaviorRef = Guid.NewGuid().ToString();
        _mockCache.Setup(c => c.Invalidate(behaviorRef)).Returns(true);

        // Act
        var (status, response) = await service.InvalidateBehaviorsAsync(
            new InvalidateBehaviorsRequest { BehaviorRef = behaviorRef },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.InvalidatedCount);
        _mockCache.Verify(c => c.Invalidate(behaviorRef), Times.Once);
    }

    [Fact]
    public async Task InvalidateBehaviorsAsync_WithUnknownRef_ReturnsZero()
    {
        // Arrange
        var service = CreateService();
        var behaviorRef = Guid.NewGuid().ToString();
        _mockCache.Setup(c => c.Invalidate(behaviorRef)).Returns(false);

        // Act
        var (status, response) = await service.InvalidateBehaviorsAsync(
            new InvalidateBehaviorsRequest { BehaviorRef = behaviorRef },
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.InvalidatedCount);
    }

    [Fact]
    public async Task InvalidateBehaviorsAsync_PublishesEvent()
    {
        // Arrange
        var service = CreateService();
        _mockCache.Setup(c => c.InvalidateAll()).Returns(3);
        BehaviorInvalidatedEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                "puppetmaster.behavior.invalidated",
                It.IsAny<BehaviorInvalidatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, BehaviorInvalidatedEvent, CancellationToken>((topic, evt, ct) =>
            {
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        // Act
        await service.InvalidateBehaviorsAsync(
            new InvalidateBehaviorsRequest { BehaviorRef = null },
            CancellationToken.None);

        // Assert
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "puppetmaster.behavior.invalidated",
                It.IsAny<BehaviorInvalidatedEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(capturedEvent);
        Assert.Equal(3, capturedEvent.InvalidatedCount);
        Assert.Null(capturedEvent.BehaviorRef);
    }

    #endregion

    #region ListWatchersAsync Tests

    [Fact]
    public async Task ListWatchersAsync_ReturnsOK()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (status, response) = await service.ListWatchersAsync(
            new ListWatchersRequest(),
            CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ListWatchersAsync_ReturnsEmptyList()
    {
        // Arrange - watchers are stub until Phase 2d
        var service = CreateService();

        // Act
        var (status, response) = await service.ListWatchersAsync(
            new ListWatchersRequest(),
            CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Watchers);
        Assert.Empty(response.Watchers);
    }

    [Fact]
    public async Task ListWatchersAsync_WithRealmFilter_ReturnsEmptyList()
    {
        // Arrange - watchers are stub until Phase 2d
        var service = CreateService();
        var realmId = Guid.NewGuid();

        // Act
        var (status, response) = await service.ListWatchersAsync(
            new ListWatchersRequest { RealmId = realmId },
            CancellationToken.None);

        // Assert
        Assert.NotNull(response);
        Assert.NotNull(response.Watchers);
        Assert.Empty(response.Watchers);
    }

    #endregion
}
