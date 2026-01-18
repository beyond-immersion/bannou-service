using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Actions that change texture, register, and density.
/// </summary>
public static class TextureActions
{
    /// <summary>
    /// Move to a higher register.
    /// </summary>
    public static IMusicalAction RaiseRegister { get; } = new RaiseRegisterAction();

    /// <summary>
    /// Move to a lower register.
    /// </summary>
    public static IMusicalAction LowerRegister { get; } = new LowerRegisterAction();

    /// <summary>
    /// Increase textural density (more voices/activity).
    /// </summary>
    public static IMusicalAction IncreaseDensity { get; } = new IncreaseDensityAction();

    /// <summary>
    /// Decrease textural density (fewer voices/activity).
    /// </summary>
    public static IMusicalAction DecreaseDensity { get; } = new DecreaseDensityAction();

    /// <summary>
    /// Thin texture to a single voice.
    /// </summary>
    public static IMusicalAction ThinToMelody { get; } = new ThinToMelodyAction();

    /// <summary>
    /// Build to full texture.
    /// </summary>
    public static IMusicalAction BuildToFull { get; } = new BuildToFullAction();

    /// <summary>
    /// Add rhythmic drive/momentum.
    /// </summary>
    public static IMusicalAction AddRhythmicDrive { get; } = new AddRhythmicDriveAction();

    /// <summary>
    /// Relax rhythmic activity.
    /// </summary>
    public static IMusicalAction RelaxRhythmicActivity { get; } = new RelaxRhythmicActivityAction();

    /// <summary>
    /// Gets all texture actions.
    /// </summary>
    public static IEnumerable<IMusicalAction> All
    {
        get
        {
            yield return RaiseRegister;
            yield return LowerRegister;
            yield return IncreaseDensity;
            yield return DecreaseDensity;
            yield return ThinToMelody;
            yield return BuildToFull;
            yield return AddRhythmicDrive;
            yield return RelaxRhythmicActivity;
        }
    }

    private sealed class RaiseRegisterAction : MusicalActionBase
    {
        public override string Id => "raise_register";
        public override string Name => "Raise Register";
        public override ActionCategory Category => ActionCategory.Texture;
        public override string Description => "Move the melody/texture to a higher range";
        public override double BaseCost => 0.8;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(0.15, "Higher register is brighter"),
            ActionEffect.TensionIncrease(0.1, "Ascending = building"),
            ActionEffect.EnergyChange(0.05, "Higher pitch = more energy"),
        ];
    }

    private sealed class LowerRegisterAction : MusicalActionBase
    {
        public override string Id => "lower_register";
        public override string Name => "Lower Register";
        public override ActionCategory Category => ActionCategory.Texture;
        public override string Description => "Move the melody/texture to a lower range";
        public override double BaseCost => 0.8;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(-0.15, "Lower register is darker"),
            ActionEffect.WarmthChange(0.1, "Lower = warmer, fuller"),
            ActionEffect.EnergyChange(-0.05, "Lower = more grounded"),
        ];
    }

    private sealed class IncreaseDensityAction : MusicalActionBase
    {
        public override string Id => "increase_density";
        public override string Name => "Increase Density";
        public override ActionCategory Category => ActionCategory.Texture;
        public override string Description => "Add more voices or activity to the texture";
        public override double BaseCost => 0.9;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.EnergyChange(0.15, "More activity = more energy"),
            ActionEffect.TensionIncrease(0.1, "Denser texture builds intensity"),
            ActionEffect.WarmthChange(0.05, "Fuller sound is warmer"),
        ];
    }

    private sealed class DecreaseDensityAction : MusicalActionBase
    {
        public override string Id => "decrease_density";
        public override string Name => "Decrease Density";
        public override ActionCategory Category => ActionCategory.Texture;
        public override string Description => "Reduce voices or activity in the texture";
        public override double BaseCost => 0.9;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.EnergyChange(-0.15, "Less activity = less energy"),
            ActionEffect.TensionDecrease(0.1, "Thinner texture = more relaxed"),
        ];
    }

    private sealed class ThinToMelodyAction : MusicalActionBase
    {
        public override string Id => "thin_to_melody";
        public override string Name => "Thin to Melody";
        public override ActionCategory Category => ActionCategory.Texture;
        public override string Description => "Reduce to a single melodic line";
        public override double BaseCost => 1.0;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.EnergyChange(-0.2, "Single line is minimal energy"),
            ActionEffect.TensionDecrease(0.15, "Simplicity brings clarity"),
            new ActionEffect("valence", EffectType.Relative, 0.05, "Intimacy of solo"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("Energy above minimum", s => s.Emotional.Energy > 0.3),
        ];
    }

    private sealed class BuildToFullAction : MusicalActionBase
    {
        public override string Id => "build_to_full";
        public override string Name => "Build to Full Texture";
        public override ActionCategory Category => ActionCategory.Texture;
        public override string Description => "Gradually build to full orchestration/texture";
        public override double BaseCost => 1.0;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.EnergyChange(0.25, "Full texture = high energy"),
            ActionEffect.TensionIncrease(0.15, "Building creates anticipation"),
            ActionEffect.WarmthChange(0.1, "Rich texture is warm"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("Energy below maximum", s => s.Emotional.Energy < 0.8),
        ];
    }

    private sealed class AddRhythmicDriveAction : MusicalActionBase
    {
        public override string Id => "add_rhythmic_drive";
        public override string Name => "Add Rhythmic Drive";
        public override ActionCategory Category => ActionCategory.Texture;
        public override string Description => "Increase rhythmic activity and momentum";
        public override double BaseCost => 0.85;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.EnergyChange(0.2, "Rhythm adds energy"),
            ActionEffect.TensionIncrease(0.1, "Rhythmic drive builds intensity"),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            // Increase rhythmic entrainment in BRECVEMA
            state.Mechanisms.RhythmicEntrainment = Math.Min(1.0, state.Mechanisms.RhythmicEntrainment + 0.2);
        }
    }

    private sealed class RelaxRhythmicActivityAction : MusicalActionBase
    {
        public override string Id => "relax_rhythmic_activity";
        public override string Name => "Relax Rhythmic Activity";
        public override ActionCategory Category => ActionCategory.Texture;
        public override string Description => "Slow down rhythmic activity";
        public override double BaseCost => 0.85;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.EnergyChange(-0.15, "Less rhythm = less energy"),
            ActionEffect.TensionDecrease(0.1, "Relaxed rhythm calms"),
            ActionEffect.StabilityChange(0.1, "Steadier = more stable"),
        ];
    }
}
