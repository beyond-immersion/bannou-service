using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

/// <summary>
/// Parses Stride's asset index files to map asset URLs to content hashes.
/// </summary>
/// <remarks>
/// Index file format is simple text:
/// {AssetURL} {32-char-hex-hash}
/// {AssetURL} {32-char-hex-hash}
/// ...
///
/// Example:
/// Materials/Vegetation/TestMinimal a65a9e3f9c73c9f8e68aaf9283639541
/// Textures/rock_dif b05ee7fbcacbff609a69277b35856b5b
/// </remarks>
public sealed class StrideIndexParser
{
    private readonly DirectoryInfo _outputDirectory;
    private readonly ILogger<StrideIndexParser>? _logger;
    private IReadOnlyDictionary<string, string>? _index;
    private DirectoryInfo? _dbDir;

    /// <summary>
    /// Creates a new index parser.
    /// </summary>
    /// <param name="outputDirectory">Stride project directory (containing obj/).</param>
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
        var results = new Dictionary<string, StrideIndexEntry>(StringComparer.OrdinalIgnoreCase);

        // Find build output directory
        var buildOutput = FindBuildOutput();
        if (buildOutput == null)
        {
            _logger?.LogWarning("Could not find Stride build output directory");
            return results;
        }

        _dbDir = new DirectoryInfo(Path.Combine(buildOutput.FullName, "data", "db"));
        if (!_dbDir.Exists)
        {
            _logger?.LogWarning("Database directory not found: {DbDir}", _dbDir.FullName);
            return results;
        }

        // Find the index file
        var indexPath = FindIndexFile(_dbDir);
        if (indexPath == null)
        {
            _logger?.LogWarning("Stride index file not found in {DbDir}", _dbDir.FullName);
            return results;
        }

        _logger?.LogDebug("Parsing Stride index at {IndexPath}", indexPath);

        // Parse index file (simple text format)
        _index = ParseIndexFileAsync(indexPath).GetAwaiter().GetResult();

        foreach (var (url, hash) in _index)
        {
            results[url] = new StrideIndexEntry
            {
                Url = url,
                ObjectId = hash,
                DataPath = GetBlobPath(hash),
                Offset = 0,
                Size = 0 // Size will be determined when reading the blob
            };
        }

        _logger?.LogDebug("Parsed {Count} index entries", results.Count);
        return results;
    }

    /// <summary>
    /// Finds the build output directory.
    /// </summary>
    private DirectoryInfo? FindBuildOutput()
    {
        // Stride stores unbundled compiled assets in obj/stride/assetbuild/
        var objPath = Path.Combine(_outputDirectory.FullName, "obj", "stride", "assetbuild");
        var objDir = new DirectoryInfo(objPath);
        if (objDir.Exists && Directory.Exists(Path.Combine(objDir.FullName, "data", "db")))
            return objDir;

        return null;
    }

    /// <summary>
    /// Finds the index file in a database directory.
    /// </summary>
    private static string? FindIndexFile(DirectoryInfo dbDir)
    {
        // Index file pattern: index.{ProjectName}.{Platform}.{GraphicsApi}
        var indexFiles = dbDir.EnumerateFiles("index.*")
            .Where(f => !f.Name.Equals("index", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return indexFiles.FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// Parses an index file and returns a mapping of asset URLs to content hashes.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>> ParseIndexFileAsync(string indexPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(indexPath))
            return result;

        var lines = await File.ReadAllLinesAsync(indexPath);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Format: "{URL} {32-char-hash}"
            // Find the last space - hash is always 32 chars at the end
            var lastSpace = line.LastIndexOf(' ');
            if (lastSpace <= 0 || lastSpace >= line.Length - 32)
                continue;

            var url = line[..lastSpace];
            var hash = line[(lastSpace + 1)..];

            // Validate hash is 32 hex characters
            if (hash.Length == 32 && IsHexString(hash))
            {
                result[url] = hash;
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the path to a compiled asset blob given its content hash.
    /// </summary>
    private string GetBlobPath(string hash)
    {
        if (_dbDir == null)
            throw new InvalidOperationException("Database directory not initialized");

        // Stride stores blobs at: db/{hash[0:2]}/{hash[2:]} (prefix in directory, rest in filename)
        var prefix = hash[..2].ToLowerInvariant();
        var filename = hash[2..].ToLowerInvariant();
        return Path.Combine(_dbDir.FullName, prefix, filename);
    }

    /// <summary>
    /// Reads compiled asset data from database file.
    /// </summary>
    /// <param name="entry">Index entry for the asset.</param>
    /// <returns>Asset data bytes.</returns>
    public byte[] ReadAssetData(StrideIndexEntry entry)
    {
        var blobPath = string.IsNullOrEmpty(entry.DataPath)
            ? GetBlobPath(entry.ObjectId)
            : entry.DataPath;

        if (!File.Exists(blobPath))
            throw new FileNotFoundException($"Data file not found: {blobPath}");

        return File.ReadAllBytes(blobPath);
    }

    /// <summary>
    /// Gets the raw index dictionary for dependency collection.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetIndex()
    {
        return _index ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Gets the database directory.
    /// </summary>
    public DirectoryInfo? GetDbDirectory()
    {
        return _dbDir;
    }

    private static bool IsHexString(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
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
