using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicTheory.Collections;

/// <summary>
/// Represents a specific voicing of a chord - actual pitches with octaves.
/// </summary>
public sealed class Voicing
{
    /// <summary>
    /// The chord being voiced.
    /// </summary>
    public Chord Chord { get; }

    /// <summary>
    /// The actual pitches in the voicing, from lowest to highest.
    /// </summary>
    public IReadOnlyList<Pitch.Pitch> Pitches { get; }

    /// <summary>
    /// Gets the bass pitch (lowest note).
    /// </summary>
    public Pitch.Pitch Bass => Pitches[0];

    /// <summary>
    /// Gets the soprano pitch (highest note).
    /// </summary>
    public Pitch.Pitch Soprano => Pitches[^1];

    /// <summary>
    /// Gets the number of voices in this voicing.
    /// </summary>
    public int VoiceCount => Pitches.Count;

    /// <summary>
    /// Creates a voicing from a chord and specific pitches.
    /// </summary>
    /// <param name="chord">The chord being voiced.</param>
    /// <param name="pitches">The specific pitches.</param>
    public Voicing(Chord chord, IReadOnlyList<Pitch.Pitch> pitches)
    {
        if (pitches.Count == 0)
        {
            throw new ArgumentException("Voicing must have at least one pitch", nameof(pitches));
        }

        Chord = chord;

        // Sort pitches from low to high
        var sorted = pitches.OrderBy(p => p.MidiNumber).ToList();
        Pitches = sorted;
    }

    /// <summary>
    /// Creates a close-position voicing of a chord.
    /// </summary>
    /// <param name="chord">The chord to voice.</param>
    /// <param name="bassOctave">Octave for the bass note.</param>
    /// <returns>Close-position voicing.</returns>
    public static Voicing ClosePosition(Chord chord, int bassOctave = 3)
    {
        var pitches = new List<Pitch.Pitch>();

        // Add bass note (potentially different from root for inversions)
        var bassClass = chord.Bass ?? chord.Root;
        pitches.Add(new Pitch.Pitch(bassClass, bassOctave));

        // Add remaining chord tones in ascending order
        var currentOctave = bassOctave;
        var lastMidi = pitches[0].MidiNumber;

        foreach (var pc in chord.PitchClasses.Where(p => p != bassClass))
        {
            var pitch = new Pitch.Pitch(pc, currentOctave);

            // If this pitch would be below the last one, move up an octave
            while (pitch.MidiNumber <= lastMidi)
            {
                currentOctave++;
                pitch = new Pitch.Pitch(pc, currentOctave);
            }

            pitches.Add(pitch);
            lastMidi = pitch.MidiNumber;
        }

        return new Voicing(chord, pitches);
    }

    /// <summary>
    /// Creates an open-position voicing (spread across octaves).
    /// </summary>
    /// <param name="chord">The chord to voice.</param>
    /// <param name="bassOctave">Octave for the bass note.</param>
    /// <param name="spread">Number of octaves to spread across.</param>
    /// <returns>Open-position voicing.</returns>
    public static Voicing OpenPosition(Chord chord, int bassOctave = 2, int spread = 2)
    {
        var pitches = new List<Pitch.Pitch>();
        var bassClass = chord.Bass ?? chord.Root;

        // Add bass
        pitches.Add(new Pitch.Pitch(bassClass, bassOctave));

        // Distribute other notes across octaves
        var otherNotes = chord.PitchClasses.Where(p => p != bassClass).ToList();
        var notesPerOctave = Math.Max(1, otherNotes.Count / spread);
        var currentOctave = bassOctave + 1;
        var noteIndex = 0;

        foreach (var pc in otherNotes)
        {
            pitches.Add(new Pitch.Pitch(pc, currentOctave));
            noteIndex++;

            if (noteIndex >= notesPerOctave)
            {
                currentOctave++;
                noteIndex = 0;
            }
        }

        return new Voicing(chord, pitches);
    }

