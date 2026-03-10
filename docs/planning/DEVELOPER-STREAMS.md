# Developer Streams: Directed Multi-Feed Streaming via Divine Actor Orchestration

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-03-10
> **Last Updated**: 2026-03-10
> **North Stars**: #4, #5
> **Related Plugins**: Broadcast, Voice, Showtime, Director, Actor, Behavior, Agency, Puppetmaster

## Summary

Designs a system where a divine actor (regional watcher pattern) orchestrates multi-feed video streaming from a developer's workspace -- selecting which terminal, IDE, or browser feed to feature based on detected activity events, compositing them into a single directed output stream via Broadcast's RTMP pipeline. Uses 100% of the same Actor runtime, ABML behaviors, Director plugin, Agency feed management, and Broadcast composition infrastructure as in-game cinematic direction. Improvements to directing behaviors, feed management, or composition for either use case (developer streams or in-game cinematics) directly benefit the other. No implementation exists yet.

---

## The Core Insight

There is no architectural difference between:

1. A divine actor watching game event streams from a region, selecting which game camera feeds to feature, and compositing them into a directed broadcast
2. A divine actor watching developer activity events from a workspace, selecting which screen capture feeds to feature, and compositing them into a directed broadcast

Both cases involve an autonomous agent running ABML behaviors on the Actor runtime, perceiving structured events, making directing decisions, executing API calls to control feed composition, and outputting a composed stream via Broadcast's RTMP pipeline. The feeds are opaque RTMP streams -- Broadcast does not care whether the pixels originate from a game camera or a terminal emulator.

This document describes how to realize developer streams using the existing directing infrastructure, what new components are needed, and how the shared architecture ensures mutual improvement across use cases.

---

## Architecture

### The Regional Watcher Pattern Applied to Developer Workspaces

In-game, a regional watcher is a divine actor that:
- Subscribes to event streams from a geographic region (potentially spanning multiple servers)
- Taps into individual character perception events for detailed awareness
- Negotiates with and commands actors responsible for individual entities
- Directs cinematic sequences by executing APIs on game servers (camera systems, entity control, scene composition)
- Manages Broadcast rooms and feed connections for streaming output

For developer streams, the same pattern:
- Subscribes to activity event streams from a developer workspace (terminal output, build systems, git operations, file changes)
- Taps into individual feed metadata for detailed awareness (which terminal has focus, typing rate, scroll velocity)
- Manages screen capture feeds through the same feed connection APIs
- Directs composition by executing the same Broadcast compositor APIs (feed switching, PiP, split-screen, transitions)
- Manages the Broadcast room and its RTMP output to the streaming platform

The divine actor runs real ABML on the real Actor runtime. Its behavior document defines directing preferences, cut timing, feed priority rules, and composition strategies -- all as authored YAML content, not compiled code.

### System Overview

```
Developer Machine (Client SDK)
│
├── Screen Capture Agent
│   ├── Screen/Terminal 1 → RTMP feed to Broadcast
│   ├── Screen/Terminal 2 → RTMP feed to Broadcast
│   ├── Screen/Terminal 3 → RTMP feed to Broadcast
│   └── (additional feeds as configured)
│
├── Activity Detection Agent
│   └── Publishes structured events to message bus
│       (same event infrastructure as game events)
│
├── Focus Tracker
│   └── Publishes focus change metadata
│
└── Client-Side Intelligence (LLM-powered, optional)
    ├── Contextual commentary generation
    ├── Bannou factoid selection and formatting
    └── Posts to Chat service rooms on behalf of developer
         │
         ▼
Bannou Service Stack
│
├── Actor Runtime (L2)
│   └── Developer Stream Director (divine actor, regional watcher behavior)
│       ├── Perceives: developer activity events via message bus
│       ├── Evaluates: ABML conditions against ${stream.*} variables
│       ├── Decides: which feeds to feature, composition layout, transitions
│       └── Executes: Broadcast compositor APIs, feed management
│
├── Director Plugin (L4)
│   └── Human-in-the-loop override (developer can manually select feeds,
│       force PiP layout, pause directing, or take full manual control)
│
├── Agency (L4)
│   └── Feed manifest management
│       ├── Active feeds (full quality streaming)
│       ├── Standby feeds (reduced quality, ready for quick switch)
│       └── Inactive feeds (not streaming, saves bandwidth)
│
├── Broadcast (L3)
│   └── Multi-input RTMP compositor
│       ├── Receives multiple RTMP feed inputs
│       ├── Composites based on director commands
│       ├── Outputs single RTMP stream to platform
│       └── Integrates Voice room audio (opt-in)
│
└── Chat (L1)
    └── Stream chat room for commentary/factoids
        (posted by client-side LLM agent)
```

