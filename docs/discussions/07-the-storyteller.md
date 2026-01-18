# Part 7: The Storyteller - Weaving It Together

> **Series**: [Understanding the Music Storyteller](./00-index-music-storyteller.md) | Part 7 of 7

## From Theory to Music

We've explored the foundational theories:
- [Part 1: Pitch Hierarchy](./01-pitch-hierarchy-tonal-pitch-space.md) - How notes relate to a tonal center
- [Part 2: ITPRA](./02-expectation-itpra.md) - How expectation creates emotion
- [Part 3: BRECVEMA](./03-brecvema.md) - Eight pathways to musical emotion
- [Part 4: Tension Mathematics](./04-mathematics-of-tension.md) - Quantifying harmonic tension
- [Part 5: Information Theory](./05-information-theory.md) - Surprise and uncertainty
- [Part 6: GOAP Planning](./06-goap-planning.md) - Finding optimal action sequences

Now we bring everything together in the **Storyteller** - the orchestrator that transforms composition requests into purposeful musical journeys.

---

## The Complete Pipeline

```
Complete Composition Pipeline
=============================

┌─────────────────────────────────────────────────────────────────────┐
│                      COMPOSITION REQUEST                            │
│  "I want a dramatic 32-bar piece that builds to a climax"          │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    1. NARRATIVE SELECTION                           │
│                                                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                 │
│  │   Journey   │  │  Tension &  │  │   Simple    │                 │
│  │ and Return  │  │   Release   │  │    Arc      │                 │
│  │  (celtic)   │  │ (dramatic)  │  │  (short)    │                 │
│  └─────────────┘  └──────┬──────┘  └─────────────┘                 │
│                          │                                          │
│            Tags match: "dramatic" ────► Selected!                   │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    2. PHASE DECOMPOSITION                           │
│                                                                     │
│  Stability  →  Disturbance  →  Building  →  Climax  →  Release     │
│    (20%)         (20%)          (25%)       (10%)      (15%)       │
│                                                                     │
│  Each phase has:                                                    │
│    • Emotional target (6D state)                                    │
│    • Harmonic character (stable, departing, building, etc.)        │
│    • Thematic goals (introduce, develop, recapitulate)             │
│    • Musical character (texture, rhythm, register)                 │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    3. GOAP PLANNING (per phase)                     │
│                                                                     │
│  Current State: Tension=0.2, Stability=0.8                         │
│  Phase Target:  Tension=0.8, Stability=0.3                         │
│                                                                     │
│  A* Search finds optimal action sequence:                          │
│    → add_secondary_dominant                                        │
│    → increase_harmonic_rhythm                                      │
│    → raise_register                                                │
│    → add_rhythmic_drive                                            │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    4. INTENT GENERATION                             │
│                                                                     │
│  Each action becomes a CompositionIntent:                          │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ CompositionIntent                                            │   │
│  │ ├─ EmotionalTarget: { T=0.5, B=0.4, E=0.6, W=0.4, S=0.5 }  │   │
│  │ ├─ HarmonicIntent: { AvoidTonic=true, TargetTension=0.5 }   │   │
│  │ ├─ MelodicIntent: { Contour=Ascending, EnergyLevel=0.6 }    │   │
│  │ ├─ ThematicIntent: { DevelopMotif=true }                    │   │
│  │ └─ Bars: 4                                                   │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    5. MUSIC THEORY ENGINE                           │
│                    (music-theory SDK)                               │
│                                                                     │
│  Intent → Chord Progression + Melody + Voice Leading               │
│                                                                     │
│  Uses: TPS chord distance, TIS tension, voice leading rules,       │
│        information-theoretic surprise optimization                  │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       MUSICAL OUTPUT                                │
│                                                                     │
│  32 bars of purposeful, emotionally coherent music                 │
│  with narrative arc: Stability → Climax → Resolution               │
└─────────────────────────────────────────────────────────────────────┘
```

---

## The Six-Dimensional Emotional Space

Every composition lives in a 6-dimensional space:

