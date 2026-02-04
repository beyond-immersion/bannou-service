namespace BeyondImmersion.Bannou.StorylineTheory.Genre;

/// <summary>
/// Story Grid genre definitions with obligatory scenes, conventions, and value spectrums.
/// Based on Shawn Coyne's "The Story Grid" (2015).
/// </summary>
public static class StoryGridGenres
{
    /// <summary>
    /// Action genre - Life vs Death with external physical conflict.
    /// </summary>
    public static StoryGridGenre Action { get; } = new(
        Code: "action",
        Name: "Action",
        CoreValue: Genre.CoreValue.Life,
        CoreEmotion: "Excitement",
        ValueSpectrum: new ValueSpectrum("Life", "Death", "Damnation"),
        CoreEvent: "hero_at_mercy_of_villain",
        ObligatoryScenes: new[]
        {
            new ObligatoryScene("inciting_attack", "Inciting Attack or Threat",
                "An attack or threat of attack on the protagonist or their loved ones"),
            new ObligatoryScene("hero_sidesteps", "Hero Sidesteps Responsibility",
                "The hero initially refuses or avoids the call to action"),
            new ObligatoryScene("forced_to_act", "Forced to Act",
                "Circumstances force the hero to engage with the conflict"),
            new ObligatoryScene("discovers_antagonist", "Discovers Antagonist's Plan",
                "Hero learns what the villain wants and their methods"),
            new ObligatoryScene("hero_at_mercy", "Hero at Mercy of Villain",
                "The hero is at their lowest point, seemingly defeated"),
            new ObligatoryScene("hero_rallies", "Hero Rallies and Wins",
                "Hero finds the strength to overcome and defeats the villain"),
        },
        Conventions: new[]
        {
            new GenreConvention("clock", "Ticking Clock",
                "Time pressure that escalates tension"),
            new GenreConvention("training", "Training or Planning Sequence",
                "Hero prepares for the final confrontation"),
            new GenreConvention("hero_flawed", "Flawed Hero",
                "Protagonist has a weakness that must be overcome"),
            new GenreConvention("power_divide", "Power Divide",
                "Villain initially has more power/resources than hero"),
            new GenreConvention("speech_praise_villain", "Speech Praising Villain",
                "A moment establishing why the villain is formidable"),
        });

    /// <summary>
    /// Thriller genre - Life vs Damnation with psychological/existential stakes.
    /// </summary>
    public static StoryGridGenre Thriller { get; } = new(
        Code: "thriller",
        Name: "Thriller",
        CoreValue: Genre.CoreValue.Life,
        CoreEmotion: "Anxiety/Dread",
        ValueSpectrum: new ValueSpectrum("Life", "Death", "Damnation"),
        CoreEvent: "hero_at_mercy_of_villain",
        ObligatoryScenes: new[]
        {
            new ObligatoryScene("inciting_crime", "Inciting Crime",
                "A crime indicative of the master villain's power and methods"),
            new ObligatoryScene("stakes_personal", "Stakes Become Personal",
                "The conflict directly threatens hero or their loved ones"),
            new ObligatoryScene("villain_motivation", "Villain's Motivation Revealed",
                "Hero discovers what the villain truly wants"),
            new ObligatoryScene("hero_at_mercy", "Hero at Mercy of Villain",
                "Hero is seemingly defeated with no way out"),
            new ObligatoryScene("false_ending", "False Ending",
                "Villain appears defeated but returns for final confrontation"),
            new ObligatoryScene("final_confrontation", "Final Confrontation",
                "Hero faces villain directly in climactic scene"),
        },
        Conventions: new[]
        {
            new GenreConvention("master_villain", "Master Villain",
                "Antagonist more powerful and intelligent than the protagonist"),
            new GenreConvention("clock", "Ticking Clock",
                "Time pressure driving urgency"),
            new GenreConvention("macguffin", "MacGuffin",
                "Object of desire that drives the plot"),
            new GenreConvention("red_herrings", "Red Herrings",
                "False leads that misdirect the audience"),
            new GenreConvention("information_reveal", "Progressive Information Reveal",
                "Truth emerges gradually through investigation"),
        });

    /// <summary>
    /// Horror genre - Life vs Fate Worse Than Death with supernatural/monstrous threat.
    /// </summary>
    public static StoryGridGenre Horror { get; } = new(
        Code: "horror",
        Name: "Horror",
        CoreValue: Genre.CoreValue.Life,
        CoreEmotion: "Terror/Dread",
        ValueSpectrum: new ValueSpectrum("Life", "Death", "Fate Worse Than Death"),
        CoreEvent: "victim_at_mercy_of_monster",
        ObligatoryScenes: new[]
        {
            new ObligatoryScene("inciting_monster", "Monster Attacks",
                "First appearance or attack by the monster/threat"),
            new ObligatoryScene("nonheroic_protagonist", "Non-Heroic Protagonist",
                "Hero is ordinary person, not equipped for this threat"),
            new ObligatoryScene("speech_praise_monster", "Speech Praising Monster",
                "Establishes why the monster is terrifying"),
            new ObligatoryScene("victim_at_mercy", "Victim at Mercy of Monster",
                "Protagonist is cornered with seemingly no escape"),
            new ObligatoryScene("false_ending", "False Ending",
                "Monster appears defeated but returns"),
        },
        Conventions: new[]
        {
            new GenreConvention("monster", "Unique Monster",
                "A threat with specific, terrifying capabilities"),
            new GenreConvention("sin_transgression", "Sin or Transgression",
                "Someone did something to deserve the monster's attention"),
            new GenreConvention("isolated_setting", "Isolated Setting",
                "Location that prevents easy escape or outside help"),
            new GenreConvention("escalating_threat", "Escalating Threat",
                "Monster becomes more dangerous over time"),
        });