### Feed Lifecycle

```
1. Developer starts streaming session
   → Client SDK registers available feeds with Broadcast
   → Director divine actor spawned by Puppetmaster

2. Director receives initial feed manifest
   → Evaluates available feeds against ABML behavior
   → Sets initial composition (default: developer's focused screen)
   → Publishes Agency feed manifest (active/standby/inactive)

3. Client SDK reads Agency manifest
   → Starts streaming active feeds at full quality
   → Starts streaming standby feeds at reduced quality/framerate
   → Does NOT stream inactive feeds (bandwidth optimization)

4. Activity events flow continuously
   → Developer types, builds, tests, commits
   → Activity Detection Agent publishes events to message bus
   → Director divine actor perceives events

5. Director makes composition decisions
   → ABML behavior evaluates ${stream.*} variables
   → Decides: cut to build terminal, hold on test output, PiP layout, etc.
   → Executes Broadcast compositor API calls
   → Updates Agency feed manifest if standby/active sets change

6. Client SDK adapts to manifest changes
   → Promotes standby feed to active (already streaming, instant switch)
   → Demotes previously active feed to standby or inactive
   → Feed switches at Broadcast compositor are near-instantaneous

7. Session ends
   → Director actor despawned
   → Broadcast session closed
   → All feeds stopped
```

---

## Variable Providers: Shared Stream/Media Domains

The `${stream.*}`, `${camera.*}`, and `${audio.*}` variable namespaces are not developer-stream-specific. They describe **media feed state** that is equally applicable to in-game camera direction and developer stream direction. The game engine provides these variables for in-game cameras; the Client SDK provides them for screen capture feeds.

### `${stream.*}` Namespace

General feed/stream state, applicable to any directing scenario.

| Variable | Type | Description |
|----------|------|-------------|
| `stream.feed_count` | int | Number of registered feeds |
| `stream.active_feed` | string | ID of the currently featured feed |
| `stream.active_feed.duration` | float | Seconds since last feed switch |
| `stream.activity.any` | bool | Whether any feed has notable activity right now |
| `stream.activity.highest` | string | Feed ID with the most activity |
| `stream.activity.highest.rate` | float | Activity rate of the most active feed (0.0-1.0) |
| `stream.composition.layout` | string | Current layout (fullscreen, pip, split, grid) |
| `stream.audience.count` | int | Current viewer count (from Broadcast/Showtime) |
| `stream.audience.engagement` | float | Audience engagement level (from Showtime sentiment) |

### `${camera.*}` Namespace

Visual feed properties. In-game: game camera state. Developer streams: screen capture state.

| Variable | Type | Description |
|----------|------|-------------|
| `camera.<feedId>.active` | bool | Whether this feed is currently active |
| `camera.<feedId>.standby` | bool | Whether this feed is on standby |
| `camera.<feedId>.visual_change_rate` | float | Rate of visual change (0.0-1.0) |
| `camera.<feedId>.motion_intensity` | float | Intensity of motion/scrolling in the feed |
| `camera.<feedId>.content_type` | string | Detected content type (text, graphics, mixed) |

### `${audio.*}` Namespace

Audio feed properties. In-game: game audio/voice state. Developer streams: microphone/system audio state.

