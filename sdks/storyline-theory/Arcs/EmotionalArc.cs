namespace BeyondImmersion.Bannou.StorylineTheory.Arcs;

/// <summary>
/// Emotional arc patterns based on Kurt Vonnegut's "Shapes of Stories"
/// and computational story analysis research.
///
/// Each arc defines a characteristic trajectory of fortune/valence
/// through a story's timeline.
/// </summary>
public static class EmotionalArcs
{
    /// <summary>
    /// Rags to Riches - steady rise from misfortune to happiness.
    /// Examples: Cinderella, Great Expectations, Jane Eyre.
    /// </summary>
    public static EmotionalArc RagsToRiches { get; } = new(
        Code: "rags_to_riches",
        Name: "Rags to Riches",
        Description: "Protagonist rises from low fortune to high fortune",
        Trajectory: new[] { 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9 },
        Archetype: ArcArchetype.Rise);

    /// <summary>
    /// Riches to Rags - steady decline from happiness to misfortune.
    /// Examples: Romeo and Juliet, The Great Gatsby, Oedipus Rex.
    /// </summary>
    public static EmotionalArc RichesToRags { get; } = new(
        Code: "riches_to_rags",
        Name: "Riches to Rags",
        Description: "Protagonist falls from high fortune to low fortune",
        Trajectory: new[] { 0.9, 0.8, 0.7, 0.6, 0.5, 0.4, 0.3, 0.2 },
        Archetype: ArcArchetype.Fall);

    /// <summary>
    /// Man in a Hole - fall then rise. Most common arc in fiction.
    /// Examples: The Hobbit, Finding Nemo, most action movies.
    /// </summary>
    public static EmotionalArc ManInHole { get; } = new(
        Code: "man_in_hole",
        Name: "Man in a Hole",
        Description: "Protagonist falls into trouble then climbs out better than before",
        Trajectory: new[] { 0.7, 0.5, 0.3, 0.2, 0.3, 0.5, 0.7, 0.9 },
        Archetype: ArcArchetype.FallRise);

    /// <summary>
    /// Icarus - rise then fall. Classic tragedy pattern.
    /// Examples: Macbeth, Breaking Bad, The Wolf of Wall Street.
    /// </summary>
    public static EmotionalArc Icarus { get; } = new(
        Code: "icarus",
        Name: "Icarus",
        Description: "Protagonist rises to great heights then falls",
        Trajectory: new[] { 0.3, 0.5, 0.7, 0.9, 0.8, 0.5, 0.3, 0.1 },
        Archetype: ArcArchetype.RiseFall);

    /// <summary>
    /// Cinderella - rise, fall, rise. Extended version of Man in Hole.
    /// Examples: Cinderella, Pretty Woman, The Pursuit of Happyness.
    /// </summary>
    public static EmotionalArc Cinderella { get; } = new(
        Code: "cinderella",
        Name: "Cinderella",
        Description: "Protagonist rises, falls near the end, then rises to triumph",
        Trajectory: new[] { 0.2, 0.4, 0.6, 0.8, 0.4, 0.3, 0.6, 0.95 },
        Archetype: ArcArchetype.RiseFallRise);

    /// <summary>
    /// Oedipus - fall, rise, fall. Double tragedy.
    /// Examples: Oedipus Rex, Hamlet, many noir films.
    /// </summary>
    public static EmotionalArc Oedipus { get; } = new(
        Code: "oedipus",
        Name: "Oedipus",
        Description: "Protagonist falls, briefly recovers, then falls further",
        Trajectory: new[] { 0.8, 0.5, 0.3, 0.5, 0.7, 0.5, 0.2, 0.1 },
        Archetype: ArcArchetype.FallRiseFall);

    /// <summary>
    /// All defined emotional arcs.
    /// </summary>
    public static IReadOnlyList<EmotionalArc> All { get; } = new List<EmotionalArc>
    {
        RagsToRiches, RichesToRags, ManInHole, Icarus, Cinderella, Oedipus
    }.AsReadOnly();

