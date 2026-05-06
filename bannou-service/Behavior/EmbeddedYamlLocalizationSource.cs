// =============================================================================
// Embedded YAML Localization Source Base
// Base implementation for ILocalizationSource implementations that load YAML
// localization tables from assembly embedded resources. Mirrors the pattern
// used by EmbeddedResourceProvider in Providers/.
// =============================================================================

using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Base class for <see cref="ILocalizationSource"/> implementations that load
/// YAML localization tables from assembly embedded resources.
/// </summary>
/// <remarks>
/// <para>
/// Subclasses specify the assembly, resource prefix, and (optionally) file
/// pattern. Resources are discovered by scanning the assembly's embedded
/// resources matching <c>{ResourcePrefix}{FilePattern with locale}</c> —
/// the default <see cref="FilePattern"/> is <c>strings.{locale}.yaml</c>.
/// </para>
/// <para>
/// <b>Naming Convention</b>: Embedded resources should follow the pattern
/// <c>{Namespace}.{Folder}.strings.{locale}.yaml</c>. The locale segment is
/// extracted at scan time. Hierarchical YAML keys are flattened with dot
/// notation (e.g., <c>ui.menu.start</c>) for compatibility with the rest of
/// the localization system.
/// </para>
/// <para>
/// <b>⚠️ MSBuild satellite-assembly trap</b>: When you add YAML files to your
/// plugin's <c>.csproj</c> with <c>&lt;EmbeddedResource Include="..." /&gt;</c>,
/// MSBuild's culture-detection heuristic interprets filenames containing a
/// locale segment (e.g., <c>strings.en.yaml</c>, <c>strings.fr.yaml</c>) as
/// culture-coded resources and routes them into <i>satellite assemblies</i>
/// (subdirectories <c>en/</c>, <c>fr/</c>, etc., next to the main DLL) rather
/// than embedding them in the main assembly. <see cref="Assembly.GetManifestResourceStream(string)"/>
/// on the main assembly will return <c>null</c> for those resources, and this
/// source will silently report empty bundles.
/// </para>
/// <para>
/// <b>The fix is mandatory</b>: add <c>&lt;WithCulture&gt;false&lt;/WithCulture&gt;</c>
/// metadata to the <c>&lt;EmbeddedResource&gt;</c> entry so MSBuild keeps the
/// resources in the main assembly:
/// </para>
/// <code>
/// &lt;ItemGroup&gt;
///   &lt;EmbeddedResource Include="Localization\*.yaml"&gt;
///     &lt;WithCulture&gt;false&lt;/WithCulture&gt;
///   &lt;/EmbeddedResource&gt;
/// &lt;/ItemGroup&gt;
/// </code>
/// <para>
/// You can verify the resources are correctly embedded by checking
/// <c>Assembly.GetManifestResourceNames()</c> at runtime, or by confirming
/// that no <c>en/</c>/<c>fr/</c>/etc. satellite-assembly subdirectories appear
/// next to your built DLL in <c>bin/</c>.
/// </para>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// [BannouHelperService("behavior-embedded-localization", typeof(IBehaviorService),
///     typeof(ILocalizationSource), lifetime: ServiceLifetime.Singleton)]
/// public sealed class BehaviorEmbeddedLocalizationSource : EmbeddedYamlLocalizationSource
/// {
///     public BehaviorEmbeddedLocalizationSource(
///         ILogger&lt;BehaviorEmbeddedLocalizationSource&gt; logger,
///         ITelemetryProvider telemetryProvider)
///         : base(logger, telemetryProvider) { }
///     public override string Name =&gt; "behavior-embedded";
///     public override int Priority =&gt; 50;
///     protected override Assembly ResourceAssembly =&gt; typeof(BehaviorEmbeddedLocalizationSource).Assembly;
///     protected override string ResourcePrefix =&gt; "BeyondImmersion.Bannou.Behavior.Localization.";
/// }
/// </code>
/// <para>
/// Subclasses are AOT-safe: they reflect only on their own compile-time-known
/// assembly to enumerate <c>GetManifestResourceNames</c> / load streams via
/// <c>GetManifestResourceStream</c>. No runtime type discovery, no dynamic
/// generic construction, and no Reflection.Emit.
/// </para>
/// </remarks>
public abstract class EmbeddedYamlLocalizationSource : ILocalizationSource
{
    private readonly ILogger _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IDeserializer _deserializer;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _localeData;
    private readonly object _loadLock = new();
    private bool _allLocalesDiscovered;

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract int Priority { get; }

