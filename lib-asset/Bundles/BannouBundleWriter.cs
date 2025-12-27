using K4os.Compression.LZ4;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Asset.Bundles;

/// <summary>
/// Writes assets into the .bannou bundle format.
/// Bundle structure:
/// 1. manifest.json (uncompressed, JSON)
/// 2. index.bin (binary offset index)
/// 3. assets/*.chunk (LZ4-compressed asset data)
/// </summary>
public sealed class BannouBundleWriter : IDisposable
{
    private readonly Stream _outputStream;
    private readonly ILogger<BannouBundleWriter>? _logger;
    private readonly List<BundleAssetEntry> _assetEntries = new();
    private readonly List<BundleIndexEntry> _indexEntries = new();
    private readonly MemoryStream _dataStream = new();
    private bool _finalized;
    private bool _disposed;

    /// <summary>
    /// Creates a new bundle writer.
    /// </summary>
    /// <param name="outputStream">The stream to write the bundle to.</param>
    /// <param name="logger">Optional logger.</param>
    public BannouBundleWriter(Stream outputStream, ILogger<BannouBundleWriter>? logger = null)
    {
        _outputStream = outputStream ?? throw new ArgumentNullException(nameof(outputStream));
        _logger = logger;
    }

    /// <summary>
    /// Adds an asset to the bundle.
    /// </summary>
    /// <param name="assetId">Unique asset identifier.</param>
    /// <param name="filename">Original filename.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="data">Asset data to compress and add.</param>
    /// <param name="metadata">Optional asset metadata.</param>
    public void AddAsset(
        string assetId,
        string filename,
        string contentType,
        ReadOnlySpan<byte> data,
        IReadOnlyDictionary<string, object>? metadata = null)
    {
        ThrowIfDisposedOrFinalized();

        var index = _assetEntries.Count;
        var offset = _dataStream.Position;

        // Calculate content hash
        var hashBytes = SHA256.HashData(data);
        var contentHash = Convert.ToHexStringLower(hashBytes);

        // Compress the data
        var maxCompressedSize = LZ4Codec.MaximumOutputSize(data.Length);
        var compressedBuffer = new byte[maxCompressedSize];
        var compressedSize = LZ4Codec.Encode(
            data,
            compressedBuffer,
            LZ4Level.L00_FAST);

        // Write compressed data to data stream
        _dataStream.Write(compressedBuffer, 0, compressedSize);

        // Create asset entry
        var assetEntry = new BundleAssetEntry
        {
            AssetId = assetId,
            Filename = filename,
            ContentType = contentType,
            UncompressedSize = data.Length,
            CompressedSize = compressedSize,
            ContentHash = contentHash,
            Index = index,
            Metadata = metadata
        };
        _assetEntries.Add(assetEntry);

        // Create index entry
        var indexEntry = new BundleIndexEntry
        {
            Offset = offset,
            CompressedSize = compressedSize,
            UncompressedSize = data.Length,
            ContentHashPrefix = hashBytes[..24]
        };
        _indexEntries.Add(indexEntry);

        _logger?.LogDebug(
            "Added asset {AssetId} ({ContentType}, {Size} bytes, compressed to {CompressedSize} bytes)",
            assetId,
            contentType,
            data.Length,
            compressedSize);
    }

    /// <summary>
    /// Adds an asset from a stream.
    /// </summary>
    public async Task AddAssetAsync(
        string assetId,
        string filename,
        string contentType,
        Stream dataStream,
        IReadOnlyDictionary<string, object>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedOrFinalized();

        // Read all data into memory for hashing and compression
        using var memoryStream = new MemoryStream();
        await dataStream.CopyToAsync(memoryStream, cancellationToken);
        var data = memoryStream.ToArray();

        AddAsset(assetId, filename, contentType, data, metadata);
    }

