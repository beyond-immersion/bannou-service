// =============================================================================
// Localization Provider Interface
// Provides language-specific text lookup for dialogue localization.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Provides language-specific text lookup for dialogue localization.
/// </summary>
/// <remarks>
/// <para>
/// The localization provider manages text strings across multiple locales.
/// It supports:
/// </para>
/// <list type="bullet">
/// <item>Multiple locale fallback chains (e.g., "en-US" → "en" → default)</item>
/// <item>Hot-reloading of localization files in development</item>
/// <item>Namespace-based key organization</item>
/// </list>
/// </remarks>
public interface ILocalizationProvider
{
    /// <summary>
    /// Gets localized text for a key and locale.
    /// </summary>
    /// <param name="key">The localization key (e.g., "dialogue/merchant/greet").</param>
    /// <param name="locale">The target locale (e.g., "en-US").</param>
    /// <returns>The localized text, or null if not found.</returns>
    string? GetText(string key, string locale);

    /// <summary>
    /// Gets localized text using a fallback chain.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <param name="context">The localization context with fallback chain.</param>
    /// <returns>The localized text and the locale it was found in, or null.</returns>
    LocalizedText? GetTextWithFallback(string key, LocalizationContext context);

    /// <summary>
    /// Checks if a key exists for a locale.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <param name="locale">The target locale.</param>
    /// <returns>True if the key exists for the locale.</returns>
    bool HasKey(string key, string locale);

    /// <summary>
    /// Gets all supported locales.
    /// </summary>
    IReadOnlyCollection<string> SupportedLocales { get; }

    /// <summary>
    /// Gets the default locale.
    /// </summary>
    string DefaultLocale { get; }

    /// <summary>
    /// Reloads localization data (for development hot-reload).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ReloadAsync(CancellationToken ct = default);

    /// <summary>
    /// Registers a localization source.
    /// </summary>
    /// <param name="source">The source to register.</param>
    void RegisterSource(ILocalizationSource source);
}

/// <summary>
/// Localized text with metadata about where it was found.
/// </summary>
public sealed class LocalizedText
{
    /// <summary>
    /// The localized text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// The locale where the text was found.
    /// </summary>
    public required string FoundLocale { get; init; }

    /// <summary>
    /// Whether a fallback was used.
    /// </summary>
    public bool IsFallback { get; init; }

    /// <summary>
    /// The source that provided the text.
    /// </summary>
    public string? SourceName { get; init; }
}

/// <summary>
/// A source of localization data.
/// </summary>
public interface ILocalizationSource
{
    /// <summary>
    /// Unique name for this source.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Priority for this source (higher = checked first).
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Gets localized text from this source.
    /// </summary>
    /// <param name="key">The localization key.</param>
    /// <param name="locale">The target locale.</param>
    /// <returns>The localized text, or null if not found.</returns>
    string? GetText(string key, string locale);

    /// <summary>
    /// Gets all locales supported by this source.
    /// </summary>
    IReadOnlyCollection<string> SupportedLocales { get; }

    /// <summary>
    /// Reloads data from this source.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ReloadAsync(CancellationToken ct = default);
}

/// <summary>
/// Aggregate localization provider that delegates to multiple sources.
/// </summary>
public interface IAggregateLocalizationProvider : ILocalizationProvider
{
    /// <summary>
    /// Gets all registered sources ordered by priority.
    /// </summary>
    IReadOnlyList<ILocalizationSource> Sources { get; }

    /// <summary>
    /// Removes a source by name.
    /// </summary>
    /// <param name="name">The source name.</param>
    /// <returns>True if removed.</returns>
    bool RemoveSource(string name);
}

/// <summary>
/// Configuration for the localization system.
/// </summary>
public sealed class LocalizationConfiguration
{
    /// <summary>
    /// The default locale when no locale is specified.
    /// </summary>
    public string DefaultLocale { get; init; } = "en";

    /// <summary>
    /// Whether to enable hot-reload of localization files.
    /// </summary>
    public bool EnableHotReload { get; init; }

    /// <summary>
    /// File patterns to watch for hot-reload (glob patterns).
    /// </summary>
    public IReadOnlyList<string> HotReloadPatterns { get; init; } = ["*.yaml", "*.yml", "*.json"];

    /// <summary>
    /// Cache duration for localization lookups.
    /// </summary>
    public TimeSpan CacheDuration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to log missing keys.
    /// </summary>
    public bool LogMissingKeys { get; init; } = true;
}
