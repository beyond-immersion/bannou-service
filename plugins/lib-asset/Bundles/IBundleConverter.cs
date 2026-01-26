namespace BeyondImmersion.BannouService.Asset.Bundles;

/// <summary>
/// Interface for converting between ZIP archives and .bannou bundle format.
/// Provides caching for ZIP downloads to avoid repeated conversion.
/// </summary>
public interface IBundleConverter
{
    /// <summary>
    /// Converts a ZIP archive to .bannou bundle format.
    /// </summary>
    /// <param name="zipStream">The ZIP archive stream.</param>
    /// <param name="outputStream">The output stream for the bundle.</param>
    /// <param name="bundleId">Unique bundle identifier.</param>
    /// <param name="name">Bundle name.</param>
    /// <param name="version">Bundle version.</param>
    /// <param name="createdBy">Account ID of creator.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="tags">Optional metadata tags.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConvertZipToBundleAsync(
        Stream zipStream,
        Stream outputStream,
        string bundleId,
        string name,
        string version,
        string createdBy,
        string? description = null,
        IReadOnlyDictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Converts a .bannou bundle to ZIP format.
    /// Uses caching to avoid repeated conversions.
    /// </summary>
    /// <param name="bundleStream">The bundle stream.</param>
    /// <param name="outputStream">The output stream for the ZIP.</param>
    /// <param name="bundleId">Bundle ID for cache lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the result came from cache.</returns>
    Task<bool> ConvertBundleToZipAsync(
        Stream bundleStream,
        Stream outputStream,
        string bundleId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a cached ZIP exists for the given bundle.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <returns>True if a valid cached ZIP exists.</returns>
    bool HasCachedZip(string bundleId);

    /// <summary>
    /// Gets a cached ZIP stream if available.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    /// <returns>A stream to the cached ZIP, or null if not cached.</returns>
    Stream? GetCachedZipStream(string bundleId);

    /// <summary>
    /// Clears expired entries from the ZIP cache.
    /// </summary>
    /// <returns>The number of cache entries cleaned.</returns>
    int CleanupCache();

    /// <summary>
    /// Invalidates a specific bundle's cached ZIP.
    /// </summary>
    /// <param name="bundleId">The bundle identifier.</param>
    void InvalidateCache(string bundleId);
}
