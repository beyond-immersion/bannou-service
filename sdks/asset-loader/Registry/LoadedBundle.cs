using BeyondImmersion.Bannou.Bundle.Format;

namespace BeyondImmersion.Bannou.AssetLoader.Registry;

/// <summary>
/// Represents a bundle that has been loaded into memory.
/// Contains the manifest, reader, and metadata needed for asset access.
/// </summary>
public sealed class LoadedBundle : IDisposable
{
    /// <summary>Bundle identifier.</summary>
    public required string BundleId { get; init; }

    /// <summary>Parsed bundle manifest with asset metadata.</summary>
    public required BundleManifest Manifest { get; init; }

    /// <summary>List of all asset IDs in this bundle.</summary>
    public required IReadOnlyList<string> AssetIds { get; init; }

    /// <summary>Bundle reader for extracting asset data.</summary>
    public required BannouBundleReader Reader { get; init; }

    /// <summary>When this bundle was loaded.</summary>
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last time an asset from this bundle was accessed.</summary>
    public DateTimeOffset LastAccessedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Number of times assets from this bundle have been accessed.</summary>
    public int AccessCount { get; set; }

    /// <summary>Gets asset entry by ID.</summary>
    public BundleAssetEntry? GetAssetEntry(string assetId)
        => Manifest.Assets.FirstOrDefault(a => a.AssetId == assetId);

    /// <summary>Checks if bundle contains an asset.</summary>
    public bool ContainsAsset(string assetId)
        => AssetIds.Contains(assetId);

    /// <summary>
    /// Reads asset data from the bundle.
    /// </summary>
    /// <param name="assetId">Asset ID to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Asset data, or null if not found.</returns>
    public async Task<byte[]?> ReadAssetAsync(string assetId, CancellationToken ct = default)
    {
        LastAccessedAt = DateTimeOffset.UtcNow;
        AccessCount++;
        return await Reader.ReadAssetAsync(assetId, ct).ConfigureAwait(false);
    }

    /// <summary>Disposes the bundle reader.</summary>
    public void Dispose()
    {
        Reader.Dispose();
    }
}
