// =============================================================================
// Dialogue Resolver
// Resolves dialogue text through the three-step resolution pipeline.
// =============================================================================

using BeyondImmersion.BannouService.Behavior;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.Bannou.Behavior.Dialogue;

/// <summary>
/// Default implementation of dialogue resolution.
/// </summary>
/// <remarks>
/// <para>
/// Implements the three-step resolution pipeline per G2 decision:
/// </para>
/// <list type="number">
/// <item>Check external file for condition overrides (highest priority match wins)</item>
/// <item>Check external file for localization (with fallback chain)</item>
/// <item>Fall back to inline default</item>
/// </list>
/// <para>
/// After resolution, template variables are evaluated in the final text.
/// </para>
/// </remarks>
public sealed class DialogueResolver : IDialogueResolver
{
    private readonly IExternalDialogueLoader _loader;
    private readonly ILogger<DialogueResolver>? _logger;

    /// <summary>
    /// Creates a new dialogue resolver.
    /// </summary>
    /// <param name="loader">External dialogue file loader.</param>
    /// <param name="logger">Optional logger.</param>
    public DialogueResolver(
        IExternalDialogueLoader loader,
        ILogger<DialogueResolver>? logger = null)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ResolvedDialogue> ResolveAsync(
        DialogueReference reference,
        LocalizationContext locale,
        IDialogueExpressionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(context);

        // If no external reference, use inline directly
        if (string.IsNullOrEmpty(reference.ExternalRef))
        {
            return CreateInlineResult(reference, context);
        }

        // Load external file
        var externalFile = await _loader.LoadAsync(reference.ExternalRef, ct);
        if (externalFile == null)
        {
            _logger?.LogDebug(
                "External dialogue file not found: {Reference}, using inline default",
                reference.ExternalRef);
            return CreateInlineResult(reference, context);
        }

        // Step 1: Check for matching override
        var overrideResult = TryResolveOverride(externalFile, locale, context);
        if (overrideResult != null)
        {
            _logger?.LogDebug(
                "Dialogue resolved via override: {Reference}, condition: {Condition}",
                reference.ExternalRef,
                overrideResult.MatchedCondition);
            return overrideResult;
        }

        // Step 2: Check for localization
        var localizationResult = TryResolveLocalization(externalFile, locale, context, reference);
        if (localizationResult != null)
        {
            _logger?.LogDebug(
                "Dialogue resolved via localization: {Reference}, locale: {Locale}",
                reference.ExternalRef,
                localizationResult.ResolvedLocale);
            return localizationResult;
        }

        // Step 3: Fall back to inline default
        _logger?.LogDebug(
            "Dialogue resolved via inline default: {Reference}",
            reference.ExternalRef);
        return CreateInlineResult(reference, context);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResolvedDialogueOption>> ResolveOptionsAsync(
        IReadOnlyList<DialogueOptionReference> options,
        LocalizationContext locale,
        IDialogueExpressionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(locale);
        ArgumentNullException.ThrowIfNull(context);

        var results = new List<ResolvedDialogueOption>(options.Count);

        foreach (var option in options)
        {
            var resolved = await ResolveOptionAsync(option, locale, context, ct);
            results.Add(resolved);
        }

        return results;
    }

    private async Task<ResolvedDialogueOption> ResolveOptionAsync(
        DialogueOptionReference option,
        LocalizationContext locale,
        IDialogueExpressionContext context,
        CancellationToken ct)
    {
        // Evaluate availability condition
        var isAvailable = string.IsNullOrEmpty(option.Condition) ||
                        context.EvaluateCondition(option.Condition);

        // Resolve label text
        var labelReference = new DialogueReference
        {
            InlineText = option.InlineLabel,
            ExternalRef = option.ExternalRef
        };

        var resolvedLabel = await ResolveAsync(labelReference, locale, context, ct);

        // Resolve description if present
        string? resolvedDescription = null;
        if (!string.IsNullOrEmpty(option.InlineDescription))
        {
            resolvedDescription = context.EvaluateTemplate(option.InlineDescription);
        }

        return new ResolvedDialogueOption
        {
            Value = option.Value,
            Label = resolvedLabel.Text,
            Description = resolvedDescription,
            IsAvailable = isAvailable,
            IsDefault = option.IsDefault,
            Source = resolvedLabel.Source
        };
    }

    private ResolvedDialogue? TryResolveOverride(
        ExternalDialogueFile file,
        LocalizationContext locale,
        IDialogueExpressionContext context)
    {
        // Sort overrides by priority (highest first) defensively
        // even though ExternalDialogueLoader should pre-sort them
        var sortedOverrides = file.Overrides.OrderByDescending(o => o.Priority);

        foreach (var @override in sortedOverrides)
        {
            // Check locale restriction
            if (!string.IsNullOrEmpty(@override.Locale) &&
                !@override.Locale.Equals(locale.Locale, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Evaluate condition
            try
            {
                if (context.EvaluateCondition(@override.Condition))
                {
                    var text = context.EvaluateTemplate(@override.Text);
                    return new ResolvedDialogue
                    {
                        Text = text,
                        Source = DialogueSource.Override,
                        MatchedCondition = @override.Condition,
                        ResolvedLocale = @override.Locale ?? locale.Locale,
                        Metadata = @override.Metadata
                    };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "Failed to evaluate dialogue override condition: {Condition}",
                    @override.Condition);
            }
        }

        return null;
    }

    private ResolvedDialogue? TryResolveLocalization(
        ExternalDialogueFile file,
        LocalizationContext locale,
        IDialogueExpressionContext context,
        DialogueReference reference)
    {
        var localization = file.GetLocalizationWithFallback(locale);
        if (localization == null)
        {
            return null;
        }

        var (text, foundLocale) = localization.Value;
        var evaluatedText = context.EvaluateTemplate(text);

        return new ResolvedDialogue
        {
            Text = evaluatedText,
            Speaker = reference.Speaker,
            Emotion = reference.Emotion,
            Source = DialogueSource.Localization,
            ResolvedLocale = foundLocale,
            Metadata = reference.Metadata
        };
    }

    private static ResolvedDialogue CreateInlineResult(
        DialogueReference reference,
        IDialogueExpressionContext context)
    {
        var text = context.EvaluateTemplate(reference.InlineText);

        return new ResolvedDialogue
        {
            Text = text,
            Speaker = reference.Speaker,
            Emotion = reference.Emotion,
            Source = DialogueSource.Inline,
            Metadata = reference.Metadata
        };
    }
}
