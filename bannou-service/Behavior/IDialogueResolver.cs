// =============================================================================
// Dialogue Resolver Interface
// Resolves dialogue text through the three-step resolution pipeline.
// =============================================================================

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Resolves dialogue text through the three-step resolution pipeline:
/// 1. Check external file for condition overrides
/// 2. Check external file for localization
/// 3. Fall back to inline default
/// </summary>
/// <remarks>
/// <para>
/// Per FOUNDATION TENETS (G2 decision), every dialogue must have an inline
/// default. External references are optional and provide localization and
/// conditional overrides.
/// </para>
/// <para>
/// Example ABML usage:
/// <code>
/// - speak:
///     character: "${npc.id}"
///     text: "Welcome to my shop!"           # Inline default (required)
///     external: dialogue/merchant/greet     # External reference (optional)
/// </code>
/// </para>
/// </remarks>
public interface IDialogueResolver
{
    /// <summary>
    /// Resolves dialogue text using the three-step resolution pipeline.
    /// </summary>
    /// <param name="reference">The dialogue reference containing inline and optional external.</param>
    /// <param name="locale">The localization context.</param>
    /// <param name="context">Expression context for evaluating condition overrides.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved dialogue with source information.</returns>
    Task<ResolvedDialogue> ResolveAsync(
        DialogueReference reference,
        LocalizationContext locale,
        IDialogueExpressionContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves multiple dialogue options (for choice prompts).
    /// </summary>
    /// <param name="options">The dialogue options to resolve.</param>
    /// <param name="locale">The localization context.</param>
    /// <param name="context">Expression context for evaluating conditions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved options.</returns>
    Task<IReadOnlyList<ResolvedDialogueOption>> ResolveOptionsAsync(
        IReadOnlyList<DialogueOptionReference> options,
        LocalizationContext locale,
        IDialogueExpressionContext context,
        CancellationToken ct = default);
}

/// <summary>
/// Reference to dialogue text with inline default and optional external reference.
/// </summary>
public sealed class DialogueReference
{
    /// <summary>
    /// The inline default text (required).
    /// </summary>
    /// <remarks>
    /// Per G2 decision, inline text is REQUIRED for all dialogue.
    /// This ensures documents are playable without external files.
    /// </remarks>
    public required string InlineText { get; init; }

    /// <summary>
    /// Optional external reference path (e.g., "dialogue/merchant/greet").
    /// </summary>
    public string? ExternalRef { get; init; }

    /// <summary>
    /// Speaker identifier (character ID).
    /// </summary>
    public string? Speaker { get; init; }

    /// <summary>
    /// Emotion or tone for the dialogue.
    /// </summary>
    public string? Emotion { get; init; }

    /// <summary>
    /// Additional metadata for the dialogue.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }

    /// <summary>
    /// Creates a simple dialogue reference with just inline text.
    /// </summary>
    public static DialogueReference Inline(string text) => new() { InlineText = text };

    /// <summary>
    /// Creates a dialogue reference with inline text and external reference.
    /// </summary>
    public static DialogueReference WithExternal(string inlineText, string externalRef) =>
        new() { InlineText = inlineText, ExternalRef = externalRef };
}

/// <summary>
/// Reference to a dialogue option (for choice prompts).
/// </summary>
public sealed class DialogueOptionReference
{
    /// <summary>
    /// The option value (returned when selected).
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The inline default label text (required).
    /// </summary>
    public required string InlineLabel { get; init; }

    /// <summary>
    /// Optional inline description text.
    /// </summary>
    public string? InlineDescription { get; init; }

    /// <summary>
    /// Optional external reference for the label.
    /// </summary>
    public string? ExternalRef { get; init; }

    /// <summary>
    /// Condition for this option to be available.
    /// </summary>
    public string? Condition { get; init; }

    /// <summary>
    /// Whether this is the default option.
    /// </summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// Resolved dialogue text with source information.
/// </summary>
public sealed class ResolvedDialogue
{
    /// <summary>
    /// The final resolved text.
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// The speaker identifier.
    /// </summary>
    public string? Speaker { get; init; }

    /// <summary>
    /// The emotion or tone.
    /// </summary>
    public string? Emotion { get; init; }

