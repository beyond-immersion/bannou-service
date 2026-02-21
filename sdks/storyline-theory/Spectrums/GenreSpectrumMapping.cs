// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineTheory.Spectrums;

/// <summary>
/// Maps a genre to its primary and optional secondary spectrum.
/// </summary>
public sealed class GenreSpectrumMapping
{
    /// <summary>
    /// The genre code (e.g., "action", "crime", "love").
    /// </summary>
    public required string Genre { get; init; }

    /// <summary>
    /// The optional subgenre.
    /// </summary>
    public required string? Subgenre { get; init; }

    /// <summary>
    /// The primary spectrum for this genre.
    /// </summary>
    public required SpectrumType PrimarySpectrum { get; init; }

    /// <summary>
    /// The optional secondary spectrum for this genre.
    /// </summary>
    public required SpectrumType? SecondarySpectrum { get; init; }
}
