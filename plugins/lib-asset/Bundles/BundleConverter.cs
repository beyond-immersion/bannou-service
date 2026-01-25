using BeyondImmersion.Bannou.Bundle.Format;
using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace BeyondImmersion.BannouService.Asset.Bundles;

/// <summary>
/// Converts between ZIP archives and .bannou bundle format.
/// Provides caching for ZIP downloads to avoid repeated conversion.
/// </summary>
public sealed class BundleConverter : IBundleConverter
{
    private readonly ILogger<BundleConverter> _logger;
    private readonly string _cacheDirectory;
    private readonly TimeSpan _cacheTtl;

    /// <summary>
    /// Creates a new bundle converter.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="cacheDirectory">Directory for ZIP cache files.</param>
    /// <param name="cacheTtl">Time-to-live for cached ZIP files.</param>
    public BundleConverter(
        ILogger<BundleConverter> logger,
        string? cacheDirectory = null,
        TimeSpan? cacheTtl = null)
    {
        _logger = logger;
        _cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "bannou-zip-cache");
        _cacheTtl = cacheTtl ?? TimeSpan.FromHours(24);

        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
    }

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
    public async Task ConvertZipToBundleAsync(
        Stream zipStream,
        Stream outputStream,
        Guid bundleId,
        string name,
        string version,
        string createdBy,
        string? description = null,
        IReadOnlyDictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        using var writer = new BannouBundleWriter(outputStream, _logger as ILogger<BannouBundleWriter>);

        var assetIndex = 0;
        foreach (var entry in archive.Entries)
        {
            // Skip directories
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            // Skip hidden files and system files
            if (entry.Name.StartsWith('.') || entry.Name.StartsWith("__"))
            {
                _logger.LogDebug("Skipping hidden/system file: {FileName}", entry.FullName);
                continue;
            }

            var assetId = GenerateAssetId(entry.FullName, assetIndex);
            var contentType = GetContentTypeFromExtension(entry.Name);

            using var entryStream = entry.Open();
            await writer.AddAssetAsync(
                assetId,
                entry.FullName,
                contentType,
                entryStream,
                cancellationToken: cancellationToken);

            assetIndex++;
        }

        await writer.FinalizeAsync(
            bundleId,
            name,
            version,
            createdBy,
            description,
            tags,
            cancellationToken);

        _logger.LogInformation(
            "Converted ZIP to bundle {BundleId} with {AssetCount} assets",
            bundleId,
            assetIndex);
    }

    /// <summary>
    /// Converts a .bannou bundle to ZIP format.
    /// Uses caching to avoid repeated conversions.
    /// </summary>
    /// <param name="bundleStream">The bundle stream.</param>
    /// <param name="outputStream">The output stream for the ZIP.</param>
    /// <param name="bundleId">Bundle ID for cache lookup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the result came from cache.</returns>
    public async Task<bool> ConvertBundleToZipAsync(
        Stream bundleStream,
        Stream outputStream,
        Guid bundleId,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cacheFile = GetCacheFilePath(bundleId);
        if (File.Exists(cacheFile))
        {
            var cacheInfo = new FileInfo(cacheFile);
            if (DateTime.UtcNow - cacheInfo.LastWriteTimeUtc < _cacheTtl)
            {
                _logger.LogDebug("Using cached ZIP for bundle {BundleId}", bundleId);
                await using var cacheStream = File.OpenRead(cacheFile);
                await cacheStream.CopyToAsync(outputStream, cancellationToken);
                return true;
            }

            // Cache expired, delete it
            try
            {
                File.Delete(cacheFile);
            }
            catch (IOException)
            {
                // Ignore deletion failures
            }
        }

        // Convert bundle to ZIP
        using var reader = new BannouBundleReader(bundleStream, _logger as ILogger<BannouBundleReader>, leaveOpen: true);
        await reader.ReadHeaderAsync(cancellationToken);

        // Write to temp file first, then copy to cache and output
        var tempFile = Path.GetTempFileName();
        try
        {
            await using (var tempStream = File.Create(tempFile))
            {
                using var archive = new ZipArchive(tempStream, ZipArchiveMode.Create, leaveOpen: true);

                await foreach (var (entry, data) in reader.ReadAllAssetsAsync(cancellationToken))
                {
                    var zipEntry = archive.CreateEntry(entry.Filename, CompressionLevel.Optimal);
                    await using var entryStream = zipEntry.Open();
                    await entryStream.WriteAsync(data, cancellationToken);
                }
            }

            // Copy to cache
            try
            {
                File.Copy(tempFile, cacheFile, overwrite: true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to cache ZIP for bundle {BundleId}", bundleId);
            }

            // Copy to output
            await using var resultStream = File.OpenRead(tempFile);
            await resultStream.CopyToAsync(outputStream, cancellationToken);
        }
        finally
        {
            try
            {
                File.Delete(tempFile);
            }
            catch (IOException)
            {
                // Ignore cleanup failures
            }
        }

        _logger.LogInformation(
            "Converted bundle {BundleId} to ZIP with {AssetCount} assets",
            bundleId,
            reader.Manifest.AssetCount);

        return false;
    }

    /// <summary>
    /// Checks if a cached ZIP exists for the given bundle.
    /// </summary>
    public bool HasCachedZip(Guid bundleId)
    {
        var cacheFile = GetCacheFilePath(bundleId);
        if (!File.Exists(cacheFile))
        {
            return false;
        }

        var cacheInfo = new FileInfo(cacheFile);
        return DateTime.UtcNow - cacheInfo.LastWriteTimeUtc < _cacheTtl;
    }

    /// <summary>
    /// Gets a cached ZIP stream if available.
    /// </summary>
    public Stream? GetCachedZipStream(Guid bundleId)
    {
        var cacheFile = GetCacheFilePath(bundleId);
        if (!File.Exists(cacheFile))
        {
            return null;
        }

        var cacheInfo = new FileInfo(cacheFile);
        if (DateTime.UtcNow - cacheInfo.LastWriteTimeUtc >= _cacheTtl)
        {
            return null;
        }

        return File.OpenRead(cacheFile);
    }

    /// <summary>
    /// Clears expired entries from the ZIP cache.
    /// </summary>
    public int CleanupCache()
    {
        var cleaned = 0;
        var cutoff = DateTime.UtcNow - _cacheTtl;

        try
        {
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.zip"))
            {
                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTimeUtc < cutoff)
                {
                    try
                    {
                        File.Delete(file);
                        cleaned++;
                    }
                    catch (IOException)
                    {
                        // Ignore individual file deletion failures
                    }
                }
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Cache directory doesn't exist, nothing to clean
        }

        if (cleaned > 0)
        {
            _logger.LogInformation("Cleaned {Count} expired ZIP cache entries", cleaned);
        }

        return cleaned;
    }

    /// <summary>
    /// Invalidates a specific bundle's cached ZIP.
    /// </summary>
    public void InvalidateCache(Guid bundleId)
    {
        var cacheFile = GetCacheFilePath(bundleId);
        try
        {
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
                _logger.LogDebug("Invalidated ZIP cache for bundle {BundleId}", bundleId);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate ZIP cache for bundle {BundleId}", bundleId);
        }
    }

    private string GetCacheFilePath(Guid bundleId)
    {
        // File system boundary: GUID to string for filename
        // Standard Guid.ToString() format is safe for all file systems
        return Path.Combine(_cacheDirectory, $"{bundleId}.zip");
    }

    private static string GenerateAssetId(string fullPath, int index)
    {
        // Create a deterministic asset ID from the path
        var normalizedPath = fullPath.Replace('\\', '/').TrimStart('/');
        return $"asset_{index:D4}_{Path.GetFileNameWithoutExtension(normalizedPath)}";
    }

    private static string GetContentTypeFromExtension(string filename)
    {
        var extension = Path.GetExtension(filename).ToLowerInvariant();
        return extension switch
        {
            // Images
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tiff" or ".tif" => "image/tiff",

            // 3D Models
            ".gltf" => "model/gltf+json",
            ".glb" => "model/gltf-binary",
            ".obj" => "model/obj",
            ".fbx" => "model/fbx",

            // Audio
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".opus" => "audio/opus",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            ".webm" => "audio/webm",

            // Video
            ".mp4" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",

            // Text/Data
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            ".yaml" or ".yml" => "application/x-yaml",

            // Default
            _ => "application/octet-stream"
        };
    }
}
