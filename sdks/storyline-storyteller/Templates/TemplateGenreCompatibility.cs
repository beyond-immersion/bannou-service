// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Genre compatibility entry for a template.
/// </summary>
public sealed class TemplateGenreCompatibility
{
    /// <summary>
    /// The genre code.
    /// </summary>
    public required string Genre { get; init; }

    /// <summary>
    /// Specific subgenres, or null for all subgenres.
    /// </summary>
    public string[]? Subgenres { get; init; }
}
