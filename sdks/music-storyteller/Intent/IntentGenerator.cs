using BeyondImmersion.Bannou.MusicStoryteller.Actions;
using BeyondImmersion.Bannou.MusicStoryteller.Narratives;
using BeyondImmersion.Bannou.MusicStoryteller.Planning;
using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Intent;

/// <summary>
/// Converts planned actions into composition intents
/// that the music-theory engine can execute.
/// Source: Plan Phase 7 - Intent Generation
/// </summary>
public sealed class IntentGenerator
{
    /// <summary>
    /// Generates a composition intent from a single action.
    /// </summary>
    /// <param name="action">The action to convert.</param>
    /// <param name="state">Current composition state.</param>
    /// <param name="phase">Current narrative phase.</param>
    /// <param name="bars">Number of bars for this section.</param>
    /// <returns>A composition intent.</returns>
    public CompositionIntent FromAction(
        IMusicalAction action,
        CompositionState state,
        NarrativePhase phase,
        int bars = 4)
    {
        // Start with phase-based intent
        var baseIntent = CompositionIntent.FromPhase(phase, bars);

        // Overlay action-specific modifications
        return ModifyIntentForAction(baseIntent, action, state);
    }

    /// <summary>
    /// Generates a composition intent from a plan step.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="stepIndex">Current step index.</param>
    /// <param name="state">Current composition state.</param>
    /// <param name="phase">Current narrative phase.</param>
    /// <param name="barsPerStep">Bars per action.</param>
    /// <returns>A composition intent.</returns>
    public CompositionIntent FromPlanStep(
        Plan plan,
        int stepIndex,
        CompositionState state,
        NarrativePhase phase,
        int barsPerStep = 4)
    {
        if (stepIndex >= plan.Actions.Count)
        {
            return CompositionIntent.FromPhase(phase, barsPerStep);
        }

        var action = plan.Actions[stepIndex].MusicalAction;
        var intent = FromAction(action, state, phase, barsPerStep);

        // Adjust for plan position
        var isFirstStep = stepIndex == 0;
        var isLastStep = stepIndex == plan.Actions.Count - 1;

        return intent with
        {
            RequireStrongEnding = isLastStep && phase.RequireResolution,
            AvoidStrongEnding = !isLastStep && phase.AvoidResolution
        };
    }

    /// <summary>
    /// Generates intents for an entire plan.
    /// </summary>
    /// <param name="plan">The plan to convert.</param>
    /// <param name="state">Initial composition state.</param>
    /// <param name="phase">Narrative phase.</param>
    /// <param name="totalBars">Total bars for the plan.</param>
    /// <returns>Sequence of intents.</returns>
    public IEnumerable<CompositionIntent> FromPlan(
        Plan plan,
        CompositionState state,
        NarrativePhase phase,
        int totalBars)
    {
        if (plan.Actions.Count == 0)
        {
            yield return CompositionIntent.FromPhase(phase, totalBars);
            yield break;
        }

        var barsPerAction = totalBars / plan.Actions.Count;
        var remainingBars = totalBars - (barsPerAction * plan.Actions.Count);
        var currentState = state.Clone();

        for (var i = 0; i < plan.Actions.Count; i++)
        {
            var action = plan.Actions[i].MusicalAction;

            // Give extra bars to the last action
            var bars = i == plan.Actions.Count - 1
                ? barsPerAction + remainingBars
                : barsPerAction;

            var intent = FromAction(action, currentState, phase, bars);

            // Mark last intent as requiring strong ending if phase does
            if (i == plan.Actions.Count - 1 && phase.RequireResolution)
            {
                intent = intent with { RequireStrongEnding = true };
            }

            yield return intent;

            // Apply action to state for next iteration
            action.Apply(currentState);
        }
    }

    /// <summary>
    /// Generates intents for a transition between phases.
    /// </summary>
    /// <param name="transition">The transition plan.</param>
    /// <param name="state">Current composition state.</param>
    /// <returns>Transition intents.</returns>
    public IEnumerable<CompositionIntent> FromTransition(
        TransitionPlan transition,
        CompositionState state)
    {
        foreach (var step in transition.Steps)
        {
            var harmony = DetermineTransitionHarmony(step);
            var melody = DetermineTransitionMelody(step);

            yield return new CompositionIntent
            {
                EmotionalTarget = step.EmotionalTarget,
                Bars = 1,
                HarmonicCharacter = step.HarmonicCharacter,
                Harmony = harmony,
                Melody = melody,
                IsTransition = true,
                RequireStrongEnding = step.Progress >= 1.0 &&
                    step.SuggestedCadence == CadencePreference.Authentic
            };
        }
    }

