# Music Plugin Deep Dive

> **Plugin**: lib-music
> **Schema**: schemas/music-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: music-styles (MySQL, unused), music-compositions (Redis)

---

## Overview

Pure computation music generation (L4 GameFeatures) using formal music theory rules and narrative-driven composition. Leverages two internal SDKs: `MusicTheory` (harmony, melody, pitch, MIDI-JSON output) and `MusicStoryteller` (narrative templates, emotional state planning). Generates complete compositions, chord progressions, melodies, and voice-led voicings. Deterministic when seeded, enabling Redis caching for repeat requests. No external service dependencies -- fully self-contained computation.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis caching for deterministic compositions |
| lib-messaging (`IMessageBus`) | Error event publishing only |
| MusicTheory SDK (internal) | Harmony, melody, pitch, voice leading, MIDI-JSON rendering |
| MusicStoryteller SDK (internal) | Narrative templates, emotional state, contour/density planning |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (none identified) | No services currently call `IMusicClient` in production code |

---

## State Storage

| Store | Backend | Prefix | Purpose |
|-------|---------|--------|---------|
| `music-styles` | MySQL | — | Style definitions (declared but not used; styles are built-in) |
| `music-compositions` | Redis | `music:comp` | Cached deterministic compositions (seed-based key) |

---

## Events

### Published Events

This plugin does not publish domain events (only error events via `TryPublishErrorAsync`).

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `CompositionCacheTtlSeconds` | `MUSIC_COMPOSITION_CACHE_TTL_SECONDS` | `86400` | TTL for cached deterministic compositions (24 hours) |
| `DefaultTicksPerBeat` | `MUSIC_DEFAULT_TICKS_PER_BEAT` | `480` | MIDI ticks per beat (PPQN) for composition rendering |
| `DefaultChordsPerBar` | `MUSIC_DEFAULT_CHORDS_PER_BAR` | `1` | Number of chords per bar in generated progressions |
| `DefaultVoiceCount` | `MUSIC_DEFAULT_VOICE_COUNT` | `4` | Number of voices for chord voicing |
| `DefaultBeatsPerChord` | `MUSIC_DEFAULT_BEATS_PER_CHORD` | `4.0` | Beats per chord in progression generation |
| `DefaultMelodySyncopation` | `MUSIC_DEFAULT_MELODY_SYNCOPATION` | `0.2` | Default syncopation amount (0.0-1.0) |
| `DefaultMelodyDensity` | `MUSIC_DEFAULT_MELODY_DENSITY` | `0.7` | Default note density (0.0-1.0) |
| `DefaultEmotionalTension` | `MUSIC_DEFAULT_EMOTIONAL_TENSION` | `0.2` | Default emotional tension (0.0-1.0) |
| `DefaultEmotionalBrightness` | `MUSIC_DEFAULT_EMOTIONAL_BRIGHTNESS` | `0.5` | Default emotional brightness (0.0-1.0) |
| `DefaultEmotionalEnergy` | `MUSIC_DEFAULT_EMOTIONAL_ENERGY` | `0.5` | Default emotional energy (0.0-1.0) |
| `DefaultEmotionalWarmth` | `MUSIC_DEFAULT_EMOTIONAL_WARMTH` | `0.5` | Default emotional warmth (0.0-1.0) |
| `DefaultEmotionalStability` | `MUSIC_DEFAULT_EMOTIONAL_STABILITY` | `0.8` | Default emotional stability (0.0-1.0) |
| `DefaultEmotionalValence` | `MUSIC_DEFAULT_EMOTIONAL_VALENCE` | `0.5` | Default emotional valence (0.0-1.0) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<MusicService>` | Scoped | Structured logging |
| `MusicServiceConfiguration` | Singleton | Cache TTL config |
| `IStateStoreFactory` | Singleton | Redis composition cache access |
| `IMessageBus` | Scoped | Error event publishing |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Generation (1 endpoint)

- **Generate** (`/music/generate`): Full composition pipeline. Gets style from `BuiltInStyles`. Checks Redis cache if seed provided. Sets up key signature (from request or random from style's mode distribution). Composes via Storyteller SDK (narrative template + emotional states), generates progression via `ProgressionGenerator`, voices chords via `VoiceLeader` (4 voices), generates melody via `MelodyGenerator` (Storyteller-guided contour/density), renders to MIDI-JSON via `MidiJsonRenderer`. Returns composition with emotional journey and tension curve metadata.

### Validation (1 endpoint)

- **Validate** (`/music/validate`): MIDI-JSON structure validation. Checks: tracks exist, ticksPerBeat range (24-960), channel range (0-15), tick non-negative, note range (0-127), velocity range (0-127). Strict mode: warns about noteOn without duration. Returns errors and warnings separately.

### Styles (3 endpoints)

- **GetStyle** (`/music/style/get`): Lookup by ID or name from `BuiltInStyles`. Returns full definition including mode distribution, interval preferences, form templates, tune types, harmony style.
- **ListStyles** (`/music/style/list`): Lists all built-in styles with optional category filter. Supports offset/limit pagination.
- **CreateStyle** (`/music/style/create`): **NOT PERSISTED** - returns the input as a response object without saving to any store. Stub implementation.

### Theory (3 endpoints)

- **GenerateProgression** (`/music/theory/progression`): Creates chord progression for given key/mode using `ProgressionGenerator`. Returns chord events with roman numerals and functional analysis (tonic/subdominant/dominant). Default length 8 chords, 4 beats each.
- **GenerateMelody** (`/music/theory/melody`): Generates melody over provided harmony. Infers key from first chord. Configurable range, contour (Arch default), density (0.7 default), syncopation (0.2 default). Returns notes with pitch analysis (range, average duration, contour shape).
- **VoiceLead** (`/music/theory/voice-lead`): Applies voice leading rules to chord sequence. Configurable rules: avoid parallel fifths/octaves, prefer stepwise motion, avoid voice crossing, max leap (default 7). Returns 4-voice voicings with any rule violations reported.

---

## Visual Aid

```
Composition Pipeline (Generate endpoint)
==========================================

  GenerateCompositionRequest
       │
       ├── StyleId → BuiltInStyles.GetById() → StyleDefinition
       │                                         │
       │                                         ├── ModeDistribution
       │                                         ├── IntervalPreferences
       │                                         └── DefaultTempo/Meter
       │
       ├── Mood/Narrative → BuildStorytellerRequest
       │    │
       │    ├── bright    → simple_arc + Joyful
       │    ├── dark      → tension_and_release + Tense
       │    ├── neutral   → journey_and_return + Neutral
       │    ├── melancholic → simple_arc + Melancholic
       │    └── triumphant → tension_and_release + Climax→Resolution
       │
       ▼
  Storyteller.Compose(request)
       │
       ├── Narrative template (sections, intents)
       ├── Contour guidance (per section)
       └── Density guidance (per section)
       │
       ▼
  ProgressionGenerator.Generate(key, bars * chordsPerBar)
       │
       ▼
  VoiceLeader.Voice(chords, voiceCount: 4)
       │
       ▼
  MelodyGenerator.Generate(progression, key, options)
       │ options.Contour = Storyteller contour
       │ options.Density = Storyteller density
       │
       ▼
  MidiJsonRenderer.RenderComposition(melody, voicings, tempo, meter, key, ...)
       │
       ▼
  GenerateCompositionResponse
       ├── MidiJson (tracks, events, ticksPerBeat)
       ├── Metadata (key, tempo, bars, seed)
       ├── NarrativeUsed (template ID)
       ├── EmotionalJourney (section → emotion)
       └── TensionCurve (per-bar tension values)


