namespace BeyondImmersion.Bannou.StorylineTheory.Elements;

/// <summary>
/// Vladimir Propp's 31 narrative functions from Morphology of the Folktale (1928).
/// Functions represent actions that advance the plot, occurring in a fixed sequential order.
/// </summary>
public static class ProppFunctions
{
    /// <summary>
    /// Gets the narrative significance of a function based on its phase.
    /// Higher significance indicates more essential story beats.
    /// </summary>
    /// <param name="phase">The narrative phase (preparation, complication, donor, transference, struggle, return).</param>
    /// <returns>Significance score (0.0-1.0).</returns>
    /// <remarks>
    /// Phase weights rationale:
    /// - Struggle/Return (0.9): Story climax and resolution - essential beats
    /// - Complication (0.8): Problem establishment - mandatory (Villainy or Lack required)
    /// - Transference (0.7): Bridge to conflict - transformation point
    /// - Donor (0.6): Helper acquisition - enables resolution
    /// - Preparation (0.4): Setup functions - often optional in shorter narratives
    /// </remarks>
    public static double GetPhaseSignificance(string phase) => phase.ToLowerInvariant() switch
    {
        "preparation" => 0.4,
        "complication" => 0.8,
        "donor" => 0.6,
        "transference" => 0.7,
        "struggle" => 0.9,
        "return" => 0.9,
        _ => 0.5
    };

    /// <summary>
    /// Gets the phase for a function by its code.
    /// </summary>
    /// <param name="code">The function code (e.g., "absentation", "villainy", "victory").</param>
    /// <returns>The phase name, or "unknown" if not found.</returns>
    public static string GetPhaseForFunction(string code) => code.ToLowerInvariant() switch
    {
        // Preparation phase (functions 1-7)
        "absentation" or "interdiction" or "violation" or
        "reconnaissance" or "delivery" or "trickery" or "complicity" => "preparation",

        // Complication phase (functions 8-11)
        "villainy" or "lack" or "mediation" or "counteraction" or "departure" => "complication",

        // Donor sequence (functions 12-15)
        "testing" or "reaction" or "acquisition" or "guidance" => "donor",

        // Transference (function 16)
        "transference" or "spatial_change" => "transference",

        // Struggle phase (functions 17-19)
        "combat" or "branding" or "victory" or "liquidation" => "struggle",

        // Return phase (functions 20-31)
        "return" or "pursuit" or "rescue" or "arrival" or "claim" or
        "task" or "solution" or "recognition" or "exposure" or
        "transfiguration" or "punishment" or "wedding" => "return",

        _ => "unknown"
    };

    /// <summary>
    /// Gets the significance for a function by its code.
    /// </summary>
    /// <param name="code">The function code.</param>
    /// <returns>Significance score (0.0-1.0).</returns>
    public static double GetFunctionSignificance(string code)
    {
        var phase = GetPhaseForFunction(code);
        return GetPhaseSignificance(phase);
    }

    /// <summary>
    /// Checks if a function is mandatory for a complete narrative.
    /// </summary>
    /// <param name="code">The function code.</param>
    /// <returns>True if the function is required for narrative completeness.</returns>
    /// <remarks>
    /// Per Propp, a tale must have either Villainy (A) or Lack (a) to initiate the complication.
    /// Liquidation (K) or its equivalent resolves the narrative problem.
    /// </remarks>
    public static bool IsMandatory(string code) => code.ToLowerInvariant() switch
    {
        "villainy" or "lack" => true,       // One of these must initiate the problem
        "liquidation" => true,               // Resolution of the initial problem
        _ => false
    };

