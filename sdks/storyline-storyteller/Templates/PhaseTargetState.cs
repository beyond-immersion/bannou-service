// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Target state for phase completion.
/// </summary>
public sealed class PhaseTargetState
{
    /// <summary>
    /// Minimum primary spectrum value for this phase.
    /// </summary>
    public required double MinPrimarySpectrum { get; init; }

    /// <summary>
    /// Maximum primary spectrum value for this phase.
    /// </summary>
    public required double MaxPrimarySpectrum { get; init; }

    /// <summary>
    /// Human-readable description of this state range.
    /// </summary>
    public string? RangeDescription { get; init; }
}
