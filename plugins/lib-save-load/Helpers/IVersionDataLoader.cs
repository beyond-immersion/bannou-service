using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Handles all version data loading operations including hot cache access,
/// asset service retrieval, and delta chain reconstruction.
/// </summary>
/// <remarks>
/// This helper centralizes version data loading logic that was previously
/// scattered across multiple private methods in SaveLoadService.
/// Extraction improves testability and separates concerns.
/// </remarks>
public interface IVersionDataLoader
{
    /// <summary>
    /// Loads version data from hot cache or asset service.
    /// </summary>
    /// <param name="slotId">The slot ID.</param>
    /// <param name="version">The version manifest to load data for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Decompressed version data, or null if not found.</returns>
    Task<byte[]?> LoadVersionDataAsync(
        string slotId,
        SaveVersionManifest version,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reconstructs full data from a delta chain by walking back to base snapshot
    /// and applying deltas in order.
    /// </summary>
    /// <param name="slotId">The slot ID.</param>
    /// <param name="targetVersion">The target delta version to reconstruct.</param>
    /// <param name="versionStore">The version manifest store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reconstructed full data, or null if reconstruction fails.</returns>
    Task<byte[]?> ReconstructFromDeltaChainAsync(
        string slotId,
        SaveVersionManifest targetVersion,
        IStateStore<SaveVersionManifest> versionStore,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads raw data directly from the asset service.
    /// </summary>
    /// <param name="assetId">The asset ID to load.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw asset data, or null if not found.</returns>
    Task<byte[]?> LoadFromAssetServiceAsync(
        string assetId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Caches loaded data in hot store for future fast access.
    /// </summary>
    /// <param name="slotId">The slot ID.</param>
    /// <param name="versionNumber">The version number.</param>
    /// <param name="decompressedData">The decompressed data to cache.</param>
    /// <param name="contentHash">The content hash for verification.</param>
    /// <param name="manifest">The version manifest with compression info.</param>
    /// <param name="hotCacheStore">The hot cache store to write to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CacheInHotStoreAsync(
        string slotId,
        int versionNumber,
        byte[] decompressedData,
        string contentHash,
        SaveVersionManifest manifest,
        IStateStore<HotSaveEntry> hotCacheStore,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds a version number by checkpoint name.
    /// </summary>
    /// <param name="slot">The slot metadata.</param>
    /// <param name="checkpointName">The checkpoint name to search for.</param>
    /// <param name="versionStore">The version manifest store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version number, or 0 if not found.</returns>
    Task<int> FindVersionByCheckpointAsync(
        SaveSlotMetadata slot,
        string checkpointName,
        IStateStore<SaveVersionManifest> versionStore,
        CancellationToken cancellationToken);
}
