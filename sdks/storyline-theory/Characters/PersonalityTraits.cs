namespace BeyondImmersion.Bannou.StorylineTheory.Characters;

/// <summary>
/// Personality trait axes for character behavior modeling.
/// Based on the Five Factor Model (Big Five) with extensions for
/// narrative-relevant dimensions.
///
/// All traits are bipolar axes from -1.0 to +1.0.
/// </summary>
public static class PersonalityTraits
{
    /// <summary>
    /// Confrontational vs Pacifist axis.
    /// -1.0 = Avoids conflict at all costs.
    /// +1.0 = Seeks confrontation.
    /// </summary>
    public static PersonalityTrait Confrontational { get; } = new(
        Code: "confrontational",
        Name: "Confrontational",
        NegativePole: "Pacifist",
        PositivePole: "Confrontational",
        Description: "Tendency to engage in or avoid conflict",
        BehaviorImpact: "Affects response to threats, conflict escalation, and diplomacy choices");

    /// <summary>
    /// Trusting vs Suspicious axis.
    /// -1.0 = Deeply suspicious of everyone.
    /// +1.0 = Trusts easily, perhaps naively.
    /// </summary>
    public static PersonalityTrait Trusting { get; } = new(
        Code: "trusting",
        Name: "Trusting",
        NegativePole: "Suspicious",
        PositivePole: "Trusting",
        Description: "Default assumption about others' intentions",
        BehaviorImpact: "Affects alliance formation, information sharing, and betrayal risk assessment");

    /// <summary>
    /// Reckless vs Cautious axis.
    /// -1.0 = Extremely cautious, risk-averse.
    /// +1.0 = Reckless, thrill-seeking.
    /// </summary>
    public static PersonalityTrait Reckless { get; } = new(
        Code: "reckless",
        Name: "Reckless",
        NegativePole: "Cautious",
        PositivePole: "Reckless",
        Description: "Willingness to take risks",
        BehaviorImpact: "Affects exploration, combat engagement, and resource gambling");

    /// <summary>
    /// Loyal vs Self-Serving axis.
    /// -1.0 = Completely self-interested.
    /// +1.0 = Intensely loyal to group/cause.
    /// </summary>
    public static PersonalityTrait Loyal { get; } = new(
        Code: "loyal",
        Name: "Loyal",
        NegativePole: "Self-Serving",
        PositivePole: "Loyal",
        Description: "Commitment to others vs self-interest",
        BehaviorImpact: "Affects group decisions, sacrifice willingness, and betrayal probability");

    /// <summary>
    /// Curious vs Incurious axis.
    /// -1.0 = Disinterested in learning.
    /// +1.0 = Intensely curious about everything.
    /// </summary>
    public static PersonalityTrait Curious { get; } = new(
        Code: "curious",
        Name: "Curious",
        NegativePole: "Incurious",
        PositivePole: "Curious",
        Description: "Drive to explore and learn",
        BehaviorImpact: "Affects exploration, investigation, and knowledge-seeking behaviors");

    /// <summary>
    /// Merciful vs Ruthless axis.
    /// -1.0 = Completely ruthless.
    /// +1.0 = Always shows mercy.
    /// </summary>
    public static PersonalityTrait Merciful { get; } = new(
        Code: "merciful",
        Name: "Merciful",
        NegativePole: "Ruthless",
        PositivePole: "Merciful",
        Description: "Tendency to show or withhold mercy",
        BehaviorImpact: "Affects combat finishing, prisoner treatment, and forgiveness");

    /// <summary>
    /// Ambitious vs Content axis.
    /// -1.0 = Content with current status.
    /// +1.0 = Intensely driven to advance.
    /// </summary>
    public static PersonalityTrait Ambitious { get; } = new(
        Code: "ambitious",
        Name: "Ambitious",
        NegativePole: "Content",
        PositivePole: "Ambitious",
        Description: "Drive for status and power",
        BehaviorImpact: "Affects goal selection, competition, and risk/reward calculations");

    /// <summary>
    /// Stoic vs Expressive axis.
    /// -1.0 = Highly expressive emotionally.
    /// +1.0 = Stoic, hides emotions.
    /// </summary>
    public static PersonalityTrait Stoic { get; } = new(
        Code: "stoic",
        Name: "Stoic",
        NegativePole: "Expressive",
        PositivePole: "Stoic",
        Description: "Emotional expression tendencies",
        BehaviorImpact: "Affects dialogue style, reaction visibility, and intimidation");

    /// <summary>
    /// All defined personality traits.
    /// </summary>
    public static IReadOnlyList<PersonalityTrait> All { get; } = new List<PersonalityTrait>
    {
        Confrontational, Trusting, Reckless, Loyal, Curious, Merciful, Ambitious, Stoic
    }.AsReadOnly();