    /// <summary>
    /// All 31 Propp functions with their symbols, names, and phases.
    /// </summary>
    public static IReadOnlyList<ProppFunction> All { get; } = new List<ProppFunction>
    {
        // Preparation (β, γ, δ, ε, ζ, η, θ)
        new("absentation", "β", "Absentation", "preparation",
            "A family member leaves home or dies"),
        new("interdiction", "γ", "Interdiction", "preparation",
            "A prohibition or command is given to the hero"),
        new("violation", "δ", "Violation", "preparation",
            "The interdiction is violated"),
        new("reconnaissance", "ε", "Reconnaissance", "preparation",
            "The villain makes an attempt to gather information"),
        new("delivery", "ζ", "Delivery", "preparation",
            "The villain receives information about the victim"),
        new("trickery", "η", "Trickery", "preparation",
            "The villain attempts to deceive the victim"),
        new("complicity", "θ", "Complicity", "preparation",
            "The victim is deceived and unwittingly helps the enemy"),

        // Complication (A/a, B, C, ↑)
        new("villainy", "A", "Villainy", "complication",
            "The villain causes harm or injury to a family member"),
        new("lack", "a", "Lack", "complication",
            "A family member lacks or desires something"),
        new("mediation", "B", "Mediation", "complication",
            "The hero is dispatched or learns of the misfortune"),
        new("counteraction", "C", "Counteraction", "complication",
            "The hero agrees to or decides upon counteraction"),
        new("departure", "↑", "Departure", "complication",
            "The hero leaves home"),

        // Donor Sequence (D, E, F, G)
        new("testing", "D", "Testing", "donor",
            "The hero is tested, interrogated, or attacked"),
        new("reaction", "E", "Reaction", "donor",
            "The hero reacts to the donor's test"),
        new("acquisition", "F", "Acquisition", "donor",
            "The hero acquires a magical agent"),
        new("guidance", "G", "Guidance", "donor",
            "The hero is transferred to the whereabouts of the object of search"),

        // Transference
        new("transference", "H", "Spatial Transference", "transference",
            "The hero is transferred, delivered, or led to the object of search"),

        // Struggle (I, J, K)
        new("combat", "I", "Combat", "struggle",
            "The hero and villain join in direct combat"),
        new("branding", "J", "Branding", "struggle",
            "The hero is branded or marked"),
        new("victory", "K", "Victory", "struggle",
            "The villain is defeated"),
        new("liquidation", "K", "Liquidation", "struggle",
            "The initial misfortune or lack is resolved"),

        // Return (↓, Pr, Rs, O, L, M, N, Q, Ex, T, U, W)
        new("return", "↓", "Return", "return",
            "The hero returns"),
        new("pursuit", "Pr", "Pursuit", "return",
            "The hero is pursued"),
        new("rescue", "Rs", "Rescue", "return",
            "The hero is rescued from pursuit"),
        new("arrival", "O", "Unrecognized Arrival", "return",
            "The hero arrives home or elsewhere unrecognized"),
        new("claim", "L", "Unfounded Claims", "return",
            "A false hero presents unfounded claims"),
        new("task", "M", "Difficult Task", "return",
            "A difficult task is proposed to the hero"),
        new("solution", "N", "Solution", "return",
            "The task is resolved"),
        new("recognition", "Q", "Recognition", "return",
            "The hero is recognized"),
        new("exposure", "Ex", "Exposure", "return",
            "The false hero or villain is exposed"),
        new("transfiguration", "T", "Transfiguration", "return",
            "The hero is given a new appearance"),
        new("punishment", "U", "Punishment", "return",
            "The villain is punished"),
        new("wedding", "W", "Wedding", "return",
            "The hero is married and ascends the throne"),
    }.AsReadOnly();

    /// <summary>
    /// Gets a function by its code.
    /// </summary>
    public static ProppFunction? GetByCode(string code) =>
        All.FirstOrDefault(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets a function by its symbol.
    /// </summary>
    public static ProppFunction? GetBySymbol(string symbol) =>
        All.FirstOrDefault(f => f.Symbol == symbol);

    /// <summary>
    /// Gets all functions in a specific phase.
    /// </summary>
    public static IEnumerable<ProppFunction> GetByPhase(string phase) =>
        All.Where(f => f.Phase.Equals(phase, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Represents a single Propp narrative function.
/// </summary>
/// <param name="Code">Unique lowercase identifier (e.g., "villainy", "departure").</param>
/// <param name="Symbol">Propp's original symbol (e.g., "A", "↑", "β").</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="Phase">Narrative phase (preparation, complication, donor, transference, struggle, return).</param>
/// <param name="Description">Brief description of the function's narrative role.</param>
public sealed record ProppFunction(
    string Code,
    string Symbol,
    string Name,
    string Phase,
    string Description)
{
    /// <summary>
    /// Gets the significance score for this function based on its phase.
    /// </summary>
    public double Significance => ProppFunctions.GetPhaseSignificance(Phase);

    /// <summary>
    /// Gets whether this function is mandatory for a complete narrative.
    /// </summary>
    public bool IsMandatory => ProppFunctions.IsMandatory(Code);
}
