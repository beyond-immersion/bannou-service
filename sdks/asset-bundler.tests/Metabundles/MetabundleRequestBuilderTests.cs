using BeyondImmersion.Bannou.AssetBundler.Metabundles;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Metabundles;

/// <summary>
/// Tests for MetabundleRequestBuilder fluent builder.
/// </summary>
public class MetabundleRequestBuilderTests
{
    [Fact]
    public void Build_WithRequiredFields_CreatesValidRequest()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("my-metabundle")
            .AddSourceBundle("bundle-1")
            .WithOwner("test-owner");

        // Act
        var request = builder.Build();

        // Assert
        Assert.Equal("my-metabundle", request.MetabundleId);
        Assert.Single(request.SourceBundleIds);
        Assert.Contains("bundle-1", request.SourceBundleIds);
        Assert.Equal("test-owner", request.Owner);
    }

    [Fact]
    public void Build_WithMultipleSourceBundles_IncludesAll()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("multi-source")
            .AddSourceBundle("bundle-1")
            .AddSourceBundle("bundle-2")
            .AddSourceBundle("bundle-3");

        // Act
        var request = builder.Build();

        // Assert
        Assert.Equal(3, request.SourceBundleIds.Count);
        Assert.Contains("bundle-1", request.SourceBundleIds);
        Assert.Contains("bundle-2", request.SourceBundleIds);
        Assert.Contains("bundle-3", request.SourceBundleIds);
    }

    [Fact]
    public void Build_AddSourceBundles_AddsMultipleAtOnce()
    {
        // Arrange
        var bundleIds = new[] { "b1", "b2", "b3", "b4" };
        var builder = new MetabundleRequestBuilder()
            .WithId("batch")
            .AddSourceBundles(bundleIds);

        // Act
        var request = builder.Build();

        // Assert
        Assert.Equal(4, request.SourceBundleIds.Count);
        foreach (var id in bundleIds)
        {
            Assert.Contains(id, request.SourceBundleIds);
        }
    }

    [Fact]
    public void Build_WithStandaloneAssets_IncludesStandaloneAssets()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("with-standalone")
            .AddSourceBundle("bundle-1")
            .AddStandaloneAsset("standalone-asset-1")
            .AddStandaloneAsset("standalone-asset-2");

        // Act
        var request = builder.Build();

        // Assert
        Assert.NotNull(request.StandaloneAssetIds);
        Assert.Equal(2, request.StandaloneAssetIds.Count);
        Assert.Contains("standalone-asset-1", request.StandaloneAssetIds);
        Assert.Contains("standalone-asset-2", request.StandaloneAssetIds);
    }

    [Fact]
    public void Build_WithAssetFilter_IncludesFilter()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("filtered")
            .AddSourceBundle("bundle-1")
            .WithAssetFilter(new[] { "asset-1", "asset-2", "asset-3" });

        // Act
        var request = builder.Build();

        // Assert
        Assert.NotNull(request.AssetFilter);
        Assert.Equal(3, request.AssetFilter.Count);
    }

    [Fact]
    public void Build_WithVersion_SetsVersion()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("versioned")
            .AddSourceBundle("bundle-1")
            .WithVersion("2.0.0");

        // Act
        var request = builder.Build();

        // Assert
        Assert.Equal("2.0.0", request.Version);
    }

    [Fact]
    public void Build_WithRealm_SetsRealm()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("realmed")
            .AddSourceBundle("bundle-1")
            .WithRealm(Realm.Private);

        // Act
        var request = builder.Build();

        // Assert
        Assert.Equal(Realm.Private, request.Realm);
    }

    [Fact]
    public void Build_DefaultRealm_IsShared()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("default-realm")
            .AddSourceBundle("bundle-1");

        // Act
        var request = builder.Build();

        // Assert
        Assert.Equal(Realm.Shared, request.Realm);
    }

    [Fact]
    public void Build_MissingId_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .AddSourceBundle("bundle-1");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_NoSourcesOrStandalone_ThrowsInvalidOperationException()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("empty-sources");

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Build_OnlyStandaloneAssets_CreatesValidRequest()
    {
        // Arrange - No source bundles, only standalone assets
        var builder = new MetabundleRequestBuilder()
            .WithId("standalone-only")
            .AddStandaloneAsset("asset-1")
            .AddStandaloneAsset("asset-2");

        // Act
        var request = builder.Build();

        // Assert
        Assert.Empty(request.SourceBundleIds);
        Assert.NotNull(request.StandaloneAssetIds);
        Assert.Equal(2, request.StandaloneAssetIds.Count);
    }

    [Fact]
    public void Build_FluentChaining_Works()
    {
        // Arrange & Act
        var request = new MetabundleRequestBuilder()
            .WithId("chained")
            .AddSourceBundle("b1")
            .AddSourceBundle("b2")
            .AddStandaloneAsset("a1")
            .WithVersion("1.0.0")
            .WithOwner("owner")
            .WithRealm(Realm.Private)
            .WithAssetFilter(new[] { "filter1" })
            .Build();

        // Assert
        Assert.Equal("chained", request.MetabundleId);
        Assert.Equal(2, request.SourceBundleIds.Count);
        Assert.NotNull(request.StandaloneAssetIds);
        Assert.Single(request.StandaloneAssetIds);
        Assert.Equal("1.0.0", request.Version);
        Assert.Equal("owner", request.Owner);
        Assert.Equal(Realm.Private, request.Realm);
        Assert.NotNull(request.AssetFilter);
        Assert.Single(request.AssetFilter);
    }

    [Fact]
    public void Build_NoAssetFilter_ReturnsNullFilter()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("no-filter")
            .AddSourceBundle("bundle-1");

        // Act
        var request = builder.Build();

        // Assert
        Assert.Null(request.AssetFilter);
    }

    [Fact]
    public void Build_NoStandaloneAssets_ReturnsNullStandalone()
    {
        // Arrange
        var builder = new MetabundleRequestBuilder()
            .WithId("no-standalone")
            .AddSourceBundle("bundle-1");

        // Act
        var request = builder.Build();

        // Assert
        Assert.Null(request.StandaloneAssetIds);
    }
}
