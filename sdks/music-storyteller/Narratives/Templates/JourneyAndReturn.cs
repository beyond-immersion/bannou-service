using BeyondImmersion.Bannou.MusicStoryteller.State;

namespace BeyondImmersion.Bannou.MusicStoryteller.Narratives.Templates;

/// <summary>
/// The "Journey and Return" narrative template.
/// A classic Celtic-style arc: Home → Departure → Adventure → Return.
/// Source: Plan Phase 5 - Built-in Templates
/// </summary>
public static class JourneyAndReturn
{
    /// <summary>
    /// Gets the template instance.
    /// </summary>
    public static NarrativeTemplate Template { get; } = new()
    {
        Id = "journey_and_return",
        Name = "Journey and Return",
        Description = "A classic arc of leaving home, adventuring, and returning transformed. " +
            "Inspired by Celtic music traditions and the hero's journey.",
        Imagery =
        [
            "Morning mist over green hills, a familiar hearth",
            "The road stretches ahead, excitement and uncertainty",
            "Wild landscapes, challenges overcome, wisdom gained",
            "The homestead appears again, changed by the journey"
        ],
        Tags = ["celtic", "journey", "adventure", "folk", "heroic"],
        MinimumBars = 24,
        IdealBars = 48,
        SupportsModulation = true,
        Phases =
        [
            // Phase 1: Home (25%)
            new NarrativePhase
            {
                Name = "Home",
                RelativeDuration = 0.25,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.2,
                    Brightness = 0.6,
                    Energy = 0.4,
                    Warmth = 0.8,
                    Stability = 0.9,
                    Valence = 0.7
                },
                HarmonicCharacter = HarmonicCharacter.Stable,
                ThematicGoals = ThematicGoals.Introduction,
                MusicalCharacter = new MusicalCharacter
                {
                    TexturalDensity = 0.4,
                    RhythmicActivity = 0.4,
                    RegisterHeight = 0.5
                },
                EndingCadence = CadencePreference.Half
            },

            // Phase 2: Departure (25%)
            new NarrativePhase
            {
                Name = "Departure",
                RelativeDuration = 0.25,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.4,
                    Brightness = 0.5,
                    Energy = 0.6,
                    Warmth = 0.5,
                    Stability = 0.5,
                    Valence = 0.5
                },
                HarmonicCharacter = HarmonicCharacter.Departing,
                ThematicGoals = ThematicGoals.Development,
                MusicalCharacter = new MusicalCharacter
                {
                    TexturalDensity = 0.5,
                    RhythmicActivity = 0.6,
                    RegisterHeight = 0.5
                },
                AvoidResolution = true
            },

            // Phase 3: Adventure (25%)
            new NarrativePhase
            {
                Name = "Adventure",
                RelativeDuration = 0.25,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.7,
                    Brightness = 0.4,
                    Energy = 0.8,
                    Warmth = 0.3,
                    Stability = 0.3,
                    Valence = 0.5
                },
                HarmonicCharacter = HarmonicCharacter.Wandering,
                ThematicGoals = new ThematicGoals
                {
                    DevelopMotif = true,
                    AllowSecondaryMotif = true,
                    PreferredTransformations =
                    [
                        MotifTransformationType.Sequence,
                        MotifTransformationType.Fragmentation,
                        MotifTransformationType.Inversion
                    ]
                },
                MusicalCharacter = MusicalCharacter.Driving,
                EndingCadence = CadencePreference.Half
            },

            // Phase 4: Return (25%)
            new NarrativePhase
            {
                Name = "Return",
                RelativeDuration = 0.25,
                EmotionalTarget = new EmotionalState
                {
                    Tension = 0.2,
                    Brightness = 0.7,
                    Energy = 0.5,
                    Warmth = 0.85,
                    Stability = 0.9,
                    Valence = 0.8
                },
                HarmonicCharacter = HarmonicCharacter.Resolving,
                ThematicGoals = ThematicGoals.Recapitulation,
                MusicalCharacter = new MusicalCharacter
                {
                    TexturalDensity = 0.5,
                    RhythmicActivity = 0.4,
                    RegisterHeight = 0.5
                },
                RequireResolution = true,
                EndingCadence = CadencePreference.Authentic
            }
        ]
    };
}
