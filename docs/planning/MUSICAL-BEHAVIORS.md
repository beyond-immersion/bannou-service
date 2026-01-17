# Musical Behaviors: ABML Extension for Dynamic Audio Orchestration

> **Status**: Planning / Research Phase
> **Created**: 2025-01-17
> **Author**: Design exploration with Claude

This document explores extending Bannou's Arcadia Behavior Markup Language (ABML) to support musical composition, dynamic audio orchestration, and real-time adaptive soundscapes.

---

## Executive Summary

ABML already handles behavioral orchestration for NPCs, fight coordinators, event managers, and more. Music is fundamentally *behavior* - sequences of events triggered by context and state. This natural alignment suggests we can extend ABML to handle:

1. **Dynamic Music Playback** - Orchestrating pre-composed tracks based on game state
2. **Adaptive Audio Layers** - Mixing and transitioning between musical stems in real-time
3. **Procedural Composition** - Generating musical content from behavioral rules
4. **DAW Integration** - Producing actual audio assets from behavioral specifications

---

## Part 1: The Vision

### 1.1 Music as Behavior

Your original Unity plugin demonstrated the core insight: music management *is* behavior programming.

| Traditional Audio Manager | Behavioral Audio |
|--------------------------|------------------|
| "Play track X" | "Express mood Y with appropriate music" |
| "Fade to track Y" | "Transition emotional state from A to B" |
| "Set volume to 0.5" | "Background presence at 50% attention" |
| "Loop ambient sounds" | "Maintain environmental texture" |

ABML already provides:
- **State machines** for tracking context
- **Event handling** for triggers
- **Transitions** with timing and conditions
- **Hierarchical composition** for layered behaviors
- **GOAP planning** for goal-driven sequences

All of these map directly to music concepts.

### 1.2 Three Levels of Musical Behavior

```
┌─────────────────────────────────────────────────────────────────┐
│  Level 3: COMPOSITION                                           │
│  Generate musical content from rules and constraints            │
│  (L-systems, Markov chains, constraint satisfaction)            │
├─────────────────────────────────────────────────────────────────┤
│  Level 2: ORCHESTRATION                                         │
│  Arrange and mix pre-composed elements dynamically              │
│  (vertical layering, horizontal resequencing, transitions)      │
├─────────────────────────────────────────────────────────────────┤
│  Level 1: PLAYBACK                                              │
│  Trigger and control audio tracks based on game state           │
│  (play, stop, fade, crossfade, volume, effects)                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Part 2: Research Findings

### 2.1 Music Notation Formats

| Format | JSON Native | Real-Time | Best For |
|--------|-------------|-----------|----------|
| **MIDI-JSON** | Yes | Excellent | Game integration, web playback |
| **MNX** | Yes | Moderate | Future-proof interchange (emerging) |
| **ChordPro** | Convertible | Good | Lyrics + chord charts |
| **ABC Notation** | Convertible | Moderate | Simple melodies, folk music |
| **MusicXML** | Convertible | Low | Professional notation interchange |

**Recommendation**: **MIDI-JSON** as primary format
- Native JSON/YAML compatibility
- Event-based (natural for reactive systems)
- Extensive ecosystem (Tone.js, web audio)
- Can be generated programmatically

### 2.2 Game Audio Middleware Patterns

The game industry has established proven patterns for dynamic music:

**Vertical Layering**
- Musical stems (drums, bass, melody, strings) added/removed dynamically
- Intensity increases by adding layers without interrupting playback
- Natural for ABML: each layer is a behavioral entity with activation conditions

**Horizontal Re-Sequencing**
- Music divided into segments with entry/exit points
- Transitions between segments based on game state
- Natural for ABML: state machine with musical segment states

**Stingers**
- Short musical phrases bridging two tracks
- Triggered at specific moments (entering danger zone, discovering item)
- Natural for ABML: one-shot events triggered by conditions

**Parameter-Driven Control** (FMOD/Wwise pattern)
- Continuous values (0.0-1.0) control audio properties
- Game state variables map to audio parameters
- Natural for ABML: context variables driving audio parameters

### 2.3 Procedural Music Generation Approaches

| Approach | Suitability | Control | Real-Time | Use Case |
|----------|-------------|---------|-----------|----------|
| **Markov Chains** | High | Low | Excellent | Ambient variation |
| **L-Systems/Grammars** | Excellent | Very High | Good | Structured composition |
| **Constraint-Based** | High | Maximum | Fair | Quality, theory-heavy |
| **Neural Networks** | Medium | Medium | Good | Style learning |
| **State Machines** | Excellent | High | Excellent | Behavioral transitions |

**Recommendation**: **Hybrid approach**
1. L-systems for structural skeleton
2. Constraint satisfaction for music theory rules
3. Markov chains for real-time variation

### 2.4 DAW Integration Paths

```
ABML Behavior Definition
         ↓
   [Behavior Parser]
         ↓
   MIDI Sequence + Automation
         ↓
   [Output Target]
         ↓
