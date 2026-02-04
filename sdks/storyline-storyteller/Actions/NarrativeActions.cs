using BeyondImmersion.Bannou.StorylineTheory.State;

namespace BeyondImmersion.Bannou.StorylineStoryteller.Actions;

/// <summary>
/// Catalog of standard narrative actions for GOAP planning.
/// Each action modifies narrative state dimensions.
/// </summary>
public static class NarrativeActions
{
    #region Escalation Actions

    /// <summary>
    /// Raise the stakes of the conflict.
    /// Effect: Stakes +0.2, Tension +0.1.
    /// </summary>
    public static NarrativeAction RaiseStakes { get; } = new(
        Code: "raise_stakes",
        Name: "Raise Stakes",
        Description: "Increase what's at risk in the conflict",
        Preconditions: new NarrativeStateRange { MaxStakes = 0.8 },
        Effects: new NarrativeState(0.6, 0.7, 0.5, 0.55, 0.5, 0.45),
        Cost: 1.0,
        Category: ActionCategory.Escalation);

    /// <summary>
    /// Introduce or escalate a threat.
    /// Effect: Tension +0.25, Hope -0.15.
    /// </summary>
    public static NarrativeAction IntroduceThreat { get; } = new(
        Code: "introduce_threat",
        Name: "Introduce Threat",
        Description: "Present a new danger or escalate existing one",
        Preconditions: new NarrativeStateRange { MaxTension = 0.85 },
        Effects: new NarrativeState(0.75, 0.6, 0.55, 0.6, 0.5, 0.35),
        Cost: 1.5,
        Category: ActionCategory.Escalation);

    /// <summary>
    /// Add time pressure to the situation.
    /// Effect: Urgency +0.3, Tension +0.1.
    /// </summary>
    public static NarrativeAction AddTimeLimit { get; } = new(
        Code: "add_time_limit",
        Name: "Add Time Limit",
        Description: "Introduce a deadline or countdown",
        Preconditions: new NarrativeStateRange { MaxUrgency = 0.7 },
        Effects: new NarrativeState(0.6, 0.55, 0.5, 0.8, 0.5, 0.45),
        Cost: 1.2,
        Category: ActionCategory.Escalation);

    #endregion

    #region Resolution Actions

    /// <summary>
    /// Resolve a conflict or threat.
    /// Effect: Tension -0.3, Hope +0.2.
    /// </summary>
    public static NarrativeAction ResolveConflict { get; } = new(
        Code: "resolve_conflict",
        Name: "Resolve Conflict",
        Description: "Successfully conclude a conflict",
        Preconditions: new NarrativeStateRange { MinTension = 0.3 },
        Effects: new NarrativeState(0.2, 0.4, 0.4, 0.3, 0.6, 0.7),
        Cost: 2.0,
        Category: ActionCategory.Resolution);

    /// <summary>
    /// Reduce urgency by extending time or removing deadline.
    /// Effect: Urgency -0.25, Tension -0.1.
    /// </summary>
    public static NarrativeAction ExtendDeadline { get; } = new(
        Code: "extend_deadline",
        Name: "Extend Deadline",
        Description: "Buy more time or remove time pressure",
        Preconditions: new NarrativeStateRange { MinUrgency = 0.3 },
        Effects: new NarrativeState(0.4, 0.5, 0.5, 0.25, 0.5, 0.55),
        Cost: 1.0,
        Category: ActionCategory.Resolution);

    /// <summary>
    /// Achieve a small victory or milestone.
    /// Effect: Hope +0.15, Stakes -0.1.
    /// </summary>
    public static NarrativeAction SmallVictory { get; } = new(
        Code: "small_victory",
        Name: "Small Victory",
        Description: "Protagonist achieves a minor success",
        Preconditions: NarrativeStateRange.Any,
        Effects: new NarrativeState(0.45, 0.4, 0.5, 0.45, 0.55, 0.65),
        Cost: 1.0,
        Category: ActionCategory.Resolution);

    #endregion

    #region Revelation Actions

    /// <summary>
    /// Reveal new information or a secret.
    /// Effect: Mystery -0.2, Tension +0.1 (from implications).
    /// </summary>
    public static NarrativeAction RevealSecret { get; } = new(
        Code: "reveal_secret",
        Name: "Reveal Secret",
        Description: "Uncover hidden information",
        Preconditions: new NarrativeStateRange { MinMystery = 0.2 },
        Effects: new NarrativeState(0.6, 0.55, 0.3, 0.5, 0.55, 0.5),
        Cost: 1.5,
        Category: ActionCategory.Revelation);

