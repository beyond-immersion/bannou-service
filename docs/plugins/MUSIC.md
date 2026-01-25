# Music Plugin Deep Dive

> **Plugin**: lib-music
> **Schema**: schemas/music-api.yaml
> **Version**: 1.0.0
> **State Stores**: music-styles (MySQL, unused), music-compositions (Redis)

---

## Overview

Pure computation music generation using formal music theory rules and narrative-driven composition. Leverages two internal SDKs: `MusicTheory` (harmony, melody, pitch, time, style, MIDI-JSON output) and `MusicStoryteller` (narrative templates, emotional state planning, contour/density guidance). Generates complete compositions, chord progressions, melodies, and voice-led chord voicings. All generation is deterministic when a seed is provided, enabling Redis-based caching for repeat requests. No external service dependencies - fully self-contained computation.

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

1. **CreateStyle not persisted**: `CreateStyleAsync` returns the style as if created but does not save to the `music-styles` MySQL store or the in-memory `BuiltInStyles` collection. Custom styles are lost after the response.
2. **music-styles store declared but unused**: The MySQL store for styles exists in `state-stores.yaml` but is never accessed. All styles come from hardcoded `BuiltInStyles`.

---

## Potential Extensions

1. **Persistent custom styles**: Implement CreateStyle to store in MySQL, merge with BuiltInStyles on load.
2. **Multi-instrument arrangement**: Extend MIDI-JSON output to support multiple instrument tracks with orchestration rules.
3. **Real-time streaming**: Generate and stream MIDI events for live performance scenarios.
4. **Style mixing**: Blend parameters from multiple styles for hybrid compositions.

---

## Tenet Violations (Require Schema Changes)

### 1. IMPLEMENTATION TENETS: Defined State Store Not Used (`music-styles`)

**File**: `schemas/state-stores.yaml` (music-styles definition) and `plugins/lib-music/MusicService.cs`

The `music-styles` MySQL state store is defined in `state-stores.yaml` but is never referenced in the service implementation. `StateStoreDefinitions.MusicStyles` is never used. All styles come from hardcoded `BuiltInStyles`. If a defined cache/state store is genuinely unnecessary, it must be removed from `schemas/state-stores.yaml`.

**Fix**: Either implement persistence for custom styles using the `music-styles` store, or remove the store definition from `schemas/state-stores.yaml`.

---

### 2. IMPLEMENTATION TENETS: Hardcoded Tunables

**File**: `plugins/lib-music/MusicService.cs`

Multiple hardcoded numeric values represent tunables that should be configuration properties:

| Line(s) | Value | Purpose |
|---------|-------|---------|
| ~114, ~519, ~600, ~618 | `480` | Ticks per beat (MIDI resolution) |
| ~144, ~617 | `0.2` | Default syncopation |
| ~616 | `0.7` | Default rhythm density |
| ~128, ~520 | `1` | Chords per bar |
| ~513 | `8` | Default progression length |
| ~135 | `4` | Voice count for voice leading |
| ~862, ~868 | `0.2` | Tension threshold for contour detection |

**Fix**: Define configuration properties in `schemas/music-configuration.yaml` for each tunable:
- `MUSIC_TICKS_PER_BEAT` (default 480)
- `MUSIC_DEFAULT_SYNCOPATION` (default 0.2)
- `MUSIC_DEFAULT_RHYTHM_DENSITY` (default 0.7)
- `MUSIC_DEFAULT_CHORDS_PER_BAR` (default 1)
- `MUSIC_DEFAULT_PROGRESSION_LENGTH` (default 8)
- `MUSIC_DEFAULT_VOICE_COUNT` (default 4)
- `MUSIC_TENSION_CONTOUR_THRESHOLD` (default 0.2)

Note: The validation constants (24, 960, 0, 127, 0, 15) in `ValidateMidiJsonAsync` are MIDI specification constants (not tunables) and are acceptable as hardcoded values.

---

### 3. IMPLEMENTATION TENETS: Hardcoded Default Emotional State Values

**File**: `plugins/lib-music/MusicService.cs`, `ToSdkEmotionalState` method

The `ToSdkEmotionalState` method uses hardcoded default values (0.2, 0.5, 0.5, 0.5, 0.8, 0.5) for each emotional dimension when not provided by the request. These defaults represent tunable parameters for the "neutral" emotional starting point.

