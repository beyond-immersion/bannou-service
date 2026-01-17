# Music System Implementation Roadmap

> **Status**: Implementation Planning
> **Decisions Made**: 2025-01-17
> **Parent Documents**: MUSIC-THEORY-FOUNDATION.md, HIERARCHICAL-MUSIC-BEHAVIORS.md

This document captures the design decisions and lays out a concrete implementation plan.

---

## Confirmed Design Decisions

| Decision | Choice | Notes |
|----------|--------|-------|
| Tuning System | 12-TET for MVP | Design interval system for future extensibility (ratios, not just semitones) |
| Style Model | Inheritance | `bebop extends jazz`, override specific rules |
| Notation vs Performance | Two layers | Performance layer is optional/suggestive, can be discarded |
| Real-Time Scope | Pre-compiled | Theory engine generates offline/async; game audio selects/plays pre-made tracks |
| Validation | Human curation + heuristics | Iterative refinement, style-by-style, with analysis tools |
| Song Structure | Yes, without lyrics | Form is narrative structure in music, independent of words |
| API Surface | Both high + low level | Granular control available, high-level convenience too |
| Execution | Server-side only | Theory engine runs on server |

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              GAME RUNTIME                                    │
│                                                                              │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                    AUDIO ORCHESTRATION BEHAVIORS                       │ │
│  │  (ABML behaviors for real-time audio management)                       │ │
│  │                                                                         │ │
│  │  - Select from pre-composed tracks based on game state                 │ │
│  │  - Trigger transitions, crossfades, stingers                           │ │
│  │  - Request async composition ("I need a battle theme for this boss")   │ │
│  │  - Receive notification when async composition is ready                │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                    ↓ request                  ↑ notify      │
└────────────────────────────────────┼──────────────────────────┼─────────────┘
                                     │                          │
┌────────────────────────────────────┼──────────────────────────┼─────────────┐
│                           COMPOSITION SERVICE                  │             │
│                                    ↓                          │             │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                    COMPOSITION BEHAVIORS                               │ │
│  │  (ABML behaviors that orchestrate the theory engine)                   │ │
│  │                                                                         │ │
│  │  - Interpret composition requests                                       │ │
│  │  - Select form, style, instrumentation                                  │ │
│  │  - Call theory engine for each section/phrase/gesture                   │ │
│  │  - Assemble final piece                                                 │ │
│  │  - Store result, notify requester                                       │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                    ↓                                        │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                      MUSIC THEORY ENGINE                               │ │
│  │  (Pure computation library - no side effects)                          │ │
│  │                                                                         │ │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌─────────────┐       │ │
│  │  │ Pitch/Scale │ │  Harmony    │ │   Melody    │ │   Rhythm    │       │ │
│  │  │   Module    │ │   Module    │ │   Module    │ │   Module    │       │ │
│  │  └─────────────┘ └─────────────┘ └─────────────┘ └─────────────┘       │ │
│  │                                                                         │ │
│  │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐                       │ │
│  │  │   Form/     │ │   Voice     │ │ Performance │                       │ │
│  │  │  Structure  │ │  Leading    │ │   Layer     │                       │ │
│  │  └─────────────┘ └─────────────┘ └─────────────┘                       │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                    ↓                                        │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                       STYLE DATABASE                                   │ │
│  │  (lib-state: Redis/MySQL backed)                                       │ │
│  │                                                                         │ │
│  │  - Style definitions (jazz, celtic, baroque, etc.)                      │ │
│  │  - Gesture libraries (rising_hopeful, falling_sad, etc.)                │ │
│  │  - Phrase patterns                                                      │ │
│  │  - Progression templates                                                │ │
│  │  - Instrumentation presets                                              │ │
│  │  - Curated/analyzed examples                                            │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                    ↓                                        │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │                        OUTPUT LAYER                                    │ │
│  │                                                                         │ │
│  │  - MIDI-JSON generation                                                 │ │
│  │  - Standard MIDI export                                                 │ │
│  │  - Sample library mapping (SFZ/VST references)                          │ │
│  │  - DAW project generation (future)                                      │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                                     ↓
                         ┌───────────────────────┐
                         │    RENDERED AUDIO     │
                         │  (Offline rendering   │
                         │   to audio files OR   │
                         │   MIDI-JSON for       │
                         │   client playback)    │
                         └───────────────────────┘
