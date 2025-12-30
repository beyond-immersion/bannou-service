using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests;

/// <summary>
/// Unit tests for BehaviorBundleManager.
/// Tests bundle membership tracking, metadata management, and state store interactions.
/// </summary>
public class BehaviorBundleManagerTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IAssetClient> _mockAssetClient;
    private readonly Mock<ILogger<BehaviorBundleManager>> _mockLogger;
    private readonly Mock<IStateStore<BehaviorMetadata>> _mockMetadataStore;
    private readonly Mock<IStateStore<BundleMembership>> _mockMembershipStore;

    public BehaviorBundleManagerTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockAssetClient = new Mock<IAssetClient>();
        _mockLogger = new Mock<ILogger<BehaviorBundleManager>>();
        _mockMetadataStore = new Mock<IStateStore<BehaviorMetadata>>();
        _mockMembershipStore = new Mock<IStateStore<BundleMembership>>();

        // Setup state store factory to return appropriate stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<BehaviorMetadata>(It.IsAny<string>()))
            .Returns(_mockMetadataStore.Object);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<BundleMembership>(It.IsAny<string>()))
            .Returns(_mockMembershipStore.Object);
    }

    private BehaviorBundleManager CreateManager() =>
        new BehaviorBundleManager(
            _mockStateStoreFactory.Object,
            _mockAssetClient.Object,
            _mockLogger.Object);

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var manager = CreateManager();

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BehaviorBundleManager(
            null!,
            _mockAssetClient.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullAssetClient_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BehaviorBundleManager(
            _mockStateStoreFactory.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new BehaviorBundleManager(
            _mockStateStoreFactory.Object,
            _mockAssetClient.Object,
            null!));
    }

    #endregion

    #region RecordBehaviorAsync Tests

    [Fact]
    public async Task RecordBehaviorAsync_NewBehavior_ReturnsTrue()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-abc123";
        var assetId = "asset-123";
        var metadata = new BehaviorMetadata { Name = "test-behavior" };

        _mockMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BehaviorMetadata?)null);

        // Act
        var isNew = await manager.RecordBehaviorAsync(behaviorId, assetId, null, metadata);

        // Assert
        Assert.True(isNew);
        _mockMetadataStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(behaviorId)),
            It.IsAny<BehaviorMetadata>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordBehaviorAsync_ExistingBehavior_ReturnsFalse()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-abc123";
        var assetId = "asset-123";
        var existingMetadata = new BehaviorMetadata
        {
            Name = "existing-behavior",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        var newMetadata = new BehaviorMetadata { Name = "test-behavior" };

        _mockMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMetadata);

        // Act
        var isNew = await manager.RecordBehaviorAsync(behaviorId, assetId, null, newMetadata);

        // Assert
        Assert.False(isNew);
    }

    [Fact]
    public async Task RecordBehaviorAsync_WithBundleId_AddsToBundleMembership()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-abc123";
        var assetId = "asset-123";
        var bundleId = "test-bundle";
        var metadata = new BehaviorMetadata { Name = "test-behavior" };

        _mockMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BehaviorMetadata?)null);

        _mockMembershipStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMembership?)null);

        // Act
        await manager.RecordBehaviorAsync(behaviorId, assetId, bundleId, metadata);

        // Assert
        _mockMembershipStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(bundleId)),
            It.Is<BundleMembership>(m => m.BehaviorAssetIds.ContainsKey(behaviorId)),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordBehaviorAsync_NullBehaviorId_Throws()
    {
        // Arrange
        var manager = CreateManager();
        var metadata = new BehaviorMetadata { Name = "test" };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            manager.RecordBehaviorAsync(null!, "asset-123", null, metadata));
    }

    [Fact]
    public async Task RecordBehaviorAsync_NullMetadata_Throws()
    {
        // Arrange
        var manager = CreateManager();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            manager.RecordBehaviorAsync("behavior-123", "asset-123", null, null!));
    }

    #endregion

    #region AddToBundleAsync Tests

    [Fact]
    public async Task AddToBundleAsync_NewBundle_CreatesBundle()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-abc123";
        var assetId = "asset-123";
        var bundleId = "new-bundle";

        _mockMembershipStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMembership?)null);

        // Act
        await manager.AddToBundleAsync(behaviorId, assetId, bundleId);

        // Assert
        _mockMembershipStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.Is<BundleMembership>(m =>
                m.BundleId == bundleId &&
                m.BehaviorAssetIds[behaviorId] == assetId),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddToBundleAsync_ExistingBundle_AppendsToBehaviorList()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-new";
        var assetId = "asset-new";
        var bundleId = "existing-bundle";
        var existingMembership = new BundleMembership
        {
            BundleId = bundleId,
            BehaviorAssetIds = new Dictionary<string, string>
            {
                { "behavior-existing", "asset-existing" }
            }
        };

        _mockMembershipStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMembership);

        // Act
        await manager.AddToBundleAsync(behaviorId, assetId, bundleId);

        // Assert
        _mockMembershipStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.Is<BundleMembership>(m =>
                m.BehaviorAssetIds.Count == 2 &&
                m.BehaviorAssetIds.ContainsKey(behaviorId) &&
                m.BehaviorAssetIds.ContainsKey("behavior-existing")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region GetMetadataAsync Tests

    [Fact]
    public async Task GetMetadataAsync_ExistingBehavior_ReturnsMetadata()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-abc123";
        var expectedMetadata = new BehaviorMetadata { Name = "test-behavior" };

        _mockMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMetadata);

        // Act
        var result = await manager.GetMetadataAsync(behaviorId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedMetadata.Name, result.Name);
    }

    [Fact]
    public async Task GetMetadataAsync_NonExistentBehavior_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "nonexistent";

        _mockMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BehaviorMetadata?)null);

        // Act
        var result = await manager.GetMetadataAsync(behaviorId);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region RemoveBehaviorAsync Tests

    [Fact]
    public async Task RemoveBehaviorAsync_ExistingBehavior_ReturnsMetadataAndDeletes()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-abc123";
        var metadata = new BehaviorMetadata
        {
            BehaviorId = behaviorId,
            Name = "test-behavior",
            BundleId = null
        };

        _mockMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        // Act
        var result = await manager.RemoveBehaviorAsync(behaviorId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(metadata.Name, result.Name);
        _mockMetadataStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(behaviorId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveBehaviorAsync_BehaviorInBundle_RemovesFromBundleMembership()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-abc123";
        var bundleId = "test-bundle";
        var metadata = new BehaviorMetadata
        {
            BehaviorId = behaviorId,
            Name = "test-behavior",
            BundleId = bundleId
        };
        var membership = new BundleMembership
        {
            BundleId = bundleId,
            BehaviorAssetIds = new Dictionary<string, string>
            {
                { behaviorId, "asset-123" },
                { "other-behavior", "other-asset" }
            }
        };

        _mockMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        _mockMembershipStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        // Act
        await manager.RemoveBehaviorAsync(behaviorId);

        // Assert
        _mockMembershipStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.Is<BundleMembership>(m =>
                !m.BehaviorAssetIds.ContainsKey(behaviorId) &&
                m.BehaviorAssetIds.ContainsKey("other-behavior")),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveBehaviorAsync_NonExistentBehavior_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "nonexistent";

        _mockMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BehaviorMetadata?)null);

        // Act
        var result = await manager.RemoveBehaviorAsync(behaviorId);

        // Assert
        Assert.Null(result);
        _mockMetadataStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region GetBundleMembershipAsync Tests

    [Fact]
    public async Task GetBundleMembershipAsync_ExistingBundle_ReturnsMembership()
    {
        // Arrange
        var manager = CreateManager();
        var bundleId = "test-bundle";
        var membership = new BundleMembership
        {
            BundleId = bundleId,
            BehaviorAssetIds = new Dictionary<string, string>
            {
                { "behavior-1", "asset-1" },
                { "behavior-2", "asset-2" }
            }
        };

        _mockMembershipStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);

        // Act
        var result = await manager.GetBundleMembershipAsync(bundleId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(bundleId, result.BundleId);
        Assert.Equal(2, result.BehaviorAssetIds.Count);
    }

    #endregion
}
