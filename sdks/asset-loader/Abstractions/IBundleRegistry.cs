using BeyondImmersion.Bannou.AssetLoader.Registry;

namespace BeyondImmersion.Bannou.AssetLoader.Abstractions;

/// <summary>
/// Registry for tracking loaded bundles and providing asset-to-bundle lookup.
/// Thread-safe for concurrent game loading scenarios.
/// </summary>
public interface IBundleRegistry
{
    /// <summary>
    /// Registers a loaded bundle in the registry.
    /// </summary>
    /// <param name="bundle">Loaded bundle to register.</param>
    void Register(LoadedBundle bundle);

    /// <summary>
    /// Unregisters a bundle from the registry.
    /// </summary>
    /// <param name="bundleId">Bundle ID to unregister.</param>
    void Unregister(string bundleId);

    /// <summary>
    /// Finds which bundle contains a specific asset.
    /// O(1) lookup via internal index.
    /// </summary>
    /// <param name="assetId">Asset ID to find.</param>
    /// <returns>Bundle ID containing the asset, or null if not found.</returns>
    string? FindBundleForAsset(string assetId);

    /// <summary>
    /// Gets a loaded bundle by ID.
    /// </summary>
    /// <param name="bundleId">Bundle ID to retrieve.</param>
    /// <returns>Loaded bundle, or null if not registered.</returns>
    LoadedBundle? GetBundle(string bundleId);

    /// <summary>
    /// Checks if an asset is available in any loaded bundle.
    /// </summary>
    /// <param name="assetId">Asset ID to check.</param>
    /// <returns>True if asset is available.</returns>
    bool HasAsset(string assetId);

    /// <summary>
    /// Checks if a bundle is loaded.
    /// </summary>
    /// <param name="bundleId">Bundle ID to check.</param>
    /// <returns>True if bundle is loaded.</returns>
    bool HasBundle(string bundleId);

    /// <summary>
    /// Gets all loaded bundle IDs.
    /// </summary>
    IEnumerable<string> GetLoadedBundleIds();

    /// <summary>
    /// Gets count of loaded bundles.
    /// </summary>
    int BundleCount { get; }

    /// <summary>
    /// Gets count of available assets across all loaded bundles.
    /// </summary>
    int AssetCount { get; }
}
