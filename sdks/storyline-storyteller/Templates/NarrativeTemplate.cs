using BeyondImmersion.Bannou.StorylineTheory.State;
using BeyondImmersion.Bannou.StorylineTheory.Structure;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Templates;

/// <summary>
/// Represents a narrative template with target states for story beats.
/// </summary>
/// <param name="Code">Unique identifier.</param>
/// <param name="Name">Display name.</param>
/// <param name="Description">What this template represents.</param>
/// <param name="Beats">Ordered beat definitions with target states.</param>
public sealed record NarrativeTemplate(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<TemplateBeat> Beats)
{
    /// <summary>
    /// Gets the target state for a given story position.
    /// Interpolates between beat targets.
    /// </summary>
    /// <param name="position">Position in the story (0-1).</param>
    /// <returns>Target narrative state at this position.</returns>
    public NarrativeState GetTargetStateAt(double position)
    {
        position = Math.Clamp(position, 0.0, 1.0);

        // Find surrounding beats
        TemplateBeat? before = null;
        TemplateBeat? after = null;

        foreach (var beat in Beats)
        {
            if (beat.Position <= position)
            {
                before = beat;
            }
            else if (after == null)
            {
                after = beat;
                break;
            }
        }

        if (before == null && after == null)
            return NarrativeState.Equilibrium;

        if (before == null)
            return after!.TargetState;

        if (after == null)
            return before.TargetState;

        // Interpolate between beats
        var range = after.Position - before.Position;
        if (range <= 0)
            return before.TargetState;

        var t = (position - before.Position) / range;
        return before.TargetState.InterpolateTo(after.TargetState, t);
    }

    /// <summary>
    /// Gets all beats within a position range.
    /// </summary>
    public IEnumerable<TemplateBeat> GetBeatsInRange(double startPosition, double endPosition) =>
        Beats.Where(b => b.Position >= startPosition && b.Position <= endPosition);

    /// <summary>
    /// Gets the next upcoming beat after a position.
    /// </summary>
    public TemplateBeat? GetNextBeat(double currentPosition) =>
        Beats.FirstOrDefault(b => b.Position > currentPosition);
}

/// <summary>
/// A beat within a narrative template.
/// </summary>
/// <param name="Code">Beat identifier (often matches Save the Cat beat code).</param>
/// <param name="Name">Display name.</param>
/// <param name="Position">Target position in the story (0-1).</param>
/// <param name="TargetState">Ideal narrative state at this beat.</param>
/// <param name="Description">What should happen at this beat.</param>
public sealed record TemplateBeat(
    string Code,
    string Name,
    double Position,
    NarrativeState TargetState,
    string Description);

/// <summary>
/// Standard narrative templates for common story patterns.
/// </summary>
public static class NarrativeTemplates
{
    /// <summary>
    /// Classic Hero's Journey / Save the Cat template.
    /// 15 beats following Blake Snyder's structure.
    /// </summary>
    public static NarrativeTemplate HeroJourney { get; } = new(
        Code: "hero_journey",
        Name: "Hero's Journey",
        Description: "Classic 15-beat structure from ordinary world to transformation",
        Beats: new List<TemplateBeat>
        {
            new("opening_image", "Opening Image", 0.00,
                NarrativeState.Presets.Equilibrium,
                "Establish the protagonist's ordinary world"),

            new("theme_stated", "Theme Stated", 0.05,
                new NarrativeState(0.2, 0.3, 0.3, 0.2, 0.5, 0.7),
                "Hint at the story's deeper meaning"),

            new("setup", "Setup", 0.10,
                new NarrativeState(0.25, 0.35, 0.25, 0.25, 0.5, 0.65),
                "Develop character and stakes"),

            new("catalyst", "Catalyst", 0.12,
                new NarrativeState(0.4, 0.45, 0.35, 0.35, 0.5, 0.55),
                "The inciting incident changes everything"),

            new("debate", "Debate", 0.17,
                new NarrativeState(0.35, 0.4, 0.4, 0.3, 0.5, 0.5),
                "Hero questions whether to accept the call"),

            new("break_into_two", "Break into Two", 0.25,
                new NarrativeState(0.45, 0.5, 0.35, 0.4, 0.5, 0.55),
                "Hero commits to the journey"),

            new("b_story", "B Story", 0.30,
                new NarrativeState(0.4, 0.45, 0.35, 0.35, 0.6, 0.55),
                "Secondary story and love interest introduced"),

            new("fun_and_games", "Fun and Games", 0.40,
                new NarrativeState(0.5, 0.55, 0.4, 0.45, 0.55, 0.6),
                "Promise of the premise delivered"),

            new("midpoint", "Midpoint", 0.50,
                new NarrativeState(0.65, 0.65, 0.35, 0.55, 0.6, 0.5),
                "Stakes are raised; false victory or defeat"),

            new("bad_guys_close_in", "Bad Guys Close In", 0.62,
                new NarrativeState(0.75, 0.75, 0.4, 0.7, 0.55, 0.4),
                "External pressure mounts"),

            new("all_is_lost", "All Is Lost", 0.75,
                NarrativeState.Presets.DarkestHour,
                "Rock bottom; something 'dies'"),

            new("dark_night_of_soul", "Dark Night of the Soul", 0.80,
                new NarrativeState(0.6, 0.85, 0.25, 0.7, 0.7, 0.15),
                "Hero processes grief and despair"),

            new("break_into_three", "Break into Three", 0.85,
                new NarrativeState(0.7, 0.85, 0.2, 0.85, 0.75, 0.35),
                "Epiphany leads to new resolve"),

            new("finale", "Finale", 0.92,
                NarrativeState.Presets.Climax,
                "Hero applies lessons learned"),

            new("final_image", "Final Image", 1.00,
                NarrativeState.Presets.Resolution,
                "Transformation complete; new equilibrium")
        }.AsReadOnly());