```
Six Emotional Dimensions
========================

Dimension     Low (0)              High (1)              Musical Expression
─────────────────────────────────────────────────────────────────────────────
TENSION       Resolution           Climax                Harmonic tension,
              │●─────────────────────────────────│       dissonance

BRIGHTNESS    Dark                 Bright                Major/minor mode,
              │─────────●───────────────────────│       register, timbre

ENERGY        Calm                 Energetic             Tempo, rhythmic
              │───────────●─────────────────────│       density, dynamics

WARMTH        Distant              Intimate              Consonance, close
              │──────────────────●──────────────│       voicing, lyricism

STABILITY     Unstable             Grounded              Tonic vs. non-tonic,
              │────────────────────────●────────│       metric regularity

VALENCE       Negative             Positive              Overall emotional
              │─────────────●───────────────────│       character
```

### Presets

```csharp
public static class Presets
{
    /// Neutral starting point
    public static EmotionalState Neutral => new(0.2, 0.5, 0.5, 0.5, 0.8, 0.5);

    /// High tension, low stability
    public static EmotionalState Tense => new(0.8, 0.4, 0.7, 0.3, 0.2, 0.4);

    /// Low tension, high stability, warm
    public static EmotionalState Peaceful => new(0.1, 0.6, 0.3, 0.8, 0.9, 0.7);

    /// High energy, bright, positive
    public static EmotionalState Joyful => new(0.3, 0.8, 0.8, 0.7, 0.7, 0.9);

    /// Maximum tension climax point
    public static EmotionalState Climax => new(0.95, 0.6, 0.9, 0.4, 0.1, 0.5);

    /// Resolution after climax
    public static EmotionalState Resolution => new(0.1, 0.6, 0.4, 0.7, 0.95, 0.7);
}
```

### Distance Calculation

Movement through emotional space is measured by Euclidean distance:

```csharp
public double DistanceTo(EmotionalState target)
{
    var dt = Tension - target.Tension;
    var db = Brightness - target.Brightness;
    var de = Energy - target.Energy;
    var dw = Warmth - target.Warmth;
    var ds = Stability - target.Stability;
    var dv = Valence - target.Valence;

    return Math.Sqrt(dt*dt + db*db + de*de + dw*dw + ds*ds + dv*dv);
}
```

This distance is used by GOAP planning as the heuristic function (h(n)) - estimating how far the current state is from the goal.

---

## Narrative Templates

Templates define emotional journeys as sequences of **phases**:

