# Bannou Music System

This document provides a comprehensive overview of Bannou's procedural music generation system, including the theoretical foundations, SDK architecture, and integration patterns.

## Overview

The Bannou music system consists of two SDKs that work together to generate purposeful, emotionally-driven music:

| Package | Purpose | Use Case |
|---------|---------|----------|
| `BeyondImmersion.Bannou.MusicTheory` | Core music theory primitives | Building blocks: pitches, chords, scales, rhythm |
| `BeyondImmersion.Bannou.MusicStoryteller` | Narrative-driven composition | High-level: emotional arcs, storytelling, intent generation |

The system is designed around a key insight: **music is a narrative medium**. Rather than generating random notes that follow rules, the Storyteller SDK plans emotional journeys and uses GOAP (Goal-Oriented Action Planning) to select musical actions that achieve narrative goals.

## Theoretical Foundations

The system synthesizes several influential music cognition theories:

### Tonal Pitch Space (Lerdahl)

Fred Lerdahl's *Tonal Pitch Space* (2001) provides the mathematical framework for harmonic relationships:

**Basic Space Hierarchy**: A 5-level stability hierarchy from most to least stable:
1. **Root** - Tonic pitch only (e.g., C in C major)
2. **Fifth** - Root + fifth (C, G)
3. **Triad** - Root + third + fifth (C, E, G)
4. **Diatonic** - All scale degrees (C, D, E, F, G, A, B)
5. **Chromatic** - All 12 pitch classes

**Chord Distance**: The sum of:
- Regional distance (key relationship)
- Chordal distance (chord-root movement)
- Pitch class distance (number of non-common tones)

**Melodic Attraction** (α): Predicts resolution tendency:
```
α = (s₂/s₁) × (1/n²)
```
Where:
- s₁ = stability of source pitch
- s₂ = stability of target pitch
- n = semitone distance

Higher attraction values indicate stronger resolution expectation (e.g., leading tone → tonic).

### ITPRA Model (Huron)

David Huron's *Sweet Anticipation* (2006) describes five response stages to musical events:

| Stage | Timing | Description | Musical Application |
|-------|--------|-------------|---------------------|
| **Imagination** | Before | Mental rehearsal of possible continuations | Listener builds expectations |
| **Tension** | Before | Pre-outcome arousal | Dominant prolongation |
| **Prediction** | At event | Accuracy of expectation | Surprise/confirmation |
| **Reaction** | At event | Immediate reflexive response | Dynamic accents, sforzando |
| **Appraisal** | After | Conscious evaluation | "That was beautiful" |

The SDK tracks listener state across these stages to manage surprise/confirmation balance.

### BRECVEMA Mechanisms (Juslin)

Patrik Juslin's *Musical Emotions Explained* (2019) identifies eight parallel emotion-induction mechanisms:

| Mechanism | Description | SDK Implementation |
|-----------|-------------|-------------------|
| **Brainstem Reflex** | Sudden acoustic changes | Dynamic accents, sforzando |
| **Rhythmic Entrainment** | Body synchronization | Tempo, metric regularity |
| **Evaluative Conditioning** | Learned associations | Style/genre recognition |
| **Contagion** | Perceived emotion in music | Expressive parameters |
| **Visual Imagery** | Mental images evoked | Programmatic associations |
| **Episodic Memory** | Personal memories | N/A (listener-specific) |
| **Musical Expectancy** | Expectation violation | ITPRA integration |
| **Aesthetic Judgment** | Beauty/craft appreciation | Structural coherence |

### Information Theory (Meyer, Pearce)

Leonard Meyer's *Emotion and Meaning in Music* (1956) and Marcus Pearce's IDyOT model inform expectation calculation:

- **Information Content**: -log₂(P(event)) - Lower probability = higher information
- **Entropy**: Uncertainty about what comes next
- **Surprise Budget**: Managing listener's expectation violation tolerance

---

## Architecture

### Two-Layer Design

