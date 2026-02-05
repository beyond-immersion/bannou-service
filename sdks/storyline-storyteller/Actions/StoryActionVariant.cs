// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Genre-specific variant of an action.
/// </summary>
public sealed class StoryActionVariant
{
    /// <summary>
    /// Genres this variant applies to.
    /// </summary>
    public required string[] Genres { get; init; }

    /// <summary>
    /// Override description for these genres.
    /// </summary>
    public string? DescriptionOverride { get; init; }

    /// <summary>
    /// Override narrative effect for these genres.
    /// </summary>
    public NarrativeEffect? NarrativeEffectOverride { get; init; }
}
