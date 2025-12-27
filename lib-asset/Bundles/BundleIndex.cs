using System.Buffers.Binary;

namespace BeyondImmersion.BannouService.Asset.Bundles;

/// <summary>
/// Binary index format for fast random access to bundle assets.
/// Stored as index.bin after the manifest.
/// Each entry is 48 bytes in big-endian format.
/// </summary>
public sealed class BundleIndex
{
    /// <summary>
    /// Size of each index entry in bytes.
    /// </summary>
    public const int EntrySize = 48;

    /// <summary>
    /// Magic bytes at the start of the index file.
    /// </summary>
    public static readonly byte[] MagicBytes = "BNIX"u8.ToArray();

    /// <summary>
    /// Index entries for each asset.
    /// </summary>
    public required IReadOnlyList<BundleIndexEntry> Entries { get; init; }

    /// <summary>
    /// Writes the index to a stream.
    /// </summary>
    public void WriteTo(Stream stream)
    {
        // Write magic bytes
        stream.Write(MagicBytes);

        // Write entry count (4 bytes, big-endian)
        Span<byte> countBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(countBuffer, Entries.Count);
        stream.Write(countBuffer);

        // Write each entry
        foreach (var entry in Entries)
        {
            entry.WriteTo(stream);
        }
    }

    /// <summary>
    /// Reads an index from a stream.
    /// </summary>
    public static BundleIndex ReadFrom(Stream stream)
    {
        // Read and verify magic bytes
        Span<byte> magicBuffer = stackalloc byte[4];
        if (stream.Read(magicBuffer) != 4)
        {
            throw new InvalidDataException("Failed to read index magic bytes");
        }

        if (!magicBuffer.SequenceEqual(MagicBytes))
        {
            throw new InvalidDataException("Invalid bundle index magic bytes");
        }

        // Read entry count
        Span<byte> countBuffer = stackalloc byte[4];
        if (stream.Read(countBuffer) != 4)
        {
            throw new InvalidDataException("Failed to read index entry count");
        }

        var entryCount = BinaryPrimitives.ReadInt32BigEndian(countBuffer);
        if (entryCount < 0 || entryCount > 1_000_000)
        {
            throw new InvalidDataException($"Invalid entry count: {entryCount}");
        }

        // Read entries
        var entries = new List<BundleIndexEntry>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            entries.Add(BundleIndexEntry.ReadFrom(stream));
        }

        return new BundleIndex { Entries = entries };
    }

    /// <summary>
    /// Gets the entry at the specified index.
    /// </summary>
    public BundleIndexEntry GetEntry(int index)
    {
        if (index < 0 || index >= Entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return Entries[index];
    }
}

/// <summary>
/// Single entry in the bundle index.
/// 48 bytes total:
/// - 8 bytes: Offset in the bundle file
/// - 8 bytes: Compressed size
/// - 8 bytes: Uncompressed size
/// - 24 bytes: Content hash (first 24 bytes of SHA256)
/// </summary>
public sealed class BundleIndexEntry
{
    /// <summary>
    /// Offset of the compressed chunk in the bundle file.
    /// </summary>
    public required long Offset { get; init; }

    /// <summary>
    /// Compressed size in bytes.
    /// </summary>
    public required long CompressedSize { get; init; }

    /// <summary>
    /// Uncompressed size in bytes.
    /// </summary>
    public required long UncompressedSize { get; init; }

    /// <summary>
    /// First 24 bytes of the SHA256 content hash.
    /// </summary>
    public required byte[] ContentHashPrefix { get; init; }

    /// <summary>
    /// Writes this entry to a stream.
    /// </summary>
    public void WriteTo(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[BundleIndex.EntrySize];

        BinaryPrimitives.WriteInt64BigEndian(buffer[0..8], Offset);
        BinaryPrimitives.WriteInt64BigEndian(buffer[8..16], CompressedSize);
        BinaryPrimitives.WriteInt64BigEndian(buffer[16..24], UncompressedSize);

        // Copy first 24 bytes of hash
        var hashSpan = ContentHashPrefix.AsSpan();
        var copyLength = Math.Min(24, hashSpan.Length);
        hashSpan[..copyLength].CopyTo(buffer[24..]);

        stream.Write(buffer);
    }

    /// <summary>
    /// Reads an entry from a stream.
    /// </summary>
    public static BundleIndexEntry ReadFrom(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[BundleIndex.EntrySize];
        if (stream.Read(buffer) != BundleIndex.EntrySize)
        {
            throw new InvalidDataException("Failed to read complete index entry");
        }

        var contentHash = new byte[24];
        buffer[24..48].CopyTo(contentHash);

        return new BundleIndexEntry
        {
            Offset = BinaryPrimitives.ReadInt64BigEndian(buffer[0..8]),
            CompressedSize = BinaryPrimitives.ReadInt64BigEndian(buffer[8..16]),
            UncompressedSize = BinaryPrimitives.ReadInt64BigEndian(buffer[16..24]),
            ContentHashPrefix = contentHash
        };
    }
}
