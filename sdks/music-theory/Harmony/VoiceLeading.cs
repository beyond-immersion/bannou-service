using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;
using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.MusicTheory.Harmony;

/// <summary>
/// Voice leading rule violations.
/// </summary>
public enum VoiceLeadingViolationType
{
    /// <summary>Parallel perfect fifths between voices</summary>
    ParallelFifths,

    /// <summary>Parallel octaves between voices</summary>
    ParallelOctaves,

    /// <summary>Voice crossed another voice</summary>
    VoiceCrossing,

    /// <summary>Voice overlap (moving past previous position of adjacent voice)</summary>
    VoiceOverlap,

    /// <summary>Leap larger than allowed</summary>
    LargeLeap,

    /// <summary>Leap not resolved by step in opposite direction</summary>
    UnresolvedLeap,

    /// <summary>Doubled leading tone</summary>
    DoubledLeadingTone
}

/// <summary>
/// A detected voice leading violation.
/// </summary>
public sealed class VoiceLeadingViolation
{
    /// <summary>
    /// Type of violation.
    /// </summary>
    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public VoiceLeadingViolationType Type { get; }

    /// <summary>
    /// Position in the progression (0-based).
    /// </summary>
    [JsonPropertyName("position")]
    public int Position { get; }

    /// <summary>
    /// Voice indices involved (0 = bass).
    /// </summary>
    [JsonPropertyName("voices")]
    public IReadOnlyList<int> Voices { get; }

    /// <summary>
    /// Severity (true = error, false = warning).
    /// </summary>
    [JsonPropertyName("isError")]
    public bool IsError { get; }

    /// <summary>
    /// Human-readable description.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; }

    /// <summary>
    /// Creates a violation record.
    /// </summary>
    [JsonConstructor]
    public VoiceLeadingViolation(VoiceLeadingViolationType type, int position,
        IReadOnlyList<int> voices, bool isError, string message)
    {
        Type = type;
        Position = position;
        Voices = voices;
        IsError = isError;
        Message = message;
    }

    /// <inheritdoc />
    public override string ToString() => $"[{Position}] {Type}: {Message}";
}

/// <summary>
/// Voice leading rules configuration.
/// </summary>
public sealed class VoiceLeadingRules
{
    /// <summary>
    /// Avoid parallel perfect fifths.
    /// </summary>
    [JsonPropertyName("avoidParallelFifths")]
    public bool AvoidParallelFifths { get; set; } = true;

    /// <summary>
    /// Avoid parallel octaves.
    /// </summary>
    [JsonPropertyName("avoidParallelOctaves")]
    public bool AvoidParallelOctaves { get; set; } = true;

    /// <summary>
    /// Avoid voice crossing.
    /// </summary>
    [JsonPropertyName("avoidVoiceCrossing")]
    public bool AvoidVoiceCrossing { get; set; } = true;

    /// <summary>
    /// Prefer stepwise motion.
    /// </summary>
    [JsonPropertyName("preferStepwiseMotion")]
    public bool PreferStepwiseMotion { get; set; } = true;

    /// <summary>
    /// Maximum leap in semitones.
    /// </summary>
    [JsonPropertyName("maxLeap")]
    public int MaxLeap { get; set; } = 7; // Perfect fifth

    /// <summary>
    /// Require large leaps to resolve by step.
    /// </summary>
    [JsonPropertyName("requireLeapResolution")]
    public bool RequireLeapResolution { get; set; } = true;

    /// <summary>
    /// Avoid doubling the leading tone.
    /// </summary>
    [JsonPropertyName("avoidDoubledLeadingTone")]
    public bool AvoidDoubledLeadingTone { get; set; } = true;

    /// <summary>
    /// Standard voice leading rules (common practice).
    /// </summary>
    public static VoiceLeadingRules Standard => new();

    /// <summary>
    /// Relaxed rules for modern/jazz contexts.
    /// </summary>
    public static VoiceLeadingRules Relaxed => new()
    {
        AvoidParallelFifths = false,
        PreferStepwiseMotion = false,
        MaxLeap = 12,
        RequireLeapResolution = false
    };
}

