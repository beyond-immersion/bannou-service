using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Narratives.Templates;

/// <summary>
/// The "Simple Arc" narrative template.
/// A basic three-part structure: Introduction → Development → Conclusion.
/// Suitable for short pieces or when simplicity is desired.
/// Source: Plan Phase 5 - Built-in Templates
/// </summary>
public static class SimpleArc
{
    /// <summary>
    /// Gets the template instance.
    /// </summary>
    public static NarrativeTemplate Template { get; } = new()
    {
        Id = "simple_arc",
        Name = "Simple Arc",
        Description = "A straightforward three-part structure for short pieces. " +
                      "Establishes, develops, and resolves without complex dramatic turns.",
        Imagery =
        [
            "A clear statement, setting the scene",
            "Exploration and variation, staying close to home",
            "A satisfying return and gentle close"
        ],
        Tags = ["simple", "short", "ambient", "background", "minimal"],
        MinimumBars = 8,
        IdealBars = 16,
        SupportsModulation = false,
        Phases =
        [
            // Phase 1: Introduction (30%)
            new NarrativePhase
            {
                Name = "Introduction",
                RelativeDuration = 0.30,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.2,
                    Brightness = 0.5,
                    Energy = 0.4,
                    Warmth = 0.6,
                    Stability = 0.8,
                    Valence = 0.6
                },
                HarmonicCharacter = HarmonicCharacter.Stable,
                ThematicGoals = ThematicGoals.Introduction,
                MusicalCharacter = new MusicalCharacter
                {
                    TexturalDensity = 0.4,
                    RhythmicActivity = 0.4,
                    RegisterHeight = 0.5
                }
            },

            // Phase 2: Development (40%)
            new NarrativePhase
            {
                Name = "Development",
                RelativeDuration = 0.40,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.4,
                    Brightness = 0.5,
                    Energy = 0.5,
                    Warmth = 0.5,
                    Stability = 0.6,
                    Valence = 0.5
                },
                HarmonicCharacter = HarmonicCharacter.Departing,
                ThematicGoals = new ThematicGoals
                {
                    DevelopMotif = true,
                    PreferredTransformations =
                    [
                        MotifTransformationType.Sequence,
                        MotifTransformationType.Repetition
                    ]
                },
                MusicalCharacter = new MusicalCharacter
                {
                    TexturalDensity = 0.5,
                    RhythmicActivity = 0.5,
                    RegisterHeight = 0.5
                },
                EndingCadence = CadencePreference.Half
            },

            // Phase 3: Conclusion (30%)
            new NarrativePhase
            {
                Name = "Conclusion",
                RelativeDuration = 0.30,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.15,
                    Brightness = 0.55,
                    Energy = 0.35,
                    Warmth = 0.65,
                    Stability = 0.85,
                    Valence = 0.65
                },
                HarmonicCharacter = HarmonicCharacter.Resolving,
                ThematicGoals = ThematicGoals.Recapitulation,
                MusicalCharacter = new MusicalCharacter
                {
                    TexturalDensity = 0.4,
                    RhythmicActivity = 0.35,
                    RegisterHeight = 0.5
                },
                RequireResolution = true,
                EndingCadence = CadencePreference.Authentic
            }
        ]
    };
}
