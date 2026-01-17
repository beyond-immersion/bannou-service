using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Pitch;
using BeyondImmersion.Bannou.MusicTheory.Time;

namespace BeyondImmersion.Bannou.MusicTheory.Melody;

/// <summary>
/// A melodic note with pitch and timing.
/// </summary>
public sealed class MelodyNote
{
    /// <summary>
    /// The pitch of the note.
    /// </summary>
    public Pitch.Pitch Pitch { get; }

    /// <summary>
    /// Start position in ticks.
    /// </summary>
    public int StartTick { get; }

    /// <summary>
    /// Duration in ticks.
    /// </summary>
    public int DurationTicks { get; }

    /// <summary>
    /// Velocity (1-127).
    /// </summary>
    public int Velocity { get; }

    /// <summary>
    /// Creates a melody note.
    /// </summary>
    public MelodyNote(Pitch.Pitch pitch, int startTick, int durationTicks, int velocity = 80)
    {
        Pitch = pitch;
        StartTick = startTick;
        DurationTicks = durationTicks;
        Velocity = Math.Clamp(velocity, 1, 127);
    }

    /// <summary>
    /// Gets the end tick (non-inclusive).
    /// </summary>
    public int EndTick => StartTick + DurationTicks;

    public override string ToString() => $"{Pitch}@{StartTick}({DurationTicks}t)";
}

/// <summary>
/// Options for melody generation.
/// </summary>
public sealed class MelodyOptions
{
    /// <summary>
    /// Pitch range for the melody.
    /// </summary>
    public PitchRange? Range { get; set; }

    /// <summary>
    /// Overall melodic contour.
    /// </summary>
    public ContourShape Contour { get; set; } = ContourShape.Arch;

    /// <summary>
    /// Interval preferences.
    /// </summary>
    public IntervalPreferences? IntervalPreferences { get; set; }

    /// <summary>
    /// Rhythm pattern set to use.
    /// </summary>
    public RhythmPatternSet? RhythmPatterns { get; set; }

    /// <summary>
    /// Note density (0 = sparse, 1 = dense).
    /// </summary>
    public double Density { get; set; } = 0.6;

    /// <summary>
    /// Syncopation amount (0 = none, 1 = maximum).
    /// </summary>
    public double Syncopation { get; set; } = 0.2;

    /// <summary>
    /// Ticks per beat (PPQN).
    /// </summary>
    public int TicksPerBeat { get; set; } = 480;

    /// <summary>
    /// Random seed for reproducibility.
    /// </summary>
    public int? Seed { get; set; }
}

/// <summary>
/// Generates melodies over chord progressions.
/// </summary>
public sealed class MelodyGenerator
{
    private readonly Random _random;

