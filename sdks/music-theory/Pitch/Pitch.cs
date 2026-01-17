namespace BeyondImmersion.Bannou.MusicTheory.Pitch;

/// <summary>
/// Represents a specific pitch with pitch class and octave.
/// Middle C is C4 (MIDI note 60).
/// </summary>
public readonly struct Pitch : IEquatable<Pitch>, IComparable<Pitch>
{
    /// <summary>
    /// The pitch class (note name without octave).
    /// </summary>
    public PitchClass PitchClass { get; }

    /// <summary>
    /// The octave number. Middle C is octave 4.
    /// </summary>
    public int Octave { get; }

    /// <summary>
    /// The MIDI note number (0-127). Middle C = 60.
    /// </summary>
    public int MidiNumber => (Octave + 1) * 12 + (int)PitchClass;

    /// <summary>
    /// Creates a pitch from pitch class and octave.
    /// </summary>
    /// <param name="pitchClass">The pitch class.</param>
    /// <param name="octave">The octave (middle C = 4).</param>
    public Pitch(PitchClass pitchClass, int octave)
    {
        if (octave < -1 || octave > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(octave), "Octave must be between -1 and 9");
        }

        var midiCheck = (octave + 1) * 12 + (int)pitchClass;
        if (midiCheck < 0 || midiCheck > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(octave), "Resulting MIDI number must be 0-127");
        }

        PitchClass = pitchClass;
        Octave = octave;
    }

    /// <summary>
    /// Creates a pitch from a MIDI note number.
    /// </summary>
    /// <param name="midiNumber">MIDI note number (0-127).</param>
    /// <returns>The corresponding pitch.</returns>
    public static Pitch FromMidi(int midiNumber)
    {
        if (midiNumber < 0 || midiNumber > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(midiNumber), "MIDI number must be 0-127");
        }

        var octave = (midiNumber / 12) - 1;
        var pitchClass = (PitchClass)(midiNumber % 12);
        return new Pitch(pitchClass, octave);
    }

    /// <summary>
    /// Transposes the pitch by the given interval.
    /// </summary>
    /// <param name="interval">The interval to transpose by.</param>
    /// <returns>The transposed pitch.</returns>
    public Pitch Transpose(Interval interval)
    {
        var newMidi = MidiNumber + interval.Semitones;
        if (newMidi < 0 || newMidi > 127)
        {
            throw new InvalidOperationException($"Transposition results in out-of-range MIDI number: {newMidi}");
        }

        return FromMidi(newMidi);
    }

    /// <summary>
    /// Transposes the pitch by the given number of semitones.
    /// </summary>
    /// <param name="semitones">Number of semitones (positive = up, negative = down).</param>
    /// <returns>The transposed pitch.</returns>
    public Pitch Transpose(int semitones) => Transpose(new Interval(semitones));

    /// <summary>
    /// Gets the interval from this pitch to another.
    /// </summary>
    /// <param name="other">The target pitch.</param>
    /// <returns>The interval between the pitches.</returns>
    public Interval IntervalTo(Pitch other) => new(other.MidiNumber - MidiNumber);

    /// <summary>
    /// Common pitch constants.
    /// </summary>
    public static class Common
    {
        /// <summary>Middle C (C4, MIDI 60)</summary>
        public static Pitch MiddleC => new(PitchClass.C, 4);

        /// <summary>Concert A (A4, MIDI 69, 440 Hz)</summary>
        public static Pitch ConcertA => new(PitchClass.A, 4);

        /// <summary>Lowest piano note (A0, MIDI 21)</summary>
        public static Pitch PianoLow => new(PitchClass.A, 0);

        /// <summary>Highest piano note (C8, MIDI 108)</summary>
        public static Pitch PianoHigh => new(PitchClass.C, 8);
    }

    /// <summary>
    /// Parses a pitch from standard notation (e.g., "C4", "F#5", "Bb3").
    /// </summary>
    /// <param name="notation">The pitch notation.</param>
    /// <returns>The parsed pitch.</returns>
    public static Pitch Parse(string notation)
    {
        if (string.IsNullOrWhiteSpace(notation))
        {
            throw new ArgumentException("Pitch notation cannot be empty", nameof(notation));
        }

        notation = notation.Trim();

        // Find where the octave number starts
        var octaveIndex = -1;
        for (var i = notation.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(notation[i]) || (notation[i] == '-' && i > 0))
            {
                octaveIndex = i;
            }
            else
            {
                break;
            }
        }

        if (octaveIndex <= 0)
        {
            throw new ArgumentException($"Invalid pitch notation: {notation}", nameof(notation));
        }

        var pitchClassPart = notation[..octaveIndex];
        var octavePart = notation[octaveIndex..];

        var pitchClass = PitchClassExtensions.Parse(pitchClassPart);

        if (!int.TryParse(octavePart, out var octave))
        {
            throw new ArgumentException($"Invalid octave in pitch notation: {notation}", nameof(notation));
        }

        return new Pitch(pitchClass, octave);
    }

    /// <summary>
    /// Attempts to parse a pitch from notation.
    /// </summary>
    public static bool TryParse(string notation, out Pitch result)
    {
        try
        {
            result = Parse(notation);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    public static Pitch operator +(Pitch pitch, Interval interval) => pitch.Transpose(interval);
    public static Pitch operator -(Pitch pitch, Interval interval) => pitch.Transpose(-interval);
    public static Interval operator -(Pitch a, Pitch b) => b.IntervalTo(a);
    public static bool operator ==(Pitch a, Pitch b) => a.MidiNumber == b.MidiNumber;
    public static bool operator !=(Pitch a, Pitch b) => a.MidiNumber != b.MidiNumber;
    public static bool operator <(Pitch a, Pitch b) => a.MidiNumber < b.MidiNumber;
    public static bool operator >(Pitch a, Pitch b) => a.MidiNumber > b.MidiNumber;
    public static bool operator <=(Pitch a, Pitch b) => a.MidiNumber <= b.MidiNumber;
    public static bool operator >=(Pitch a, Pitch b) => a.MidiNumber >= b.MidiNumber;

    public bool Equals(Pitch other) => MidiNumber == other.MidiNumber;
    public override bool Equals(object? obj) => obj is Pitch other && Equals(other);
    public override int GetHashCode() => MidiNumber;
    public int CompareTo(Pitch other) => MidiNumber.CompareTo(other.MidiNumber);

    public override string ToString() => $"{PitchClass.ToSharpName()}{Octave}";

    /// <summary>
    /// Returns the pitch name using flats.
    /// </summary>
    public string ToFlatString() => $"{PitchClass.ToFlatName()}{Octave}";
}

/// <summary>
/// Represents a range of pitches from low to high.
/// </summary>
public readonly struct PitchRange
{
    /// <summary>
    /// The lowest pitch in the range (inclusive).
    /// </summary>
    public Pitch Low { get; }

    /// <summary>
    /// The highest pitch in the range (inclusive).
    /// </summary>
    public Pitch High { get; }

    /// <summary>
    /// The span of the range in semitones.
    /// </summary>
    public int Span => High.MidiNumber - Low.MidiNumber;

    /// <summary>
    /// Creates a pitch range.
    /// </summary>
    /// <param name="low">Lowest pitch (inclusive).</param>
    /// <param name="high">Highest pitch (inclusive).</param>
    public PitchRange(Pitch low, Pitch high)
    {
        if (high < low)
        {
            throw new ArgumentException("High pitch must be >= low pitch");
        }

        Low = low;
        High = high;
    }

    /// <summary>
    /// Creates a pitch range from MIDI numbers.
    /// </summary>
    public static PitchRange FromMidi(int low, int high)
    {
        return new PitchRange(Pitch.FromMidi(low), Pitch.FromMidi(high));
    }

    /// <summary>
    /// Checks if a pitch is within this range.
    /// </summary>
    public bool Contains(Pitch pitch)
    {
        return pitch >= Low && pitch <= High;
    }

    /// <summary>
    /// Standard vocal ranges.
    /// </summary>
    public static class Vocal
    {
        /// <summary>Bass range (E2-E4)</summary>
        public static PitchRange Bass => new(new Pitch(PitchClass.E, 2), new Pitch(PitchClass.E, 4));

        /// <summary>Baritone range (A2-A4)</summary>
        public static PitchRange Baritone => new(new Pitch(PitchClass.A, 2), new Pitch(PitchClass.A, 4));

        /// <summary>Tenor range (C3-C5)</summary>
        public static PitchRange Tenor => new(new Pitch(PitchClass.C, 3), new Pitch(PitchClass.C, 5));

        /// <summary>Alto range (F3-F5)</summary>
        public static PitchRange Alto => new(new Pitch(PitchClass.F, 3), new Pitch(PitchClass.F, 5));

        /// <summary>Soprano range (C4-C6)</summary>
        public static PitchRange Soprano => new(new Pitch(PitchClass.C, 4), new Pitch(PitchClass.C, 6));
    }

    /// <summary>
    /// Standard instrument ranges.
    /// </summary>
    public static class Instrument
    {
        /// <summary>Violin range (G3-A7)</summary>
        public static PitchRange Violin => new(new Pitch(PitchClass.G, 3), new Pitch(PitchClass.A, 7));

        /// <summary>Viola range (C3-E6)</summary>
        public static PitchRange Viola => new(new Pitch(PitchClass.C, 3), new Pitch(PitchClass.E, 6));

        /// <summary>Cello range (C2-C6)</summary>
        public static PitchRange Cello => new(new Pitch(PitchClass.C, 2), new Pitch(PitchClass.C, 6));

        /// <summary>Double bass range (E1-C5)</summary>
        public static PitchRange DoubleBass => new(new Pitch(PitchClass.E, 1), new Pitch(PitchClass.C, 5));

        /// <summary>Piano range (A0-C8)</summary>
        public static PitchRange Piano => new(new Pitch(PitchClass.A, 0), new Pitch(PitchClass.C, 8));

        /// <summary>Guitar range (E2-E6)</summary>
        public static PitchRange Guitar => new(new Pitch(PitchClass.E, 2), new Pitch(PitchClass.E, 6));

        /// <summary>Flute range (C4-C7)</summary>
        public static PitchRange Flute => new(new Pitch(PitchClass.C, 4), new Pitch(PitchClass.C, 7));
    }

    public override string ToString() => $"{Low} - {High}";
}