    /// <summary>
    /// Mystery/Investigation template.
    /// Emphasizes mystery dimension throughout.
    /// </summary>
    public static NarrativeTemplate Mystery { get; } = new(
        Code: "mystery",
        Name: "Mystery Investigation",
        Description: "Crime/mystery structure with discovery-driven pacing",
        Beats: new List<TemplateBeat>
        {
            new("opening", "Opening", 0.00,
                NarrativeState.Presets.MysteryHook,
                "Establish normalcy before crime"),

            new("crime_discovered", "Crime Discovered", 0.10,
                new NarrativeState(0.5, 0.5, 0.9, 0.4, 0.3, 0.5),
                "The mystery is revealed"),

            new("investigation_begins", "Investigation Begins", 0.20,
                new NarrativeState(0.4, 0.5, 0.85, 0.45, 0.35, 0.55),
                "Detective takes the case"),

            new("first_clue", "First Clue", 0.30,
                new NarrativeState(0.45, 0.55, 0.75, 0.5, 0.4, 0.6),
                "Initial evidence found"),

            new("red_herring", "Red Herring", 0.40,
                new NarrativeState(0.55, 0.6, 0.7, 0.55, 0.4, 0.5),
                "False lead explored"),

            new("stakes_personal", "Stakes Become Personal", 0.50,
                new NarrativeState(0.65, 0.75, 0.65, 0.65, 0.55, 0.4),
                "Detective is personally invested"),

            new("key_discovery", "Key Discovery", 0.65,
                new NarrativeState(0.7, 0.8, 0.5, 0.7, 0.5, 0.45),
                "Major breakthrough"),

            new("confrontation", "Confrontation", 0.80,
                new NarrativeState(0.85, 0.85, 0.3, 0.85, 0.6, 0.35),
                "Face-to-face with the truth"),

            new("truth_revealed", "Truth Revealed", 0.90,
                new NarrativeState(0.7, 0.7, 0.1, 0.6, 0.65, 0.5),
                "Full solution explained"),

            new("justice", "Justice", 1.00,
                new NarrativeState(0.15, 0.3, 0.05, 0.1, 0.7, 0.85),
                "Resolution and closure")
        }.AsReadOnly());

