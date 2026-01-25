using BeyondImmersion.Bannou.Bundle.Format;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Storage;
using K4os.Compression.LZ4;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Security.Cryptography;

namespace BeyondImmersion.BannouService.Asset.Streaming;

/// <summary>
/// Streaming bundle writer that writes directly to multipart upload parts.
/// Designed for memory-efficient assembly of large metabundles.
/// </summary>
/// <remarks>
/// <para>
/// The bundle format requires: [manifest length][manifest JSON][index binary][asset data]
/// For streaming, we defer the header (manifest + index) until finalization since we
/// don't know the total size until all assets are processed.
/// </para>
/// <para>
/// S3/MinIO multipart upload requires each part (except last) to be at least 5MB.
/// We handle this by:
/// 1. Buffering initial data until we have enough for a complete part
/// 2. Uploading subsequent parts as they fill
/// 3. At finalization, prepending header to buffered data for part 1
/// </para>
/// </remarks>
public sealed class StreamingBundleWriter : IAsyncDisposable
{
    private readonly IAssetStorageProvider _storageProvider;
    private readonly ServerMultipartUploadSession _uploadSession;
    private readonly StreamingBundleWriterOptions _options;
    private readonly ILogger? _logger;

    // Tracking for manifest and index generation
    private readonly List<BundleAssetEntry> _assetEntries = new();
    private readonly List<BundleIndexEntry> _indexEntries = new();
    private readonly List<ServerUploadedPart> _uploadedParts = new();

    // Buffer management
    // We reserve part 1 for header + first chunk, upload subsequent parts as 2, 3, etc.
    private MemoryStream _partBuffer;
    private MemoryStream _headerReserveBuffer; // First chunk that will be combined with header
    private int _nextPartNumber = 2; // Start at 2, part 1 reserved for header
    private long _totalDataBytesWritten;
    private bool _finalized;
    private bool _disposed;

    // S3 minimum part size (5MB) - except for last part
    private const long MinPartSize = 5 * 1024 * 1024;

    /// <summary>
    /// Creates a new streaming bundle writer.
    /// </summary>
    /// <param name="uploadSession">The multipart upload session from storage provider.</param>
    /// <param name="storageProvider">Storage provider for uploading parts.</param>
    /// <param name="options">Streaming configuration options.</param>
    /// <param name="logger">Optional logger.</param>
    public StreamingBundleWriter(
        ServerMultipartUploadSession uploadSession,
        IAssetStorageProvider storageProvider,
        StreamingBundleWriterOptions options,
        ILogger? logger = null)
    {
        _uploadSession = uploadSession ?? throw new ArgumentNullException(nameof(uploadSession));
        _storageProvider = storageProvider ?? throw new ArgumentNullException(nameof(storageProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;

        // Initialize buffers
        _partBuffer = new MemoryStream((int)_options.PartSizeBytes);
        _headerReserveBuffer = new MemoryStream((int)MinPartSize); // Reserve at least 5MB for header + first chunk
    }

    /// <summary>
    /// Gets the number of assets added so far.
    /// </summary>
    public int AssetCount => _assetEntries.Count;

    /// <summary>
    /// Gets the total compressed data bytes written so far.
    /// </summary>
    public long TotalCompressedBytes => _totalDataBytesWritten;

    /// <summary>
    /// Adds an asset from raw data.
    /// </summary>
    /// <param name="assetId">Unique asset identifier.</param>
    /// <param name="filename">Original filename.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="data">Uncompressed asset data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AddAssetAsync(
        string assetId,
        string filename,
        string contentType,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedOrFinalized();

        var index = _assetEntries.Count;

        // Calculate content hash
        var hashBytes = SHA256.HashData(data.Span);
        var contentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Compress the data using LZ4
        var maxCompressedSize = LZ4Codec.MaximumOutputSize(data.Length);
        var compressedBuffer = ArrayPool<byte>.Shared.Rent(maxCompressedSize);

        try
        {
            var compressedSize = LZ4Codec.Encode(
                data.Span,
                compressedBuffer.AsSpan(),
                LZ4Level.L00_FAST);

            // Write compressed data to buffer
            var offset = await WriteToBufferAsync(
                compressedBuffer.AsMemory(0, compressedSize),
                cancellationToken).ConfigureAwait(false);

            // Create asset entry
            var assetEntry = new BundleAssetEntry
            {
                AssetId = assetId,
                Filename = filename,
                ContentType = contentType,
                UncompressedSize = data.Length,
                CompressedSize = compressedSize,
                ContentHash = contentHash,
                Index = index
            };
            _assetEntries.Add(assetEntry);

            // Create index entry (offset is relative to data section start)
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
                assetId, contentType, data.Length, compressedSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compressedBuffer);
        }
    }

