namespace BeyondImmersion.Bannou.MusicStoryteller.State;

/// <summary>
/// Tracks the activation of BRECVEMA emotional mechanisms.
/// BRECVEMA: Brainstem reflex, Rhythmic entrainment, Evaluative conditioning,
/// Contagion, Visual imagery, Episodic memory, Musical expectancy, Aesthetic judgment.
/// </summary>
public sealed class MechanismState
{
    /// <summary>
    /// Brainstem reflex activation (0-1).
    /// Triggered by sudden loud sounds, dissonance, or rapid changes.
    /// </summary>
    public double BrainstemReflex { get; set; }

    /// <summary>
    /// Rhythmic entrainment activation (0-1).
    /// Triggered by strong, regular pulse patterns.
    /// </summary>
    public double RhythmicEntrainment { get; set; }

    /// <summary>
    /// Evaluative conditioning activation (0-1).
    /// Triggered by learned associations with musical elements.
    /// </summary>
    public double EvaluativeConditioning { get; set; }

    /// <summary>
    /// Emotional contagion activation (0-1).
    /// Triggered by perceiving emotion in the music's expression.
    /// </summary>
    public double Contagion { get; set; }

    /// <summary>
    /// Visual imagery activation (0-1).
    /// Triggered by music that evokes mental images.
    /// </summary>
    public double VisualImagery { get; set; }

    /// <summary>
    /// Episodic memory activation (0-1).
    /// Triggered by music that evokes personal memories.
    /// </summary>
    public double EpisodicMemory { get; set; }

    /// <summary>
    /// Musical expectancy activation (0-1).
    /// Triggered by confirmation or violation of expectations.
    /// This is the ITPRA mechanism - the most relevant for composition.
    /// </summary>
    public double MusicalExpectancy { get; set; }

    /// <summary>
    /// Aesthetic judgment activation (0-1).
    /// Meta-level appreciation of the music's qualities.
    /// </summary>
    public double AestheticJudgment { get; set; }

    /// <summary>
    /// Gets the total emotional activation (sum of mechanisms).
    /// </summary>
    public double TotalActivation =>
        BrainstemReflex + RhythmicEntrainment + EvaluativeConditioning +
        Contagion + VisualImagery + EpisodicMemory +
        MusicalExpectancy + AestheticJudgment;

    /// <summary>
    /// Gets the dominant mechanism (highest activation).
    /// </summary>
    public MechanismType DominantMechanism
    {
        get
        {
            var max = BrainstemReflex;
            var dominant = MechanismType.BrainstemReflex;

            if (RhythmicEntrainment > max) { max = RhythmicEntrainment; dominant = MechanismType.RhythmicEntrainment; }
            if (EvaluativeConditioning > max) { max = EvaluativeConditioning; dominant = MechanismType.EvaluativeConditioning; }
            if (Contagion > max) { max = Contagion; dominant = MechanismType.Contagion; }
            if (VisualImagery > max) { max = VisualImagery; dominant = MechanismType.VisualImagery; }
            if (EpisodicMemory > max) { max = EpisodicMemory; dominant = MechanismType.EpisodicMemory; }
            if (MusicalExpectancy > max) { max = MusicalExpectancy; dominant = MechanismType.MusicalExpectancy; }
            if (AestheticJudgment > max) { dominant = MechanismType.AestheticJudgment; }

            return dominant;
        }
    }

    /// <summary>
    /// Applies natural decay to all mechanisms over time.
    /// </summary>
    /// <param name="decayRate">Decay rate per call (typically 0.05-0.1).</param>
    public void ApplyDecay(double decayRate = 0.05)
    {
        BrainstemReflex = Math.Max(0, BrainstemReflex - decayRate * 2); // Fast decay
        RhythmicEntrainment = Math.Max(0, RhythmicEntrainment - decayRate * 0.5); // Slow decay
        EvaluativeConditioning = Math.Max(0, EvaluativeConditioning - decayRate);
        Contagion = Math.Max(0, Contagion - decayRate);
        VisualImagery = Math.Max(0, VisualImagery - decayRate * 0.5); // Slow decay
        EpisodicMemory = Math.Max(0, EpisodicMemory - decayRate * 0.5); // Slow decay
        MusicalExpectancy = Math.Max(0, MusicalExpectancy - decayRate);
        AestheticJudgment = Math.Max(0, AestheticJudgment - decayRate * 0.3); // Very slow decay
    }