    /// <summary>
    /// Gets a trait by its code.
    /// </summary>
    public static PersonalityTrait? GetByCode(string code) =>
        All.FirstOrDefault(t => t.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the descriptive label for a trait value.
    /// </summary>
    /// <param name="trait">The trait to describe.</param>
    /// <param name="value">The trait value (-1 to +1).</param>
    /// <returns>Human-readable description of this trait level.</returns>
    public static string GetLabel(PersonalityTrait trait, double value)
    {
        return value switch
        {
            <= -0.7 => $"Extremely {trait.NegativePole}",
            <= -0.3 => $"Moderately {trait.NegativePole}",
            <= 0.3 => "Balanced",
            <= 0.7 => $"Moderately {trait.PositivePole}",
            _ => $"Extremely {trait.PositivePole}"
        };
    }
}

/// <summary>
/// Represents a personality trait axis.
/// </summary>
/// <param name="Code">Unique identifier for the trait.</param>
/// <param name="Name">Display name (typically the positive pole).</param>
/// <param name="NegativePole">Label for -1.0 end of axis.</param>
/// <param name="PositivePole">Label for +1.0 end of axis.</param>
/// <param name="Description">What this trait measures.</param>
/// <param name="BehaviorImpact">How this trait affects NPC behavior.</param>
public sealed record PersonalityTrait(
    string Code,
    string Name,
    string NegativePole,
    string PositivePole,
    string Description,
    string BehaviorImpact)
{
    /// <summary>
    /// Gets the label for a specific value on this trait axis.
    /// </summary>
    public string GetLabel(double value) => PersonalityTraits.GetLabel(this, value);

    /// <summary>
    /// Gets which pole a value leans toward.
    /// </summary>
    public string GetPolarity(double value) => value switch
    {
        < -0.1 => NegativePole,
        > 0.1 => PositivePole,
        _ => "Neutral"
    };
}

/// <summary>
/// Common personality archetypes composed of trait combinations.
/// </summary>
public static class PersonalityArchetypes
{
    /// <summary>
    /// The Hero - brave, loyal, merciful.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Hero { get; } = new Dictionary<string, double>
    {
        ["confrontational"] = 0.3,
        ["trusting"] = 0.4,
        ["reckless"] = 0.2,
        ["loyal"] = 0.8,
        ["curious"] = 0.3,
        ["merciful"] = 0.6,
        ["ambitious"] = 0.4,
        ["stoic"] = 0.2
    };

    /// <summary>
    /// The Villain - ruthless, ambitious, suspicious.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Villain { get; } = new Dictionary<string, double>
    {
        ["confrontational"] = 0.7,
        ["trusting"] = -0.6,
        ["reckless"] = 0.1,
        ["loyal"] = -0.5,
        ["curious"] = 0.3,
        ["merciful"] = -0.8,
        ["ambitious"] = 0.9,
        ["stoic"] = 0.4
    };

    /// <summary>
    /// The Mentor - wise, cautious, loyal.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Mentor { get; } = new Dictionary<string, double>
    {
        ["confrontational"] = -0.3,
        ["trusting"] = 0.2,
        ["reckless"] = -0.6,
        ["loyal"] = 0.6,
        ["curious"] = 0.7,
        ["merciful"] = 0.4,
        ["ambitious"] = -0.2,
        ["stoic"] = 0.5
    };

    /// <summary>
    /// The Trickster - curious, reckless, self-serving.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Trickster { get; } = new Dictionary<string, double>
    {
        ["confrontational"] = 0.0,
        ["trusting"] = -0.2,
        ["reckless"] = 0.5,
        ["loyal"] = -0.3,
        ["curious"] = 0.8,
        ["merciful"] = 0.1,
        ["ambitious"] = 0.4,
        ["stoic"] = -0.5
    };

    /// <summary>
    /// The Guardian - loyal, cautious, stoic.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Guardian { get; } = new Dictionary<string, double>
    {
        ["confrontational"] = 0.4,
        ["trusting"] = 0.1,
        ["reckless"] = -0.7,
        ["loyal"] = 0.9,
        ["curious"] = -0.2,
        ["merciful"] = 0.3,
        ["ambitious"] = -0.1,
        ["stoic"] = 0.7
    };

    /// <summary>
    /// All defined archetypes.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> All { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, double>>
        {
            ["hero"] = Hero,
            ["villain"] = Villain,
            ["mentor"] = Mentor,
            ["trickster"] = Trickster,
            ["guardian"] = Guardian
        };

    /// <summary>
    /// Finds the closest archetype to a given trait set.
    /// </summary>
    /// <param name="traits">Trait values keyed by trait code.</param>
    /// <returns>Archetype name and similarity score (0-1).</returns>
    public static (string Archetype, double Similarity) FindClosest(IReadOnlyDictionary<string, double> traits)
    {
        string? bestArchetype = null;
        var bestSimilarity = double.MinValue;

        foreach (var (name, archetype) in All)
        {
            var similarity = CalculateSimilarity(traits, archetype);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestArchetype = name;
            }
        }

        // Normalize similarity to 0-1 range
        var normalizedSimilarity = (bestSimilarity + 1) / 2;
        return (bestArchetype ?? "hero", Math.Clamp(normalizedSimilarity, 0, 1));
    }

    /// <summary>
    /// Calculate cosine similarity between trait sets.
    /// </summary>
    private static double CalculateSimilarity(
        IReadOnlyDictionary<string, double> a,
        IReadOnlyDictionary<string, double> b)
    {
        var dotProduct = 0.0;
        var magnitudeA = 0.0;
        var magnitudeB = 0.0;

        foreach (var trait in PersonalityTraits.All)
        {
            var valueA = a.TryGetValue(trait.Code, out var va) ? va : 0.0;
            var valueB = b.TryGetValue(trait.Code, out var vb) ? vb : 0.0;

            dotProduct += valueA * valueB;
            magnitudeA += valueA * valueA;
            magnitudeB += valueB * valueB;
        }

        if (magnitudeA == 0 || magnitudeB == 0)
            return 0.0;

        return dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));
    }
}
