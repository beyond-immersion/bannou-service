using BeyondImmersion.Bannou.AssetBundler.Abstractions;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.Bannou.AssetBundler.State;

/// <summary>
/// Manages bundler state for incremental builds.
/// Tracks which sources have been processed and their content hashes.
/// </summary>
public sealed class BundlerStateManager
{
    private readonly DirectoryInfo _stateDirectory;
    private readonly string _stateFilePath;
    private readonly ILogger<BundlerStateManager>? _logger;
    private BundlerState _state;

    /// <summary>
    /// Creates a new state manager.
    /// </summary>
    /// <param name="stateDirectory">Directory to store state file.</param>
    /// <param name="logger">Optional logger.</param>
    public BundlerStateManager(DirectoryInfo stateDirectory, ILogger<BundlerStateManager>? logger = null)
    {
        _stateDirectory = stateDirectory;
        _stateFilePath = Path.Combine(stateDirectory.FullName, "bundler-state.json");
        _logger = logger;
        _state = LoadState();
    }

    /// <summary>
    /// Checks if a source needs processing based on content hash.
    /// </summary>
    /// <param name="source">The asset source to check.</param>
    /// <returns>True if the source needs processing.</returns>
    public bool NeedsProcessing(IAssetSource source)
    {
        if (!_state.Sources.TryGetValue(source.SourceId, out var record))
            return true;

        return record.ContentHash != source.ContentHash;
    }

    /// <summary>
    /// Records that a source has been successfully processed.
    /// </summary>
    /// <param name="record">The processing record.</param>
    public void RecordProcessed(SourceProcessingRecord record)
    {
        _state.Sources[record.SourceId] = record;
        _state.LastUpdated = DateTimeOffset.UtcNow;
        SaveState();

        _logger?.LogDebug("Recorded processing state for {SourceId}", record.SourceId);
    }

    /// <summary>
    /// Gets the processing record for a source, if it exists.
    /// </summary>
    /// <param name="sourceId">The source identifier.</param>
    /// <returns>The record, or null if not found.</returns>
    public SourceProcessingRecord? GetRecord(string sourceId)
    {
        return _state.Sources.TryGetValue(sourceId, out var record) ? record : null;
    }

    /// <summary>
    /// Gets all processing records.
    /// </summary>
    /// <returns>All recorded sources.</returns>
    public IReadOnlyDictionary<string, SourceProcessingRecord> GetAllRecords()
    {
        return _state.Sources;
    }

    /// <summary>
    /// Removes a processing record.
    /// </summary>
    /// <param name="sourceId">The source identifier.</param>
    /// <returns>True if the record was removed.</returns>
    public bool RemoveRecord(string sourceId)
    {
        if (_state.Sources.Remove(sourceId))
        {
            SaveState();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Computes SHA256 hash of a file for change detection.
    /// </summary>
    /// <param name="file">The file to hash.</param>
    /// <returns>Lowercase hex-encoded hash.</returns>
    public static string ComputeHash(FileInfo file)
    {
        using var stream = file.OpenRead();
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes SHA256 hash of a directory (based on file contents and paths).
    /// </summary>
    /// <param name="directory">The directory to hash.</param>
    /// <returns>Lowercase hex-encoded hash.</returns>
    public static string ComputeDirectoryHash(DirectoryInfo directory)
    {
        using var sha = SHA256.Create();
        var files = directory.GetFiles("*", SearchOption.AllDirectories)
            .OrderBy(f => f.FullName)
            .ToList();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(directory.FullName, file.FullName);
            var pathBytes = Encoding.UTF8.GetBytes(relativePath);
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, pathBytes, 0);

            using var stream = file.OpenRead();
            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha.TransformBlock(buffer, 0, bytesRead, buffer, 0);
            }
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash ?? []).ToLowerInvariant();
    }

    private BundlerState LoadState()
    {
        if (!File.Exists(_stateFilePath))
            return new BundlerState();

        try
        {
            var json = File.ReadAllText(_stateFilePath);
            return JsonSerializer.Deserialize<BundlerState>(json) ?? new BundlerState();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load state file, starting fresh");
            return new BundlerState();
        }
    }

    private void SaveState()
    {
        _stateDirectory.Create();
        var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_stateFilePath, json);
    }
}

internal sealed class BundlerState
{
    public Dictionary<string, SourceProcessingRecord> Sources { get; set; } = new();
    public DateTimeOffset LastUpdated { get; set; }
}