```
Built-in Templates
==================

┌─────────────────────────────────────────────────────────────────────┐
│  TENSION AND RELEASE                                                │
│  "The fundamental dramatic arc of music"                            │
│                                                                     │
│  Tension                                                            │
│    1.0 │                    ●                                       │
│        │                  ╱   ╲                                     │
│    0.8 │                ●       ╲                                   │
│        │              ╱          ╲                                  │
│    0.6 │            ╱             ╲                                 │
│        │          ╱                 ●                               │
│    0.4 │        ╱                     ╲                             │
│        │      ●                         ╲                           │
│    0.2 │    ╱                             ●─────●                   │
│        │  ●                                                         │
│    0.0 └───┬────────┬────────┬────────┬────────┬────────┬──────    │
│         Stability  Disturb  Building  Climax  Release  Peace       │
│           20%       20%       25%       10%     15%      10%        │
│                                                                     │
│  Tags: dramatic, universal, classical, cinematic, emotional         │
└─────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────┐
│  JOURNEY AND RETURN                                                 │
│  "A Celtic-inspired arc of leaving home and returning transformed"  │
│                                                                     │
│  Tension                                                            │
│    0.8 │              ●───●                                         │
│        │            ╱       ╲                                       │
│    0.6 │          ╱           ╲                                     │
│        │        ●               ╲                                   │
│    0.4 │      ╱                   ╲                                 │
│        │    ●                       ╲                               │
│    0.2 │  ●                           ●───●                         │
│        │                                                            │
│    0.0 └───┬────────┬────────┬────────┬────────                    │
│           Home    Departure  Adventure Return                       │
│           25%       25%        25%       25%                        │
│                                                                     │
│  Tags: celtic, journey, adventure, folk, heroic                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Template Definition

```csharp
public static class TensionAndRelease
{
    public static NarrativeTemplate Template { get; } = new()
    {
        Id = "tension_and_release",
        Name = "Tension and Release",
        Description = "The fundamental dramatic arc of music.",
        Imagery =
        [
            "Calm waters, a quiet beginning",
            "Ripples appear, something stirs beneath",
            "Waves build, momentum gathering",
            "The storm breaks, full intensity",
            "Waters calming, tension dissolving",
            "Still waters again, transformed by the storm"
        ],
        Tags = ["dramatic", "universal", "classical", "cinematic"],
        MinimumBars = 32,
        IdealBars = 64,
        Phases =
        [
            // Phase 1: Stability (20%)
            new NarrativePhase
            {
                Name = "Stability",
                RelativeDuration = 0.20,
                EmotionalTarget = new EmotionalState(0.2, 0.5, 0.3, 0.6, 0.8, 0.6),
                HarmonicCharacter = HarmonicCharacter.Stable,
                ThematicGoals = ThematicGoals.Introduction,
                MusicalCharacter = MusicalCharacter.Intimate
            },

            // Phase 2: Disturbance (20%)
            new NarrativePhase
            {
                Name = "Disturbance",
                RelativeDuration = 0.20,
                EmotionalTarget = new EmotionalState(0.5, 0.4, 0.5, 0.4, 0.5, 0.4),
                HarmonicCharacter = HarmonicCharacter.Departing,
                AvoidResolution = true  // Keep tension building
            },

            // Phase 3: Building (25%)
            new NarrativePhase
            {
                Name = "Building",
                RelativeDuration = 0.25,
                EmotionalTarget = new EmotionalState(0.8, 0.5, 0.8, 0.3, 0.3, 0.4),
                HarmonicCharacter = HarmonicCharacter.Building,
                MusicalCharacter = MusicalCharacter.Driving,
                EndingCadence = CadencePreference.Half  // Maintain tension
            },

            // Phase 4: Climax (10%)
            new NarrativePhase
            {
                Name = "Climax",
                RelativeDuration = 0.10,
                EmotionalTarget = EmotionalState.Presets.Climax,
                HarmonicCharacter = HarmonicCharacter.Climactic,
                MusicalCharacter = MusicalCharacter.Climactic
            },

            // Phase 5: Release (15%)
            new NarrativePhase
            {
                Name = "Release",
                RelativeDuration = 0.15,
                EmotionalTarget = new EmotionalState(0.3, 0.6, 0.4, 0.6, 0.7, 0.7),
                HarmonicCharacter = HarmonicCharacter.Resolving,
                EndingCadence = CadencePreference.Authentic
            },

            // Phase 6: Peace (10%)
            new NarrativePhase
            {
                Name = "Peace",
                RelativeDuration = 0.10,
                EmotionalTarget = EmotionalState.Presets.Peaceful,
                HarmonicCharacter = HarmonicCharacter.Peaceful,
                RequireResolution = true,
                EndingCadence = CadencePreference.Plagal
            }
        ]
    };
}
```

---

## Phase Components

Each phase specifies **four types of targets**:

### 1. Emotional Target (6D State)

```
The primary driver - where the emotional state should be at the end of this phase.
```

### 2. Harmonic Character

```csharp
public enum HarmonicCharacter
{
    Stable,      // Tonic-centered
    Departing,   // Moving away from tonic
    Wandering,   // Distant from tonic
    Building,    // Toward climax
    Climactic,   // Maximum tension
    Resolving,   // Returning to tonic
    Peaceful     // Post-resolution stability
}
```

### 3. Thematic Goals

```csharp
public sealed class ThematicGoals
{
    public bool IntroduceMainMotif { get; init; }
    public bool ReturnMainMotif { get; init; }
    public bool DevelopMotif { get; init; }
    public bool AllowSecondaryMotif { get; init; }
    public IReadOnlyList<MotifTransformationType> PreferredTransformations { get; init; }
}
```

### 4. Musical Character

```csharp
public sealed class MusicalCharacter
{
    public double TexturalDensity { get; init; }   // 0=sparse → 1=full
    public double RhythmicActivity { get; init; }  // 0=static → 1=driving
    public double RegisterHeight { get; init; }    // 0=low → 1=high
    public bool AllowDynamicChanges { get; init; }
}
```

---

## The Storyteller Orchestrator

```csharp
public sealed class Storyteller
{
    private readonly ActionLibrary _actions;
    private readonly GOAPPlanner _planner;
    private readonly IntentGenerator _intentGenerator;
    private readonly NarrativeSelector _narrativeSelector;

