using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

/// <summary>
/// Parses Stride's asset index to locate compiled assets.
/// </summary>
public sealed class StrideIndexParser
{
    private readonly DirectoryInfo _outputDirectory;
    private readonly ILogger<StrideIndexParser>? _logger;

    /// <summary>
    /// Creates a new index parser.
    /// </summary>
    /// <param name="outputDirectory">Stride build output directory.</param>
    /// <param name="logger">Optional logger.</param>
    public StrideIndexParser(DirectoryInfo outputDirectory, ILogger<StrideIndexParser>? logger = null)
    {
        _outputDirectory = outputDirectory;
        _logger = logger;
    }

    /// <summary>
    /// Parses the Stride index and returns compiled asset entries.
    /// </summary>
    /// <returns>Dictionary of asset URL to index entry.</returns>
    public Dictionary<string, StrideIndexEntry> ParseIndex()
    {
        var results = new Dictionary<string, StrideIndexEntry>();

        // Look for the index file in typical Stride output locations
        var indexPaths = new[]
        {
            Path.Combine(_outputDirectory.FullName, "bin", "Release", "net8.0", "data", "index"),
            Path.Combine(_outputDirectory.FullName, "bin", "Debug", "net8.0", "data", "index"),
            Path.Combine(_outputDirectory.FullName, "obj", "stride", "data", "index"),
            Path.Combine(_outputDirectory.FullName, "data", "index")
        };

        var indexPath = indexPaths.FirstOrDefault(File.Exists);
        if (indexPath == null)
        {
            _logger?.LogWarning("Stride index file not found in expected locations");
            return results;
        }

        _logger?.LogDebug("Parsing Stride index at {IndexPath}", indexPath);

        try
        {
            var indexData = File.ReadAllBytes(indexPath);
            results = ParseIndexBinary(indexData);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse Stride index file");
        }

        return results;
    }

    /// <summary>
    /// Parses the Stride binary index format.
    /// </summary>
    private Dictionary<string, StrideIndexEntry> ParseIndexBinary(byte[] data)
    {
        var results = new Dictionary<string, StrideIndexEntry>();

        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        // Stride index format:
        // - Header (magic, version, entry count)
        // - Entries (URL string, ObjectId, location info)

        // Check for text-based index (JSON)
        if (data.Length > 0 && data[0] == '{')
        {
            return ParseIndexJson(data);
        }

        // Binary format parsing
        try
        {
            // Read header
            var magic = reader.ReadInt32();
            if (magic != 0x58444E49) // "INDX"
            {
                _logger?.LogWarning("Unexpected index magic: 0x{Magic:X8}", magic);
                return results;
            }

            var version = reader.ReadInt32();
            var entryCount = reader.ReadInt32();

            _logger?.LogDebug("Index version {Version}, {EntryCount} entries", version, entryCount);

            for (var i = 0; i < entryCount; i++)
            {
                var urlLength = reader.ReadInt32();
                var urlBytes = reader.ReadBytes(urlLength);
                var url = System.Text.Encoding.UTF8.GetString(urlBytes);

                var objectIdBytes = reader.ReadBytes(20); // ObjectId is 20 bytes
                var objectId = Convert.ToHexString(objectIdBytes).ToLowerInvariant();

                var dataPath = reader.ReadString();
                var offset = reader.ReadInt64();
                var size = reader.ReadInt64();

                results[url] = new StrideIndexEntry
                {
                    Url = url,
                    ObjectId = objectId,
                    DataPath = dataPath,
                    Offset = offset,
                    Size = size
                };
            }
        }
        catch (EndOfStreamException)
        {
            _logger?.LogWarning("Reached end of index file unexpectedly");
        }

        return results;
    }

    /// <summary>
    /// Parses JSON-based index (used in some Stride versions).
    /// </summary>
    private Dictionary<string, StrideIndexEntry> ParseIndexJson(byte[] data)
    {
        var results = new Dictionary<string, StrideIndexEntry>();

        try
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Entries", out var entries))
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    var url = entry.GetProperty("Url").GetString() ?? string.Empty;
                    var objectId = entry.GetProperty("ObjectId").GetString() ?? string.Empty;

                    results[url] = new StrideIndexEntry
                    {
                        Url = url,
                        ObjectId = objectId,
                        DataPath = entry.TryGetProperty("DataPath", out var dp)
                            ? dp.GetString() ?? string.Empty
                            : string.Empty,
                        Offset = entry.TryGetProperty("Offset", out var off) ? off.GetInt64() : 0,
                        Size = entry.TryGetProperty("Size", out var sz) ? sz.GetInt64() : 0
                    };
                }
            }
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse JSON index");
        }

        return results;
    }

    /// <summary>
    /// Finds all database files containing compiled assets.
    /// </summary>
    /// <returns>List of database file paths.</returns>
    public IReadOnlyList<string> FindDatabaseFiles()
    {
        var results = new List<string>();

        var searchPaths = new[]
        {
            Path.Combine(_outputDirectory.FullName, "bin", "Release", "net8.0", "data", "db"),
            Path.Combine(_outputDirectory.FullName, "bin", "Debug", "net8.0", "data", "db"),
            Path.Combine(_outputDirectory.FullName, "obj", "stride", "data", "db"),
            Path.Combine(_outputDirectory.FullName, "data", "db")
        };

        foreach (var searchPath in searchPaths)
        {
            if (Directory.Exists(searchPath))
            {
                results.AddRange(Directory.GetFiles(searchPath, "*.bundle", SearchOption.AllDirectories));
            }
        }

        return results;
    }

    /// <summary>
    /// Reads compiled asset data from database file.
    /// </summary>
    /// <param name="entry">Index entry for the asset.</param>
    /// <returns>Asset data bytes.</returns>
    public byte[] ReadAssetData(StrideIndexEntry entry)
    {
        if (string.IsNullOrEmpty(entry.DataPath))
            throw new InvalidOperationException($"No data path for asset {entry.Url}");

        var fullPath = Path.IsPathRooted(entry.DataPath)
            ? entry.DataPath
            : Path.Combine(_outputDirectory.FullName, entry.DataPath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Data file not found: {fullPath}");

        using var fs = File.OpenRead(fullPath);
        fs.Seek(entry.Offset, SeekOrigin.Begin);

        var data = new byte[entry.Size];
        var bytesRead = fs.Read(data, 0, (int)entry.Size);

        if (bytesRead != entry.Size)
            throw new InvalidOperationException(
                $"Expected to read {entry.Size} bytes but got {bytesRead}");

        return data;
    }
}

/// <summary>
/// Entry in the Stride asset index.
/// </summary>
public sealed class StrideIndexEntry
{
    /// <summary>Asset URL (e.g., "Models/MyModel").</summary>
    public required string Url { get; init; }

    /// <summary>Stride ObjectId hash.</summary>
    public required string ObjectId { get; init; }

    /// <summary>Path to the database file containing this asset.</summary>
    public string DataPath { get; init; } = string.Empty;

    /// <summary>Offset within the database file.</summary>
    public long Offset { get; init; }

    /// <summary>Size of the asset data in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Asset GUID assigned by Stride.</summary>
    public Guid Guid { get; init; }

    /// <summary>Asset type extension.</summary>
    public string Extension { get; init; } = string.Empty;
}
