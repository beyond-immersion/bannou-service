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

## Tenet Violations (Fix Immediately)

### 1. FOUNDATION TENETS (T6): Missing Null Checks in Constructor

**File**: `plugins/lib-music/MusicService.cs`, lines 40-50

The constructor does not perform `ArgumentNullException.ThrowIfNull()` or `?? throw new ArgumentNullException(...)` on any of its injected dependencies (`messageBus`, `stateStoreFactory`, `logger`, `configuration`). The standard service pattern requires explicit null checks on all constructor parameters.

**Fix**: Add null checks for all injected dependencies:
```csharp
_messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
_stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
_logger = logger ?? throw new ArgumentNullException(nameof(logger));
_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
```

---

### 2. IMPLEMENTATION TENETS (T21): Defined State Store Not Used (`music-styles`)

**File**: `schemas/state-stores.yaml` (music-styles definition) and `plugins/lib-music/MusicService.cs`

The `music-styles` MySQL state store is defined in `state-stores.yaml` but is never referenced in the service implementation. `StateStoreDefinitions.MusicStyles` is never used. All styles come from hardcoded `BuiltInStyles`. Per T21, if a defined cache/state store is genuinely unnecessary, it must be removed from `schemas/state-stores.yaml`.

**Fix**: Either implement persistence for custom styles using the `music-styles` store, or remove the store definition from `schemas/state-stores.yaml`.

---

### 3. IMPLEMENTATION TENETS (T21): Hardcoded Tunables

**File**: `plugins/lib-music/MusicService.cs`

Multiple hardcoded numeric values represent tunables that should be configuration properties:

| Line(s) | Value | Purpose |
|---------|-------|---------|
| 114, 521, 602, 622 | `480` | Ticks per beat (MIDI resolution) |
| 146, 621 | `0.2` | Default syncopation |
| 620 | `0.7` | Default rhythm density |
| 130, 522 | `1` | Chords per bar |
| 517 | `8` | Default progression length |
| 137 | `4` | Voice count for voice leading |
| 864, 870 | `0.2` | Tension threshold for contour detection |

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

### 4. IMPLEMENTATION TENETS (T23): Improper Async Pattern (`await Task.CompletedTask`)

**File**: `plugins/lib-music/MusicService.cs`, lines 322, 371, 433, 480, 557, 663, 736

Six methods (`ValidateMidiJsonAsync`, `GetStyleAsync`, `ListStylesAsync`, `CreateStyleAsync`, `GenerateProgressionAsync`, `GenerateMelodyAsync`, `ApplyVoiceLeadingAsync`) use `await Task.CompletedTask` as their only await. Per T23, this pattern is only acceptable when implementing a synchronous operation required by an async interface. However, these methods also call `_messageBus.TryPublishErrorAsync` in their catch blocks -- meaning they DO have async work. The `await Task.CompletedTask` on the happy path is technically acceptable per T23's guidance ("Synchronous Implementation of Async Interface") but is unnecessary because the catch block already provides async operations. The core issue is that the happy-path code is entirely synchronous and only the error path is async.

**Fix**: This is a minor stylistic issue. The current pattern is technically compliant since the `catch` block has real awaits, but the `await Task.CompletedTask` statements on the happy path are unnecessary noise. They could be removed if the method body is restructured, but this is low priority.

---

### 5. IMPLEMENTATION TENETS (T7): Missing `ApiException` Catch Block

**File**: `plugins/lib-music/MusicService.cs`, all endpoint methods

Every endpoint method catches only the generic `Exception` type. Per T7, methods making external calls (including state store operations in `GenerateCompositionAsync`) must distinguish `ApiException` from `Exception`. While some methods (style lookup, validation) are purely in-memory computation, `GenerateCompositionAsync` calls `TryGetCachedCompositionAsync` and `CacheCompositionAsync` which interact with the state store. Those helper methods have their own try-catch, but the outer catch in `GenerateCompositionAsync` should still distinguish `ApiException` for any future state store calls added to the method body.

**Fix**: Add `catch (ApiException ex)` before `catch (Exception ex)` in `GenerateCompositionAsync` at minimum:
```csharp
catch (ApiException ex)
{
    _logger.LogWarning(ex, "State store call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
catch (Exception ex)
{
    // existing error handling
}
```

---

### 6. QUALITY TENETS (T10): LogInformation Used for Operation Entry Points

**File**: `plugins/lib-music/MusicService.cs`, lines 61, 222, 349, 398, 460, 507, 584, 690

All endpoint entry-point log statements use `LogInformation` level. Per T10, operation entry logging should use `LogDebug` level. `LogInformation` is reserved for "significant state changes" (business decisions), not routine operation entry.

