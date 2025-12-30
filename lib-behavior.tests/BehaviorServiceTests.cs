using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.Bannou.Behavior.Goap;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests;

/// <summary>
/// Unit tests for BehaviorService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class BehaviorServiceTests
{
    private readonly Mock<ILogger<BehaviorService>> _mockLogger;
    private readonly Mock<BehaviorServiceConfiguration> _mockConfiguration;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IGoapPlanner> _mockGoapPlanner;
    private readonly BehaviorCompiler _compiler;
    private readonly Mock<IAssetClient> _mockAssetClient;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<BehaviorBundleManager> _mockBundleManager;

    public BehaviorServiceTests()
    {
        _mockLogger = new Mock<ILogger<BehaviorService>>();
        _mockConfiguration = new Mock<BehaviorServiceConfiguration>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockGoapPlanner = new Mock<IGoapPlanner>();
        _compiler = new BehaviorCompiler();
        _mockAssetClient = new Mock<IAssetClient>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockBundleManager = new Mock<BehaviorBundleManager>(
            Mock.Of<IStateStoreFactory>(),
            Mock.Of<IAssetClient>(),
            Mock.Of<ILogger<BehaviorBundleManager>>());
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var service = new BehaviorService(
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object,
            _mockGoapPlanner.Object,
            _compiler,
            _mockAssetClient.Object,
            _mockHttpClientFactory.Object,
            _mockBundleManager.Object);

        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullBundleManager_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BehaviorService(
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object,
            _mockGoapPlanner.Object,
            _compiler,
            _mockAssetClient.Object,
            _mockHttpClientFactory.Object,
            null!));
    }

    // Note: Additional tests for GOAP methods will be added in Phase 2.6.
}