```
┌──────────────────────────────────────────────────────────────┐
│                    MusicStoryteller SDK                       │
│  ┌─────────────┐  ┌───────────────┐  ┌──────────────────┐   │
│  │ Storyteller │──│ GOAP Planner  │──│ Intent Generator │   │
│  │ Orchestrator│  │ (Action Plans)│  │ (→ Constraints)  │   │
│  └─────────────┘  └───────────────┘  └──────────────────┘   │
│         │                                      │              │
│         ▼                                      ▼              │
│  ┌─────────────┐                    ┌──────────────────┐    │
│  │ Narrative   │                    │ Composition      │    │
│  │ Templates   │                    │ Intent           │    │
│  └─────────────┘                    └──────────────────┘    │
└──────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────┐
│                     MusicTheory SDK                           │
│  ┌──────────┐  ┌───────────┐  ┌────────────┐  ┌──────────┐  │
│  │ Pitch/   │  │ Harmony/  │  │ Melody/    │  │ Style/   │  │
│  │ Interval │  │ Voicing   │  │ Contour    │  │ Rhythm   │  │
│  └──────────┘  └───────────┘  └────────────┘  └──────────┘  │
│                              │                               │
│                              ▼                               │
│                    ┌──────────────────┐                     │
│                    │    MIDI-JSON     │                     │
│                    │    Output        │                     │
│                    └──────────────────┘                     │
└──────────────────────────────────────────────────────────────┘
```

### Data Flow

1. **Request** → Storyteller receives composition request with duration, style, and optional emotional targets
2. **Narrative Selection** → Selects appropriate narrative template (simple arc, tension-release, journey-return)
3. **Phase Planning** → Breaks composition into phases with emotional targets
4. **GOAP Planning** → For each phase, plans sequence of musical actions to reach target state
5. **Intent Generation** → Converts actions to CompositionIntent objects
6. **Theory Generation** → MusicTheory SDK generates actual notes from intents
7. **MIDI-JSON Output** → Final output in portable JSON format

---

## MusicTheory SDK

**Package**: `BeyondImmersion.Bannou.MusicTheory`

Core music theory primitives and generation algorithms.

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.MusicTheory
```

### Key Components

#### Pitch Primitives

```csharp
using BeyondImmersion.Bannou.MusicTheory.Pitch;

// Pitch class (0-11)
var c = PitchClass.C;      // 0
var fSharp = PitchClass.FSharp;  // 6

// Concrete pitch (with octave)
var middleC = new Pitch(PitchClass.C, 4);  // C4, MIDI 60
var midiNote = middleC.MidiNumber;  // 60

// Intervals
var fifth = Interval.PerfectFifth;      // 7 semitones
var target = middleC.Transpose(fifth);  // G4
```

#### Scales and Modes

```csharp
using BeyondImmersion.Bannou.MusicTheory.Collections;

// Create a scale
var cMajor = new Scale(PitchClass.C, ModeType.Major);
var aDorian = new Scale(PitchClass.A, ModeType.Dorian);

// Get scale degrees
var pitches = cMajor.GetPitches(octave: 4);  // C4, D4, E4, F4, G4, A4, B4

// Check membership
var inScale = cMajor.Contains(PitchClass.E);  // true
```

#### Chords and Voicings

```csharp
using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;

// Chord from roman numeral
var chord = Chord.Parse("V7", cMajor);  // G7 in C major

// Voice leading
var source = new Voicing([new Pitch(PitchClass.C, 4), ...]);
var target = VoiceLeading.OptimalVoicing(chord, source);
```

#### Style Definitions

```csharp
using BeyondImmersion.Bannou.MusicTheory.Style;

// Use built-in style
var celtic = BuiltInStyles.Celtic;

// Mode distribution
var mode = celtic.SelectMode(random);  // 64% Major, 16% Minor, 14% Dorian, 6% Mixolydian

// Tune types
var reel = celtic.GetTuneType("reel");
var meter = reel.Meter;  // 4/4
var tempoRange = reel.TempoRange;  // (100, 140)

// Load custom style from YAML
var style = StyleLoader.Load("path/to/style.yaml");
```

#### MIDI-JSON Output

```csharp
using BeyondImmersion.Bannou.MusicTheory.Output;

// Render melody to MIDI-JSON
var midiJson = MidiJsonRenderer.RenderComposition(
    melody: melodyNotes,
    chords: voicings,
    tempo: 120,
    meter: new Meter(4, 4),
    key: cMajor,
    name: "My Composition"
);

// Serialize
var json = midiJson.ToJson(indented: true);

// Combine multiple tracks
var combined = MidiJsonRenderer.Combine(melodyTrack, chordTrack, bassTrack);
```

### MIDI-JSON Format

The SDK outputs compositions in MIDI-JSON format, a portable JSON representation of MIDI data:

```json
{
  "ticksPerBeat": 480,
  "header": {
    "format": 1,
    "name": "Celtic Reel",
    "tempos": [{ "tick": 0, "bpm": 120 }],
    "timeSignatures": [{ "tick": 0, "numerator": 4, "denominator": 4 }],
    "keySignatures": [{ "tick": 0, "tonic": "G", "mode": "Major" }]
  },
  "tracks": [
    {
      "name": "Melody",
      "channel": 0,
      "instrument": 73,
      "events": [
        { "tick": 0, "type": "NoteOn", "note": 67, "velocity": 80, "duration": 240 },
        { "tick": 240, "type": "NoteOn", "note": 69, "velocity": 75, "duration": 240 }
      ]
    }
  ]
}
```

---

## MusicStoryteller SDK

**Package**: `BeyondImmersion.Bannou.MusicStoryteller`

High-level narrative composition using emotional states and GOAP planning.

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.MusicStoryteller
```

