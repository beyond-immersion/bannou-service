namespace BeyondImmersion.Bannou.StorylineTheory.Structure;

/// <summary>
/// Blake Snyder's Save the Cat beat sheet with timing percentages.
/// Based on "Save the Cat!" (2005) and "Save the Cat! Writes a Novel" (2018).
/// </summary>
public static class SaveTheCatBeats
{
    /// <summary>
    /// Opening Image - Visual snapshot of the hero's "before" world.
    /// </summary>
    public static SaveTheCatBeat OpeningImage { get; } = new(
        Code: "opening_image",
        Name: "Opening Image",
        PositionIndex: 0,
        PercentageBs2: 0.00,
        PercentageFiction: 0.00,
        Tolerance: 0.01,
        Importance: 1.0,
        Description: "Visual snapshot of the hero's world before the journey begins",
        Act: 1);

    /// <summary>
    /// Theme Stated - The story's deeper truth is articulated.
    /// </summary>
    public static SaveTheCatBeat ThemeStated { get; } = new(
        Code: "theme_stated",
        Name: "Theme Stated",
        PositionIndex: 1,
        PercentageBs2: 0.05,
        PercentageFiction: 0.03,
        Tolerance: 0.02,
        Importance: 0.6,
        Description: "Someone states the theme, often to the protagonist who doesn't yet understand it",
        Act: 1);

    /// <summary>
    /// Set-Up - World, flaws, and supporting cast established.
    /// </summary>
    public static SaveTheCatBeat SetUp { get; } = new(
        Code: "setup",
        Name: "Set-Up",
        PositionIndex: 2,
        PercentageBs2: 0.01,
        PercentageFiction: 0.01,
        Tolerance: 0.05,
        Importance: 0.8,
        Description: "Establishes the hero's world, flaws, and what's missing from their life",
        Act: 1,
        IsRange: true,
        RangeEnd: 0.10);

    /// <summary>
    /// Catalyst - The inciting incident that disrupts the status quo.
    /// </summary>
    public static SaveTheCatBeat Catalyst { get; } = new(
        Code: "catalyst",
        Name: "Catalyst",
        PositionIndex: 3,
        PercentageBs2: 0.10,
        PercentageFiction: 0.10,
        Tolerance: 0.02,
        Importance: 1.0,
        Description: "Life-changing event that knocks the hero out of their comfort zone",
        Act: 1);

    /// <summary>
    /// Debate - Hero questions whether to take the journey.
    /// </summary>
    public static SaveTheCatBeat Debate { get; } = new(
        Code: "debate",
        Name: "Debate",
        PositionIndex: 4,
        PercentageBs2: 0.10,
        PercentageFiction: 0.10,
        Tolerance: 0.05,
        Importance: 0.7,
        Description: "Hero hesitates, wrestling with doubt, fear, or resistance",
        Act: 1,
        IsRange: true,
        RangeEnd: 0.20);

    /// <summary>
    /// Break into Two - Hero commits to the journey.
    /// </summary>
    public static SaveTheCatBeat BreakIntoTwo { get; } = new(
        Code: "break_into_two",
        Name: "Break into Two",
        PositionIndex: 5,
        PercentageBs2: 0.20,
        PercentageFiction: 0.20,
        Tolerance: 0.02,
        Importance: 1.0,
        Description: "Hero chooses to enter the 'upside-down world' of Act Two",
        Act: 2);

    /// <summary>
    /// B Story - Secondary story begins (often a love story).
    /// </summary>
    public static SaveTheCatBeat BStory { get; } = new(
        Code: "b_story",
        Name: "B Story",
        PositionIndex: 6,
        PercentageBs2: 0.22,
        PercentageFiction: 0.22,
        Tolerance: 0.03,
        Importance: 0.7,
        Description: "Secondary story introduces a character who helps the hero learn the theme",
        Act: 2);

    /// <summary>
    /// Fun and Games - The "promise of the premise" is delivered.
    /// </summary>
    public static SaveTheCatBeat FunAndGames { get; } = new(
        Code: "fun_and_games",
        Name: "Fun and Games",
        PositionIndex: 7,
        PercentageBs2: 0.20,
        PercentageFiction: 0.20,
        Tolerance: 0.10,
        Importance: 0.8,
        Description: "The heart of the movie - the promise of the premise is delivered",
        Act: 2,
        IsRange: true,
        RangeEnd: 0.50);

    /// <summary>
    /// Midpoint - Major shift with false victory or false defeat.
    /// </summary>
    public static SaveTheCatBeat Midpoint { get; } = new(
        Code: "midpoint",
        Name: "Midpoint",
        PositionIndex: 8,
        PercentageBs2: 0.50,
        PercentageFiction: 0.50,
        Tolerance: 0.03,
        Importance: 1.0,
        Description: "Stakes are raised with either a false victory or false defeat",
        Act: 2);

    /// <summary>
    /// Bad Guys Close In - External pressures and internal doubts mount.
    /// </summary>
    public static SaveTheCatBeat BadGuysCloseIn { get; } = new(
        Code: "bad_guys_close_in",
        Name: "Bad Guys Close In",
        PositionIndex: 9,
        PercentageBs2: 0.50,
        PercentageFiction: 0.50,
        Tolerance: 0.08,
        Importance: 0.8,
        Description: "The villain regroups; internal dissent, doubt, and jealousy threaten the team",
        Act: 2,
        IsRange: true,
        RangeEnd: 0.75);

