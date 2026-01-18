# Understanding the Music Storyteller: A Theoretical Deep Dive

> **Series Index** | 8 parts exploring the music cognition research behind Bannou's procedural music generation system

## What Does This System Do?

The Music Storyteller is an AI composition system that doesn't just generate *notes*—it generates *musical experiences*. Given a request like "compose a 32-bar piece with building tension," it:

1. **Selects a narrative arc** (like a story with beginning, middle, and end)
2. **Plans musical actions** using AI planning (GOAP) to reach emotional goals
3. **Generates intents** that guide harmony, melody, and rhythm
4. **Produces MIDI-JSON** ready for playback

The key insight: **Music is narrative**. Rather than randomly applying music theory rules, the system plans emotional journeys the way a human composer thinks about form and drama.

---

## The Big Picture

This system stands on the shoulders of decades of music cognition research. Here's how it all connects:

```
┌─────────────────────────────────────────────────────────────────────┐
│                     BRECVEMA (Juslin 2013)                          │
│              8 parallel mechanisms for musical emotion               │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ Brainstem│Rhythmic │Evaluative│Contagion│Visual  │Episodic │   │
│  │ Reflex   │Entrainmt│Condition │         │Imagery │Memory   │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                              │                                      │
│                     ┌────────▼────────┐                            │
│                     │    MUSICAL      │                            │
│                     │   EXPECTANCY    │ ◄── Mechanism #7           │
│                     └────────┬────────┘                            │
│                              │                                      │
└──────────────────────────────┼──────────────────────────────────────┘
                               │
         ┌─────────────────────┼─────────────────────┐
         │                     │                     │
         ▼                     ▼                     ▼
┌─────────────────┐   ┌─────────────────┐   ┌─────────────────┐
│  ITPRA (Huron)  │   │  TPS (Lerdahl)  │   │ Information     │
│                 │   │                 │   │ Theory (Pearce) │
│  WHY we respond │   │  HOW MUCH       │   │                 │
│  to expectations│   │  tension/       │   │  Entropy &      │
│                 │   │  distance       │   │  Surprise       │
│  Imagination    │   │                 │   │                 │
│  Tension        │   │  Basic Space    │   │  IC = -log₂(p)  │
│  Prediction     │   │  Chord Distance │   │                 │
│  Reaction       │   │  Attraction     │   │                 │
│  Appraisal      │   │                 │   │                 │
└────────┬────────┘   └────────┬────────┘   └────────┬────────┘
         │                     │                     │
         └─────────────────────┼─────────────────────┘
                               │
                               ▼
                 ┌─────────────────────────┐
                 │     STORYTELLER         │
                 │                         │
                 │  Narrative Templates    │
                 │  GOAP Planning          │
                 │  Emotional State (6D)   │
                 │  Intent Generation      │
                 │                         │
                 └─────────────────────────┘
                               │
                               ▼
                        ┌─────────────┐
                        │  MIDI-JSON  │
                        │   Output    │
                        └─────────────┘
```

**Key Insight**: These frameworks aren't competing—they operate at different levels:
- **BRECVEMA** explains *through what pathways* music affects us (8 parallel mechanisms)
- **ITPRA** explains *why* expectation creates emotion (5-stage psychological response)
- **TPS** provides *how much* tension and distance to quantify (mathematical formulas)
- **Information Theory** measures *surprise* statistically

---

## Series Navigation

### Part 1: [Pitch Hierarchy: Lerdahl's Tonal Pitch Space](#)
> Why does the tonic feel like "home"? Why do some chords want to resolve?

Lerdahl's 5-level pitch hierarchy provides the foundation for understanding harmonic stability and tension. Learn about the Basic Space, chord distance formula (δ), and how we quantify the "pull" between pitches.

**Best for**: Understanding harmonic fundamentals

---

### Part 2: [Musical Expectation: Why Surprise Feels Good](#)
> Why do we enjoy music we already know? Why does a deceptive cadence feel "surprising"?

Huron's ITPRA model explains the five-stage response to musical events, plus the Cheung model reveals that pleasure comes from the *interaction* of entropy and surprise—not surprise alone.

**Best for**: Understanding the psychology of musical emotion

---

### Part 3: [BRECVEMA: Eight Paths to Musical Emotion](#)
> How does a loud chord startle us? Why does a waltz make us sway?

Juslin's BRECVEMA framework identifies eight parallel mechanisms through which music induces emotion. Our implementation tracks all eight with different decay rates.

**Best for**: Understanding the diversity of emotional responses

---

### Part 4: [The Mathematics of Tension](#)
> Can we put a number on how "tense" a chord progression feels?

The Navarro TIS formula combines four components (dissonance, hierarchy, tonal distance, voice leading) into a single tension value. We show the weights and how they're implemented.

**Best for**: Quantitative understanding of harmonic tension

---

### Part 5: [Information Theory in Music](#)
> What makes an event "surprising"? How do we track listener expectations?

Entropy, information content, and the four types of expectation (schematic, veridical, dynamic, conscious) form the statistical backbone of our expectation tracking.