    /// <summary>
    /// Creates a drop-2 voicing (second voice from top dropped an octave).
    /// Common in jazz guitar and piano.
    /// </summary>
    public static Voicing Drop2(Chord chord, int bassOctave = 3)
    {
        // Start with close position
        var close = ClosePosition(chord, bassOctave);

        if (close.Pitches.Count < 4)
        {
            return close; // Need at least 4 notes for drop-2
        }

        var pitches = close.Pitches.ToList();

        // Drop the second-from-top note down an octave
        var dropIndex = pitches.Count - 2;
        var droppedPitch = pitches[dropIndex];
        pitches[dropIndex] = droppedPitch.Transpose(-12);

        // Re-sort
        pitches = pitches.OrderBy(p => p.MidiNumber).ToList();

        return new Voicing(chord, pitches);
    }

    /// <summary>
    /// Gets the interval between two adjacent voices.
    /// </summary>
    /// <param name="lowerVoice">Index of lower voice (0-based).</param>
    /// <returns>Interval between the voices.</returns>
    public Interval GetIntervalBetween(int lowerVoice)
    {
        if (lowerVoice < 0 || lowerVoice >= Pitches.Count - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lowerVoice));
        }

        return new Interval(Pitches[lowerVoice + 1].MidiNumber - Pitches[lowerVoice].MidiNumber);
    }

    /// <summary>
    /// Checks if this voicing has any spacing larger than an octave between adjacent voices
    /// (except possibly between bass and tenor, which is allowed).
    /// </summary>
    public bool HasLargeSpacing
    {
        get
        {
            // Check upper voices (skip bass-tenor spacing)
            for (var i = 1; i < Pitches.Count - 1; i++)
            {
                if (GetIntervalBetween(i).Semitones > 12)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Transposes all pitches in the voicing.
    /// </summary>
    /// <param name="semitones">Number of semitones to transpose.</param>
    /// <returns>Transposed voicing.</returns>
    public Voicing Transpose(int semitones)
    {
        var newPitches = Pitches.Select(p => p.Transpose(semitones)).ToList();
        var newChord = Chord.Transpose(semitones);
        return new Voicing(newChord, newPitches);
    }

    /// <summary>
    /// Gets MIDI note numbers for all pitches.
    /// </summary>
    public IEnumerable<int> ToMidi() => Pitches.Select(p => p.MidiNumber);

    /// <inheritdoc />
    public override string ToString()
    {
        var notes = string.Join("-", Pitches.Select(p => p.ToString()));
        return $"{Chord.Symbol}: [{notes}]";
    }
}

/// <summary>
/// Factory for creating common voicing patterns.
/// </summary>
public static class VoicingFactory
{
    /// <summary>
    /// Creates a voicing within a specified range with a target number of voices.
    /// </summary>
    /// <param name="chord">The chord to voice.</param>
    /// <param name="range">Pitch range constraint.</param>
    /// <param name="voiceCount">Target number of voices.</param>
    /// <returns>Voicing within the range.</returns>
    public static Voicing CreateInRange(Chord chord, PitchRange range, int voiceCount = 4)
    {
        var pitches = new List<Pitch.Pitch>();
        var chordTones = chord.PitchClasses;

        // Start from the bass of the range
        var currentMidi = range.Low.MidiNumber;

        // Find a starting chord tone near the bass
        var bassClass = chord.Bass ?? chord.Root;
        while (currentMidi <= range.High.MidiNumber && pitches.Count < voiceCount)
        {
            var pitch = Pitch.Pitch.FromMidi(currentMidi);

            // Check if this is a chord tone
            if (chordTones.Contains(pitch.PitchClass))
            {
                // Prefer bass note first if we haven't added any notes
                if (pitches.Count == 0 && pitch.PitchClass != bassClass)
                {
                    currentMidi++;
                    continue;
                }

                pitches.Add(pitch);
            }

            currentMidi++;
        }

        // If we didn't get enough notes, double some
        while (pitches.Count < voiceCount && pitches.Count > 0)
        {
            // Find the highest note and try to add another an octave up
            var highest = pitches[^1];
            var doubled = highest.Transpose(12);

            if (range.Contains(doubled))
            {
                pitches.Add(doubled);
            }
            else
            {
                break;
            }
        }

        return new Voicing(chord, pitches);
    }
}
