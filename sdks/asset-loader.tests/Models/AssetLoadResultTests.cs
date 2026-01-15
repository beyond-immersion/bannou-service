using BeyondImmersion.Bannou.AssetLoader.Models;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Tests.Models;

/// <summary>
/// Unit tests for AssetLoadResult and AssetAvailabilityResult models.
/// </summary>
public class AssetLoadResultTests
{
    #region AssetLoadResult<T>.Succeeded Tests

    /// <summary>
    /// Verifies that Succeeded creates a result with Success=true.
    /// </summary>
    [Fact]
    public void Succeeded_SetsSuccessToTrue()
    {
        // Arrange & Act
        var result = AssetLoadResult<string>.Succeeded("asset-1", "loaded value", "bundle-1");

        // Assert
        Assert.True(result.Success);
    }

    /// <summary>
    /// Verifies that Succeeded stores the asset correctly.
    /// </summary>
    [Fact]
    public void Succeeded_StoresAsset()
    {
        // Arrange
        var expectedValue = new TestAsset { Id = 42, Name = "Test" };

        // Act
        var result = AssetLoadResult<TestAsset>.Succeeded("asset-1", expectedValue, "bundle-1");

        // Assert
        Assert.NotNull(result.Asset);
        Assert.Equal(42, result.Asset.Id);
        Assert.Equal("Test", result.Asset.Name);
    }

    /// <summary>
    /// Verifies that Succeeded stores the bundle ID.
    /// </summary>
    [Fact]
    public void Succeeded_StoresBundleId()
    {
        // Act
        var result = AssetLoadResult<string>.Succeeded("asset-1", "value", "my-bundle");

        // Assert
        Assert.Equal("my-bundle", result.BundleId);
    }

    /// <summary>
    /// Verifies that Succeeded stores the asset ID.
    /// </summary>
    [Fact]
    public void Succeeded_StoresAssetId()
    {
        // Act
        var result = AssetLoadResult<string>.Succeeded("my-asset", "value", "bundle-1");

        // Assert
        Assert.Equal("my-asset", result.AssetId);
    }

    /// <summary>
    /// Verifies that Succeeded has null error message.
    /// </summary>
    [Fact]
    public void Succeeded_HasNullErrorMessage()
    {
        // Act
        var result = AssetLoadResult<string>.Succeeded("asset-1", "value", "bundle-1");

        // Assert
        Assert.Null(result.ErrorMessage);
    }

    #endregion

    #region AssetLoadResult<T>.Failed Tests

