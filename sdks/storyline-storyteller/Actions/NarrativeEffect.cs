// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Narrative effect on the emotional arc.
/// </summary>
public sealed class NarrativeEffect
{
    /// <summary>
    /// Delta to apply to the primary spectrum (e.g., -0.4).
    /// </summary>
    public double? PrimarySpectrumDelta { get; init; }

    /// <summary>
    /// Delta to apply to the secondary spectrum.
    /// </summary>
    public double? SecondarySpectrumDelta { get; init; }

    /// <summary>
    /// Position advance type: "micro", "standard", or "macro".
    /// </summary>
    public string? PositionAdvance { get; init; }
}
