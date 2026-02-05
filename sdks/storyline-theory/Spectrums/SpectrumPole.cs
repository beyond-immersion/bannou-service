// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Spectrums;

/// <summary>
/// A single pole (stage) in a four-stage spectrum.
/// Story Grid spectrums have four stages: Positive, Contrary, Negative, and Negation of Negation.
/// </summary>
public sealed class SpectrumPole
{
    /// <summary>
    /// The display label for this pole (e.g., "Life", "Death", "Damnation").
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// The numeric value for this pole on the spectrum scale.
    /// Typically: Positive=1.0, Contrary=0.66, Negative=0.0 (or 0.33), Negation=-1.0.
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// A description of what this pole represents in narrative terms.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Optional examples of this pole in media.
    /// </summary>
    public string[]? Examples { get; init; }
}
