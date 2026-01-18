using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Actions;

/// <summary>
/// Actions that change harmonic color (mode, modulation, borrowed chords).
/// </summary>
public static class ColorActions
{
    /// <summary>
    /// Borrow the flat-VI chord from parallel minor (modal interchange).
    /// </summary>
    public static IMusicalAction ModalInterchangeBVI { get; } = new ModalInterchangeBVIAction();

    /// <summary>
    /// Move to relative minor key area.
    /// </summary>
    public static IMusicalAction MoveToRelativeMinor { get; } = new MoveToRelativeMinorAction();

    /// <summary>
    /// Move to relative major key area.
    /// </summary>
    public static IMusicalAction MoveToRelativeMajor { get; } = new MoveToRelativeMajorAction();

    /// <summary>
    /// Apply a Picardy third (major I in minor key).
    /// </summary>
    public static IMusicalAction PicardyThird { get; } = new PicardyThirdAction();

    /// <summary>
    /// Modulate to the dominant key.
    /// </summary>
    public static IMusicalAction ModulateToDominant { get; } = new ModulateToDominantAction();

    /// <summary>
    /// Modulate to the subdominant key.
    /// </summary>
    public static IMusicalAction ModulateToSubdominant { get; } = new ModulateToSubdominantAction();

    /// <summary>
    /// Switch to Dorian mode.
    /// </summary>
    public static IMusicalAction SwitchToDorian { get; } = new SwitchToDorianAction();

    /// <summary>
    /// Switch to Mixolydian mode.
    /// </summary>
    public static IMusicalAction SwitchToMixolydian { get; } = new SwitchToMixolydianAction();

    /// <summary>
    /// Gets all color actions.
    /// </summary>
    public static IEnumerable<IMusicalAction> All
    {
        get
        {
            yield return ModalInterchangeBVI;
            yield return MoveToRelativeMinor;
            yield return MoveToRelativeMajor;
            yield return PicardyThird;
            yield return ModulateToDominant;
            yield return ModulateToSubdominant;
            yield return SwitchToDorian;
            yield return SwitchToMixolydian;
        }
    }

    private sealed class ModalInterchangeBVIAction : MusicalActionBase
    {
        public override string Id => "modal_interchange_bVI";
        public override string Name => "Modal Interchange bVI";
        public override ActionCategory Category => ActionCategory.Color;
        public override string Description => "Borrow flat-VI from parallel minor for darker color";
        public override double BaseCost => 1.0;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(-0.2, "Borrowed chord darkens harmony"),
            ActionEffect.TensionIncrease(0.05, "Chromatic surprise"),
            new ActionEffect("valence", EffectType.Relative, -0.1, "Minor borrowing = more melancholic"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("In major mode",
                s => s.PitchSpace.Mode == ModeType.Major || s.PitchSpace.Mode == ModeType.Mixolydian),
        ];
    }

    private sealed class MoveToRelativeMinorAction : MusicalActionBase
    {
        public override string Id => "move_to_relative_minor";
        public override string Name => "Move to Relative Minor";
        public override ActionCategory Category => ActionCategory.Color;
        public override string Description => "Tonicize or modulate to the relative minor";
        public override double BaseCost => 1.1;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(-0.3, "Minor mode is darker"),
            ActionEffect.TensionIncrease(0.1, "Key change creates tension"),
            new ActionEffect("valence", EffectType.Relative, -0.15, "Minor = more serious/sad"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("In major mode", s => s.PitchSpace.Mode == ModeType.Major),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            // Enter tonicization of vi
            var newRoot = (int)state.Harmonic.CurrentKey.Root + 9; // Minor 6th up = relative minor
            state.PitchSpace.EnterTonicization((MusicTheory.Pitch.PitchClass)(newRoot % 12));
        }
    }

    private sealed class MoveToRelativeMajorAction : MusicalActionBase
    {
        public override string Id => "move_to_relative_major";
        public override string Name => "Move to Relative Major";
        public override ActionCategory Category => ActionCategory.Color;
        public override string Description => "Tonicize or modulate to the relative major";
        public override double BaseCost => 1.1;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(0.3, "Major mode is brighter"),
            ActionEffect.TensionIncrease(0.1, "Key change creates tension"),
            new ActionEffect("valence", EffectType.Relative, 0.15, "Major = more positive"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("In minor mode", s => s.PitchSpace.Mode == ModeType.Minor),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            // Enter tonicization of III (relative major)
            var newRoot = (int)state.Harmonic.CurrentKey.Root + 3; // Minor 3rd up = relative major
            state.PitchSpace.EnterTonicization((MusicTheory.Pitch.PitchClass)(newRoot % 12));
        }
    }

