using BeyondImmersion.BannouService.Configuration;
using K4os.Compression.LZ4;
using Microsoft.Extensions.Logging;
using System.Buffers.Binary;

namespace BeyondImmersion.BannouService.Asset.Bundles;

/// <summary>
/// Reads assets from the .bannou bundle format.
/// Supports random access to individual assets via the binary index.
/// </summary>
public sealed class BannouBundleReader : IDisposable
{
    private readonly Stream _inputStream;
    private readonly ILogger<BannouBundleReader>? _logger;
    private readonly bool _leaveOpen;
    private BundleManifest? _manifest;
    private BundleIndex? _index;
    private long _dataOffset;
    private bool _disposed;

    /// <summary>
    /// Creates a new bundle reader.
    /// </summary>
    /// <param name="inputStream">The stream containing the bundle.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="leaveOpen">Whether to leave the stream open when disposed.</param>
    public BannouBundleReader(
        Stream inputStream,
        ILogger<BannouBundleReader>? logger = null,
        bool leaveOpen = false)
    {
        _inputStream = inputStream ?? throw new ArgumentNullException(nameof(inputStream));
        _logger = logger;
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Gets the bundle manifest.
    /// </summary>
    public BundleManifest Manifest
    {
        get
        {
            EnsureHeaderRead();
            return _manifest!;
        }
    }

    /// <summary>
    /// Gets the bundle index.
    /// </summary>
    public BundleIndex Index
    {
        get
        {
            EnsureHeaderRead();
            return _index!;
        }
    }

    /// <summary>
    /// Reads and parses the bundle header (manifest and index).
    /// </summary>
    public void ReadHeader()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_manifest != null)
        {
            return;
        }

        // Read manifest length
        Span<byte> lengthBuffer = stackalloc byte[4];
        if (_inputStream.Read(lengthBuffer) != 4)
        {
            throw new InvalidDataException("Failed to read manifest length");
        }

        var manifestLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (manifestLength <= 0 || manifestLength > 10_000_000)
        {
            throw new InvalidDataException($"Invalid manifest length: {manifestLength}");
        }

        // Read manifest JSON
        var manifestBytes = new byte[manifestLength];
        if (_inputStream.Read(manifestBytes) != manifestLength)
        {
            throw new InvalidDataException("Failed to read complete manifest");
        }

        // Deserialize using BannouJson for consistent serialization (T20)
        var manifestJson = System.Text.Encoding.UTF8.GetString(manifestBytes);
        _manifest = BannouJson.Deserialize<BundleManifest>(manifestJson)
            ?? throw new InvalidDataException("Failed to deserialize manifest");

        // Read index
        _index = BundleIndex.ReadFrom(_inputStream);

        // Record where asset data starts
        _dataOffset = _inputStream.Position;

