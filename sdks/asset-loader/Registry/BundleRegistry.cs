using System.Collections.Concurrent;
using BeyondImmersion.Bannou.AssetLoader.Abstractions;

namespace BeyondImmersion.Bannou.AssetLoader.Registry;

/// <summary>
/// Thread-safe registry for tracking loaded bundles and providing O(1) asset-to-bundle lookup.
/// </summary>
public sealed class BundleRegistry : IBundleRegistry
{
    private readonly ConcurrentDictionary<string, LoadedBundle> _bundles = new();
    private readonly ConcurrentDictionary<string, string> _assetToBundleIndex = new();

    /// <inheritdoc />
    public int BundleCount => _bundles.Count;

    /// <inheritdoc />
    public int AssetCount => _assetToBundleIndex.Count;

    /// <inheritdoc />
    public void Register(LoadedBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentException.ThrowIfNullOrEmpty(bundle.BundleId);

        _bundles[bundle.BundleId] = bundle;

        foreach (var assetId in bundle.AssetIds)
        {
            // TryAdd - first bundle wins if asset exists in multiple bundles
            _assetToBundleIndex.TryAdd(assetId, bundle.BundleId);
        }
    }

    /// <inheritdoc />
    public void Unregister(string bundleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);

        if (_bundles.TryRemove(bundleId, out var bundle))
        {
            foreach (var assetId in bundle.AssetIds)
            {
                // Only remove if this bundle owns the asset
                _assetToBundleIndex.TryRemove(new KeyValuePair<string, string>(assetId, bundleId));
            }

            bundle.Dispose();
        }
    }

    /// <inheritdoc />
    public string? FindBundleForAsset(string assetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetId);
        return _assetToBundleIndex.TryGetValue(assetId, out var bundleId) ? bundleId : null;
    }

    /// <inheritdoc />
    public LoadedBundle? GetBundle(string bundleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        return _bundles.TryGetValue(bundleId, out var bundle) ? bundle : null;
    }

    /// <inheritdoc />
    public bool HasAsset(string assetId)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetId);
        return _assetToBundleIndex.ContainsKey(assetId);
    }

    /// <inheritdoc />
    public bool HasBundle(string bundleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleId);
        return _bundles.ContainsKey(bundleId);
    }

    /// <inheritdoc />
    public IEnumerable<string> GetLoadedBundleIds()
        => _bundles.Keys;

    /// <summary>
    /// Gets all loaded bundles.
    /// </summary>
    public IEnumerable<LoadedBundle> GetLoadedBundles()
        => _bundles.Values;

    /// <summary>
    /// Clears all registered bundles.
    /// </summary>
    public void Clear()
    {
        foreach (var bundleId in _bundles.Keys.ToList())
        {
            Unregister(bundleId);
        }
    }
}
