using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Melody;
using BeyondImmersion.Bannou.MusicTheory.Time;

namespace BeyondImmersion.Bannou.MusicTheory.Structure;

/// <summary>
/// Phrase structure types.
/// </summary>
public enum PhraseType
{
    /// <summary>Opening phrase (antecedent)</summary>
    Antecedent,

    /// <summary>Closing phrase (consequent)</summary>
    Consequent,

    /// <summary>Continuation/extension phrase</summary>
    Continuation,

    /// <summary>Cadential phrase (closing gesture)</summary>
    Cadential,

    /// <summary>Transitional phrase</summary>
    Transition
}

/// <summary>
/// Represents a musical phrase - a complete musical thought.
/// </summary>
public sealed class Phrase
{
    /// <summary>
    /// The phrase type.
    /// </summary>
    public PhraseType Type { get; }

    /// <summary>
    /// Start position in ticks.
    /// </summary>
    public int StartTick { get; }

    /// <summary>
    /// Duration in ticks.
    /// </summary>
    public int DurationTicks { get; }

    /// <summary>
    /// Melody notes in this phrase.
    /// </summary>
    public IReadOnlyList<MelodyNote> Notes { get; }

    /// <summary>
    /// Chord progression for this phrase.
    /// </summary>
    public IReadOnlyList<ProgressionChord>? Harmony { get; }

    /// <summary>
    /// Cadence at the end of this phrase.
    /// </summary>
    public CadenceType? EndingCadence { get; }

    /// <summary>
    /// Creates a phrase.
    /// </summary>
    public Phrase(
        PhraseType type,
        int startTick,
        int durationTicks,
        IReadOnlyList<MelodyNote> notes,
        IReadOnlyList<ProgressionChord>? harmony = null,
        CadenceType? endingCadence = null)
    {
        Type = type;
        StartTick = startTick;
        DurationTicks = durationTicks;
        Notes = notes;
        Harmony = harmony;
        EndingCadence = endingCadence;
    }

    /// <summary>
    /// End tick (exclusive).
    /// </summary>
    public int EndTick => StartTick + DurationTicks;

    public override string ToString() => $"{Type} [{StartTick}-{EndTick}] ({Notes.Count} notes)";
}

/// <summary>
/// Represents a period (pair of related phrases).
/// </summary>
public sealed class Period
{
    /// <summary>
    /// The antecedent (opening) phrase.
    /// </summary>
    public Phrase Antecedent { get; }

    /// <summary>
    /// The consequent (closing) phrase.
    /// </summary>
    public Phrase Consequent { get; }

    /// <summary>
    /// Whether this is a parallel period (similar melodic material).
    /// </summary>
    public bool IsParallel { get; }

    /// <summary>
    /// Creates a period.
    /// </summary>
    public Period(Phrase antecedent, Phrase consequent, bool isParallel = true)
    {
        Antecedent = antecedent;
        Consequent = consequent;
        IsParallel = isParallel;
    }

    /// <summary>
    /// Total duration in ticks.
    /// </summary>
    public int TotalDurationTicks => Antecedent.DurationTicks + Consequent.DurationTicks;
}

/// <summary>
/// Options for phrase generation.
/// </summary>
public sealed class PhraseOptions
{
    /// <summary>
    /// Length in beats.
    /// </summary>
    public double LengthBeats { get; set; } = 4.0;

    /// <summary>
    /// Ticks per beat.
    /// </summary>
    public int TicksPerBeat { get; set; } = 480;

    /// <summary>
    /// Whether to end with a cadence.
    /// </summary>
    public bool EndWithCadence { get; set; } = true;

    /// <summary>
    /// Preferred cadence type.
    /// </summary>
    public CadenceType CadenceType { get; set; } = CadenceType.Half;

    /// <summary>
    /// Meter for the phrase.
    /// </summary>
    public Meter Meter { get; set; } = Meter.Common.CommonTime;
}

/// <summary>
/// Builder for constructing musical phrases.
/// </summary>
public sealed class PhraseBuilder
{
    private readonly List<MelodyNote> _notes = [];
    private readonly List<ProgressionChord> _harmony = [];
    private PhraseType _type = PhraseType.Antecedent;
    private int _startTick;
    private int _currentTick;
    private CadenceType? _cadence;

    /// <summary>
    /// Creates a phrase builder starting at the given tick.
    /// </summary>
    public PhraseBuilder(int startTick = 0)
    {
        _startTick = startTick;
        _currentTick = startTick;
    }

    /// <summary>
    /// Sets the phrase type.
    /// </summary>
    public PhraseBuilder WithType(PhraseType type)
    {
        _type = type;
        return this;
    }

    /// <summary>
    /// Adds a note to the phrase.
    /// </summary>
    public PhraseBuilder AddNote(Pitch.Pitch pitch, int durationTicks, int velocity = 80)
    {
        _notes.Add(new MelodyNote(pitch, _currentTick, durationTicks, velocity));
        _currentTick += durationTicks;
        return this;
    }

    /// <summary>
    /// Adds a rest (advances time without a note).
    /// </summary>
    public PhraseBuilder AddRest(int durationTicks)
    {
        _currentTick += durationTicks;
        return this;
    }

    /// <summary>
    /// Adds harmony to the phrase.
    /// </summary>
    public PhraseBuilder WithHarmony(IEnumerable<ProgressionChord> chords)
    {
        _harmony.AddRange(chords);
        return this;
    }

    /// <summary>
    /// Sets the ending cadence.
    /// </summary>
    public PhraseBuilder WithCadence(CadenceType cadence)
    {
        _cadence = cadence;
        return this;
    }

    /// <summary>
    /// Builds the phrase.
    /// </summary>
    public Phrase Build()
    {
        var durationTicks = _currentTick - _startTick;
        return new Phrase(
            _type,
            _startTick,
            durationTicks,
            _notes.ToList(),
            _harmony.Count > 0 ? _harmony.ToList() : null,
            _cadence);
    }
}

/// <summary>
/// Standard phrase length patterns.
/// </summary>
public static class PhraseLengths
{
    /// <summary>
    /// Gets phrase length in bars for common forms.
    /// </summary>
    public static int GetBarsForForm(string formName)
    {
        return formName.ToUpperInvariant() switch
        {
            "AABB" => 4, // Celtic standard
            "AABA" => 8, // Jazz standard
            "AAB" => 4, // Blues
            "ABAB" => 4, // Verse-chorus
            _ => 4
        };
    }

    /// <summary>
    /// Common phrase length (4 bars).
    /// </summary>
    public static int Standard => 4;

    /// <summary>
    /// Extended phrase length (8 bars).
    /// </summary>
    public static int Extended => 8;

    /// <summary>
    /// Short phrase length (2 bars).
    /// </summary>
    public static int Short => 2;
}
