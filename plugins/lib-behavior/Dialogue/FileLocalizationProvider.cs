// =============================================================================
// File Localization Provider
// Provides localization from file-based string tables.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.Bannou.Behavior.Dialogue;

/// <summary>
/// File-based localization provider supporting multiple sources.
/// </summary>
/// <remarks>
/// <para>
/// Loads localization from YAML files in the format:
/// <code>
/// # strings.en.yaml
/// ui:
///   menu:
///     start: "Start Game"
///     quit: "Quit"
/// dialogue:
///   common:
///     greeting: "Hello!"
/// </code>
/// </para>
/// <para>
/// Keys are hierarchical and accessed via dot notation (e.g., "ui.menu.start").
/// </para>
/// </remarks>
public sealed class FileLocalizationProvider : IAggregateLocalizationProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, ILocalizationSource> _sources;
    private readonly LocalizationConfiguration _config;
    private readonly ILogger<FileLocalizationProvider>? _logger;
    private readonly ITelemetryProvider? _telemetryProvider;
    private readonly SemaphoreSlim _reloadLock;
    private bool _disposed;

    /// <summary>
    /// Creates a new file localization provider with default configuration.
    /// </summary>
    /// <param name="logger">Optional logger.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for span instrumentation.</param>
    public FileLocalizationProvider(ILogger<FileLocalizationProvider>? logger = null, ITelemetryProvider? telemetryProvider = null)
        : this(new LocalizationConfiguration(), logger, telemetryProvider)
    {
    }

    /// <summary>
    /// Creates a new file localization provider with specified configuration.
    /// </summary>
    /// <param name="config">Localization configuration.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for span instrumentation.</param>
    public FileLocalizationProvider(
        LocalizationConfiguration config,
        ILogger<FileLocalizationProvider>? logger = null,
        ITelemetryProvider? telemetryProvider = null)
    {
        _config = config;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _sources = new ConcurrentDictionary<string, ILocalizationSource>(StringComparer.OrdinalIgnoreCase);
        _reloadLock = new SemaphoreSlim(1, 1);
    }

    /// <inheritdoc/>
    public string DefaultLocale => _config.DefaultLocale;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedLocales
    {
        get
        {
            var locales = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var source in _sources.Values)
            {
                foreach (var locale in source.SupportedLocales)
                {
                    locales.Add(locale);
                }
            }
            return locales;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<ILocalizationSource> Sources =>
        _sources.Values.OrderByDescending(s => s.Priority).ToList();

    /// <inheritdoc/>
    public string? GetText(string key, string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(locale);

        // Check sources in priority order
        foreach (var source in Sources)
        {
            var text = source.GetText(key, locale);
            if (text != null)
            {
                return text;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public LocalizedText? GetTextWithFallback(string key, LocalizationContext context)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        // Try primary locale
        var text = GetTextFromSources(key, context.Locale, out var sourceName);
        if (text != null)
        {
            return new LocalizedText
            {
                Text = text,
                FoundLocale = context.Locale,
                IsFallback = false,
                SourceName = sourceName
            };
        }

        // Try fallback chain
        foreach (var fallback in context.FallbackLocales)
        {
            text = GetTextFromSources(key, fallback, out sourceName);
            if (text != null)
            {
                return new LocalizedText
                {
                    Text = text,
                    FoundLocale = fallback,
                    IsFallback = true,
                    SourceName = sourceName
                };
            }
        }

        // Try default locale
        text = GetTextFromSources(key, context.DefaultLocale, out sourceName);
        if (text != null)
        {
            return new LocalizedText
            {
                Text = text,
                FoundLocale = context.DefaultLocale,
                IsFallback = true,
                SourceName = sourceName
            };
        }

        if (_config.LogMissingKeys)
        {
            _logger?.LogDebug(
                "Missing localization key: {Key}, locale: {Locale}",
                key,
                context.Locale);
        }

        return null;
    }

    /// <inheritdoc/>
    public bool HasKey(string key, string locale)
    {
        return GetText(key, locale) != null;
    }

    /// <inheritdoc/>
    public void RegisterSource(ILocalizationSource source)
    {

        _sources[source.Name] = source;
        _logger?.LogDebug(
            "Registered localization source: {Name}, priority: {Priority}",
            source.Name,
            source.Priority);
    }

    /// <inheritdoc/>
    public bool RemoveSource(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _sources.TryRemove(name, out _);
    }

    /// <inheritdoc/>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "FileLocalizationProvider.ReloadAsync");
        await _reloadLock.WaitAsync(ct);
        try
        {
            foreach (var source in _sources.Values)
            {
                await source.ReloadAsync(ct);
            }
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    private string? GetTextFromSources(string key, string locale, out string? sourceName)
    {
        foreach (var source in Sources)
        {
            var text = source.GetText(key, locale);
            if (text != null)
            {
                sourceName = source.Name;
                return text;
            }
        }

        sourceName = null;
        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _reloadLock.Dispose();

        foreach (var source in _sources.Values)
        {
            if (source is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _sources.Clear();
    }
}

/// <summary>
/// File-based localization source that loads from YAML files.
/// </summary>
public sealed class YamlFileLocalizationSource : ILocalizationSource, IDisposable
{
    private readonly string _directory;
    private readonly string _filePattern;
    private readonly IDeserializer _deserializer;
    private readonly ILogger<YamlFileLocalizationSource>? _logger;
    private readonly ITelemetryProvider? _telemetryProvider;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _localeData;
    private readonly SemaphoreSlim _loadLock;
    private bool _disposed;

    /// <summary>
    /// Creates a new YAML file localization source.
    /// </summary>
    /// <param name="name">Source name.</param>
    /// <param name="directory">Directory containing localization files.</param>
    /// <param name="filePattern">File pattern (e.g., "strings.{locale}.yaml").</param>
    /// <param name="priority">Source priority.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="telemetryProvider">Optional telemetry provider for span instrumentation.</param>
    public YamlFileLocalizationSource(
        string name,
        string directory,
        string filePattern = "strings.{locale}.yaml",
        int priority = 0,
        ILogger<YamlFileLocalizationSource>? logger = null,
        ITelemetryProvider? telemetryProvider = null)
    {
        Name = name;
        _directory = directory;
        _filePattern = filePattern;
        Priority = priority;
        _logger = logger;
        _telemetryProvider = telemetryProvider;

        _localeData = new ConcurrentDictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        _loadLock = new SemaphoreSlim(1, 1);

        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public int Priority { get; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> SupportedLocales => _localeData.Keys.ToList();

    /// <inheritdoc/>
    public string? GetText(string key, string locale)
    {
        EnsureLocaleLoaded(locale);

        if (!_localeData.TryGetValue(locale, out var data))
        {
            return null;
        }

        return data.TryGetValue(key, out var text) ? text : null;
    }

    /// <inheritdoc/>
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "YamlFileLocalizationSource.ReloadAsync");
        await _loadLock.WaitAsync(ct);
        try
        {
            _localeData.Clear();

            // Scan directory for locale files
            if (!Directory.Exists(_directory))
            {
                return;
            }

            var pattern = _filePattern.Replace("{locale}", "*");
            var files = Directory.GetFiles(_directory, pattern);

            foreach (var file in files)
            {
                var locale = ExtractLocaleFromFilename(file);
                if (locale != null)
                {
                    await LoadLocaleFileAsync(locale, file, ct);
                }
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Synchronously loads a locale if not already loaded.
    /// Uses synchronous file I/O to avoid sync-over-async per IMPLEMENTATION TENETS.
    /// </summary>
    private void EnsureLocaleLoaded(string locale)
    {
        if (_localeData.ContainsKey(locale))
        {
            return;
        }

        _loadLock.Wait();
        try
        {
            if (_localeData.ContainsKey(locale))
            {
                return;
            }

            var fileName = _filePattern.Replace("{locale}", locale);
            var filePath = Path.Combine(_directory, fileName);

            if (File.Exists(filePath))
            {
                // Use synchronous file read since this method is inherently synchronous.
                // The GetText interface is sync for hot-path performance, so we use
                // sync IO rather than sync-over-async which could cause deadlocks.
                LoadLocaleFileSync(locale, filePath);
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <summary>
    /// Synchronously loads a locale file.
    /// </summary>
    private void LoadLocaleFileSync(string locale, string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            var raw = _deserializer.Deserialize<Dictionary<string, object>>(content);

            if (raw == null)
            {
                return;
            }

            var flattened = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FlattenDictionary(raw, "", flattened);

            _localeData[locale] = flattened;

            _logger?.LogDebug(
                "Loaded localization file: {FilePath}, locale: {Locale}, keys: {KeyCount}",
                filePath,
                locale,
                flattened.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to load localization file: {FilePath}",
                filePath);
        }
    }

    private async Task LoadLocaleFileAsync(string locale, string filePath, CancellationToken ct)
    {
        using var activity = _telemetryProvider?.StartActivity("bannou.behavior", "YamlFileLocalizationSource.LoadLocaleFileAsync");
        try
        {
            var content = await File.ReadAllTextAsync(filePath, ct);
            var raw = _deserializer.Deserialize<Dictionary<string, object>>(content);

            if (raw == null)
            {
                return;
            }

            var flattened = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            FlattenDictionary(raw, "", flattened);

            _localeData[locale] = flattened;

            _logger?.LogDebug(
                "Loaded localization file: {FilePath}, locale: {Locale}, keys: {KeyCount}",
                filePath,
                locale,
                flattened.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(
                ex,
                "Failed to load localization file: {FilePath}",
                filePath);
        }
    }

    private static void FlattenDictionary(
        Dictionary<string, object> source,
        string prefix,
        Dictionary<string, string> target)
    {
        foreach (var kvp in source)
        {
            var key = string.IsNullOrEmpty(prefix) ? kvp.Key : $"{prefix}.{kvp.Key}";

            if (kvp.Value is Dictionary<object, object> nestedDict)
            {
                // Convert to proper dictionary
                // ToString() on non-null dictionary key cannot return null in practice;
                // coalesce satisfies compiler nullable analysis (will never execute)
                var converted = nestedDict.ToDictionary(
                    k => k.Key.ToString() ?? string.Empty,
                    v => v.Value);
                FlattenDictionary(converted, key, target);
            }
            else if (kvp.Value is Dictionary<string, object> stringDict)
            {
                FlattenDictionary(stringDict, key, target);
            }
            else if (kvp.Value != null)
            {
                // ToString() on non-null object cannot return null in practice;
                // coalesce satisfies compiler nullable analysis (will never execute)
                target[key] = kvp.Value.ToString() ?? string.Empty;
            }
        }
    }

    private string? ExtractLocaleFromFilename(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var pattern = _filePattern;

        // Find where {locale} is in the pattern
        var localeStart = pattern.IndexOf("{locale}", StringComparison.Ordinal);
        if (localeStart < 0)
        {
            return null;
        }

        // Get prefix and suffix
        var prefix = pattern[..localeStart];
        var suffix = pattern[(localeStart + "{locale}".Length)..];

        // Extract locale from filename
        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fileName[prefix.Length..^suffix.Length];
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadLock.Dispose();
    }
}