| Variable | Type | Description |
|----------|------|-------------|
| `audio.voice.active` | bool | Whether voice is currently active (Voice room) |
| `audio.voice.speaking` | bool | Whether the developer/player is currently speaking |
| `audio.music.active` | bool | Whether background music is playing |
| `audio.music.energy` | float | Current music energy level (from MusicStoryteller) |

### Developer-Specific Extensions

Additional variables specific to the developer workspace context. These would be provided by the Activity Detection Agent on the client machine, published as events, and exposed via a developer-specific variable provider.

| Variable | Type | Description |
|----------|------|-------------|
| `developer.build.status` | string | Current build state (idle, compiling, succeeded, failed) |
| `developer.build.target` | string | What is being built (service name, project) |
| `developer.test.status` | string | Test state (idle, running, passed, failed) |
| `developer.test.pass_count` | int | Number of passing tests in last run |
| `developer.test.fail_count` | int | Number of failing tests in last run |
| `developer.git.last_operation` | string | Most recent git operation (commit, push, diff, etc.) |
| `developer.git.uncommitted_changes` | bool | Whether there are uncommitted changes |
| `developer.focus.feed_id` | string | Feed ID of the developer's currently focused screen |
| `developer.focus.duration` | float | Seconds the developer has been focused on current screen |
| `developer.typing.active` | bool | Whether the developer is actively typing |
| `developer.typing.rate` | float | Characters per second (rolling average) |
| `developer.terminal.<feedId>.output_rate` | float | Terminal output rate (lines/sec) |
| `developer.file.last_saved` | string | Path of most recently saved file |
| `developer.file.service_context` | string | Bannou service context inferred from file path |

In-game equivalents would use game-specific variable providers -- `${battle.*}`, `${npc.*}`, etc. -- rather than `${developer.*}`. The `${stream.*}`, `${camera.*}`, and `${audio.*}` namespaces are shared across both contexts.

---

## ABML Director Behaviors

Director behaviors are authored YAML, not compiled code. Different behavior documents produce different directing styles -- the same infrastructure, different creative outcomes.

### Example: Activity-Driven Developer Stream Director

```yaml
# developer-stream-director.abml
# A regional watcher that directs developer streams based on activity patterns.
# Follows the same ABML structure as in-game regional watcher behaviors.

metadata:
  name: developer_stream_director
  type: regional_watcher
  region_type: developer_workspace

goals:
  - name: maintain_engaging_stream
    description: Keep the stream visually interesting by following activity
    priority: 1.0
    conditions:
      - "${stream.feed_count} > 0"

  - name: follow_dramatic_moments
    description: Prioritize high-drama events (test failures, build errors)
    priority: 0.9

  - name: provide_context
    description: Periodically show overview/context for viewers
    priority: 0.3

actions:
  - name: switch_to_feed
    preconditions:
      - "${camera.{target_feed}.active} == false"
    effects:
      - "stream.active_feed = {target_feed}"
    cost: 1.0  # Low cost -- cuts are cheap

  - name: setup_pip
    preconditions:
      - "${stream.composition.layout} != 'pip'"
    effects:
      - "stream.composition.layout = pip"
    cost: 2.0  # Slightly higher -- PiP is more complex visually

  - name: hold_current
    preconditions: []
    effects: []
    cost: 0.1  # Very cheap -- doing nothing is easy

# Perception handlers drive reactive decisions
perception_handlers:
  - type: developer_build_event
    evaluation:
      - condition: "${developer.build.status} == 'compiling'"
        action: switch_to_feed
        params: { target_feed: "${developer.build.terminal_feed}" }
        priority: 0.7

      - condition: "${developer.build.status} == 'failed'"
        action: switch_to_feed
        params: { target_feed: "${developer.build.terminal_feed}" }
        priority: 1.0
        hold_duration: 5000  # Hold on error output

      - condition: "${developer.build.status} == 'succeeded'"
        action: hold_current
        hold_duration: 2000  # Brief hold, then resume normal directing

  - type: developer_test_event
    evaluation:
      - condition: "${developer.test.status} == 'failed'"
        action: switch_to_feed
        params: { target_feed: "${developer.test.terminal_feed}" }
        priority: 1.0
        hold_duration: 5000

      - condition: "${developer.test.status} == 'passed' AND ${developer.test.pass_count} > 10"
        action: switch_to_feed
        params: { target_feed: "${developer.test.terminal_feed}" }
        priority: 0.8
        hold_duration: 3000  # Celebrate green tests

  - type: developer_typing_burst
    evaluation:
      - condition: "${developer.typing.rate} > 60 AND ${stream.active_feed.duration} > 8"
        action: switch_to_feed
        params: { target_feed: "${developer.focus.feed_id}" }
        priority: 0.5

  - type: developer_focus_change
    evaluation:
      # Developer explicitly switched focus -- follow after a brief delay
      - condition: "${developer.focus.duration} > 3"
        action: switch_to_feed
        params: { target_feed: "${developer.focus.feed_id}" }
        priority: 0.4

  - type: idle_timeout
    evaluation:
      # Nothing happening for a while -- show the focused screen
      - condition: "${stream.activity.any} == false AND ${stream.active_feed.duration} > 15"
        action: switch_to_feed
        params: { target_feed: "${developer.focus.feed_id}" }
        priority: 0.2
```

