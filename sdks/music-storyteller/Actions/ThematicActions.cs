using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Actions related to thematic/motivic development.
/// </summary>
public static class ThematicActions
{
    /// <summary>
    /// Introduce the main motif/theme.
    /// </summary>
    public static IMusicalAction IntroduceMainMotif { get; } = new IntroduceMainMotifAction();

    /// <summary>
    /// Return the main motif/theme.
    /// </summary>
    public static IMusicalAction ReturnMainMotif { get; } = new ReturnMainMotifAction();

    /// <summary>
    /// Transform the motif using sequence (transposition).
    /// </summary>
    public static IMusicalAction TransformMotifSequence { get; } = new TransformMotifSequenceAction();

    /// <summary>
    /// Fragment the motif (use only part of it).
    /// </summary>
    public static IMusicalAction FragmentMotif { get; } = new FragmentMotifAction();

    /// <summary>
    /// Extend/develop the motif with new material.
    /// </summary>
    public static IMusicalAction ExtendMotif { get; } = new ExtendMotifAction();

    /// <summary>
    /// Invert the motif (flip intervals).
    /// </summary>
    public static IMusicalAction InvertMotif { get; } = new InvertMotifAction();

    /// <summary>
    /// Augment the motif (stretch rhythmically).
    /// </summary>
    public static IMusicalAction AugmentMotif { get; } = new AugmentMotifAction();

    /// <summary>
    /// Introduce a secondary/contrasting motif.
    /// </summary>
    public static IMusicalAction IntroduceSecondaryMotif { get; } = new IntroduceSecondaryMotifAction();

    /// <summary>
    /// Gets all thematic actions.
    /// </summary>
    public static IEnumerable<IMusicalAction> All
    {
        get
        {
            yield return IntroduceMainMotif;
            yield return ReturnMainMotif;
            yield return TransformMotifSequence;
            yield return FragmentMotif;
            yield return ExtendMotif;
            yield return InvertMotif;
            yield return AugmentMotif;
            yield return IntroduceSecondaryMotif;
        }
    }

    private sealed class IntroduceMainMotifAction : MusicalActionBase
    {
        public override string Id => "introduce_main_motif";
        public override string Name => "Introduce Main Motif";
        public override ActionCategory Category => ActionCategory.Thematic;
        public override string Description => "Present the main thematic material for the first time";
        public override double BaseCost => 0.8;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.StabilityChange(0.1, "Establishing theme creates grounding"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.MainMotifNotIntroduced,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Thematic.Stage = DevelopmentStage.Introduction;
            // Actual motif assignment would happen in the intent generation
        }
    }

    private sealed class ReturnMainMotifAction : MusicalActionBase
    {
        public override string Id => "return_main_motif";
        public override string Name => "Return Main Motif";
        public override ActionCategory Category => ActionCategory.Thematic;
        public override string Description => "Bring back the main theme for unity/recognition";
        public override double BaseCost => 0.7;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.StabilityChange(0.2, "Recognition provides grounding"),
            ActionEffect.WarmthChange(0.1, "Familiarity = comfort"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.MainMotifIntroduced,
            ActionPrecondition.BarsSinceMainMotifAtLeast(4),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);

            if (state.Thematic.MainMotif != null)
            {
                state.Thematic.RecordUsage(
                    state.Thematic.MainMotif.Id,
                    state.Position.Bar,
                    MotifTransformationType.Repetition);
            }

            // Trigger episodic memory for recognition
            state.Mechanisms.TriggerEpisodicMemory(0.4);
        }