    /// <summary>
    /// Finalizes the bundle and writes it to the output stream.
    /// </summary>
    /// <param name="bundleId">Unique bundle identifier.</param>
    /// <param name="name">Bundle name.</param>
    /// <param name="version">Bundle version.</param>
    /// <param name="createdBy">Account ID of creator.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="tags">Optional metadata tags.</param>
    public void Finalize(
        string bundleId,
        string name,
        string version,
        string createdBy,
        string? description = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        ThrowIfDisposedOrFinalized();
        _finalized = true;

        // Calculate totals
        var totalUncompressed = _assetEntries.Sum(a => a.UncompressedSize);
        var totalCompressed = _assetEntries.Sum(a => a.CompressedSize);

        // Create manifest
        var manifest = new BundleManifest
        {
            FormatVersion = BundleManifest.CurrentFormatVersion,
            BundleId = bundleId,
            Name = name,
            Description = description,
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
            TotalUncompressedSize = totalUncompressed,
            TotalCompressedSize = totalCompressed,
            AssetCount = _assetEntries.Count,
            CompressionAlgorithm = "lz4",
            Assets = _assetEntries,
            Tags = tags
        };

        // Write manifest
        var manifestJson = JsonSerializer.Serialize(manifest, BannouBundleJsonContext.Default.BundleManifest);
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        // Write manifest length (4 bytes, big-endian)
        Span<byte> lengthBuffer = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, manifestBytes.Length);
        _outputStream.Write(lengthBuffer);

        // Write manifest
        _outputStream.Write(manifestBytes);

        // Create and write index
        var index = new BundleIndex { Entries = _indexEntries };
        index.WriteTo(_outputStream);

        // Write asset data
        _dataStream.Position = 0;
        _dataStream.CopyTo(_outputStream);

        _logger?.LogInformation(
            "Finalized bundle {BundleId} with {AssetCount} assets ({TotalSize} bytes compressed)",
            bundleId,
            _assetEntries.Count,
            totalCompressed);
    }

    /// <summary>
    /// Finalizes the bundle asynchronously.
    /// </summary>
    public async Task FinalizeAsync(
        string bundleId,
        string name,
        string version,
        string createdBy,
        string? description = null,
        IReadOnlyDictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedOrFinalized();
        _finalized = true;

        var totalUncompressed = _assetEntries.Sum(a => a.UncompressedSize);
        var totalCompressed = _assetEntries.Sum(a => a.CompressedSize);

        var manifest = new BundleManifest
        {
            FormatVersion = BundleManifest.CurrentFormatVersion,
            BundleId = bundleId,
            Name = name,
            Description = description,
            Version = version,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = createdBy,
            TotalUncompressedSize = totalUncompressed,
            TotalCompressedSize = totalCompressed,
            AssetCount = _assetEntries.Count,
            CompressionAlgorithm = "lz4",
            Assets = _assetEntries,
            Tags = tags
        };

        var manifestJson = JsonSerializer.Serialize(manifest, BannouBundleJsonContext.Default.BundleManifest);
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        var lengthBuffer = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, manifestBytes.Length);
        await _outputStream.WriteAsync(lengthBuffer, cancellationToken);
        await _outputStream.WriteAsync(manifestBytes, cancellationToken);

        using var indexStream = new MemoryStream();
        var index = new BundleIndex { Entries = _indexEntries };
        index.WriteTo(indexStream);
        indexStream.Position = 0;
        await indexStream.CopyToAsync(_outputStream, cancellationToken);

        _dataStream.Position = 0;
        await _dataStream.CopyToAsync(_outputStream, cancellationToken);

        _logger?.LogInformation(
            "Finalized bundle {BundleId} with {AssetCount} assets ({TotalSize} bytes compressed)",
            bundleId,
            _assetEntries.Count,
            totalCompressed);
    }

    private void ThrowIfDisposedOrFinalized()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_finalized)
        {
            throw new InvalidOperationException("Bundle has already been finalized");
        }
    }

    /// <summary>
    /// Disposes the writer and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dataStream.Dispose();
    }
}

/// <summary>
/// JSON serialization context for bundle types.
/// </summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(BundleManifest))]
[System.Text.Json.Serialization.JsonSerializable(typeof(BundleAssetEntry))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = false)]
public partial class BannouBundleJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