    /// <summary>
    /// Romance template.
    /// Emphasizes intimacy and emotional vulnerability.
    /// </summary>
    public static NarrativeTemplate Romance { get; } = new(
        Code: "romance",
        Name: "Romance",
        Description: "Love story with relationship-focused beats",
        Beats: new List<TemplateBeat>
        {
            new("meet_cute", "Meet Cute", 0.10,
                new NarrativeState(0.2, 0.2, 0.4, 0.15, 0.2, 0.7),
                "Lovers first meet"),

            new("attraction", "Attraction", 0.20,
                new NarrativeState(0.25, 0.25, 0.35, 0.2, 0.35, 0.75),
                "Initial chemistry develops"),

            new("obstacle_revealed", "Obstacle Revealed", 0.30,
                new NarrativeState(0.4, 0.45, 0.45, 0.35, 0.4, 0.55),
                "Something stands in the way"),

            new("deepening", "Deepening", 0.40,
                new NarrativeState(0.35, 0.4, 0.35, 0.3, 0.55, 0.65),
                "Genuine connection forms"),

            new("intimacy", "Intimacy", 0.50,
                new NarrativeState(0.3, 0.5, 0.3, 0.35, 0.75, 0.7),
                "Emotional (or physical) intimacy peak"),

            new("crisis_of_faith", "Crisis of Faith", 0.65,
                new NarrativeState(0.55, 0.65, 0.4, 0.5, 0.5, 0.35),
                "Doubt threatens the relationship"),

            new("breakup", "Breakup", 0.75,
                new NarrativeState(0.6, 0.7, 0.35, 0.45, 0.3, 0.2),
                "Lovers separate"),

            new("realization", "Realization", 0.85,
                new NarrativeState(0.5, 0.6, 0.25, 0.55, 0.45, 0.4),
                "Truth about their feelings"),

            new("grand_gesture", "Grand Gesture", 0.92,
                new NarrativeState(0.55, 0.65, 0.15, 0.7, 0.7, 0.65),
                "Proof of love"),

            new("reunion", "Reunion", 1.00,
                new NarrativeState(0.1, 0.2, 0.05, 0.1, 0.95, 0.95),
                "Lovers reunited")
        }.AsReadOnly());

    /// <summary>
    /// Tragedy template.
    /// Hope steadily diminishes toward inevitable end.
    /// </summary>
    public static NarrativeTemplate Tragedy { get; } = new(
        Code: "tragedy",
        Name: "Tragedy",
        Description: "Downward arc to inevitable doom",
        Beats: new List<TemplateBeat>
        {
            new("prosperity", "Prosperity", 0.00,
                new NarrativeState(0.15, 0.25, 0.25, 0.15, 0.65, 0.85),
                "Hero at their peak"),

            new("flaw_established", "Flaw Established", 0.15,
                new NarrativeState(0.25, 0.35, 0.35, 0.2, 0.6, 0.75),
                "Tragic flaw is shown"),

            new("temptation", "Temptation", 0.25,
                new NarrativeState(0.35, 0.45, 0.4, 0.3, 0.55, 0.65),
                "Hero faces moral choice"),

            new("wrong_choice", "Wrong Choice", 0.35,
                new NarrativeState(0.45, 0.55, 0.45, 0.4, 0.5, 0.55),
                "Hero chooses poorly"),

            new("consequences_begin", "Consequences Begin", 0.45,
                new NarrativeState(0.55, 0.65, 0.5, 0.5, 0.45, 0.45),
                "Results of choice emerge"),

            new("denial", "Denial", 0.55,
                new NarrativeState(0.6, 0.7, 0.55, 0.55, 0.4, 0.35),
                "Hero refuses to see truth"),

            new("isolation", "Isolation", 0.65,
                new NarrativeState(0.7, 0.8, 0.45, 0.65, 0.25, 0.25),
                "Allies abandon hero"),

            new("recognition", "Recognition", 0.80,
                new NarrativeState(0.75, 0.85, 0.3, 0.75, 0.35, 0.15),
                "Hero finally sees the truth"),

            new("catastrophe", "Catastrophe", 0.90,
                new NarrativeState(0.85, 0.95, 0.15, 0.9, 0.3, 0.1),
                "Inevitable doom arrives"),

            new("catharsis", "Catharsis", 1.00,
                NarrativeState.Presets.Tragedy,
                "Emotional release for audience")
        }.AsReadOnly());

    /// <summary>
    /// All defined templates.
    /// </summary>
    public static IReadOnlyList<NarrativeTemplate> All { get; } = new List<NarrativeTemplate>
    {
        HeroJourney, Mystery, Romance, Tragedy
    }.AsReadOnly();

    /// <summary>
    /// Gets a template by code.
    /// </summary>
    public static NarrativeTemplate? GetByCode(string code) =>
        All.FirstOrDefault(t => t.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
}