        public override double CalculateCost(CompositionState state)
        {
            // Lower cost when theme return is overdue
            if (state.Thematic.ShouldReturnMainMotif(state.Position.Bar))
            {
                return BaseCost * 0.5;
            }

            return BaseCost;
        }
    }

    private sealed class TransformMotifSequenceAction : MusicalActionBase
    {
        public override string Id => "transform_motif_sequence";
        public override string Name => "Sequence Transform";
        public override ActionCategory Category => ActionCategory.Thematic;
        public override string Description => "Present the motif at a different pitch level";
        public override double BaseCost => 0.9;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.1, "Sequence builds momentum"),
            ActionEffect.EnergyChange(0.05, "Sequential motion adds drive"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.MainMotifIntroduced,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Thematic.Stage = DevelopmentStage.Development;

            if (state.Thematic.MainMotif != null)
            {
                state.Thematic.RecordUsage(
                    state.Thematic.MainMotif.Id,
                    state.Position.Bar,
                    MotifTransformationType.Sequence);
            }
        }
    }

    private sealed class FragmentMotifAction : MusicalActionBase
    {
        public override string Id => "fragment_motif";
        public override string Name => "Fragment Motif";
        public override ActionCategory Category => ActionCategory.Thematic;
        public override string Description => "Use only part of the motif for development";
        public override double BaseCost => 1.0;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.1, "Fragmentation suggests instability"),
            ActionEffect.StabilityChange(-0.1, "Incomplete = less stable"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.MainMotifIntroduced,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Thematic.Stage = DevelopmentStage.Development;

            if (state.Thematic.MainMotif != null)
            {
                state.Thematic.RecordUsage(
                    state.Thematic.MainMotif.Id,
                    state.Position.Bar,
                    MotifTransformationType.Fragmentation);
            }
        }
    }

    private sealed class ExtendMotifAction : MusicalActionBase
    {
        public override string Id => "extend_motif";
        public override string Name => "Extend Motif";
        public override ActionCategory Category => ActionCategory.Thematic;
        public override string Description => "Add new material to develop the motif";
        public override double BaseCost => 1.0;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.EnergyChange(0.1, "Extension adds momentum"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.MainMotifIntroduced,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Thematic.Stage = DevelopmentStage.Development;

            if (state.Thematic.MainMotif != null)
            {
                state.Thematic.RecordUsage(
                    state.Thematic.MainMotif.Id,
                    state.Position.Bar,
                    MotifTransformationType.Extension);
            }
        }
    }

    private sealed class InvertMotifAction : MusicalActionBase
    {
        public override string Id => "invert_motif";
        public override string Name => "Invert Motif";
        public override ActionCategory Category => ActionCategory.Thematic;
        public override string Description => "Flip the intervals of the motif";
        public override double BaseCost => 1.1;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.TensionIncrease(0.05, "Inversion creates subtle tension"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.MainMotifIntroduced,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Thematic.Stage = DevelopmentStage.Transformation;

            if (state.Thematic.MainMotif != null)
            {
                state.Thematic.RecordUsage(
                    state.Thematic.MainMotif.Id,
                    state.Position.Bar,
                    MotifTransformationType.Inversion);
            }
        }
    }

    private sealed class AugmentMotifAction : MusicalActionBase
    {
        public override string Id => "augment_motif";
        public override string Name => "Augment Motif";
        public override ActionCategory Category => ActionCategory.Thematic;
        public override string Description => "Stretch the motif rhythmically (longer note values)";
        public override double BaseCost => 1.1;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.EnergyChange(-0.1, "Slower = less energy"),
            ActionEffect.StabilityChange(0.1, "Augmentation often signals grandeur/arrival"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.MainMotifIntroduced,
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.Thematic.Stage = DevelopmentStage.Transformation;

            if (state.Thematic.MainMotif != null)
            {
                state.Thematic.RecordUsage(
                    state.Thematic.MainMotif.Id,
                    state.Position.Bar,
                    MotifTransformationType.Augmentation);
            }
        }
    }

    private sealed class IntroduceSecondaryMotifAction : MusicalActionBase
    {
        public override string Id => "introduce_secondary_motif";
        public override string Name => "Introduce Secondary Motif";
        public override ActionCategory Category => ActionCategory.Thematic;
        public override string Description => "Present a contrasting secondary theme";
        public override double BaseCost => 1.0;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            new ActionEffect("valence", EffectType.Relative, 0.05, "New material provides interest"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            ActionPrecondition.MainMotifIntroduced,
            ActionPrecondition.ProgressAbove(0.2), // Not too early
        ];
    }
}