### Key Components

#### Emotional State

A 6-dimensional emotional space validated by music psychology research:

```csharp
using BeyondImmersion.Bannou.MusicStoryteller.State;

var state = new EmotionalState
{
    Tension = 0.3,      // 0 = resolved, 1 = climax
    Brightness = 0.7,   // 0 = dark, 1 = bright
    Energy = 0.5,       // 0 = calm, 1 = energetic
    Warmth = 0.6,       // 0 = distant, 1 = intimate
    Stability = 0.8,    // 0 = unstable, 1 = grounded
    Valence = 0.6       // 0 = negative, 1 = positive
};

// Presets
var peaceful = EmotionalState.Presets.Peaceful;
var climax = EmotionalState.Presets.Climax;
var melancholic = EmotionalState.Presets.Melancholic;

// Interpolation
var transition = current.InterpolateTo(target, t: 0.5);

// Distance (for planning)
var distance = current.NormalizedDistanceTo(target);
```

#### Narrative Templates

Pre-built emotional journey structures:

```csharp
using BeyondImmersion.Bannou.MusicStoryteller.Narratives;

// Available templates
var templates = new[]
{
    "simple_arc",           // Intro → Build → Climax → Resolution
    "tension_and_release",  // Build tension, release, repeat
    "journey_and_return"    // Depart home → Adventure → Return home
};

// Get template
var narrative = NarrativeSelector.GetById("simple_arc");

// Template phases
foreach (var phase in narrative.Phases)
{
    Console.WriteLine($"{phase.Name}: Tension={phase.EmotionalTarget.Tension}");
}
```

#### Storyteller Orchestrator

The main entry point for composition:

```csharp
using BeyondImmersion.Bannou.MusicStoryteller;

var storyteller = new Storyteller();

// Compose with automatic narrative selection
var request = new CompositionRequest
{
    TotalBars = 32,
    StyleId = "celtic",
    InitialEmotion = EmotionalState.Neutral
};

var result = storyteller.Compose(request);

// Or compose with specific template
var result = storyteller.ComposeWithTemplate("tension_and_release", totalBars: 64);

// Access results
foreach (var section in result.Sections)
{
    Console.WriteLine($"{section.PhaseName}: Bars {section.StartBar}-{section.EndBar}");

    foreach (var intent in section.Intents)
    {
        // Intent contains emotional targets, harmonic character,
        // melodic contour, thematic instructions, etc.
    }
}
```

#### Musical Actions

GOAP actions that modify composition state:

```csharp
using BeyondImmersion.Bannou.MusicStoryteller.Actions;

// Tension-building actions
TensionActions.UseDominantSeventh      // V7 chord
TensionActions.UseSecondaryDominant    // V/x tonicization
TensionActions.IncreaseHarmonicRhythm  // More chord changes
TensionActions.AscendingSequence       // Upward melodic motion
TensionActions.DelayResolution         // Prolong dominant

// Resolution actions
ResolutionActions.AuthenticCadence     // V-I
ResolutionActions.PlagalCadence        // IV-I
ResolutionActions.DescendingContour    // Downward melodic motion
ResolutionActions.ReduceHarmonicRhythm // Slower chord changes

// Thematic actions
ThematicActions.IntroduceMotif         // Present main theme
ThematicActions.DevelopMotif           // Transform theme
ThematicActions.RecapMotif             // Return to theme
```

#### Composition Intent

The bridge between storytelling and theory generation:

```csharp
using BeyondImmersion.Bannou.MusicStoryteller.Intent;

// Intent generated by Storyteller
var intent = new CompositionIntent
{
    EmotionalTarget = EmotionalState.Presets.Climax,
    Bars = 4,
    HarmonicCharacter = HarmonicCharacter.Climactic,
    MusicalCharacter = new MusicalCharacter
    {
        RegisterHeight = 0.8,    // High register
        RhythmicActivity = 0.9,  // Active rhythm
        TexturalDensity = 0.7    // Full texture
    },
    Harmony = new HarmonicIntent
    {
        AvoidTonic = true,
        EmphasizeDominant = true,
        TargetTensionLevel = 0.9
    },
    Melody = new MelodicIntent
    {
        Contour = MelodicContour.Ascending,
        Range = MelodicRange.High,
        AllowSyncopation = true
    },
    DynamicLevel = 0.9  // fortissimo
};
```