    /// <summary>
    /// Mystery/Crime genre - Justice vs Injustice with investigation.
    /// </summary>
    public static StoryGridGenre Crime { get; } = new(
        Code: "crime",
        Name: "Crime/Mystery",
        CoreValue: Genre.CoreValue.Justice,
        CoreEmotion: "Intrigue",
        ValueSpectrum: new ValueSpectrum("Justice", "Injustice", "Tyranny"),
        CoreEvent: "criminal_exposed",
        ObligatoryScenes: new[]
        {
            new ObligatoryScene("crime_discovered", "Crime Discovered",
                "The crime or mystery is revealed to the protagonist"),
            new ObligatoryScene("clue_gathering", "Clue Gathering",
                "Detective/protagonist collects evidence"),
            new ObligatoryScene("red_herring_pursuit", "Red Herring Pursuit",
                "A false lead is followed to dead end"),
            new ObligatoryScene("truth_revealed", "Truth Revealed",
                "Detective exposes the actual truth"),
            new ObligatoryScene("criminal_unmasked", "Criminal Exposed",
                "Identity of perpetrator revealed publicly"),
        },
        Conventions: new[]
        {
            new GenreConvention("clever_criminal", "Clever Criminal",
                "Perpetrator capable of concealing their crime"),
            new GenreConvention("clever_detective", "Clever Detective",
                "Investigator capable of solving the mystery"),
            new GenreConvention("clues", "Fair Play Clues",
                "Evidence available to both detective and audience"),
            new GenreConvention("red_herrings", "Red Herrings",
                "Misleading evidence or suspects"),
            new GenreConvention("personal_stakes", "Making It Personal",
                "Investigation becomes personally meaningful to detective"),
        });

    /// <summary>
    /// Love/Romance genre - Love vs Hate with relationship focus.
    /// </summary>
    public static StoryGridGenre Love { get; } = new(
        Code: "love",
        Name: "Love Story",
        CoreValue: Genre.CoreValue.Love,
        CoreEmotion: "Joy/Heartbreak",
        ValueSpectrum: new ValueSpectrum("Love", "Hate", "Self-Hatred"),
        CoreEvent: "proof_of_love",
        ObligatoryScenes: new[]
        {
            new ObligatoryScene("lovers_meet", "Lovers Meet",
                "First meeting between the potential lovers"),
            new ObligatoryScene("first_connection", "First Genuine Connection",
                "Moment of authentic intimacy beyond attraction"),
            new ObligatoryScene("confession", "Confession of Love",
                "One or both lovers express their feelings"),
            new ObligatoryScene("lovers_break_up", "Lovers Break Up",
                "Relationship appears to end irreparably"),
            new ObligatoryScene("proof_of_love", "Proof of Love",
                "Ultimate demonstration of love (often sacrifice)"),
            new ObligatoryScene("lovers_reunite", "Lovers Reunite",
                "Final reunion confirming the relationship"),
        },
        Conventions: new[]
        {
            new GenreConvention("helpers_harmers", "Helpers and Harmers",
                "Secondary characters who assist or obstruct the romance"),
            new GenreConvention("triangle", "Love Triangle",
                "Rival for one lover's affection"),
            new GenreConvention("external_obstacles", "External Obstacles",
                "Society, family, or circumstance opposing the union"),
            new GenreConvention("internal_obstacles", "Internal Obstacles",
                "Personal fears or flaws preventing commitment"),
        });

    /// <summary>
    /// Worldview genre - Sophistication vs Naivete (coming of age).
    /// </summary>
    public static StoryGridGenre Worldview { get; } = new(
        Code: "worldview",
        Name: "Worldview (Coming of Age)",
        CoreValue: Genre.CoreValue.Worldview,
        CoreEmotion: "Satisfaction/Revelation",
        ValueSpectrum: new ValueSpectrum("Sophistication", "Naivete", "Disillusionment"),
        CoreEvent: "moment_of_realization",
        ObligatoryScenes: new[]
        {
            new ObligatoryScene("naive_state", "Naive State",
                "Protagonist begins with limited worldview"),
            new ObligatoryScene("new_experience", "Challenging Experience",
                "Event that challenges the protagonist's assumptions"),
            new ObligatoryScene("denial", "Denial",
                "Protagonist resists changing their worldview"),
            new ObligatoryScene("moment_realization", "Moment of Realization",
                "Epiphany where protagonist sees truth"),
            new ObligatoryScene("new_worldview", "Acceptance of New Worldview",
                "Protagonist integrates the new understanding"),
        },
        Conventions: new[]
        {
            new GenreConvention("mentor", "Mentor Figure",
                "Older/wiser character who guides the protagonist"),
            new GenreConvention("threshold", "Threshold Guardian",
                "Test the protagonist must pass to advance"),
            new GenreConvention("coming_of_age_markers", "Coming of Age Markers",
                "Symbolic transitions (first love, first job, etc.)"),
        });