    /// <summary>
    /// Adds an asset from a stream.
    /// </summary>
    /// <param name="assetId">Unique asset identifier.</param>
    /// <param name="filename">Original filename.</param>
    /// <param name="contentType">MIME content type.</param>
    /// <param name="dataStream">Stream containing uncompressed asset data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AddAssetFromStreamAsync(
        string assetId,
        string filename,
        string contentType,
        Stream dataStream,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedOrFinalized();

        // Read stream into memory for compression
        // This is necessary because LZ4 needs the full data for compression
        using var memoryStream = new MemoryStream();
        await dataStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);

        await AddAssetAsync(
            assetId,
            filename,
            contentType,
            memoryStream.ToArray(),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds an asset by extracting it from a source bundle reader.
    /// The asset is read, decompressed, then re-compressed into the new bundle.
    /// </summary>
    /// <param name="reader">Bundle reader positioned with header already read.</param>
    /// <param name="assetId">Asset ID to extract from the source bundle.</param>
    /// <param name="filename">Filename for the asset in the new bundle.</param>
    /// <param name="contentType">Content type for the asset.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if asset was found and added, false if not found.</returns>
    public async Task<bool> AddAssetFromBundleAsync(
        BannouBundleReader reader,
        string assetId,
        string filename,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposedOrFinalized();

        // Read and decompress the asset from source bundle
        var assetData = await reader.ReadAssetAsync(assetId, cancellationToken).ConfigureAwait(false);
        if (assetData == null)
        {
            return false;
        }

        await AddAssetAsync(assetId, filename, contentType, assetData, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Writes data to the buffer, flushing to upload when full.
    /// Returns the offset where the data was written (relative to data section start).
    /// </summary>
    private async Task<long> WriteToBufferAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken)
    {
        var offset = _totalDataBytesWritten;

        // First, fill the header reserve buffer (first ~5MB of data)
        if (_headerReserveBuffer.Length < MinPartSize)
        {
            var spaceInReserve = MinPartSize - _headerReserveBuffer.Length;
            var bytesToReserve = (int)Math.Min(spaceInReserve, data.Length);

            await _headerReserveBuffer.WriteAsync(data[..bytesToReserve], cancellationToken)
                .ConfigureAwait(false);
            _totalDataBytesWritten += bytesToReserve;

            // If we consumed all data, we're done
            if (bytesToReserve == data.Length)
            {
                return offset;
            }

            // Otherwise, continue with remaining data to part buffer
            data = data[bytesToReserve..];
        }

        // Write remaining data to part buffer
        var remaining = data;
        while (remaining.Length > 0)
        {
            var spaceInBuffer = _options.PartSizeBytes - _partBuffer.Length;
            var bytesToWrite = (int)Math.Min(spaceInBuffer, remaining.Length);

            await _partBuffer.WriteAsync(remaining[..bytesToWrite], cancellationToken)
                .ConfigureAwait(false);
            _totalDataBytesWritten += bytesToWrite;
            remaining = remaining[bytesToWrite..];

            // If buffer is full, flush it as a part
            if (_partBuffer.Length >= _options.PartSizeBytes)
            {
                await FlushPartBufferAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return offset;
    }

    /// <summary>
    /// Flushes the current part buffer to storage as a multipart part.
    /// </summary>
    private async Task FlushPartBufferAsync(CancellationToken cancellationToken)
    {
        if (_partBuffer.Length == 0)
        {
            return;
        }

        _partBuffer.Position = 0;

        var part = await _storageProvider.UploadPartAsync(
            _uploadSession,
            _nextPartNumber,
            _partBuffer,
            _partBuffer.Length,
            cancellationToken).ConfigureAwait(false);

        _uploadedParts.Add(part);
        _nextPartNumber++;

        _logger?.LogDebug(
            "Uploaded part {PartNumber} ({Size} bytes)",
            part.PartNumber, part.Size);

        // Reset buffer for next part
        _partBuffer.SetLength(0);
        _partBuffer.Position = 0;
    }

    /// <summary>
    /// Finalizes the bundle and completes the multipart upload.
    /// </summary>
    /// <param name="bundleId">Unique bundle identifier.</param>
    /// <param name="name">Bundle name.</param>
    /// <param name="version">Bundle version.</param>
    /// <param name="createdBy">Account ID of creator.</param>
    /// <param name="description">Optional description.</param>
    /// <param name="tags">Optional metadata tags.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Total size of the finalized bundle in bytes.</returns>
    public async Task<long> FinalizeAsync(
        Guid bundleId,
        string name,
        string version,
        string createdBy,
        string? description = null,
        IReadOnlyDictionary<string, string>? tags = null,
        CancellationToken cancellationToken = default)
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

        // Serialize manifest
        var manifestJson = BannouJson.Serialize(manifest);
        var manifestBytes = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        // Build header: [4 bytes length][manifest JSON][index binary]
        using var headerStream = new MemoryStream();

        // Write manifest length (4 bytes, big-endian)
        var lengthBuffer = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lengthBuffer, manifestBytes.Length);
        await headerStream.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);

        // Write manifest
        await headerStream.WriteAsync(manifestBytes, cancellationToken).ConfigureAwait(false);

        // Write index
        var bundleIndex = new BundleIndex { Entries = _indexEntries };
        bundleIndex.WriteTo(headerStream);

        var headerSize = headerStream.Length;

        _logger?.LogDebug(
            "Generated header: manifest={ManifestSize} bytes, index={IndexSize} bytes, total={HeaderSize} bytes",
            manifestBytes.Length,
            headerStream.Length - manifestBytes.Length - 4,
            headerSize);

        // Now we need to construct and upload the parts
        // Part 1: header + header reserve buffer (first chunk of data)
        // Parts 2+: already uploaded
        // Final part: any remaining data in part buffer

        // Build part 1: header + first data chunk
        using var part1Stream = new MemoryStream();
        headerStream.Position = 0;
        await headerStream.CopyToAsync(part1Stream, cancellationToken).ConfigureAwait(false);
        _headerReserveBuffer.Position = 0;
        await _headerReserveBuffer.CopyToAsync(part1Stream, cancellationToken).ConfigureAwait(false);

        // If we have remaining data in part buffer, it becomes the final part
        // But first check if everything fits in one part (small bundle case)
        var totalSize = part1Stream.Length + _partBuffer.Length;

        if (_uploadedParts.Count == 0 && totalSize < MinPartSize * 2)
        {
            // Small bundle - everything fits in one upload (no multipart needed)
            // But we already started multipart, so we need to use at least one part
            // Combine everything into part 1
            _partBuffer.Position = 0;
            await _partBuffer.CopyToAsync(part1Stream, cancellationToken).ConfigureAwait(false);

            part1Stream.Position = 0;
            var part1 = await _storageProvider.UploadPartAsync(
                _uploadSession,
                1,
                part1Stream,
                part1Stream.Length,
                cancellationToken).ConfigureAwait(false);

            _uploadedParts.Insert(0, part1); // Part 1 at the beginning

            _logger?.LogDebug(
                "Uploaded single part bundle: {Size} bytes",
                part1Stream.Length);
        }
        else
        {
            // Upload part 1 (header + first chunk)
            part1Stream.Position = 0;
            var part1 = await _storageProvider.UploadPartAsync(
                _uploadSession,
                1,
                part1Stream,
                part1Stream.Length,
                cancellationToken).ConfigureAwait(false);

            _uploadedParts.Insert(0, part1); // Part 1 at the beginning

            _logger?.LogDebug(
                "Uploaded part 1 (header + first chunk): {Size} bytes",
                part1Stream.Length);

            // If there's remaining data in part buffer, upload as final part
            if (_partBuffer.Length > 0)
            {
                _partBuffer.Position = 0;
                var finalPart = await _storageProvider.UploadPartAsync(
                    _uploadSession,
                    _nextPartNumber,
                    _partBuffer,
                    _partBuffer.Length,
                    cancellationToken).ConfigureAwait(false);

                _uploadedParts.Add(finalPart);

                _logger?.LogDebug(
                    "Uploaded final part {PartNumber}: {Size} bytes",
                    finalPart.PartNumber, finalPart.Size);
            }
        }

        // Complete the multipart upload
        var result = await _storageProvider.CompleteServerMultipartUploadAsync(
            _uploadSession,
            _uploadedParts,
            cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation(
            "Finalized streaming bundle {BundleId}: {AssetCount} assets, {TotalSize} bytes, {PartCount} parts",
            bundleId, _assetEntries.Count, result.Size, _uploadedParts.Count);

        return result.Size;
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
    /// If not finalized, aborts the multipart upload.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // If not finalized, abort the upload to clean up
        if (!_finalized)
        {
            try
            {
                await _storageProvider.AbortServerMultipartUploadAsync(_uploadSession)
                    .ConfigureAwait(false);
                _logger?.LogDebug("Aborted incomplete multipart upload for {Key}", _uploadSession.Key);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to abort multipart upload for {Key}", _uploadSession.Key);
            }
        }

        _partBuffer.Dispose();
        _headerReserveBuffer.Dispose();
    }
}