    /// <summary>
    /// Updates mechanisms based on a musical event.
    /// </summary>
    /// <param name="loudnessJump">Change in loudness (0-1).</param>
    /// <param name="rhythmStrength">Strength of rhythmic pulse (0-1).</param>
    /// <param name="expressiveness">Emotional expressiveness (0-1).</param>
    /// <param name="surpriseLevel">Information content / surprise (0-1).</param>
    public void ProcessEvent(double loudnessJump, double rhythmStrength, double expressiveness, double surpriseLevel)
    {
        // Brainstem reflex: sudden changes, loud sounds
        if (loudnessJump > 0.3)
        {
            BrainstemReflex = Math.Min(1.0, BrainstemReflex + loudnessJump);
        }

        // Rhythmic entrainment: builds with consistent pulse
        RhythmicEntrainment = RhythmicEntrainment * 0.8 + rhythmStrength * 0.2;

        // Contagion: perceiving emotion in expression
        Contagion = Math.Min(1.0, Contagion * 0.7 + expressiveness * 0.3);

        // Musical expectancy: surprise/confirmation
        MusicalExpectancy = Math.Min(1.0, MusicalExpectancy + surpriseLevel * 0.3);

        // Aesthetic judgment builds slowly with variety and quality
        if (surpriseLevel is > 0.2 and < 0.8)
        {
            AestheticJudgment = Math.Min(1.0, AestheticJudgment + 0.02);
        }
    }

    /// <summary>
    /// Triggers visual imagery (e.g., for programmatic/evocative sections).
    /// </summary>
    /// <param name="strength">Imagery strength (0-1).</param>
    public void TriggerImagery(double strength)
    {
        VisualImagery = Math.Min(1.0, VisualImagery + strength);
    }

    /// <summary>
    /// Triggers episodic memory (e.g., for theme returns).
    /// </summary>
    /// <param name="strength">Memory strength (0-1).</param>
    public void TriggerEpisodicMemory(double strength)
    {
        EpisodicMemory = Math.Min(1.0, EpisodicMemory + strength);
    }

    /// <summary>
    /// Creates a deep copy of this mechanism state.
    /// </summary>
    public MechanismState Clone()
    {
        return new MechanismState
        {
            BrainstemReflex = BrainstemReflex,
            RhythmicEntrainment = RhythmicEntrainment,
            EvaluativeConditioning = EvaluativeConditioning,
            Contagion = Contagion,
            VisualImagery = VisualImagery,
            EpisodicMemory = EpisodicMemory,
            MusicalExpectancy = MusicalExpectancy,
            AestheticJudgment = AestheticJudgment
        };
    }

    public override string ToString()
    {
        return $"Mechanisms[Total={TotalActivation:F2}, Dominant={DominantMechanism}, Expectancy={MusicalExpectancy:F2}]";
    }
}

/// <summary>
/// The BRECVEMA mechanism types.
/// </summary>
public enum MechanismType
{
    /// <summary>Automatic response to acoustic features.</summary>
    BrainstemReflex,

    /// <summary>Synchronization with musical pulse.</summary>
    RhythmicEntrainment,

    /// <summary>Learned emotional associations.</summary>
    EvaluativeConditioning,

    /// <summary>Perceiving emotion in music.</summary>
    Contagion,

    /// <summary>Mental imagery evoked by music.</summary>
    VisualImagery,

    /// <summary>Personal memories triggered by music.</summary>
    EpisodicMemory,

    /// <summary>Expectation confirmation/violation (ITPRA).</summary>
    MusicalExpectancy,

    /// <summary>Appreciation of musical quality.</summary>
    AestheticJudgment
}