    /// <summary>
    /// Verifies that Failed creates a result with Success=false.
    /// </summary>
    [Fact]
    public void Failed_SetsSuccessToFalse()
    {
        // Act
        var result = AssetLoadResult<string>.Failed("asset-1", "Something went wrong");

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Verifies that Failed stores the error message.
    /// </summary>
    [Fact]
    public void Failed_StoresErrorMessage()
    {
        // Act
        var result = AssetLoadResult<string>.Failed("asset-1", "File not found");

        // Assert
        Assert.Equal("File not found", result.ErrorMessage);
    }

    /// <summary>
    /// Verifies that Failed stores the asset ID.
    /// </summary>
    [Fact]
    public void Failed_StoresAssetId()
    {
        // Act
        var result = AssetLoadResult<string>.Failed("missing-asset", "Not found");

        // Assert
        Assert.Equal("missing-asset", result.AssetId);
    }

    /// <summary>
    /// Verifies that Failed has null asset.
    /// </summary>
    [Fact]
    public void Failed_HasNullAsset()
    {
        // Act
        var result = AssetLoadResult<TestAsset>.Failed("asset-1", "Error");

        // Assert
        Assert.Null(result.Asset);
    }

    /// <summary>
    /// Verifies that Failed has null bundle ID.
    /// </summary>
    [Fact]
    public void Failed_HasNullBundleId()
    {
        // Act
        var result = AssetLoadResult<string>.Failed("asset-1", "Error");

        // Assert
        Assert.Null(result.BundleId);
    }

    #endregion

    #region AssetAvailabilityResult Tests

    /// <summary>
    /// Verifies that AllAvailable returns true when no unresolved assets.
    /// </summary>
    [Fact]
    public void AssetAvailabilityResult_AllAvailable_WhenNoUnresolved()
    {
        // Arrange
        var result = new AssetAvailabilityResult
        {
            RequestedAssetIds = new[] { "asset-1", "asset-2" },
            DownloadedBundleIds = new[] { "bundle-1" },
            UnresolvedAssetIds = Array.Empty<string>()
        };

        // Assert
        Assert.True(result.AllAvailable);
    }

    /// <summary>
    /// Verifies that AllAvailable returns false when there are unresolved assets.
    /// </summary>
    [Fact]
    public void AssetAvailabilityResult_AllAvailable_FalseWhenUnresolved()
    {
        // Arrange
        var result = new AssetAvailabilityResult
        {
            RequestedAssetIds = new[] { "asset-1", "asset-2" },
            DownloadedBundleIds = new[] { "bundle-1" },
            UnresolvedAssetIds = new[] { "asset-2" }
        };

        // Assert
        Assert.False(result.AllAvailable);
    }

    /// <summary>
    /// Verifies that AvailableCount calculates correctly.
    /// </summary>
    [Fact]
    public void AssetAvailabilityResult_AvailableCount_CalculatesCorrectly()
    {
        // Arrange
        var result = new AssetAvailabilityResult
        {
            RequestedAssetIds = new[] { "a1", "a2", "a3", "a4", "a5" },
            DownloadedBundleIds = new[] { "b1" },
            UnresolvedAssetIds = new[] { "a4", "a5" }
        };

        // Assert
        Assert.Equal(3, result.AvailableCount);
    }

    /// <summary>
    /// Verifies that AllAlreadyAvailable creates correct result.
    /// </summary>
    [Fact]
    public void AllAlreadyAvailable_CreatesCorrectResult()
    {
        // Arrange
        var assetIds = new[] { "asset-1", "asset-2", "asset-3" };

        // Act
        var result = AssetAvailabilityResult.AllAlreadyAvailable(assetIds);

        // Assert
        Assert.Equal(assetIds, result.RequestedAssetIds);
        Assert.Empty(result.DownloadedBundleIds);
        Assert.Empty(result.UnresolvedAssetIds);
        Assert.True(result.AllAvailable);
        Assert.Equal(3, result.AvailableCount);
    }

    #endregion

    #region BundleLoadResult.Success Tests

    /// <summary>
    /// Verifies that Success creates a result with Success status.
    /// </summary>
    [Fact]
    public void BundleLoadResult_Success_SetsStatusToSuccess()
    {
        // Act
        var result = BundleLoadResult.Success("bundle-1", 5);

        // Assert
        Assert.Equal(BundleLoadStatus.Success, result.Status);
    }

    /// <summary>
    /// Verifies that Success stores bundle ID and asset count.
    /// </summary>
    [Fact]
    public void BundleLoadResult_Success_StoresBundleIdAndAssetCount()
    {
        // Act
        var result = BundleLoadResult.Success("my-bundle", 10);

        // Assert
        Assert.Equal("my-bundle", result.BundleId);
        Assert.Equal(10, result.AssetCount);
    }

    /// <summary>
    /// Verifies that Success stores optional parameters.
    /// </summary>
    [Fact]
    public void BundleLoadResult_Success_StoresOptionalParameters()
    {
        // Act
        var result = BundleLoadResult.Success(
            "bundle-1",
            assetCount: 5,
            fromCache: true,
            downloadTimeMs: 150,
            sizeBytes: 1024);

        // Assert
        Assert.True(result.FromCache);
        Assert.Equal(150, result.DownloadTimeMs);
        Assert.Equal(1024, result.SizeBytes);
    }

    /// <summary>
    /// Verifies that Success has null error message.
    /// </summary>
    [Fact]
    public void BundleLoadResult_Success_HasNullErrorMessage()
    {
        // Act
        var result = BundleLoadResult.Success("bundle-1", 5);

        // Assert
        Assert.Null(result.ErrorMessage);
    }

    #endregion

    #region BundleLoadResult.AlreadyLoaded Tests

    /// <summary>
    /// Verifies that AlreadyLoaded creates correct status.
    /// </summary>
    [Fact]
    public void BundleLoadResult_AlreadyLoaded_SetsStatusToAlreadyLoaded()
    {
        // Act
        var result = BundleLoadResult.AlreadyLoaded("bundle-1");

        // Assert
        Assert.Equal(BundleLoadStatus.AlreadyLoaded, result.Status);
    }

    /// <summary>
    /// Verifies that AlreadyLoaded stores bundle ID.
    /// </summary>
    [Fact]
    public void BundleLoadResult_AlreadyLoaded_StoresBundleId()
    {
        // Act
        var result = BundleLoadResult.AlreadyLoaded("existing-bundle");

        // Assert
        Assert.Equal("existing-bundle", result.BundleId);
    }

    #endregion

    #region BundleLoadResult.Failed Tests

    /// <summary>
    /// Verifies that Failed creates correct status.
    /// </summary>
    [Fact]
    public void BundleLoadResult_Failed_SetsStatusToFailed()
    {
        // Act
        var result = BundleLoadResult.Failed("bundle-1", "Download error");

        // Assert
        Assert.Equal(BundleLoadStatus.Failed, result.Status);
    }

    /// <summary>
    /// Verifies that Failed stores error message.
    /// </summary>
    [Fact]
    public void BundleLoadResult_Failed_StoresErrorMessage()
    {
        // Act
        var result = BundleLoadResult.Failed("bundle-1", "Connection timeout");

        // Assert
        Assert.Equal("Connection timeout", result.ErrorMessage);
    }

    /// <summary>
    /// Verifies that Failed stores bundle ID.
    /// </summary>
    [Fact]
    public void BundleLoadResult_Failed_StoresBundleId()
    {
        // Act
        var result = BundleLoadResult.Failed("failed-bundle", "Error");

        // Assert
        Assert.Equal("failed-bundle", result.BundleId);
    }

    #endregion

    #region Test Types

    private class TestAsset
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    #endregion
}