```

---

## Component Breakdown

### Component 1: Music Theory Engine (Pure Library)

**Purpose**: All music theory knowledge as pure, side-effect-free functions.

**Location**: `bannou-service/Music/Theory/` or new `lib-music-theory/`

**Submodules**:

```
lib-music-theory/
├── Pitch/
│   ├── PitchClass.cs         # C, C#, D, etc.
│   ├── Pitch.cs              # PitchClass + Octave
│   ├── Interval.cs           # Distance between pitches
│   ├── IntervalQuality.cs    # Major, minor, perfect, augmented, diminished
│   └── TuningSystem.cs       # 12-TET (extensible later)
│
├── Collections/
│   ├── Scale.cs              # Ordered pitch collection with formula
│   ├── ScaleDatabase.cs      # Major, minor, modes, pentatonic, blues, etc.
│   ├── Chord.cs              # Vertical pitch structure
│   ├── ChordQuality.cs       # Major, minor, diminished, augmented, 7ths, etc.
│   ├── ChordVoicing.cs       # How chord tones are arranged
│   └── VoicingStrategies.cs  # Close, open, drop-2, shell, quartal
│
├── Time/
│   ├── Duration.cs           # Note length relative to beat
│   ├── Meter.cs              # Time signature, beat grouping
│   ├── Tempo.cs              # BPM
│   ├── Rhythm.cs             # Pattern of durations
│   └── Subdivision.cs        # Straight, swing, shuffle
│
├── Harmony/
│   ├── HarmonicFunction.cs   # Tonic, subdominant, dominant
│   ├── Progression.cs        # Sequence of chords
│   ├── ProgressionGenerator.cs # Generate progressions given constraints
│   ├── Cadence.cs            # Authentic, half, plagal, deceptive
│   └── VoiceLeading.cs       # Rules for voice movement
│
├── Melody/
│   ├── Contour.cs            # Shape of pitch movement
│   ├── MelodicMotion.cs      # Step, skip, leap
│   ├── Phrase.cs             # Complete musical thought
│   ├── Motif.cs              # Smallest recognizable idea
│   ├── MotifTransformations.cs # Inversion, retrograde, sequence, etc.
│   └── MelodyGenerator.cs    # Generate melody given constraints
│
├── Structure/
│   ├── SectionRole.cs        # Exposition, development, climax, resolution
│   ├── Form.cs               # Large-scale organization
│   ├── FormDatabase.cs       # Binary, ternary, rondo, sonata, verse-chorus, etc.
│   └── FormGenerator.cs      # Select/customize form for a piece
│
├── Performance/
│   ├── Articulation.cs       # Legato, staccato, accent, etc.
│   ├── Dynamics.cs           # Velocity, crescendo, diminuendo
│   ├── Timing.cs             # Humanization, rubato, laid-back
│   └── PerformanceLayer.cs   # Apply performance suggestions to notation
│
├── Style/
│   ├── StyleDefinition.cs    # Complete style configuration
│   ├── StyleInheritance.cs   # Extends mechanism
│   ├── StyleMerger.cs        # Blend multiple styles
│   └── Styles/
│       ├── Jazz.cs
│       ├── Celtic.cs
│       ├── Baroque.cs
│       ├── Blues.cs
│       └── ... (many more)
│
└── Output/
    ├── MidiJson.cs           # MIDI-JSON format
    ├── MidiJsonRenderer.cs   # Render composition to MIDI-JSON
    ├── StandardMidi.cs       # Standard MIDI file generation
    └── InstrumentMapping.cs  # Map abstract instruments to MIDI programs/samples
```

**Key Design Principle**: Every function in this library is **pure** - given the same inputs, it always produces the same outputs, with no side effects. This makes testing trivial.

```csharp
// Example: Generate a chord progression
public static Progression GenerateProgression(
    Scale scale,
    StyleDefinition style,
    int measures,
    CadenceType endingCadence,
    ProgressionConstraints? constraints = null)
{
    // Pure computation - no I/O, no state
}

// Example: Generate a melody over a progression
public static Melody GenerateMelody(
    Progression harmony,
    StyleDefinition style,
    Contour preferredContour,
    MelodicConstraints? constraints = null)
{
    // Pure computation
}
```

---

### Component 2: Style Database (lib-state backed)

**Purpose**: Store style definitions, gesture libraries, learned patterns.

**Schema Approach**: Use our existing state store infrastructure.

```yaml
# State store definitions for music
music-styles:
  backend: mysql  # Complex queries, relational
  key_type: string  # style_id
  value_type: StyleDefinition

music-gestures:
  backend: mysql
  key_type: string  # gesture_id
  value_type: GestureDefinition
  indexes:
    - style
    - emotional_character
    - duration_range

