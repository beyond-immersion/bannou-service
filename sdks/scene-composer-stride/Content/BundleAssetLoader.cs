using BeyondImmersion.Bannou.Bundle.Format;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.Bannou.SceneComposer.Stride.Content;

/// <summary>
/// Wraps a BannouBundleReader with fast asset ID lookup and manifest access.
/// </summary>
/// <remarks>
/// This class manages the lifecycle of a loaded bundle and provides
/// efficient O(1) asset existence checks via a HashSet.
/// </remarks>
public sealed class BundleAssetLoader : IDisposable
{
    private readonly Stream _stream;
    private readonly BannouBundleReader _reader;
    private readonly HashSet<string> _assetIds;
    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Creates a new bundle loader from a stream.
    /// </summary>
    /// <param name="stream">Stream containing the .bannou bundle data.</param>
    public BundleAssetLoader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _reader = new BannouBundleReader(stream, leaveOpen: false);
        _assetIds = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the bundle manifest.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if bundle not initialized.</exception>
    public BundleManifest Manifest
    {
        get
        {
            EnsureInitialized();
            return _reader.Manifest;
        }
    }

    /// <summary>
    /// Gets the bundle ID.
    /// </summary>
    public string BundleId => Manifest.BundleId;

    /// <summary>
    /// Gets the bundle name.
    /// </summary>
    public string Name => Manifest.Name;

    /// <summary>
    /// Gets the bundle version.
    /// </summary>
    public string Version => Manifest.Version;

    /// <summary>
    /// Gets the number of assets in this bundle.
    /// </summary>
    public int AssetCount => Manifest.AssetCount;

    /// <summary>
    /// Initializes the bundle by reading the header and building the asset index.
    /// </summary>
    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        _reader.ReadHeader();
        BuildAssetIndex();
        _initialized = true;
    }

    /// <summary>
    /// Initializes the bundle asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
            return;

        await _reader.ReadHeaderAsync(cancellationToken);
        BuildAssetIndex();
        _initialized = true;
    }

    /// <summary>
    /// Checks if an asset exists in this bundle.
    /// </summary>
    /// <param name="assetId">The asset ID to check.</param>
    /// <returns>True if the asset exists in this bundle.</returns>
    public bool HasAsset(string assetId)
    {
        EnsureInitialized();
        return _assetIds.Contains(assetId);
    }

    /// <summary>
    /// Gets the asset entry for an asset ID.
    /// </summary>
    /// <param name="assetId">The asset ID.</param>
    /// <returns>The asset entry, or null if not found.</returns>
    public BundleAssetEntry? GetAssetEntry(string assetId)
    {
        EnsureInitialized();
        return _reader.GetAssetEntry(assetId);
    }

    /// <summary>
    /// Reads and decompresses an asset's data.
    /// </summary>
    /// <param name="assetId">The asset ID to read.</param>
    /// <returns>The decompressed asset data, or null if not found.</returns>
    public byte[]? ReadAsset(string assetId)
    {
        EnsureInitialized();
        return _reader.ReadAsset(assetId);
    }

    /// <summary>
    /// Reads and decompresses an asset's data asynchronously.
    /// </summary>
    /// <param name="assetId">The asset ID to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decompressed asset data, or null if not found.</returns>
    public async Task<byte[]?> ReadAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        return await _reader.ReadAssetAsync(assetId, cancellationToken);
    }

    /// <summary>
    /// Enumerates all asset IDs in this bundle.
    /// </summary>
    /// <returns>Enumerable of all asset IDs.</returns>
    public IEnumerable<string> GetAssetIds()
    {
        EnsureInitialized();
        return _assetIds;
    }

    /// <summary>
    /// Enumerates all asset entries in this bundle.
    /// </summary>
    /// <returns>Enumerable of all asset entries.</returns>
    public IEnumerable<BundleAssetEntry> GetAssetEntries()
    {
        EnsureInitialized();
        return Manifest.Assets;
    }

    private void BuildAssetIndex()
    {
        _assetIds.Clear();
        foreach (var asset in _reader.Manifest.Assets)
        {
            _assetIds.Add(asset.AssetId);
        }
    }

    private void EnsureInitialized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
        {
            throw new InvalidOperationException(
                "Bundle not initialized. Call Initialize() or InitializeAsync() first.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _reader.Dispose();
        // Stream is disposed by the reader since leaveOpen is false
    }
}