#### Theory Integration

The Storyteller uses Lerdahl's Tonal Pitch Space for harmonic calculations:

```csharp
using BeyondImmersion.Bannou.MusicStoryteller.Theory;

// Basic space for stability calculations
var basicSpace = BasicSpace.FromScale(scale);
var stability = basicSpace.GetStabilityLevel(pitchClass);

// Chord distance
var distance = ChordDistance.Calculate(chordA, chordB, basicSpace);

// Melodic attraction (leading tone → tonic = high attraction)
var attraction = MelodicAttraction.Calculate(sourcePitch, targetPitch, basicSpace);

// Tension calculation
var tension = TensionCalculator.Calculate(chord, basicSpace);
```

---

## Service Integration

The music system is exposed via the Music service in Bannou:

### Generate Composition

```csharp
// Via WebSocket client
var response = await client.Music.GenerateAsync(new MusicGenerateRequest
{
    StyleId = "celtic",
    TotalBars = 32,
    TemplateId = "simple_arc"
});

var midiJson = response.MidiJson;
```

### Generate Melody Over Harmony

```csharp
var response = await client.Music.MelodyAsync(new MusicMelodyRequest
{
    Key = "G",
    Mode = "Major",
    ChordProgression = ["I", "IV", "V", "I"],
    Bars = 8
});
```

### Voice Leading

```csharp
var response = await client.Music.VoiceLeadAsync(new MusicVoiceLeadRequest
{
    ChordSymbols = ["Cmaj7", "Dm7", "G7", "Cmaj7"],
    VoiceCount = 4
});
```

---

## Style Configuration

### YAML Style Definition

Custom styles can be defined in YAML:

```yaml
id: "my-style"
name: "My Custom Style"
category: "folk"
description: "A custom folk style"

modeDistribution:
  major: 0.5
  minor: 0.3
  dorian: 0.2

intervalPreferences:
  stepProbability: 0.6
  skipProbability: 0.3
  leapProbability: 0.1

defaultTempo: 100
defaultMeter:
  numerator: 4
  denominator: 4

formTemplates:
  - sections: "AABB"

tuneTypes:
  - name: "waltz"
    meter: { numerator: 3, denominator: 4 }
    tempoRange: [80, 110]
    defaultForm: "AABB"

harmonyStyle:
  primaryCadence: "AuthenticPerfect"
  dominantPrepProbability: 0.5
  secondaryDominantProbability: 0.2
  commonProgressions:
    - "I-IV-V-I"
    - "I-vi-IV-V"
```

### Loading Custom Styles

```csharp
var style = StyleLoader.Load("path/to/my-style.yaml");

// Or register with the service
await client.Music.StyleCreateAsync(new MusicStyleCreateRequest
{
    StyleYaml = File.ReadAllText("my-style.yaml")
});
```

---

## Decision Guide

```
Are you building a music-aware game?
├─ Yes, need full emotional storytelling
│  └─ Use MusicStoryteller SDK
│     - Narrative templates
│     - GOAP planning
│     - Emotional state tracking
│
├─ Yes, but just need theory primitives
│  └─ Use MusicTheory SDK only
│     - Pitch/interval calculations
│     - Chord/scale operations
│     - Voice leading
│     - MIDI-JSON output
│
└─ No, just consuming generated music
   └─ Use Music service API
      - /music/generate endpoint
      - Returns MIDI-JSON
```

---

## References

### Academic Sources

- Lerdahl, F. (2001). *Tonal Pitch Space*. Oxford University Press.
- Huron, D. (2006). *Sweet Anticipation: Music and the Psychology of Expectation*. MIT Press.
- Juslin, P. N. (2019). *Musical Emotions Explained*. Oxford University Press.
- Meyer, L. B. (1956). *Emotion and Meaning in Music*. University of Chicago Press.
- Pearce, M. T. (2005). *The Construction and Evaluation of Statistical Models of Melodic Structure*. PhD Thesis, City University London.
- Narmour, E. (1990). *The Analysis and Cognition of Basic Melodic Structures*. University of Chicago Press.
- Temperley, D. (2007). *Music and Probability*. MIT Press.

### Further Reading

- [MusicTheory SDK README](../../sdks/music-theory/README.md)
- [MusicStoryteller SDK README](../../sdks/music-storyteller/README.md)
- [MIDI-JSON Specification](https://github.com/Tonejs/Midi)
- [Music Service API](../GENERATED-SERVICE-DETAILS.md#music)
