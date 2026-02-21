// Copyright (c) Beyond Immersion. All rights reserved.

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Phase position constraints from Save the Cat beat timing.
/// </summary>
public sealed class PhasePosition
{
    /// <summary>
    /// Target position from STC beat timing.
    /// </summary>
    public required double StcCenter { get; init; }

    /// <summary>
    /// Earliest advancement position (prevents speed-running).
    /// </summary>
    public required double Floor { get; init; }

    /// <summary>
    /// Forced advancement position (prevents deadlock).
    /// </summary>
    public required double Ceiling { get; init; }

    /// <summary>
    /// Validation tolerance (±).
    /// </summary>
    public required double ValidationBand { get; init; }
}