    /// <summary>
    /// All Is Lost - The lowest point with a "whiff of death."
    /// </summary>
    public static SaveTheCatBeat AllIsLost { get; } = new(
        Code: "all_is_lost",
        Name: "All Is Lost",
        PositionIndex: 10,
        PercentageBs2: 0.75,
        PercentageFiction: 0.68,
        Tolerance: 0.03,
        Importance: 1.0,
        Description: "Rock bottom - something 'dies' (literally or figuratively)",
        Act: 2);

    /// <summary>
    /// Dark Night of the Soul - Hero reflects in despair.
    /// </summary>
    public static SaveTheCatBeat DarkNightOfSoul { get; } = new(
        Code: "dark_night_of_soul",
        Name: "Dark Night of the Soul",
        PositionIndex: 11,
        PercentageBs2: 0.75,
        PercentageFiction: 0.68,
        Tolerance: 0.04,
        Importance: 0.8,
        Description: "Hero processes grief and faces who they must become",
        Act: 2,
        IsRange: true,
        RangeEnd: 0.80);

    /// <summary>
    /// Break into Three - Epiphany sparks renewed resolve.
    /// </summary>
    public static SaveTheCatBeat BreakIntoThree { get; } = new(
        Code: "break_into_three",
        Name: "Break into Three",
        PositionIndex: 12,
        PercentageBs2: 0.80,
        PercentageFiction: 0.77,
        Tolerance: 0.02,
        Importance: 1.0,
        Description: "Thanks to the B Story character, the hero realizes the solution",
        Act: 3);

    /// <summary>
    /// Finale - Hero applies what they've learned.
    /// </summary>
    public static SaveTheCatBeat Finale { get; } = new(
        Code: "finale",
        Name: "Finale",
        PositionIndex: 13,
        PercentageBs2: 0.80,
        PercentageFiction: 0.77,
        Tolerance: 0.08,
        Importance: 0.9,
        Description: "Synthesis of A and B stories - hero proves they've changed",
        Act: 3,
        IsRange: true,
        RangeEnd: 0.99);

    /// <summary>
    /// Final Image - Visual "after" showing transformation.
    /// </summary>
    public static SaveTheCatBeat FinalImage { get; } = new(
        Code: "final_image",
        Name: "Final Image",
        PositionIndex: 14,
        PercentageBs2: 1.00,
        PercentageFiction: 1.00,
        Tolerance: 0.01,
        Importance: 1.0,
        Description: "Opposite of Opening Image - proof the transformation has occurred",
        Act: 3);

    /// <summary>
    /// All Save the Cat beats in order.
    /// </summary>
    public static IReadOnlyList<SaveTheCatBeat> All { get; } = new List<SaveTheCatBeat>
    {
        OpeningImage, ThemeStated, SetUp, Catalyst, Debate,
        BreakIntoTwo, BStory, FunAndGames, Midpoint, BadGuysCloseIn,
        AllIsLost, DarkNightOfSoul, BreakIntoThree, Finale, FinalImage
    }.AsReadOnly();

    /// <summary>
    /// Gets a beat by its code.
    /// </summary>
    public static SaveTheCatBeat? GetByCode(string code) =>
        All.FirstOrDefault(b => b.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets all beats for a specific act.
    /// </summary>
    public static IEnumerable<SaveTheCatBeat> GetByAct(int act) =>
        All.Where(b => b.Act == act);

    /// <summary>
    /// Gets the target position for a beat using the specified strategy.
    /// </summary>
    /// <param name="code">Beat code.</param>
    /// <param name="strategy">Timing strategy ("bs2" or "fiction").</param>
    /// <returns>Target position as percentage (0-1).</returns>
    public static double GetTargetPosition(string code, string strategy = "fiction")
    {
        var beat = GetByCode(code);
        if (beat == null) return 0.0;

        return strategy.ToLowerInvariant() switch
        {
            "bs2" => beat.PercentageBs2,
            "fiction" => beat.PercentageFiction,
            _ => beat.PercentageFiction
        };
    }

    /// <summary>
    /// Critical beats that are most important for story structure.
    /// </summary>
    public static IReadOnlyList<SaveTheCatBeat> CriticalBeats { get; } = new List<SaveTheCatBeat>
    {
        Catalyst, BreakIntoTwo, Midpoint, AllIsLost, BreakIntoThree, FinalImage
    }.AsReadOnly();
}

/// <summary>
/// Represents a single Save the Cat beat.
/// </summary>
public sealed record SaveTheCatBeat(
    string Code,
    string Name,
    int PositionIndex,
    double PercentageBs2,
    double PercentageFiction,
    double Tolerance,
    double Importance,
    string Description,
    int Act,
    bool IsRange = false,
    double? RangeEnd = null)
{
    /// <summary>
    /// Gets the target position for this beat using the specified strategy.
    /// </summary>
    public double GetTargetPosition(string strategy = "fiction") => strategy.ToLowerInvariant() switch
    {
        "bs2" => PercentageBs2,
        _ => PercentageFiction
    };

    /// <summary>
    /// Checks if a position is within the acceptable range for this beat.
    /// </summary>
    public bool IsPositionAcceptable(double position, string strategy = "fiction")
    {
        var target = GetTargetPosition(strategy);
        return Math.Abs(position - target) <= Tolerance;
    }

    /// <summary>
    /// Gets whether this beat is considered critical for story structure.
    /// </summary>
    public bool IsCritical => Importance >= 1.0;
}

/// <summary>
/// Midpoint types per Save the Cat methodology.
/// </summary>
public enum MidpointType
{
    /// <summary>Hero seems to achieve goal but hasn't truly.</summary>
    FalseVictory,

    /// <summary>Appears all is lost but isn't; often raises stakes.</summary>
    FalseDefeat
}