    /// <summary>
    /// Gets an arc by its code.
    /// </summary>
    public static EmotionalArc? GetByCode(string code) =>
        All.FirstOrDefault(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets arcs matching a specific archetype.
    /// </summary>
    public static IEnumerable<EmotionalArc> GetByArchetype(ArcArchetype archetype) =>
        All.Where(a => a.Archetype == archetype);

    /// <summary>
    /// Finds the best-matching arc for a given trajectory.
    /// Uses least squares distance to compare trajectories.
    /// </summary>
    /// <param name="actualTrajectory">The observed emotional trajectory (0-1 values).</param>
    /// <returns>The best-matching arc and its fit score (0-1, higher is better).</returns>
    public static (EmotionalArc Arc, double FitScore) FindBestMatch(IReadOnlyList<double> actualTrajectory)
    {
        if (actualTrajectory.Count == 0)
            return (ManInHole, 0.0); // Default

        EmotionalArc? bestArc = null;
        var bestDistance = double.MaxValue;

        foreach (var arc in All)
        {
            var distance = CalculateTrajectoryDistance(actualTrajectory, arc.Trajectory);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestArc = arc;
            }
        }

        // Convert distance to fit score (0-1, where 1 is perfect match)
        // Max possible distance is sqrt(n) where n is trajectory length
        var maxDistance = Math.Sqrt(actualTrajectory.Count);
        var fitScore = Math.Max(0, 1.0 - bestDistance / maxDistance);

        return (bestArc ?? ManInHole, fitScore);
    }

    /// <summary>
    /// Calculate RMSE between two trajectories, interpolating if lengths differ.
    /// </summary>
    private static double CalculateTrajectoryDistance(
        IReadOnlyList<double> actual,
        IReadOnlyList<double> reference)
    {
        var sumSquaredError = 0.0;
        var n = actual.Count;

        for (var i = 0; i < n; i++)
        {
            // Interpolate reference trajectory to match actual length
            var refPosition = (double)i / (n - 1) * (reference.Count - 1);
            var refIndex = (int)refPosition;
            var fraction = refPosition - refIndex;

            double refValue;
            if (refIndex >= reference.Count - 1)
            {
                refValue = reference[^1];
            }
            else
            {
                refValue = reference[refIndex] * (1 - fraction) + reference[refIndex + 1] * fraction;
            }

            var error = actual[i] - refValue;
            sumSquaredError += error * error;
        }

        return Math.Sqrt(sumSquaredError / n);
    }
}

/// <summary>
/// Represents an emotional arc pattern.
/// </summary>
/// <param name="Code">Unique identifier for the arc.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">Human-readable description.</param>
/// <param name="Trajectory">Normalized trajectory points (0-1), evenly spaced through story.</param>
/// <param name="Archetype">The basic shape category.</param>
public sealed record EmotionalArc(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<double> Trajectory,
    ArcArchetype Archetype)
{
    /// <summary>
    /// Gets the expected fortune value at a given story position.
    /// </summary>
    /// <param name="position">Position in the story (0-1).</param>
    /// <returns>Expected fortune value (0-1).</returns>
    public double GetFortuneAt(double position)
    {
        position = Math.Clamp(position, 0.0, 1.0);

        var scaledPosition = position * (Trajectory.Count - 1);
        var index = (int)scaledPosition;
        var fraction = scaledPosition - index;

        if (index >= Trajectory.Count - 1)
            return Trajectory[^1];

        return Trajectory[index] * (1 - fraction) + Trajectory[index + 1] * fraction;
    }

    /// <summary>
    /// Gets whether this arc ends positively (fortune greater than 0.5).
    /// </summary>
    public bool EndsPositive => Trajectory[^1] > 0.5;

    /// <summary>
    /// Gets whether this arc ends negatively (fortune less than 0.5).
    /// </summary>
    public bool EndsNegative => Trajectory[^1] < 0.5;

    /// <summary>
    /// Gets the overall range of the arc (max - min).
    /// </summary>
    public double Range => Trajectory.Max() - Trajectory.Min();
}

/// <summary>
/// Basic shape categories for emotional arcs.
/// </summary>
public enum ArcArchetype
{
    /// <summary>Steady rise from low to high.</summary>
    Rise,

    /// <summary>Steady fall from high to low.</summary>
    Fall,

    /// <summary>Fall then rise (most common).</summary>
    FallRise,

    /// <summary>Rise then fall (tragedy).</summary>
    RiseFall,

    /// <summary>Rise, fall, then rise again.</summary>
    RiseFallRise,

    /// <summary>Fall, rise, then fall again.</summary>
    FallRiseFall
}
