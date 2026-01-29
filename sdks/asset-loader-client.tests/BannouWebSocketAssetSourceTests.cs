using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Asset;
using Moq;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Client.Tests;

/// <summary>
/// Unit tests for BannouWebSocketAssetSource.
/// Tests constructor validation, property behaviors, and connected-state API operations.
/// </summary>
public class BannouWebSocketAssetSourceTests : IAsyncLifetime
{
    private BannouClient? _realClient;

    /// <summary>
    /// Initializes test resources.
    /// </summary>
    public Task InitializeAsync()
    {
        _realClient = new BannouClient();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Cleans up test resources.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_realClient != null)
        {
            await _realClient.DisposeAsync();
        }
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that constructor accepts valid client.
    /// </summary>
    [Fact]
    public async Task Constructor_ValidClient_CreatesInstance()
    {
        // Act
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Assert
        Assert.NotNull(source);
    }

    /// <summary>
    /// Verifies that constructor accepts optional logger.
    /// </summary>
    [Fact]
    public async Task Constructor_WithLogger_CreatesInstance()
    {
        // Act
        await using var source = new BannouWebSocketAssetSource(_realClient!, logger: null);

        // Assert
        Assert.NotNull(source);
    }

    /// <summary>
    /// Verifies that constructor accepts mocked IBannouClient.
    /// </summary>
    [Fact]
    public async Task Constructor_MockedClient_CreatesInstance()
    {
        // Arrange
        var mockClient = new Mock<IBannouClient>();

        // Act
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        // Assert
        Assert.NotNull(source);
    }

    #endregion

    #region Property Tests

    /// <summary>
    /// Verifies that RequiresAuthentication is true.
    /// </summary>
    [Fact]
    public async Task RequiresAuthentication_ReturnsTrue()
    {
        // Arrange
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Assert
        Assert.True(source.RequiresAuthentication);
    }

    /// <summary>
    /// Verifies that IsAvailable reflects client connection state.
    /// </summary>
    [Fact]
    public async Task IsAvailable_WhenDisconnected_ReturnsFalse()
    {
        // Arrange
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Assert - client is not connected
        Assert.False(source.IsAvailable);
    }

    /// <summary>
    /// Verifies that IsAvailable returns true when mock client is connected.
    /// </summary>
    [Fact]
    public async Task IsAvailable_WhenConnected_ReturnsTrue()
    {
        // Arrange
        var mockClient = new Mock<IBannouClient>();
        mockClient.Setup(c => c.IsConnected).Returns(true);
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        // Assert
        Assert.True(source.IsAvailable);
    }

    #endregion

    #region API Method Tests - Disconnected State