    /// <summary>
    /// Introduce a new mystery or question.
    /// Effect: Mystery +0.25, Stakes +0.05.
    /// </summary>
    public static NarrativeAction IntroduceMystery { get; } = new(
        Code: "introduce_mystery",
        Name: "Introduce Mystery",
        Description: "Raise new questions or unknowns",
        Preconditions: new NarrativeStateRange { MaxMystery = 0.8 },
        Effects: new NarrativeState(0.55, 0.55, 0.75, 0.5, 0.5, 0.5),
        Cost: 1.0,
        Category: ActionCategory.Revelation);

    /// <summary>
    /// A character reveals their true nature or motivation.
    /// Effect: Mystery -0.15, Intimacy +0.1.
    /// </summary>
    public static NarrativeAction CharacterRevelation { get; } = new(
        Code: "character_revelation",
        Name: "Character Revelation",
        Description: "A character's true nature is revealed",
        Preconditions: NarrativeStateRange.Any,
        Effects: new NarrativeState(0.55, 0.5, 0.35, 0.5, 0.6, 0.5),
        Cost: 1.3,
        Category: ActionCategory.Revelation);

    #endregion

    #region Bonding Actions

    /// <summary>
    /// Characters share a meaningful moment.
    /// Effect: Intimacy +0.2, Tension -0.1.
    /// </summary>
    public static NarrativeAction SharedMoment { get; } = new(
        Code: "shared_moment",
        Name: "Shared Moment",
        Description: "Characters connect emotionally",
        Preconditions: NarrativeStateRange.Any,
        Effects: new NarrativeState(0.4, 0.5, 0.5, 0.45, 0.7, 0.55),
        Cost: 0.8,
        Category: ActionCategory.Bonding);

    /// <summary>
    /// A character makes a sacrifice for another.
    /// Effect: Intimacy +0.3, Stakes +0.1, Hope +0.1.
    /// </summary>
    public static NarrativeAction SacrificeForOther { get; } = new(
        Code: "sacrifice_for_other",
        Name: "Sacrifice for Other",
        Description: "Character gives up something for someone else",
        Preconditions: new NarrativeStateRange { MinIntimacy = 0.3 },
        Effects: new NarrativeState(0.55, 0.6, 0.5, 0.5, 0.8, 0.6),
        Cost: 2.0,
        Category: ActionCategory.Bonding);

    /// <summary>
    /// Characters experience a betrayal.
    /// Effect: Intimacy -0.3, Tension +0.2, Hope -0.15.
    /// </summary>
    public static NarrativeAction Betrayal { get; } = new(
        Code: "betrayal",
        Name: "Betrayal",
        Description: "Trust is broken between characters",
        Preconditions: new NarrativeStateRange { MinIntimacy = 0.4 },
        Effects: new NarrativeState(0.7, 0.6, 0.6, 0.6, 0.2, 0.35),
        Cost: 2.5,
        Category: ActionCategory.Bonding);

    #endregion

    #region Complication Actions

    /// <summary>
    /// An unexpected setback occurs.
    /// Effect: Hope -0.2, Tension +0.15, Stakes +0.1.
    /// </summary>
    public static NarrativeAction Setback { get; } = new(
        Code: "setback",
        Name: "Setback",
        Description: "Progress is reversed or blocked",
        Preconditions: new NarrativeStateRange { MinHope = 0.2 },
        Effects: new NarrativeState(0.65, 0.6, 0.55, 0.6, 0.5, 0.3),
        Cost: 1.5,
        Category: ActionCategory.Complication);

    /// <summary>
    /// A new problem emerges from a previous solution.
    /// Effect: Mystery +0.1, Stakes +0.15, Tension +0.1.
    /// </summary>
    public static NarrativeAction UnintendedConsequence { get; } = new(
        Code: "unintended_consequence",
        Name: "Unintended Consequence",
        Description: "Previous action creates new problems",
        Preconditions: NarrativeStateRange.Any,
        Effects: new NarrativeState(0.6, 0.65, 0.6, 0.55, 0.5, 0.4),
        Cost: 1.3,
        Category: ActionCategory.Complication);