    private sealed class PicardyThirdAction : MusicalActionBase
    {
        public override string Id => "picardy_third";
        public override string Name => "Picardy Third";
        public override ActionCategory Category => ActionCategory.Color;
        public override string Description => "End a minor piece with a major I chord";
        public override double BaseCost => 1.2;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(0.3, "Major third is brighter"),
            new ActionEffect("valence", EffectType.Relative, 0.2, "Unexpected brightness"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("In minor mode", s => s.PitchSpace.Mode == ModeType.Minor),
            ActionPrecondition.ProgressAbove(0.9), // Near the end
        ];
    }

    private sealed class ModulateToDominantAction : MusicalActionBase
    {
        public override string Id => "modulate_to_dominant";
        public override string Name => "Modulate to Dominant Key";
        public override ActionCategory Category => ActionCategory.Color;
        public override string Description => "Modulate to the key a fifth higher";
        public override double BaseCost => 1.3;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(0.1, "Sharp direction is brighter"),
            ActionEffect.TensionIncrease(0.15, "New key area creates drive"),
            ActionEffect.StabilityChange(-0.15, "Away from home key"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("Not already modulated far",
                s => Math.Abs(s.Harmonic.KeyDistance) < 2),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            var newRoot = ((int)state.Harmonic.CurrentKey.Root + 7) % 12;
            var newKey = new Scale((MusicTheory.Pitch.PitchClass)newRoot, ModeType.Major);
            state.Harmonic.Modulate(newKey);
        }
    }

    private sealed class ModulateToSubdominantAction : MusicalActionBase
    {
        public override string Id => "modulate_to_subdominant";
        public override string Name => "Modulate to Subdominant Key";
        public override ActionCategory Category => ActionCategory.Color;
        public override string Description => "Modulate to the key a fifth lower";
        public override double BaseCost => 1.3;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(-0.1, "Flat direction is warmer/darker"),
            ActionEffect.WarmthChange(0.1, "Subdominant = warmer"),
            ActionEffect.StabilityChange(-0.1, "Away from home key"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("Not already modulated far",
                s => Math.Abs(s.Harmonic.KeyDistance) < 2),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            var newRoot = ((int)state.Harmonic.CurrentKey.Root + 5) % 12; // Perfect fourth up = fifth down
            var newKey = new Scale((MusicTheory.Pitch.PitchClass)newRoot, ModeType.Major);
            state.Harmonic.Modulate(newKey);
        }
    }

    private sealed class SwitchToDorianAction : MusicalActionBase
    {
        public override string Id => "switch_to_dorian";
        public override string Name => "Switch to Dorian Mode";
        public override ActionCategory Category => ActionCategory.Color;
        public override string Description => "Change to Dorian mode for Celtic/folk color";
        public override double BaseCost => 1.1;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(-0.15, "Minor third but raised 6th"),
            ActionEffect.WarmthChange(0.1, "Dorian has warm quality"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("Not already Dorian", s => s.PitchSpace.Mode != ModeType.Dorian),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.PitchSpace.Mode = ModeType.Dorian;
        }
    }

    private sealed class SwitchToMixolydianAction : MusicalActionBase
    {
        public override string Id => "switch_to_mixolydian";
        public override string Name => "Switch to Mixolydian Mode";
        public override ActionCategory Category => ActionCategory.Color;
        public override string Description => "Change to Mixolydian for dominant-feeling color";
        public override double BaseCost => 1.1;

        public override IReadOnlyList<ActionEffect> Effects =>
        [
            ActionEffect.BrightnessChange(0.1, "Major but with flat 7"),
            ActionEffect.TensionIncrease(0.05, "Built-in dominant quality"),
        ];

        public override IReadOnlyList<ActionPrecondition> Preconditions =>
        [
            new ActionPrecondition("Not already Mixolydian", s => s.PitchSpace.Mode != ModeType.Mixolydian),
        ];

        public override void Apply(CompositionState state)
        {
            base.Apply(state);
            state.PitchSpace.Mode = ModeType.Mixolydian;
        }
    }
}