**Best for**: Understanding prediction and probability in music

---

### Part 6: [GOAP: Planning Musical Narratives](#)
> How does the AI decide what to do next?

Goal-Oriented Action Planning (GOAP) from game AI adapts to music composition. The system searches through possible musical actions to find paths that achieve emotional goals.

**Best for**: Understanding the AI planning architecture

---

### Part 7: [The Storyteller: Weaving It Together](#)
> How do all these pieces combine into a working composition system?

A complete walkthrough of the Storyteller, from composition request through narrative selection, GOAP planning, intent generation, and final output.

**Best for**: Understanding the complete pipeline

---

## Reading Paths

| If you're a... | Start here | Then read |
|----------------|------------|-----------|
| **Composer/Musician** | Part 1 (Pitch) | → Part 4 (Tension) → Part 6 (Planning) → Part 7 |
| **Software Developer** | Part 5 (Information) | → Part 6 (GOAP) → Part 7 (Integration) |
| **Music Cognition Researcher** | Part 2 (ITPRA) | → All in order |
| **Curious Reader** | Part 2 (Expectation) | → Part 3 (BRECVEMA) → Part 7 (Storyteller) |

---

## The 6-Dimensional Emotional Space

The system tracks emotional state across six validated dimensions:

| Dimension | Range | What It Represents | Musical Markers |
|-----------|-------|-------------------|-----------------|
| **Tension** | 0→1 | Resolution to climax | Dissonance, harmonic rhythm, instability |
| **Brightness** | 0→1 | Dark to bright | Major/minor mode, register, timbre |
| **Energy** | 0→1 | Calm to energetic | Tempo, rhythmic density, dynamics |
| **Warmth** | 0→1 | Distant to intimate | Consonance, close voicing, legato |
| **Stability** | 0→1 | Unstable to grounded | Tonic presence, metric regularity |
| **Valence** | 0→1 | Negative to positive | Overall emotional character |

Presets like `EmotionalState.Presets.Climax` (tension=0.95, stability=0.1) define target states for narrative phases.

---

## Academic Sources

### Primary References

| Theory | Author | Year | Title |
|--------|--------|------|-------|
| TPS | Lerdahl, F. | 2001 | *Tonal Pitch Space*. Oxford University Press |
| ITPRA | Huron, D. | 2006 | *Sweet Anticipation: Music and the Psychology of Expectation*. MIT Press |
| BRECVEMA | Juslin, P.N. | 2013 | "From everyday emotions to aesthetic emotions" in *Physics of Life Reviews* |
| IDyOM | Pearce, M.T. | 2005 | *The Construction and Evaluation of Statistical Models of Melodic Structure*. PhD, City University London |

### Key Papers

| Topic | Authors | Year | Finding |
|-------|---------|------|---------|
| Pleasure Model | Cheung et al. | 2019 | Pleasure = f(Entropy × Surprise interaction) |
| Tension Formula | Navarro-Cáceres et al. | 2020 | T = 0.402D + 0.246H + 0.202T + 0.193VL |
| Rhythmic Models | PLOS Comp Bio | 2025 | PIPPET bridges entrainment and probabilistic paradigms |

### Additional Reading

- Krumhansl, C.L. (1990). *Cognitive Foundations of Musical Pitch*
- Meyer, L.B. (1956). *Emotion and Meaning in Music*
- Temperley, D. (2007). *Music and Probability*
- Narmour, E. (1990). *The Analysis and Cognition of Basic Melodic Structures*

---

## Implementation

The system is implemented as two NuGet packages:

```bash
# Core music theory primitives
dotnet add package BeyondImmersion.Bannou.MusicTheory

# Narrative composition (includes MusicTheory)
dotnet add package BeyondImmersion.Bannou.MusicStoryteller
```

### Key Files

| Component | File | Purpose |
|-----------|------|---------|
| TPS Hierarchy | `Theory/BasicSpace.cs` | 5-level pitch stability |
| ITPRA | `Theory/ContrastiveValence.cs` | Expectation response pipeline |
| BRECVEMA | `State/MechanismState.cs` | 8-mechanism tracking |
| Tension | `Theory/TensionCalculator.cs` | Navarro formula |
| Information | `Theory/InformationContent.cs` | IC and entropy |
| Planning | `Planning/GOAPPlanner.cs` | A* search for actions |
| Orchestrator | `Storyteller.cs` | Main entry point |

---

## What Makes This Novel?

While individual components (TPS calculations, GOAP planning, emotional models) exist in various implementations, this system is unique in:

1. **Full ITPRA implementation** - No complete computational ITPRA exists; this is novel work
2. **Unified framework** - Combines BRECVEMA umbrella with TPS quantification and ITPRA psychology
3. **Narrative-first design** - Plans emotional journeys, not just valid note sequences
4. **Continuous state tracking** - 8 sub-states updated every musical event
5. **Source-verified parameters** - All formulas traced to peer-reviewed research

---

*This series is part of the [Bannou](https://github.com/BeyondImmersion/bannou) project. Questions? Open a discussion!*