    /// <summary>
    /// Multiple problems converge simultaneously.
    /// Effect: Urgency +0.2, Tension +0.2, Stakes +0.15.
    /// </summary>
    public static NarrativeAction ConvergingCrises { get; } = new(
        Code: "converging_crises",
        Name: "Converging Crises",
        Description: "Multiple threats combine",
        Preconditions: new NarrativeStateRange { MinTension = 0.4 },
        Effects: new NarrativeState(0.7, 0.65, 0.55, 0.7, 0.5, 0.35),
        Cost: 2.0,
        Category: ActionCategory.Complication);

    #endregion

    #region Tone Shift Actions

    /// <summary>
    /// Shift to a more hopeful tone.
    /// Effect: Hope +0.25, Tension -0.1.
    /// </summary>
    public static NarrativeAction ShiftHopeful { get; } = new(
        Code: "shift_hopeful",
        Name: "Shift Hopeful",
        Description: "Move toward optimistic tone",
        Preconditions: new NarrativeStateRange { MaxHope = 0.8 },
        Effects: new NarrativeState(0.4, 0.5, 0.5, 0.45, 0.55, 0.75),
        Cost: 1.0,
        Category: ActionCategory.ToneShift);

    /// <summary>
    /// Shift to a darker tone.
    /// Effect: Hope -0.25, Stakes +0.1.
    /// </summary>
    public static NarrativeAction ShiftDark { get; } = new(
        Code: "shift_dark",
        Name: "Shift Dark",
        Description: "Move toward pessimistic tone",
        Preconditions: new NarrativeStateRange { MinHope = 0.2 },
        Effects: new NarrativeState(0.55, 0.6, 0.55, 0.55, 0.5, 0.25),
        Cost: 1.0,
        Category: ActionCategory.ToneShift);

    /// <summary>
    /// A moment of calm amid chaos.
    /// Effect: Tension -0.2, Urgency -0.15, Intimacy +0.1.
    /// </summary>
    public static NarrativeAction BreathingRoom { get; } = new(
        Code: "breathing_room",
        Name: "Breathing Room",
        Description: "Brief respite from tension",
        Preconditions: new NarrativeStateRange { MinTension = 0.4 },
        Effects: new NarrativeState(0.3, 0.5, 0.5, 0.35, 0.6, 0.55),
        Cost: 0.8,
        Category: ActionCategory.ToneShift);

    #endregion

    /// <summary>
    /// All defined narrative actions.
    /// </summary>
    public static IReadOnlyList<NarrativeAction> All { get; } = new List<NarrativeAction>
    {
        // Escalation
        RaiseStakes, IntroduceThreat, AddTimeLimit,
        // Resolution
        ResolveConflict, ExtendDeadline, SmallVictory,
        // Revelation
        RevealSecret, IntroduceMystery, CharacterRevelation,
        // Bonding
        SharedMoment, SacrificeForOther, Betrayal,
        // Complication
        Setback, UnintendedConsequence, ConvergingCrises,
        // Tone Shift
        ShiftHopeful, ShiftDark, BreathingRoom
    }.AsReadOnly();

    /// <summary>
    /// Gets an action by its code.
    /// </summary>
    public static NarrativeAction? GetByCode(string code) =>
        All.FirstOrDefault(a => a.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Gets all actions in a category.
    /// </summary>
    public static IEnumerable<NarrativeAction> GetByCategory(ActionCategory category) =>
        All.Where(a => a.Category == category);

    /// <summary>
    /// Gets actions that are valid from a given state.
    /// </summary>
    public static IEnumerable<NarrativeAction> GetValidActions(NarrativeState state) =>
        All.Where(a => a.CanApply(state));

    /// <summary>
    /// Gets actions ranked by how much they help reach a goal.
    /// </summary>
    /// <param name="currentState">Current narrative state.</param>
    /// <param name="goalState">Target state.</param>
    /// <returns>Actions sorted by effectiveness (best first).</returns>
    public static IEnumerable<NarrativeAction> GetRankedActions(
        NarrativeState currentState,
        NarrativeState goalState)
    {
        return All
            .Where(a => a.CanApply(currentState))
            .OrderBy(a => a.GetEffectiveCost(currentState, goalState));
    }
}