    /// <summary>
    /// Morality genre - Altruism vs Selfishness.
    /// </summary>
    public static StoryGridGenre Morality { get; } = new(
        Code: "morality",
        Name: "Morality",
        CoreValue: Genre.CoreValue.Morality,
        CoreEmotion: "Catharsis",
        ValueSpectrum: new ValueSpectrum("Altruism", "Selfishness", "Self-Destruction"),
        CoreEvent: "moral_weight_scene",
        ObligatoryScenes: new[]
        {
            new ObligatoryScene("temptation", "Temptation",
                "Protagonist faces opportunity for easy selfish gain"),
            new ObligatoryScene("wrong_path", "Wrong Path",
                "Protagonist chooses selfishly (or is perceived to)"),
            new ObligatoryScene("consequences", "Consequences",
                "Results of the selfish choice emerge"),
            new ObligatoryScene("moral_weight", "Moral Weight Scene",
                "Moment where protagonist must choose altruism at cost"),
            new ObligatoryScene("redemption_or_fall", "Redemption or Fall",
                "Final choice determines protagonist's moral fate"),
        },
        Conventions: new[]
        {
            new GenreConvention("sacrifice_opportunity", "Sacrifice Opportunity",
                "Clear moment where protagonist can help others at personal cost"),
            new GenreConvention("mirror_character", "Mirror Character",
                "Character who represents the path not taken"),
            new GenreConvention("moral_stakes", "Clear Moral Stakes",
                "Audience understands what's right and wrong"),
        });

    /// <summary>
    /// Status genre - Success vs Failure.
    /// </summary>
    public static StoryGridGenre Status { get; } = new(
        Code: "status",
        Name: "Status",
        CoreValue: Genre.CoreValue.Success,
        CoreEmotion: "Admiration/Pity",
        ValueSpectrum: new ValueSpectrum("Success", "Failure", "Selling Out"),
        CoreEvent: "big_event",
        ObligatoryScenes: new[]
        {
            new ObligatoryScene("ambition", "Ambition Established",
                "Protagonist's goal for status/success is clear"),
            new ObligatoryScene("mentor_antagonist", "Mentor or Antagonist",
                "Guide or obstacle to protagonist's ambition"),
            new ObligatoryScene("setback", "Major Setback",
                "Protagonist suffers significant failure"),
            new ObligatoryScene("big_event", "Big Event",
                "Public moment of success or failure"),
            new ObligatoryScene("new_status", "New Status",
                "Protagonist's position in hierarchy changes"),
        },
        Conventions: new[]
        {
            new GenreConvention("rise_fall_arc", "Rise and/or Fall Arc",
                "Clear trajectory of status change"),
            new GenreConvention("external_measure", "External Measure of Success",
                "Visible marker of status (money, title, fame)"),
            new GenreConvention("cost_of_success", "Cost of Success",
                "What protagonist must sacrifice for their ambition"),
        });

    /// <summary>
    /// All defined genres.
    /// </summary>
    public static IReadOnlyList<StoryGridGenre> All { get; } = new List<StoryGridGenre>
    {
        Action, Thriller, Horror, Crime, Love, Worldview, Morality, Status
    }.AsReadOnly();

    /// <summary>
    /// Gets a genre by its code.
    /// </summary>
    public static StoryGridGenre? GetByCode(string code) =>
        All.FirstOrDefault(g => g.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Represents a complete Story Grid genre definition.
/// </summary>
public sealed record StoryGridGenre(
    string Code,
    string Name,
    CoreValue CoreValue,
    string CoreEmotion,
    ValueSpectrum ValueSpectrum,
    string CoreEvent,
    IReadOnlyList<ObligatoryScene> ObligatoryScenes,
    IReadOnlyList<GenreConvention> Conventions)
{
    /// <summary>
    /// Checks if a scene code matches the core event for this genre.
    /// </summary>
    public bool IsCoreEvent(string sceneCode) =>
        CoreEvent.Equals(sceneCode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets an obligatory scene by its code.
    /// </summary>
    public ObligatoryScene? GetObligatoryScene(string code) =>
        ObligatoryScenes.FirstOrDefault(s => s.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets a convention by its code.
    /// </summary>
    public GenreConvention? GetConvention(string code) =>
        Conventions.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Represents a required scene for a genre.
/// </summary>
public sealed record ObligatoryScene(
    string Code,
    string Name,
    string Description);

/// <summary>
/// Represents an expected convention for a genre.
/// </summary>
public sealed record GenreConvention(
    string Code,
    string Name,
    string Description);