music-phrases:
  backend: mysql
  key_type: string
  value_type: PhraseDefinition
  indexes:
    - style
    - section_role
    - length_bars

music-progressions:
  backend: mysql
  key_type: string
  value_type: ProgressionTemplate
  indexes:
    - style
    - length
    - ending_cadence

music-compositions:
  backend: mysql  # Store generated pieces
  key_type: guid
  value_type: CompositionRecord
  indexes:
    - style
    - form
    - created_date
    - tags
```

**Analysis Data** (for the "training" process you described):

```yaml
# Store analysis of existing music
music-analysis:
  backend: mysql
  key_type: guid
  value_type: AnalysisRecord
  schema:
    source_reference: string  # Where the music came from (royalty-free source)
    style_tags: string[]
    analyzed_progression: Progression
    analyzed_form: Form
    analyzed_phrases: Phrase[]
    tempo: number
    key: string
    meter: string
    notes: string  # Human notes from curation
```

---

### Component 3: Composition Behaviors (ABML)

**Purpose**: High-level orchestration of the theory engine.

These are ABML behaviors that:
1. Receive composition requests
2. Make creative decisions (form, style, instrumentation)
3. Call into the theory engine for each component
4. Assemble the result
5. Store and notify

```yaml
# behaviors/composition/compose-piece.abml.yaml
version: "3.0"
metadata:
  id: compose_piece
  type: composition_behavior
  description: "Main entry point for piece composition"

context:
  variables:
    composition_request:
      type: CompositionRequest
    style:
      type: StyleDefinition
    form:
      type: Form
    sections:
      type: Section[]
    final_piece:
      type: Composition

flows:
  receive_request:
    trigger: composition.request
    actions:
      # Step 1: Load style configuration
      - service_call:
          service: state
          method: get
          parameters:
            store: music-styles
            key: "${request.style_id}"
          result_variable: style

      # Step 2: Select or generate form
      - invoke_behavior:
          behavior: select_form
          parameters:
            style: "${style}"
            duration_target: "${request.duration}"
            mood: "${request.mood}"
          result_variable: form

      # Step 3: Generate each section
      - foreach:
          items: "${form.sections}"
          as: section_template
          do:
            - invoke_behavior:
                behavior: compose_section
                parameters:
                  section_template: "${section_template}"
                  style: "${style}"
                  previous_sections: "${sections}"
                result_variable: composed_section

            - list_append:
                list: sections
                item: "${composed_section}"

      # Step 4: Apply performance layer (optional)
      - cond:
          - when: "${request.include_performance}"
            then:
              - invoke_behavior:
                  behavior: apply_performance
                  parameters:
                    sections: "${sections}"
                    style: "${style}"
                  result_variable: sections

      # Step 5: Render to MIDI-JSON
      - service_call:
          service: music
          method: render
          parameters:
            sections: "${sections}"
            format: midi_json
          result_variable: midi_json

      # Step 6: Store the composition
      - service_call:
          service: state
          method: save
          parameters:
            store: music-compositions
            key: "${request.composition_id}"
            value:
              request: "${request}"
              style_id: "${style.id}"
              form: "${form}"
              midi_json: "${midi_json}"
              created: "${now()}"

      # Step 7: Notify requester
      - publish:
          topic: composition.completed
          payload:
            request_id: "${request.request_id}"
            composition_id: "${request.composition_id}"
```

```yaml
# behaviors/composition/compose-section.abml.yaml
version: "3.0"
metadata:
  id: compose_section
  type: composition_behavior
  description: "Compose a single section of a piece"

context:
  variables:
    harmony:
      type: Progression
    melody:
      type: Melody
    accompaniment:
      type: Voice[]

flows:
  compose:
    actions:
      # Step 1: Generate chord progression for this section
      - service_call:
          service: music
          method: generate_progression
          parameters:
            scale: "${style.preferred_scale_for(section_template.role)}"
            style: "${style}"
            measures: "${section_template.length_bars}"
            ending_cadence: "${section_template.cadence}"
            constraints:
              # Use material from previous sections if appropriate
              thematic_connection: "${previous_sections}"
          result_variable: harmony

      # Step 2: Generate melody
      - service_call:
          service: music
          method: generate_melody
          parameters:
            harmony: "${harmony}"
            style: "${style}"
            role: "${section_template.role}"
            contour: "${section_template.preferred_contour}"
          result_variable: melody

      # Step 3: Generate accompaniment pattern
      - service_call:
          service: music
          method: generate_accompaniment
          parameters:
            harmony: "${harmony}"
            style: "${style}"
            instruments: "${section_template.instrumentation}"
          result_variable: accompaniment

      # Step 4: Apply voice leading
      - service_call:
          service: music
          method: apply_voice_leading
          parameters:
            harmony: "${harmony}"
            melody: "${melody}"
            accompaniment: "${accompaniment}"
            rules: "${style.voice_leading_rules}"
          result_variable: voiced_section

      - return:
          value: "${voiced_section}"
