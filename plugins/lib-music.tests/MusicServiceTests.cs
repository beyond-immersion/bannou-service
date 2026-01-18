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
