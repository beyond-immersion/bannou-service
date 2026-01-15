// =============================================================================
// External Dialogue Loader
// Loads external dialogue YAML files with caching support.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.Bannou.Behavior.Dialogue;

/// <summary>
/// Default implementation of external dialogue file loading.
/// </summary>
/// <remarks>
/// <para>
/// Loads dialogue files from registered directories with support for:
/// </para>
/// <list type="bullet">
/// <item>Multiple base directories with priority ordering</item>
/// <item>In-memory caching with configurable expiration</item>
/// <item>YAML file parsing with snake_case naming</item>
/// <item>Automatic override priority sorting</item>
/// </list>
/// </remarks>
public sealed class ExternalDialogueLoader : IExternalDialogueLoader, IDisposable
{
    private readonly List<DialogueDirectory> _directories;
    private readonly IMemoryCache _cache;
    private readonly ExternalDialogueLoaderOptions _options;
    private readonly IDeserializer _deserializer;
    private readonly ILogger<ExternalDialogueLoader>? _logger;
    private readonly object _directoryLock = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new external dialogue loader with default options.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    public ExternalDialogueLoader(ILogger<ExternalDialogueLoader>? logger = null)
        : this(new ExternalDialogueLoaderOptions(), logger)
    {
    }

    /// <summary>
    /// Creates a new external dialogue loader with specified options.
    /// </summary>
    /// <param name="options">Loader options.</param>
    /// <param name="logger">Optional logger.</param>
    public ExternalDialogueLoader(
        ExternalDialogueLoaderOptions options,
        ILogger<ExternalDialogueLoader>? logger = null)
    {
        _options = options;
        _logger = logger;
        _directories = new List<DialogueDirectory>();
        _cache = new MemoryCache(new MemoryCacheOptions());

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        // Register initial directories
        foreach (var dir in options.BaseDirectories)
        {
            RegisterDirectory(dir);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<DialogueDirectory> RegisteredDirectories
    {
        get
        {
            lock (_directoryLock)
            {
                return _directories.OrderByDescending(d => d.Priority).ToList();
            }
        }
    }

    /// <inheritdoc/>
    public void RegisterDirectory(string directory, int priority = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(directory);

        lock (_directoryLock)
        {
            // Remove existing registration for this path
            _directories.RemoveAll(d =>
                d.Path.Equals(directory, StringComparison.OrdinalIgnoreCase));

            _directories.Add(new DialogueDirectory
            {
                Path = directory,
                Priority = priority
            });

            _logger?.LogDebug(
                "Registered dialogue directory: {Directory}, priority: {Priority}",
                directory,
                priority);
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalDialogueFile?> LoadAsync(string reference, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(reference);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Check cache first
        var cacheKey = GetCacheKey(reference);
        if (_options.EnableCaching && _cache.TryGetValue(cacheKey, out ExternalDialogueFile? cached))
        {
            return cached;
        }

        // Find and load file
        var filePath = FindFile(reference);
        if (filePath == null)
        {
            return null;
        }

        var file = await LoadFileAsync(reference, filePath, ct);
        if (file == null)
        {
            return null;
        }

        // Cache the result
        if (_options.EnableCaching)
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.CacheExpiration
            };
            _cache.Set(cacheKey, file, cacheOptions);
        }

        return file;
    }

    /// <inheritdoc/>
    public bool Exists(string reference)
    {
        ArgumentException.ThrowIfNullOrEmpty(reference);
        return FindFile(reference) != null;
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
        if (_cache is MemoryCache mc)
        {
            mc.Compact(1.0);
        }
    }

    /// <inheritdoc/>
    public async Task<ExternalDialogueFile?> ReloadAsync(string reference, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(reference);

        // Remove from cache
        var cacheKey = GetCacheKey(reference);
        _cache.Remove(cacheKey);

        // Reload
        return await LoadAsync(reference, ct);
    }

    private string? FindFile(string reference)
    {
        // Normalize reference path
        var normalizedRef = reference.Replace('/', Path.DirectorySeparatorChar);

        lock (_directoryLock)
        {
            // Search directories in priority order
            var sortedDirs = _directories.OrderByDescending(d => d.Priority);

            foreach (var dir in sortedDirs)
            {
                foreach (var ext in _options.FileExtensions)
                {
                    var filePath = Path.Combine(dir.Path, normalizedRef + ext);
                    if (File.Exists(filePath))
                    {
                        return filePath;
                    }
                }

                // Also try without extension (in case reference includes it)
                var directPath = Path.Combine(dir.Path, normalizedRef);
                if (File.Exists(directPath))
                {
                    return directPath;
                }
            }
        }

        return null;
    }

    private async Task<ExternalDialogueFile?> LoadFileAsync(
        string reference,
        string filePath,
        CancellationToken ct)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var raw = _deserializer.Deserialize<RawDialogueFile>(content);

            if (raw == null)
            {
                _logger?.LogWarning(
                    "Failed to parse dialogue file (null result): {FilePath}",
                    filePath);
                return null;
            }

            var file = ConvertToDialogueFile(reference, filePath, raw);

            if (_options.LogFileLoads)
            {
                _logger?.LogDebug(
                    "Loaded dialogue file: {Reference} from {FilePath}, " +
                    "{LocaleCount} localizations, {OverrideCount} overrides",
                    reference,
                    filePath,
                    file.Localizations.Count,
                    file.Overrides.Count);
            }

            return file;
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to load dialogue file: {FilePath}",
                filePath);
            return null;
        }
    }

    private static ExternalDialogueFile ConvertToDialogueFile(
        string reference,
        string filePath,
        RawDialogueFile raw)
    {
        // Convert localizations
        var localizations = raw.Localizations ?? new Dictionary<string, string>();

        // Convert and sort overrides by priority (highest first)
        var overrides = (raw.Overrides ?? [])
            .Select(o => new DialogueOverride
            {
                Condition = o.Condition ?? string.Empty,
                Text = o.Text ?? string.Empty,
                Priority = o.Priority,
                Locale = o.Locale,
                Metadata = o.Metadata
            })
            .OrderByDescending(o => o.Priority)
            .ToList();

        // Get file modification time
        var lastModified = File.GetLastWriteTimeUtc(filePath);

        return new ExternalDialogueFile
        {
            Reference = reference,
            FilePath = filePath,
            Localizations = localizations,
            Overrides = overrides,
            Metadata = raw.Metadata,
            LastModified = lastModified
        };
    }

    private static string GetCacheKey(string reference)
    {
        return $"dialogue:{reference.ToLowerInvariant()}";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cache.Dispose();
    }

    /// <summary>
    /// Raw YAML structure for dialogue files.
    /// </summary>
    private sealed class RawDialogueFile
    {
        public Dictionary<string, string>? Localizations { get; set; }
        public List<RawOverride>? Overrides { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// Raw YAML structure for overrides.
    /// </summary>
    private sealed class RawOverride
    {
        public string? Condition { get; set; }
        public string? Text { get; set; }
        public int Priority { get; set; }
        public string? Locale { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
    }
}