```

---

### Component 4: Analysis Tools

**Purpose**: Extract patterns from existing music for style database.

```yaml
# Analysis workflow
analysis:
  input: midi_file OR audio_file
  pipeline:
    - extract_melody:
        method: highest_voice  # or ML-based separation
    - detect_key:
        method: key_finding_algorithm
    - extract_chords:
        method: chord_detection
    - analyze_form:
        method: section_detection
    - detect_tempo:
        method: beat_tracking
    - extract_phrases:
        method: phrase_boundary_detection
  output:
    - progression_templates
    - phrase_patterns
    - gesture_examples
    - style_characteristics
```

We'll need tools for:
1. **MIDI Analysis**: Parse MIDI files, extract patterns
2. **Audio Analysis**: For samples where MIDI isn't available (more complex)
3. **Curation Interface**: Human review and tagging of analyzed material
4. **Pattern Aggregation**: Summarize patterns across many examples into style rules

---

## Implementation Phases

### Phase 1: Foundation (Theory Engine Core)

**Goal**: Pure music theory library that can generate basic compositions.

**Deliverables**:
1. Pitch/Interval/Scale/Chord modules
2. Basic melody generation (scale-constrained, contour-following)
3. Basic progression generation (style-constrained)
4. Simple form templates (A-B, A-A-B-A)
5. MIDI-JSON output
6. Unit tests for all theory operations

**Validation**: Generate a simple 16-bar piece in a defined style, listen, verify it follows the rules.

### Phase 2: Style System

**Goal**: Style inheritance and configuration.

**Deliverables**:
1. StyleDefinition schema
2. Style inheritance resolver
3. Initial style definitions (jazz, celtic, classical, blues)
4. Style database integration (lib-state)
5. Style-specific generation (progressions sound jazzy vs classical)

**Validation**: Generate same melodic contour in jazz vs classical style, verify they sound different.

### Phase 3: Form & Structure

**Goal**: Song-level structure and narrative arc.

**Deliverables**:
1. SectionRole system (exposition, development, climax, resolution)
2. Form templates (verse-chorus, AABA, ternary, rondo)
3. Section-to-section transitions
4. Dynamic/intensity arc across piece
5. Thematic connection between sections (motif reuse)

**Validation**: Generate a 3-minute piece with clear form, verify emotional arc.

### Phase 4: Composition Behaviors

**Goal**: ABML-driven composition orchestration.

**Deliverables**:
1. Music Service schema (`schemas/music-api.yaml`)
2. Composition behavior ABML definitions
3. Async composition workflow (request → process → notify)
4. Integration with game audio orchestration behaviors

**Validation**: Request a composition via API, receive completed piece asynchronously.

### Phase 5: Analysis & Curation

**Goal**: Tools to learn from existing music.

**Deliverables**:
1. MIDI analysis pipeline
2. Pattern extraction algorithms
3. Curation interface (could be simple CLI initially)
4. First curated style database (one genre deep)

**Validation**: Analyze 50 Celtic tunes, extract patterns, generate new tune that sounds Celtic.

### Phase 6: Performance Layer

**Goal**: Humanization and interpretation.

**Deliverables**:
1. Articulation system
2. Timing humanization
3. Velocity/dynamics curves
4. Style-specific performance practices

**Validation**: Same notation played "straight" vs "performed" sounds different.

### Phase 7: Advanced Generation

**Goal**: More sophisticated algorithms.

**Deliverables**:
1. Counterpoint generation
2. Sophisticated voice leading
3. Motivic development (transform motifs intelligently)
4. Style blending (50% jazz, 50% Celtic)

**Validation**: Generate a piece with two independent melodic lines that work together.

---

## Style Development Process

For each style we want to support:

### 1. Research Phase
- Gather reference material (public domain, royalty-free)
- Read music theory texts on the style
- Document characteristic features

### 2. Definition Phase
- Create StyleDefinition YAML
- Define scales, chords, progressions used
- Define rhythmic patterns
- Define ornamentation
- Define form preferences

### 3. Analysis Phase
- Run analysis tools on reference material
- Extract concrete examples
- Build gesture/phrase/progression libraries

### 4. Testing Phase
- Generate test pieces
- Human evaluation: Does it sound like the style?
- Iterate on style definition

### 5. Curation Phase
- Build up library of style-specific components
- Tag and organize patterns
- Document what makes "good" examples

### 6. Integration Phase
- Style available for composition requests
- Performance layer tuned for style
- Documentation complete

---

## First Target: Celtic Instrumental

**Why Celtic first?**
- Modal (Dorian, Mixolydian) - exercises mode system
- Clear form (AA-BB tune structure)
- Characteristic ornamentation - tests ornament system
- Rich public domain corpus - plenty of reference material
- Your stated interest in folk/Celtic style

**Celtic Style Definition Draft**:

```yaml
style:
  id: celtic_traditional
  name: "Celtic Traditional"
  extends: folk_base

  scales:
    preferred: [dorian, mixolydian, major, minor_pentatonic]
    characteristic: [dorian, mixolydian]
    avoid: [harmonic_minor, locrian]

  progressions:
    prefer_modal: true
    common_cadences:
      - i - bVII - i    # Dorian
      - I - bVII - I    # Mixolydian
      - I - IV - I
    avoid:
      - V7 - I  # Too classical

  rhythm:
    subdivision: straight
    dance_forms:
      jig: { meter: "6/8", tempo_range: [100, 130] }
      reel: { meter: "4/4", tempo_range: [100, 130] }
      air: { meter: "4/4", tempo_range: [50, 80], rubato: true }

  ornamentation:
    frequency: high
    types:
      grace_note: common
      roll: common
      cut: common
      slide: occasional
    placement: phrase_beginnings, emphasized_notes

  form:
    typical: AABB
    each_section: 8_bars
    repeats: true

  instrumentation:
    melody: [fiddle, tin_whistle, flute, uilleann_pipes]
    accompaniment: [guitar, harp, bouzouki]
    rhythm: [bodhran]

  voice_leading:
    parallel_fifths: allowed  # Common in folk
    drone: common