    /// <summary>
    /// Creates a melody generator.
    /// </summary>
    public MelodyGenerator(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Generates a melody over a chord progression.
    /// </summary>
    /// <param name="progression">The chord progression.</param>
    /// <param name="scale">The scale/key.</param>
    /// <param name="options">Generation options.</param>
    /// <returns>The generated melody.</returns>
    public IReadOnlyList<MelodyNote> Generate(
        IReadOnlyList<ProgressionChord> progression,
        Scale scale,
        MelodyOptions? options = null)
    {
        options ??= new MelodyOptions();

        var range = options.Range ?? PitchRange.Instrument.Flute;
        var contour = new Contour(options.Contour);
        var intervalPrefs = options.IntervalPreferences ?? IntervalPreferences.Default;
        var ticksPerBeat = options.TicksPerBeat;

        var notes = new List<MelodyNote>();
        var currentTick = 0;
        var totalTicks = (int)(progression.Sum(c => c.DurationBeats) * ticksPerBeat);

        // Get available scale pitches in range
        var scalePitches = scale.GetPitchesInRange(range).ToList();
        if (scalePitches.Count == 0)
        {
            throw new InvalidOperationException("No scale pitches available in the specified range");
        }

        // Start near the middle of the range
        var currentPitch = scalePitches[scalePitches.Count / 2];

        foreach (var chordEvent in progression)
        {
            var chordStartTick = currentTick;
            var chordEndTick = currentTick + (int)(chordEvent.DurationBeats * ticksPerBeat);
            var chordTones = chordEvent.Chord.PitchClasses;

            // Generate notes for this chord
            while (currentTick < chordEndTick)
            {
                // Determine position in phrase for contour
                var phrasePosition = (double)currentTick / totalTicks;

                // Select note duration
                var durationTicks = SelectDuration(options, ticksPerBeat, chordEndTick - currentTick);

                // Determine target direction from contour
                var targetDirection = contour.GetDirectionAt(phrasePosition);

                // Select next pitch
                currentPitch = SelectNextPitch(
                    currentPitch,
                    chordTones,
                    scale,
                    range,
                    scalePitches,
                    targetDirection,
                    intervalPrefs);

                // Apply syncopation (occasionally start slightly before the beat)
                var noteStart = currentTick;
                if (options.Syncopation > 0 && _random.NextDouble() < options.Syncopation * 0.5)
                {
                    noteStart = Math.Max(0, noteStart - ticksPerBeat / 4);
                }

                // Create the note
                var velocity = 70 + _random.Next(20);

                // Emphasize chord tones
                if (chordTones.Contains(currentPitch.PitchClass))
                {
                    velocity = Math.Min(127, velocity + 10);
                }

                notes.Add(new MelodyNote(currentPitch, noteStart, durationTicks, velocity));
                currentTick += durationTicks;

                // Occasionally add a rest (based on inverse density)
                if (_random.NextDouble() > options.Density)
                {
                    currentTick += durationTicks / 2;
                }
            }

            currentTick = chordEndTick;
        }

        return notes;
    }

    private int SelectDuration(MelodyOptions options, int ticksPerBeat, int maxTicks)
    {
        // Select from common note values
        var candidates = new[]
        {
            (ticksPerBeat * 2, 0.1), // Half note
            (ticksPerBeat, 0.25), // Quarter
            (ticksPerBeat / 2, 0.4), // Eighth
            (ticksPerBeat / 4, 0.2), // Sixteenth
            (ticksPerBeat * 3 / 4, 0.05) // Dotted eighth
        };

        // Weight by density (denser = shorter notes more likely)
        var densityFactor = options.Density;
        var totalWeight = 0.0;
        var roll = _random.NextDouble();
        var cumulative = 0.0;

        foreach (var (duration, weight) in candidates)
        {
            if (duration > maxTicks)
            {
                continue;
            }

            // Adjust weight based on density
            var adjustedWeight = weight;
            if (duration < ticksPerBeat)
            {
                adjustedWeight *= 1 + densityFactor;
            }
            else
            {
                adjustedWeight *= 1 - densityFactor * 0.5;
            }

            totalWeight += adjustedWeight;
        }

        roll *= totalWeight;

        foreach (var (duration, weight) in candidates)
        {
            if (duration > maxTicks)
            {
                continue;
            }

            var adjustedWeight = weight;
            if (duration < ticksPerBeat)
            {
                adjustedWeight *= 1 + densityFactor;
            }
            else
            {
                adjustedWeight *= 1 - densityFactor * 0.5;
            }

            cumulative += adjustedWeight;
            if (cumulative >= roll)
            {
                return Math.Min(duration, maxTicks);
            }
        }

        return ticksPerBeat / 2;
    }

    private Pitch.Pitch SelectNextPitch(
        Pitch.Pitch current,
        IReadOnlyList<PitchClass> chordTones,
        Scale scale,
        PitchRange range,
        IReadOnlyList<Pitch.Pitch> scalePitches,
        int targetDirection,
        IntervalPreferences intervalPrefs)
    {
        // Select interval size
        var intervalSize = intervalPrefs.SelectInterval(_random);

        // Apply direction
        var direction = targetDirection;
        if (direction == 0)
        {
            direction = _random.NextDouble() > 0.5 ? 1 : -1;
        }

        var targetMidi = current.MidiNumber + (intervalSize * direction);

        // Find the nearest scale tone to the target
        var bestPitch = current;
        var bestDistance = int.MaxValue;
        var isChordToneBonus = false;

        foreach (var candidate in scalePitches)
        {
            if (!range.Contains(candidate))
            {
                continue;
            }

            var distance = Math.Abs(candidate.MidiNumber - targetMidi);

            // Prefer chord tones (reduce effective distance)
            var isChordTone = chordTones.Contains(candidate.PitchClass);
            var effectiveDistance = isChordTone ? distance - 2 : distance;

            if (effectiveDistance < bestDistance)
            {
                bestDistance = effectiveDistance;
                bestPitch = candidate;
                isChordToneBonus = isChordTone;
            }
            else if (effectiveDistance == bestDistance && isChordTone && !isChordToneBonus)
            {
                // Prefer chord tone on ties
                bestPitch = candidate;
                isChordToneBonus = true;
            }
        }

        return bestPitch;
    }
}
