using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Actions that provide resolution/release.
/// </summary>
public static class ResolutionActions
{
    /// <summary>
    /// Authentic cadence (V-I) - strongest resolution.
    /// </summary>
    public static IMusicalAction AuthenticCadence { get; } = new AuthenticCadenceAction();

    /// <summary>
    /// Plagal cadence (IV-I) - gentler "amen" resolution.
    /// </summary>
    public static IMusicalAction PlagalCadence { get; } = new PlagalCadenceAction();

    /// <summary>
    /// Half cadence (ending on V) - creates more tension.
    /// </summary>
    public static IMusicalAction HalfCadence { get; } = new HalfCadenceAction();

    /// <summary>
    /// Deceptive cadence (V-vi) - surprise, tension maintained.
    /// </summary>
    public static IMusicalAction DeceptiveCadence { get; } = new DeceptiveCadenceAction();

    /// <summary>
    /// Move to tonic chord.
    /// </summary>
    public static IMusicalAction ResolveToTonic { get; } = new ResolveToTonicAction();

    /// <summary>
    /// Return to home key (after modulation).
    /// </summary>
    public static IMusicalAction ReturnToHomeKey { get; } = new ReturnToHomeKeyAction();

    /// <summary>
    /// Gets all resolution actions.
    /// </summary>
    public static IEnumerable<IMusicalAction> All
    {
        get
        {
            yield return AuthenticCadence;
            yield return PlagalCadence;
            yield return HalfCadence;
            yield return DeceptiveCadence;
            yield return ResolveToTonic;
            yield return ReturnToHomeKey;
        }
    }

    private sealed class AuthenticCadenceAction : MusicalActionBase
    {
        public override string Id => "authentic_cadence";
        public override string Name => "Authentic Cadence";
        public override ActionCategory Category => ActionCategory.Resolution;
        public override string Description => "V-I cadence for strong resolution";
        public override double BaseCost => 0.7;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionDecrease(0.4, "Strong resolution releases tension"),
            ActionEffect.StabilityChange(0.5, "Returns to tonic stability"),
            ActionEffect.ContrastiveValence("Huron: resolution pleasure scales with prior tension"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.Any(
                ActionPrecondition.OnDominant,
                ActionPrecondition.ExpectingCadence),
        ];

        public override void Apply(CompositionState state)
        {
            var priorTension = state.Emotional.Tension;
            base.Apply(state);

            // Process the resolution in listener model
            state.Listener.ProcessEvent(0.1, true, priorTension);
            state.Harmonic.ExpectingCadence = false;
            state.Harmonic.BarsSinceTonic = 0;
        }

        public override double CalculateCost(CompositionState state)
        {
            // Lower cost when cadence is expected
            return state.Harmonic.ExpectingCadence ? BaseCost * 0.5 : BaseCost;
        }
    }

    private sealed class PlagalCadenceAction : MusicalActionBase
    {
        public override string Id => "plagal_cadence";
        public override string Name => "Plagal Cadence";
        public override ActionCategory Category => ActionCategory.Resolution;
        public override string Description => "IV-I cadence for gentle resolution";
        public override double BaseCost => 0.8;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionDecrease(0.2, "Softer resolution than authentic"),
            ActionEffect.StabilityChange(0.3, "Returns to tonic"),
            ActionEffect.WarmthChange(0.1, "Plagal has warm, hymn-like quality"),
        ];

        public override void Apply(CompositionState state)
        {
            var priorTension = state.Emotional.Tension;
            base.Apply(state);
            state.Listener.ProcessEvent(0.15, true, priorTension);
            state.Harmonic.BarsSinceTonic = 0;
        }
    }

    private sealed class HalfCadenceAction : MusicalActionBase
    {
        public override string Id => "half_cadence";
        public override string Name => "Half Cadence";
        public override ActionCategory Category => ActionCategory.Resolution; // Actually creates tension
        public override string Description => "End phrase on V (dominant) - creates expectation";
        public override double BaseCost => 0.9;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            // Half cadence actually increases tension!
            ActionEffect.TensionIncrease(0.1, "Ends on dominant, creates anticipation"),
            ActionEffect.StabilityChange(-0.1, "Dominant is less stable than tonic"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.AtPhraseBoundary,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Harmonic.ExpectingCadence = true;
            state.Listener.RegisterTensionEvent();
        }
    }

    private sealed class DeceptiveCadenceAction : MusicalActionBase
    {
        public override string Id => "deceptive_cadence";
        public override string Name => "Deceptive Cadence";
        public override ActionCategory Category => ActionCategory.Resolution;
        public override string Description => "V-vi cadence - surprise, tension not fully resolved";
        public override double BaseCost => 1.0;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.05, "Expectation violated, some tension remains"),
            new ActionEffect("valence", EffectType.Relative, 0.1, "Surprise can be pleasant"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.OnDominant,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            // Surprise - expectation was not met
            state.Listener.ProcessEvent(0.6, false, state.Emotional.Tension);
            state.Harmonic.ExpectingCadence = true; // Still expecting resolution
        }
    }

    private sealed class ResolveToTonicAction : MusicalActionBase
    {
        public override string Id => "resolve_to_tonic";
        public override string Name => "Resolve to Tonic";
        public override ActionCategory Category => ActionCategory.Resolution;
        public override string Description => "Move to tonic chord (without full cadence)";
        public override double BaseCost => 0.75;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionDecrease(0.25, "Return to home base"),
            ActionEffect.StabilityChange(0.35, "Tonic is stable"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.NotOnTonic,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Harmonic.BarsSinceTonic = 0;
        }
    }

    private sealed class ReturnToHomeKeyAction : MusicalActionBase
    {
        public override string Id => "return_to_home_key";
        public override string Name => "Return to Home Key";
        public override ActionCategory Category => ActionCategory.Resolution;
        public override string Description => "Modulate back to the original key";
        public override double BaseCost => 1.2;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionDecrease(0.2, "Return to familiar key"),
            ActionEffect.StabilityChange(0.3, "Home key is most stable"),
            ActionEffect.WarmthChange(0.1, "Homecoming warmth"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("Not in home key", s => s.Harmonic.IsModulating || s.Harmonic.KeyDistance != 0),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Harmonic.ReturnHome();
            state.PitchSpace.Modulate(state.Harmonic.HomeKey.Root, state.Harmonic.HomeKey.Mode);

            // Episodic memory trigger - recognition of return
            state.Mechanisms.TriggerEpisodicMemory(0.3);
        }
    }
}