### Example: Scenario-Driven Director (Following a Build Sequence)

```yaml
# build-sequence-director.abml
# Follows a known build/test/commit sequence, directing like a scripted segment.
# Same pattern as an in-game director following a quest cinematic sequence.

metadata:
  name: build_sequence_director
  type: scenario_director
  scenario: build_test_commit

phases:
  - name: code_editing
    entry_condition: "${developer.typing.active} == true"
    directing:
      primary_feed: "${developer.focus.feed_id}"
      layout: fullscreen
      hold_until: "${developer.build.status} == 'compiling'"

  - name: compilation
    entry_condition: "${developer.build.status} == 'compiling'"
    directing:
      primary_feed: "${developer.build.terminal_feed}"
      secondary_feed: "${developer.focus.feed_id}"
      layout: pip  # Build output main, editor small
      hold_until: "${developer.build.status} != 'compiling'"

  - name: build_result
    entry_condition: "${developer.build.status} in ['succeeded', 'failed']"
    directing:
      primary_feed: "${developer.build.terminal_feed}"
      layout: fullscreen
      hold_duration: 3000
      # On failure, stay longer
      hold_duration_override:
        condition: "${developer.build.status} == 'failed'"
        duration: 8000

  - name: testing
    entry_condition: "${developer.test.status} == 'running'"
    directing:
      primary_feed: "${developer.test.terminal_feed}"
      layout: fullscreen
      hold_until: "${developer.test.status} != 'running'"

  - name: test_results
    entry_condition: "${developer.test.status} in ['passed', 'failed']"
    directing:
      primary_feed: "${developer.test.terminal_feed}"
      layout: fullscreen
      hold_duration: 5000

  - name: commit
    entry_condition: "${developer.git.last_operation} == 'commit'"
    directing:
      primary_feed: "${developer.git.terminal_feed}"
      layout: fullscreen
      hold_duration: 3000
```

Different ABML behavior documents produce fundamentally different directing experiences from the same feeds and activity events. A "chill coding" director might cut slowly and prefer the focused screen. A "dramatic build" director might rapid-cut during compilation and zoom to error lines. An "educational" director might hold on each screen longer for viewer comprehension. The directing style is authored content.

---

## Broadcast Multi-Input Composition

Broadcast's compositor is the central piece that serves both in-game and developer stream directing. It accepts multiple RTMP input feeds and composites them into a single output based on commands from the directing actor.

### Compositor Commands (Shared API Surface)

These commands are executed by the divine actor via API calls. They are identical whether the actor is directing game cameras or developer screens.