    /// <summary>
    /// Where the text came from in the resolution chain.
    /// </summary>
    public DialogueSource Source { get; init; }

    /// <summary>
    /// If an override was used, which condition matched.
    /// </summary>
    public string? MatchedCondition { get; init; }

    /// <summary>
    /// The locale used for resolution.
    /// </summary>
    public string? ResolvedLocale { get; init; }

    /// <summary>
    /// Additional metadata.
    /// </summary>
    public IReadOnlyDictionary<string, object>? Metadata { get; init; }
}

/// <summary>
/// Resolved dialogue option.
/// </summary>
public sealed class ResolvedDialogueOption
{
    /// <summary>
    /// The option value (returned when selected).
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// The resolved label text.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// The resolved description text.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Whether this option is available (condition passed).
    /// </summary>
    public bool IsAvailable { get; init; } = true;

    /// <summary>
    /// Whether this is the default option.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Where the label text came from.
    /// </summary>
    public DialogueSource Source { get; init; }
}

/// <summary>
/// Source of the resolved dialogue text.
/// </summary>
public enum DialogueSource
{
    /// <summary>
    /// Used the inline default text.
    /// </summary>
    Inline,

    /// <summary>
    /// Used localized text from external file.
    /// </summary>
    Localization,

    /// <summary>
    /// Used conditional override from external file.
    /// </summary>
    Override
}

/// <summary>
/// Localization context for dialogue resolution.
/// </summary>
public sealed class LocalizationContext
{
    /// <summary>
    /// The primary locale (e.g., "en-US", "ja-JP").
    /// </summary>
    public required string Locale { get; init; }

    /// <summary>
    /// Fallback locales in order of preference.
    /// </summary>
    public IReadOnlyList<string> FallbackLocales { get; init; } = [];

    /// <summary>
    /// The default locale to use when all else fails.
    /// </summary>
    public string DefaultLocale { get; init; } = "en";

    /// <summary>
    /// Creates a context for the specified locale with standard fallback chain.
    /// </summary>
    /// <param name="locale">The primary locale (e.g., "en-US").</param>
    /// <returns>A localization context with fallback chain.</returns>
    public static LocalizationContext ForLocale(string locale)
    {
        var fallbacks = new List<string>();

        // Add language-only fallback (e.g., "en-US" -> "en")
        var dashIndex = locale.IndexOf('-');
        if (dashIndex > 0)
        {
            fallbacks.Add(locale[..dashIndex]);
        }

        return new LocalizationContext
        {
            Locale = locale,
            FallbackLocales = fallbacks
        };
    }

    /// <summary>
    /// Creates a context for English with no fallbacks.
    /// </summary>
    public static LocalizationContext English => new() { Locale = "en" };
}

/// <summary>
/// Expression context for evaluating dialogue conditions.
/// </summary>
/// <remarks>
/// This interface abstracts the expression evaluation so that
/// the dialogue resolver doesn't depend directly on ABML internals.
/// </remarks>
public interface IDialogueExpressionContext
{
    /// <summary>
    /// Evaluates a condition expression.
    /// </summary>
    /// <param name="condition">The condition expression (e.g., "${player.reputation > 50}").</param>
    /// <returns>True if the condition is satisfied.</returns>
    bool EvaluateCondition(string condition);

    /// <summary>
    /// Evaluates template variables in text.
    /// </summary>
    /// <param name="text">Text with template variables (e.g., "Hello {{ player.name }}").</param>
    /// <returns>Text with variables substituted.</returns>
    string EvaluateTemplate(string text);

    /// <summary>
    /// Gets a variable value from the context.
    /// </summary>
    /// <param name="name">Variable name.</param>
    /// <returns>The value, or null if not found.</returns>
    object? GetVariable(string name);
}

/// <summary>
/// Null implementation of expression context (always returns false/unchanged).
/// </summary>
public sealed class NullDialogueExpressionContext : IDialogueExpressionContext
{
    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static NullDialogueExpressionContext Instance { get; } = new();

    private NullDialogueExpressionContext() { }

    /// <inheritdoc/>
    public bool EvaluateCondition(string condition) => false;

    /// <inheritdoc/>
    public string EvaluateTemplate(string text) => text;

    /// <inheritdoc/>
    public object? GetVariable(string name) => null;
}
