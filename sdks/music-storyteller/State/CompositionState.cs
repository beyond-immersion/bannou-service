using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Pitch;

namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Represents a musical event that updates the composition state.
/// </summary>
public sealed class MusicalEvent
{
    /// <summary>
    /// The type of event.
    /// </summary>
    public MusicalEventType Type { get; init; }

    /// <summary>
    /// The chord associated with this event (if applicable).
    /// </summary>
    public Chord? Chord { get; init; }

    /// <summary>
    /// The pitch associated with this event (if applicable).
    /// </summary>
    public Pitch? Pitch { get; init; }

    /// <summary>
    /// Information content in bits.
    /// </summary>
    public double InformationContent { get; init; }

    /// <summary>
    /// Whether this event is a resolution.
    /// </summary>
    public bool IsResolution { get; init; }

    /// <summary>
    /// Duration in beats.
    /// </summary>
    public double DurationBeats { get; init; } = 1.0;

    /// <summary>
    /// Loudness change (for mechanism tracking).
    /// </summary>
    public double LoudnessChange { get; init; }

    /// <summary>
    /// Rhythm strength (for mechanism tracking).
    /// </summary>
    public double RhythmStrength { get; init; } = 0.5;

    /// <summary>
    /// Expressiveness level (for mechanism tracking).
    /// </summary>
    public double Expressiveness { get; init; } = 0.5;
}

/// <summary>
/// Types of musical events.
/// </summary>
public enum MusicalEventType
{
    /// <summary>A chord change.</summary>
    ChordChange,

    /// <summary>A melodic note.</summary>
    MelodicNote,

    /// <summary>A motif statement.</summary>
    MotifStatement,

    /// <summary>A cadence.</summary>
    Cadence,

    /// <summary>A modulation.</summary>
    Modulation,

    /// <summary>A dynamic change.</summary>
    DynamicChange,

    /// <summary>A texture change.</summary>
    TextureChange
}

/// <summary>
/// Master state container for the entire composition.
/// Aggregates all sub-states and provides unified state management.
/// </summary>
public sealed class CompositionState
{
    /// <summary>
    /// Position within the composition (bar, beat, phase).
    /// </summary>
    public PositionState Position { get; private set; }

    /// <summary>
    /// Emotional state (6-dimensional).
    /// </summary>
    public EmotionalState Emotional { get; private set; }

    /// <summary>
    /// Harmonic context (key, chord, function).
    /// </summary>
    public HarmonicState Harmonic { get; private set; }

    /// <summary>
    /// Thematic/motivic state (motif memory and usage).
    /// </summary>
    public ThematicState Thematic { get; private set; }

    /// <summary>
    /// Listener model (expectations, attention, engagement).
    /// </summary>
    public ListenerModel Listener { get; private set; }

    /// <summary>
    /// Information-theoretic state (IC, entropy).
    /// </summary>
    public InformationState Information { get; private set; }

    /// <summary>
    /// Pitch space state (Lerdahl TPS position).
    /// </summary>
    public PitchSpaceState PitchSpace { get; private set; }

    /// <summary>
    /// BRECVEMA mechanism activations.
    /// </summary>
    public MechanismState Mechanisms { get; private set; }

    /// <summary>
    /// Total number of events processed.
    /// </summary>
    public int EventCount { get; private set; }

    /// <summary>
    /// Creates a new composition state with default values.
    /// </summary>
    public CompositionState()
    {
        Position = new PositionState();
        Emotional = new EmotionalState();
        Harmonic = new HarmonicState();
        Thematic = new ThematicState();
        Listener = new ListenerModel();
        Information = new InformationState();
        PitchSpace = new PitchSpaceState();
        Mechanisms = new MechanismState();
    }