**Fix**: Change all entry-point log statements from `_logger.LogInformation(...)` to `_logger.LogDebug(...)`. For example:
```csharp
_logger.LogDebug("Generating composition with style {StyleId}", body.StyleId);
```

---

### 7. QUALITY TENETS (T19): Missing XML Documentation on Private Helper Methods

**File**: `plugins/lib-music/MusicService.cs`

Several private helper methods lack `<summary>` XML documentation:

| Line | Method |
|------|--------|
| 758 | `GetStyleDefinition` |
| 1002 | `ToModeType` |
| 1015 | `ToApiMode` |
| 1027 | `ToChordQuality` |
| 1044 | `ToApiChordQuality` |
| 1061 | `ToContourShape` |
| 1071 | `ToApiFunctionalAnalysis` |
| 1096 | `GetRomanNumeral` |
| 1110 | `ConvertToStyleResponse` |

While T19 specifically requires documentation on "all public classes, interfaces, methods, and properties," consistency within the file is important. Some private methods have documentation (e.g., `BuildStorytellerRequest`, `GetContourFromStoryResult`) while others do not.

**Fix**: Add `<summary>` documentation to all private helper methods for consistency. This is lower priority than public API documentation.

---

### 8. Dead Code: Unused Method `ToApiFunctionalAnalysis`

**File**: `plugins/lib-music/MusicService.cs`, line 1071

The method `ToApiFunctionalAnalysis(HarmonicFunctionType function)` is defined but never called anywhere in the codebase. `GetFunctionalAnalysis(int degree)` is used instead for functional analysis derivation from scale degree.

**Fix**: Remove the dead `ToApiFunctionalAnalysis` method.

---

### 9. Dead Code: Unused `Random` Variables

**File**: `plugins/lib-music/MusicService.cs`, lines 512, 589

In `GenerateProgressionAsync` (line 512) and `GenerateMelodyAsync` (line 589), a `var random = new Random(seed)` is created but never used. The `seed` value is passed directly to `ProgressionGenerator` and `MelodyGenerator` constructors instead.

**Fix**: Remove the unused `var random = new Random(seed);` declarations from both methods.

---

### 10. Duplicate `InternalsVisibleTo` Assembly Attribute

**File**: `plugins/lib-music/MusicService.cs` (line 21) and `plugins/lib-music/AssemblyInfo.cs` (line 5)

Both files declare `[assembly: InternalsVisibleTo("lib-music.tests")]`. This duplication is harmless but represents unnecessary noise.

**Fix**: Remove the `[assembly: InternalsVisibleTo("lib-music.tests")]` from `MusicService.cs` line 21 (keep it in `AssemblyInfo.cs` which is the canonical location for assembly attributes).

---

### 11. IMPLEMENTATION TENETS (T21): Hardcoded Default Emotional State Values

**File**: `plugins/lib-music/MusicService.cs`, lines 844-849

The `ToSdkEmotionalState` method uses hardcoded default values (0.2, 0.5, 0.5, 0.5, 0.8, 0.5) for each emotional dimension when not provided by the request. These defaults represent tunable parameters for the "neutral" emotional starting point.

**Fix**: Define these defaults in the configuration schema:
- `MUSIC_DEFAULT_TENSION` (default 0.2)
- `MUSIC_DEFAULT_BRIGHTNESS` (default 0.5)
- `MUSIC_DEFAULT_ENERGY` (default 0.5)
- `MUSIC_DEFAULT_WARMTH` (default 0.5)
- `MUSIC_DEFAULT_STABILITY` (default 0.8)
- `MUSIC_DEFAULT_VALENCE` (default 0.5)

---

### 12. FOUNDATION TENETS (T6): `MusicServicePlugin` Does Not Follow Standard Pattern

**File**: `plugins/lib-music/MusicServicePlugin.cs`

The `MusicServicePlugin` class creates a DI scope in `OnStartAsync` (line 66), resolves `IMusicService` into a field `_service`, and then the scope is disposed at the end of the `using` block. This means `_service` holds a reference to a scoped service whose scope has already been disposed. Any subsequent calls to `_service` (in `OnRunningAsync` or `OnShutdownAsync`) operate on an object whose injected scoped dependencies may already be disposed.

**Fix**: Either store the scope for the lifetime of the plugin (disposing it in `OnShutdownAsync`), or resolve the service fresh in each lifecycle method.

---

### Summary of Violations by Category

| Category | Count | Severity |
|----------|-------|----------|
| FOUNDATION TENETS | 2 | Medium (null checks), Medium (plugin lifecycle) |
| IMPLEMENTATION TENETS | 4 | High (unused state store, hardcoded tunables), Low (async pattern, error handling) |
| QUALITY TENETS | 2 | Low (log levels, XML docs) |
| Dead Code | 3 | Low (unused method, unused variables, duplicate attribute) |

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
