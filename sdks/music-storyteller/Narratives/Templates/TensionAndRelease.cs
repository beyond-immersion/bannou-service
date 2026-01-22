using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Narratives.Templates;

/// <summary>
/// The "Tension and Release" narrative template.
/// A universal dramatic arc: Stability → Disturbance → Building → Climax → Release → Peace.
/// Source: Plan Phase 5 - Built-in Templates
/// </summary>
public static class TensionAndRelease
{
    /// <summary>
    /// Gets the template instance.
    /// </summary>
    public static NarrativeTemplate Template { get; } = new()
    {
        Id = "tension_and_release",
        Name = "Tension and Release",
        Description = "The fundamental dramatic arc of music. Builds tension through a climax, " +
            "then releases into peaceful resolution. Works for any genre.",
        Imagery =
        [
            "Calm waters, a quiet beginning",
            "Ripples appear, something stirs beneath",
            "Waves build, momentum gathering",
            "The storm breaks, full intensity",
            "Waters calming, tension dissolving",
            "Still waters again, transformed by the storm"
        ],
        Tags = ["dramatic", "universal", "classical", "cinematic", "emotional"],
        MinimumBars = 32,
        IdealBars = 64,
        SupportsModulation = true,
        Phases =
        [
            // Phase 1: Stability (20%)
            new NarrativePhase
            {
                Name = "Stability",
                RelativeDuration = 0.20,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.2,
                    Brightness = 0.5,
                    Energy = 0.3,
                    Warmth = 0.6,
                    Stability = 0.8,
                    Valence = 0.6
                },
                HarmonicCharacter = HarmonicCharacter.Stable,
                ThematicGoals = ThematicGoals.Introduction,
                MusicalCharacter = MusicalCharacter.Intimate
            },

            // Phase 2: Disturbance (20%)
            new NarrativePhase
            {
                Name = "Disturbance",
                RelativeDuration = 0.20,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.5,
                    Brightness = 0.4,
                    Energy = 0.5,
                    Warmth = 0.4,
                    Stability = 0.5,
                    Valence = 0.4
                },
                HarmonicCharacter = HarmonicCharacter.Departing,
                ThematicGoals = new ThematicGoals
                {
                    DevelopMotif = true,
                    PreferredTransformations =
                    [
                        MotifTransformationType.Sequence,
                        MotifTransformationType.Extension
                    ]
                },
                MusicalCharacter = new MusicalCharacter
                {
                    TexturalDensity = 0.5,
                    RhythmicActivity = 0.5,
                    RegisterHeight = 0.5
                },
                AvoidResolution = true
            },

            // Phase 3: Building (25%)
            new NarrativePhase
            {
                Name = "Building",
                RelativeDuration = 0.25,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.8,
                    Brightness = 0.5,
                    Energy = 0.8,
                    Warmth = 0.3,
                    Stability = 0.3,
                    Valence = 0.4
                },
                HarmonicCharacter = HarmonicCharacter.Building,
                ThematicGoals = new ThematicGoals
                {
                    DevelopMotif = true,
                    PreferredTransformations =
                    [
                        MotifTransformationType.Fragmentation,
                        MotifTransformationType.Sequence
                    ]
                },
                MusicalCharacter = MusicalCharacter.Driving,
                AvoidResolution = true,
                EndingCadence = CadencePreference.Half
            },

            // Phase 4: Climax (10%)
            new NarrativePhase
            {
                Name = "Climax",
                RelativeDuration = 0.10,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.95,
                    Brightness = 0.6,
                    Energy = 0.95,
                    Warmth = 0.2,
                    Stability = 0.2,
                    Valence = 0.5
                },
                HarmonicCharacter = HarmonicCharacter.Climactic,
                ThematicGoals = new ThematicGoals
                {
                    ReturnMainMotif = true,
                    PreferredTransformations =
                    [
                        MotifTransformationType.Augmentation
                    ]
                },
                MusicalCharacter = MusicalCharacter.Climactic
            },

            // Phase 5: Release (15%)
            new NarrativePhase
            {
                Name = "Release",
                RelativeDuration = 0.15,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.3,
                    Brightness = 0.6,
                    Energy = 0.4,
                    Warmth = 0.6,
                    Stability = 0.7,
                    Valence = 0.7
                },
                HarmonicCharacter = HarmonicCharacter.Resolving,
                ThematicGoals = new ThematicGoals
                {
                    ReturnMainMotif = true,
                    PreferredTransformations =
                    [
                        MotifTransformationType.Repetition
                    ]
                },
                MusicalCharacter = new MusicalCharacter
                {
                    TexturalDensity = 0.5,
                    RhythmicActivity = 0.3,
                    RegisterHeight = 0.5
                },
                EndingCadence = CadencePreference.Authentic
            },

            // Phase 6: Peace (10%)
            new NarrativePhase
            {
                Name = "Peace",
                RelativeDuration = 0.10,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.1,
                    Brightness = 0.6,
                    Energy = 0.2,
                    Warmth = 0.8,
                    Stability = 0.9,
                    Valence = 0.8
                },
                HarmonicCharacter = HarmonicCharacter.Peaceful,
                ThematicGoals = ThematicGoals.Recapitulation,
                MusicalCharacter = MusicalCharacter.Peaceful,
                RequireResolution = true,
                EndingCadence = CadencePreference.Plagal
            }
        ]
    };
}