    /// <summary>
    /// Creates a new composition state with a specific key.
    /// </summary>
    /// <param name="key">The initial key.</param>
    /// <param name="totalBars">Total bars in the composition.</param>
    /// <param name="totalPhases">Total narrative phases.</param>
    public CompositionState(Scale key, int totalBars, int totalPhases)
        : this()
    {
        Harmonic = new HarmonicState(key);
        PitchSpace.Tonic = key.Root;
        PitchSpace.Mode = key.Mode;
        Position.TotalBars = totalBars;
        Position.TotalPhases = totalPhases;
    }

    /// <summary>
    /// Applies a musical event and updates all relevant sub-states.
    /// </summary>
    /// <param name="event">The musical event to apply.</param>
    public void ApplyEvent(MusicalEvent @event)
    {
        EventCount++;
        var tensionBefore = Emotional.Tension;

        // Update position
        Position.Advance(@event.DurationBeats);

        // Update information state
        Information.RecordIC(@event.InformationContent, Position.Bar);

        // Update listener model
        var surprise = @event.InformationContent / 4.0; // Normalize IC to 0-1 range
        Listener.ProcessEvent(surprise, @event.IsResolution, tensionBefore);

        // Update mechanisms
        Mechanisms.ProcessEvent(
            @event.LoudnessChange,
            @event.RhythmStrength,
            @event.Expressiveness,
            surprise);
        Mechanisms.ApplyDecay();

        // Handle specific event types
        switch (@event.Type)
        {
            case MusicalEventType.ChordChange:
                if (@event.Chord != null)
                {
                    HandleChordChange(@event.Chord);
                }
                break;

            case MusicalEventType.Cadence:
                HandleCadence(@event);
                break;

            case MusicalEventType.Modulation:
                if (@event.Chord != null)
                {
                    HandleModulation(@event.Chord.Root);
                }
                break;

            case MusicalEventType.MotifStatement:
                // Motif handling would update ThematicState
                break;
        }

        // Update emotional state based on other states
        UpdateEmotionalState();
    }

    /// <summary>
    /// Advances to the next bar (for bar-level processing).
    /// </summary>
    public void AdvanceBar()
    {
        Position.NextBar();
        Harmonic.AdvanceBar();
        Thematic.AdvanceBar();
    }

    /// <summary>
    /// Enters a new narrative phase.
    /// </summary>
    /// <param name="phaseIndex">The new phase index.</param>
    /// <param name="phaseStartBar">The bar where this phase starts.</param>
    /// <param name="phaseDurationBars">The duration of this phase in bars.</param>
    public void EnterPhase(int phaseIndex, int phaseStartBar, int phaseDurationBars)
    {
        Position.PhaseIndex = phaseIndex;
        Position.UpdatePhaseProgress(phaseStartBar, phaseDurationBars);
    }

    /// <summary>
    /// Gets the overall composition tension (combining multiple sources).
    /// </summary>
    public double GetOverallTension()
    {
        // Combine harmonic tension, pitch space tension, and emotional tension
        var harmonicTension = Harmonic.CalculateTension();
        var pitchSpaceTension = PitchSpace.GetPitchSpaceTension();
        var emotionalTension = Emotional.Tension;

        // Weighted combination
        return harmonicTension * 0.4 + pitchSpaceTension * 0.3 + emotionalTension * 0.3;
    }

    /// <summary>
    /// Checks if a resolution is expected based on current state.
    /// </summary>
    public bool IsResolutionExpected()
    {
        return Harmonic.ExpectingCadence ||
               Listener.ExpectedResolution ||
               Emotional.Tension > 0.7;
    }

    /// <summary>
    /// Creates a deep copy of this composition state.
    /// </summary>
    public CompositionState Clone()
    {
        return new CompositionState
        {
            Position = Position.Clone(),
            Emotional = Emotional.Clone(),
            Harmonic = Harmonic.Clone(),
            Thematic = Thematic.Clone(),
            Listener = Listener.Clone(),
            Information = Information.Clone(),
            PitchSpace = PitchSpace.Clone(),
            Mechanisms = Mechanisms.Clone(),
            EventCount = EventCount
        };
    }

