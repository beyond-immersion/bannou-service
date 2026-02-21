// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineStoryteller.Templates;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Planning;

/// <summary>
/// Resolves target action count using layered resolution (template → genre → override).
/// </summary>
public sealed class ActionCountResolver
{
    private static readonly Dictionary<string, double> GenreModifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["thriller"] = 0.8,      // Tighter pacing
        ["epic_fantasy"] = 1.5,  // More expansive
        ["romance"] = 1.0,       // Neutral
        ["horror"] = 0.9,        // Slightly tighter
        ["action"] = 0.85,       // Fast-paced
        ["mystery"] = 1.1,       // Room for clues
        ["drama"] = 1.2          // Character development
    };

    /// <summary>
    /// Resolves the target action count for a story.
    /// </summary>
    /// <param name="template">The story template.</param>
    /// <param name="genre">The genre.</param>
    /// <param name="requestOverride">Optional override from the request.</param>
    /// <returns>The resolved action count.</returns>
    public int ResolveTargetActionCount(StoryTemplate template, string genre, int? requestOverride)
    {
        // 1. Start with template default
        var baseCount = template.DefaultActionCount;

        // 2. Apply genre modifier
        var modifier = GenreModifiers.GetValueOrDefault(genre.ToLowerInvariant(), 1.0);
        var adjusted = (int)(baseCount * modifier);

        // 3. Apply request override if provided
        var target = requestOverride ?? adjusted;

        // 4. Clamp to template's valid range
        return Math.Clamp(target, template.ActionCountRange.Min, template.ActionCountRange.Max);
    }
}
