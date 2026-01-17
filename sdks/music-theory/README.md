# BeyondImmersion.Bannou.MusicTheory

Pure computation music theory library for deterministic MIDI generation.

## Overview

This library provides formal music theory primitives for generating music programmatically without AI/neural networks. It implements music theory rules as code, enabling:

- **Pitch operations**: Pitch classes, intervals, transposition
- **Collections**: Scales, modes, chords, voicings
- **Time**: Durations, meters, rhythmic patterns
- **Harmony**: Harmonic functions, progressions, voice leading
- **Melody**: Contour-based generation, motif development
- **Structure**: Form templates, phrase construction
- **Style**: Genre-specific configurations (Celtic, Jazz, Baroque, etc.)

## Usage

```csharp
using BeyondImmersion.Bannou.MusicTheory;

// Create a key
var key = new KeySignature(PitchClass.D, Mode.Dorian);

// Generate a chord progression
var generator = new ProgressionGenerator();
var chords = generator.Generate(key, length: 8);

// Generate a melody over the chords
var melodyGen = new MelodyGenerator();
var melody = melodyGen.Generate(chords, style: "celtic");

// Output as MIDI-JSON
var midiJson = MidiJsonRenderer.Render(melody, chords);
```

## Modules

| Module | Description |
|--------|-------------|
| `Pitch` | PitchClass, Pitch, Interval fundamentals |
| `Collections` | Scale, Mode, Chord, Voicing types |
| `Time` | Duration, Meter, Rhythm patterns |
| `Harmony` | HarmonicFunction, Progression, VoiceLeading |
| `Melody` | Contour, MelodyGenerator, Motif |
| `Structure` | Form, Phrase construction |
| `Style` | StyleDefinition, StyleLoader |
| `Output` | MidiJson rendering |

## License

MIT