/// <summary>
/// Applies voice leading to chord progressions.
/// </summary>
public sealed class VoiceLeader
{
    private readonly VoiceLeadingRules _rules;

    /// <summary>
    /// Creates a voice leader with specified rules.
    /// </summary>
    public VoiceLeader(VoiceLeadingRules? rules = null)
    {
        _rules = rules ?? VoiceLeadingRules.Standard;
    }

    /// <summary>
    /// Generates voiced chords with good voice leading.
    /// </summary>
    /// <param name="chords">Chord symbols to voice.</param>
    /// <param name="voiceCount">Number of voices.</param>
    /// <param name="ranges">Optional pitch ranges per voice.</param>
    /// <returns>Voicings and any violations.</returns>
    public (IReadOnlyList<Voicing> voicings, IReadOnlyList<VoiceLeadingViolation> violations)
        Voice(IReadOnlyList<Chord> chords, int voiceCount, IReadOnlyList<PitchRange>? ranges = null)
    {
        if (chords.Count == 0)
        {
            return ([], []);
        }

        ranges ??= GetDefaultRanges(voiceCount);
        var voicings = new List<Voicing>();
        var violations = new List<VoiceLeadingViolation>();

        // Voice the first chord
        var firstVoicing = VoiceFirstChord(chords[0], voiceCount, ranges);
        voicings.Add(firstVoicing);

        // Voice subsequent chords with voice leading
        for (var i = 1; i < chords.Count; i++)
        {
            var prevVoicing = voicings[i - 1];
            var nextVoicing = VoiceNextChord(chords[i], prevVoicing, ranges);
            voicings.Add(nextVoicing);

            // Check for violations
            var newViolations = CheckViolations(prevVoicing, nextVoicing, i);
            violations.AddRange(newViolations);
        }

        return (voicings, violations);
    }

    private Voicing VoiceFirstChord(Chord chord, int voiceCount, IReadOnlyList<PitchRange> ranges)
    {
        // Create a combined range spanning all voices
        var combinedRange = new PitchRange(ranges[0].Low, ranges[^1].High);

        // Use VoicingFactory.CreateInRange which respects voiceCount by doubling notes as needed
        return VoicingFactory.CreateInRange(chord, combinedRange, voiceCount);
    }

    private Voicing VoiceNextChord(Chord chord, Voicing previous, IReadOnlyList<PitchRange> ranges)
    {
        var prevPitches = previous.Pitches;
        var newPitches = new List<Pitch.Pitch>();

        // For each voice, find the nearest chord tone
        for (var voice = 0; voice < prevPitches.Count; voice++)
        {
            var prevPitch = prevPitches[voice];
            var range = voice < ranges.Count ? ranges[voice] : ranges[^1];

            var bestPitch = FindNearestChordTone(chord, prevPitch, range);
            newPitches.Add(bestPitch);
        }

        // Ensure bass has the bass note if specified
        if (chord.Bass.HasValue)
        {
            var bassTarget = chord.Bass.Value;
            var currentBass = newPitches[0];

            if (currentBass.PitchClass != bassTarget)
            {
                // Find bass note near current position
                var bassOctave = currentBass.Octave;
                var newBass = new Pitch.Pitch(bassTarget, bassOctave);

                // Adjust octave if needed to stay in range
                while (!ranges[0].Contains(newBass) && bassOctave > ranges[0].Low.Octave)
                {
                    bassOctave--;
                    newBass = new Pitch.Pitch(bassTarget, bassOctave);
                }

                newPitches[0] = newBass;
            }
        }

        // Sort pitches to ensure proper voicing order
        newPitches = newPitches.OrderBy(p => p.MidiNumber).ToList();

        return new Voicing(chord, newPitches);
    }