**Fix**: Define these defaults in the configuration schema:
- `MUSIC_DEFAULT_TENSION` (default 0.2)
- `MUSIC_DEFAULT_BRIGHTNESS` (default 0.5)
- `MUSIC_DEFAULT_ENERGY` (default 0.5)
- `MUSIC_DEFAULT_WARMTH` (default 0.5)
- `MUSIC_DEFAULT_STABILITY` (default 0.8)
- `MUSIC_DEFAULT_VALENCE` (default 0.5)

---

### 4. FOUNDATION TENETS: `MusicServicePlugin` Scope Lifecycle Issue

**File**: `plugins/lib-music/MusicServicePlugin.cs`

The `MusicServicePlugin` class creates a DI scope in `OnStartAsync`, resolves `IMusicService` into a field `_service`, and then the scope is disposed at the end of the `using` block. This means `_service` holds a reference to a scoped service whose scope has already been disposed. Any subsequent calls to `_service` (in `OnRunningAsync` or `OnShutdownAsync`) operate on an object whose injected scoped dependencies may already be disposed.

**Fix**: Either store the scope for the lifetime of the plugin (disposing it in `OnShutdownAsync`), or resolve the service fresh in each lifecycle method.

---

### 5. QUALITY TENETS: Missing XML Documentation on Private Helper Methods

**File**: `plugins/lib-music/MusicService.cs`

Several private helper methods lack `<summary>` XML documentation:

| Method |
|--------|
| `GetStyleDefinition` |
| `ToModeType` |
| `ToApiMode` |
| `ToChordQuality` |
| `ToApiChordQuality` |
| `ToContourShape` |
| `GetRomanNumeral` |
| `ConvertToStyleResponse` |

While private methods don't strictly require documentation, consistency within the file is valuable. Some private methods have documentation (e.g., `BuildStorytellerRequest`, `GetContourFromStoryResult`) while others do not. Low priority.

---

### Summary of Violations by Category

| Category | Count | Severity |
|----------|-------|----------|
| FOUNDATION TENETS | 1 | Medium (plugin lifecycle) |
| IMPLEMENTATION TENETS | 3 | High (unused state store, hardcoded tunables) |
| QUALITY TENETS | 1 | Low (XML docs consistency) |

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **480 ticks per beat hardcoded**: All generation uses 480 TPB (standard MIDI resolution). Not configurable per request. This is a common MIDI standard value.

2. **Seed determinism**: When `body.Seed` is provided, the same seed + parameters produces identical output. This enables Redis caching. Without a seed, `Environment.TickCount` is used (non-deterministic).

3. **Style lookup is in-memory only**: `BuiltInStyles.GetById()` and `BuiltInStyles.All` are static collections. No database lookup occurs. Adding styles requires code deployment.

4. **Storyteller always used for Generate**: Even when no mood or narrative options are provided, the Storyteller SDK is invoked with default parameters (neutral mood → `simple_arc` template).

5. **Mood-to-template mapping is hardcoded**: The switch expression mapping moods to narrative templates and emotional presets is embedded in code. Not externally configurable.

6. **VoiceLead returns violations alongside results**: Unlike validation endpoints that return pass/fail, voice leading always produces voicings even when rules are violated. Violations are reported alongside the result for informational purposes.

7. **Melody infers key from first chord**: `GenerateMelodyAsync` uses the root of the first harmony chord as the key center with Major mode. Does not attempt to detect actual key from the full progression.

### Design Considerations (Requires Planning)

1. **No event publishing for compositions**: Generated compositions are not tracked. No analytics on what styles/moods are popular, no composition history per user.

2. **Cache key includes all parameters**: The cache key for deterministic compositions includes seed, style, mood, tempo, bars, key, and tune type. Any parameter change misses the cache.

3. **No rate limiting on generation**: Composition generation is CPU-intensive (Storyteller + Theory + Rendering). No protection against burst requests exhausting compute resources.

4. **SDK types cross the API boundary**: Several types (PitchClass, Pitch, PitchRange, VoiceLeadingViolation) use `x-sdk-type` annotations meaning the API models are the actual SDK types. Changes to SDK types directly affect the API contract.

5. **EmotionalState has 6 floating-point dimensions**: The emotional space is 6D (tension, brightness, energy, warmth, stability, valence). Default values are 0.2/0.5/0.5/0.5/0.8/0.5 respectively. The relationship between these dimensions and musical output is embedded in the Storyteller SDK.