```

---

## API Surface

### High-Level API (Composition Service)

```csharp
// Request a composition
public async Task<CompositionId> RequestCompositionAsync(
    CompositionRequest request)
{
    // Starts async composition workflow
    // Returns immediately with ID
    // Notifies via event when complete
}

public class CompositionRequest
{
    public string StyleId { get; set; }          // "celtic_traditional"
    public string? Form { get; set; }            // "AABB" or null for auto
    public TimeSpan? TargetDuration { get; set; }
    public string? Mood { get; set; }            // "melancholic", "joyful"
    public string[]? Instruments { get; set; }
    public bool IncludePerformance { get; set; } // Apply humanization?
    public Dictionary<string, object>? ExtraConstraints { get; set; }
}
```

### Low-Level API (Theory Engine Direct)

```csharp
// Direct access to theory engine for fine control
public interface IMusicTheoryEngine
{
    // Pitch operations
    Pitch TransposePitch(Pitch pitch, Interval interval);
    Interval GetInterval(Pitch from, Pitch to);
    Scale GetScale(PitchClass root, ScaleType type);

    // Chord operations
    Chord BuildChord(PitchClass root, ChordQuality quality, Pitch[]? voicing = null);
    Chord[] VoiceProgressionWithLeading(Chord[] progression, VoiceLeadingRules rules);

    // Generation
    Progression GenerateProgression(ProgressionConstraints constraints);
    Melody GenerateMelody(MelodyConstraints constraints);
    Rhythm GenerateRhythm(RhythmConstraints constraints);

    // Structure
    Form SelectForm(FormConstraints constraints);
    Section[] ApplyFormToContent(Form form, SectionContent[] content);

    // Output
    MidiJson RenderToMidiJson(Composition composition);
    byte[] RenderToStandardMidi(Composition composition);
}
```

---

## Next Immediate Steps

1. **Create `lib-music-theory` project** in the plugins directory
2. **Implement Pitch module** - PitchClass, Pitch, Interval, Scale basics
3. **Implement simple tests** - Verify interval math, scale construction
4. **Add schema for Music Service** - `schemas/music-api.yaml`
5. **Create first style definition** - Celtic as YAML

Would you like me to start with any of these?

---

## Open Items (Not Blocking)

- **Sample library integration**: How do we map abstract instruments to actual samples?
- **Audio rendering**: Do we render to audio server-side or just provide MIDI-JSON?
- **Curation UI**: What interface for human curation during style development?
- **LLM integration**: When/how does natural language enter the pipeline?

These can be decided as we build.
