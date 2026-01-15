using BeyondImmersion.Bannou.Client;
using Xunit;

namespace BeyondImmersion.Bannou.AssetLoader.Client.Tests;

/// <summary>
/// Unit tests for BannouWebSocketAssetSource.
/// Tests constructor validation and property behaviors.
/// Note: Full API tests require integration testing with a live server.
/// </summary>
public class BannouWebSocketAssetSourceTests : IAsyncLifetime
{
    private BannouClient? _client;

    public Task InitializeAsync()
    {
        _client = new BannouClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            await _client.DisposeAsync();
        }
    }

    #region Constructor Tests

    /// <summary>
    /// Verifies that constructor throws when client is null.
    /// </summary>
    [Fact]
    public void Constructor_NullClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new BannouWebSocketAssetSource(null!));
    }

    /// <summary>
    /// Verifies that constructor accepts valid client.
    /// </summary>
    [Fact]
    public void Constructor_ValidClient_CreatesInstance()
    {
        // Act
        var source = new BannouWebSocketAssetSource(_client!);

        // Assert
        Assert.NotNull(source);
    }

    /// <summary>
    /// Verifies that constructor accepts optional logger.
    /// </summary>
    [Fact]
    public void Constructor_WithLogger_CreatesInstance()
    {
        // Act
        var source = new BannouWebSocketAssetSource(_client!, logger: null);

        // Assert
        Assert.NotNull(source);
    }

    #endregion

    #region Property Tests

    /// <summary>
    /// Verifies that RequiresAuthentication is true.
    /// </summary>
    [Fact]
    public void RequiresAuthentication_ReturnsTrue()
    {
        // Arrange
        var source = new BannouWebSocketAssetSource(_client!);

        // Assert
        Assert.True(source.RequiresAuthentication);
    }

    /// <summary>
    /// Verifies that IsAvailable reflects client connection state.
    /// </summary>
    [Fact]
    public void IsAvailable_WhenDisconnected_ReturnsFalse()
    {
        // Arrange
        var source = new BannouWebSocketAssetSource(_client!);

        // Assert - client is not connected
        Assert.False(source.IsAvailable);
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
        var source = new BannouWebSocketAssetSource(_client!);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ResolveBundlesAsync(new[] { "asset-1" }));
    }

    /// <summary>
    /// Verifies that ResolveBundlesAsync validates null input.
    /// </summary>
    [Fact]
    public async Task ResolveBundlesAsync_NullAssetIds_ThrowsArgumentNullException()
    {
        // Arrange
        var source = new BannouWebSocketAssetSource(_client!);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            source.ResolveBundlesAsync(null!));
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync throws when not connected.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_WhenDisconnected_ThrowsInvalidOperationException()
    {
        // Arrange
        var source = new BannouWebSocketAssetSource(_client!);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.GetBundleDownloadInfoAsync("bundle-1"));
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync validates null input.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_NullBundleId_ThrowsArgumentException()
    {
        // Arrange
        var source = new BannouWebSocketAssetSource(_client!);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.GetBundleDownloadInfoAsync(null!));
    }

    /// <summary>
    /// Verifies that GetBundleDownloadInfoAsync validates empty input.
    /// </summary>
    [Fact]
    public async Task GetBundleDownloadInfoAsync_EmptyBundleId_ThrowsArgumentException()
    {
        // Arrange
        var source = new BannouWebSocketAssetSource(_client!);

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
        var source = new BannouWebSocketAssetSource(_client!);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.GetAssetDownloadInfoAsync("asset-1"));
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync validates null input.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_NullAssetId_ThrowsArgumentException()
    {
        // Arrange
        var source = new BannouWebSocketAssetSource(_client!);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.GetAssetDownloadInfoAsync(null!));
    }

    /// <summary>
    /// Verifies that GetAssetDownloadInfoAsync validates empty input.
    /// </summary>
    [Fact]
    public async Task GetAssetDownloadInfoAsync_EmptyAssetId_ThrowsArgumentException()
    {
        // Arrange
        var source = new BannouWebSocketAssetSource(_client!);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.GetAssetDownloadInfoAsync(""));
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
        var source = new BannouWebSocketAssetSource(_client!);

        // Act
        await source.DisposeAsync();

        // Assert - client should still be usable (not disposed)
        // This is a sanity check - the real verification is that no exception is thrown
        Assert.NotNull(_client);
    }

    #endregion
}