| Command | Parameters | Description |
|---------|------------|-------------|
| `feed/activate` | feedId, layout | Set a feed as the primary visible feed |
| `feed/pip` | primaryFeedId, secondaryFeedId, position, scale | Picture-in-picture layout |
| `feed/split` | leftFeedId, rightFeedId, ratio | Side-by-side split screen |
| `feed/grid` | feedIds[], columns | Grid layout of multiple feeds |
| `transition/cut` | targetFeedId | Hard cut to a new feed |
| `transition/dissolve` | targetFeedId, durationMs | Cross-dissolve between feeds |
| `transition/wipe` | targetFeedId, direction, durationMs | Directional wipe transition |
| `overlay/text` | text, position, style, durationMs | Text overlay (for titles, labels) |
| `overlay/graphic` | assetId, position, scale, durationMs | Graphic overlay (logos, borders) |

### Feed Management via Agency Manifest

The Agency service manages which feeds the client should be streaming at any given time. The feed manifest follows the same pattern as the UX capability manifest:

```json
{
  "sessionId": "...",
  "computedAt": "2026-03-10T14:30:00Z",
  "feeds": {
    "terminal-1": { "state": "active", "quality": "high", "priority": 1 },
    "terminal-2": { "state": "standby", "quality": "low", "priority": 2 },
    "ide-main":   { "state": "active", "quality": "high", "priority": 1 },
    "browser-1":  { "state": "inactive", "priority": 3 }
  },
  "nextLikelySwitches": ["terminal-2"],
  "bandwidthBudget": "adaptive"
}
```

The client SDK reads this manifest and manages feed streaming accordingly:
- **Active**: Streaming at configured quality (1080p, 30fps, etc.)
- **Standby**: Streaming at reduced quality (720p, 15fps) -- ready for instant promotion
- **Inactive**: Not streaming -- requires spin-up time if the director switches to it

This is the same mechanism that manages which scene feed a game client renders in the distributed scene sourcing model from COMPOSITIONAL-CINEMATICS. When the director anticipates a feed switch (the build is about to complete, so the test terminal will likely be needed next), it updates the manifest to promote that feed to standby before the switch happens.

---

## Client-Side Intelligence: Commentary and Factoids

Following the established Bannou principle that LLMs are never part of the server infrastructure, all natural language generation for stream commentary happens on the client machine.

### The Pattern

This follows the same pattern as the planned "dynamic dialogue" system for in-game characters:

| In-Game Dynamic Dialogue | Developer Stream Commentary |
|---|---|
| Intent chat (Lexicon entries) + archive data → embedded/cloud LLM on client → natural language dialogue | Activity events + Documentation entries → embedded/cloud LLM on client → stream commentary |
| Client-side, using player's own LLM resources | Client-side, using developer's own LLM resources |
| Posts to Chat room as character dialogue | Posts to Chat room as stream commentary |
| Bannou provides structured data; client provides language | Bannou provides structured data; client provides language |

### Data Flow

```
Bannou Services
├── Activity events (build started, test passed, file saved)
├── Documentation entries (pre-made summaries of services, features)
├── Chat service (room for stream commentary)
│
▼
Client SDK
├── Receives activity events + documentation context
├── Embedded/cloud LLM generates natural language:
│   ├── "Building the Transit service -- Transit manages geographic
│   │    connectivity graphs and game-time journey tracking"
│   ├── "All 47 tests passing for lib-transit"
│   └── "Schema changes detected -- regenerating service events"
├── Posts commentary to Chat room via Bannou API
│   (room visible to stream viewers via platform chat bridge)
└── Optionally renders text overlay on stream via Broadcast overlay API
```

### What Bannou Provides vs What the Client Provides

| Bannou Provides | Client Provides |
|---|---|
| Structured activity events | Natural language generation (LLM) |
| Documentation service entries (summaries, descriptions) | Contextual formatting and tone |
| Chat service rooms for posting | LLM inference (embedded or cloud) |
| Broadcast overlay API for on-stream text | Decision about when/what to comment |
| Service/plugin metadata (names, descriptions, endpoint counts) | Commentary timing and relevance filtering |

The Documentation service's full-text search and git-sync namespaces provide the raw factoid data. The client-side LLM selects relevant entries based on the current activity context (working on lib-transit → fetch Transit-related documentation entries) and formats them into engaging stream commentary. No LLM runs on the Bannou server.

