using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicTheory.Harmony;

/// <summary>
/// Harmonic function categories in tonal harmony.
/// </summary>
public enum HarmonicFunctionType
{
    /// <summary>Tonic function - stable, home</summary>
    Tonic,

    /// <summary>Subdominant function - pre-dominant, moves away from tonic</summary>
    Subdominant,

    /// <summary>Dominant function - tension, resolves to tonic</summary>
    Dominant
}

/// <summary>
/// Represents a harmonic function with its typical chords and tendencies.
/// </summary>
public sealed class HarmonicFunction
{
    /// <summary>
    /// The function type.
    /// </summary>
    public HarmonicFunctionType Type { get; }

    /// <summary>
    /// Roman numeral representation.
    /// </summary>
    public string RomanNumeral { get; }

    /// <summary>
    /// Scale degree (1-7).
    /// </summary>
    public int Degree { get; }

    /// <summary>
    /// Whether this is a primary (I, IV, V) or secondary (ii, iii, vi, vii) function.
    /// </summary>
    public bool IsPrimary { get; }

    /// <summary>
    /// Typical resolution targets (degrees that this chord tends to move to).
    /// </summary>
    public IReadOnlyList<int> TypicalResolutions { get; }

    private HarmonicFunction(HarmonicFunctionType type, string romanNumeral, int degree,
        bool isPrimary, IReadOnlyList<int> resolutions)
    {
        Type = type;
        RomanNumeral = romanNumeral;
        Degree = degree;
        IsPrimary = isPrimary;
        TypicalResolutions = resolutions;
    }

    /// <summary>
    /// Scale degree functions in major keys.
    /// </summary>
    public static class Major
    {
        /// <summary>I - Tonic</summary>
        public static HarmonicFunction I => new(HarmonicFunctionType.Tonic, "I", 1, true, [4, 5, 6]);

        /// <summary>ii - Supertonic (subdominant function)</summary>
        public static HarmonicFunction ii => new(HarmonicFunctionType.Subdominant, "ii", 2, false, [5, 7]);

        /// <summary>iii - Mediant (tonic function)</summary>
        public static HarmonicFunction iii => new(HarmonicFunctionType.Tonic, "iii", 3, false, [4, 6]);

        /// <summary>IV - Subdominant</summary>
        public static HarmonicFunction IV => new(HarmonicFunctionType.Subdominant, "IV", 4, true, [1, 2, 5]);

        /// <summary>V - Dominant</summary>
        public static HarmonicFunction V => new(HarmonicFunctionType.Dominant, "V", 5, true, [1, 6]);

        /// <summary>vi - Submediant (tonic function)</summary>
        public static HarmonicFunction vi => new(HarmonicFunctionType.Tonic, "vi", 6, false, [2, 4]);

        /// <summary>vii° - Leading tone (dominant function)</summary>
        public static HarmonicFunction viiDim => new(HarmonicFunctionType.Dominant, "vii°", 7, false, [1, 3]);

        /// <summary>Gets the function for a given scale degree.</summary>
        public static HarmonicFunction ForDegree(int degree)
        {
            return degree switch
            {
                1 => I,
                2 => ii,
                3 => iii,
                4 => IV,
                5 => V,
                6 => vi,
                7 => viiDim,
                _ => throw new ArgumentOutOfRangeException(nameof(degree), "Degree must be 1-7")
            };
        }
    }

    /// <summary>
    /// Scale degree functions in minor keys.
    /// </summary>
    public static class Minor
    {
        /// <summary>i - Tonic</summary>
        public static HarmonicFunction i => new(HarmonicFunctionType.Tonic, "i", 1, true, [4, 5, 6]);

        /// <summary>ii° - Supertonic diminished</summary>
        public static HarmonicFunction iiDim => new(HarmonicFunctionType.Subdominant, "ii°", 2, false, [5, 7]);

        /// <summary>III - Mediant major</summary>
        public static HarmonicFunction III => new(HarmonicFunctionType.Tonic, "III", 3, false, [4, 6]);

        /// <summary>iv - Subdominant</summary>
        public static HarmonicFunction iv => new(HarmonicFunctionType.Subdominant, "iv", 4, true, [1, 2, 5]);

        /// <summary>V - Dominant (harmonic minor)</summary>
        public static HarmonicFunction V => new(HarmonicFunctionType.Dominant, "V", 5, true, [1, 6]);