┌────────┬────────────┬──────────────┐
│ Game   │ DAW Import │ Real-time    │
│ Engine │ (REAPER/   │ Synthesis    │
│ (Unity)│ Ableton)   │ (SFZ/Tone.js)│
└────────┴────────────┴──────────────┘
```

**Key Integration Points**:
- **OSC (Open Sound Control)**: Network-transparent, bidirectional control
- **Max for Live**: Full Ableton Live control via API
- **SFZ Format**: Open text-based sample format, programmable
- **REAPER**: Most extensible DAW (Lua/Python scripting, OSC support)

---

## Part 3: Proposed Architecture

### 3.1 Music Service Design

```
┌─────────────────────────────────────────────────────────────────┐
│                        Music Service                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │
│  │   Composer   │  │ Orchestrator │  │   Player     │          │
│  │              │  │              │  │              │          │
│  │ - Grammar    │  │ - Layers     │  │ - MIDI-JSON  │          │
│  │ - Markov     │  │ - Transitions│  │ - Synthesis  │          │
│  │ - Constraints│  │ - Parameters │  │ - Playback   │          │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘          │
│         │                 │                 │                   │
│         └────────────┬────┴────────────────┘                   │
│                      ↓                                          │
│              ┌──────────────┐                                   │
│              │ Music State  │  (lib-state)                      │
│              │ - Themes     │                                   │
│              │ - Tracks     │                                   │
│              │ - Parameters │                                   │
│              └──────────────┘                                   │
│                      ↓                                          │
│              ┌──────────────┐                                   │
│              │ Music Events │  (lib-messaging)                  │
│              │ music.play   │                                   │
│              │ music.layer  │                                   │
│              │ music.param  │                                   │
│              └──────────────┘                                   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 ABML Music Extension Syntax

**Theme Definition** (stored as state):
```yaml
# music/themes/combat.music.yaml
version: "1.0"
format: abml-music
metadata:
  id: combat_theme
  name: "Combat Theme"
  bpm: 140
  key: D_minor

layers:
  - id: percussion
    stems:
      - asset: music/combat/drums_low.ogg
        intensity_range: [0.0, 0.4]
      - asset: music/combat/drums_high.ogg
        intensity_range: [0.4, 1.0]

  - id: bass
    stems:
      - asset: music/combat/bass_pulse.ogg
        intensity_range: [0.2, 1.0]

  - id: strings
    stems:
      - asset: music/combat/strings_tension.ogg
        intensity_range: [0.5, 1.0]

  - id: brass
    stems:
      - asset: music/combat/brass_heroic.ogg
        intensity_range: [0.8, 1.0]

transitions:
  fade_in_duration: 0.5s
  crossfade_duration: 2.0s
  beat_sync: true
```