---

## Relationship to Existing Systems

### What Already Exists or Is Planned (No Changes Needed to Core Design)

| Component | Role in Developer Streams | Status |
|-----------|--------------------------|--------|
| **Actor Runtime** (L2) | Executes the developer stream director divine actor | Implemented |
| **ABML Compiler** (L4) | Compiles director behavior YAML to bytecode | Implemented |
| **Puppetmaster** (L4) | Spawns/manages the director divine actor | Implemented |
| **Director Plugin** (L4) | Human-in-the-loop override (Observe/Steer/Drive) | Pre-implementation |
| **Agency** (L4) | Feed manifest management (active/standby/inactive) | Pre-implementation |
| **Broadcast** (L3) | RTMP compositor and platform output | Pre-implementation |
| **Voice** (L3) | Audio feed integration (developer voice opt-in) | Implemented |
| **Chat** (L1) | Stream chat room for commentary | Implemented |
| **Documentation** (L3) | Knowledge base for factoid content | Implemented |
| **Showtime** (L4) | Audience engagement, hype mechanics | Pre-implementation |
| **MusicStoryteller SDK** | Adaptive background music generation | Implemented (SDK) |

### What Needs to Be Built

| Component | Type | Description |
|-----------|------|-------------|
| **Screen Capture SDK** | Client SDK | Captures multiple screens/terminals, encodes as independent RTMP feeds. Platform-specific (Linux/macOS/Windows). Publishes feeds to Broadcast. |
| **Activity Detection Agent** | Client SDK | Hooks into terminal emulators, build systems, file watchers, git. Publishes structured activity events to Bannou message bus. |
| **`${stream.*}` / `${camera.*}` / `${audio.*}` Variable Providers** | DI Providers | Shared media/feed variable namespaces. Consumed by Actor runtime for ABML evaluation. Provided by game engine (in-game) or Client SDK (developer streams). |
| **`${developer.*}` Variable Provider** | DI Provider | Developer-specific variables (build state, test results, git ops, typing, focus). Registered via `IVariableProviderFactory`. |
| **Developer Director ABML Behaviors** | Authored Content | YAML behavior documents defining directing styles. Multiple variants for different streaming preferences. |
| **Broadcast Multi-Input Compositor** | Broadcast Enhancement | FFmpeg filter chain management for compositing multiple RTMP inputs. Shared with in-game camera composition. |
| **Client-Side Commentary Agent** | Client SDK | LLM-powered commentary generation using activity events + Documentation entries. Posts to Chat rooms. Developer's own LLM resources. |
| **Feed Manifest in Agency** | Agency Enhancement | Extension of the UX manifest pattern for media feed management (active/standby/inactive). Shared with in-game distributed scene sourcing. |

### What Improves Both Use Cases

Every component marked "shared" benefits both developer streams and in-game directing:

| Improvement | Developer Stream Benefit | In-Game Benefit |
|-------------|--------------------------|-----------------|
| Better ABML directing behaviors | More engaging stream composition | More engaging game cinematics |
| Richer `${stream.*}` variables | Better activity-based decisions | Better camera-based decisions |
| Smarter Agency feed prediction | Lower bandwidth, faster switches | Lower latency scene transitions |
| Better Broadcast composition | Smoother transitions, more layouts | Same |
| Better Director human override | Developer can fine-tune the stream | Game masters can fine-tune events |
| Better Showtime integration | Stream engagement drives directing | Audience hype drives game events |

---

## Open Questions

### 1. Screen Capture SDK Platform Strategy

The Screen Capture SDK is inherently platform-specific (screen capture APIs differ across Linux/macOS/Windows). Options:

- **FFmpeg-based**: Use FFmpeg's screen capture input (`x11grab`, `avfoundation`, `gdigrab`) with pipe-to-RTMP output. Cross-platform via FFmpeg, no custom capture code.
- **Native per-platform**: Platform-specific capture APIs (PipeWire on Linux, ScreenCaptureKit on macOS, DXGI on Windows) for lower latency and better control.
- **Hybrid**: FFmpeg for encoding/streaming, native APIs for capture.

