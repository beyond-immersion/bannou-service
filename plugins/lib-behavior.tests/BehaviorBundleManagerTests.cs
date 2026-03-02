using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly Mock<IStateStore<CachedGoapMetadata>> _mockGoapMetadataStore;
    private readonly BehaviorServiceConfiguration _configuration;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public BehaviorBundleManagerTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockAssetClient = new Mock<IAssetClient>();
        _mockLogger = new Mock<ILogger<BehaviorBundleManager>>();
        _mockMetadataStore = new Mock<IStateStore<BehaviorMetadata>>();
        _mockMembershipStore = new Mock<IStateStore<BundleMembership>>();
        _mockGoapMetadataStore = new Mock<IStateStore<CachedGoapMetadata>>();
        _configuration = new BehaviorServiceConfiguration();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Setup state store factory to return appropriate stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<BehaviorMetadata>(It.IsAny<string>()))
            .Returns(_mockMetadataStore.Object);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<BundleMembership>(It.IsAny<string>()))
            .Returns(_mockMembershipStore.Object);

        _mockStateStoreFactory
            .Setup(f => f.GetStore<CachedGoapMetadata>(It.IsAny<string>()))
            .Returns(_mockGoapMetadataStore.Object);
    }

    private BehaviorBundleManager CreateManager() =>
        new BehaviorBundleManager(
            _mockStateStoreFactory.Object,
            _mockAssetClient.Object,
            _configuration,
            _mockLogger.Object,
            _mockTelemetryProvider.Object);

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<BehaviorBundleManager>();
        Assert.NotNull(CreateManager());
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

    #region GOAP Metadata Caching Tests

    [Fact]
    public async Task SaveGoapMetadataAsync_ValidMetadata_SavesCorrectly()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-with-goap";
        var metadata = new CachedGoapMetadata
        {
            BehaviorId = behaviorId,
            Goals = new List<CachedGoapGoal>
            {
                new CachedGoapGoal
                {
                    Name = "find_food",
                    Priority = 80,
                    Conditions = new Dictionary<string, string> { { "has_food", "==true" } }
                }
            },
            Actions = new List<CachedGoapAction>
            {
                new CachedGoapAction
                {
                    FlowName = "gather_food",
                    Preconditions = new Dictionary<string, string> { { "energy", ">=20" } },
                    Effects = new Dictionary<string, string> { { "has_food", "true" } },
                    Cost = 2.0f
                }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await manager.SaveGoapMetadataAsync(behaviorId, metadata);

        // Assert
        _mockGoapMetadataStore.Verify(s => s.SaveAsync(
            It.Is<string>(k => k.Contains(behaviorId)),
            It.Is<CachedGoapMetadata>(m =>
                m.BehaviorId == behaviorId &&
                m.Goals.Count == 1 &&
                m.Actions.Count == 1),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveGoapMetadataAsync_SetsBehaviorIdOnMetadata()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-abc123";
        var metadata = new CachedGoapMetadata
        {
            BehaviorId = string.Empty, // Will be set by SaveGoapMetadataAsync
            Goals = new List<CachedGoapGoal>(),
            Actions = new List<CachedGoapAction>()
        };

        CachedGoapMetadata? savedMetadata = null;
        _mockGoapMetadataStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CachedGoapMetadata>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CachedGoapMetadata, StateOptions?, CancellationToken>((k, m, o, c) => savedMetadata = m)
            .ReturnsAsync(behaviorId);

        // Act
        await manager.SaveGoapMetadataAsync(behaviorId, metadata);

        // Assert
        Assert.NotNull(savedMetadata);
        Assert.Equal(behaviorId, savedMetadata.BehaviorId);
    }

    [Fact]
    public async Task GetGoapMetadataAsync_ExistingMetadata_ReturnsMetadata()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-with-goap";
        var expectedMetadata = new CachedGoapMetadata
        {
            BehaviorId = behaviorId,
            Goals = new List<CachedGoapGoal>
            {
                new CachedGoapGoal { Name = "survive", Priority = 100 }
            },
            Actions = new List<CachedGoapAction>
            {
                new CachedGoapAction { FlowName = "flee", Cost = 1.0f }
            }
        };

        _mockGoapMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMetadata);

        // Act
        var result = await manager.GetGoapMetadataAsync(behaviorId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(behaviorId, result.BehaviorId);
        Assert.Single(result.Goals);
        Assert.Single(result.Actions);
        Assert.Equal("survive", result.Goals[0].Name);
        Assert.Equal("flee", result.Actions[0].FlowName);
    }

    [Fact]
    public async Task GetGoapMetadataAsync_NonExistentBehavior_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "nonexistent-behavior";

        _mockGoapMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CachedGoapMetadata?)null);

        // Act
        var result = await manager.GetGoapMetadataAsync(behaviorId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveGoapMetadataAsync_ExistingMetadata_DeletesAndReturnsTrue()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "behavior-with-goap";
        var existingMetadata = new CachedGoapMetadata { BehaviorId = behaviorId };

        _mockGoapMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMetadata);

        _mockGoapMetadataStore
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await manager.RemoveGoapMetadataAsync(behaviorId);

        // Assert
        Assert.True(result);
        _mockGoapMetadataStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(behaviorId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveGoapMetadataAsync_NonExistentMetadata_ReturnsFalse()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "nonexistent-behavior";

        _mockGoapMetadataStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CachedGoapMetadata?)null);

        // Act
        var result = await manager.RemoveGoapMetadataAsync(behaviorId);

        // Assert
        Assert.False(result);
        _mockGoapMetadataStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveGoapMetadataAsync_ComplexMetadata_PreservesAllFields()
    {
        // Arrange
        var manager = CreateManager();
        var behaviorId = "complex-behavior";
        var now = DateTimeOffset.UtcNow;
        var metadata = new CachedGoapMetadata
        {
            BehaviorId = behaviorId,
            Goals = new List<CachedGoapGoal>
            {
                new CachedGoapGoal
                {
                    Name = "goal1",
                    Priority = 100,
                    Conditions = new Dictionary<string, string>
                    {
                        { "health", ">=80" },
                        { "is_safe", "==true" }
                    }
                },
                new CachedGoapGoal
                {
                    Name = "goal2",
                    Priority = 50,
                    Conditions = new Dictionary<string, string> { { "has_weapon", "==true" } }
                }
            },
            Actions = new List<CachedGoapAction>
            {
                new CachedGoapAction
                {
                    FlowName = "attack",
                    Preconditions = new Dictionary<string, string>
                    {
                        { "has_weapon", "==true" },
                        { "enemy_visible", "==true" }
                    },
                    Effects = new Dictionary<string, string>
                    {
                        { "enemy_health", "-=20" }
                    },
                    Cost = 3.5f
                },
                new CachedGoapAction
                {
                    FlowName = "heal",
                    Preconditions = new Dictionary<string, string> { { "has_potion", "==true" } },
                    Effects = new Dictionary<string, string> { { "health", "+=30" } },
                    Cost = 1.0f
                }
            },
            CreatedAt = now
        };

        CachedGoapMetadata? savedMetadata = null;
        _mockGoapMetadataStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CachedGoapMetadata>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CachedGoapMetadata, StateOptions?, CancellationToken>((k, m, o, c) => savedMetadata = m)
            .ReturnsAsync(behaviorId);

        // Act
        await manager.SaveGoapMetadataAsync(behaviorId, metadata);

        // Assert
        Assert.NotNull(savedMetadata);
        Assert.Equal(2, savedMetadata.Goals.Count);
        Assert.Equal(2, savedMetadata.Actions.Count);
        Assert.Equal(100, savedMetadata.Goals[0].Priority);
        Assert.Equal(2, savedMetadata.Goals[0].Conditions.Count);
        Assert.Equal(3.5f, savedMetadata.Actions[0].Cost);
        Assert.Equal(2, savedMetadata.Actions[0].Preconditions.Count);
    }

    #endregion
}