    public CompositionResult Compose(CompositionRequest request)
    {
        // 1. Select narrative template
        var narrative = _narrativeSelector.Select(request);

        // 2. Initialize state
        var state = new CompositionState();
        var sections = new List<CompositionSection>();
        var totalBars = request.TotalBars > 0
            ? request.TotalBars
            : narrative.IdealBars;

        // 3. Process each phase
        for (var i = 0; i < narrative.Phases.Count; i++)
        {
            var phase = narrative.Phases[i];
            var (startBar, endBar) = narrative.GetPhaseBarRange(i, totalBars);

            // 4. Create GOAP plan to reach phase target
            var goal = GOAPGoal.FromNarrativePhase(phase);
            var worldState = WorldState.FromCompositionState(state);
            var plan = _planner.CreatePlan(worldState, goal);

            // 5. Generate intents from plan
            var intents = _intentGenerator.FromPlan(plan, state, phase, endBar - startBar + 1);

            sections.Add(new CompositionSection
            {
                PhaseName = phase.Name,
                StartBar = startBar,
                EndBar = endBar,
                Plan = plan,
                Intents = intents.ToList()
            });

            // 6. Apply plan effects to state for next phase
            foreach (var action in plan.MusicalActions)
                action.Apply(state);
        }

        return new CompositionResult
        {
            Narrative = narrative,
            Sections = sections,
            TotalBars = totalBars
        };
    }
}
```

---

## Complete Example: 32-Bar Composition

Let's trace a request through the entire pipeline:

```
Request: CompositionRequest.ForTemplate("tension_and_release", 32)

Step 1: Narrative Selection
===========================
Selected: "Tension and Release" template
Total bars: 32

Step 2: Phase Decomposition
===========================
┌──────────────┬──────────┬───────────┬──────────────────────┐
│ Phase        │ Duration │ Bar Range │ Emotional Target     │
├──────────────┼──────────┼───────────┼──────────────────────┤
│ Stability    │ 20%      │ 1-6       │ T=0.2 S=0.8 E=0.3   │
│ Disturbance  │ 20%      │ 7-12      │ T=0.5 S=0.5 E=0.5   │
│ Building     │ 25%      │ 13-20     │ T=0.8 S=0.3 E=0.8   │
│ Climax       │ 10%      │ 21-23     │ T=0.95 S=0.2 E=0.95 │
│ Release      │ 15%      │ 24-28     │ T=0.3 S=0.7 E=0.4   │
│ Peace        │ 10%      │ 29-32     │ T=0.1 S=0.9 E=0.2   │
└──────────────┴──────────┴───────────┴──────────────────────┘

Step 3: GOAP Planning (Building Phase Example)
==============================================
Current state: T=0.5, S=0.5, E=0.5
Target state:  T=0.8, S=0.3, E=0.8

A* search finds optimal sequence:
  Action 1: add_secondary_dominant  (ΔT=+0.1, ΔS=-0.1)
  Action 2: increase_harmonic_rhythm (ΔT=+0.1, ΔE=+0.1)
  Action 3: add_rhythmic_drive      (ΔE=+0.2)
  Action 4: raise_register          (ΔE=+0.1)

Plan cost: 4 actions
Estimated reach: T=0.8, S=0.3, E=0.9 ✓

Step 4: Intent Generation
=========================
Building Phase → 8 bars (13-20) → 4 actions → 2 bars each

Intent 1 (bars 13-14):
┌───────────────────────────────────────────────────────┐
│ EmotionalTarget: { T=0.6, B=0.5, E=0.6, W=0.4, S=0.4 }│
│ HarmonicIntent:                                        │
│   AvoidTonic: true                                     │
│   EncourageSecondaryDominants: true                   │
│   TargetTension: 0.6                                  │
│ MelodicIntent:                                        │
│   Contour: Ascending                                  │
│   DissonanceLevel: 0.2                                │
│ Bars: 2                                               │
│ AvoidStrongEnding: true                               │
└───────────────────────────────────────────────────────┘

