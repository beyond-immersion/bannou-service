using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicTheory.Collections;

/// <summary>
/// Represents a musical scale - a set of pitches within an octave starting from a root.
/// </summary>
public sealed class Scale
{
    /// <summary>
    /// The root pitch class of the scale.
    /// </summary>
    public PitchClass Root { get; }

    /// <summary>
    /// The mode (interval pattern) of the scale.
    /// </summary>
    public ModeType Mode { get; }

    /// <summary>
    /// The pitch classes in this scale.
    /// </summary>
    public IReadOnlyList<PitchClass> PitchClasses { get; }

    /// <summary>
    /// Creates a scale with the given root and mode.
    /// </summary>
    /// <param name="root">The root pitch class.</param>
    /// <param name="mode">The mode type.</param>
    public Scale(PitchClass root, ModeType mode)
    {
        Root = root;
        Mode = mode;

        var pattern = ModePatterns.GetPattern(mode);
        var pitchClasses = new PitchClass[pattern.Count];

        for (var i = 0; i < pattern.Count; i++)
        {
            pitchClasses[i] = root.Transpose(pattern[i]);
        }

        PitchClasses = pitchClasses;
    }

    /// <summary>
    /// Gets the scale degree (1-based) for a pitch class, or null if not in scale.
    /// </summary>
    /// <param name="pitchClass">The pitch class to find.</param>
    /// <returns>Scale degree (1-7+) or null if not in scale.</returns>
    public int? GetDegree(PitchClass pitchClass)
    {
        for (var i = 0; i < PitchClasses.Count; i++)
        {
            if (PitchClasses[i] == pitchClass)
            {
                return i + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the pitch class at a given scale degree (1-based).
    /// </summary>
    /// <param name="degree">Scale degree (1 = root).</param>
    /// <returns>The pitch class at that degree.</returns>
    public PitchClass GetPitchClassAtDegree(int degree)
    {
        if (degree < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(degree), "Degree must be >= 1");
        }

        // Handle degrees beyond the octave
        var index = (degree - 1) % PitchClasses.Count;
        return PitchClasses[index];
    }

    /// <summary>
    /// Checks if a pitch class belongs to this scale.
    /// </summary>
    public bool Contains(PitchClass pitchClass)
    {
        return PitchClasses.Contains(pitchClass);
    }

    /// <summary>
    /// Gets all pitches in this scale within a given range.
    /// </summary>
    /// <param name="range">The pitch range to fill.</param>
    /// <returns>All scale pitches within the range.</returns>
    public IEnumerable<Pitch.Pitch> GetPitchesInRange(PitchRange range)
    {
        for (var midi = range.Low.MidiNumber; midi <= range.High.MidiNumber; midi++)
        {
            var pitch = Pitch.Pitch.FromMidi(midi);
            if (Contains(pitch.PitchClass))
            {
                yield return pitch;
            }
        }
    }

    /// <summary>
    /// Gets the relative major/minor scale.
    /// </summary>
    public Scale GetRelative()
    {
        if (Mode == ModeType.Major)
        {
            // Relative minor is 3 semitones down
            return new Scale(Root.Transpose(-3), ModeType.Minor);
        }
        else if (Mode == ModeType.Minor || Mode == ModeType.Aeolian)
        {
            // Relative major is 3 semitones up
            return new Scale(Root.Transpose(3), ModeType.Major);
        }

        // For other modes, return the scale built on the first degree of the parent major
        var parentMajorRoot = GetParentMajorRoot();
        return new Scale(parentMajorRoot, ModeType.Major);
    }

    /// <summary>
    /// Gets the parallel major/minor scale (same root, different mode).
    /// </summary>
    public Scale GetParallel()
    {
        if (ModePatterns.IsMajor(Mode))
        {
            return new Scale(Root, ModeType.Minor);
        }

        return new Scale(Root, ModeType.Major);
    }

    private PitchClass GetParentMajorRoot()
    {
        // Calculate how many semitones the mode's root is from its parent major
        var offset = Mode switch
        {
            ModeType.Major => 0,
            ModeType.Dorian => -2,
            ModeType.Phrygian => -4,
            ModeType.Lydian => -5,
            ModeType.Mixolydian => -7,
            ModeType.Minor or ModeType.Aeolian => -9,
            ModeType.Locrian => -11,
            _ => 0
        };

        return Root.Transpose(offset);
    }

    /// <summary>
    /// Common scale presets.
    /// </summary>
    public static class Common
    {
        /// <summary>C Major scale</summary>
        public static Scale CMajor => new(PitchClass.C, ModeType.Major);

        /// <summary>A minor scale</summary>
        public static Scale AMinor => new(PitchClass.A, ModeType.Minor);

        /// <summary>G Major scale</summary>
        public static Scale GMajor => new(PitchClass.G, ModeType.Major);

        /// <summary>D Major scale</summary>
        public static Scale DMajor => new(PitchClass.D, ModeType.Major);

        /// <summary>D Dorian scale</summary>
        public static Scale DDorian => new(PitchClass.D, ModeType.Dorian);

        /// <summary>A Mixolydian scale</summary>
        public static Scale AMixolydian => new(PitchClass.A, ModeType.Mixolydian);
    }

    public override string ToString() => $"{Root.ToSharpName()} {Mode}";
}