    private Pitch.Pitch FindNearestChordTone(Chord chord, Pitch.Pitch reference, PitchRange range)
    {
        var bestPitch = reference;
        var bestDistance = int.MaxValue;

        // Search nearby octaves for chord tones
        for (var octave = range.Low.Octave; octave <= range.High.Octave; octave++)
        {
            foreach (var pc in chord.PitchClasses)
            {
                var candidate = new Pitch.Pitch(pc, octave);

                if (!range.Contains(candidate))
                {
                    continue;
                }

                var distance = Math.Abs(candidate.MidiNumber - reference.MidiNumber);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestPitch = candidate;
                }
            }
        }

        return bestPitch;
    }

    private IEnumerable<VoiceLeadingViolation> CheckViolations(Voicing prev, Voicing next, int position)
    {
        var violations = new List<VoiceLeadingViolation>();
        var voiceCount = Math.Min(prev.VoiceCount, next.VoiceCount);

        for (var i = 0; i < voiceCount; i++)
        {
            // Check for large leaps
            var motion = next.Pitches[i].MidiNumber - prev.Pitches[i].MidiNumber;

            if (_rules.MaxLeap > 0 && Math.Abs(motion) > _rules.MaxLeap)
            {
                violations.Add(new VoiceLeadingViolation(
                    VoiceLeadingViolationType.LargeLeap,
                    position,
                    [i],
                    false,
                    $"Voice {i} leaps {Math.Abs(motion)} semitones"));
            }

            // Check for parallels with other voices
            for (var j = i + 1; j < voiceCount; j++)
            {
                var prevInterval = prev.Pitches[j].MidiNumber - prev.Pitches[i].MidiNumber;
                var nextInterval = next.Pitches[j].MidiNumber - next.Pitches[i].MidiNumber;

                // Parallel motion
                var iMotion = next.Pitches[i].MidiNumber - prev.Pitches[i].MidiNumber;
                var jMotion = next.Pitches[j].MidiNumber - prev.Pitches[j].MidiNumber;
                var isParallel = iMotion != 0 && iMotion == jMotion;

                // Check parallel fifths
                if (_rules.AvoidParallelFifths && isParallel &&
                    prevInterval % 12 == 7 && nextInterval % 12 == 7)
                {
                    violations.Add(new VoiceLeadingViolation(
                        VoiceLeadingViolationType.ParallelFifths,
                        position,
                        [i, j],
                        true,
                        $"Parallel fifths between voices {i} and {j}"));
                }

                // Check parallel octaves
                if (_rules.AvoidParallelOctaves && isParallel &&
                    prevInterval % 12 == 0 && nextInterval % 12 == 0 &&
                    prevInterval != 0 && nextInterval != 0)
                {
                    violations.Add(new VoiceLeadingViolation(
                        VoiceLeadingViolationType.ParallelOctaves,
                        position,
                        [i, j],
                        true,
                        $"Parallel octaves between voices {i} and {j}"));
                }
            }

            // Check voice crossing
            if (_rules.AvoidVoiceCrossing && i > 0)
            {
                if (next.Pitches[i].MidiNumber < next.Pitches[i - 1].MidiNumber)
                {
                    violations.Add(new VoiceLeadingViolation(
                        VoiceLeadingViolationType.VoiceCrossing,
                        position,
                        [i - 1, i],
                        false,
                        $"Voice {i} crosses below voice {i - 1}"));
                }
            }
        }

        return violations;
    }

    private static IReadOnlyList<PitchRange> GetDefaultRanges(int voiceCount)
    {
        return voiceCount switch
        {
            2 => [PitchRange.Vocal.Bass, PitchRange.Vocal.Soprano],
            3 => [PitchRange.Vocal.Bass, PitchRange.Vocal.Alto, PitchRange.Vocal.Soprano],
            4 => [PitchRange.Vocal.Bass, PitchRange.Vocal.Tenor, PitchRange.Vocal.Alto, PitchRange.Vocal.Soprano],
            _ => Enumerable.Range(0, voiceCount)
                .Select(i => PitchRange.FromMidi(36 + i * 12, 72 + i * 6))
                .ToList()
        };
    }
}
