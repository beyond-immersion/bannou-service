using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Actions that increase tension/expectation.
/// </summary>
public static class TensionActions
{
    /// <summary>
    /// Use a dominant seventh chord to create expectation of resolution.
    /// </summary>
    public static IMusicalAction UseDominantSeventh { get; } = new DominantSeventhAction();

    /// <summary>
    /// Use a secondary dominant to intensify progression.
    /// </summary>
    public static IMusicalAction UseSecondaryDominant { get; } = new SecondaryDominantAction();

    /// <summary>
    /// Increase harmonic rhythm (more chord changes per bar).
    /// </summary>
    public static IMusicalAction IncreaseHarmonicRhythm { get; } = new IncreaseHarmonicRhythmAction();

    /// <summary>
    /// Use ascending melodic sequence.
    /// </summary>
    public static IMusicalAction AscendingSequence { get; } = new AscendingSequenceAction();

    /// <summary>
    /// Apply chromatic voice leading for tension.
    /// </summary>
    public static IMusicalAction ChromaticVoiceLeading { get; } = new ChromaticVoiceLeadingAction();

    /// <summary>
    /// Delay expected resolution.
    /// </summary>
    public static IMusicalAction DelayResolution { get; } = new DelayResolutionAction();

    /// <summary>
    /// Use diminished seventh for maximum tension.
    /// </summary>
    public static IMusicalAction UseDiminishedSeventh { get; } = new DiminishedSeventhAction();

    /// <summary>
    /// Gets all tension actions.
    /// </summary>
    public static IEnumerable<IMusicalAction> All
    {
        get
        {
            yield return UseDominantSeventh;
            yield return UseSecondaryDominant;
            yield return IncreaseHarmonicRhythm;
            yield return AscendingSequence;
            yield return ChromaticVoiceLeading;
            yield return DelayResolution;
            yield return UseDiminishedSeventh;
        }
    }

    private sealed class DominantSeventhAction : MusicalActionBase
    {
        public override string Id => "use_dominant_seventh";
        public override string Name => "Use Dominant Seventh";
        public override ActionCategory Category => ActionCategory.Tension;
        public override string Description => "Introduce a V7 chord to create expectation of tonic resolution";
        public override double BaseCost => 0.8;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.15, "Lerdahl TPS: V7 has higher tension than V"),
            ActionEffect.StabilityChange(-0.1, "Dominant function creates instability"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.NotOnTonic,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Harmonic.ExpectingCadence = true;
            state.Listener.RegisterTensionEvent();
        }
    }

    private sealed class SecondaryDominantAction : MusicalActionBase
    {
        public override string Id => "use_secondary_dominant";
        public override string Name => "Use Secondary Dominant";
        public override ActionCategory Category => ActionCategory.Tension;
        public override string Description => "Use V/x chord to tonicize a non-tonic chord";
        public override double BaseCost => 1.0;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.2, "Secondary dominants add chromatic tension"),
            ActionEffect.StabilityChange(-0.2, "Temporary tonicization destabilizes"),
            ActionEffect.BrightnessChange(0.05, "Raised leading tone brightens"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.TensionBelow(0.8),
        ];
    }

    private sealed class IncreaseHarmonicRhythmAction : MusicalActionBase
    {
        public override string Id => "increase_harmonic_rhythm";
        public override string Name => "Increase Harmonic Rhythm";
        public override ActionCategory Category => ActionCategory.Tension;
        public override string Description => "More frequent chord changes to build momentum";
        public override double BaseCost => 0.7;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.1, "Faster harmony increases drive"),
            ActionEffect.EnergyChange(0.15, "More activity = more energy"),
        ];
    }

    private sealed class AscendingSequenceAction : MusicalActionBase
    {
        public override string Id => "ascending_sequence";
        public override string Name => "Ascending Sequence";
        public override ActionCategory Category => ActionCategory.Tension;
        public override string Description => "Use ascending melodic/harmonic sequence";
        public override double BaseCost => 0.9;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.1, "Ascending motion builds tension"),
            ActionEffect.EnergyChange(0.1, "Upward motion increases energy"),
            ActionEffect.BrightnessChange(0.05, "Higher register is brighter"),
        ];
    }

    private sealed class ChromaticVoiceLeadingAction : MusicalActionBase
    {
        public override string Id => "chromatic_voice_leading";
        public override string Name => "Chromatic Voice Leading";
        public override ActionCategory Category => ActionCategory.Tension;
        public override string Description => "Apply chromatic motion in one or more voices";
        public override double BaseCost => 0.85;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.1, "Chromatic motion adds tension"),
            new ActionEffect("warmth", EffectType.Relative, -0.05, "Chromatic = less diatonic warmth"),
        ];
    }

    private sealed class DelayResolutionAction : MusicalActionBase
    {
        public override string Id => "delay_resolution";
        public override string Name => "Delay Resolution";
        public override ActionCategory Category => ActionCategory.Tension;
        public override string Description => "Prolong dominant or delay expected resolution";
        public override double BaseCost => 1.1;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.15, "Delayed resolution increases anticipation"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.ExpectingCadence,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            // Maintain the expectation but don't resolve
            state.Listener.SurpriseBudget -= 0.05;
        }
    }

    private sealed class DiminishedSeventhAction : MusicalActionBase
    {
        public override string Id => "use_diminished_seventh";
        public override string Name => "Use Diminished Seventh";
        public override ActionCategory Category => ActionCategory.Tension;
        public override string Description => "Use a fully diminished seventh chord for maximum tension";
        public override double BaseCost => 1.2;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.25, "Diminished seventh = high dissonance"),
            ActionEffect.StabilityChange(-0.25, "Highly unstable chord"),
            ActionEffect.WarmthChange(-0.1, "Dissonance reduces warmth"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.TensionBelow(0.9),
            ActionPrecondition.StabilityAbove(0.2),
        ];
    }
}