    /// <summary>Assembly containing the embedded YAML localization resources.</summary>
    /// <remarks>
    /// Must return a compile-time-known assembly reference (e.g.,
    /// <c>typeof(MyConcreteSource).Assembly</c>). Returning a runtime-discovered
    /// assembly would violate AOT-compatibility constraints for shipping plugin code.
    /// </remarks>
    protected abstract Assembly ResourceAssembly { get; }

    /// <summary>
    /// Resource name prefix used to filter embedded resources
    /// (e.g., <c>"BeyondImmersion.Bannou.Behavior.Localization."</c>).
    /// Must end with a period.
    /// </summary>
    protected abstract string ResourcePrefix { get; }

    /// <summary>
    /// File pattern within the prefix; default <c>strings.{locale}.yaml</c>.
    /// The token <c>{locale}</c> is replaced with each discovered locale.
    /// </summary>
    protected virtual string FilePattern => "strings.{locale}.yaml";

    /// <summary>
    /// Creates a new embedded YAML localization source.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation on async methods.</param>
    protected EmbeddedYamlLocalizationSource(
        ILogger logger,
        ITelemetryProvider telemetryProvider)
    {
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _localeData = new ConcurrentDictionary<string, Dictionary<string, string>>(
            StringComparer.OrdinalIgnoreCase);
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedLocales
    {
        get
        {
            EnsureAllLocalesDiscovered();
            return _localeData.Keys.ToList();
        }
    }

    /// <inheritdoc />
    public string? GetText(string key, string locale)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(locale);

        EnsureLocaleLoaded(locale);
        if (!_localeData.TryGetValue(locale, out var data))
        {
            return null;
        }
        return data.TryGetValue(key, out var text) ? text : null;
    }

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.behavior", "EmbeddedYamlLocalizationSource.ReloadAsync");

        lock (_loadLock)
        {
            _localeData.Clear();
            _allLocalesDiscovered = false;
        }

        // No async I/O — the lock above protects the synchronous cache reset.
        // The await ensures T23 compliance (Task-returning method must be async).
        await Task.CompletedTask;
    }

    private void EnsureLocaleLoaded(string locale)
    {
        if (_localeData.ContainsKey(locale)) return;

        lock (_loadLock)
        {
            if (_localeData.ContainsKey(locale)) return;

            var resourceName =
                $"{ResourcePrefix}{FilePattern.Replace("{locale}", locale)}";
            using var stream = ResourceAssembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                // Locale not present in this source. Cache an empty dictionary
                // so we don't re-attempt the lookup on every GetText call.
                _localeData[locale] = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                return;
            }

            try
            {
                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                var raw = _deserializer.Deserialize<Dictionary<string, object>>(content);
                if (raw == null)
                {
                    _localeData[locale] = new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);
                    return;
                }

                var flattened = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
                FlattenDictionary(raw, "", flattened);
                _localeData[locale] = flattened;

                _logger.LogDebug(
                    "Loaded embedded localization resource: {Resource}, locale: {Locale}, keys: {KeyCount}",
                    resourceName,
                    locale,
                    flattened.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to load embedded localization resource: {Resource}",
                    resourceName);
                // Cache empty dict on failure so repeated calls don't keep retrying.
                _localeData[locale] = new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private void EnsureAllLocalesDiscovered()
    {
        if (_allLocalesDiscovered) return;

        lock (_loadLock)
        {
            if (_allLocalesDiscovered) return;

            var pattern = FilePattern; // e.g. "strings.{locale}.yaml"
            var localeStart = pattern.IndexOf("{locale}", StringComparison.Ordinal);
            if (localeStart < 0)
            {
                _allLocalesDiscovered = true;
                return;
            }

            var prefix = pattern[..localeStart];
            var suffix = pattern[(localeStart + "{locale}".Length)..];

            foreach (var resourceName in ResourceAssembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                    continue;
                var trailing = resourceName[ResourcePrefix.Length..];
                if (!trailing.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!trailing.EndsWith(suffix, StringComparison.Ordinal)) continue;

                var locale = trailing[prefix.Length..^suffix.Length];
                if (string.IsNullOrEmpty(locale)) continue;

                if (!_localeData.ContainsKey(locale))
                {
                    // Release the outer lock recursion path and call the loader.
                    // EnsureLocaleLoaded re-acquires the same lock; Monitor reentrance is
                    // permitted on the same thread, so this is safe.
                    EnsureLocaleLoaded(locale);
                }
            }

            _allLocalesDiscovered = true;
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
                // ToString() on non-null dictionary key cannot return null in practice;
                // coalesce satisfies compiler nullable analysis (will never execute).
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
                // coalesce satisfies compiler nullable analysis (will never execute).
                target[key] = kvp.Value.ToString() ?? string.Empty;
            }
        }
    }
}
