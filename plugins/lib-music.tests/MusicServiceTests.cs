using BeyondImmersion.BannouService.Music;
using BeyondImmersion.BannouService.TestUtilities;
using SdkBuiltInStyles = BeyondImmersion.Bannou.MusicTheory.Style.BuiltInStyles;
using SdkChord = BeyondImmersion.Bannou.MusicTheory.Collections.Chord;
using SdkChordQuality = BeyondImmersion.Bannou.MusicTheory.Collections.ChordQuality;
using SdkContourShape = BeyondImmersion.Bannou.MusicTheory.Melody.ContourShape;
using SdkInterval = BeyondImmersion.Bannou.MusicTheory.Pitch.Interval;
using SdkMelodyGenerator = BeyondImmersion.Bannou.MusicTheory.Melody.MelodyGenerator;
using SdkMelodyNote = BeyondImmersion.Bannou.MusicTheory.Melody.MelodyNote;
using SdkMelodyOptions = BeyondImmersion.Bannou.MusicTheory.Melody.MelodyOptions;
using SdkMeter = BeyondImmersion.Bannou.MusicTheory.Time.Meter;
using SdkMidiEvent = BeyondImmersion.Bannou.MusicTheory.Output.MidiEvent;
using SdkMidiEventType = BeyondImmersion.Bannou.MusicTheory.Output.MidiEventType;
using SdkMidiJson = BeyondImmersion.Bannou.MusicTheory.Output.MidiJson;
using SdkMidiJsonRenderer = BeyondImmersion.Bannou.MusicTheory.Output.MidiJsonRenderer;
using SdkMidiTrack = BeyondImmersion.Bannou.MusicTheory.Output.MidiTrack;
using SdkModeDistribution = BeyondImmersion.Bannou.MusicTheory.Style.ModeDistribution;
using SdkModeType = BeyondImmersion.Bannou.MusicTheory.Collections.ModeType;
using SdkPitch = BeyondImmersion.Bannou.MusicTheory.Pitch.Pitch;
// SDK types - fully qualified aliases to avoid collision with generated API models
using SdkPitchClass = BeyondImmersion.Bannou.MusicTheory.Pitch.PitchClass;
using SdkPitchRange = BeyondImmersion.Bannou.MusicTheory.Pitch.PitchRange;
using SdkProgressionChord = BeyondImmersion.Bannou.MusicTheory.Harmony.ProgressionChord;
using SdkProgressionGenerator = BeyondImmersion.Bannou.MusicTheory.Harmony.ProgressionGenerator;
using SdkScale = BeyondImmersion.Bannou.MusicTheory.Collections.Scale;
using SdkStyleDefinition = BeyondImmersion.Bannou.MusicTheory.Style.StyleDefinition;
using SdkStyleLoader = BeyondImmersion.Bannou.MusicTheory.Style.StyleLoader;
using SdkVoiceLeader = BeyondImmersion.Bannou.MusicTheory.Harmony.VoiceLeader;
using SdkVoiceLeadingRules = BeyondImmersion.Bannou.MusicTheory.Harmony.VoiceLeadingRules;
using SdkVoiceLeadingViolationType = BeyondImmersion.Bannou.MusicTheory.Harmony.VoiceLeadingViolationType;
using SdkVoicing = BeyondImmersion.Bannou.MusicTheory.Collections.Voicing;
// Time module
using SdkDuration = BeyondImmersion.Bannou.MusicTheory.Time.Duration;
using SdkDurationValue = BeyondImmersion.Bannou.MusicTheory.Time.DurationValue;
using SdkRhythmPattern = BeyondImmersion.Bannou.MusicTheory.Time.RhythmPattern;
using SdkRhythmPatternSet = BeyondImmersion.Bannou.MusicTheory.Time.RhythmPatternSet;
// Structure module
using SdkForm = BeyondImmersion.Bannou.MusicTheory.Structure.Form;
using SdkSection = BeyondImmersion.Bannou.MusicTheory.Structure.Section;
using SdkExpandedForm = BeyondImmersion.Bannou.MusicTheory.Structure.ExpandedForm;
// Melody module (Contour, Motif, MotifLibrary)
using SdkContour = BeyondImmersion.Bannou.MusicTheory.Melody.Contour;
using SdkIntervalPreferences = BeyondImmersion.Bannou.MusicTheory.Melody.IntervalPreferences;
using SdkMotif = BeyondImmersion.Bannou.MusicTheory.Melody.Motif;
using SdkMotifCategory = BeyondImmersion.Bannou.MusicTheory.Melody.MotifCategory;
using SdkNamedMotif = BeyondImmersion.Bannou.MusicTheory.Melody.NamedMotif;
using SdkMotifLibrary = BeyondImmersion.Bannou.MusicTheory.Melody.MotifLibrary;
using SdkBuiltInMotifs = BeyondImmersion.Bannou.MusicTheory.Melody.BuiltInMotifs;
// Harmony module
using SdkCadenceType = BeyondImmersion.Bannou.MusicTheory.Harmony.CadenceType;
// Structure module - Phrase
using SdkPhrase = BeyondImmersion.Bannou.MusicTheory.Structure.Phrase;
using SdkPhraseType = BeyondImmersion.Bannou.MusicTheory.Structure.PhraseType;
using SdkPhraseBuilder = BeyondImmersion.Bannou.MusicTheory.Structure.PhraseBuilder;
using SdkFormSection = BeyondImmersion.Bannou.MusicTheory.Structure.FormSection;

namespace BeyondImmersion.BannouService.Music.Tests;

public class MusicServiceTests
{
    #region Constructor Validation

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void MusicService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<MusicService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void MusicServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new MusicServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    #endregion
}

/// <summary>
/// Unit tests for music theory SDK - Pitch module.
/// </summary>
public class PitchTests
{
    [Theory]
    [InlineData(SdkPitchClass.C, 4, 60)]
    [InlineData(SdkPitchClass.A, 4, 69)]
    [InlineData(SdkPitchClass.A, 0, 21)]
    [InlineData(SdkPitchClass.C, 8, 108)]
    public void Pitch_MidiNumber_CalculatesCorrectly(SdkPitchClass pitchClass, int octave, int expectedMidi)
    {
        // Arrange & Act
        var pitch = new SdkPitch(pitchClass, octave);

        // Assert
        Assert.Equal(expectedMidi, pitch.MidiNumber);
    }

    [Fact]
    public void Pitch_MiddleC_HasCorrectMidiNumber()
    {
        // Arrange & Act
        var middleC = SdkPitch.Common.MiddleC;

        // Assert
        Assert.Equal(60, middleC.MidiNumber);
        Assert.Equal(SdkPitchClass.C, middleC.PitchClass);
        Assert.Equal(4, middleC.Octave);
    }

    [Fact]
    public void Pitch_ConcertA_HasCorrectMidiNumber()
    {
        // Arrange & Act
        var concertA = SdkPitch.Common.ConcertA;

        // Assert
        Assert.Equal(69, concertA.MidiNumber);
        Assert.Equal(SdkPitchClass.A, concertA.PitchClass);
        Assert.Equal(4, concertA.Octave);
    }

