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

    /// <inheritdoc />
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
    /// Motif library for thematic development.
    /// </summary>
    public MotifLibrary? MotifLibrary { get; set; }

    /// <summary>
    /// Style ID for motif selection.
    /// </summary>
    public string? StyleId { get; set; }

    /// <summary>
    /// Probability of inserting a motif at phrase boundaries (0-1).
    /// </summary>
    public double MotifProbability { get; set; } = 0.4;

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

        var chordIndex = 0;
        foreach (var chordEvent in progression)
        {
            var chordStartTick = currentTick;
            var chordEndTick = currentTick + (int)(chordEvent.DurationBeats * ticksPerBeat);
            var chordTones = chordEvent.Chord.PitchClasses;
            var isFirstChord = chordIndex == 0;
            var isLastChord = chordIndex == progression.Count - 1;

            // Try to insert a motif at phrase boundaries
            if (options.MotifLibrary != null && _random.NextDouble() < options.MotifProbability)
            {
                var category = DetermineMotifCategory(isFirstChord, isLastChord, (double)currentTick / totalTicks);
                var motif = SelectMotif(options.MotifLibrary, options.StyleId, category);

                if (motif != null)
                {
                    var motifNotes = ApplyMotif(
                        motif.Motif,
                        currentPitch,
                        currentTick,
                        ticksPerBeat,
                        range,
                        scalePitches,
                        chordEndTick - currentTick);

                    if (motifNotes.Count > 0)
                    {
                        notes.AddRange(motifNotes);
                        currentTick = motifNotes[^1].EndTick;
                        currentPitch = motifNotes[^1].Pitch;
                    }
                }
            }

            // Generate remaining notes for this chord (if motif didn't fill it)
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
            chordIndex++;
        }

        return notes;
    }

    /// <summary>
    /// Determines the appropriate motif category based on position.
    /// </summary>
    private MotifCategory DetermineMotifCategory(bool isFirstChord, bool isLastChord, double phrasePosition)
    {
        if (isFirstChord)
        {
            return MotifCategory.Opening;
        }

        if (isLastChord || phrasePosition > 0.85)
        {
            return MotifCategory.Cadential;
        }

        if (phrasePosition > 0.4 && phrasePosition < 0.6)
        {
            return MotifCategory.Development;
        }

        // Random choice between ornament and melodic for middle sections
        return _random.NextDouble() > 0.5 ? MotifCategory.Ornament : MotifCategory.Melodic;
    }

    /// <summary>
    /// Selects a motif from the library for the given category.
    /// </summary>
    private NamedMotif? SelectMotif(MotifLibrary library, string? styleId, MotifCategory category)
    {
        if (!string.IsNullOrEmpty(styleId))
        {
            return library.SelectWeightedByStyleAndCategory(styleId, category);
        }

        return library.SelectWeightedByCategory(category);
    }

    /// <summary>
    /// Applies a motif from a starting pitch, returning the generated notes.
    /// </summary>
    private List<MelodyNote> ApplyMotif(
        Motif motif,
        Pitch.Pitch startPitch,
        int startTick,
        int ticksPerBeat,
        PitchRange range,
        IReadOnlyList<Pitch.Pitch> scalePitches,
        int maxTicks)
    {
        var notes = new List<MelodyNote>();
        var currentTick = startTick;
        var currentMidi = startPitch.MidiNumber;

        // Calculate total motif duration in ticks
        var totalMotifTicks = (int)(motif.TotalDuration * ticksPerBeat);
        if (totalMotifTicks > maxTicks)
        {
            // Motif too long, skip it
            return notes;
        }

        for (var i = 0; i < motif.Intervals.Count; i++)
        {
            var interval = motif.Intervals[i];
            var duration = motif.Durations[i];
            var durationTicks = (int)(duration * ticksPerBeat);

            // Apply interval to get target MIDI number
            var targetMidi = startPitch.MidiNumber + interval;

            // Find nearest scale pitch to target
            var maybePitch = FindNearestScalePitch(targetMidi, scalePitches, range);
            if (maybePitch == null)
            {
                continue; // Skip if no valid pitch found
            }

            var bestPitch = maybePitch.Value;
            var velocity = 75 + _random.Next(15);
            notes.Add(new MelodyNote(bestPitch, currentTick, durationTicks, velocity));
            currentTick += durationTicks;
        }

        return notes;
    }

    /// <summary>
    /// Finds the nearest pitch in scalePitches to the target MIDI number.
    /// </summary>
    private Pitch.Pitch? FindNearestScalePitch(int targetMidi, IReadOnlyList<Pitch.Pitch> scalePitches, PitchRange range)
    {
        Pitch.Pitch? best = null;
        var bestDistance = int.MaxValue;

        foreach (var pitch in scalePitches)
        {
            if (!range.Contains(pitch))
            {
                continue;
            }

            var distance = Math.Abs(pitch.MidiNumber - targetMidi);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = pitch;
            }
        }

        return best;
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