Intent 2 (bars 15-16):
┌───────────────────────────────────────────────────────┐
│ EmotionalTarget: { T=0.7, B=0.5, E=0.7, W=0.4, S=0.4 }│
│ HarmonicIntent:                                        │
│   HarmonicRhythmDensity: 2.0                          │
│   TargetTension: 0.7                                  │
│ Bars: 2                                               │
│ AvoidStrongEnding: true                               │
└───────────────────────────────────────────────────────┘

... continues through all phases ...

Step 5: Final Output
====================
CompositionResult with 6 sections, ~15 total intents
Ready for music-theory engine to generate actual notes
```

---

## Intent Structure

The **CompositionIntent** bridges storytelling to music generation:

```csharp
public sealed record CompositionIntent
{
    // Primary emotional target
    public required EmotionalState EmotionalTarget { get; init; }

    // Harmonic guidance
    public HarmonicIntent Harmony { get; init; }

    // Melodic guidance
    public MelodicIntent Melody { get; init; }

    // Thematic guidance
    public ThematicIntent Thematic { get; init; }

    // Duration
    public int Bars { get; init; }

    // Character
    public HarmonicCharacter HarmonicCharacter { get; init; }
    public MusicalCharacter MusicalCharacter { get; init; }

    // Performance
    public double TempoMultiplier { get; init; }
    public double DynamicLevel { get; init; }

    // Structural flags
    public bool IsTransition { get; init; }
    public bool AvoidStrongEnding { get; init; }
    public bool RequireStrongEnding { get; init; }

    // Additional hints
    public IReadOnlyDictionary<string, string> Hints { get; init; }
}
```

### Intent Sub-Components

```csharp
// What the harmony should do
public sealed record HarmonicIntent
{
    public bool AvoidTonic { get; init; }
    public bool EmphasizeDominant { get; init; }
    public bool AllowModalInterchange { get; init; }
    public bool PreferChromaticVoiceLeading { get; init; }
    public bool EncourageSecondaryDominants { get; init; }
    public double HarmonicRhythmDensity { get; init; }
    public double TargetTensionLevel { get; init; }
    public CadencePreference? EndingCadence { get; init; }
}

// What the melody should do
public sealed record MelodicIntent
{
    public MelodicContour Contour { get; init; }  // Ascending, Descending, Balanced
    public MelodicRange Range { get; init; }      // Low, Middle, High
    public double RhythmicDensity { get; init; }
    public double DissonanceLevel { get; init; }
    public double EnergyLevel { get; init; }
    public bool AllowSyncopation { get; init; }
    public bool EndOnStable { get; init; }
}

// What to do with motifs
public sealed record ThematicIntent
{
    public bool IntroduceMainMotif { get; init; }
    public bool ReturnMainMotif { get; init; }
    public MotifTransformationType? TransformationType { get; init; }
    public bool AllowFragmentation { get; init; }
    public bool AllowSecondaryMotif { get; init; }
    public double TransformationDegree { get; init; }
}
```

---

## Replanning

The Storyteller can adapt when actual results diverge from expected:

```csharp
public ReplanDecision EvaluateReplan(PlanExecution execution, CompositionState actualState)
{
    var actualWorld = WorldState.FromCompositionState(actualState);
    return _replanner.Evaluate(execution, actualWorld);
}

public Plan Replan(CompositionState state, NarrativePhase phase)
{
    var goal = GOAPGoal.FromNarrativePhase(phase);
    return _replanner.Replan(state, goal);
}
```

**When to replan:**
- Actual tension diverges >0.2 from expected
- A required cadence failed to achieve resolution
- Unexpected modulation changed the key context

---

## How Theories Integrate

```
Theory Integration Map
======================

┌─────────────────────────────────────────────────────────────────────┐
│                         STORYTELLER                                 │
│              (Orchestrates the composition)                         │
└─────────────────────────────────────────────────────────────────────┘
                               │
        ┌──────────────────────┼──────────────────────┐
        │                      │                      │
        ▼                      ▼                      ▼
