// Copyright (c) Beyond Immersion. All rights reserved.

using BeyondImmersion.Bannou.StorylineTheory.Arcs;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Full story template definition.
/// </summary>
public sealed class StoryTemplate
{
    /// <summary>
    /// The arc type this template is based on.
    /// </summary>
    public required ArcType ArcType { get; init; }

    /// <summary>
    /// Template code (e.g., "MAN_IN_HOLE").
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Whether the arc ends higher or lower than it starts.
    /// </summary>
    public required ArcDirection Direction { get; init; }

    /// <summary>
    /// Mathematical description of the arc shape.
    /// </summary>
    public required string MathematicalForm { get; init; }

    /// <summary>
    /// The phases in this template.
    /// </summary>
    public required StoryPhase[] Phases { get; init; }

    /// <summary>
    /// Genres compatible with this template.
    /// </summary>
    public required TemplateGenreCompatibility[] CompatibleGenres { get; init; }

    /// <summary>
    /// Default action count for this template.
    /// </summary>
    public required int DefaultActionCount { get; init; }

    /// <summary>
    /// Valid action count range (min, max).
    /// </summary>
    public required (int Min, int Max) ActionCountRange { get; init; }

    /// <summary>
    /// Get the phase for a given position.
    /// </summary>
    public StoryPhase GetPhaseAt(double position)
    {
        // Find the phase whose position range contains this position
        foreach (var phase in Phases)
        {
            if (position >= phase.Position.Floor && position < phase.Position.Ceiling)
            {
                return phase;
            }
        }

        // Return last phase if position is beyond all phases
        return Phases[^1];
    }

    /// <summary>
    /// Check genre compatibility.
    /// </summary>
    public bool IsCompatibleWith(string genre, string? subgenre)
    {
        foreach (var compat in CompatibleGenres)
        {
            if (!string.Equals(compat.Genre, genre, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // If no subgenres specified, all subgenres are compatible
            if (compat.Subgenres == null)
            {
                return true;
            }

            // If subgenre is null, check if any subgenre is compatible
            if (subgenre == null)
            {
                return true;
            }

            // Check if specific subgenre is compatible
            if (compat.Subgenres.Contains(subgenre, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