        /// <summary>v - Dominant minor (natural minor)</summary>
        public static HarmonicFunction v => new(HarmonicFunctionType.Dominant, "v", 5, true, [1, 6]);

        /// <summary>VI - Submediant</summary>
        public static HarmonicFunction VI => new(HarmonicFunctionType.Tonic, "VI", 6, false, [2, 4]);

        /// <summary>VII - Subtonic (natural minor)</summary>
        public static HarmonicFunction VII => new(HarmonicFunctionType.Dominant, "VII", 7, false, [3, 1]);

        /// <summary>vii° - Leading tone (harmonic minor)</summary>
        public static HarmonicFunction viiDim => new(HarmonicFunctionType.Dominant, "vii°", 7, false, [1]);

        /// <summary>Gets the function for a given scale degree.</summary>
        public static HarmonicFunction ForDegree(int degree)
        {
            return degree switch
            {
                1 => i,
                2 => iiDim,
                3 => III,
                4 => iv,
                5 => V,
                6 => VI,
                7 => VII,
                _ => throw new ArgumentOutOfRangeException(nameof(degree), "Degree must be 1-7")
            };
        }
    }

    /// <summary>
    /// Gets the diatonic chord for this function in a given scale.
    /// </summary>
    /// <param name="scale">The scale.</param>
    /// <param name="seventh">Whether to include the seventh.</param>
    /// <returns>The diatonic chord.</returns>
    public Chord GetChord(Scale scale, bool seventh = false)
    {
        return Chord.FromScaleDegree(scale, Degree, seventh);
    }

    public override string ToString() => RomanNumeral;
}

/// <summary>
/// Types of cadences.
/// </summary>
public enum CadenceType
{
    /// <summary>V - I (strongest resolution)</summary>
    AuthenticPerfect,

    /// <summary>V - I with non-tonic soprano</summary>
    AuthenticImperfect,

    /// <summary>Any - V (ends on dominant)</summary>
    Half,

    /// <summary>IV - I (amen cadence)</summary>
    Plagal,

    /// <summary>V - vi (unexpected resolution)</summary>
    Deceptive,

    /// <summary>vii° - I (leading tone resolution)</summary>
    LeadingTone
}

/// <summary>
/// Represents a cadential pattern.
/// </summary>
public sealed class Cadence
{
    /// <summary>
    /// The cadence type.
    /// </summary>
    public CadenceType Type { get; }

    /// <summary>
    /// Chords in the cadence (typically 2-3).
    /// </summary>
    public IReadOnlyList<int> Degrees { get; }

    /// <summary>
    /// Relative strength (0-1).
    /// </summary>
    public double Strength { get; }

    private Cadence(CadenceType type, IReadOnlyList<int> degrees, double strength)
    {
        Type = type;
        Degrees = degrees;
        Strength = strength;
    }

    /// <summary>
    /// Gets common cadence patterns.
    /// </summary>
    public static class Patterns
    {
        /// <summary>Perfect authentic cadence: V - I</summary>
        public static Cadence PAC => new(CadenceType.AuthenticPerfect, [5, 1], 1.0);

        /// <summary>Imperfect authentic cadence: V - I (weaker)</summary>
        public static Cadence IAC => new(CadenceType.AuthenticImperfect, [5, 1], 0.8);

        /// <summary>Half cadence: any - V</summary>
        public static Cadence HC => new(CadenceType.Half, [5], 0.5);

        /// <summary>Plagal cadence: IV - I</summary>
        public static Cadence PC => new(CadenceType.Plagal, [4, 1], 0.7);

        /// <summary>Deceptive cadence: V - vi</summary>
        public static Cadence DC => new(CadenceType.Deceptive, [5, 6], 0.6);

        /// <summary>ii - V - I progression</summary>
        public static Cadence TwoFiveOne => new(CadenceType.AuthenticPerfect, [2, 5, 1], 1.0);

        /// <summary>IV - V - I progression</summary>
        public static Cadence FourFiveOne => new(CadenceType.AuthenticPerfect, [4, 5, 1], 0.9);
    }

    /// <summary>
    /// Gets the chords for this cadence in a given scale.
    /// </summary>
    public IEnumerable<Chord> GetChords(Scale scale, bool sevenths = false)
    {
        return Degrees.Select(d => Chord.FromScaleDegree(scale, d, sevenths));
    }

    public override string ToString() => $"{Type}: {string.Join("-", Degrees)}";
}
