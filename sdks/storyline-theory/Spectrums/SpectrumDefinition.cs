// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Spectrums;

/// <summary>
/// A complete spectrum definition loaded from narrative-state.yaml.
/// </summary>
public sealed class SpectrumDefinition
{
    /// <summary>
    /// The spectrum type identifier.
    /// </summary>
    public required SpectrumType Type { get; init; }

    /// <summary>
    /// The positive pole label (e.g., "Life").
    /// </summary>
    public required string PositiveLabel { get; init; }

    /// <summary>
    /// The negative pole label (e.g., "Death").
    /// </summary>
    public required string NegativeLabel { get; init; }

    /// <summary>
    /// The four stages of the spectrum.
    /// </summary>
    public required SpectrumPole[] Stages { get; init; }

    /// <summary>
    /// Media examples for this spectrum.
    /// </summary>
    public required string[] MediaExamples { get; init; }
}
