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
    /// The primary spectrum for this genre.
    /// </summary>
    public required SpectrumType PrimarySpectrum { get; init; }

    /// <summary>
    /// The core event for this genre (e.g., "Hero at Mercy of Villain").
    /// </summary>
    public required string CoreEvent { get; init; }

    /// <summary>
    /// Optional override for core emotion (some genres sharing a spectrum have different emotions).
    /// </summary>
    public string? CoreEmotionOverride { get; init; }
}
