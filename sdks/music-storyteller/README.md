# BeyondImmersion.Bannou.MusicStoryteller

Compositional intelligence layer for music generation. Transforms "notes wandering around" into "music with purpose".

## Overview

The Music Storyteller SDK sits atop the MusicTheory SDK (the "reader/tools") and provides the "writer" intelligence - the decision-making layer that determines *what* to compose and *why*.

### Research Foundation

Built on peer-reviewed music cognition research:

- **Huron's ITPRA Theory** - Imagination, Tension, Prediction, Reaction, Appraisal responses to musical events
- **Lerdahl's Tonal Pitch Space (TPS)** - Mathematical framework for tonal tension and resolution
- **BRECVEMA** - Eight mechanisms of emotional induction through music

## Architecture

```
Storyteller (Orchestrator)
    │
    ├── Narratives     → Story arc templates (Journey and Return, etc.)
    │
    ├── Planning       → GOAP-based action planning
    │   └── Actions    → Musical actions with effects (tension, resolution, etc.)
    │
    ├── State          → Composition state tracking
    │   ├── Emotional  → 6-dimension emotional space
    │   ├── Listener   → ITPRA expectation model
    │   ├── Harmonic   → Current harmonic context
    │   └── Thematic   → Motif memory and usage
    │
    ├── Theory         → Lerdahl TPS calculations
    │   ├── BasicSpace → 5-level pitch hierarchy
    │   ├── ChordDistance → δ(x→y) = i + j + k
    │   └── TensionCalculator → TIS formula
    │
    └── Intent         → Bridge to MusicTheory SDK
        └── CompositionIntent → What theory engine receives
```

## Key Concepts

### Emotional State (6 dimensions)

| Dimension | Range | Description |
|-----------|-------|-------------|
| Tension | 0-1 | Resolution to climax |
| Brightness | 0-1 | Dark to bright |
| Energy | 0-1 | Calm to energetic |
| Warmth | 0-1 | Distant to intimate |
| Stability | 0-1 | Unstable to grounded |
| Valence | 0-1 | Negative to positive |

### Narrative Templates

Pre-defined story arcs with emotional targets:

- **Journey and Return** - Celtic classic (Home → Departure → Adventure → Return)
- **Tension and Release** - Universal (Stability → Building → Climax → Release)
- **Simple Arc** - Basic intro-body-conclusion

### GOAP Planning

Goal-Oriented Action Planning finds sequences of musical actions to reach emotional targets:

```csharp
// Current state: tension=0.2, stability=0.8
// Goal: tension=0.8, stability=0.3

// GOAP might plan:
// 1. use_secondary_dominant (+0.2 tension, -0.2 stability)
// 2. ascending_sequence (+0.1 tension, +0.1 energy)
// 3. chromatic_voice_leading (+0.1 tension)
// 4. ...until goal reached
```

## Usage

```csharp
using BeyondImmersion.Bannou.MusicStoryteller;

var storyteller = new Storyteller();

var result = storyteller.Compose(new CompositionRequest
{
    DurationBars = 32,
    Style = "celtic",
    Mood = "adventurous",
    NarrativeHint = "journey"
});

// result.Sections contains CompositionIntents for MusicTheory SDK
// result.Narrative describes the chosen story arc
// result.FinalState shows where we ended emotionally
```

## Dependencies

- `BeyondImmersion.Bannou.MusicTheory` - Music primitives and generation

## License

MIT