**Behavioral Music Control** (in ABML behavior):
```yaml
# behaviors/ambient-music-controller.abml.yaml
version: "3.0"
metadata:
  id: ambient_music_controller
  type: behavior
  category: audio_orchestration

context:
  variables:
    current_theme:
      type: string
      default: "exploration"
    intensity:
      type: number
      default: 0.3
    danger_level:
      type: number
      default: 0.0

perceptions:
  - id: location_changed
    type: event
    topic: player.location.changed

  - id: combat_started
    type: event
    topic: combat.started

  - id: combat_ended
    type: event
    topic: combat.ended

  - id: danger_proximity
    type: continuous
    source: ai.threat_assessment.danger_level
    update_rate: 1s

flows:
  handle_location:
    trigger: location_changed
    actions:
      - service_call:
          service: location
          method: get
          parameters:
            location_id: "${event.location_id}"
          result_variable: location_data

      - set:
          variable: current_theme
          value: "${location_data.music_theme ?? 'exploration'}"

      - music.transition:
          theme: "${current_theme}"
          duration: 3s
          sync_to_beat: true

  handle_combat:
    trigger: combat_started
    actions:
      - music.transition:
          theme: combat
          duration: 0.5s
          stinger: combat_start_stinger

      - music.set_parameter:
          name: intensity
          value: 0.6

  update_intensity:
    trigger: danger_proximity
    actions:
      - set:
          variable: danger_level
          value: "${perception.value}"

      # Map danger to intensity with smoothing
      - music.set_parameter:
          name: intensity
          value: "${0.3 + (danger_level * 0.7)}"
          interpolation: smooth
          duration: 1s
```

### 3.3 Procedural Composition Extension

For actual music *generation* (not just playback orchestration):

```yaml
# compositions/procedural/tavern_ambience.compose.yaml
version: "1.0"
format: abml-compose
metadata:
  id: tavern_ambience_generator
  description: "Generates jovial tavern background music"

parameters:
  mood:
    type: enum
    values: [peaceful, lively, rowdy]
    default: lively
  time_of_day:
    type: number  # 0-24 hours
    default: 20

composition:
  # L-system grammar for structure
  grammar:
    axiom: SONG
    rules:
      SONG: INTRO VERSE VERSE CHORUS VERSE CHORUS OUTRO
      VERSE: PHRASE PHRASE VARIATION PHRASE
      CHORUS: HOOK HOOK RESPONSE
      PHRASE: MELODY_4BAR
      HOOK: MELODY_2BAR_CATCHY

  # Constraint satisfaction for harmony
  constraints:
    scale:
      peaceful: major
      lively: mixolydian
      rowdy: dorian
    tempo:
      peaceful: 80
      lively: 120
      rowdy: 140
    chord_progression:
      - pattern: "I IV V I"
        weight: 0.4
      - pattern: "I V vi IV"
        weight: 0.3
      - pattern: "I IV I V"
        weight: 0.3
    voice_leading: smooth

  # Markov chains for melody variation
  melody_generation:
    training_style: celtic_folk
    temperature: 0.7  # Randomness control
    constraints:
      - stay_in_scale
      - stepwise_motion_preferred
      - resolve_to_tonic

  # Instrumentation layers
  orchestration:
    peaceful:
      - instrument: lute
        role: melody
      - instrument: harp
        role: harmony
    lively:
      - instrument: fiddle
        role: melody
      - instrument: bodhran
        role: rhythm
      - instrument: tin_whistle
        role: countermelody
    rowdy:
      - instrument: accordion
        role: melody
      - instrument: drums
        role: rhythm
      - instrument: bass_viol
        role: bass

output:
  format: midi-json
  tempo_map: true
  articulation: true
```

### 3.4 Data Format: MIDI-JSON

The core interchange format for all musical data:

```yaml
# Example MIDI-JSON representation (YAML for readability)
duration: 8.0  # seconds
tempo: 120
time_signature: [4, 4]
key: "C_major"

tracks:
  - name: melody
    instrument: 73  # MIDI program number (flute)
    notes:
      - time: 0.0
        pitch: 72  # C5
        duration: 0.5
        velocity: 80
      - time: 0.5
        pitch: 74  # D5
        duration: 0.5
        velocity: 75
      - time: 1.0
        pitch: 76  # E5
        duration: 1.0
        velocity: 85

  - name: harmony
    instrument: 0  # Piano
    notes:
      - time: 0.0
        pitch: [60, 64, 67]  # C major chord
        duration: 2.0
        velocity: 60

control_changes:
  - track: melody
    time: 0.0
    controller: 7  # Volume
    value: 100
  - track: melody
    time: 4.0
    controller: 7
    value: 80  # Fade down
```