EmotionalState Dimensions
===========================

  EmotionalState(tension, brightness, energy, warmth, stability, valence)
                   │          │         │        │         │         │
                [0,1]      [0,1]     [0,1]    [0,1]     [0,1]     [0,1]
                   │          │         │        │         │         │
              Dissonance  Major/   Tempo/   Timbre  Rhythmic  Happy/
              Level       Minor    Density  warmth  regularity Sad
```

---

## Stubs & Unimplemented Features

1. **CreateStyle not persisted**: `CreateStyleAsync` returns the style as if created but does not save to the `music-styles` MySQL store or the in-memory `BuiltInStyles` collection. Custom styles are lost after the response. See Design Consideration #1 below - tracked in [#188](https://github.com/beyond-immersion/bannou-service/issues/188).
   <!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/188 -->
2. **music-styles store declared but unused**: The MySQL store for styles exists in `state-stores.yaml` but is never accessed. All styles come from hardcoded `BuiltInStyles`. See Design Consideration #1 below - tracked in [#188](https://github.com/beyond-immersion/bannou-service/issues/188).
   <!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/188 -->

---

## Potential Extensions

1. **Persistent custom styles**: Implement CreateStyle to store in MySQL, merge with BuiltInStyles on load. Tracked in [#188](https://github.com/beyond-immersion/bannou-service/issues/188).
   <!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/188 -->
2. **Multi-instrument arrangement**: Extend MIDI-JSON output to support multiple instrument tracks with orchestration rules.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/202 -->
3. **Real-time streaming**: Generate and stream MIDI events for live performance scenarios.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/203 -->
4. **Style mixing**: Blend parameters from multiple styles for hybrid compositions. Four candidate blending strategies: weighted average (interpolate style parameters), alternation (switch styles by phrase/section), layered (different styles for melody vs harmony), and evolution (transition between styles over composition duration). Should also support per-component blend ratios (e.g., 80% Celtic melody + 60% Jazz harmony). Tracked in [#204](https://github.com/beyond-immersion/bannou-service/issues/204).
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/204 -->
5. **Composer Layer (personality-driven generation)**: A third SDK layer above MusicStoryteller enabling persistent musical personalities ("virtual composers") with stylistic signatures, preference evolution, and ABML behavior integration. See full design in [#431](https://github.com/beyond-immersion/bannou-service/issues/431).
   <!-- AUDIT:NEEDS_DESIGN:2026-02-14:https://github.com/beyond-immersion/bannou-service/issues/431 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Storyteller always used for Generate**: Even when no mood or narrative options are provided, the Storyteller SDK is invoked with default parameters (neutral mood → `simple_arc` template).

2. **VoiceLead returns violations alongside results**: Unlike validation endpoints that return pass/fail, voice leading always produces voicings even when rules are violated. Violations are reported alongside the result for informational purposes.

3. **Melody infers key from first chord**: `GenerateMelodyAsync` uses the root of the first harmony chord as the key center with Major mode. Does not attempt to detect actual key from the full progression.

4. **SDK types cross the API boundary**: Several types (PitchClass, Pitch, PitchRange, VoiceLeadingViolation) use `x-sdk-type` annotations meaning the API models are the actual SDK types. Changes to SDK types directly affect the API contract.

### Design Considerations (Requires Planning)

1. **Unused state store (music-styles)**: The MySQL store is defined in `state-stores.yaml` but never accessed. All styles come from hardcoded `BuiltInStyles`. Either implement CreateStyle persistence or remove the store from schema.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/188 -->

2. ~~**Hardcoded tunables throughout**~~: **FIXED** (2026-01-31) - All hardcoded tunables moved to configuration schema. Added 12 new configuration properties in `music-configuration.yaml`: `DefaultTicksPerBeat`, `DefaultChordsPerBar`, `DefaultVoiceCount`, `DefaultBeatsPerChord`, `DefaultMelodySyncopation`, `DefaultMelodyDensity`, and 6 emotional state defaults. Service now reads from `_configuration` instead of magic numbers.

3. ~~**MusicServicePlugin scope lifecycle**~~: **FIXED** (2026-01-31) - Removed dead code from plugin. MusicService doesn't implement IBannouService, so the entire lifecycle resolution pattern was no-ops. Simplified plugin to minimal implementation: empty ConfigureServices, empty ConfigureApplication, no-op lifecycle methods. Plugin went from 141 lines to 60 lines of clean, honest code.

4. **No event publishing for compositions**: Generated compositions are not tracked. No analytics on what styles/moods are popular, no composition history per user.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/205 -->

5. **No rate limiting on generation**: Composition generation is CPU-intensive (Storyteller + Theory + Rendering). No protection against burst requests exhausting compute resources.
   <!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/206 -->

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Completed (2026-01-31)

- **Design Consideration #2** (Hardcoded tunables): FIXED - Added 12 configuration properties to `music-configuration.yaml`. All magic numbers now read from `_configuration`.
- **Design Consideration #3** (MusicServicePlugin lifecycle): FIXED - Removed dead code, simplified plugin from 141 to 60 lines.

### Pending Design

- **Stubs #1-2, Potential Extension #1, Design Consideration #1** (Unused state store / CreateStyle persistence): [#188](https://github.com/beyond-immersion/bannou-service/issues/188) - Requires decision on whether to implement custom styles or remove unused store
- **Potential Extension #2** (Multi-instrument arrangement): [#202](https://github.com/beyond-immersion/bannou-service/issues/202) - Needs design for orchestration rules and presets
- **Potential Extension #3** (Real-time streaming): [#203](https://github.com/beyond-immersion/bannou-service/issues/203) - Needs design for streaming protocol and timing semantics
- **Potential Extension #4** (Style mixing): [#204](https://github.com/beyond-immersion/bannou-service/issues/204) - Four candidate blend strategies and per-component ratios documented; needs implementation design
- **Potential Extension #5** (Composer Layer): [#431](https://github.com/beyond-immersion/bannou-service/issues/431) - Personality-driven generation layer with 5-dimension personality model, preference evolution, and ABML integration
- **Design Consideration #4** (Event publishing): [#205](https://github.com/beyond-immersion/bannou-service/issues/205) - Needs decision on whether composition analytics are needed
- **Design Consideration #5** (Rate limiting): [#206](https://github.com/beyond-immersion/bannou-service/issues/206) - Needs design for appropriate limits and enforcement point