    /// <summary>
    /// Converts the composition state to a world state for GOAP planning.
    /// </summary>
    /// <returns>A dictionary of state values.</returns>
    public Dictionary<string, object> ToWorldState()
    {
        return new Dictionary<string, object>
        {
            ["tension"] = Emotional.Tension,
            ["brightness"] = Emotional.Brightness,
            ["energy"] = Emotional.Energy,
            ["warmth"] = Emotional.Warmth,
            ["stability"] = Emotional.Stability,
            ["valence"] = Emotional.Valence,
            ["harmonicFunction"] = Harmonic.CurrentFunction,
            ["barsSinceTonic"] = Harmonic.BarsSinceTonic,
            ["expectingCadence"] = Harmonic.ExpectingCadence,
            ["mainMotifIntroduced"] = Thematic.MainMotifIntroduced,
            ["barsSinceMainMotif"] = Thematic.BarsSinceMainMotif,
            ["attention"] = Listener.Attention,
            ["phaseProgress"] = Position.PhaseProgress,
            ["overallProgress"] = Position.OverallProgress
        };
    }

    private void HandleChordChange(Chord chord)
    {
        var previousChord = Harmonic.CurrentChord;
        Harmonic.SetChord(chord);

        // Update pitch space
        var distance = previousChord != null
            ? CalculateChordDistance(previousChord.Root, chord.Root)
            : 0;
        PitchSpace.MoveToChord(chord.Root, distance);

        // Register tension if on dominant
        if (Harmonic.CurrentFunction == MusicTheory.Harmony.HarmonicFunctionType.Dominant)
        {
            Listener.RegisterTensionEvent();
        }
    }

    private void HandleCadence(MusicalEvent @event)
    {
        if (@event.Chord != null)
        {
            Harmonic.SetChord(@event.Chord);
        }

        // Resolution affects emotional state
        Emotional.Tension *= 0.5;
        Emotional.Stability = Math.Min(1.0, Emotional.Stability + 0.3);

        // Exit any tonicization
        PitchSpace.ExitTonicization();
    }

    private void HandleModulation(PitchClass newTonic)
    {
        var newKey = new Scale(newTonic, ModeType.Major); // Simplified - would detect mode
        Harmonic.Modulate(newKey);
        PitchSpace.Modulate(newTonic, ModeType.Major);

        // Modulation increases tension temporarily
        Emotional.Tension = Math.Min(1.0, Emotional.Tension + 0.2);
        Emotional.Stability *= 0.7;
    }

    private void UpdateEmotionalState()
    {
        // Tension influenced by harmonic state
        var harmonicTension = Harmonic.CalculateTension();
        Emotional.Tension = Emotional.Tension * 0.7 + harmonicTension * 0.3;

        // Stability influenced by harmonic function
        if (Harmonic.CurrentFunction == MusicTheory.Harmony.HarmonicFunctionType.Tonic)
        {
            Emotional.Stability = Math.Min(1.0, Emotional.Stability + 0.1);
        }

        // Brightness influenced by mode
        if (PitchSpace.Mode is ModeType.Major or ModeType.Lydian)
        {
            Emotional.Brightness = Math.Min(1.0, Emotional.Brightness * 0.9 + 0.1);
        }
        else if (PitchSpace.Mode is ModeType.Minor or ModeType.Phrygian)
        {
            Emotional.Brightness = Math.Max(0.0, Emotional.Brightness * 0.9 - 0.05);
        }
    }

    private static int CalculateChordDistance(PitchClass from, PitchClass to)
    {
        var semitones = ((int)to - (int)from + 12) % 12;
        // Simple distance - could be enhanced with full TPS calculation
        return semitones switch
        {
            0 => 0,
            7 or 5 => 1,
            2 or 10 => 2,
            4 or 8 or 3 or 9 => 3,
            1 or 11 => 4,
            6 => 5,
            _ => 3
        };
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"CompositionState[Bar {Position.Bar}, {Emotional}, {Harmonic.CurrentFunction}]";
    }
}