        _logger?.LogDebug(
            "Read bundle {BundleId} header: {AssetCount} assets, format version {Version}",
            _manifest.BundleId,
            _manifest.AssetCount,
            _manifest.FormatVersion);
    }

    /// <summary>
    /// Reads and parses the bundle header asynchronously.
    /// </summary>
    public async Task ReadHeaderAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_manifest != null)
        {
            return;
        }

        var lengthBuffer = new byte[4];
        if (await _inputStream.ReadAsync(lengthBuffer, cancellationToken) != 4)
        {
            throw new InvalidDataException("Failed to read manifest length");
        }

        var manifestLength = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        if (manifestLength <= 0 || manifestLength > 10_000_000)
        {
            throw new InvalidDataException($"Invalid manifest length: {manifestLength}");
        }

        var manifestBytes = new byte[manifestLength];
        if (await _inputStream.ReadAsync(manifestBytes, cancellationToken) != manifestLength)
        {
            throw new InvalidDataException("Failed to read complete manifest");
        }

        // Deserialize using BannouJson for consistent serialization (T20)
        var manifestJson = System.Text.Encoding.UTF8.GetString(manifestBytes);
        _manifest = BannouJson.Deserialize<BundleManifest>(manifestJson)
            ?? throw new InvalidDataException("Failed to deserialize manifest");

        _index = BundleIndex.ReadFrom(_inputStream);
        _dataOffset = _inputStream.Position;

        _logger?.LogDebug(
            "Read bundle {BundleId} header: {AssetCount} assets, format version {Version}",
            _manifest.BundleId,
            _manifest.AssetCount,
            _manifest.FormatVersion);
    }

    /// <summary>
    /// Gets an asset entry by ID.
    /// </summary>
    public BundleAssetEntry? GetAssetEntry(string assetId)
    {
        EnsureHeaderRead();
        return _manifest!.Assets.FirstOrDefault(a => a.AssetId == assetId);
    }

    /// <summary>
    /// Gets an asset entry by index.
    /// </summary>
    public BundleAssetEntry GetAssetEntryByIndex(int index)
    {
        EnsureHeaderRead();

        if (index < 0 || index >= _manifest!.Assets.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _manifest.Assets[index];
    }

    /// <summary>
    /// Reads and decompresses an asset by its ID.
    /// </summary>
    /// <returns>The decompressed asset data, or null if not found.</returns>
    public byte[]? ReadAsset(string assetId)
    {
        var entry = GetAssetEntry(assetId);
        if (entry == null)
        {
            return null;
        }

        return ReadAssetByIndex(entry.Index);
    }

    /// <summary>
    /// Reads and decompresses an asset by its index.
    /// </summary>
    public byte[] ReadAssetByIndex(int index)
    {
        EnsureHeaderRead();

        var indexEntry = _index!.GetEntry(index);
        var assetEntry = GetAssetEntryByIndex(index);

        // Seek to asset data
        _inputStream.Position = _dataOffset + indexEntry.Offset;

        // Read compressed data
        var compressedData = new byte[indexEntry.CompressedSize];
        if (_inputStream.Read(compressedData) != compressedData.Length)
        {
            throw new InvalidDataException($"Failed to read asset data at index {index}");
        }

        // Decompress
        var decompressedData = new byte[indexEntry.UncompressedSize];
        var decodedSize = LZ4Codec.Decode(compressedData, decompressedData);

        if (decodedSize != indexEntry.UncompressedSize)
        {
            throw new InvalidDataException(
                $"Decompression size mismatch: expected {indexEntry.UncompressedSize}, got {decodedSize}");
        }

        _logger?.LogDebug(
            "Read asset {AssetId} ({Size} bytes decompressed from {CompressedSize} bytes)",
            assetEntry.AssetId,
            decodedSize,
            indexEntry.CompressedSize);

        return decompressedData;
    }

    /// <summary>
    /// Reads and decompresses an asset by ID asynchronously.
    /// </summary>
    public async Task<byte[]?> ReadAssetAsync(string assetId, CancellationToken cancellationToken = default)
    {
        var entry = GetAssetEntry(assetId);
        if (entry == null)
        {
            return null;
        }

        return await ReadAssetByIndexAsync(entry.Index, cancellationToken);
    }

    /// <summary>
    /// Reads and decompresses an asset by index asynchronously.
    /// </summary>
    public async Task<byte[]> ReadAssetByIndexAsync(int index, CancellationToken cancellationToken = default)
    {
        EnsureHeaderRead();

        var indexEntry = _index!.GetEntry(index);
        var assetEntry = GetAssetEntryByIndex(index);

        _inputStream.Position = _dataOffset + indexEntry.Offset;

        var compressedData = new byte[indexEntry.CompressedSize];
        if (await _inputStream.ReadAsync(compressedData, cancellationToken) != compressedData.Length)
        {
            throw new InvalidDataException($"Failed to read asset data at index {index}");
        }

        var decompressedData = new byte[indexEntry.UncompressedSize];
        var decodedSize = LZ4Codec.Decode(compressedData, decompressedData);

        if (decodedSize != indexEntry.UncompressedSize)
        {
            throw new InvalidDataException(
                $"Decompression size mismatch: expected {indexEntry.UncompressedSize}, got {decodedSize}");
        }

        _logger?.LogDebug(
            "Read asset {AssetId} ({Size} bytes decompressed from {CompressedSize} bytes)",
            assetEntry.AssetId,
            decodedSize,
            indexEntry.CompressedSize);

        return decompressedData;
    }

    /// <summary>
    /// Reads an asset and writes it to a destination stream.
    /// </summary>
    public async Task ReadAssetToStreamAsync(
        string assetId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var data = await ReadAssetAsync(assetId, cancellationToken) ?? throw new KeyNotFoundException($"Asset not found: {assetId}");
        await destination.WriteAsync(data, cancellationToken);
    }

    /// <summary>
    /// Enumerates all assets in the bundle.
    /// </summary>
    public IEnumerable<(BundleAssetEntry Entry, byte[] Data)> ReadAllAssets()
    {
        EnsureHeaderRead();

        for (var i = 0; i < _manifest!.Assets.Count; i++)
        {
            var entry = _manifest.Assets[i];
            var data = ReadAssetByIndex(i);
            yield return (entry, data);
        }
    }

    /// <summary>
    /// Enumerates all assets in the bundle asynchronously.
    /// </summary>
    public async IAsyncEnumerable<(BundleAssetEntry Entry, byte[] Data)> ReadAllAssetsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureHeaderRead();

        for (var i = 0; i < _manifest!.Assets.Count; i++)
        {
            var entry = _manifest.Assets[i];
            var data = await ReadAssetByIndexAsync(i, cancellationToken);
            yield return (entry, data);
        }
    }

    private void EnsureHeaderRead()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_manifest == null)
        {
            ReadHeader();
        }
    }

    /// <summary>
    /// Disposes the reader.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_leaveOpen)
        {
            _inputStream.Dispose();
        }
    }
}