    /// <summary>
    /// Verifies that ResolveBundlesAsync throws when not connected.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ResolveBundlesAsync(new[] { "asset-1" }));
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync throws when not connected.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.GetBundleDownloadInfoAsync("bundle-1"));
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync validates empty input.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_EmptyBundleId_ThrowsArgumentException()
    {
        // Arrange
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.GetBundleDownloadInfoAsync(""));
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync throws when not connected.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.GetAssetDownloadInfoAsync("asset-1"));
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync validates empty input.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_EmptyAssetId_ThrowsArgumentException()
    {
        // Arrange
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.GetAssetDownloadInfoAsync(""));
    }

    #endregion

    #region Connected State API Tests - ResolveBundlesAsync

    /// <summary>
    /// Verifies that ResolveBundlesAsync calls client with correct request.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WhenConnected_CallsClientWithCorrectRequest()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);
        var assetIds = new[] { "asset-1", "asset-2" };

        SetupResolveBundlesResponse(mockClient, new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>(),
            StandaloneAssets = new List<ResolvedAsset>(),
            Unresolved = new List<string>()
        });

        // Act
        await source.ResolveBundlesAsync(assetIds);

        // Assert
        mockClient.Verify(c => c.InvokeAsync<ResolveBundlesRequest, ResolveBundlesResponse>(
            "/bundles/resolve",
            It.Is<ResolveBundlesRequest>(r =>
                r.AssetIds.Count == 2 &&
                r.AssetIds.Contains("asset-1") &&
                r.AssetIds.Contains("asset-2") &&
                r.PreferMetabundles == true &&
                r.IncludeStandalone == true),
            It.IsAny<ushort>(),
            It.IsAny<TimeSpan?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync returns resolved bundles.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WithBundles_ReturnsBundles()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        SetupResolveBundlesResponse(mockClient, new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>
            {
                new ResolvedBundle
                {
                    BundleId = "bundle-1",
                    DownloadUrl = new Uri("https://cdn.example.com/bundles/bundle-1.zip"),
                    Size = 1024 * 1024,
                    ExpiresAt = expiresAt,
                    AssetsProvided = new List<string> { "asset-1", "asset-2" },
                    BundleType = BundleType.Source
                }
            },
            StandaloneAssets = new List<ResolvedAsset>(),
            Unresolved = new List<string>()
        });

        // Act
        var result = await source.ResolveBundlesAsync(new[] { "asset-1", "asset-2" });

        // Assert
        Assert.Single(result.Bundles);
        var bundle = result.Bundles[0];
        Assert.Equal("bundle-1", bundle.BundleId);
        Assert.Equal(new Uri("https://cdn.example.com/bundles/bundle-1.zip"), bundle.DownloadUrl);
        Assert.Equal(1024 * 1024, bundle.SizeBytes);
        Assert.Equal(expiresAt, bundle.ExpiresAt);
        Assert.Contains("asset-1", bundle.IncludedAssetIds);
        Assert.Contains("asset-2", bundle.IncludedAssetIds);
        Assert.False(bundle.IsMetabundle);
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync returns metabundles with IsMetabundle flag.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WithMetabundle_SetsIsMetabundleFlag()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        SetupResolveBundlesResponse(mockClient, new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>
            {
                new ResolvedBundle
                {
                    BundleId = "metabundle-1",
                    DownloadUrl = new Uri("https://cdn.example.com/bundles/metabundle-1.zip"),
                    Size = 5 * 1024 * 1024,
                    ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    AssetsProvided = new List<string> { "asset-1", "asset-2", "asset-3" },
                    BundleType = BundleType.Metabundle
                }
            },
            StandaloneAssets = new List<ResolvedAsset>(),
            Unresolved = new List<string>()
        });

        // Act
        var result = await source.ResolveBundlesAsync(new[] { "asset-1" });

        // Assert
        Assert.True(result.Bundles[0].IsMetabundle);
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync returns standalone assets.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WithStandaloneAssets_ReturnsStandaloneAssets()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        SetupResolveBundlesResponse(mockClient, new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>(),
            StandaloneAssets = new List<ResolvedAsset>
            {
                new ResolvedAsset
                {
                    AssetId = "standalone-1",
                    DownloadUrl = new Uri("https://cdn.example.com/assets/standalone-1.bin"),
                    Size = 512 * 1024,
                    ExpiresAt = expiresAt
                }
            },
            Unresolved = new List<string>()
        });

        // Act
        var result = await source.ResolveBundlesAsync(new[] { "standalone-1" });

        // Assert
        Assert.Empty(result.Bundles);
        Assert.Single(result.StandaloneAssets);
        var asset = result.StandaloneAssets[0];
        Assert.Equal("standalone-1", asset.AssetId);
        Assert.Equal(new Uri("https://cdn.example.com/assets/standalone-1.bin"), asset.DownloadUrl);
        Assert.Equal(512 * 1024, asset.SizeBytes);
        Assert.Equal(expiresAt, asset.ExpiresAt);
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync returns unresolved asset IDs.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_WithUnresolvedAssets_ReturnsUnresolved()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        SetupResolveBundlesResponse(mockClient, new ResolveBundlesResponse
        {
            Bundles = new List<ResolvedBundle>(),
            StandaloneAssets = new List<ResolvedAsset>(),
            Unresolved = new List<string> { "missing-asset-1", "missing-asset-2" }
        });

        // Act
        var result = await source.ResolveBundlesAsync(new[] { "missing-asset-1", "missing-asset-2" });

        // Assert
        Assert.Empty(result.Bundles);
        Assert.Empty(result.StandaloneAssets);
        Assert.NotNull(result.UnresolvedAssetIds);
        Assert.Equal(2, result.UnresolvedAssetIds.Count);
        Assert.Contains("missing-asset-1", result.UnresolvedAssetIds);
        Assert.Contains("missing-asset-2", result.UnresolvedAssetIds);
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync throws AssetSourceException on API failure.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_ApiFailure_ThrowsAssetSourceException()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        mockClient
            .Setup(c => c.InvokeAsync<ResolveBundlesRequest, ResolveBundlesResponse>(
                It.IsAny<string>(),
                It.IsAny<ResolveBundlesRequest>(),
                It.IsAny<ushort>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ResolveBundlesResponse>.Failure(new ErrorResponse
            {
                ResponseCode = 500,
                ErrorName = "InternalServerError",
                Message = "Database connection failed"
            }));

        // Act & Assert
        var ex = await Assert.ThrowsAsync<AssetSourceException>(() =>
            source.ResolveBundlesAsync(new[] { "asset-1" }));

        Assert.Contains("Database connection failed", ex.Message);
    }

    #endregion

    #region Connected State API Tests - GetBundleDownloadInfoAsync

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync returns bundle info when found.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_BundleExists_ReturnsBundleInfo()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        mockClient
            .Setup(c => c.InvokeAsync<GetBundleRequest, BundleWithDownloadUrl>(
                "/bundles/get",
                It.Is<GetBundleRequest>(r => r.BundleId == "bundle-123"),
                It.IsAny<ushort>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BundleWithDownloadUrl>.Success(new BundleWithDownloadUrl
            {
                BundleId = "bundle-123",
                DownloadUrl = new Uri("https://cdn.example.com/bundles/bundle-123.zip"),
                Size = 2 * 1024 * 1024,
                ExpiresAt = expiresAt,
                Version = "1.0.0",
                Format = BundleFormat.Zip
            }));

        // Act
        var result = await source.GetBundleDownloadInfoAsync("bundle-123");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("bundle-123", result.BundleId);
        Assert.Equal(new Uri("https://cdn.example.com/bundles/bundle-123.zip"), result.DownloadUrl);
        Assert.Equal(2 * 1024 * 1024, result.SizeBytes);
        Assert.Equal(expiresAt, result.ExpiresAt);
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync returns null when bundle not found.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_BundleNotFound_ReturnsNull()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        mockClient
            .Setup(c => c.InvokeAsync<GetBundleRequest, BundleWithDownloadUrl>(
                "/bundles/get",
                It.IsAny<GetBundleRequest>(),
                It.IsAny<ushort>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<BundleWithDownloadUrl>.Failure(new ErrorResponse
            {
                ResponseCode = 404,
                ErrorName = "NotFound",
                Message = "Bundle not found"
            }));

        // Act
        var result = await source.GetBundleDownloadInfoAsync("nonexistent-bundle");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Connected State API Tests - GetAssetDownloadInfoAsync

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync returns asset info when found.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_AssetExists_ReturnsAssetInfo()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        mockClient
            .Setup(c => c.InvokeAsync<GetAssetRequest, AssetWithDownloadUrl>(
                "/assets/get",
                It.Is<GetAssetRequest>(r => r.AssetId == "asset-456" && r.Version == "latest"),
                It.IsAny<ushort>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<AssetWithDownloadUrl>.Success(new AssetWithDownloadUrl
            {
                AssetId = "asset-456",
                DownloadUrl = new Uri("https://cdn.example.com/assets/asset-456.bin"),
                Size = 512 * 1024,
                ExpiresAt = expiresAt,
                ContentType = "model/gltf-binary",
                ContentHash = "sha256:abc123def456",
                VersionId = "v1",
                Metadata = new AssetMetadata
                {
                    AssetId = "asset-456",
                    ContentHash = "sha256:abc123def456",
                    Filename = "asset-456.bin",
                    ContentType = "model/gltf-binary",
                    AssetType = AssetType.Model,
                    Realm = "shared",
                    ProcessingStatus = ProcessingStatus.Complete,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            }));

        // Act
        var result = await source.GetAssetDownloadInfoAsync("asset-456");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("asset-456", result.AssetId);
        Assert.Equal(new Uri("https://cdn.example.com/assets/asset-456.bin"), result.DownloadUrl);
        Assert.Equal(512 * 1024, result.SizeBytes);
        Assert.Equal(expiresAt, result.ExpiresAt);
        Assert.Equal("model/gltf-binary", result.ContentType);
        Assert.Equal("sha256:abc123def456", result.ContentHash);
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync returns null when asset not found.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_AssetNotFound_ReturnsNull()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        mockClient
            .Setup(c => c.InvokeAsync<GetAssetRequest, AssetWithDownloadUrl>(
                "/assets/get",
                It.IsAny<GetAssetRequest>(),
                It.IsAny<ushort>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<AssetWithDownloadUrl>.Failure(new ErrorResponse
            {
                ResponseCode = 404,
                ErrorName = "NotFound",
                Message = "Asset not found"
            }));

        // Act
        var result = await source.GetAssetDownloadInfoAsync("nonexistent-asset");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync returns null when download URL is missing.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_NoDownloadUrl_ReturnsNull()
    {
        // Arrange
        var mockClient = CreateConnectedMockClient();
        await using var source = new BannouWebSocketAssetSource(mockClient.Object);

        mockClient
            .Setup(c => c.InvokeAsync<GetAssetRequest, AssetWithDownloadUrl>(
                "/assets/get",
                It.IsAny<GetAssetRequest>(),
                It.IsAny<ushort>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<AssetWithDownloadUrl>.Success(new AssetWithDownloadUrl
            {
                AssetId = "asset-789",
                DownloadUrl = null, // No download URL
                Size = 100,
                ContentType = "application/octet-stream"
            }));

        // Act
        var result = await source.GetAssetDownloadInfoAsync("asset-789");

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Dispose Tests

    /// <summary>
    /// Verifies that DisposeAsync does not throw when source doesn't own client.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WhenNotOwningClient_DoesNotDisposeClient()
    {
        // Arrange
        await using var source = new BannouWebSocketAssetSource(_realClient!);

        // Act - dispose happens automatically via await using

        // Assert - client should still be usable (not disposed)
        // This is a sanity check - the real verification is that no exception is thrown
        Assert.NotNull(_realClient);
    }

    /// <summary>
    /// Verifies that DisposeAsync with mocked client does not call DisconnectAsync.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_WithMockedClient_DoesNotCallDisconnect()
    {
        // Arrange
        var mockClient = new Mock<IBannouClient>();
        var source = new BannouWebSocketAssetSource(mockClient.Object);

        // Act
        await source.DisposeAsync();

        // Assert - Disconnect should not be called when not owning client
        mockClient.Verify(c => c.DisconnectAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region Helper Methods

    private static Mock<IBannouClient> CreateConnectedMockClient()
    {
        var mock = new Mock<IBannouClient>();
        mock.Setup(c => c.IsConnected).Returns(true);
        return mock;
    }

    private static void SetupResolveBundlesResponse(
        Mock<IBannouClient> mockClient,
        ResolveBundlesResponse response)
    {
        mockClient
            .Setup(c => c.InvokeAsync<ResolveBundlesRequest, ResolveBundlesResponse>(
                "/bundles/resolve",
                It.IsAny<ResolveBundlesRequest>(),
                It.IsAny<ushort>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ResolveBundlesResponse>.Success(response));
    }

    #endregion
}
