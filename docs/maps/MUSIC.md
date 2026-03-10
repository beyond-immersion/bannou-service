# Music Implementation Map

> **Plugin**: lib-music
> **Schema**: schemas/music-api.yaml
> **Layer**: GameFeatures
> **Deep Dive**: [docs/plugins/MUSIC.md](../plugins/MUSIC.md)

---

## Summary Table

| Field | Value |
|-------|-------|
| Plugin | lib-music |
| Layer | L4 GameFeatures |
| Endpoints | 8 (8 generated) |
| State Stores | music-compositions (Redis) |
| Events Published | 0 |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `music-compositions` (Backend: Redis, prefix: `music:comp`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `BuildCompositionCacheKey(request)` | `GenerateCompositionResponse` | Cache deterministic compositions keyed by style+seed+bars+key+tempo+tune+mood+narrative |

Cache key components (joined with `::`):
1. `styleId`
2. `seed` (or `"0"` fallback)
3. `durationBars`
4. `key:{Tonic}:{Mode}` (if key provided)
5. `tempo:{Value}` (if tempo provided)
6. `tune:{TuneType}` (if tune type provided)
7. `mood:{Mood}`
8. `narrative:{TemplateId}` (if narrative provided)

> **Note**: `music-styles` (MySQL) is defined in `state-stores.yaml` but never acquired or used. CreateStyle is a stub. See deep dive Known Quirks.

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | Acquires `_compositionCache` (Redis) in constructor |
| lib-messaging (IMessageBus) | L0 | Hard | Injected but unused in current implementation (no domain events published) |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Spans for cache read/write helper methods |

**No service client dependencies.** Music calls no other Bannou services.

**Internal SDK dependencies** (direct instantiation, not DI):
- **MusicTheory**: Scale, ProgressionGenerator, VoiceLeader, MelodyGenerator, MidiJsonRenderer, MelodyOptions
- **MusicStoryteller**: Storyteller, CompositionRequest, EmotionalState, WorldState

**Leaf node**: Nothing currently calls `IMusicClient`. Music is a terminal node in the dependency graph.

---

## Events Published

This plugin does not publish domain events.

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

#### Constructor Dependencies

| Service | Role |
|---------|------|
| `IMessageBus` | Event publishing (currently unused) |
| `IStateStoreFactory` | Acquires composition cache store (not stored as field) |
| `ILogger<MusicService>` | Structured logging |
| `MusicServiceConfiguration` | 18 tunable properties (cache TTL, MIDI resolution, emotional defaults, contour/density thresholds) |
| `ITelemetryProvider` | Activity spans for cache operations |

#### Static Helpers

| Class | File | Purpose |
|-------|------|---------|
| `MusicServiceMapper` | `MusicServiceMapper.cs` | A2 SDK boundary mapping between generated types and MusicTheory/MusicStoryteller SDK types (~27 methods) |
| `BuiltInStyles` | SDK (`MusicTheory.Style`) | Compiled-in style definitions (Celtic, Jazz, Baroque, etc.) |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| GenerateComposition | POST /music/generate | generated | user | composition-cache (conditional) | - |
| ValidateMidiJson | POST /music/validate | generated | user | - | - |
| GetStyle | POST /music/style/get | generated | user | - | - |
| ListStyles | POST /music/style/list | generated | user | - | - |
| CreateStyle | POST /music/style/create | generated | admin | - | - |
| GenerateProgression | POST /music/theory/progression | generated | user | - | - |
| GenerateMelody | POST /music/theory/melody | generated | user | - | - |
| ApplyVoiceLeading | POST /music/theory/voice-lead | generated | user | - | - |

---

## Methods

### GenerateComposition
POST /music/generate | Roles: [user]

```
style = GetStyleDefinition(body.StyleId)                     -> 404 if null
IF body.Seed.HasValue
  cacheKey = BuildCompositionCacheKey(body)
  READ composition-cache:cacheKey                            -> return (200, cached) if hit
  // cache read is non-fatal (try/catch, miss on failure)

seed = body.Seed ?? Environment.TickCount
random = new Random(seed)

IF body.Key != null
  scale = Scale(ToSdkPitchClass(body.Key.Tonic), TestableToModeType(body.Key.Mode))
ELSE
  scale = Scale(random PitchClass, style.ModeDistribution.Select(random))

tempo = body.Tempo ?? style.DefaultTempo
meter = style.DefaultMeter
IF body.TuneType non-empty AND style has matching tune type
  meter = tuneType.Meter

// Storyteller SDK: narrative-driven composition planning
compositionRequest = BuildStorytellerRequest(body, totalBars)
  // IF body.Narrative != null: use explicit template + emotional states
  // ELSE: map body.Mood to template+emotion preset:
  //   Bright → simple_arc + Joyful
  //   Dark → tension_and_release + Tense
  //   Neutral → journey_and_return + Neutral
  //   Melancholic → simple_arc + Melancholic
  //   Triumphant → tension_and_release + Climax→Resolution
  //   default → simple_arc + Neutral
storyResult = Storyteller.Compose(compositionRequest)

// MusicTheory SDK: harmony + melody + voicing
progression = ProgressionGenerator(seed).Generate(scale, totalBars * config.DefaultChordsPerBar)
voicings = VoiceLeader.Voice(progression.Chords, config.DefaultVoiceCount)

contour = GetContourFromStoryResult(storyResult)
  // tension delta > threshold → Ascending/Descending
  // narrative "tension_and_release" → Arch
  // narrative "journey_and_return" → Wave
  // default → Arch
density = GetDensityFromStoryResult(storyResult)
  // config.DensityMinimum + avgEnergy * config.DensityEnergyMultiplier

melody = MelodyGenerator(seed).Generate(progression, scale, melodyOptions)
midiJson = MidiJsonRenderer.RenderComposition(melody, voicings, tempo, meter, scale, ticksPerBeat)

response = GenerateCompositionResponse with MidiJson, Metadata, NarrativeUsed, EmotionalJourney, TensionCurve

IF cacheKey != null
  WRITE composition-cache:cacheKey <- response (TTL: config.CompositionCacheTtlSeconds)
  // cache write is non-fatal (try/catch, failure logged at Warning)

RETURN (200, GenerateCompositionResponse)
```

---

### ValidateMidiJson
POST /music/validate | Roles: [user]

```
// Pure validation — no state, no I/O
IF body.MidiJson.Tracks null or empty
  add Structure error
IF body.MidiJson.TicksPerBeat not in 24–960
  add Range error
FOREACH track in body.MidiJson.Tracks
  IF track.Channel not in 0–15 → add Range error
  FOREACH event in track.Events
    IF event.Tick < 0 → add Timing error
    IF event.Note not in 0–127 → add Range error
    IF event.Velocity not in 0–127 → add Range error
    IF body.StrictMode AND event is NoteOn without Duration → add warning

RETURN (200, ValidateMidiJsonResponse { IsValid = errors.Count == 0 })
```

---

### GetStyle
POST /music/style/get | Roles: [user]

```
IF body.StyleId non-empty
  style = BuiltInStyles.GetById(body.StyleId)
ELSE IF body.StyleName non-empty
  style = BuiltInStyles.All first match (case-insensitive name)
                                                               -> 404 if null

RETURN (200, StyleDefinitionResponse from ConvertToStyleResponse(style))
```

---

### ListStyles
POST /music/style/list | Roles: [user]

```
styles = BuiltInStyles.All
IF body.Category non-empty
  styles = filter by category (case-insensitive)

total = styles.Count
pagedStyles = styles.Skip(body.Offset).Take(body.Limit)

RETURN (200, ListStylesResponse { Styles, Total = total })
```

---

### CreateStyle
POST /music/style/create | Roles: [admin]

```
// STUB: does not persist — returns request data as response
response = StyleDefinitionResponse {
  StyleId = body.Name.ToLowerInvariant().Replace(" ", "-"),
  fields copied from request
}

RETURN (200, StyleDefinitionResponse)
// NOTE: No WRITE to music-styles store. Created styles are silently discarded.
```

---

### GenerateProgression
POST /music/theory/progression | Roles: [user]

```
seed = body.Seed ?? Environment.TickCount
scale = Scale(ToSdkPitchClass(body.Key.Tonic), TestableToModeType(body.Key.Mode))
length = body.Length ?? config.DefaultProgressionLength

progression = ProgressionGenerator(seed).Generate(scale, length)

// Convert SDK chords to API ChordEvents with timing
ticksPerBeat = config.DefaultTicksPerBeat
beatsPerChord = config.DefaultBeatsPerChord
FOREACH chord in progression
  ChordEvent { Root, Quality, Bass?, StartTick, DurationTicks, RomanNumeral }

analysis = ProgressionAnalysis { RomanNumerals, FunctionalAnalysis per degree }

RETURN (200, GenerateProgressionResponse { Chords, Analysis })
```

---

### GenerateMelody
POST /music/theory/melody | Roles: [user]

```
seed = body.Seed ?? Environment.TickCount

// Key inferred from first chord (or C Major default)
root = body.Harmony?.First?.Chord.Root ?? PitchClass.C
scale = Scale(ToSdkPitchClass(root), SdkModeType.Major)
// NOTE: always Major — mode is hardcoded regardless of harmony

// Convert harmony to SDK ProgressionChord objects
FOREACH chordEvent in body.Harmony
  ProgressionChord(ToSdkPitchClass, ToSdkChordQuality, romanNumeral, durationTicks / 480.0)
  // NOTE: 480 divisor is hardcoded (should use config.DefaultTicksPerBeat)

range = body.Range != null ? ToSdkPitchRange(body.Range) : SdkPitchRange.Vocal.Soprano
contour = body.Contour.HasValue ? ToSdkContourShape(body.Contour) : SdkContourShape.Arch
density = body.RhythmDensity ?? config.DefaultMelodyDensity
syncopation = body.Syncopation ?? config.DefaultMelodySyncopation

melody = MelodyGenerator(seed).Generate(progression, scale, melodyOptions)

// Convert SDK melody notes to API NoteEvents
FOREACH note in melody
  NoteEvent { Pitch = ToApiPitch(note.Pitch), StartTick, DurationTicks, Velocity }

analysis = MelodyAnalysis { NoteCount, Contour, AverageNoteDuration }
IF notes.Count > 0
  analysis.Range = PitchRange { Low = minPitch, High = maxPitch }

RETURN (200, GenerateMelodyResponse { Notes, Analysis })
```

---

### ApplyVoiceLeading
POST /music/theory/voice-lead | Roles: [user]

```
// Convert request chords to SDK Chord objects
FOREACH cs in body.Chords
  Chord(ToSdkPitchClass(cs.Root), ToSdkChordQuality(cs.Quality), ToSdkPitchClass(cs.Bass)?)

rules = ToSdkVoiceLeadingRules(body.Rules)
  // defaults: AvoidParallelFifths=true, AvoidParallelOctaves=true,
  //           PreferStepwiseMotion=true, AvoidVoiceCrossing=true, MaxLeap=7

(voicings, violations) = VoiceLeader(rules).Voice(chords, config.DefaultVoiceCount)

// Convert SDK voicings to API VoicedChords
FOR i = 0 to min(voicings.Count, body.Chords.Count)
  VoicedChord { Symbol = original chord, Pitches = voicings[i].Select(ToApiPitch) }

violations = violations.Count > 0 ? violations.Select(ToApiVoiceLeadingViolation) : null

RETURN (200, VoiceLeadResponse { Voicings, Violations })
```

---

## Background Services

No background services.

---

## Non-Standard Implementation Patterns

No non-standard patterns. All 8 endpoints are standard generated interface methods. No plugin lifecycle behavior beyond default. No manual routes, no controller-only endpoints, no custom overrides.