    private CompositionIntent ModifyIntentForAction(
        CompositionIntent baseIntent,
        IMusicalAction action,
        CompositionState state)
    {
        var harmony = baseIntent.Harmony;
        var melody = baseIntent.Melody;
        var thematic = baseIntent.Thematic;
        var hints = new Dictionary<string, string>(baseIntent.Hints);

        // Apply action-specific modifications based on category
        switch (action.Category)
        {
            case ActionCategory.Tension:
                harmony = harmony with
                {
                    AvoidTonic = true,
                    EncourageSecondaryDominants = true,
                    TargetTensionLevel = Math.Min(1.0, harmony.TargetTensionLevel + 0.2)
                };
                melody = melody with
                {
                    Contour = MelodicContour.Ascending,
                    DissonanceLevel = Math.Min(1.0, melody.DissonanceLevel + 0.1)
                };
                break;

            case ActionCategory.Resolution:
                harmony = harmony with
                {
                    AvoidTonic = false,
                    TargetTensionLevel = Math.Max(0, harmony.TargetTensionLevel - 0.3),
                    EndingCadence = DetermineCadence(action)
                };
                melody = melody with
                {
                    Contour = MelodicContour.Descending,
                    EndOnStable = true
                };
                hints["cadence_type"] = action.Id;
                break;

            case ActionCategory.Color:
                harmony = harmony with
                {
                    AllowModalInterchange = true,
                    PreferChromaticVoiceLeading = true
                };
                hints["color_action"] = action.Id;
                break;

            case ActionCategory.Thematic:
                thematic = DetermineThematicIntent(action, state);
                hints["thematic_action"] = action.Id;
                break;

            case ActionCategory.Texture:
                // Texture primarily affects musical character
                var textureModifier = DetermineTextureModifier(action);
                hints["texture_action"] = action.Id;
                break;
        }

        return baseIntent with
        {
            Harmony = harmony,
            Melody = melody,
            Thematic = thematic,
            Hints = hints
        };
    }

    private CadencePreference DetermineCadence(IMusicalAction action)
    {
        return action.Id switch
        {
            "authentic_cadence" => CadencePreference.Authentic,
            "plagal_cadence" => CadencePreference.Plagal,
            "half_cadence" => CadencePreference.Half,
            "deceptive_cadence" => CadencePreference.Deceptive,
            _ => CadencePreference.None
        };
    }

    private ThematicIntent DetermineThematicIntent(IMusicalAction action, CompositionState state)
    {
        return action.Id switch
        {
            "introduce_main_motif" => ThematicIntent.Introduction,
            "return_main_motif" => ThematicIntent.Recapitulation,
            "transform_motif_sequence" => ThematicIntent.Transform(MotifTransformationType.Sequence),
            "fragment_motif" => ThematicIntent.Transform(MotifTransformationType.Fragmentation),
            "extend_motif" => ThematicIntent.Transform(MotifTransformationType.Extension),
            "invert_motif" => ThematicIntent.Transform(MotifTransformationType.Inversion),
            "augment_motif" => ThematicIntent.Transform(MotifTransformationType.Augmentation),
            "introduce_secondary_motif" => new ThematicIntent
            {
                AllowSecondaryMotif = true,
                IntroduceMainMotif = false
            },
            _ => ThematicIntent.Default
        };
    }

    private double DetermineTextureModifier(IMusicalAction action)
    {
        return action.Id switch
        {
            "raise_register" => 0.15,
            "lower_register" => -0.15,
            "increase_density" => 0.2,
            "decrease_density" => -0.2,
            "thin_to_melody" => -0.5,
            "build_to_full" => 0.5,
            "add_rhythmic_drive" => 0.2,
            "relax_rhythmic_activity" => -0.2,
            _ => 0
        };
    }

    private HarmonicIntent DetermineTransitionHarmony(TransitionStep step)
    {
        return step.HarmonicCharacter switch
        {
            HarmonicCharacter.Resolving => HarmonicIntent.Resolve with
            {
                EndingCadence = step.SuggestedCadence
            },
            HarmonicCharacter.Building => HarmonicIntent.BuildTension,
            HarmonicCharacter.Stable => HarmonicIntent.Stable,
            _ => HarmonicIntent.Default with
            {
                TargetTensionLevel = step.EmotionalTarget.Tension
            }
        };
    }

    private MelodicIntent DetermineTransitionMelody(TransitionStep step)
    {
        return new MelodicIntent
        {
            Contour = step.Progress < 0.5 ? MelodicContour.Balanced : MelodicContour.Descending,
            EnergyLevel = step.EmotionalTarget.Energy,
            DissonanceLevel = step.EmotionalTarget.Tension * 0.3,
            EndOnStable = step.Progress >= 1.0
        };
    }
}
