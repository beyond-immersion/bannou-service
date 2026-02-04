namespace BeyondImmersion.Bannou.StorylineTheory.Elements;

/// <summary>
/// Narrative phases based on Propp's morphological analysis.
/// These represent the major structural divisions of a narrative.
/// </summary>
public enum NarrativePhase
{
    /// <summary>
    /// Initial situation and setup (Propp functions 1-7).
    /// Establishes the world, characters, and initial state.
    /// </summary>
    Preparation = 0,

    /// <summary>
    /// Problem establishment (Propp functions 8-11).
    /// Introduces the villainy or lack that drives the story.
    /// </summary>
    Complication = 1,

    /// <summary>
    /// Helper acquisition (Propp functions 12-15).
    /// Hero gains magical agent or assistance.
    /// </summary>
    Donor = 2,

    /// <summary>
    /// Transition to confrontation (Propp function 16).
    /// Hero is transported to the location of conflict.
    /// </summary>
    Transference = 3,

    /// <summary>
    /// Direct conflict (Propp functions 17-19).
    /// Combat, victory, and resolution of the initial problem.
    /// </summary>
    Struggle = 4,

    /// <summary>
    /// Resolution and aftermath (Propp functions 20-31).
    /// Hero returns, is recognized, and receives reward.
    /// </summary>
    Return = 5
}

/// <summary>
/// Extension methods for NarrativePhase.
/// </summary>
public static class NarrativePhaseExtensions
{
    /// <summary>
    /// Gets the significance weight for a narrative phase.
    /// </summary>
    public static double GetSignificance(this NarrativePhase phase) => phase switch
    {
        NarrativePhase.Preparation => 0.4,
        NarrativePhase.Complication => 0.8,
        NarrativePhase.Donor => 0.6,
        NarrativePhase.Transference => 0.7,
        NarrativePhase.Struggle => 0.9,
        NarrativePhase.Return => 0.9,
        _ => 0.5
    };

    /// <summary>
    /// Parses a phase name to the enum value.
    /// </summary>
    public static NarrativePhase Parse(string phaseName) => phaseName.ToLowerInvariant() switch
    {
        "preparation" => NarrativePhase.Preparation,
        "complication" => NarrativePhase.Complication,
        "donor" => NarrativePhase.Donor,
        "transference" => NarrativePhase.Transference,
        "struggle" => NarrativePhase.Struggle,
        "return" => NarrativePhase.Return,
        _ => throw new ArgumentException($"Unknown phase: {phaseName}", nameof(phaseName))
    };

    /// <summary>
    /// Tries to parse a phase name.
    /// </summary>
    public static bool TryParse(string phaseName, out NarrativePhase phase)
    {
        try
        {
            phase = Parse(phaseName);
            return true;
        }
        catch (ArgumentException)
        {
            phase = default;
            return false;
        }
    }
}
