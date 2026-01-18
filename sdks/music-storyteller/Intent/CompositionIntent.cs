using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Intent;

/// <summary>
/// Complete composition intent that bridges storytelling decisions
/// to the music-theory generation engine.
/// Source: Plan Phase 7 - Intent Generation
/// </summary>
public sealed record CompositionIntent
{
    /// <summary>
    /// Gets the target emotional state for this section.
    /// </summary>
    public required EmotionalState EmotionalTarget { get; init; }

    /// <summary>
    /// Gets the harmonic intent.
    /// </summary>
    public HarmonicIntent Harmony { get; init; } = HarmonicIntent.Default;

    /// <summary>
    /// Gets the melodic intent.
    /// </summary>
    public MelodicIntent Melody { get; init; } = MelodicIntent.Default;

    /// <summary>
    /// Gets the thematic intent.
    /// </summary>
    public ThematicIntent Thematic { get; init; } = ThematicIntent.Default;

    /// <summary>
    /// Gets the number of bars for this section.
    /// </summary>
    public int Bars { get; init; } = 4;

    /// <summary>
    /// Gets the harmonic character.
    /// </summary>
    public HarmonicCharacter HarmonicCharacter { get; init; } = HarmonicCharacter.Stable;

    /// <summary>
    /// Gets the musical character (texture, rhythm, register).
    /// </summary>
    public MusicalCharacter MusicalCharacter { get; init; } = MusicalCharacter.Default;

    /// <summary>
    /// Gets the target tempo as a multiplier (1.0 = base tempo).
    /// </summary>
    public double TempoMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Gets the dynamic level (0 = pianissimo, 1 = fortissimo).
    /// </summary>
    public double DynamicLevel { get; init; } = 0.5;

    /// <summary>
    /// Gets whether this section is a transition.
    /// </summary>
    public bool IsTransition { get; init; }

    /// <summary>
    /// Gets whether this section should avoid strong endings.
    /// </summary>
    public bool AvoidStrongEnding { get; init; }

    /// <summary>
    /// Gets whether this section requires a strong ending.
    /// </summary>
    public bool RequireStrongEnding { get; init; }

    /// <summary>
    /// Gets specific generation hints for the theory engine.
    /// </summary>
    public IReadOnlyDictionary<string, string> Hints { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Creates a default composition intent.
    /// </summary>
    public static CompositionIntent Default => new()
    {
        EmotionalTarget = EmotionalState.Neutral
    };

    /// <summary>
    /// Creates an intent from a narrative phase.
    /// </summary>
    /// <param name="phase">The narrative phase.</param>
    /// <param name="bars">Number of bars.</param>
    /// <returns>A composition intent.</returns>
    public static CompositionIntent FromPhase(NarrativePhase phase, int bars)
    {
        return new CompositionIntent
        {
            EmotionalTarget = phase.EmotionalTarget,
            Bars = bars,
            HarmonicCharacter = phase.HarmonicCharacter,
            MusicalCharacter = phase.MusicalCharacter,
            Harmony = HarmonicIntentFromPhase(phase),
            Melody = MelodicIntentFromPhase(phase),
            Thematic = ThematicIntentFromPhase(phase),
            DynamicLevel = phase.EmotionalTarget.Energy,
            AvoidStrongEnding = phase.AvoidResolution,
            RequireStrongEnding = phase.RequireResolution
        };
    }

    private static HarmonicIntent HarmonicIntentFromPhase(NarrativePhase phase)
    {
        return phase.HarmonicCharacter switch
        {
            HarmonicCharacter.Stable => HarmonicIntent.Stable,
            HarmonicCharacter.Departing => new HarmonicIntent
            {
                AvoidTonic = true,
                HarmonicRhythmDensity = 1.0,
                TargetTensionLevel = 0.5
            },
            HarmonicCharacter.Wandering => HarmonicIntent.Wandering,
            HarmonicCharacter.Building => HarmonicIntent.BuildTension,
            HarmonicCharacter.Climactic => new HarmonicIntent
            {
                AvoidTonic = true,
                EmphasizeDominant = true,
                HarmonicRhythmDensity = 2.0,
                TargetTensionLevel = 0.9,
                EncourageSecondaryDominants = true
            },
            HarmonicCharacter.Resolving => HarmonicIntent.Resolve,
            HarmonicCharacter.Peaceful => new HarmonicIntent
            {
                AvoidTonic = false,
                HarmonicRhythmDensity = 0.5,
                TargetTensionLevel = 0.1,
                EndingCadence = phase.EndingCadence
            },
            _ => HarmonicIntent.Default
        };
    }

    private static MelodicIntent MelodicIntentFromPhase(NarrativePhase phase)
    {
        return new MelodicIntent
        {
            Contour = phase.MusicalCharacter.RegisterHeight > 0.6
                ? MelodicContour.Ascending
                : phase.MusicalCharacter.RegisterHeight < 0.4
                    ? MelodicContour.Descending
                    : MelodicContour.Balanced,
            Range = phase.MusicalCharacter.RegisterHeight > 0.6
                ? MelodicRange.High
                : phase.MusicalCharacter.RegisterHeight < 0.4
                    ? MelodicRange.Low
                    : MelodicRange.Middle,
            RhythmicDensity = phase.MusicalCharacter.RhythmicActivity * 2,
            DissonanceLevel = phase.EmotionalTarget.Tension * 0.5,
            EnergyLevel = phase.EmotionalTarget.Energy,
            AllowSyncopation = phase.MusicalCharacter.RhythmicActivity > 0.6,
            EndOnStable = phase.HarmonicCharacter == HarmonicCharacter.Stable ||
                          phase.HarmonicCharacter == HarmonicCharacter.Resolving ||
                          phase.HarmonicCharacter == HarmonicCharacter.Peaceful
        };
    }

    private static ThematicIntent ThematicIntentFromPhase(NarrativePhase phase)
    {
        if (phase.ThematicGoals.IntroduceMainMotif)
            return ThematicIntent.Introduction;

        if (phase.ThematicGoals.ReturnMainMotif)
            return ThematicIntent.Recapitulation;

        if (phase.ThematicGoals.DevelopMotif)
        {
            var transformType = phase.ThematicGoals.PreferredTransformations.FirstOrDefault();
            return new ThematicIntent
            {
                TransformationType = transformType != default ? transformType : null,
                AllowFragmentation = phase.ThematicGoals.PreferredTransformations
                    .Contains(MotifTransformationType.Fragmentation),
                AllowSecondaryMotif = phase.ThematicGoals.AllowSecondaryMotif,
                TransformationDegree = 0.5
            };
        }

        return ThematicIntent.Default;
    }
}