---

## Part 4: Implementation Roadmap

### Phase 1: Playback Orchestration (Foundation)

**Goal**: Dynamic music playback and layer control via ABML

**Components**:
1. `MusicService` with basic playback control
2. ABML extensions: `music.play`, `music.stop`, `music.transition`
3. Theme and layer definitions in YAML
4. Parameter-driven intensity control
5. Event-based triggers from game state

**Deliverables**:
- Music service schema (`schemas/music-api.yaml`)
- Music state store for active playback state
- ABML action types for music control
- Client-side audio player (Unity integration)

### Phase 2: Adaptive Orchestration

**Goal**: Vertical layering and horizontal re-sequencing

**Components**:
1. Layer management with intensity-based stem activation
2. Beat-synchronized transitions
3. Stinger system for one-shot musical events
4. Crossfade and blend algorithms
5. Real-time parameter interpolation

**Deliverables**:
- Extended music service with layer control
- Transition engine with beat sync
- Stinger library management
- Advanced ABML music actions

### Phase 3: Procedural Composition

**Goal**: Generate musical content from behavioral rules

**Components**:
1. Grammar-based structure generation (L-systems)
2. Constraint satisfaction for music theory
3. Markov chains for melodic variation
4. MIDI-JSON output generation
5. Training data integration (style learning)

**Deliverables**:
- Composition service (`schemas/composition-api.yaml`)
- Grammar parser and expander
- Constraint solver for harmony/voice leading
- Markov model training and generation
- MIDI-JSON export pipeline

### Phase 4: DAW Integration

**Goal**: Bridge to professional audio production

**Components**:
1. MIDI file export from MIDI-JSON
2. OSC output for real-time DAW control
3. SFZ integration for sample library playback
4. REAPER/Ableton project generation
5. Max for Live integration module

**Deliverables**:
- DAW integration service
- OSC message routing
- SFZ sample library support
- Project file generators
- Real-time DAW control API

---

## Part 5: Technical Considerations

### 5.1 Audio Playback in Game Clients

**Unity Integration**:
- WebSocket receives music events from server
- Local audio manager interprets commands
- Stems loaded and mixed client-side
- FMOD or Wwise for professional audio (optional)
- Fallback to Unity's native audio system

**Web Clients**:
- Tone.js for MIDI-JSON playback
- Web Audio API for synthesis
- WebSocket for real-time music events

### 5.2 State Synchronization

Music state must be synchronized across:
- Server (authoritative music state per session)
- Multiple clients (latency-tolerant sync)
- Behavioral systems (NPC/event awareness)

```yaml
# Music state in lib-state
store: music_session_state
schema:
  session_id: string
  current_theme: string
  active_layers: string[]
  parameters:
    intensity: number
    tempo_modifier: number
  playback_position: number
  last_transition: timestamp
```

### 5.3 Performance Considerations

- **Pre-computation**: Generate MIDI-JSON during non-critical times
- **Caching**: Cache compiled music for repeated playback
- **Streaming**: Stream large compositions in chunks
- **Client-side**: Keep audio mixing on clients, not server

### 5.4 Existing Tools to Consider

| Tool | Use Case | Integration |
|------|----------|-------------|
| **Tone.js** | Web audio playback | Direct MIDI-JSON consumption |
| **FMOD** | Game audio middleware | Unity/Unreal integration |
| **Wwise** | Professional game audio | AAA-quality adaptive music |
| **NAudio** | .NET audio library | Server-side MIDI generation |
| **Magenta** | ML music generation | Style learning for procedural |
| **REAPER** | DAW with scripting | OSC control, Lua automation |
| **SFZ** | Open sample format | Text-based, programmable |

---

## Part 6: Creative Applications

### 6.1 NPC Musician Behaviors