┌───────────────┐      ┌───────────────┐      ┌───────────────┐
│  NARRATIVE    │      │     GOAP      │      │    INTENT     │
│  TEMPLATES    │      │   PLANNING    │      │  GENERATION   │
│               │      │               │      │               │
│ Defines the   │      │ Finds optimal │      │ Converts to   │
│ emotional     │      │ action paths  │      │ music params  │
│ journey       │      │               │      │               │
└───────────────┘      └───────────────┘      └───────────────┘
        │                      │                      │
        └──────────────────────┼──────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      MUSIC-THEORY ENGINE                            │
│                                                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌───────────┐ │
│  │ TPS (Part 1)│  │ITPRA (Part 2│  │TIS (Part 4) │  │IC (Part 5)│ │
│  │             │  │             │  │             │  │           │ │
│  │ Pitch hier- │  │ Expectation │  │ Tension     │  │ Surprise  │ │
│  │ archy, chord│  │ response,   │  │ calculation │  │ & entropy │ │
│  │ distance    │  │ contrastive │  │ T = 0.402D  │  │ IC=-log₂P │ │
│  │ δ = i+j+k   │  │ valence     │  │ + 0.246H... │  │           │ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └───────────┘ │
│                                                                     │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────────┐ │
│  │BRECVEMA     │  │ Listener    │  │ Chord/Melody Generation     │ │
│  │(Part 3)     │  │ Model       │  │                             │ │
│  │             │  │             │  │ Voice leading, melodic      │ │
│  │ 8 emotion   │  │ 4 expect.   │  │ attraction, cadence         │ │
│  │ mechanisms  │  │ types       │  │ selection                   │ │
│  └─────────────┘  └─────────────┘  └─────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────────────┐
│                       MUSICAL OUTPUT                                │
│                                                                     │
│  Purposeful music with:                                            │
│  • Coherent narrative arc                                          │
│  • Optimal surprise/predictability balance                         │
│  • Appropriate tension curves                                      │
│  • Emotionally resonant progression                                │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Summary: The Complete Picture

| Layer | Component | Function |
|-------|-----------|----------|
| **Request** | CompositionRequest | What the user wants |
| **Selection** | NarrativeSelector | Chooses best template |
| **Structure** | NarrativeTemplate | Defines emotional arc |
| **Phases** | NarrativePhase | Subdivides the journey |
| **Planning** | GOAPPlanner | Finds action sequences |
| **Actions** | ActionLibrary | Musical operations |
| **Intent** | IntentGenerator | Converts to parameters |
| **Theory** | TPS, TIS, IC, ITPRA | Mathematical foundations |
| **Output** | CompositionResult | Structured music data |

---

## Academic Foundations

The Storyteller integrates insights from multiple domains:

### Music Cognition
- **Huron, D.** (2006). *Sweet Anticipation: Music and the Psychology of Expectation*. MIT Press.
- **Juslin, P.N.** (2013). "From everyday emotions to aesthetic emotions." *Physics of Life Reviews*.
- **Meyer, L.B.** (1956). *Emotion and Meaning in Music*. University of Chicago Press.

### Music Theory
- **Lerdahl, F.** (2001). *Tonal Pitch Space*. Oxford University Press.
- **Navarro-Cáceres, M.** et al. (2020). "A Computational Model of Tonal Tension." *Entropy*.

### Information Theory
- **Pearce, M.T.** (2005). *Statistical Models of Melodic Structure*. PhD Thesis.
- **Cheung, V.K.M.** et al. (2019). "Uncertainty and Surprise Jointly Predict Musical Pleasure." *Current Biology*.

### AI Planning
- **Orkin, J.** (2006). "Three states and a plan: The AI of FEAR." *Game Developers Conference*.

---

## Key Takeaways

1. **Narrative templates** define emotional journeys as phase sequences
2. **Six-dimensional emotional space** captures the musical state
3. **GOAP planning** finds optimal paths through emotional space
4. **Intent generation** bridges story decisions to music parameters
5. **Theory integration** ensures musical validity at every step
6. **Replanning** allows adaptation when execution diverges from plan

---

**This concludes the Music Storyteller SDK documentation series.**

The system transforms the question "What music should play here?" into a principled answer: select a narrative, decompose into phases, plan actions to reach emotional targets, generate intents, and let validated music theory handle the rest.

---

*This document is part of the [Music Storyteller SDK](https://github.com/BeyondImmersion/bannou) documentation.*