    [Theory]
    [InlineData(60, SdkPitchClass.C, 4)]
    [InlineData(69, SdkPitchClass.A, 4)]
    [InlineData(72, SdkPitchClass.C, 5)]
    public void Pitch_FromMidi_CreatesCorrectPitch(int midi, SdkPitchClass expectedClass, int expectedOctave)
    {
        // Arrange & Act
        var pitch = SdkPitch.FromMidi(midi);

        // Assert
        Assert.Equal(expectedClass, pitch.PitchClass);
        Assert.Equal(expectedOctave, pitch.Octave);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Interval calculations.
/// </summary>
public class IntervalTests
{
    [Theory]
    [InlineData(0, "P1")]
    [InlineData(7, "P5")]
    [InlineData(12, "P8")]
    [InlineData(4, "M3")]
    [InlineData(3, "m3")]
    public void Interval_FromSemitones_ReturnsCorrectName(int semitones, string expectedName)
    {
        // Arrange & Act
        var interval = new SdkInterval(semitones);

        // Assert
        Assert.Equal(expectedName, interval.Name);
    }

    [Fact]
    public void Interval_PerfectFifth_HasSevenSemitones()
    {
        // Arrange & Act
        var fifth = SdkInterval.PerfectFifth;

        // Assert
        Assert.Equal(7, fifth.Semitones);
    }

    [Fact]
    public void Interval_Octave_HasTwelveSemitones()
    {
        // Arrange & Act
        var octave = SdkInterval.PerfectOctave;

        // Assert
        Assert.Equal(12, octave.Semitones);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Scale construction.
/// </summary>
public class ScaleTests
{
    [Fact]
    public void Scale_CMajor_HasCorrectPitchClasses()
    {
        // Arrange & Act
        var cMajor = new SdkScale(SdkPitchClass.C, SdkModeType.Major);
        var pitchClasses = cMajor.PitchClasses.ToList();

        // Assert
        Assert.Equal(7, pitchClasses.Count);
        Assert.Equal(SdkPitchClass.C, pitchClasses[0]);
        Assert.Equal(SdkPitchClass.D, pitchClasses[1]);
        Assert.Equal(SdkPitchClass.E, pitchClasses[2]);
        Assert.Equal(SdkPitchClass.F, pitchClasses[3]);
        Assert.Equal(SdkPitchClass.G, pitchClasses[4]);
        Assert.Equal(SdkPitchClass.A, pitchClasses[5]);
        Assert.Equal(SdkPitchClass.B, pitchClasses[6]);
    }

    [Fact]
    public void Scale_DDorian_HasCorrectIntervalPattern()
    {
        // Arrange - D Dorian: D E F G A B C
        var dDorian = new SdkScale(SdkPitchClass.D, SdkModeType.Dorian);
        var pitchClasses = dDorian.PitchClasses.ToList();

        // Assert
        Assert.Equal(SdkPitchClass.D, pitchClasses[0]);
        Assert.Equal(SdkPitchClass.E, pitchClasses[1]);
        Assert.Equal(SdkPitchClass.F, pitchClasses[2]);
        Assert.Equal(SdkPitchClass.G, pitchClasses[3]);
        Assert.Equal(SdkPitchClass.A, pitchClasses[4]);
        Assert.Equal(SdkPitchClass.B, pitchClasses[5]);
        Assert.Equal(SdkPitchClass.C, pitchClasses[6]);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Chord construction.
/// </summary>
public class ChordTests
{
    [Fact]
    public void Chord_CMajorTriad_HasCorrectPitchClasses()
    {
        // Arrange & Act
        var cMajor = new SdkChord(SdkPitchClass.C, SdkChordQuality.Major);
        var pitchClasses = cMajor.PitchClasses;

        // Assert
        Assert.Equal(3, pitchClasses.Count);
        Assert.Contains(SdkPitchClass.C, pitchClasses);
        Assert.Contains(SdkPitchClass.E, pitchClasses);
        Assert.Contains(SdkPitchClass.G, pitchClasses);
    }

    [Fact]
    public void Chord_AMinorTriad_HasCorrectPitchClasses()
    {
        // Arrange & Act
        var aMinor = new SdkChord(SdkPitchClass.A, SdkChordQuality.Minor);
        var pitchClasses = aMinor.PitchClasses;

        // Assert
        Assert.Equal(3, pitchClasses.Count);
        Assert.Contains(SdkPitchClass.A, pitchClasses);
        Assert.Contains(SdkPitchClass.C, pitchClasses);
        Assert.Contains(SdkPitchClass.E, pitchClasses);
    }

    [Fact]
    public void Chord_G7_HasCorrectPitchClasses()
    {
        // Arrange & Act - G dominant 7: G B D F
        var g7 = new SdkChord(SdkPitchClass.G, SdkChordQuality.Dominant7);
        var pitchClasses = g7.PitchClasses;

        // Assert
        Assert.Equal(4, pitchClasses.Count);
        Assert.Contains(SdkPitchClass.G, pitchClasses);
        Assert.Contains(SdkPitchClass.B, pitchClasses);
        Assert.Contains(SdkPitchClass.D, pitchClasses);
        Assert.Contains(SdkPitchClass.F, pitchClasses);
    }

    [Fact]
    public void Chord_FromScaleDegree_BuildsCorrectTriad()
    {
        // Arrange
        var cMajorScale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);

        // Act - ii chord in C major = D minor
        var iiChord = SdkChord.FromScaleDegree(cMajorScale, 2);

        // Assert
        Assert.Equal(SdkPitchClass.D, iiChord.Root);
        Assert.Equal(SdkChordQuality.Minor, iiChord.Quality);
    }

    [Fact]
    public void Chord_FromScaleDegree_Dominant_IsCorrectQuality()
    {
        // Arrange
        var cMajorScale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);

        // Act - V chord in C major = G major
        var vChord = SdkChord.FromScaleDegree(cMajorScale, 5);

        // Assert
        Assert.Equal(SdkPitchClass.G, vChord.Root);
        Assert.Equal(SdkChordQuality.Major, vChord.Quality);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Progression generation.
/// </summary>
public class ProgressionTests
{
    [Fact]
    public void ProgressionGenerator_Generate_ReturnsRequestedLength()
    {
        // Arrange
        var generator = new SdkProgressionGenerator(seed: 42);
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);

        // Act
        var progression = generator.Generate(scale, 4);

        // Assert
        Assert.Equal(4, progression.Count);
    }

    [Fact]
    public void ProgressionGenerator_GenerateFromPattern_ParsesIVVI()
    {
        // Arrange
        var generator = new SdkProgressionGenerator();
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);

        // Act
        var progression = generator.GenerateFromPattern(scale, "I-IV-V-I");

        // Assert
        Assert.Equal(4, progression.Count);
        Assert.Equal(1, progression[0].Degree);
        Assert.Equal(4, progression[1].Degree);
        Assert.Equal(5, progression[2].Degree);
        Assert.Equal(1, progression[3].Degree);
    }

    [Fact]
    public void ProgressionGenerator_WithSeed_ProducesReproducibleResults()
    {
        // Arrange
        var scale = new SdkScale(SdkPitchClass.D, SdkModeType.Major);
        var generator1 = new SdkProgressionGenerator(seed: 12345);
        var generator2 = new SdkProgressionGenerator(seed: 12345);

        // Act
        var prog1 = generator1.Generate(scale, 8);
        var prog2 = generator2.Generate(scale, 8);

        // Assert
        Assert.Equal(prog1.Count, prog2.Count);
        for (int i = 0; i < prog1.Count; i++)
        {
            Assert.Equal(prog1[i].Degree, prog2[i].Degree);
        }
    }
}

/// <summary>
/// Unit tests for music theory SDK - Melody generation.
/// </summary>
public class MelodyTests
{
    [Fact]
    public void MelodyGenerator_Generate_ProducesNotes()
    {
        // Arrange
        var generator = new SdkMelodyGenerator(seed: 42);
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);
        var progression = new SdkProgressionGenerator(seed: 42).Generate(scale, 4);

        // Act
        var melody = generator.Generate(progression, scale);

        // Assert
        Assert.NotEmpty(melody);
        Assert.All(melody, note => Assert.InRange(note.Velocity, 1, 127));
    }

    [Fact]
    public void MelodyGenerator_WithSeed_ProducesReproducibleResults()
    {
        // Arrange
        var scale = new SdkScale(SdkPitchClass.G, SdkModeType.Major);
        var progression = new SdkProgressionGenerator(seed: 100).Generate(scale, 4);

        var generator1 = new SdkMelodyGenerator(seed: 999);
        var generator2 = new SdkMelodyGenerator(seed: 999);

        // Act
        var melody1 = generator1.Generate(progression, scale);
        var melody2 = generator2.Generate(progression, scale);

        // Assert
        Assert.Equal(melody1.Count, melody2.Count);
        for (int i = 0; i < melody1.Count; i++)
        {
            Assert.Equal(melody1[i].Pitch.MidiNumber, melody2[i].Pitch.MidiNumber);
            Assert.Equal(melody1[i].StartTick, melody2[i].StartTick);
        }
    }

    [Fact]
    public void MelodyGenerator_RespectsRange()
    {
        // Arrange
        var generator = new SdkMelodyGenerator(seed: 42);
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);
        var progression = new SdkProgressionGenerator(seed: 42).Generate(scale, 4);
        var range = SdkPitchRange.Vocal.Soprano;

        var options = new SdkMelodyOptions
        {
            Range = range
        };

        // Act
        var melody = generator.Generate(progression, scale, options);

        // Assert
        Assert.All(melody, note =>
        {
            Assert.True(range.Contains(note.Pitch),
                $"Note {note.Pitch} is outside soprano range {range}");
        });
    }
}

/// <summary>
/// Unit tests for music theory SDK - MIDI-JSON output.
/// </summary>
public class MidiJsonTests
{
    [Fact]
    public void MidiJson_HasCorrectTicksPerBeat()
    {
        // Arrange & Act
        var midi = new SdkMidiJson { TicksPerBeat = 480 };

        // Assert
        Assert.Equal(480, midi.TicksPerBeat);
    }

    [Fact]
    public void MidiJson_CanAddTrack()
    {
        // Arrange
        var midi = new SdkMidiJson { TicksPerBeat = 480 };
        var track = new SdkMidiTrack { Name = "Lead", Channel = 0 };

        // Act
        midi.Tracks.Add(track);

        // Assert
        Assert.Single(midi.Tracks);
        Assert.Equal("Lead", midi.Tracks[0].Name);
    }

    [Fact]
    public void MidiTrack_CanAddNoteEvents()
    {
        // Arrange
        var track = new SdkMidiTrack { Name = "Test", Channel = 0 };

        // Act
        track.Events.Add(new SdkMidiEvent
        {
            Tick = 0,
            Type = SdkMidiEventType.NoteOn,
            Note = 60,
            Velocity = 80,
            Duration = 480
        });
        track.Events.Add(new SdkMidiEvent
        {
            Tick = 480,
            Type = SdkMidiEventType.NoteOn,
            Note = 62,
            Velocity = 75,
            Duration = 480
        });

        // Assert
        Assert.Equal(2, track.Events.Count);
        Assert.All(track.Events, e => Assert.Equal(SdkMidiEventType.NoteOn, e.Type));
    }

    [Fact]
    public void MidiJsonRenderer_RenderChords_CreatesValidOutput()
    {
        // Arrange
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);
        var progression = new SdkProgressionGenerator(seed: 42).Generate(scale, 4);
        var voiceLeader = new SdkVoiceLeader();
        var (voicings, _) = voiceLeader.Voice(
            progression.Select(p => p.Chord).ToList(),
            voiceCount: 4);

        // Act
        var midiJson = SdkMidiJsonRenderer.RenderChords(
            voicings,
            ticksPerBeat: 480,
            beatsPerChord: 4.0,
            trackName: "Test Progression");

        // Assert
        Assert.NotNull(midiJson);
        Assert.NotEmpty(midiJson.Tracks);
        Assert.True(midiJson.TicksPerBeat > 0);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Style loading.
/// </summary>
public class StyleTests
{
    [Fact]
    public void BuiltInStyles_Celtic_HasCorrectProperties()
    {
        // Arrange & Act
        var celtic = SdkBuiltInStyles.Celtic;

        // Assert
        Assert.Equal("celtic", celtic.Id);
        Assert.Equal("folk", celtic.Category);
        Assert.True(celtic.ModeDistribution.Major > 0);
        Assert.True(celtic.ModeDistribution.Dorian > 0);
    }

    [Fact]
    public void BuiltInStyles_Jazz_HasCorrectProperties()
    {
        // Arrange & Act
        var jazz = SdkBuiltInStyles.Jazz;

        // Assert
        Assert.Equal("jazz", jazz.Id);
        Assert.True(jazz.ModeDistribution.Dorian > 0);
    }

    [Fact]
    public void StyleLoader_LoadFromYaml_ParsesCorrectly()
    {
        // Arrange
        var yaml = @"
id: test-style
name: Test Style
category: test
defaultTempo: 120
defaultMeter: 4/4
modeDistribution:
  major: 0.5
  minor: 0.3
  dorian: 0.2
intervalPreferences:
  stepWeight: 0.5
  thirdWeight: 0.3
  leapWeight: 0.15
  largeLeapWeight: 0.05
";

        var loader = new SdkStyleLoader();

        // Act
        var style = loader.LoadFromYaml(yaml);

        // Assert
        Assert.Equal("test-style", style.Id);
        Assert.Equal("Test Style", style.Name);
        Assert.Equal(120, style.DefaultTempo);
        Assert.Equal(0.5, style.ModeDistribution.Major);
        Assert.Equal(0.3, style.ModeDistribution.Minor);
        Assert.Equal(0.2, style.ModeDistribution.Dorian);
    }

    [Fact]
    public void ModeDistribution_Select_ReturnsValidMode()
    {
        // Arrange
        var dist = new SdkModeDistribution
        {
            Major = 0.6,
            Minor = 0.2,
            Dorian = 0.2
        };
        var random = new Random(42);

        // Act
        var modes = Enumerable.Range(0, 100)
            .Select(_ => dist.Select(random))
            .ToList();

        // Assert
        Assert.Contains(SdkModeType.Major, modes);
        // Minor or Dorian should also appear given the distribution
        Assert.Contains(modes, m => m != SdkModeType.Major);
    }
}

/// <summary>
/// Integration tests for Celtic-style tune generation.
/// </summary>
public class CelticGenerationTests
{
    [Fact]
    public void CelticStyle_GenerateProgression_ProducesValidChords()
    {
        // Arrange
        var style = SdkBuiltInStyles.Celtic;
        var scale = new SdkScale(SdkPitchClass.D, SdkModeType.Dorian);
        var generator = new SdkProgressionGenerator(seed: 42);

        // Act
        var progression = generator.Generate(scale, 8);

        // Assert
        Assert.Equal(8, progression.Count);
        Assert.All(progression, chord =>
        {
            Assert.InRange(chord.Degree, 1, 7);
            Assert.NotNull(chord.RomanNumeral);
        });
    }

    [Fact]
    public void CelticStyle_GenerateMelody_StaysInRange()
    {
        // Arrange
        var scale = new SdkScale(SdkPitchClass.G, SdkModeType.Major);
        var progression = new SdkProgressionGenerator(seed: 42).Generate(scale, 4);
        var generator = new SdkMelodyGenerator(seed: 42);

        var options = new SdkMelodyOptions
        {
            Range = SdkPitchRange.Instrument.Flute, // Traditional Celtic range
            Contour = SdkContourShape.Arch
        };

        // Act
        var melody = generator.Generate(progression, scale, options);

        // Assert
        Assert.NotEmpty(melody);
        Assert.NotNull(options.Range);
        Assert.All(melody, note =>
        {
            Assert.True(options.Range.Value.Contains(note.Pitch),
                $"Note {note.Pitch} outside flute range");
        });
    }

    [Fact]
    public void CelticStyle_FullComposition_ProducesValidMidiJson()
    {
        // Arrange
        var style = SdkBuiltInStyles.Celtic;
        var scale = new SdkScale(SdkPitchClass.D, SdkModeType.Dorian);
        var seed = 42;

        // Generate components
        var progGenerator = new SdkProgressionGenerator(seed);
        var progression = progGenerator.Generate(scale, 8);

        var voiceLeader = new SdkVoiceLeader();
        var (voicings, violations) = voiceLeader.Voice(
            progression.Select(p => p.Chord).ToList(),
            voiceCount: 4);

        var melodyGenerator = new SdkMelodyGenerator(seed);
        var melodyOptions = new SdkMelodyOptions
        {
            Range = SdkPitchRange.Instrument.Flute,
            Contour = SdkContourShape.Arch,
            IntervalPreferences = style.IntervalPreferences
        };
        var melody = melodyGenerator.Generate(progression, scale, melodyOptions);

        // Act
        var midiJson = SdkMidiJsonRenderer.RenderComposition(
            melody,
            voicings,
            tempo: 112,
            meter: new SdkMeter(4, 4),
            key: scale,
            ticksPerBeat: 480,
            name: "Celtic Test Tune");

        // Assert
        Assert.NotNull(midiJson);
        Assert.Equal(480, midiJson.TicksPerBeat);
        Assert.NotEmpty(midiJson.Tracks);

        // Should have melody and harmony tracks
        Assert.True(midiJson.Tracks.Count >= 2,
            "Should have at least melody and harmony tracks");

        // Verify note events exist
        var totalNotes = midiJson.Tracks.Sum(t => t.Events.Count(e => e.Type == SdkMidiEventType.NoteOn));
        Assert.True(totalNotes > 0, "Should have note events");
    }
}

/// <summary>
/// Unit tests for voice leading rules.
/// </summary>
public class VoiceLeadingTests
{
    [Fact]
    public void VoiceLeader_StandardRules_ProducesVoicings()
    {
        // Arrange - Create chords for voicing
        var chords = new List<SdkChord>
        {
            new(SdkPitchClass.C, SdkChordQuality.Major),
            new(SdkPitchClass.D, SdkChordQuality.Major)
        };

        var rules = SdkVoiceLeadingRules.Standard;
        var voiceLeader = new SdkVoiceLeader(rules);

        // Act
        var (voicings, violations) = voiceLeader.Voice(chords, voiceCount: 4);

        // Assert - Voice leader should produce valid voicings
        Assert.NotNull(voicings);
        Assert.Equal(2, voicings.Count);
        // Each voicing should have 4 voices
        Assert.All(voicings, v => Assert.Equal(4, v.VoiceCount));
    }

    [Fact]
    public void VoiceLeader_RelaxedRules_AllowsLargerLeaps()
    {
        // Arrange
        var chords = new List<SdkChord>
        {
            new(SdkPitchClass.C, SdkChordQuality.Major),
            new(SdkPitchClass.F, SdkChordQuality.Major),
            new(SdkPitchClass.G, SdkChordQuality.Major),
            new(SdkPitchClass.C, SdkChordQuality.Major)
        };

        var rules = SdkVoiceLeadingRules.Relaxed;
        var voiceLeader = new SdkVoiceLeader(rules);

        // Act
        var (voicings, violations) = voiceLeader.Voice(chords, voiceCount: 4);

        // Assert
        Assert.Equal(4, voicings.Count);
        // Relaxed rules should have fewer violations
        var leapViolations = violations.Count(v =>
            v.Type == SdkVoiceLeadingViolationType.LargeLeap);
        // With relaxed rules (MaxLeap = 12), large leap violations should be rare
        Assert.True(leapViolations < 3, "Relaxed rules should allow larger leaps");
    }

    [Fact]
    public void VoiceLeader_IVIProgression_ProducesCorrectVoicings()
    {
        // Arrange - Classic I-IV-V-I cadence
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);
        var generator = new SdkProgressionGenerator();
        var progression = generator.GenerateFromPattern(scale, "I-IV-V-I");

        var voiceLeader = new SdkVoiceLeader();

        // Act
        var (voicings, violations) = voiceLeader.Voice(
            progression.Select(p => p.Chord).ToList(),
            voiceCount: 4);

        // Assert
        Assert.Equal(4, voicings.Count);

        // Each voicing should contain only pitches from the chord
        for (int i = 0; i < voicings.Count; i++)
        {
            var voicing = voicings[i];
            var chordPitchClasses = progression[i].Chord.PitchClasses;

            Assert.All(voicing.Pitches, pitch =>
            {
                Assert.Contains(pitch.PitchClass, chordPitchClasses);
            });
        }
    }
}

/// <summary>
/// Unit tests for music theory SDK - Duration calculations.
/// </summary>
public class DurationTests
{
    [Fact]
    public void Duration_QuarterNote_HasOnebeat()
    {
        // Arrange & Act
        var quarter = SdkDuration.Common.Quarter;

        // Assert
        Assert.Equal(1.0, quarter.Beats, 3);
    }

    [Fact]
    public void Duration_WholeNote_HasFourBeats()
    {
        // Arrange & Act
        var whole = SdkDuration.Common.Whole;

        // Assert
        Assert.Equal(4.0, whole.Beats, 3);
    }

    [Fact]
    public void Duration_HalfNote_HasTwoBeats()
    {
        // Arrange & Act
        var half = SdkDuration.Common.Half;

        // Assert
        Assert.Equal(2.0, half.Beats, 3);
    }

    [Fact]
    public void Duration_EighthNote_HasHalfBeat()
    {
        // Arrange & Act
        var eighth = SdkDuration.Common.Eighth;

        // Assert
        Assert.Equal(0.5, eighth.Beats, 3);
    }

    [Fact]
    public void Duration_DottedQuarter_HasOneAndHalfBeats()
    {
        // Arrange & Act
        var dottedQuarter = SdkDuration.Common.DottedQuarter;

        // Assert
        Assert.Equal(1.5, dottedQuarter.Beats, 3);
    }

    [Fact]
    public void Duration_EighthTriplet_HasCorrectDuration()
    {
        // Arrange & Act - Triplet eighths: 3 in the space of 2
        var triplet = SdkDuration.Common.EighthTriplet;

        // Assert - 0.5 * (2/3) = 0.333...
        Assert.Equal(1.0 / 3.0, triplet.Beats, 3);
    }

    [Fact]
    public void Duration_ToTicks_CalculatesCorrectly()
    {
        // Arrange
        var quarter = SdkDuration.Common.Quarter;
        var ticksPerBeat = 480;

        // Act
        var ticks = quarter.ToTicks(ticksPerBeat);

        // Assert
        Assert.Equal(480, ticks);
    }

    [Fact]
    public void Duration_ToTicks_EighthNote_IsHalf()
    {
        // Arrange
        var eighth = SdkDuration.Common.Eighth;
        var ticksPerBeat = 480;

        // Act
        var ticks = eighth.ToTicks(ticksPerBeat);

        // Assert
        Assert.Equal(240, ticks);
    }

    [Fact]
    public void Duration_AsTriplet_ModifiesDuration()
    {
        // Arrange
        var quarter = SdkDuration.Common.Quarter;

        // Act
        var triplet = quarter.AsTriplet();

        // Assert - Quarter triplet: 3 in the space of 2 quarter notes
        Assert.Equal(2.0 / 3.0, triplet.Beats, 3);
    }

    [Fact]
    public void Duration_Dotted_AddsDot()
    {
        // Arrange
        var quarter = SdkDuration.Common.Quarter;

        // Act
        var dotted = quarter.Dotted();

        // Assert
        Assert.Equal(1, dotted.Dots);
        Assert.Equal(1.5, dotted.Beats, 3);
    }

    [Fact]
    public void Duration_FromBeats_ReturnsClosestMatch()
    {
        // Act
        var duration1 = SdkDuration.FromBeats(1.0);
        var duration2 = SdkDuration.FromBeats(2.0);
        var duration4 = SdkDuration.FromBeats(4.0);

        // Assert
        Assert.Equal(1.0, duration1.Beats, 3);
        Assert.Equal(2.0, duration2.Beats, 3);
        Assert.Equal(4.0, duration4.Beats, 3);
    }

    [Fact]
    public void Duration_Comparison_WorksCorrectly()
    {
        // Arrange
        var quarter = SdkDuration.Common.Quarter;
        var eighth = SdkDuration.Common.Eighth;
        var half = SdkDuration.Common.Half;

        // Assert
        Assert.True(eighth < quarter);
        Assert.True(quarter < half);
        Assert.True(half > quarter);
        Assert.True(quarter > eighth);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Meter (time signature).
/// </summary>
public class MeterTests
{
    [Fact]
    public void Meter_CommonTime_Is4_4()
    {
        // Arrange & Act
        var commonTime = SdkMeter.Common.CommonTime;

        // Assert
        Assert.Equal(4, commonTime.Numerator);
        Assert.Equal(4, commonTime.Denominator);
    }

    [Fact]
    public void Meter_SixEight_IsCompound()
    {
        // Arrange & Act
        var sixEight = SdkMeter.Common.SixEight;

        // Assert
        Assert.Equal(6, sixEight.Numerator);
        Assert.Equal(8, sixEight.Denominator);
        Assert.Equal(BeyondImmersion.Bannou.MusicTheory.Time.MeterType.Compound, sixEight.Type);
    }

    [Fact]
    public void Meter_ThreeFour_IsSimple()
    {
        // Arrange & Act
        var threeFour = SdkMeter.Common.ThreeFour;

        // Assert
        Assert.Equal(BeyondImmersion.Bannou.MusicTheory.Time.MeterType.Simple, threeFour.Type);
    }

    [Fact]
    public void Meter_SevenEight_IsIrregular()
    {
        // Arrange & Act
        var sevenEight = SdkMeter.Common.SevenEight;

        // Assert
        Assert.Equal(BeyondImmersion.Bannou.MusicTheory.Time.MeterType.Irregular, sevenEight.Type);
    }

    [Fact]
    public void Meter_MeasureBeats_CalculatesCorrectly()
    {
        // Arrange
        var fourFour = SdkMeter.Common.CommonTime;
        var sixEight = SdkMeter.Common.SixEight;

        // Assert - 4/4 = 4 beats, 6/8 = 3 beats (6 eighths = 3 quarter-note equivalent beats)
        Assert.Equal(4.0, fourFour.MeasureBeats);
        Assert.Equal(3.0, sixEight.MeasureBeats);
    }

    [Fact]
    public void Meter_Parse_ParsesCorrectly()
    {
        // Act
        var fourFour = SdkMeter.Parse("4/4");
        var sixEight = SdkMeter.Parse("6/8");
        var threeFour = SdkMeter.Parse("3/4");

        // Assert
        Assert.Equal(4, fourFour.Numerator);
        Assert.Equal(4, fourFour.Denominator);
        Assert.Equal(6, sixEight.Numerator);
        Assert.Equal(8, sixEight.Denominator);
        Assert.Equal(3, threeFour.Numerator);
        Assert.Equal(4, threeFour.Denominator);
    }

    [Fact]
    public void Meter_StrongBeats_IncludesDownbeat()
    {
        // Arrange
        var fourFour = SdkMeter.Common.CommonTime;

        // Act
        var strongBeats = fourFour.StrongBeats.ToList();

        // Assert - In 4/4, beats 1 and 3 are strong
        Assert.Contains(1, strongBeats);
        Assert.Contains(3, strongBeats);
    }

    [Fact]
    public void Meter_MeasureTicks_CalculatesCorrectly()
    {
        // Arrange
        var fourFour = SdkMeter.Common.CommonTime;
        var ticksPerBeat = 480;

        // Act
        var measureTicks = fourFour.MeasureTicks(ticksPerBeat);

        // Assert - 4 beats * 480 = 1920 ticks
        Assert.Equal(1920, measureTicks);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Rhythm patterns.
/// </summary>
public class RhythmTests
{
    [Fact]
    public void RhythmPattern_EvenQuarters_HasFourBeats()
    {
        // Arrange & Act
        var evenQuarters = SdkRhythmPattern.Common.EvenQuarters;

        // Assert
        Assert.Equal(4.0, evenQuarters.TotalBeats);
        Assert.Equal(4, evenQuarters.Pattern.Count);
    }

    [Fact]
    public void RhythmPattern_EvenEighths_HasFourBeats()
    {
        // Arrange & Act
        var evenEighths = SdkRhythmPattern.Common.EvenEighths;

        // Assert
        Assert.Equal(4.0, evenEighths.TotalBeats);
        Assert.Equal(8, evenEighths.Pattern.Count);
    }

    [Fact]
    public void RhythmPattern_Triplet_HasOnebeat()
    {
        // Arrange & Act
        var triplet = SdkRhythmPattern.Common.Triplet;

        // Assert
        Assert.Equal(1.0, triplet.TotalBeats, 3);
        Assert.Equal(3, triplet.Pattern.Count);
    }

    [Fact]
    public void RhythmPattern_ToTicks_ConvertsCorrectly()
    {
        // Arrange
        var evenQuarters = SdkRhythmPattern.Common.EvenQuarters;
        var ticksPerBeat = 480;

        // Act
        var ticks = evenQuarters.ToTicks(ticksPerBeat).ToList();

        // Assert - Each quarter note should be 480 ticks
        Assert.Equal(4, ticks.Count);
        Assert.All(ticks, t => Assert.Equal(480, t));
    }

    [Fact]
    public void RhythmPattern_ScaleTo_ScalesCorrectly()
    {
        // Arrange
        var pattern = SdkRhythmPattern.Common.EvenQuarters; // 4 beats total

        // Act
        var scaled = pattern.ScaleTo(8.0); // Scale to 8 beats

        // Assert
        Assert.Equal(8.0, scaled.TotalBeats);
        Assert.All(scaled.Pattern, d => Assert.Equal(2.0, d)); // Each duration doubled
    }

    [Fact]
    public void RhythmPattern_Repeat_RepeatsCorrectly()
    {
        // Arrange
        var pattern = new SdkRhythmPattern("test", [1.0, 0.5]);

        // Act
        var repeated = pattern.Repeat(3);

        // Assert
        Assert.Equal(6, repeated.Pattern.Count);
        Assert.Equal(4.5, repeated.TotalBeats); // (1.0 + 0.5) * 3
    }

    [Fact]
    public void RhythmPatternSet_SelectWeighted_ReturnsPattern()
    {
        // Arrange
        var patterns = new List<SdkRhythmPattern>
        {
            SdkRhythmPattern.Common.EvenQuarters,
            SdkRhythmPattern.Common.EvenEighths
        };
        var patternSet = new SdkRhythmPatternSet(patterns, seed: 42);

        // Act
        var selected = patternSet.SelectWeighted();

        // Assert
        Assert.NotNull(selected);
        Assert.Contains(selected, patterns);
    }

    [Fact]
    public void RhythmPattern_Celtic_JigBasic_HasCorrectPattern()
    {
        // Arrange & Act
        var jig = SdkRhythmPattern.Celtic.JigBasic;

        // Assert - Jig: dotted eighth + sixteenth + eighth = 1.5 beats
        Assert.Equal(1.5, jig.TotalBeats);
        Assert.Equal(3, jig.Pattern.Count);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Form and structure.
/// </summary>
public class FormTests
{
    [Fact]
    public void Form_AABB_HasFourSections()
    {
        // Arrange & Act
        var aabb = SdkForm.Common.AABB;

        // Assert
        Assert.Equal(4, aabb.SectionCount);
        Assert.Equal("AABB", aabb.Name);
    }

    [Fact]
    public void Form_AABA_HasCorrectStructure()
    {
        // Arrange & Act
        var aaba = SdkForm.Common.AABA;
        var sections = aaba.Sections.Select(s => s.Label).ToList();

        // Assert
        Assert.Equal(["A", "A", "B", "A"], sections);
    }

    [Fact]
    public void Form_Parse_ParsesSimplePattern()
    {
        // Act
        var form = SdkForm.Parse("ABAB");

        // Assert
        Assert.Equal(4, form.SectionCount);
        Assert.Equal("A", form.Sections[0].Label);
        Assert.Equal("B", form.Sections[1].Label);
        Assert.Equal("A", form.Sections[2].Label);
        Assert.Equal("B", form.Sections[3].Label);
    }

    [Fact]
    public void Form_Parse_HandlesHyphenatedPattern()
    {
        // Act
        var form = SdkForm.Parse("A-A-B-B");

        // Assert
        Assert.Equal(4, form.SectionCount);
    }

    [Fact]
    public void Form_TotalBars_CalculatesCorrectly()
    {
        // Arrange & Act
        var aabb = SdkForm.Common.AABB; // 4 sections, 8 bars each

        // Assert
        Assert.Equal(32, aabb.TotalBars);
    }

    [Fact]
    public void Form_UniqueLabels_ReturnsDistinctLabels()
    {
        // Arrange & Act
        var aabb = SdkForm.Common.AABB;
        var uniqueLabels = aabb.UniqueLabels.ToList();

        // Assert
        Assert.Equal(2, uniqueLabels.Count);
        Assert.Contains("A", uniqueLabels);
        Assert.Contains("B", uniqueLabels);
    }

    [Fact]
    public void Section_CreateVariation_IncrementsVariation()
    {
        // Arrange
        var sectionA = SdkSection.A;

        // Act
        var aPrime = sectionA.CreateVariation();
        var aDoublePrime = aPrime.CreateVariation();

        // Assert
        Assert.Equal(0, sectionA.Variation);
        Assert.Equal(1, aPrime.Variation);
        Assert.Equal(2, aDoublePrime.Variation);
        Assert.Equal("A'", aPrime.ToString());
        Assert.Equal("A''", aDoublePrime.ToString());
    }

    [Fact]
    public void ExpandedForm_GetSectionAtBar_FindsCorrectSection()
    {
        // Arrange
        var form = SdkForm.Common.AABB; // 4 sections, 8 bars each
        var expanded = new SdkExpandedForm(form);

        // Act
        var sectionAt0 = expanded.GetSectionAtBar(0);
        var sectionAt8 = expanded.GetSectionAtBar(8);
        var sectionAt16 = expanded.GetSectionAtBar(16);

        // Assert
        Assert.NotNull(sectionAt0);
        Assert.Equal("A", sectionAt0.Section.Label);
        Assert.NotNull(sectionAt8);
        Assert.Equal("A", sectionAt8.Section.Label);
        Assert.NotNull(sectionAt16);
        Assert.Equal("B", sectionAt16.Section.Label);
    }

    [Fact]
    public void ExpandedForm_TotalBars_MatchesFormTotalBars()
    {
        // Arrange
        var form = SdkForm.Common.AABB;
        var expanded = new SdkExpandedForm(form);

        // Assert
        Assert.Equal(form.TotalBars, expanded.TotalBars);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Contour analysis.
/// </summary>
public class ContourTests
{
    [Fact]
    public void Contour_Arch_GetDirectionAt_ReturnsUpThenDown()
    {
        // Arrange
        var contour = new SdkContour(SdkContourShape.Arch);

        // Act
        var directionAtStart = contour.GetDirectionAt(0.25);
        var directionAtEnd = contour.GetDirectionAt(0.75);

        // Assert
        Assert.Equal(1, directionAtStart); // Ascending
        Assert.Equal(-1, directionAtEnd); // Descending
    }

    [Fact]
    public void Contour_Ascending_AlwaysReturnsUp()
    {
        // Arrange
        var contour = new SdkContour(SdkContourShape.Ascending);

        // Act & Assert
        Assert.Equal(1, contour.GetDirectionAt(0.0));
        Assert.Equal(1, contour.GetDirectionAt(0.5));
        Assert.Equal(1, contour.GetDirectionAt(1.0));
    }

    [Fact]
    public void Contour_Descending_AlwaysReturnsDown()
    {
        // Arrange
        var contour = new SdkContour(SdkContourShape.Descending);

        // Act & Assert
        Assert.Equal(-1, contour.GetDirectionAt(0.0));
        Assert.Equal(-1, contour.GetDirectionAt(0.5));
        Assert.Equal(-1, contour.GetDirectionAt(1.0));
    }

    [Fact]
    public void Contour_Static_AlwaysReturnsZero()
    {
        // Arrange
        var contour = new SdkContour(SdkContourShape.Static);

        // Act & Assert
        Assert.Equal(0, contour.GetDirectionAt(0.0));
        Assert.Equal(0, contour.GetDirectionAt(0.5));
        Assert.Equal(0, contour.GetDirectionAt(1.0));
    }

    [Fact]
    public void Contour_GetTargetPitchAt_Arch_PeaksAtMiddle()
    {
        // Arrange
        var contour = new SdkContour(SdkContourShape.Arch);

        // Act
        var pitchAtMiddle = contour.GetTargetPitchAt(0.5);
        var pitchAtStart = contour.GetTargetPitchAt(0.0);
        var pitchAtEnd = contour.GetTargetPitchAt(1.0);

        // Assert - Arch peaks at middle (sin(0.5*PI) = 1)
        Assert.True(pitchAtMiddle > pitchAtStart);
        Assert.True(pitchAtMiddle > pitchAtEnd);
    }

    [Fact]
    public void Contour_Analyze_DetectsArchShape()
    {
        // Arrange - Pitches that rise then fall
        var pitches = new List<int> { 60, 62, 64, 67, 72, 69, 65, 62, 60 };

        // Act
        var detected = SdkContour.Analyze(pitches);

        // Assert
        Assert.Equal(SdkContourShape.Arch, detected);
    }

    [Fact]
    public void Contour_Analyze_DetectsAscending()
    {
        // Arrange - Steadily rising pitches
        var pitches = new List<int> { 60, 62, 64, 65, 67, 69, 71, 72 };

        // Act
        var detected = SdkContour.Analyze(pitches);

        // Assert
        Assert.Equal(SdkContourShape.Ascending, detected);
    }

    [Fact]
    public void Contour_Analyze_DetectsStatic()
    {
        // Arrange - Barely moving pitches
        var pitches = new List<int> { 60, 60, 61, 60, 60, 61, 60 };

        // Act
        var detected = SdkContour.Analyze(pitches);

        // Assert
        Assert.Equal(SdkContourShape.Static, detected);
    }
}

/// <summary>
/// Unit tests for music theory SDK - Interval preferences.
/// </summary>
public class IntervalPreferencesTests
{
    [Fact]
    public void IntervalPreferences_Default_HasValidWeights()
    {
        // Arrange & Act
        var prefs = SdkIntervalPreferences.Default;

        // Assert - Weights should sum to approximately 1.0
        var total = prefs.StepWeight + prefs.ThirdWeight + prefs.LeapWeight + prefs.LargeLeapWeight;
        Assert.Equal(1.0, total, 2);
    }

    [Fact]
    public void IntervalPreferences_Celtic_FavorsSteps()
    {
        // Arrange & Act
        var celtic = SdkIntervalPreferences.Celtic;

        // Assert - Celtic style favors stepwise motion
        Assert.True(celtic.StepWeight > celtic.ThirdWeight);
        Assert.True(celtic.StepWeight > celtic.LeapWeight);
    }

    [Fact]
    public void IntervalPreferences_Jazz_AllowsMoreLeaps()
    {
        // Arrange & Act
        var jazz = SdkIntervalPreferences.Jazz;
        var celtic = SdkIntervalPreferences.Celtic;

        // Assert - Jazz allows more leaps than Celtic
        Assert.True(jazz.LargeLeapWeight > celtic.LargeLeapWeight);
    }

    [Fact]
    public void IntervalPreferences_SelectInterval_ReturnsValidInterval()
    {
        // Arrange
        var prefs = SdkIntervalPreferences.Default;
        var random = new Random(42);

        // Act
        var intervals = Enumerable.Range(0, 100)
            .Select(_ => prefs.SelectInterval(random))
            .ToList();

        // Assert - All intervals should be positive
        Assert.All(intervals, i => Assert.True(i >= 1 && i <= 12));
    }
}

/// <summary>
/// Unit tests for music theory SDK - Motif transformations.
/// </summary>
public class MotifTests
{
    [Fact]
    public void Motif_Create_StoresIntervalsAndDurations()
    {
        // Arrange - intervals from first note, relative durations
        var intervals = new List<int> { 0, 2, 4 }; // C, D, E (steps from C)
        var durations = new List<double> { 1.0, 1.0, 1.0 };

        // Act
        var motif = new SdkMotif(intervals, durations);

        // Assert
        Assert.Equal(3, motif.Length);
        Assert.Equal(3.0, motif.TotalDuration);
        Assert.Equal(intervals, motif.Intervals);
        Assert.Equal(durations, motif.Durations);
    }

    [Fact]
    public void Motif_FromNotes_ExtractsIntervalsAndDurations()
    {
        // Arrange
        var notes = new List<SdkMelodyNote>
        {
            new(new SdkPitch(SdkPitchClass.C, 4), 0, 480, 80), // MIDI 60
            new(new SdkPitch(SdkPitchClass.E, 4), 480, 480, 80), // MIDI 64 (+4)
            new(new SdkPitch(SdkPitchClass.G, 4), 960, 480, 80) // MIDI 67 (+7)
        };

        // Act
        var motif = SdkMotif.FromNotes(notes);

        // Assert - intervals relative to first note
        Assert.Equal(3, motif.Length);
        Assert.Equal(0, motif.Intervals[0]); // First note is reference
        Assert.Equal(4, motif.Intervals[1]); // E is +4 semitones from C
        Assert.Equal(7, motif.Intervals[2]); // G is +7 semitones from C
    }

    [Fact]
    public void Motif_Transpose_ShiftsIntervals()
    {
        // Arrange - C-E-G intervals: 0, +4, +7
        var intervals = new List<int> { 0, 4, 7 };
        var durations = new List<double> { 1.0, 1.0, 2.0 };
        var motif = new SdkMotif(intervals, durations);

        // Act - Transpose up 2 semitones
        var transposed = motif.Transpose(2);

        // Assert - All intervals shifted by 2
        Assert.Equal(2, transposed.Intervals[0]);
        Assert.Equal(6, transposed.Intervals[1]);
        Assert.Equal(9, transposed.Intervals[2]);
    }

    [Fact]
    public void Motif_Invert_NegatesIntervals()
    {
        // Arrange - Ascending major triad intervals: 0, +4, +7
        var intervals = new List<int> { 0, 4, 7 };
        var durations = new List<double> { 1.0, 1.0, 2.0 };
        var motif = new SdkMotif(intervals, durations);

        // Act - Invert (no argument - just negates intervals)
        var inverted = motif.Invert();

        // Assert - Intervals negated: 0, -4, -7
        Assert.Equal(0, inverted.Intervals[0]);
        Assert.Equal(-4, inverted.Intervals[1]);
        Assert.Equal(-7, inverted.Intervals[2]);
    }

    [Fact]
    public void Motif_Retrograde_ReversesPattern()
    {
        // Arrange - C-D-E intervals: 0, +2, +4
        var intervals = new List<int> { 0, 2, 4 };
        var durations = new List<double> { 1.0, 1.0, 1.0 };
        var motif = new SdkMotif(intervals, durations);

        // Act
        var retrograde = motif.Retrograde();

        // Assert - Pattern reversed, recalculated from new starting point
        // Original last interval was 4, so retrograde starts at 0 and subtracts
        Assert.Equal(3, retrograde.Length);
        Assert.Equal(0, retrograde.Intervals[0]); // New start
    }

    [Fact]
    public void Motif_Augment_StretchesDurations()
    {
        // Arrange
        var intervals = new List<int> { 0, 2 };
        var durations = new List<double> { 1.0, 1.0 };
        var motif = new SdkMotif(intervals, durations);

        // Act - Double the durations
        var augmented = motif.Augment(2.0);

        // Assert
        Assert.Equal(2.0, augmented.Durations[0]);
        Assert.Equal(2.0, augmented.Durations[1]);
        Assert.Equal(4.0, augmented.TotalDuration);
    }

    [Fact]
    public void Motif_Diminish_CompressesDurations()
    {
        // Arrange
        var intervals = new List<int> { 0, 2 };
        var durations = new List<double> { 1.0, 1.0 };
        var motif = new SdkMotif(intervals, durations);

        // Act - Halve the durations
        var diminished = motif.Diminish(2.0);

        // Assert
        Assert.Equal(0.5, diminished.Durations[0]);
        Assert.Equal(0.5, diminished.Durations[1]);
        Assert.Equal(1.0, diminished.TotalDuration);
    }

    [Fact]
    public void Motif_Sequence_RepeatsAtDifferentPitchLevels()
    {
        // Arrange - Simple 2-note motif
        var intervals = new List<int> { 0, 2 };
        var durations = new List<double> { 1.0, 1.0 };
        var motif = new SdkMotif(intervals, durations);

        // Act - Sequence 3 times, stepping up by 2 semitones each time
        var sequenced = motif.Sequence(2, 3);

        // Assert - 6 notes total (2 * 3)
        Assert.Equal(6, sequenced.Length);
        // First repetition: 0, 2
        Assert.Equal(0, sequenced.Intervals[0]);
        Assert.Equal(2, sequenced.Intervals[1]);
        // Second repetition: 2, 4
        Assert.Equal(2, sequenced.Intervals[2]);
        Assert.Equal(4, sequenced.Intervals[3]);
        // Third repetition: 4, 6
        Assert.Equal(4, sequenced.Intervals[4]);
        Assert.Equal(6, sequenced.Intervals[5]);
    }

    [Fact]
    public void Motif_Realize_CreatesMelodyNotes()
    {
        // Arrange - C major triad motif
        var intervals = new List<int> { 0, 4, 7 };
        var durations = new List<double> { 1.0, 1.0, 2.0 };
        var motif = new SdkMotif(intervals, durations);
        var startPitch = new SdkPitch(SdkPitchClass.C, 4);

        // Act - Realize starting at C4
        var notes = motif.Realize(startPitch, ticksPerBeat: 480);

        // Assert
        Assert.Equal(3, notes.Count);
        Assert.Equal(60, notes[0].Pitch.MidiNumber); // C4
        Assert.Equal(64, notes[1].Pitch.MidiNumber); // E4
        Assert.Equal(67, notes[2].Pitch.MidiNumber); // G4
        Assert.Equal(480, notes[0].DurationTicks); // 1.0 * 480
        Assert.Equal(480, notes[1].DurationTicks);
        Assert.Equal(960, notes[2].DurationTicks); // 2.0 * 480
    }

    [Fact]
    public void Motif_Patterns_ScaleUp_HasCorrectIntervals()
    {
        // Arrange & Act
        var scaleUp = SdkMotif.Patterns.ScaleUp;

        // Assert - do-re-mi-fa = 0, 2, 4, 5
        Assert.Equal(4, scaleUp.Length);
        Assert.Equal(0, scaleUp.Intervals[0]);
        Assert.Equal(2, scaleUp.Intervals[1]);
        Assert.Equal(4, scaleUp.Intervals[2]);
        Assert.Equal(5, scaleUp.Intervals[3]);
    }

    [Fact]
    public void Motif_Patterns_ArpeggioUp_HasTriadIntervals()
    {
        // Arrange & Act
        var arpeggio = SdkMotif.Patterns.ArpeggioUp;

        // Assert - do-mi-sol = 0, 4, 7
        Assert.Equal(3, arpeggio.Length);
        Assert.Equal(0, arpeggio.Intervals[0]);
        Assert.Equal(4, arpeggio.Intervals[1]);
        Assert.Equal(7, arpeggio.Intervals[2]);
    }

    [Fact]
    public void Motif_Constructor_ThrowsOnEmptyInput()
    {
        // Arrange
        var emptyIntervals = new List<int>();
        var emptyDurations = new List<double>();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new SdkMotif(emptyIntervals, emptyDurations));
    }

    [Fact]
    public void Motif_Constructor_ThrowsOnMismatchedLengths()
    {
        // Arrange
        var intervals = new List<int> { 0, 2, 4 };
        var durations = new List<double> { 1.0, 1.0 }; // One less

        // Act & Assert
        Assert.Throws<ArgumentException>(() => new SdkMotif(intervals, durations));
    }
}

/// <summary>
/// Unit tests for music theory SDK - Mode patterns.
/// </summary>
public class ModeTests
{
    [Fact]
    public void ModePatterns_Major_HasCorrectPattern()
    {
        // Arrange & Act
        var pattern = BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.GetPattern(SdkModeType.Major);

        // Assert - Major: W-W-H-W-W-W-H = 0,2,4,5,7,9,11
        Assert.Equal([0, 2, 4, 5, 7, 9, 11], pattern);
    }

    [Fact]
    public void ModePatterns_Dorian_HasCorrectPattern()
    {
        // Arrange & Act
        var pattern = BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.GetPattern(SdkModeType.Dorian);

        // Assert - Dorian: W-H-W-W-W-H-W = 0,2,3,5,7,9,10
        Assert.Equal([0, 2, 3, 5, 7, 9, 10], pattern);
    }

    [Fact]
    public void ModePatterns_Blues_HasSixNotes()
    {
        // Arrange & Act
        var pattern = BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.GetPattern(SdkModeType.Blues);

        // Assert - Blues scale has 6 notes
        Assert.Equal(6, pattern.Count);
    }

    [Fact]
    public void ModePatterns_MajorPentatonic_HasFiveNotes()
    {
        // Arrange & Act
        var pattern = BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.GetPattern(SdkModeType.MajorPentatonic);

        // Assert
        Assert.Equal(5, pattern.Count);
    }

    [Fact]
    public void ModePatterns_IsMajor_ReturnsTrueForMajorModes()
    {
        // Assert
        Assert.True(BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.IsMajor(SdkModeType.Major));
        Assert.True(BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.IsMajor(SdkModeType.Lydian));
        Assert.True(BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.IsMajor(SdkModeType.Mixolydian));
        Assert.False(BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.IsMajor(SdkModeType.Minor));
        Assert.False(BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.IsMajor(SdkModeType.Dorian));
    }

    [Fact]
    public void ModePatterns_Parse_ParsesCorrectly()
    {
        // Act
        var major = BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.Parse("major");
        var dorian = BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.Parse("dorian");
        var minor = BeyondImmersion.Bannou.MusicTheory.Collections.ModePatterns.Parse("minor");

        // Assert
        Assert.Equal(SdkModeType.Major, major);
        Assert.Equal(SdkModeType.Dorian, dorian);
        Assert.Equal(SdkModeType.Minor, minor);
    }
}

// =============================================================================
// INTEGRATION TESTS
// Tests that verify complete end-to-end music generation pipelines
// =============================================================================

/// <summary>
/// Integration tests for the complete music generation pipeline.
/// Tests the flow: Style → Progression → Melody → MIDI-JSON
/// </summary>
public class MusicGenerationPipelineTests
{
    [Fact]
    public void Pipeline_CelticStyle_GeneratesValidMidiJson()
    {
        // Arrange - Celtic style with D Dorian key
        var style = SdkBuiltInStyles.Celtic;
        var scale = new SdkScale(SdkPitchClass.D, SdkModeType.Dorian);
        var form = SdkForm.Common.AABB;
        var progressionGen = new SdkProgressionGenerator(seed: 42);
        var melodyGen = new SdkMelodyGenerator(seed: 42);

        // Act - Generate progression (8 chords for 8 bars)
        var progression = progressionGen.Generate(scale, length: 8);

        // Generate melody over progression
        var melodyOptions = new SdkMelodyOptions
        {
            Range = SdkPitchRange.Instrument.Flute,
            Contour = SdkContourShape.Arch,
            IntervalPreferences = style.IntervalPreferences,
            TicksPerBeat = 480,
            Seed = 42
        };
        var melody = melodyGen.Generate(progression, scale, melodyOptions);

        // Voice the chords
        var voiceLeader = new SdkVoiceLeader();
        var (voicings, _) = voiceLeader.Voice(
            progression.Select(p => p.Chord).ToList(),
            voiceCount: 4);

        // Render to MIDI-JSON
        var midiJson = SdkMidiJsonRenderer.RenderComposition(
            melody,
            voicings,
            tempo: 120,
            meter: SdkMeter.Common.CommonTime,
            key: scale,
            name: "Celtic Test Tune");

        // Assert - Valid MIDI-JSON structure
        Assert.NotNull(midiJson);
        Assert.NotNull(midiJson.Header);
        Assert.Equal(480, midiJson.TicksPerBeat);
        Assert.True(midiJson.Tracks.Count >= 1, "Should have at least melody track");

        // Verify header metadata
        Assert.Equal("Celtic Test Tune", midiJson.Header.Name);
        Assert.NotNull(midiJson.Header.Tempos);
        Assert.Single(midiJson.Header.Tempos);
        Assert.Equal(120.0, midiJson.Header.Tempos[0].Bpm);
        Assert.NotNull(midiJson.Header.KeySignatures);
        Assert.Equal(SdkPitchClass.D, midiJson.Header.KeySignatures[0].Tonic);
        Assert.Equal(SdkModeType.Dorian, midiJson.Header.KeySignatures[0].Mode);

        // Verify melody track has notes
        var melodyTrack = midiJson.Tracks[0];
        Assert.Equal("Melody", melodyTrack.Name);
        Assert.True(melodyTrack.Events.Count > 0, "Melody track should have note events");

        // All notes should be NoteOn events with valid MIDI numbers
        Assert.All(melodyTrack.Events, evt =>
        {
            Assert.Equal(SdkMidiEventType.NoteOn, evt.Type);
            Assert.NotNull(evt.Note);
            Assert.InRange(evt.Note.Value, 0, 127);
            Assert.NotNull(evt.Velocity);
            Assert.InRange(evt.Velocity.Value, 1, 127);
        });
    }

    [Fact]
    public void Pipeline_JazzStyle_GeneratesValidMidiJson()
    {
        // Arrange - Jazz style with Bb Major (common jazz key) - using As (A#) for Bb
        var style = SdkBuiltInStyles.Jazz;
        var scale = new SdkScale(SdkPitchClass.As, SdkModeType.Major);
        var progressionGen = new SdkProgressionGenerator(seed: 123);
        var melodyGen = new SdkMelodyGenerator(seed: 123);

        // Act - Generate ii-V-I progression (jazz standard)
        var progression = progressionGen.GenerateFromPattern(scale, "ii-V-I");

        // Generate melody
        var melodyOptions = new SdkMelodyOptions
        {
            IntervalPreferences = style.IntervalPreferences,
            Contour = SdkContourShape.Wave,
            TicksPerBeat = 480,
            Seed = 123
        };
        var melody = melodyGen.Generate(progression, scale, melodyOptions);

        // Render
        var midiJson = SdkMidiJsonRenderer.RenderMelody(melody);

        // Assert
        Assert.NotNull(midiJson);
        Assert.Single(midiJson.Tracks);
        Assert.True(midiJson.Tracks[0].Events.Count > 0);
    }

    [Fact]
    public void Pipeline_FormStructure_AABB_CreatesCorrectSections()
    {
        // Arrange - AABB form (typical Celtic)
        var form = SdkForm.Common.AABB;
        var expanded = new SdkExpandedForm(form, barsPerSection: 8);

        // Act
        var sections = expanded.Sections.ToList();

        // Assert - 4 sections with correct labels
        Assert.Equal(4, sections.Count);
        Assert.Equal("A", sections[0].Section.Label);
        Assert.Equal("A", sections[1].Section.Label);
        Assert.Equal("B", sections[2].Section.Label);
        Assert.Equal("B", sections[3].Section.Label);

        // Verify bar assignments
        Assert.Equal(0, sections[0].StartBar);
        Assert.Equal(8, sections[0].Bars);
        Assert.Equal(8, sections[1].StartBar);
        Assert.Equal(16, sections[2].StartBar);
        Assert.Equal(24, sections[3].StartBar);
        Assert.Equal(32, expanded.TotalBars);
    }

    [Fact]
    public void Pipeline_MelodyMatchesProgression_AllNotesWithinChordDuration()
    {
        // Arrange
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);
        var progressionGen = new SdkProgressionGenerator(seed: 42);
        var melodyGen = new SdkMelodyGenerator(seed: 42);

        // Create a simple I-IV-V-I progression
        var progression = progressionGen.GenerateFromPattern(scale, "I-IV-V-I");
        var ticksPerBeat = 480;

        // Generate melody
        var melody = melodyGen.Generate(progression, scale, new SdkMelodyOptions
        {
            TicksPerBeat = ticksPerBeat,
            Seed = 42
        });

        // Calculate total progression duration
        var totalProgressionTicks = (int)(progression.Sum(p => p.DurationBeats) * ticksPerBeat);

        // Assert - All melody notes should be within the progression duration
        Assert.All(melody, note =>
        {
            Assert.True(note.StartTick >= 0, $"Note starts at negative tick: {note.StartTick}");
            Assert.True(note.EndTick <= totalProgressionTicks + ticksPerBeat,
                $"Note ends at {note.EndTick} but progression ends at {totalProgressionTicks}");
        });
    }

    [Fact]
    public void Pipeline_VoiceLeading_ProducesValidVoicings()
    {
        // Arrange - Common progression
        var scale = new SdkScale(SdkPitchClass.G, SdkModeType.Major);
        var progressionGen = new SdkProgressionGenerator(seed: 42);
        var progression = progressionGen.GenerateFromPattern(scale, "I-V-vi-IV");

        var voiceLeader = new SdkVoiceLeader(SdkVoiceLeadingRules.Standard);

        // Act
        var (voicings, violations) = voiceLeader.Voice(
            progression.Select(p => p.Chord).ToList(),
            voiceCount: 4);

        // Assert - Should have 4 voicings (one per chord)
        Assert.Equal(4, voicings.Count);

        // Each voicing should have 4 voices
        Assert.All(voicings, v => Assert.Equal(4, v.VoiceCount));

        // Violations should be minimal for this common progression
        Assert.True(violations.Count < 5, $"Too many voice leading violations: {violations.Count}");
    }

    [Fact]
    public void Pipeline_MidiJsonRoundTrip_PreservesData()
    {
        // Arrange - Create a simple MIDI-JSON
        var notes = new List<SdkMelodyNote>
        {
            new(new SdkPitch(SdkPitchClass.C, 4), 0, 480, 80),
            new(new SdkPitch(SdkPitchClass.E, 4), 480, 480, 75),
            new(new SdkPitch(SdkPitchClass.G, 4), 960, 960, 85)
        };
        var original = SdkMidiJsonRenderer.RenderMelody(notes, ticksPerBeat: 480, trackName: "Test");

        // Act - Serialize and deserialize
        var json = original.ToJson();
        var restored = SdkMidiJson.FromJson(json);

        // Assert - Data preserved
        Assert.Equal(original.TicksPerBeat, restored.TicksPerBeat);
        Assert.Equal(original.Tracks.Count, restored.Tracks.Count);
        Assert.Equal(original.Tracks[0].Events.Count, restored.Tracks[0].Events.Count);

        for (int i = 0; i < original.Tracks[0].Events.Count; i++)
        {
            var origEvt = original.Tracks[0].Events[i];
            var restEvt = restored.Tracks[0].Events[i];
            Assert.Equal(origEvt.Tick, restEvt.Tick);
            Assert.Equal(origEvt.Type, restEvt.Type);
            Assert.Equal(origEvt.Note, restEvt.Note);
            Assert.Equal(origEvt.Velocity, restEvt.Velocity);
            Assert.Equal(origEvt.Duration, restEvt.Duration);
        }
    }
}

/// <summary>
/// Integration tests for motif development workflows.
/// Tests extracting, transforming, and realizing motifs.
/// </summary>
public class MotifDevelopmentTests
{
    [Fact]
    public void Motif_ExtractAndRealize_RoundTrip()
    {
        // Arrange - Create original notes
        var originalNotes = new List<SdkMelodyNote>
        {
            new(new SdkPitch(SdkPitchClass.C, 4), 0, 480, 80), // C4
            new(new SdkPitch(SdkPitchClass.E, 4), 480, 480, 80), // E4 (+4)
            new(new SdkPitch(SdkPitchClass.G, 4), 960, 480, 80) // G4 (+7)
        };

        // Act - Extract motif and realize at same starting pitch
        var motif = SdkMotif.FromNotes(originalNotes);
        var realized = motif.Realize(
            new SdkPitch(SdkPitchClass.C, 4),
            ticksPerBeat: 480);

        // Assert - Should produce same pitches
        Assert.Equal(3, realized.Count);
        Assert.Equal(60, realized[0].Pitch.MidiNumber); // C4
        Assert.Equal(64, realized[1].Pitch.MidiNumber); // E4
        Assert.Equal(67, realized[2].Pitch.MidiNumber); // G4
    }

    [Fact]
    public void Motif_TransposeAndRealize_ShiftsProperly()
    {
        // Arrange - C major triad motif
        var motif = new SdkMotif([0, 4, 7], [1.0, 1.0, 2.0]);

        // Act - Transpose up a whole step (2 semitones) and realize from D4
        var transposed = motif.Transpose(2);
        var realized = transposed.Realize(
            new SdkPitch(SdkPitchClass.C, 4), // Start from C4, but intervals are shifted
            ticksPerBeat: 480);

        // Assert - Intervals are now 2, 6, 9 (D, F#, A)
        Assert.Equal(62, realized[0].Pitch.MidiNumber); // D4
        Assert.Equal(66, realized[1].Pitch.MidiNumber); // F#4
        Assert.Equal(69, realized[2].Pitch.MidiNumber); // A4
    }

    [Fact]
    public void Motif_InvertAndSequence_CreatesDevelopment()
    {
        // Arrange - Simple ascending motif
        var motif = SdkMotif.Patterns.ScaleUp; // 0, 2, 4, 5

        // Act - Invert to get descending
        var inverted = motif.Invert(); // 0, -2, -4, -5

        // Sequence the inverted motif 2 times, stepping down by 2
        var developed = inverted.Sequence(-2, 2);

        // Realize from G4
        var realized = developed.Realize(
            new SdkPitch(SdkPitchClass.G, 4),
            ticksPerBeat: 480);

        // Assert - Should have 8 notes (4 * 2)
        Assert.Equal(8, realized.Count);

        // First group: G, F, E, D (descending from G)
        Assert.Equal(67, realized[0].Pitch.MidiNumber); // G4
        Assert.Equal(65, realized[1].Pitch.MidiNumber); // F4
        Assert.Equal(63, realized[2].Pitch.MidiNumber); // Eb4
        Assert.Equal(62, realized[3].Pitch.MidiNumber); // D4

        // Second group: same pattern but 2 semitones lower
        Assert.Equal(65, realized[4].Pitch.MidiNumber); // F4
    }

    [Fact]
    public void Motif_AugmentAndDiminish_AffectsDuration()
    {
        // Arrange
        var motif = new SdkMotif([0, 2], [1.0, 1.0]);

        // Act - Augment by 2x
        var augmented = motif.Augment(2.0);
        var augNotes = augmented.Realize(
            new SdkPitch(SdkPitchClass.C, 4),
            ticksPerBeat: 480);

        // Diminish by 2x
        var diminished = motif.Diminish(2.0);
        var dimNotes = diminished.Realize(
            new SdkPitch(SdkPitchClass.C, 4),
            ticksPerBeat: 480);

        // Assert - Augmented has double duration
        Assert.Equal(960, augNotes[0].DurationTicks); // 2.0 * 480
        Assert.Equal(960, augNotes[1].DurationTicks);

        // Diminished has half duration
        Assert.Equal(240, dimNotes[0].DurationTicks); // 0.5 * 480
        Assert.Equal(240, dimNotes[1].DurationTicks);
    }

    [Fact]
    public void Motif_Retrograde_ReversesPattern()
    {
        // Arrange - Ascending triad: intervals 0, 4, 7
        var motif = SdkMotif.Patterns.ArpeggioUp;

        // Act
        var retrograde = motif.Retrograde();

        // Assert - Retrograde recalculates intervals from new starting point
        // Original: [0, 4, 7] with lastInterval = 7
        // Retrograde: [7-7, 7-4, 7-0] = [0, 3, 7]
        Assert.Equal(3, retrograde.Length);
        Assert.Equal(0, retrograde.Intervals[0]); // First note is always 0
        Assert.Equal(3, retrograde.Intervals[1]); // 7 - 4 = 3
        Assert.Equal(7, retrograde.Intervals[2]); // 7 - 0 = 7

        // Durations should be reversed
        var originalDurations = motif.Durations.ToList();
        var retrogradeDurations = retrograde.Durations.ToList();
        Assert.Equal(originalDurations[2], retrogradeDurations[0]);
        Assert.Equal(originalDurations[1], retrogradeDurations[1]);
        Assert.Equal(originalDurations[0], retrogradeDurations[2]);
    }

    [Fact]
    public void Motif_CombinedTransformations_ChainCorrectly()
    {
        // Arrange - Simple motif
        var motif = new SdkMotif([0, 2, 4], [1.0, 1.0, 1.0]);

        // Act - Chain: Transpose up, Invert, Augment
        var developed = motif
            .Transpose(5) // Up a P4
            .Invert() // Flip direction
            .Augment(1.5); // Stretch by 1.5x

        var realized = developed.Realize(
            new SdkPitch(SdkPitchClass.C, 4),
            ticksPerBeat: 480);

        // Assert - Transformations applied correctly
        Assert.Equal(3, realized.Count);

        // Durations should be 1.5x original
        Assert.Equal(720, realized[0].DurationTicks); // 1.5 * 480

        // Intervals: (0, 2, 4) + 5 = (5, 7, 9), then inverted = (-5, -7, -9)
        Assert.Equal(55, realized[0].Pitch.MidiNumber); // C4 - 5 = G3
        Assert.Equal(53, realized[1].Pitch.MidiNumber); // C4 - 7 = F3
        Assert.Equal(51, realized[2].Pitch.MidiNumber); // C4 - 9 = Eb3
    }
}

/// <summary>
/// Integration tests for style-based generation.
/// Verifies that generated music matches style characteristics.
/// </summary>
public class StyleAdherenceTests
{
    [Fact]
    public void StyleDistribution_Celtic_MatchesExpectedModeRatios()
    {
        // Arrange - Sample many mode selections from Celtic style
        var style = SdkBuiltInStyles.Celtic;
        var random = new Random(42);
        var sampleSize = 1000;

        var modeCounts = new Dictionary<SdkModeType, int>();

        // Act - Sample mode distribution
        for (int i = 0; i < sampleSize; i++)
        {
            var mode = style.SelectMode(random);
            modeCounts[mode] = modeCounts.GetValueOrDefault(mode) + 1;
        }

        // Assert - Distribution should roughly match:
        // Major ~64%, Minor ~16%, Dorian ~14%, Mixolydian ~6%
        var majorPct = modeCounts.GetValueOrDefault(SdkModeType.Major) / (double)sampleSize;
        var minorPct = modeCounts.GetValueOrDefault(SdkModeType.Minor) / (double)sampleSize;
        var dorianPct = modeCounts.GetValueOrDefault(SdkModeType.Dorian) / (double)sampleSize;

        // Allow 10% tolerance
        Assert.InRange(majorPct, 0.54, 0.74); // 64% ± 10%
        Assert.InRange(minorPct, 0.06, 0.26); // 16% ± 10%
        Assert.InRange(dorianPct, 0.04, 0.24); // 14% ± 10%
    }

    [Fact]
    public void StyleIntervals_Celtic_FavorsStepwiseMotion()
    {
        // Arrange - Celtic prefers steps
        var celticPrefs = SdkIntervalPreferences.Celtic;
        var jazzPrefs = SdkIntervalPreferences.Jazz;
        var random = new Random(42);
        var sampleSize = 500;

        // Act - Count step vs leap for each style
        int celticSteps = 0, celticLeaps = 0;
        int jazzSteps = 0, jazzLeaps = 0;

        for (int i = 0; i < sampleSize; i++)
        {
            var celticInterval = celticPrefs.SelectInterval(random);
            if (celticInterval <= 2) celticSteps++; else celticLeaps++;

            var jazzInterval = jazzPrefs.SelectInterval(random);
            if (jazzInterval <= 2) jazzSteps++; else jazzLeaps++;
        }

        // Assert - Celtic should have higher step ratio than Jazz
        var celticStepRatio = celticSteps / (double)(celticSteps + celticLeaps);
        var jazzStepRatio = jazzSteps / (double)(jazzSteps + jazzLeaps);

        Assert.True(celticStepRatio > jazzStepRatio,
            $"Celtic step ratio ({celticStepRatio:P1}) should exceed Jazz ({jazzStepRatio:P1})");
    }

    [Fact]
    public void BuiltInStyles_AllStylesHaveRequiredFields()
    {
        // Act & Assert - All built-in styles should be complete
        foreach (var style in SdkBuiltInStyles.All)
        {
            Assert.False(string.IsNullOrEmpty(style.Id), $"Style missing Id");
            Assert.False(string.IsNullOrEmpty(style.Name), $"Style {style.Id} missing Name");
            Assert.False(string.IsNullOrEmpty(style.Category), $"Style {style.Id} missing Category");
            Assert.NotNull(style.ModeDistribution);
            Assert.NotNull(style.IntervalPreferences);
            Assert.True(style.DefaultTempo > 0, $"Style {style.Id} has invalid tempo");
        }
    }

    [Fact]
    public void BuiltInStyles_GetById_ReturnsCorrectStyle()
    {
        // Act & Assert
        Assert.Equal("Celtic", SdkBuiltInStyles.GetById("celtic")?.Name);
        Assert.Equal("Jazz", SdkBuiltInStyles.GetById("jazz")?.Name);
        Assert.Equal("Baroque", SdkBuiltInStyles.GetById("baroque")?.Name);
        Assert.Null(SdkBuiltInStyles.GetById("nonexistent"));
    }

    [Fact]
    public void Style_TuneTypes_CelticHasExpectedTypes()
    {
        // Arrange
        var celtic = SdkBuiltInStyles.Celtic;

        // Assert
        Assert.True(celtic.TuneTypes.Count >= 4, "Celtic should have reel, jig, polka, hornpipe");

        var reel = celtic.GetTuneType("reel");
        Assert.NotNull(reel);
        Assert.Equal(4, reel.Meter.Numerator);
        Assert.Equal(4, reel.Meter.Denominator);

        var jig = celtic.GetTuneType("jig");
        Assert.NotNull(jig);
        Assert.Equal(6, jig.Meter.Numerator);
        Assert.Equal(8, jig.Meter.Denominator);
    }
}

/// <summary>
/// Integration tests for phrase building and structural organization.
/// </summary>
public class PhraseStructureTests
{
    [Fact]
    public void PhraseBuilder_CreatesPhraseWithNotes()
    {
        // Arrange
        var builder = new SdkPhraseBuilder(startTick: 0)
            .WithType(SdkPhraseType.Antecedent);

        // Act - Add some notes
        builder.AddNote(new SdkPitch(SdkPitchClass.C, 4), durationTicks: 480);
        builder.AddNote(new SdkPitch(SdkPitchClass.D, 4), durationTicks: 480);
        builder.AddRest(durationTicks: 240);
        builder.AddNote(new SdkPitch(SdkPitchClass.E, 4), durationTicks: 720);
        builder.WithCadence(SdkCadenceType.Half);

        var phrase = builder.Build();

        // Assert
        Assert.Equal(SdkPhraseType.Antecedent, phrase.Type);
        Assert.Equal(0, phrase.StartTick);
        Assert.Equal(3, phrase.Notes.Count);
        Assert.Equal(SdkCadenceType.Half, phrase.EndingCadence);

        // Duration includes rest
        Assert.Equal(480 + 480 + 240 + 720, phrase.DurationTicks);

        // Notes have correct timings
        Assert.Equal(0, phrase.Notes[0].StartTick);
        Assert.Equal(480, phrase.Notes[1].StartTick);
        Assert.Equal(480 + 480 + 240, phrase.Notes[2].StartTick); // After rest
    }

    [Fact]
    public void PhraseBuilder_WithHarmony_IncludesProgression()
    {
        // Arrange
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);
        var progressionGen = new SdkProgressionGenerator(seed: 42);
        var progression = progressionGen.GenerateFromPattern(scale, "I-V");

        var builder = new SdkPhraseBuilder(startTick: 0)
            .WithType(SdkPhraseType.Consequent)
            .WithHarmony(progression)
            .WithCadence(SdkCadenceType.AuthenticPerfect);

        // Add melody notes
        builder.AddNote(new SdkPitch(SdkPitchClass.G, 4), 960);
        builder.AddNote(new SdkPitch(SdkPitchClass.C, 5), 960);

        var phrase = builder.Build();

        // Assert
        Assert.NotNull(phrase.Harmony);
        Assert.Equal(2, phrase.Harmony.Count);
        Assert.Equal(SdkCadenceType.AuthenticPerfect, phrase.EndingCadence);
    }

    [Fact]
    public void ExpandedForm_GetSectionsByLabel_ReturnsAllMatches()
    {
        // Arrange
        var form = SdkForm.Common.AABA;
        var expanded = new SdkExpandedForm(form, barsPerSection: 8);

        // Act
        var aSections = expanded.GetSectionsByLabel("A").ToList();
        var bSections = expanded.GetSectionsByLabel("B").ToList();

        // Assert - AABA has 3 A sections and 1 B section
        Assert.Equal(3, aSections.Count);
        Assert.Single(bSections);

        // Verify bar positions
        Assert.Equal(0, aSections[0].StartBar);
        Assert.Equal(8, aSections[1].StartBar);
        Assert.Equal(24, aSections[2].StartBar); // After B section
        Assert.Equal(16, bSections[0].StartBar);
    }
}

/// <summary>
/// Tests for MotifLibrary and NamedMotif cataloguing functionality.
/// </summary>
public class MotifLibraryTests
{
    [Fact]
    public void MotifLibrary_Add_IndexesByIdStyleAndCategory()
    {
        // Arrange
        var library = new SdkMotifLibrary(seed: 42);
        var motif = SdkNamedMotif.Create(
            "test-motif", "Test Motif",
            [0, 2, 4], [1.0, 1.0, 2.0],
            SdkMotifCategory.Melodic,
            styleId: "celtic",
            description: "Test melodic pattern");

        // Act
        library.Add(motif);

        // Assert - Can retrieve by ID
        var byId = library.Get("test-motif");
        Assert.NotNull(byId);
        Assert.Equal("Test Motif", byId.Name);

        // Assert - Indexed by style
        var byStyle = library.GetByStyle("celtic").ToList();
        Assert.Contains(byStyle, m => m.Id == "test-motif");

        // Assert - Indexed by category
        var byCategory = library.GetByCategory(SdkMotifCategory.Melodic).ToList();
        Assert.Contains(byCategory, m => m.Id == "test-motif");
    }

    [Fact]
    public void MotifLibrary_UniversalMotifs_IncludedInAllStyles()
    {
        // Arrange
        var library = new SdkMotifLibrary(seed: 42);

        // Add universal motif (no styleId)
        library.Add(SdkNamedMotif.Create(
            "universal-1", "Universal Pattern",
            [0, 2, 0], [1.0, 0.5, 1.5],
            SdkMotifCategory.Ornament));

        // Add style-specific motif
        library.Add(SdkNamedMotif.Create(
            "celtic-only", "Celtic Pattern",
            [0, 2, 4, 2], [0.25, 0.25, 0.25, 0.25],
            SdkMotifCategory.Ornament,
            styleId: "celtic"));

        // Act
        var celticMotifs = library.GetByStyle("celtic").ToList();
        var jazzMotifs = library.GetByStyle("jazz").ToList();

        // Assert - Celtic gets both universal and celtic-specific
        Assert.Equal(2, celticMotifs.Count);
        Assert.Contains(celticMotifs, m => m.Id == "universal-1");
        Assert.Contains(celticMotifs, m => m.Id == "celtic-only");

        // Assert - Jazz only gets universal (no jazz-specific motifs added)
        Assert.Single(jazzMotifs);
        Assert.Contains(jazzMotifs, m => m.Id == "universal-1");
    }

    [Fact]
    public void MotifLibrary_GetByStyleAndCategory_FiltersCorrectly()
    {
        // Arrange
        var library = new SdkMotifLibrary(seed: 42);

        library.Add(SdkNamedMotif.Create("celtic-orn", "Celtic Ornament",
            [0, 2, 0], [0.25, 0.25, 0.5], SdkMotifCategory.Ornament, styleId: "celtic"));
        library.Add(SdkNamedMotif.Create("celtic-mel", "Celtic Melodic",
            [0, 2, 4, 5], [1.0, 1.0, 1.0, 1.0], SdkMotifCategory.Melodic, styleId: "celtic"));
        library.Add(SdkNamedMotif.Create("jazz-orn", "Jazz Ornament",
            [1, -1, 0], [0.5, 0.5, 1.0], SdkMotifCategory.Ornament, styleId: "jazz"));

        // Act
        var celticOrnaments = library.GetByStyleAndCategory("celtic", SdkMotifCategory.Ornament).ToList();

        // Assert - Only celtic ornaments, not celtic melodic or jazz ornaments
        Assert.Single(celticOrnaments);
        Assert.Equal("celtic-orn", celticOrnaments[0].Id);
    }

    [Fact]
    public void MotifLibrary_SelectWeighted_RespectsWeights()
    {
        // Arrange
        var library = new SdkMotifLibrary(seed: 42);

        // Add motif with high weight (10x more likely)
        library.Add(SdkNamedMotif.Create("high-weight", "High Weight",
            [0, 2], [1.0, 1.0], SdkMotifCategory.Melodic, weight: 10.0));

        // Add motif with low weight
        library.Add(SdkNamedMotif.Create("low-weight", "Low Weight",
            [0, -2], [1.0, 1.0], SdkMotifCategory.Melodic, weight: 1.0));

        // Act - Select many times (initialize counts to avoid KeyNotFound)
        var selections = new Dictionary<string, int> { ["high-weight"] = 0, ["low-weight"] = 0 };
        for (int i = 0; i < 200; i++)
        {
            var selected = library.SelectWeighted();
            Assert.NotNull(selected);
            selections[selected.Id]++;
        }

        // Assert - High weight motif should be selected more often
        // With 10:1 ratio and 200 selections, expect ~182:18 split
        Assert.True(selections["high-weight"] > selections["low-weight"],
            $"High weight: {selections["high-weight"]}, Low weight: {selections["low-weight"]}");
    }

    [Fact]
    public void MotifLibrary_DuplicateId_ThrowsException()
    {
        // Arrange
        var library = new SdkMotifLibrary();
        library.Add(SdkNamedMotif.Create("duplicate-id", "First",
            [0, 2], [1.0, 1.0], SdkMotifCategory.Melodic));

        // Act & Assert
        var ex = Assert.Throws<ArgumentException>(() =>
            library.Add(SdkNamedMotif.Create("duplicate-id", "Second",
                [0, 4], [0.5, 0.5], SdkMotifCategory.Melodic)));

        Assert.Contains("duplicate-id", ex.Message);
    }

    [Fact]
    public void BuiltInMotifs_ContainsCelticPatterns()
    {
        // Act
        var celticMotifs = SdkBuiltInMotifs.Celtic.ToList();

        // Assert - Should have Celtic-specific patterns
        Assert.Contains(celticMotifs, m => m.Id == "celtic-roll");
        Assert.Contains(celticMotifs, m => m.Id == "celtic-triplet");
        Assert.Contains(celticMotifs, m => m.Id == "celtic-leap-step");

        // Assert - Should also include universal motifs
        Assert.Contains(celticMotifs, m => m.Id == "scale-up");
        Assert.Contains(celticMotifs, m => m.Id == "arpeggio-up");
    }

    [Fact]
    public void BuiltInMotifs_ContainsJazzPatterns()
    {
        // Act
        var jazzMotifs = SdkBuiltInMotifs.Jazz.ToList();

        // Assert - Should have Jazz-specific patterns
        Assert.Contains(jazzMotifs, m => m.Id == "jazz-approach");
        Assert.Contains(jazzMotifs, m => m.Id == "jazz-enclosure");
        Assert.Contains(jazzMotifs, m => m.Id == "jazz-bebop-scale");

        // Verify enclosure pattern intervals (above, below, target)
        var enclosure = jazzMotifs.First(m => m.Id == "jazz-enclosure");
        Assert.Equal([1, -1, 0], enclosure.Motif.Intervals.ToArray());
    }

    [Fact]
    public void BuiltInMotifs_ContainsCadentialPatterns()
    {
        // Act
        var cadentialMotifs = SdkBuiltInMotifs.Library.GetByCategory(SdkMotifCategory.Cadential).ToList();

        // Assert
        Assert.True(cadentialMotifs.Count >= 2);
        Assert.Contains(cadentialMotifs, m => m.Id == "cadence-fall");
        Assert.Contains(cadentialMotifs, m => m.Id == "cadence-resolution");

        // Verify cadential fall ends on tonic (interval 0)
        var fall = cadentialMotifs.First(m => m.Id == "cadence-fall");
        Assert.Equal(0, fall.Motif.Intervals.Last());
    }

    [Fact]
    public void NamedMotif_UnderlyingMotif_SupportsTransformations()
    {
        // Arrange
        var namedMotif = SdkBuiltInMotifs.Get("scale-up");
        Assert.NotNull(namedMotif);

        // Act - Apply transformations to underlying motif
        var inverted = namedMotif.Motif.Invert();
        var augmented = namedMotif.Motif.Augment(2.0);

        // Assert - Original intervals [0, 2, 4, 5] become [0, -2, -4, -5]
        Assert.Equal([0, -2, -4, -5], inverted.Intervals.ToArray());

        // Assert - Durations doubled
        Assert.Equal([2.0, 2.0, 2.0, 2.0], augmented.Durations.ToArray());
    }

    [Fact]
    public void NamedMotif_CreateWithValidation_EnforcesConstraints()
    {
        // Act & Assert - Empty ID throws
        Assert.Throws<ArgumentException>(() =>
            SdkNamedMotif.Create("", "Name", [0, 2], [1.0, 1.0], SdkMotifCategory.Melodic));

        // Act & Assert - Empty name throws
        Assert.Throws<ArgumentException>(() =>
            SdkNamedMotif.Create("id", "", [0, 2], [1.0, 1.0], SdkMotifCategory.Melodic));

        // Act & Assert - Negative weight becomes 1.0
        var motif = SdkNamedMotif.Create("test", "Test",
            [0, 2], [1.0, 1.0], SdkMotifCategory.Melodic, weight: -5.0);
        Assert.Equal(1.0, motif.Weight);
    }
}

/// <summary>
/// Tests for StyleDefinition integration with MotifLibrary.
/// </summary>
public class StyleMotifIntegrationTests
{
    [Fact]
    public void StyleDefinition_GetCharacteristicMotifs_ReturnsMotifsById()
    {
        // Arrange
        var style = SdkBuiltInStyles.Celtic;
        var library = SdkBuiltInMotifs.Library;

        // Act
        var motifs = style.GetCharacteristicMotifs(library).ToList();

        // Assert - Should return the Celtic-specific motifs
        Assert.Equal(3, motifs.Count);
        Assert.Contains(motifs, m => m.Id == "celtic-roll");
        Assert.Contains(motifs, m => m.Id == "celtic-triplet");
        Assert.Contains(motifs, m => m.Id == "celtic-leap-step");
    }

    [Fact]
    public void StyleDefinition_GetAvailableMotifs_IncludesUniversal()
    {
        // Arrange
        var style = SdkBuiltInStyles.Celtic;
        var library = SdkBuiltInMotifs.Library;

        // Act
        var motifs = style.GetAvailableMotifs(library).ToList();

        // Assert - Should include both Celtic-specific and universal motifs
        Assert.Contains(motifs, m => m.Id == "celtic-roll");
        Assert.Contains(motifs, m => m.Id == "scale-up");
        Assert.Contains(motifs, m => m.Id == "arpeggio-up");
    }

    [Fact]
    public void StyleDefinition_SelectMotif_ReturnsAppropriateCategory()
    {
        // Arrange
        var style = SdkBuiltInStyles.Jazz;
        var library = SdkBuiltInMotifs.Library;

        // Act - Select an ornament motif for Jazz
        var motif = style.SelectMotif(library, SdkMotifCategory.Ornament);

        // Assert - Should return an ornament (jazz-enclosure is the jazz-specific ornament)
        Assert.NotNull(motif);
        Assert.Equal(SdkMotifCategory.Ornament, motif.Category);
    }

    [Fact]
    public void StyleDefinition_JazzCharacteristicMotifs_IncludesAllThree()
    {
        // Arrange
        var style = SdkBuiltInStyles.Jazz;
        var library = SdkBuiltInMotifs.Library;

        // Act
        var motifs = style.GetCharacteristicMotifs(library).ToList();

        // Assert
        Assert.Equal(3, motifs.Count);
        Assert.Contains(motifs, m => m.Id == "jazz-approach");
        Assert.Contains(motifs, m => m.Id == "jazz-enclosure");
        Assert.Contains(motifs, m => m.Id == "jazz-bebop-scale");
    }

    [Fact]
    public void StyleDefinition_BaroqueCharacteristicMotifs_IncludesPatterns()
    {
        // Arrange
        var style = SdkBuiltInStyles.Baroque;
        var library = SdkBuiltInMotifs.Library;

        // Act
        var motifs = style.GetCharacteristicMotifs(library).ToList();

        // Assert
        Assert.Equal(2, motifs.Count);
        Assert.Contains(motifs, m => m.Id == "baroque-sequence");
        Assert.Contains(motifs, m => m.Id == "baroque-trill-prep");
    }

    [Fact]
    public void StyleDefinition_WithCustomMotifLibrary_WorksCorrectly()
    {
        // Arrange - Create custom style with custom library
        var style = new SdkStyleDefinition
        {
            Id = "custom",
            Name = "Custom Style",
            CharacteristicMotifIds = ["my-motif-1", "my-motif-2"]
        };

        var library = new SdkMotifLibrary();
        library.Add(SdkNamedMotif.Create("my-motif-1", "My Motif 1",
            [0, 2, 4], [1.0, 1.0, 1.0], SdkMotifCategory.Melodic, styleId: "custom"));
        library.Add(SdkNamedMotif.Create("my-motif-2", "My Motif 2",
            [0, -2, -4], [0.5, 0.5, 1.0], SdkMotifCategory.Melodic, styleId: "custom"));

        // Act
        var characteristic = style.GetCharacteristicMotifs(library).ToList();
        var available = style.GetAvailableMotifs(library).ToList();

        // Assert
        Assert.Equal(2, characteristic.Count);
        Assert.Equal(2, available.Count); // Only custom motifs, no universal in this library
    }
}

/// <summary>
/// Tests for MelodyGenerator motif integration.
/// </summary>
public class MelodyGeneratorMotifTests
{
    [Fact]
    public void MelodyGenerator_WithMotifLibrary_GeneratesMelody()
    {
        // Arrange
        var generator = new SdkMelodyGenerator(seed: 42);
        var scale = new SdkScale(SdkPitchClass.C, SdkModeType.Major);
        var progressionGen = new SdkProgressionGenerator(seed: 42);
        var progression = progressionGen.Generate(scale, length: 4);

        var options = new SdkMelodyOptions
        {
            MotifLibrary = SdkBuiltInMotifs.Library,
            StyleId = "celtic",
            MotifProbability = 0.8, // High probability to trigger motifs
            Seed = 42
        };

        // Act
        var melody = generator.Generate(progression, scale, options);

        // Assert - Should generate a melody with notes
        Assert.NotNull(melody);
        Assert.True(melody.Count > 0, "Should generate melody notes");
    }

    [Fact]
    public void MelodyGenerator_WithHighMotifProbability_InsertsMotifs()
    {
        // Arrange
        var generator = new SdkMelodyGenerator(seed: 42);
        var scale = new SdkScale(SdkPitchClass.G, SdkModeType.Major);
        var progressionGen = new SdkProgressionGenerator(seed: 42);
        var progression = progressionGen.Generate(scale, length: 8);

        var optionsWithMotifs = new SdkMelodyOptions
        {
            MotifLibrary = SdkBuiltInMotifs.Library,
            MotifProbability = 1.0, // Always try to insert motifs
            Seed = 42
        };

        var optionsWithoutMotifs = new SdkMelodyOptions
        {
            MotifProbability = 0.0,
            Seed = 42
        };

        // Act
        var melodyWithMotifs = generator.Generate(progression, scale, optionsWithMotifs);
        var melodyWithout = generator.Generate(progression, scale, optionsWithoutMotifs);

        // Assert - Both should generate melodies (different patterns)
        Assert.True(melodyWithMotifs.Count > 0);
        Assert.True(melodyWithout.Count > 0);
    }

    [Fact]
    public void MelodyGenerator_WithStyleSpecificMotifs_UsesStyleId()
    {
        // Arrange
        var generator = new SdkMelodyGenerator(seed: 123);
        var scale = new SdkScale(SdkPitchClass.D, SdkModeType.Dorian);
        var progressionGen = new SdkProgressionGenerator(seed: 123);
        var progression = progressionGen.Generate(scale, length: 4);

        var options = new SdkMelodyOptions
        {
            MotifLibrary = SdkBuiltInMotifs.Library,
            StyleId = "jazz",
            MotifProbability = 0.7,
            Seed = 123
        };

        // Act
        var melody = generator.Generate(progression, scale, options);

        // Assert - Should complete without error
        Assert.NotNull(melody);
        Assert.True(melody.Count > 0);
    }

    [Fact]
    public void MelodyGenerator_WithoutMotifLibrary_GeneratesNormally()
    {
        // Arrange
        var generator = new SdkMelodyGenerator(seed: 42);
        var scale = new SdkScale(SdkPitchClass.A, SdkModeType.Minor);
        var progressionGen = new SdkProgressionGenerator(seed: 42);
        var progression = progressionGen.Generate(scale, length: 4);

        var options = new SdkMelodyOptions
        {
            // No MotifLibrary - should generate normally
            MotifProbability = 1.0, // Even with high probability, no motifs without library
            Seed = 42
        };

        // Act
        var melody = generator.Generate(progression, scale, options);

        // Assert
        Assert.NotNull(melody);
        Assert.True(melody.Count > 0);
    }

    [Fact]
    public void MelodyOptions_MotifProperties_HaveCorrectDefaults()
    {
        // Arrange & Act
        var options = new SdkMelodyOptions();

        // Assert
        Assert.Null(options.MotifLibrary);
        Assert.Null(options.StyleId);
        Assert.Equal(0.4, options.MotifProbability);
    }
}