```yaml
# An NPC bard that performs contextually appropriate music
behaviors:
  bard_performance:
    context:
      audience_mood: aggregated from nearby NPCs
      time_of_day: world state
      location_type: tavern, court, street

    actions:
      - analyze_context:
          factors: [audience_mood, time_of_day, location_type]
          result: performance_parameters

      - compose_performance:
          generator: tavern_ambience_generator
          parameters: "${performance_parameters}"

      - play_composition:
          to: nearby_clients
          visualize: true  # Show NPC playing animation
```

### 6.2 Environmental Soundscapes

```yaml
# Forest environment with layered ambient audio
environment:
  forest_ambience:
    base_layers:
      - wind_through_trees: always
      - distant_stream: if proximity to water

    time_layers:
      dawn: [bird_dawn_chorus]
      day: [bird_day_activity, insect_hum]
      dusk: [bird_evening_calls, cricket_start]
      night: [cricket_chorus, owl_calls, wolf_distant]

    weather_layers:
      rain: [rain_on_leaves, thunder_distant]
      storm: [heavy_rain, thunder_close, wind_gusts]

    danger_layers:
      - predator_nearby: [silence_insects, tension_drone]
      - combat_nearby: [combat_percussion, urgency_strings]
```

### 6.3 Narrative Music Scoring

```yaml
# Cutscene music that adapts to player choices
narrative_score:
  revelation_scene:
    structure:
      intro: mysterious_strings
      buildup: tension_layers
      revelation:
        if: player_chose_compassion
        then: hopeful_resolution
        else: tragic_theme
      outro: reflection_melody

    sync_points:
      - at: dialogue.line_5
        action: start_buildup
      - at: dialogue.choice_made
        action: trigger_revelation
      - at: scene.fade_out
        action: begin_outro
```

---

## Part 7: Open Questions

1. **Client-side synthesis vs. streaming**: Should clients synthesize from MIDI-JSON or stream rendered audio?

2. **Procedural vs. pre-composed balance**: What ratio of procedural content is acceptable for different game contexts?

3. **Training data licensing**: What music can we legally use to train Markov models?

4. **Latency tolerance**: How much music latency is acceptable for combat transitions?

5. **Memory constraints**: How many stems/layers can clients reasonably mix simultaneously?

6. **Compression formats**: Should we use Opus, AAC, or custom compression for stems?

---

## Part 8: Next Steps

1. **Schema Design**: Draft `schemas/music-api.yaml` for Music Service
2. **Prototype**: Build basic ABML music actions in behavior service
3. **MIDI-JSON Library**: Create .NET library for MIDI-JSON generation/parsing
4. **Unity Demo**: Proof-of-concept music manager in game client
5. **Procedural Prototype**: Simple L-system + Markov chain music generator

---

## Appendix A: Research Sources

### Music Notation Formats
- MNX Specification (W3C, emerging)
- Tone.js/Midi library
- MusicXML Documentation
- ABC Notation Standard
- ChordPro Standard

### Game Audio Middleware
- FMOD Studio Documentation
- Audiokinetic Wwise Courses
- ADX2 AISAC Documentation
- iMUSE historical analysis

### Procedural Music Generation
- Markov chains for music generation research
- L-systems in algorithmic composition
- Strasheela constraint-based composition
- Google Magenta neural music models

### DAW Integration
- REAPER OSC Documentation
- Max for Live API
- SFZ Format Specification
- PyLive Ableton control library

---

## Appendix B: Glossary

| Term | Definition |
|------|------------|
| **Stem** | Individual audio track (drums, bass, etc.) that can be mixed |
| **Vertical Layering** | Adding/removing musical layers to change intensity |
| **Horizontal Re-sequencing** | Transitioning between musical segments |
| **Stinger** | Short musical phrase for transitions |
| **MIDI-JSON** | JSON representation of MIDI data |
| **L-system** | Formal grammar for recursive pattern generation |
| **Markov Chain** | Probabilistic state machine for sequence generation |
| **OSC** | Open Sound Control protocol for audio systems |
| **SFZ** | Open text-based sample format |
| **BPM** | Beats per minute (tempo) |
| **Constraint Satisfaction** | Finding solutions that meet all specified rules |

---

*This document represents initial research and exploration. Implementation details will evolve as we prototype and validate approaches.*
