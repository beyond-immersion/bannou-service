// =============================================================================
// External Dialogue Loader Interface
// Loads external dialogue files containing localizations and overrides.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Loads external dialogue files containing localizations and conditional overrides.
/// </summary>
/// <remarks>
/// <para>
/// External dialogue files are YAML documents with the following structure:
/// <code>
/// # dialogue/merchant/greet.yaml
/// localizations:
///   en: "Welcome to my shop!"
///   es: "¡Bienvenido a mi tienda!"
///   ja: "いらっしゃいませ！"
///
/// overrides:
///   - condition: "${player.reputation > 50}"
///     text: "Ah, my favorite customer!"
///     priority: 10
///   - condition: "${time.hour >= 20}"
///     text: "We're about to close, but come in!"
///     priority: 5
/// </code>
/// </para>
/// <para>
/// Resolution order per G2 decision:
/// 1. Check overrides (highest priority matching condition wins)
/// 2. Check localizations (for current locale with fallback)
/// 3. Fall back to inline default (from ABML document)
/// </para>
/// </remarks>
public interface IExternalDialogueLoader
{
    /// <summary>
    /// Loads an external dialogue file by reference path.
    /// </summary>
    /// <param name="reference">The reference path (e.g., "dialogue/merchant/greet").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded dialogue file, or null if not found.</returns>
    Task<ExternalDialogueFile?> LoadAsync(string reference, CancellationToken ct = default);

    /// <summary>
    /// Checks if a dialogue file exists.
    /// </summary>
    /// <param name="reference">The reference path.</param>
    /// <returns>True if the file exists.</returns>
    bool Exists(string reference);

    /// <summary>
    /// Registers a base directory for dialogue files.
    /// </summary>
    /// <param name="directory">The directory path.</param>
    /// <param name="priority">Priority (higher = searched first).</param>
    void RegisterDirectory(string directory, int priority = 0);

    /// <summary>
    /// Clears the file cache.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Reloads a specific file (invalidates cache).
    /// </summary>
    /// <param name="reference">The reference path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<ExternalDialogueFile?> ReloadAsync(string reference, CancellationToken ct = default);

    /// <summary>
    /// Gets all registered directories.
    /// </summary>
    IReadOnlyList<DialogueDirectory> RegisteredDirectories { get; }
}

/// <summary>
/// A registered dialogue directory.
/// </summary>
public sealed class DialogueDirectory
{
    /// <summary>
    /// The directory path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Search priority (higher = searched first).
    /// </summary>
    public int Priority { get; init; }
}

/// <summary>
/// An external dialogue file containing localizations and overrides.
/// </summary>
public sealed class ExternalDialogueFile
{
    /// <summary>
    /// The reference path this file was loaded from.
    /// </summary>
    public required string Reference { get; init; }

    /// <summary>
    /// The full file path on disk.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Localized text by locale code.
    /// </summary>
    public IReadOnlyDictionary<string, string> Localizations { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Conditional overrides ordered by priority (highest first).
    /// </summary>
    public IReadOnlyList<DialogueOverride> Overrides { get; init; } = [];

    /// <summary>
    /// Optional metadata for the dialogue.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// When the file was last modified.
    /// </summary>
    public DateTime LastModified { get; init; }

    /// <summary>
    /// Gets localized text for a locale.
    /// </summary>
    /// <param name="locale">The locale code.</param>
    /// <returns>The localized text, or null if not found.</returns>
    public string? GetLocalization(string locale)
    {
        return Localizations.TryGetValue(locale, out var text) ? text : null;
    }

    /// <summary>
    /// Gets localized text with fallback chain.
    /// </summary>
    /// <param name="context">The localization context.</param>
    /// <returns>The localized text and locale found, or null.</returns>
    public (string text, string locale)? GetLocalizationWithFallback(LocalizationContext context)
    {
        // Try primary locale
        if (Localizations.TryGetValue(context.Locale, out var text))
        {
            return (text, context.Locale);
        }

        // Try fallback chain
        foreach (var fallback in context.FallbackLocales)
        {
            if (Localizations.TryGetValue(fallback, out text))
            {
                return (text, fallback);
            }
        }

        // Try default locale
        if (Localizations.TryGetValue(context.DefaultLocale, out text))
        {
            return (text, context.DefaultLocale);
        }

        return null;
    }
}

/// <summary>
/// A conditional override for dialogue text.
/// </summary>
public sealed class DialogueOverride
{
    /// <summary>
    /// The condition expression to evaluate.
    /// </summary>
    /// <remarks>
    /// Conditions use ABML expression syntax (e.g., "${player.reputation > 50}").
    /// The condition is evaluated against the current expression context.
    /// </remarks>
    public required string Condition { get; init; }

    /// <summary>
    /// The override text to use when condition matches.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// Priority for this override (higher wins when multiple match).
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Optional locale restriction (only applies to this locale).
    /// </summary>
    /// <remarks>
    /// If null, the override applies to all locales.
    /// If set, the override only applies when the target locale matches.
    /// </remarks>
    public string? Locale { get; init; }

    /// <summary>
    /// Optional metadata for this override.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Options for loading external dialogue files.
/// </summary>
public sealed class ExternalDialogueLoaderOptions
{
    /// <summary>
    /// Base directories to search for dialogue files.
    /// </summary>
    public IReadOnlyList<string> BaseDirectories { get; init; } = [];

    /// <summary>
    /// File extensions to search for (default: .yaml, .yml).
    /// </summary>
    public IReadOnlyList<string> FileExtensions { get; init; } = [".yaml", ".yml"];

    /// <summary>
    /// Whether to cache loaded files.
    /// </summary>
    public bool EnableCaching { get; init; } = true;

    /// <summary>
    /// Cache expiration time.
    /// </summary>
    public TimeSpan CacheExpiration { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether to watch for file changes and auto-reload.
    /// </summary>
    public bool EnableFileWatching { get; init; }

    /// <summary>
    /// Whether to log when files are loaded.
    /// </summary>
    public bool LogFileLoads { get; init; }
}