The FFmpeg approach is simplest and aligns with Broadcast's existing FFmpeg pipeline.

### 2. Activity Detection Granularity

How does the Activity Detection Agent hook into development tools?

- **Terminal-level**: PTY monitoring, character rate detection, ANSI escape parsing. Works with any terminal but limited semantic understanding.
- **Tool-level**: Direct integration with make, dotnet, git, etc. Rich semantic events but requires per-tool integration.
- **File-system-level**: inotify/FSEvents for file changes, git status polling. Easy to implement, moderate semantic richness.
- **Hybrid**: File-system baseline + specific hooks for known tools (Makefile targets, dotnet test output parsing).

The hybrid approach likely provides the best balance of coverage and semantic richness.

### 3. Bandwidth Budget for Multi-Feed Streaming

With 4-6 screen capture feeds, bandwidth can be significant. The Agency feed manifest provides the control mechanism, but the policy needs design:

- How many feeds can be active simultaneously? (Probably 1-2 active, 1-2 standby, rest inactive)
- What quality/framerate for standby feeds? (720p/15fps might be sufficient for instant promotion)
- Should the director's ABML behavior be bandwidth-aware? (`${stream.bandwidth.available}` variable)
- Can feeds be compressed more aggressively when they contain mostly static text? (terminal-optimized encoding)

### 4. Music Integration

The MusicStoryteller SDK could provide adaptive background music for the stream:

- Calm ambient during code reading/writing
- Building tension during compilation
- Triumphant on successful builds/tests
- Ominous on failures
- Energy matching the director's current composition intensity

The `${audio.music.*}` variables would expose this state to the directing ABML, and the music system would read `${developer.*}` variables to adapt. This is exactly the Video Director's music-cinematic synchronization applied to developer activity instead of game events.

Should this be designed now or deferred as an enhancement?

### 5. Multi-Developer Streams

Could this extend to multiple developers working on the same codebase? Each developer is a "character" in the "region," and the divine actor director selects between developers the way it would select between characters in a game scene. The "region" becomes a collaborative development session rather than a single workspace.

This maps directly to the in-game multi-character ensemble model from VIDEO-DIRECTOR.md -- one developer as protagonist (most active), others as ensemble, with role-based screen time budgets.

### 6. Showtime Integration for Developer Streams

Showtime's simulated audience and hype train mechanics could apply to developer streams:

- Simulated "audience engagement" based on what's happening (tests passing = engagement spike)
- Hype train triggered by dramatic moments (long debugging session finally resolving)
- Audience engagement influencing director decisions (viewers are engaged with the build → hold on build terminal longer)

Should this be part of the initial design or a future enhancement?

---

## Relationship to Other Planning Documents

| Document | Relationship |
|----------|-------------|
| [VIDEO-DIRECTOR.md](VIDEO-DIRECTOR.md) | Shares the role-based composition model, ensemble directing, and music synchronization concepts. Developer streams use the same composition pipeline. |
| [COMPOSITIONAL-CINEMATICS.md](COMPOSITIONAL-CINEMATICS.md) | Provides the "independence is the default; synchronization is earned" principle. Each screen feed is an independent "cel layer." Distributed scene sourcing (multiple servers) maps to multiple screen feeds. |
| [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md) | The runtime infrastructure (CinematicRunner, CutsceneSession) that the developer stream director commands through the same APIs as in-game directing. |
| [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md) | Embedded Bannou on the developer machine could run the full directing stack locally for zero-latency composition, with only the final composed output streaming to the cloud. |

---

*This document describes a system that uses 100% of the same directing infrastructure as in-game cinematics, applied to developer workspace streaming. The key architectural thesis is that there is no meaningful difference between directing game camera feeds and directing screen capture feeds -- the divine actor, ABML behaviors, Agency feed management, and Broadcast composition work identically in both cases. Improvements are bidirectional.*
