// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Spectrums;

/// <summary>
/// A single pole (stage) in a four-stage spectrum.
/// </summary>
public sealed class SpectrumPole
{
    /// <summary>
    /// The display label for this pole (e.g., "Life", "Death", "Damnation").
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// The numeric value for this pole on the spectrum scale.
    /// </summary>
    public required double Value { get; init; }

    /// <summary>
    /// A description of what this pole represents in narrative terms.
    /// </summary>
    public required string Description { get; init; }
}
