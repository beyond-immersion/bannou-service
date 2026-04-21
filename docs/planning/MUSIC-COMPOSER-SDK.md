# Music Composer SDK: Pre-Authoring DAW-Ready Sessions via DAWproject

> **Type**: Design
> **Status**: All open questions in §11 ratified 2026-04-19. Planning document is complete and ready for implementation.
> **Created**: 2026-04-19
> **Last Updated**: 2026-04-19 (§3.3, §3.4, §6, §10.2, §11.1 revised post-Q1 — reverse-DNS role taxonomy, hierarchical fallback resolution, bulk-import as primary capture path; §3.7, §4.5, §5.2.2, §7.3, §7.10, §10.5, §11.2 revised post-Q2 — counterpoint companion-files-only emission plus mixdown verification tool; §11.3 ratified post-Q3 — MusicComposer stays file-format-only, no workbench scaffold; §2.9, §10.6, §11.4, §13.2 revised post-Q4 — Studio One Pro 7 is the sole DAW target; §11.5 ratified post-Q5 — pluginLibraryProfileId uses soft-preference semantics; §10 introduction + §10.1-§10.6 titles + §10.7 + §12.3 revised to remove MVP concept, time estimates, and invented productivity baselines — the SDK is all-or-nothing, phases represent dependencies not scheduling, implementation time is not estimated)
> **North Stars**: #5 (Emergent Over Authored), #4 (Ship Games Fast)
> **Related Plugins**: Music (lib-music)
> **Related SDKs**: MusicTheory (complete), MusicStoryteller (complete), *MusicComposer* (this document), *MusicComposer.Counterpoint* (peer planning doc)
> **Related Documents**: [COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md), [DARK-REVELRY-STYLE-OBJECTIVE.md](DARK-REVELRY-STYLE-OBJECTIVE.md), [MUSIC-SYSTEM.md](../guides/MUSIC-SYSTEM.md), [SDK-OVERVIEW.md](../guides/SDK-OVERVIEW.md), [PROCEDURAL.md](../plugins/PROCEDURAL.md)
> **External Dependencies**: [bitwig/dawproject](https://github.com/bitwig/dawproject) (MIT-licensed, XML/ZIP file format)
> **Primary Consumers**: Bannou developers authoring music for Arcadia and other Bannou-based games, holders of commercial DAW subscriptions (primarily PreSonus Studio One, but any DAWproject-compatible DAW works)

## Summary

Designs the **MusicComposer** SDK — the composer-layer companion to the existing MusicTheory (primitives) and MusicStoryteller (procedural generation) SDKs. Where MusicStoryteller is a GOAP planner that produces MIDI-JSON, MusicComposer produces DAWproject files — richly authored, near-complete DAW sessions with tracks, plugins, patches, MIDI with expression, automation, markers, and a pre-wired mix bus — so that the human composer's remaining job in their DAW is tweaking rather than authoring. MusicComposer does not generate music on its own; it marshals the output of Storyteller plus a developer-curated plugin library into a file format that opens directly in PreSonus Studio One, Bitwig, Cubase, Cubasis, VST Live, or n-Track and presents the composer with a session that plays meaningfully on first open.

## Executive Summary

The Bannou music stack has two complete layers: MusicTheory (pitches, scales, chord voicings, voice leading, MIDI-JSON rendering, grounded in Lerdahl/Huron/Juslin/Meyer) and MusicStoryteller (6-D emotional state, narrative arc templates, GOAP-planned composition). Between them, they can emit a fully notated composition as MIDI-JSON. But MIDI-JSON is an inert text file. A game needs *audio*, and audio requires instruments, mix, and a finishing pass — which requires a DAW. Today there is no bridge from the Bannou music pipeline into that finishing workflow.

The gap matters because the Bannou music system cannot, today, produce a track that a game developer would actually ship. A MIDI-JSON file of a dark-revelry villain theme is a useful artifact for an in-engine adaptive music runtime that renders MIDI live, but it is not a mixed-and-mastered hero-entrance piece suitable for a cinematic. Bannou composers end up either hand-authoring everything from scratch in their DAW (leaving Storyteller unused) or treating Storyteller's output as a structural suggestion they reimplement by hand. Either way, the sophisticated narrative-planning work in MusicStoryteller evaporates at the DAW boundary.

MusicComposer closes the gap by targeting [DAWproject](https://github.com/bitwig/dawproject) — an open XML/ZIP project-exchange format co-developed by PreSonus and Bitwig (announced September 2023), natively supported by Studio One 6.5+, Bitwig 5.0.9+, Cubase 14, Cubasis 3.7.1, VST Live 2.2, and n-Track 10.2.2. A Bannou-emitted `.dawproject` contains a full track structure, referenced plugin instances with embedded state, MIDI notes with per-note expression, automation curves derived from Storyteller's emotional arc, named section markers, pre-wired groups and sends, and a mix-bus chain using DAWproject's built-in (DAW-agnostic) EQ/compressor/limiter devices. The composer opens the file in Studio One; the session already plays with the intended sound palette; their remaining work is aesthetic refinement, not structural authoring.

The SDK does *not* attempt to control Studio One as a server, does *not* host VST plugins for rendering, and does *not* produce audio on its own. Those capabilities were investigated and deliberately rejected — see [Part 2: Paths Explored and Rejected](#part-2-paths-explored-and-rejected) for the full account. The accepted design is file-based and asynchronous by design, matching the composer-layer role defined in [SDK-OVERVIEW.md](../guides/SDK-OVERVIEW.md) — "ruler and compass, not a printing press."

A peer document, [COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md), designs the structural-template workbench features (counterpoint compatibility validation, temporal-offset analysis, registral assignment). That document is unchanged by this one; MusicComposer as specified here is the output/export substrate that the Counterpoint workbench and Storyteller both publish through.

---

## Table of Contents

1. [Context — What's There and What's Missing](#part-1-context--whats-there-and-whats-missing)
2. [Paths Explored and Rejected](#part-2-paths-explored-and-rejected)
3. [The Accepted Design](#part-3-the-accepted-design)
4. [Architecture](#part-4-architecture)
5. [DAWproject Output Specification](#part-5-dawproject-output-specification)
6. [The Plugin Library Service](#part-6-the-plugin-library-service)
7. [ProjectBuilder — Mapping Table](#part-7-projectbuilder--mapping-table)
8. [Gaps and Workarounds](#part-8-gaps-and-workarounds)
9. [Licensing and Legal](#part-9-licensing-and-legal)
10. [Implementation Phases](#part-10-implementation-phases)
11. [Open Questions and Decision Points](#part-11-open-questions-and-decision-points)
12. [Success Criteria](#part-12-success-criteria)
13. [Future Extensions](#part-13-future-extensions)
14. [References](#references)

---

## Part 1: Context — What's There and What's Missing

### 1.1 The Music Stack As It Exists Today

Bannou's music system is two SDKs plus one plugin.

| Component | Status | What It Does |
|---|---|---|
| **`MusicTheory` SDK** | Complete | Pure-computation primitives: `Pitch`, `Interval`, `Scale`, `Chord`, `Voicing`, `VoiceLeader`, `MelodyGenerator`, `ProgressionGenerator`, `MidiJsonRenderer`, `Style` definitions. Zero dependencies. Deterministic. Grounded in Lerdahl's Tonal Pitch Space, Huron's ITPRA model, Juslin's BRECVEMA mechanisms, and Meyer/Pearce information theory. |
| **`MusicStoryteller` SDK** | Complete | GOAP-driven procedural composition. Tracks a 6-dimensional emotional state (tension, brightness, energy, warmth, stability, valence). Selects `NarrativeTemplate` (simple_arc, tension_and_release, journey_and_return) to structure a composition, plans sequences of musical actions (TensionActions, ResolutionActions, ThematicActions) via A*-based GOAP to reach target emotional states, and emits `CompositionIntent` objects that MusicTheory consumes to render actual notes. |
| **`lib-music` plugin** | Complete (L4) | HTTP API wrapping the two SDKs. Endpoints for `/music/generate` (full composition), `/music/theory/progression`, `/music/theory/melody`, `/music/theory/voice-lead`, `/music/validate`, `/music/style/*`. Redis-caches deterministic compositions by seed. Output format: **MIDI-JSON** — a portable JSON representation of MIDI data. |
| **`MusicComposer` SDK** | **Does not exist** — placeholder in SDK-OVERVIEW.md | Intended composer-layer SDK for interactive human authoring. The gap this document fills. |

The two SDKs work together cleanly. Storyteller receives a `CompositionRequest` with style, duration in bars, initial emotional state, and optional narrative template. It plans emotional phases, GOAP-selects musical actions for each phase, emits intents; MusicTheory turns the intents into chord progressions, voice-led voicings, and melodies with rhythmic density and contour shaped by the intents; MidiJsonRenderer flattens the result into a single MIDI-JSON document with tracks, tempo, time signature, key signature, and note events.

### 1.2 What MIDI-JSON Is (and What It Isn't)

Bannou's MIDI-JSON is a portable JSON representation of MIDI data, modeled loosely after Tone.js's format:

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
    { "name": "Melody", "channel": 0, "instrument": 73,
      "events": [{ "tick": 0, "type": "NoteOn", "note": 67, "velocity": 80, "duration": 240 }] }
  ]
}
```

This is enough for an **in-engine adaptive music runtime** — a game that renders MIDI live through a General MIDI synth or a small VST-host library embedded in the engine. It is not enough for **high-production-value authored music** — the kind of hero-entrance cinematic theme, regional identity anthem, deity leitmotif, or counterpoint-composed duet that the Counterpoint Composer SDK and the Dark Revelry style objective are designed to produce. Those tracks need:

- High-quality virtual instruments (orchestral libraries like Spitfire BBC SO, Kontakt-based sample libraries, analog-modeling synths like u-he Diva, drum samplers like Battery, etc.)
- Sound-design mixing (reverb sends, EQ shaping, compression gluing, saturation for character, width processing)
- Automation that shapes parameters over time in musically expressive ways (filter sweeps following tension curves, reverb tail length following warmth, send levels opening during climaxes)
- Articulations and expression (legato phrasing, staccato punctuation, crescendi, marcato accents, vibrato on sustained notes)
- A finishing pass by a human ear

MIDI-JSON encodes notes and timing. It does not encode sound design. An engine-side MIDI synth playing MIDI-JSON produces what MIDI sounded like in 1992 — general-MIDI-standard rendering of otherwise sophisticated compositional choices. The compositional sophistication is real; the sonic result is not.

### 1.3 The Composer Layer's Intended Role

[SDK-OVERVIEW.md](../guides/SDK-OVERVIEW.md) establishes the three-layer pattern across every creative domain in Bannou:

```
     COMPOSER                STORYTELLER            THEORY
 Handcrafted Authoring   Procedural Generation   Formal Primitives
```

The key architectural contract: **storytellers produce the same output format as composers.** A consumer receiving a scenario/composition/scene document cannot tell whether it was hand-authored or procedurally generated. In Music's case, both a human composer in a DAW and MusicStoryteller must produce the same kind of output artifact.

The SDK-OVERVIEW domain matrix lists the music composer as planned:

| Domain | Theory | Storyteller | Composer |
|---|---|---|---|
| Music | `music-theory` ✅ | `music-storyteller` ✅ | *planned* |
| Storyline | `storyline-theory` ✅ | `storyline-storyteller` ✅ | `storyline-composer` (in design) |
| Scene | — | — | `scene-composer` ✅ (reference impl) |
| Sprite | `sprite-theory` ✅ | *future SpriteBatcher* | `sprite-composer` ✅ |
| Voxel | `voxel-core` ✅ | `voxel-generator` ✅ | `voxel-builder` (planned) |

Two peer documents already exist that describe what a music composer experience *should* do:

- **[COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md)** designs a structural template workbench for authoring counterpoint-compatible music. Composers define templates (chord progressions, form, energy curves, contour hints), and the SDK validates compositions against template constraints, checks two pieces for compatibility at specified temporal offsets, and suggests consonant notes at conflict points. Its output artifact is "a composition validated against a structural template" — and it leaves the question of *what file format that composition lives in* open.
- **[DARK-REVELRY-STYLE-OBJECTIVE.md](DARK-REVELRY-STYLE-OBJECTIVE.md)** codifies a specific compositional aesthetic (gleeful villainy, theatrical minor-key darkness, dance-ready high-energy production) as a reusable style parameterization. It includes style YAML with mode distribution, swing factor, emotional profile, harmony style, dynamics profile, articulation profile, and character archetype variants. The style feeds either Storyteller (procedural) or Composer (human authoring aid).

Both documents describe pieces of a Composer experience. Neither answers the mechanical question: **once a human or the Storyteller has made compositional decisions, how does that composition reach a tool where it can be rendered as audio?**

### 1.4 The Author's Actual Workflow

The current author of this document (and likely the early Bannou game-dev adopters) owns a **PreSonus Sphere subscription** — PreSonus Studio One Pro 7, Notion scoring software, and the full PreSonus virtual instrument collection (Mai Tai polyphonic analog-modeling synth, Presence XT sample-player with 14 GB library, Impact XT drum sampler, Mojito monophonic subtractive synth, Sample One XT) — plus an additional library of third-party commercial VST plugins. This is a common posture: one DAW subscription + a collection of accumulated VST plugins representing years of purchases.

The author's current authoring experience is: conceive a theme, sit down in Studio One, manually set up tracks, load instruments, draw MIDI, mix, render. Bannou's music generation is visible in this workflow only if the author voluntarily reads Storyteller's MIDI-JSON output, opens Studio One, and transcribes it — which is functionally the same as authoring from scratch, because transcribing 32 bars of MIDI across four tracks is about as much work as writing those 32 bars. Storyteller's narrative planning, style selection, and emotional-state reasoning does not reach the DAW.

### 1.5 The Gap This SDK Closes

The gap is the file-format handoff between Bannou's compositional reasoning and the DAW's sonic realization. Close that gap with an exchange format that:

1. Carries the full compositional output of Storyteller/MusicTheory (notes, timing, tempo, meter, key, per-section emotional state)
2. Carries human-composer-authored work from the Counterpoint workbench (structural templates, phase-offset designs, registral assignments)
3. Tells the DAW which instruments go on which tracks, and what state those instruments start in
4. Pre-wires a mix bus with reasonable defaults for the style
5. Encodes expression and automation so the session plays musically on first open, not just mechanically
6. Opens natively in the DAW without import dialogs, format conversion, or plugin hunting

DAWproject carries every one of these, as the following parts establish in detail. The remaining design question — "can we carry *enough* of it to make Studio One's job small enough that the composer is tweaking rather than authoring?" — is answered concretely in [Part 5](#part-5-dawproject-output-specification) and [Part 7](#part-7-projectbuilder--mapping-table).

---

## Part 2: Paths Explored and Rejected

DAWproject was not the first shape proposed for this SDK. Several paths were investigated in depth before landing on the file-exchange model. Each is documented here with what it was, what made it attractive, what killed it, and the primary sources consulted. The ordering roughly follows the decision tree — the earliest-rejected paths at the top, the last-rejected path (headless VST hosting) at the bottom.

### 2.1 REJECTED: Studio One as a Headless Render Server (the "Houdini Parallel")

**What it was.** Model Studio One as a long-running headless worker, like `lib-procedural` models headless Houdini. Bannou's Storyteller composes MIDI, an Orchestrator-managed pool of Studio One worker processes receives the MIDI over an RPC protocol, loads the instruments, renders to audio, returns the WAV. This was the original mental model the author proposed when first framing the problem — "what if PreSonus is our Houdini?"

**What made it attractive.** Studio One's own instruments (Mai Tai, Presence XT, Impact XT, Mojito, Sample One XT) are Studio One-native and not available as standalone VST3 plugins ([KVR confirmed](https://www.kvraudio.com/forum/viewtopic.php?t=439186)). If Bannou could drive Studio One as a server, those instruments would be usable in automated rendering. The PreSonus Sphere subscription also includes massive sample libraries that are Studio One-format-only. A headless Studio One worker would be the only way to use those assets in an automated pipeline.

**What killed it.** Definitively: **Studio One has no headless mode, no CLI render flag, and no documented out-of-process automation surface.** Every path to programmatic control was investigated and found closed:

- **No CLI render mode.** No `Studio One.exe --render input.song output.wav`-style invocation exists. PreSonus has not shipped this capability in any released version. Community forum threads asking for it have gone unanswered for years ([PreSonus Answers thread](https://answers.presonus.com/11370/are-there-any-command-line-arguments-that-used-with-studio-one)).
- **No public remote-control API.** Studio One's companion mobile app, Studio One Remote, uses PreSonus's proprietary UCNet protocol. PreSonus has explicitly declined to document or open UCNet to third-party developers ([Answers thread](https://answers.presonus.com/13519/there-api-that-studio-one-remote-uses-which-can-used-other-apps)). Reverse-engineering UCNet is technically possible but unmaintainable — PreSonus is free to change it in any release, and a reverse-engineered integration is a support burden that grows indefinitely.
- **Internal JavaScript scripting exists but is locked.** Studio One has a hidden JavaScript engine used internally. Lukas Ruschitzka — a PreSonus contractor — has shipped community scripting add-ons ([Studio One Toolbox](https://studioonetoolbox.com/)) like Navigation Essentials, Scoring Tools, and Macro Toolbar extensions via it. But PreSonus has not made the API public ([VI-Control](https://vi-control.net/community/threads/why-does-presonus-keep-studio-ones-scripting-capabilities-under-lock-and-key.122050/)), the feature-request ticket ([feedback-software.presonus.com/feedback/233875](https://feedback-software.presonus.com/feedback/233875)) has not been delivered, and — critically — those scripts run **inside the Studio One GUI process**, triggered from inside the app, not from an external orchestrator. Even if the API were public, it would not give us a headless worker. It would give us "a script Studio One runs while Studio One is open."
- **No HTTP server surface.** Studio One does not expose itself as an HTTP endpoint. Nothing listens on any port that external processes can talk to beyond the UCNet protocol mentioned above, which is intentionally opaque.
- **Named pipes / Windows IPC hooks would require private-API hooking.** Windows inter-process communication techniques (named pipes, COM automation, DLL injection) require Studio One to expose some stable entrypoint. It does not. Building integration on top of private, undocumented Studio One internals would violate the EULA, lock us to Windows (despite PreSonus adding Linux support in Pro 7), and be broken by every Studio One update.

**Sources examined.**

- [studiooneforum.com Scripting API thread](https://studiooneforum.com/threads/studio-one-scripting-api.1356/)
- [PreSonus Answers: I need presonus' api for developing programs](https://answers.presonus.com/78545/i-need-presonus-api-for-developing-programs)
- [PreSonus Answers: scripting tools and knowledge base](https://answers.presonus.com/74686/enable-make-public-studio-one-scripting-tools-knowledge-base)
- [KVR Audio: Scripting in Studio One](https://www.kvraudio.com/forum/viewtopic.php?t=506195)
- [PreSonus Developer page](https://www.presonussoftware.com/en_US/developer) — exists but does not publish host-control APIs
- [fast-and-wide.com on Studio One Remote's UCNet protocol](https://www.fast-and-wide.com/equipment-releases/mobile-applications/7053-presonus-studio-one-remote)

**Verdict.** Studio One cannot be made to run as a server. The Houdini model does not translate. This is a product decision by PreSonus, not a technical gap that a clever workaround could close. Every workaround proposed during investigation collapsed on inspection: the internal JavaScript engine only runs inside the GUI, UCNet is closed, there's no CLI, hooking is EULA-violating, and the Studio One-native instruments fundamentally don't exist outside the app.

### 2.2 REJECTED: Studio One Internal JavaScript Scripting (Lukas Ruschitzka's Path)

**What it was.** Develop against Studio One's undocumented internal JavaScript engine — the one Studio One Toolbox, Navigation Essentials, and Scoring Tools use. These shipping products prove the engine is capable of meaningful scripting: MIDI editing, navigation, scoring workflows, macro sequences. If that expressiveness is available, surely Bannou could emit scripts that take MusicStoryteller's compositional plan and apply it inside Studio One.

**What made it attractive.** A working scripting path exists. Someone has already done non-trivial work in it. The community-reverse-engineered documentation at [studioonetoolbox.com/macrodocumentation](https://studioonetoolbox.com/macrodocumentation) contains enough information to attempt implementation.

**What killed it.** Three independent failures, any one of which would be disqualifying:

1. **It's not a remote API.** The JavaScript engine runs inside Studio One, triggered from inside Studio One. There is no "Bannou sends a script to Studio One and Studio One executes it" mechanism. The composer would need to manually open Studio One, load our script file, and run it. That's worse than the current workflow because it adds a scripting step on top of opening the DAW.
2. **It's not publicly supported.** PreSonus has declined to publish the API despite years of community requests. Lukas Ruschitzka is a PreSonus insider; others can only proceed via reverse engineering. Any Bannou product built on this surface would ride on the goodwill of Lukas's publications and PreSonus's forbearance — both of which can evaporate in any point release. Structural tests cannot validate the integration; we cannot promise our users that their workflow will survive a Studio One update.
3. **The output quality is wrong.** The scripting engine is designed for per-click operations ("select this range and transpose up an octave"), not large-scale session construction ("create 14 tracks, load these specific plugins, embed these preset states, write these 2000 notes with per-note expression"). It would require thousands of script commands to approximate what DAWproject expresses in a single XML file.

**Sources examined.**

- [Studio One Toolbox scripts page](https://studioonetoolbox.com/studioonescripts)
- [Studio One Toolbox macro documentation](https://studioonetoolbox.com/macrodocumentation)
- [VI-Control: Studio One Scripts — commands for composing, arranging, editing](https://vi-control.net/community/threads/studio-one-scripts-commands-for-composing-arranging-editing-christmas-freebie.119216/)
- [Lukas Ruschitzka's Payhip](https://payhip.com/LukasRuschitzka)

**Verdict.** The scripting engine is a real capability with real community use, but it's a fundamentally different shape from what Bannou needs. Even granting every optimistic assumption (we figure out the API, PreSonus doesn't break it, performance is acceptable), the integration would be: "open Studio One, open a file picker, run our script, wait, hope." DAWproject opens the session directly with `.dawproject → Open With → Studio One` and the session is already built.

### 2.3 REJECTED: PreSonus Plug-In Extensions SDK (Wrong Direction)

**What it was.** The [fenderdigital/presonus-plugin-extensions](https://github.com/fenderdigital/presonus-plugin-extensions) GitHub repository — an officially published PreSonus SDK. Surface scan suggested this was "the PreSonus API."

**What made it attractive.** It exists, it's published on GitHub, it's officially PreSonus, and the name includes "plugin extensions" which sounds like exactly what we need.

**What killed it.** It's the wrong direction. The SDK is for **plugin authors who want to extend their VST/VST3/AU plugins with Studio One-specific integration features** — things like Sound Variations, Instrument Extensions, key switch queries, gain reduction reporting, High-DPI scaling notifications, direct rendering targets, Wayland support, speaker arrangement queries. It's for Arturia, Spitfire, u-he, and other plugin vendors to make their plugins behave better *inside* Studio One. It does nothing for "Bannou wants to tell Studio One what to do" — that's the opposite polarity.

**Sources examined.**

- [fenderdigital/presonus-plugin-extensions README](https://github.com/fenderdigital/presonus-plugin-extensions)
- The repository's PDF specification and header files

**Verdict.** Important to document this one so future investigators don't have to re-rule-it-out. The repository is real, actively maintained, and correctly named for its purpose — it just isn't this purpose.

### 2.4 REJECTED: PreSonus StudioLive Hardware API (Wrong Product)

**What it was.** [featherbear/presonus-studiolive-api](https://github.com/featherbear/presonus-studiolive-api) — a reverse-engineered implementation of UCNet for PreSonus StudioLive Series III hardware mixers.

**What made it attractive.** Someone reverse-engineered UCNet successfully. Surface-level research suggested this could be adapted to Studio One.

**What killed it.** Different product line. StudioLive is PreSonus's hardware mixer family; Studio One is PreSonus's DAW. They both use UCNet but the protocol surfaces are different, the feature sets don't overlap, and the StudioLive reverse engineering does nothing for Studio One control.

**Sources examined.** [featherbear/presonus-studiolive-api repository](https://github.com/featherbear/presonus-studiolive-api).

**Verdict.** Good reverse-engineering work, wrong target. Documented here so the next investigator doesn't spend time on it.

### 2.5 REJECTED: Headless VST Hosting via DawDreamer or JUCE ("Shape 2")

**What it was.** Bypass Studio One entirely. Stand up a Bannou-managed pool of headless VST-host workers — DawDreamer (Python + JUCE) or a custom JUCE-based C++ service — running in Docker containers managed by the Orchestrator (exactly like `lib-procedural`'s Houdini workers). Bannou's Storyteller emits MIDI + plugin references + VST presets; workers render to audio; WAVs land in the Asset service. This is the real "Houdini parallel" — the name "MusicComposer" is almost a misnomer in this shape, because the Composer layer becomes the orchestrator of a render pool rather than an authoring aid.

**What made it attractive.**

- **It is architecturally clean.** `lib-procedural`'s shape is proven: Orchestrator-managed pool, `SHA256(templateId + params + seed)` content-addressed cache, parameterized generation, graceful degradation when the pool is unavailable. Applying the same shape to music works on paper.
- **Mature infrastructure exists.** [DawDreamer](https://github.com/DBraun/DawDreamer) is an actively-maintained Python/JUCE framework hosting VST2/VST3 instruments and effects, supporting parameter automation at audio-rate and PPQN, preset loading, batch and multiprocessing rendering, on Windows/macOS/Linux/Docker. [JUCE](https://github.com/juce-framework/JUCE) is the C++ framework it's built on; its `AudioPluginHost` example is a working headless host. [RenderMan](https://github.com/fedden/RenderMan) is a simpler Python alternative.
- **Licensing passes T18.** DawDreamer and JUCE are both GPLv3. T18 forbids GPL for linked code but explicitly permits GPL infrastructure in separate containers communicating via network protocols — the RTPEngine/Kamailio/Houdini carve-out. Containerized DawDreamer workers invoked via HTTP fit squarely in that carve-out.
- **It produces audio.** A Bannou user could hit `POST /music/render` and get back a WAV, no DAW required. Studio One's subscription could become irrelevant.

**What killed it.**

1. **It bypasses PreSonus's subscription.** The primary consumer of this SDK has a PreSonus Sphere subscription specifically for the Studio One-native instruments and the curated PreSonus sample libraries. DawDreamer cannot load Mai Tai, Presence XT, Impact XT, Mojito, or Sample One XT — none of them exist as standalone VST3 plugins ([KVR Audio](https://www.kvraudio.com/forum/viewtopic.php?t=439186)). The same is true of most of the Presence XT Core instrument libraries, which are Studio One-native content. A DawDreamer integration renders only the subset of the user's library that ships as standalone VST3/VST2/AU — for the author specifically, that's the *third-party* subset, not the PreSonus subset the subscription was purchased for.
2. **Most commercial VST licenses prohibit server-side deployment.** A large fraction of the high-quality commercial VST library market (Kontakt-based libraries from Native Instruments, Spitfire Audio libraries, EastWest ComposerCloud content, many Arturia/Waves plugins) has EULA language explicitly restricting deployment to single-user workstations, explicitly forbidding cloud/server hosting, or gating server use behind a separate (expensive) enterprise license tier. Building a Bannou feature that requires a render-pool-compatible VST library would either limit users to a small subset of what they own or require them to audit every EULA on their machine.
3. **It is not actually the "Composer" layer.** The SDK-OVERVIEW composer pattern is an *authoring aid for humans*, paired with a Storyteller for procedural generation. A headless render pool is neither. It would more properly be a new category — something like a `music-renderer` plugin parallel to `lib-procedural` — that the existing Storyteller could feed. Fitting it under "Composer" would confuse the domain pattern for future developers reading the SDK catalog.
4. **The production-quality ceiling is lower than DAW-based authoring.** A render pool can render MIDI with VST instruments and apply parameter automation — everything a DAW can do as pure operation. What it cannot do is what a human ear does: decide that the strings are too forward in the bridge, add a 2 dB bump at 6 kHz for air, pan the counterpoint line slightly left so it doesn't fight the melody, nudge a note off a grid by 8 ticks to make it feel played rather than sequenced. The final 10% of production quality is human judgment. A render pool completes track authoring; a human completes track finishing. For the caliber of music that Counterpoint and Dark Revelry aspire to, we need the finishing step.
5. **Infrastructure cost is non-trivial.** A Docker image with DawDreamer + license server integration + VST installation + orchestration adds meaningful operational surface. Compared to DAWproject (a few hundred KB of XML/ZIP, generated in under a second, no runtime infrastructure), it's an order of magnitude more complex to deploy and maintain.

**Sources examined.**

- [DBraun/DawDreamer repository and Wiki](https://github.com/DBraun/DawDreamer)
- [DawDreamer Plugin Processor wiki page](https://github.com/DBraun/DawDreamer/wiki/Plugin-Processor)
- [fedden/RenderMan repository](https://github.com/fedden/RenderMan)
- [juce-framework/JUCE repository](https://github.com/juce-framework/JUCE) and its `extras/AudioPluginHost` example
- [KVR Audio on Mai Tai/Presence XT availability](https://www.kvraudio.com/forum/viewtopic.php?t=439186)
- T18 (Licensing Requirements) from FOUNDATION.md for the GPL infrastructure carve-out

**Verdict.** Shape 2 is a real, viable, already-proven architecture for the "Houdini-for-music" concept — but it solves a different problem than MusicComposer is supposed to solve. The author's clarified intent ("use the composer workbench to pre-decide, then export to PreSonus for the finishing pass") means what's needed is an *export format for a DAW workflow*, not a render pool that replaces the DAW. Shape 2 is explicitly deferred and may be revisited later as a separate feature (see [Part 13: Future Extensions](#part-13-future-extensions)); it does not belong under the MusicComposer SDK umbrella.

### 2.6 REJECTED: C# VST Hosting (VST.NET, AudioPlugSharp, NPlug)

**What it was.** If a headless VST-hosting render pool was the right shape, building it in C# would match the rest of the Bannou server stack (avoiding the Python/.NET interop seam that DawDreamer introduces). Several C# VST libraries exist: [VST.NET](https://github.com/obiwanjacobi/vst.net), [AudioPlugSharp](https://github.com/mikeoliphant/AudioPlugSharp), [NPlug](https://github.com/xoofx/NPlug).

**What made it attractive.** Native C# means no Python dependency, clean integration with the existing `bannou-service` stack, familiar debugging and profiling tools, and lockstep with the rest of the codebase's .NET runtime.

**What killed it.** Mostly superseded by the rejection of Shape 2 itself, but also: none of the C# libraries are a full replacement for DawDreamer's feature set.

- **VST.NET** supports VST2 only. VST2 is deprecated by Steinberg (SDK removed 2018, existing licenses only for existing VST2 authors). New plugin development is all VST3. Building on VST.NET means building on the dying format.
- **AudioPlugSharp** and **NPlug** are both focused on **building VST3 plugins in C#**, not hosting them. Different polarity from what we'd need.
- **NetVST** is a network-transport wrapper around VST2, also dying with VST2.

**Sources examined.**

- [obiwanjacobi/vst.net repository](https://github.com/obiwanjacobi/vst.net)
- [mikeoliphant/AudioPlugSharp repository](https://github.com/mikeoliphant/AudioPlugSharp)
- [xoofx/NPlug repository](https://github.com/xoofx/NPlug)
- Cantabile community notes on `Jacobi.Vst2.Net` compatibility issues
- [NuGet Gallery VST.NET2-Host 2.1.0](https://www.nuget.org/packages/VST.NET2-Host)

**Verdict.** If Shape 2 ever gets revisited, the worker language is DawDreamer (Python/JUCE, out-of-process) or hand-rolled JUCE (C++, out-of-process). A native-C# host is not available at acceptable quality. Since Shape 2 is out of scope for this SDK, this rejection is mostly pre-emptive.

### 2.7 REJECTED: Pivot to Reaper Instead of Studio One

**What it was.** Reaper (by Cockos) has a fully public scripting API (ReaScript in Lua, EEL, or Python), a real headless render mode, and a thriving third-party extension ecosystem. [YatingMusic/ReaRender](https://github.com/YatingMusic/ReaRender) is a working Python toolkit for automatic MIDI → audio rendering. Reaper also has a community tool ([git-moss/ProjectConverter](https://github.com/git-moss/ProjectConverter)) that converts between DAWproject and Reaper's native format. If the fundamental problem is "my DAW is hostile to automation," the fix could be "use a DAW that isn't."

**What made it attractive.**

- **ReaScript is fully public and supported.** Cockos publishes comprehensive documentation, and the community has built thousands of scripts against it.
- **Reaper has a real headless render mode.** You can batch-render a `.rpp` file from the command line.
- **Licensing is friendly.** Personal/small-commercial Reaper license is $60 one-time.
- **Reaper imports DAWproject via ProjectConverter.** So even Reaper users can still consume DAWproject output — the integration story isn't exclusive.

**What killed it.** Not a technical rejection — a scope rejection. The primary consumer owns a PreSonus Sphere subscription and wants to use it. Asking the consumer to switch DAWs to work around PreSonus's product decisions is not an acceptable answer. Additionally, the consumer's virtual instrument library is tuned to their workflow in Studio One (keyboard shortcuts, plugin-browser organization, mix templates, chord-track macros); asking them to rebuild all of that in a different DAW nets negative productivity.

**Sources examined.**

- [REAPER ReaScript API](https://www.reaper.fm/sdk/reascript/reascript.php)
- [YatingMusic/ReaRender repository](https://github.com/YatingMusic/ReaRender)
- [git-moss/ProjectConverter repository](https://github.com/git-moss/ProjectConverter)
- [X-Raym's ReaScript API Documentation](https://www.extremraym.com/cloud/reascript-doc/)

**Verdict.** If Bannou's primary music consumer were a Reaper user, this shape would be extremely attractive. Since they are a Studio One user, it isn't. The silver lining: DAWproject is Reaper-accessible via ProjectConverter, so our DAWproject output *also* works for Reaper users who show up later — at no additional cost to us.

### 2.8 REJECTED: Pre-Authored Audio Only (No Integration At All)

**What it was.** Accept that Bannou's music system and the DAW workflow are fundamentally disjoint. Continue rendering MIDI-JSON for in-engine adaptive playback. For high-production-value tracks, the author authors in Studio One from scratch, ignores Bannou's music tooling entirely, and uploads the rendered WAV to the Asset service.

**What made it attractive.** Zero new infrastructure. Zero implementation cost. Zero risk of format-compatibility issues.

**What killed it.** It makes all of the Storyteller, Counterpoint, and Dark Revelry investment pointless for any music that actually ships. The Storyteller becomes a curiosity for in-engine procedural music only, never touching the authoritative, human-polished tracks that comprise the bulk of a game's musical identity. This is a tacit admission that Bannou's music system is a research prototype rather than a production tool.

It is also not what the consumer wants. The consumer's explicit request is: use the composer + Storyteller to do as much compositional work as possible up front, then export to Studio One for tweaking. A "no integration" answer rejects that request outright.

**Verdict.** This is the null-hypothesis shape — what Bannou has today. The SDK exists specifically to improve on it.

### 2.9 Decision Summary

| Shape | Verdict | Primary Failure Mode |
|---|---|---|
| 2.1 Studio One as headless server | Rejected | No headless mode exists; UCNet closed; scripting engine is in-process-GUI only |
| 2.2 Studio One internal JavaScript | Rejected | Not remote; not publicly supported; wrong granularity |
| 2.3 PreSonus Plug-In Extensions | Rejected | Wrong direction (plugin-side, not host-side) |
| 2.4 StudioLive hardware API | Rejected | Wrong product (hardware mixer, not DAW) |
| 2.5 Headless VST hosting (DawDreamer) | Rejected | Bypasses PreSonus subscription; VST EULAs; not the Composer layer; quality ceiling |
| 2.6 C# VST hosting libraries | Rejected | Depends on 2.5; VST2 deprecated; C# VST3 hosts lack features |
| 2.7 Pivot to Reaper | Rejected | Consumer owns Studio One; scope violation |
| 2.8 Pre-authored audio only | Rejected | Doesn't improve on current state; wastes Storyteller/Counterpoint investment |
| **DAWproject file exchange** | **Accepted** | See Part 3 |

The accepted design falls out of the rejections. DAWproject is the only shape that:

- Uses Studio One (not bypassing PreSonus subscription)
- Works with PreSonus-native instruments (via embedded state files the user captures)
- Fits the SDK-OVERVIEW Composer pattern (authoring aid, not render pool)
- Has no hostile or undocumented dependencies (MIT-licensed open standard co-developed by PreSonus themselves)
- Requires no Bannou-side infrastructure beyond a C# library that writes XML/ZIP
- Produces XSD-compliant output, so other DAWproject-supporting DAWs (Bitwig, Cubase, Cubasis, VST Live, n-Track) can also open the files in principle — though this is a side effect of format choice, not a Bannou goal, and is explicitly not validated (see [§11.4](#114-daw-target-priorities))

---

## Part 3: The Accepted Design

### 3.1 Thesis — "Do Studio One's Job For It, Up Front"

The design thesis, stated directly by the primary consumer during design: **as much "work" as we can pack into the composer and storyteller to be able to fill in a bunch of blanks on what kind of music we want to make, the easier it should be in PreSonus to get exactly the intended result. We can't use PreSonus like a set of APIs, but we should come as close as we can to doing the majority of its job FOR IT, up-front, so that the actual work in Studio One amounts to tweaking, not even really authoring.**

This reframes the Composer SDK not as an interactive authoring environment (the Counterpoint workbench covers that), nor as a procedural generator (Storyteller covers that), but as a **pre-authoring substrate** — the layer that takes every decision Bannou's music stack can make (compositional, stylistic, sonic, structural) and packs those decisions into a DAW session file so thoroughly that what's left for the human is aesthetic refinement.

### 3.2 What "Doing Studio One's Job" Means Concretely

Enumerating the work a composer normally does in Studio One and mapping each item to "can MusicComposer do this up front?":

| Studio One Task | Can MusicComposer do it? | How |
|---|---|---|
| Set project tempo | Yes | `Transport.Tempo` |
| Set time signature (including mid-piece changes) | Yes | `Transport.TimeSignature` + `Arrangement.TimeSignatureAutomation` |
| Define section structure (Intro, Verse, Chorus...) | Yes | `Arrangement.Markers` with named Marker entries |
| Create tracks | Yes | `Track` entries in `Project.structure` |
| Group tracks into folders (Strings, Brass, Rhythm...) | Yes | Nested `Track.tracks` with `contentType="tracks"` |
| Name tracks and set colors | Yes | `Nameable.name`, `Nameable.color` |
| Load VST3 instrument on a track | Yes | `Channel.devices` with `Vst3Plugin` |
| Set instrument starting patch | Yes (if captured) | Embedded `.vstpreset` file referenced via `Device.state` |
| Load Studio One-native instrument (Mai Tai, Presence XT) | Yes (if captured) | Generic `Device` with Studio One's native state format embedded |
| Place notes on tracks | Yes | `Clip` → `Notes` → `Note` entries |
| Set per-note velocity and release velocity | Yes | `Note.vel`, `Note.rel` |
| Set per-note expression (pitch bend, CC, pressure, timbre) | Yes | Per-note nested `Timeline` with `Points` on `ExpressionType` targets |
| Draw automation on a plugin parameter | Yes (if parameter IDs are known) | `Points` with `AutomationTarget.parameter` IDREF + `RealPoint` sequence |
| Draw MIDI CC automation (CC1 mod wheel, CC11 expression, etc.) | Yes | `Points` with `AutomationTarget.expression = CHANNEL_CONTROLLER`, `controller = N` |
| Draw pitch bend automation | Yes | `Points` with `AutomationTarget.expression = PITCH_BEND` |
| Set channel volume / pan / mute / solo | Yes | `Channel.volume`, `Channel.pan`, `Channel.mute`, `Channel.solo` |
| Wire aux sends to an FX bus | Yes | `Channel.sends` with `Send.destination` IDREF + level |
| Route tracks through submix groups to master | Yes | `Channel.destination` IDREF + `MixerRole.SUBMIX` / `MASTER` |
| Put an EQ on a track or bus | Yes | Built-in `Equalizer` device with `Band` entries |
| Put a compressor on the master bus | Yes | Built-in `Compressor` device |
| Put a limiter on the master bus | Yes | Built-in `Limiter` device |
| Put a gate on a track | Yes | Built-in `NoiseGate` device |
| Set tempo ramps (rit./accel.) | Yes | `Arrangement.TempoAutomation` with `RealPoint` sequence |
| Set clip fade-in / fade-out | Yes | `Clip.fadeInTime`, `Clip.fadeOutTime` |
| Create alias clips (shared content across multiple positions) | Yes | `Clip.reference` IDREF |
| Add annotations / comments | Yes | `Nameable.comment` on any entity; also `Marker` names |
| Set key signature | Partial | Embed in a Marker name at section boundaries; Studio One also infers from notes |
| Choose instrument articulation / key-switch patch | Partial | Expressible via embedded preset (if the user captured a patch with that articulation) or via MIDI program change events, but not through a dedicated "articulation" element |
| Select a different preset within a loaded plugin | No | The user picks in the plugin's GUI |
| Tweak mix balance between tracks | No (seed only) | MusicComposer sets a style-appropriate starting balance; user refines by ear |
| Add character processing (tape saturation, space designer reverbs, exciters) | No | User adds their own |
| Add vocals / foley / sound-design elements | No | Not compositional |
| Export to WAV / FLAC / OGG | No | User's export step |

The vast majority of Studio One's authoring work is in the "Yes" column. The "No" column is primarily aesthetic refinement and user-preference choices that should be left to the human.

### 3.3 Three-Tier Plugin State Strategy

Plugin state embedding is the mechanism that pushes "Yes (if captured)" rows into fully-resolved "Yes" rows. The SDK supports three plugin-state tiers, corresponding to how much of the sonic identity the user has captured:

**Tier 1 — Reference Only.** `Vst3Plugin` element with `deviceID` (VST3 UUID) + `deviceName`, no embedded state. Studio One opens the project, inserts the named plugin on the track, and the plugin loads in its factory-default state. The user picks a patch in the plugin's GUI.

```xml
<Vst3Plugin deviceID="84e8de5f-9255-4be0-8b99-3da3c51b3e04"
            deviceName="Kontakt 7" deviceRole="instrument" loaded="true"/>
```

**Use when:** the user hasn't registered a specific patch for this role, or the composition uses a plugin the user owns but for which no patch is curated yet. Works 100% reliably for any installed plugin.

**Tier 2 — Reference + Embedded Preset.** `Vst3Plugin` element plus a `.vstpreset` file embedded in the DAWproject ZIP, referenced by `Device.state.path`. Studio One loads the plugin, then loads the preset state into it — sample library loaded, patch selected, filter cutoff dialed in, effects configured. User doesn't touch the instrument at all.

```xml
<Vst3Plugin deviceID="84e8de5f-..." deviceName="Kontakt 7" deviceRole="instrument">
  <State path="plugins/kontakt-spitfire-albion-legato-warm-01.vstpreset"/>
</Vst3Plugin>
```

**Use when:** the user has previously captured this patch via the Plugin Library service and tagged it for this role. This is the target tier for the maturity phase of the system — once the user has built a library of 20–40 curated patches, most compositions can be authored with Tier 2 state throughout.

**Tier 3 — Studio One-Native Device + Captured State.** Generic `Device` element referencing a Studio One-proprietary instrument (Mai Tai, Presence XT, etc.) with a Studio One-native state file embedded. Same mechanism as Tier 2, different state format.

```xml
<Device deviceName="Mai Tai" deviceRole="instrument">
  <State path="plugins/mai-tai-dark-revelry-lead-01.preset"/>
</Device>
```

**Use when:** the user's composition depends on a PreSonus-native instrument (which cannot be referenced as a VST3 since they don't ship as standalone VST3). The user captures the patch from Studio One once; MusicComposer embeds it in subsequent projects. This is how MusicComposer takes full advantage of the PreSonus Sphere subscription.

The three tiers degrade gracefully, and degradation happens **along a reverse-DNS hierarchy** rather than as a single-level on/off. A role like `strings.orchestral.violin.legato.warm` first looks for an exact match in the Plugin Library (best case: Tier 2 or Tier 3 with captured state). If none, it walks up — `strings.orchestral.violin.legato`, then `strings.orchestral.violin`, then `strings.orchestral`, then `strings` — until it finds a registered template at some level of specificity. Only if no ancestor matches does it fall to Tier 1, and Tier 1 itself uses a Plugin Family Registry (see [§6.3](#63-plugin-family-registry)) to pick a plausible family-capable plugin to reference. See [§6.2 Role Taxonomy](#62-role-taxonomy) for the hierarchy design and [§6.8 Resolution Semantics](#68-resolution-semantics) for the full scoring algorithm.

The SDK never fails to produce a valid `.dawproject` — worst case, Studio One opens the file with an empty instrument slot on some tracks, but the MIDI, automation, and structure are all intact, and the user can load an instrument manually.

### 3.4 The Capture-Once Plugin Library Pattern

The Plugin Library service is the persistent registry of captured plugin states. It turns a one-time curation act into a permanent reusable asset. The primary capture path is **bulk import** — a single command that reads an entire directory tree of previously-saved plugin presets, infers the role slug for each from the directory hierarchy, and registers everything in one transaction. One-at-a-time registration exists as a secondary path for capturing single new patches without re-running a bulk import, and a third path imports templates by extracting every plugin instance from an existing `.dawproject` file.

**Phase 1 — Organize and export.** The user saves presets from Studio One using the plugin's native preset-export function (VST3: `.vstpreset`; Studio One-native: `.preset`). They organize those files into a directory hierarchy that mirrors the Bannou role taxonomy (see [§6.2](#62-role-taxonomy)):

```
~/MusicLibrary/
├── Kontakt/
│   └── strings/
│       └── orchestral/
│           └── violin/
│               ├── legato-warm.vstpreset
│               └── spiccato-sharp.vstpreset
└── Omnisphere/
    └── pad/
        ├── dark-enveloping.vstpreset
        └── bright-ethereal.vstpreset
```

The top-level directory after the library root is the plugin name; each subsequent nested directory is one segment of the role slug. The filename (minus extension) is the final segment. Many composers already organize their saved presets this way for their own retrieval; the bulk-import workflow just formalizes the convention.

**Phase 2 — Bulk register.** A single command walks the tree and registers everything:

```bash
bannou music-library bulk-import --dir ~/MusicLibrary --game-service-id {game}
```

Bannou reads each preset file, extracts plugin identity (VST3 UUID is embedded in the preset format; for other formats Bannou consults its plugin-family registry to identify the plugin by name), infers the role slug from the directory path as `{family}.{subfamily}.{voice}.{articulation}.{modifier}` (dot-separated, reverse-DNS style), and creates all the `PluginLibraryTemplate` records in a single transaction. A dry-run mode previews the inferred slugs before committing. See [§6.7 Capture Workflow](#67-capture-workflow) for the full command specification and optional sidecar overrides.

**Phase 3 — Selection.** When MusicComposer builds a `.dawproject` for a composition, its `ProjectBuilder` resolves each required track role (`strings.orchestral.violin.legato.warm`, etc.) to a specific template by walking the hierarchy. Exact matches win; if nothing matches at the most-specific level, the resolver drops segments from the right and tries again at each level until something matches or the family segment is exhausted. See [§6.8 Resolution Semantics](#68-resolution-semantics) for the full scoring algorithm.

**Phase 4 — Storage.** Bannou stores every captured file in the Asset service (binary content) and records the metadata in the Plugin Library state store (`plugin-library-templates`, MySQL). The `PluginLibraryTemplate` record captures plugin identity (VST3 UUID + name + vendor, or native plugin name for Tier 3), the reverse-DNS role slug, optional stylistic tags (orthogonal dimension: `[dark-revelry, cinematic]`), capability flags (MPE, key switches, responsive CCs), and the embedded-file asset reference.

Over time, the user's library grows. A typical mature library has 40–100 templates covering common ensemble needs across several stylistic profiles. The hierarchical fallback means even roles that don't have exact matches still resolve — a request for `strings.orchestral.violin.legato.warm` against a library with only `strings.orchestral.violin.legato` registered still works, just with less specificity.

### 3.5 MIDI + Expression + Automation Maximalism

DAWproject's MIDI model is notably richer than a Standard MIDI File (SMF) — particularly in two dimensions:

**Per-note expression timelines.** Each `Note` element can contain a nested `Timeline` of automation points scoped to that specific note. This enables MPE-style per-note expression: a sustained C4 note can have its own pitch-bend curve (slowly drifting slightly sharp then settling), its own pressure curve (a gradual crescendo of expression), and its own timbre curve (opening filter from dark to bright). SMF cannot express any of this per-note — MPE in SMF is a polyphonic channel-allocation trick that most DAWs don't round-trip cleanly. DAWproject native-supports it.

**Rich automation targets.** A `Points` timeline can target any `Parameter` by IDREF (any VST parameter, built-in-device parameter, channel parameter) OR one of 11 `ExpressionType` enum values (gain, pan, transpose, timbre, formant, pressure, channelController, channelPressure, polyPressure, pitchBend, programChange). The `channelController` case takes a controller number attribute, so CC1 (mod wheel), CC11 (expression), CC64 (sustain), CC71–74 (sound controllers) are all expressible natively.

MusicComposer must exploit this maximalism. A Storyteller-generated composition feeds six emotional dimensions through every bar of the piece; each dimension has natural DAW expressions:

| Emotional dimension | Primary automation target |
|---|---|
| Tension | CC11 (expression), channel volume, reverb send level |
| Brightness | CC74 (sound controller 5 — commonly "brightness"), EQ high-shelf gain |
| Energy | Velocity baseline, rhythmic density (note count), tempo micro-nudge |
| Warmth | Reverb send level, EQ low-shelf gain, filter resonance |
| Stability | Pan automation smoothness, tempo rigidity, less rubato |
| Valence | Mode choice (already upstream), major-chord probability (upstream), CC1 vibrato depth |

The Storyteller's per-phase emotional state becomes a RealPoint-sequence on each of these targets across the composition's arrangement. The user opens Studio One and hears volume breathing into the chorus, the reverb tail opening during the bridge, mod-wheel vibrato subtly engaging on sustained lead notes during high-warmth sections — not because Storyteller is running at playback time, but because the expressive arcs it designed are encoded into the session's automation.

### 3.6 Mix-Bus Pre-Wiring Via Built-In Devices

DAWproject defines four **built-in devices** — Equalizer, Compressor, NoiseGate, Limiter — that every supporting DAW guarantees to render identically because each is a formal specification rather than a reference to a proprietary plugin. MusicComposer uses these to ship a **pre-wired mix bus** with every project:

- **Master bus.** `MixerRole.MASTER` channel with an `Equalizer` (style-tuned curve — dark-revelry gets a slight 6 kHz tilt for theatrical air, celtic gets a warmer 2 kHz bump), a `Compressor` with gentle bus-glue settings (ratio 2:1, threshold -12 dB, auto-makeup), and a `Limiter` at -0.3 dB ceiling as a safety brick wall. The user hears mastered output on open instead of raw stems.
- **Reverb return.** `MixerRole.EFFECT` channel containing a VST3 reverb (user-library-captured — typically something like Valhalla Vintage Verb, Lexicon, or a Kontakt convolution reverb) with an embedded preset tuned to the style. Every instrument track has a `Send` routing to this return with a role-appropriate level.
- **Submix groups.** For style-appropriate instrument groupings (Rhythm Section, Harmonic Core, Melodic, Color/Flavor), `MixerRole.SUBMIX` channels that route to Master. Lets the user mix in groups (pull down all strings together, for example) without rebuilding routing.

None of this requires VSTs the user doesn't own — the EQ/Compressor/NoiseGate/Limiter are part of the DAWproject specification itself. The reverb return is the one Tier-2 dependency; if the user hasn't captured a reverb preset yet, MusicComposer falls back to a default `Limiter` on the master only (no reverb return) and skips the sends.

### 3.7 The Counterpoint Relationship

[COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md) describes a structural-template workbench — composers define templates, validate compositions against them, check counterpoint compatibility between pieces at designed temporal offsets, get suggestions for consonant notes. That document's scope is the *interactive authoring experience* for the structural template metaphor.

MusicComposer (this document) does not absorb or replace Counterpoint. Its relationship to Counterpoint is *peer*: Counterpoint uses MusicComposer as its export substrate, the same way a Storyteller procedural generation would. Both produce the same class of artifact — a Bannou composition with tracks, MIDI, automation, and plugin-library-resolved instruments — and both publish through the MusicComposer ProjectBuilder.

```
                  Counterpoint Workbench           MusicStoryteller
                  (human authoring, structural      (procedural GOAP,
                   template validation, offset       emotional state
                   compatibility checking)           planning)
                              ↓                              ↓
                              + both produce the same CompositionPlan(s) →
                              ↓
                     MusicComposer ProjectBuilder
                              ↓
               .dawproject file(s) + (for counterpoint) cross-reference markers
                              ↓
        (counterpoint only) Rendered stems → /music/counterpoint/mixdown → combined WAV
```

The question "what does the Counterpoint workbench's Save button do?" is answered by "it calls the MusicComposer ProjectBuilder with a CounterpointSetPlan and writes N companion files." The question "what does `POST /music/export/dawproject` do?" is answered by "it calls the MusicComposer ProjectBuilder with a single CompositionPlan and writes one file." Both paths use the same underlying DOM and writer — they differ only in how many output files they produce and whether cross-reference metadata is embedded.

**Counterpoint exports as N companion files, always.** A counterpoint set with pieces A and B produces `{base-name}.a.dawproject` and `{base-name}.b.dawproject` — two standalone files, each a complete Bannou composition, linked via a counterpoint cross-reference marker at tick 0. Single-file emission was considered and rejected ([§11.2](#112-counterpoint-output-shape-same-file-or-companion-files)) because companion files match the philosophical framing ("each piece is a full standalone work"), scale naturally to 3+ pieces, and the auditioning step they give up is recovered by the mixdown verification tool. See [§7.10](#710-counterpoint-companion-emission-and-verification) for the companion-emission spec, cross-reference marker format, and mixdown tool.

### 3.8 Non-Goals

Explicitly outside the scope of MusicComposer as designed here:

- **Not a render pool.** No VST hosting. No audio synthesis. No WAV output. See [Shape 2 rejection in Part 2.5](#25-rejected-headless-vst-hosting-via-dawdreamer-or-juce-shape-2).
- **Not a DAW.** No interactive timeline editing, no piano-roll UI, no mixer GUI. The user's DAW is the DAW.
- **Not a Studio One controller.** No attempt to script, remote-control, or hook into Studio One. The integration surface is the file format the user opens.
- **Not a full Counterpoint workbench.** Counterpoint's interactive authoring features (template-driven composition aid, offset compatibility checking, consonant-note suggestions) are in their own SDK. MusicComposer only provides the export substrate.
- **Not DAWproject-validator.** The DAWproject reference library (Bitwig's Java implementation) validates DAWproject files against the XSD. Bannou uses the same validation strategy but does not publish a validator — we consume the reference XSD.
- **Not a music-authoring UI.** The Plugin Library capture workflow has an API surface; any UI on top of that API is outside this SDK's scope.

---

## Part 4: Architecture

### 4.1 SDK Layering

MusicComposer sits between the existing music SDKs and the DAWproject format. It consumes compositional output from MusicTheory + MusicStoryteller (procedural) or the Counterpoint workbench (human authoring) and produces `.dawproject` file bytes. It introduces no new runtime services but does require one new state-store-backed capability (the Plugin Library) which is surfaced as lib-music endpoints.

```
┌──────────────────────────────────────────────────────────────────────────┐
│                      MusicComposer SDK (new — this document)              │
│                                                                            │
│  ┌──────────────────────┐         ┌────────────────────────────────────┐  │
│  │  DawProject DOM      │         │  ProjectBuilder                     │  │
│  │  (C# port of         │         │  (CompositionPlan + PluginLibrary  │  │
│  │   bitwig/dawproject  │────────→│   + Style → Project DOM)           │  │
│  │   Java model)        │         │                                     │  │
│  └──────────────────────┘         └────────────────────────────────────┘  │
│           ↓                                        ↓                        │
│  ┌──────────────────────┐         ┌────────────────────────────────────┐  │
│  │  DawProjectWriter    │         │  CompositionPlan                    │  │
│  │  (XML + ZIP + state  │         │  (domain-neutral compositional     │  │
│  │   file embedding)    │         │   intermediate — tracks, notes,    │  │
│  │                      │         │   automation, plugin role reqs)    │  │
│  └──────────────────────┘         └────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
             ↑                                      ↑
             │                                      │
┌────────────┴────────────┐        ┌────────────────┴──────────────────────┐
│ MusicTheory (existing)  │        │ MusicStoryteller (existing)            │
│ Pitch, Scale, Chord,    │──────→│ CompositionIntent, EmotionalState,    │
│ VoiceLeader, Melody-    │        │ NarrativeTemplate, GOAP actions       │
│ Generator, MidiJson     │        │                                        │
└─────────────────────────┘        └────────────────────────────────────────┘
                                                    ↑
                                     ┌──────────────┴───────────────────┐
                                     │ Counterpoint Workbench           │
                                     │ (sibling planning doc — peer,   │
                                     │  not descendant of MusicComposer)│
                                     └──────────────────────────────────┘

                                                    ↓↓
                                    ┌──────────────────────────────┐
                                    │ lib-music plugin (L4)         │
                                    │ + /music/export/dawproject    │
                                    │ + /music/plugin-library/*     │
                                    └──────────────────────────────┘
                                                    ↓
                                              .dawproject bytes
                                                    ↓
                                  Studio One / Bitwig / Cubase / etc.
```

### 4.2 Package Identity

| Property | Value |
|----------|-------|
| Directory | `sdks/music-composer/` |
| PackageId | `BeyondImmersion.Bannou.MusicComposer` |
| RootNamespace | `BeyondImmersion.Bannou.MusicComposer` |
| AssemblyName | `BeyondImmersion.Bannou.MusicComposer` |
| Target | `net8.0` |
| Dependencies | `BeyondImmersion.Bannou.MusicTheory`, `BeyondImmersion.Bannou.MusicStoryteller`, `BeyondImmersion.Bannou.Core` (for `BannouJson`) |

The SDK ships as a NuGet package alongside the other creative-domain SDKs (`music-theory`, `music-storyteller`, `storyline-theory`, `storyline-storyteller`, `scene-composer`, `sprite-theory`, `sprite-composer`). Unified version number via `sdks/SDK_VERSION`.

### 4.3 Project Structure

```
sdks/music-composer/
├── Dom/                                        # C# port of bitwig/dawproject Java model
│   ├── Project.cs                              # Root: Application, Transport, Structure, Arrangement, Scenes
│   ├── Application.cs                          # Name, version
│   ├── Transport.cs                            # Tempo, TimeSignature
│   ├── Nameable.cs / Referenceable.cs / Lane.cs  # Base types
│   ├── Parameter.cs / RealParameter.cs / BoolParameter.cs /
│   │   IntegerParameter.cs / EnumParameter.cs / TimeSignatureParameter.cs
│   ├── Track.cs                                # contentType, Channel, nested tracks
│   ├── Channel.cs                              # Volume, Pan, Mute, Sends, Devices, destination, role
│   ├── Send.cs                                 # Volume, Pan, Enable, type, destination
│   ├── Arrangement.cs                          # Lanes, Markers, TempoAutomation, TimeSignatureAutomation
│   ├── Scene.cs                                # Clip launcher scenes (future — rarely used)
│   ├── FileReference.cs                        # path, external flag
│   ├── Enums/
│   │   ├── Unit.cs                             # linear, normalized, percent, decibel, hertz, semitones, seconds, beats, bpm
│   │   ├── TimeUnit.cs                         # beats, seconds
│   │   ├── MixerRole.cs                        # regular, master, effect, submix, vca
│   │   ├── SendType.cs                         # pre, post
│   │   ├── ContentType.cs                      # audio, automation, notes, video, markers, tracks
│   │   ├── Interpolation.cs                    # hold, linear
│   │   ├── ExpressionType.cs                   # gain, pan, transpose, timbre, formant, pressure, channelController, channelPressure, polyPressure, pitchBend, programChange
│   │   ├── DeviceRole.cs                       # instrument, noteFX, audioFX, analyzer
│   │   └── EqBandType.cs                       # highPass, lowPass, bandPass, highShelf, lowShelf, bell, notch
│   ├── Device/
│   │   ├── Device.cs                           # Base: deviceName, deviceRole, deviceID, deviceVendor, state, automatedParameters
│   │   ├── Plugin.cs                           # pluginVersion
│   │   ├── Vst2Plugin.cs
│   │   ├── Vst3Plugin.cs
│   │   ├── ClapPlugin.cs
│   │   ├── AuPlugin.cs
│   │   ├── BuiltinDevice.cs                    # DAW-native (e.g., Studio One Mai Tai when stored via Device element)
│   │   ├── Equalizer.cs                        # Band[], InputGain, OutputGain
│   │   ├── EqBand.cs                           # Freq, Gain, Q, Enabled, type, order
│   │   ├── Compressor.cs                       # Attack, AutoMakeup, InputGain, OutputGain, Ratio, Release, Threshold
│   │   ├── NoiseGate.cs                        # Attack, Range, Ratio, Release, Threshold
│   │   └── Limiter.cs                          # Attack, InputGain, OutputGain, Release, Threshold
│   └── Timeline/
│       ├── Timeline.cs                         # Base: track IDREF, timeUnit
│       ├── Lanes.cs                            # Nested timelines, main layering element
│       ├── Clip.cs                             # time, duration, playStart, fades, content or reference
│       ├── Clips.cs                            # Collection of Clip
│       ├── ClipSlot.cs                         # Clip launcher slot (future)
│       ├── Notes.cs                            # Collection of Note
│       ├── Note.cs                             # time, duration, channel, key, vel, rel, per-note content timeline
│       ├── Marker.cs / Markers.cs              # Cue markers
│       ├── Warps.cs / Warp.cs                  # Audio time-stretching
│       ├── Audio.cs / MediaFile.cs             # Audio clips referencing embedded WAV files
│       ├── Video.cs                            # Not used by MusicComposer but required for DOM completeness
│       ├── Points.cs                           # Automation timeline (target + points + unit)
│       ├── Point.cs / RealPoint.cs / EnumPoint.cs / BoolPoint.cs / IntegerPoint.cs /
│       │   TimeSignaturePoint.cs
│       └── AutomationTarget.cs                 # parameter IDREF OR expression enum + channel/key/controller
│
├── Plan/                                       # Domain-neutral intermediate representation
│   ├── CompositionPlan.cs                      # Root: tempo, meter, key, sections, tracks, master-bus-config
│   ├── TrackPlan.cs                            # role, pluginRoleRequirement, notes, expression, automation
│   ├── NotePlan.cs                             # time, duration, key, velocity, release, perNoteExpression
│   ├── PerNoteExpression.cs                    # pitchBendCurve, pressureCurve, timbreCurve, ccOverrides
│   ├── AutomationPlan.cs                       # target (param IDREF slug OR expression enum + CC#), points
│   ├── SectionPlan.cs                          # name, color, startBar, endBar, emotionalState, chordProgression
│   ├── SendPlan.cs                             # destinationRole, level, pre/post
│   ├── PluginRoleRequirement.cs                # role slug + fallback policy + stylistic hints
│   └── MasterBusConfig.cs                      # EQ curve, compressor settings, limiter ceiling, reverb return config
│
├── Build/                                      # ProjectBuilder pipeline
│   ├── ProjectBuilder.cs                       # CompositionPlan + PluginLibrary + Style → Project DOM
│   ├── StorytellerAdapter.cs                   # MusicStoryteller CompositionResult → CompositionPlan
│   ├── CounterpointAdapter.cs                  # Counterpoint workbench output → CompositionPlan (when Counterpoint lands)
│   ├── PluginResolver.cs                       # PluginRoleRequirement + Style → PluginLibraryTemplate
│   ├── EmotionToAutomation.cs                  # EmotionalState curve → AutomationPlan entries (CC11, volume, reverb send, etc.)
│   ├── StyleApplication.cs                     # Style → mix-bus defaults, articulation probabilities, default track set
│   └── IdAllocator.cs                          # Stable ID generation for XML id/IDREF references
│
├── Io/                                         # Serialization and file I/O
│   ├── DawProjectWriter.cs                     # Project DOM + embedded files → ZIP bytes
│   ├── DawProjectReader.cs                     # ZIP bytes → Project DOM (for round-trip validation)
│   ├── XmlSerialization.cs                     # System.Xml.Serialization annotations + marshaling
│   └── MetaData.cs                             # Title, Artist, Album, Composer, Year, Genre, Copyright, Comment, etc.
│
├── Library/                                    # Plugin Library client + types
│   ├── PluginLibraryTemplate.cs                # role, pluginIdentity, stateAssetId, stylisticTags
│   ├── PluginLibraryClient.cs                  # Client for /music/plugin-library/* endpoints
│   ├── PluginIdentity.cs                       # VST3 UUID OR native plugin name + vendor
│   └── CaptureRequest.cs                       # POST body shape for registering a new template
│
├── MusicComposer.cs                            # Main entry point / facade
└── music-composer.csproj
```

### 4.4 Data Flow

The end-to-end data flow from a composition request to a `.dawproject` file:

```
  Client request: POST /music/export/dawproject
  Body: GenerateCompositionRequest + exportOptions (style, pluginLibraryProfileId, etc.)
          │
          ▼
  lib-music plugin
    1. Run existing /music/generate pipeline (MusicStoryteller + MusicTheory)
    2. Produce CompositionResult (MIDI-JSON + emotional journey + metadata)
          │
          ▼
  StorytellerAdapter.ToCompositionPlan(result, style)
    — Map tracks, notes, sections, emotional state → CompositionPlan
    — Designate plugin role requirements per track
    — Compute master-bus-config from style
          │
          ▼
  PluginResolver.ResolveAll(plan.PluginRoleRequirements, pluginLibraryProfileId)
    — For each role: query /music/plugin-library/resolve?role=X&styleId=Y
    — Receive PluginLibraryTemplate (including state file Asset ID) or null (falls back to Tier 1)
          │
          ▼
  ProjectBuilder.Build(plan, resolvedTemplates, style)
    — Construct Project DOM: Application, Transport, Structure (tracks),
      Arrangement (lanes, markers, tempo/sig automation), Scenes
    — Populate channels with Devices (resolved plugins, built-in mix devices)
    — Populate clips with Notes and per-note expression Timelines
    — Emit AutomationPlan entries as Points timelines
    — Derive master-bus device chain from MasterBusConfig
          │
          ▼
  DawProjectWriter.Write(project, metadata, embeddedFiles, stream)
    — Fetch embedded state files from Asset service (by asset ID)
    — Serialize Project DOM to project.xml
    — Serialize MetaData to metadata.xml
    — ZIP everything together with a deterministic path layout
          │
          ▼
  Response: application/octet-stream (dawproject bytes)
  (Optionally also: POST to Asset service for persistence, return asset ID)
```

The adapter and builder layers are pure computation. The writer fetches state files from Asset service (I/O) and produces bytes. lib-music orchestrates but adds no new business logic beyond the endpoint wiring.

### 4.5 Relationship to lib-music

lib-music gains new endpoints but otherwise is unchanged. The existing `/music/generate` endpoint continues to produce MIDI-JSON for in-engine adaptive runtime consumers. The new endpoints are additive:

| New endpoint | Permissions | Purpose |
|---|---|---|
| `POST /music/export/dawproject` | `role: developer` | Full composition export — same parameters as `/music/generate` plus `pluginLibraryProfileId` and `dawprojectOptions`; returns `.dawproject` bytes |
| `POST /music/plugin-library/register` | `role: developer` | Register a captured plugin state file. Uploads binary to Asset service, creates `PluginLibraryTemplate` record |
| `POST /music/plugin-library/get` | `role: developer` | Retrieve a registered template by ID |
| `POST /music/plugin-library/list` | `role: developer` | Paged list with optional role/style/plugin-id filters |
| `POST /music/plugin-library/resolve` | `role: developer` | Internal endpoint: given a role slug + style + optional profile, return the best-matching template (used by ProjectBuilder) |
| `POST /music/plugin-library/deprecate` | `role: developer` | Category B deprecation (template persists forever; new resolutions stop returning it) |
| `POST /music/plugin-library/clean-deprecated` | `role: admin` | Category B cleanup sweep |
| `POST /music/counterpoint/mixdown` | `role: developer` | Verification tool for counterpoint companion files. Takes rendered audio stems + companion `.dawproject` files, aligns the stems at the designed offsets from each file's counterpoint cross-reference marker, sums them sample-by-sample, normalizes, and returns a combined WAV. See [§7.10](#710-counterpoint-companion-emission-and-verification). |

See [Part 6](#part-6-the-plugin-library-service) for the full Plugin Library specification. See [§7.10](#710-counterpoint-companion-emission-and-verification) for the counterpoint companion emission and mixdown specification. See [Part 10](#part-10-implementation-phases) for the phase ordering (the SDK lands before lib-music gains these endpoints).

### 4.6 File Format Invariants

Every Bannou-emitted `.dawproject` file observes these invariants, which ProjectBuilder and DawProjectWriter enforce:

| Invariant | Why |
|---|---|
| Container is a ZIP with `project.xml` + `metadata.xml` at the root | DAWproject spec requires this |
| XML files are UTF-8 encoded, no BOM, LF line endings | DAWproject spec recommends; consistent diff-ability |
| Embedded state files live under `plugins/` with deterministic filenames | Deterministic file layout makes round-trip diffs readable |
| Embedded audio files (future) live under `audio/` | Matches Bitwig and Studio One convention |
| All XML IDs follow the pattern `id{N}` where N is a monotonically-increasing integer | Matches the Bitwig reference output; consumers that parse by sight find it readable |
| Project versioning uses DAWproject `version="1.0"` | Current stable version |
| `Application.name = "Bannou"`, `Application.version = {SDK version from SDK_VERSION}` | Diagnostics and bug reports can identify the producer version |
| MetaData block is always present with at least `Generator = "BeyondImmersion.Bannou.MusicComposer"` | Traceability |
| All `Track` entries have `loaded="true"` | Unloaded tracks are for DAWs that can't find referenced plugins; MusicComposer never emits unloaded |
| Every `Track` has a `Channel` with at least `Mute`, `Pan`, `Volume` parameters and a `destination` IDREF (except Master) | Mixer routing is always explicit, never implicit |
| `Arrangement.lanes.timeUnit = "beats"` at the root | Musical-time scope throughout; audio clips may use `seconds` internally |
| Tempo always has a baseline `Transport.Tempo` value | Even when `Arrangement.TempoAutomation` is present, the baseline value is the unautomated default |

### 4.7 Determinism

Same inputs → identical output bytes. The ProjectBuilder is a pure function of (CompositionPlan, resolved PluginLibraryTemplate set, Style, SDK version). IdAllocator assigns IDs in a deterministic order. DawProjectWriter serializes XML with ordered attributes and elements. The ZIP is assembled with entries in sorted order with `ZipEntry.DateTime = 1980-01-01 00:00:00` (the DOS epoch, the conventional "null timestamp" for reproducible builds).

This matters because the DAWproject output is an artifact Bannou can Redis-cache keyed by input hash — exactly like lib-music already caches MIDI-JSON. A second request with the same seed returns the same bytes in a single round-trip.

### 4.8 Layer Placement

This SDK does not define a new plugin. It adds endpoints to the existing `lib-music` plugin (L4 Game Features), following the same pattern as MusicTheory and MusicStoryteller. Service layer hierarchy (per T2) places this at L4 — the existing layer.

The Plugin Library service surface is also part of `lib-music`. This is a deliberate colocation: the plugin library is tightly coupled to MusicComposer's ProjectBuilder, and splitting it into a separate plugin would create an L4-to-L4 dependency that buys nothing. A future refactor could extract the Plugin Library into its own L4 plugin if other music-adjacent SDKs need it (e.g., a future MusicRenderer plugin for Shape 2), but that's speculation; this design ships them together.

---

## Part 5: DAWproject Output Specification

This section specifies the canonical shape of every `.dawproject` file MusicComposer emits. The ProjectBuilder constructs this structure from a `CompositionPlan`; the DawProjectWriter serializes it to bytes.

### 5.1 Container Layout

```
some-composition.dawproject (ZIP container)
├── project.xml           (UTF-8 XML, no BOM, LF line endings)
├── metadata.xml          (UTF-8 XML, no BOM, LF line endings)
└── plugins/
    ├── kontakt-spitfire-albion-legato-warm.vstpreset
    ├── serum-dark-revelry-theatrical-lead.vstpreset
    ├── mai-tai-dark-revelry-bass-01.preset
    ├── valhalla-vintage-verb-large-hall.vstpreset
    └── ...
```

`plugins/` contains all embedded state files. Filenames are derived from the `PluginLibraryTemplate.slug` plus the source file extension, with path characters sanitized. Deterministic file order is alphabetical within `plugins/` with `metadata.xml` and `project.xml` in fixed positions.

### 5.2 Transport, Markers, and Arrangement Skeleton

Every composition receives the same arrangement skeleton:

```xml
<Project version="1.0">
  <Application name="Bannou" version="{sdkVersion}"/>
  <Transport>
    <Tempo max="300.000000" min="20.000000" unit="bpm" value="{style.defaultTempo}" id="id0" name="Tempo"/>
    <TimeSignature denominator="{style.defaultMeter.denominator}" numerator="{style.defaultMeter.numerator}" id="id1"/>
  </Transport>
  <Structure>
    <!-- tracks, channels (see §5.3) -->
  </Structure>
  <Arrangement id="id_arrangement">
    <Lanes timeUnit="beats" id="id_arrangement_lanes">
      <!-- per-track Lanes (see §5.4) -->
    </Lanes>
    <Markers id="id_markers">
      <!-- section markers (see §5.2.2) -->
    </Markers>
    <TempoAutomation id="id_tempo_auto">
      <!-- if style has tempo changes (see §5.2.3) -->
    </TempoAutomation>
    <TimeSignatureAutomation id="id_ts_auto">
      <!-- if composition has meter changes -->
    </TimeSignatureAutomation>
  </Arrangement>
  <Scenes/>  <!-- Empty by default; Scene/clip launcher support deferred -->
</Project>
```

#### 5.2.1 Tempo baseline

`Transport.Tempo.value` is the style's default tempo, or the composition's requested tempo if overridden. Min/max bounds follow DAWproject convention (20 - 300 BPM).

#### 5.2.2 Section markers

Every section boundary from the CompositionPlan produces a `Marker` entry. Names follow the pattern `{section-name}` for neutral names or `{section-name} ({phase-detail})` when Storyteller has additional structural annotation to communicate:

```xml
<Markers id="id_markers">
  <Marker time="0.0"   name="Bb Minor (Harmonic)" color="#888888" id="id_m0"/>
  <Marker time="0.0"   name="Intro"                 color="#7a9cc6" id="id_m1"/>
  <Marker time="16.0"  name="Verse 1"               color="#9cc67a" id="id_m2"/>
  <Marker time="48.0"  name="PreChorus"             color="#c6b07a" id="id_m3"/>
  <Marker time="64.0"  name="Chorus 1"              color="#c67a7a" id="id_m4"/>
  <Marker time="96.0"  name="Verse 2"               color="#9cc67a" id="id_m5"/>
  <Marker time="128.0" name="Chorus 2"              color="#c67a7a" id="id_m6"/>
  <Marker time="160.0" name="Bridge"                color="#7a7ac6" id="id_m7"/>
  <Marker time="176.0" name="Final Chorus"          color="#c67a7a" id="id_m8"/>
  <Marker time="208.0" name="Outro"                 color="#7a9cc6" id="id_m9"/>
</Markers>
```

**Key signature marker convention.** The composition's key is encoded as the first marker at `time=0`. Its name is `{Tonic} {Mode}` (e.g., "Bb Minor", "G Major", "D Dorian") plus parenthetical mode qualifier when the mode isn't one of {Major, Minor} (e.g., "Bb Minor (Harmonic)" for harmonic minor). DAWproject has no first-class key signature element — this marker serves the dual purpose of communicating the key to humans reading the timeline and giving Storyteller a known place to stash mode information that round-trips cleanly.

**Modulation markers.** When the composition modulates (changes key) at a specific bar, an additional marker is placed at that bar with the new key name.

**Chord symbol markers.** Optional. When ProjectBuilder is configured with `dawprojectOptions.annotateChords = true`, it emits an additional marker at every chord change containing the roman numeral + chord symbol (e.g., `iv (Ebm)`). This produces a busy timeline but gives the composer a visible chord track. Off by default — Studio One's proprietary Chord Track is a better home for this when available, and markers get cluttered fast.

**Counterpoint cross-reference markers.** When MusicComposer emits a counterpoint companion file (one of N `.dawproject` files produced from a Counterpoint workbench export), a special marker at `time=0` carries the cross-reference metadata for the mixdown verification tool. The marker's name uses a structured serialization that `DawProjectReader` parses at verification time. Color is `#5b7a5b` (a muted green) to visually distinguish these markers from section/chord markers. See [§7.10.2](#7102-cross-reference-marker-format) for the full format specification.

#### 5.2.3 Tempo automation

When the CompositionPlan includes tempo changes (rit./accel., sectional tempo shifts), they emit as `RealPoint` entries in `TempoAutomation`. Each point has `time` (in beats), `value` (in BPM), and `interpolation` (`linear` for gradual ramps, `hold` for abrupt shifts).

```xml
<TempoAutomation id="id_tempo_auto">
  <RealPoint time="0.0"   value="128.000000" interpolation="hold"/>
  <RealPoint time="60.0"  value="128.000000" interpolation="linear"/>
  <RealPoint time="63.5"  value="120.000000" interpolation="linear"/>  <!-- rit. into chorus -->
  <RealPoint time="64.0"  value="128.000000" interpolation="hold"/>    <!-- snap back for downbeat -->
</TempoAutomation>
```

### 5.3 Track Structure Template

Every Bannou composition has a canonical track set determined by the style and Storyteller's instrumentation requirements. A typical dark-revelry composition's track structure:

```
Master (MASTER)
│   devices: Equalizer → Compressor → Limiter
│
├── Reverb Return (EFFECT)
│     devices: Vst3Plugin "Valhalla Vintage Verb" + captured preset
│     destination → Master
│
├── Rhythm Section (SUBMIX)
│   │   destination → Master
│   ├── Drums                (NOTES)
│   │     devices: Vst3Plugin "Battery 4" + captured preset
│   │     sends → Reverb Return (level: style-driven, typically 0.1-0.2)
│   │     destination → Rhythm Section
│   └── Bass                 (NOTES)
│         devices: Vst3Plugin "Serum" + captured preset (or Mai Tai via Device)
│         sends → Reverb Return (level: 0.05-0.15)
│         destination → Rhythm Section
│
├── Harmonic Core (SUBMIX)
│   │   destination → Master
│   ├── Chord Pad            (NOTES)
│   │     devices: Vst3Plugin "Omnisphere" + captured preset
│   │     sends → Reverb Return (level: 0.3-0.45)
│   │     destination → Harmonic Core
│   └── Arpeggiator          (NOTES)
│         devices: Vst3Plugin "Serum" + captured preset
│         sends → Reverb Return (level: 0.2-0.3)
│         destination → Harmonic Core
│
├── Melodic (SUBMIX)
│   │   destination → Master
│   ├── Lead Melody          (NOTES)
│   │     devices: Vst3Plugin "Spire" + captured preset
│   │     sends → Reverb Return (level: 0.15-0.25)
│   │     destination → Melodic
│   └── Counterpoint         (NOTES)  (present only when style requires or Counterpoint designates)
│         devices: Vst3Plugin "Kontakt" + Spitfire Strings preset
│         sends → Reverb Return (level: 0.35-0.50)
│         destination → Melodic
│
└── Color/Flavor (SUBMIX)
    │   destination → Master
    ├── Style-Specific A     (NOTES)  (e.g., dark-revelry: cabaret piano; celtic: uilleann pipes)
    │     devices: Vst3Plugin ... + captured preset
    │     sends → Reverb Return (varies)
    │     destination → Color/Flavor
    └── Style-Specific B ... (as many as the style requires)
```

**Track roles are style-determined.** Different styles have different canonical track sets. The style schema (loaded by StyleApplication) declares the canonical tracks for that style — dark-revelry gets a theatrical-cabaret track, celtic gets a drone track, cinematic gets a choral track, etc. ProjectBuilder reads the style's track list and instantiates them all, mapping each to a PluginLibraryTemplate via the PluginResolver.

**Submix grouping is always present.** Even for minimal compositions with three tracks (Drums, Bass, Melody), the tracks route through submix groups to Master. This produces consistent mixer topology across projects — the user can mix any project the same way.

**Color assignments are stable.** Submix groups have fixed colors (Rhythm Section: warm red; Harmonic Core: teal; Melodic: gold; Color/Flavor: muted purple). Track colors within a group follow a gentle hue-variation convention so the user visually distinguishes siblings.

### 5.4 Per-Track Content

Each track's `Lanes` element contains the MIDI clips (`Clips` → `Clip` → `Notes` → `Note`) plus any track-level automation (`Points` entries).

#### 5.4.1 Clips

One `Clip` per compositional section (Verse 1, Chorus 1, etc.), time-aligned with the Marker for that section. Clip name matches the section name. When the MusicStoryteller designates repeated sections (Verse 2 structurally identical to Verse 1), the second clip uses `Clip.reference` to point at the first — a shared-content alias:

```xml
<Lanes track="id_lead_melody" id="id_lead_lanes">
  <Clips id="id_lead_clips">
    <Clip time="0.0"   duration="16.0" name="Intro"       content="{...notes...}"/>
    <Clip time="16.0"  duration="32.0" name="Verse 1"     content="{...notes...}" id="id_c_v1"/>
    <Clip time="48.0"  duration="16.0" name="PreChorus"   content="{...notes...}"/>
    <Clip time="64.0"  duration="32.0" name="Chorus 1"    content="{...notes...}" id="id_c_c1"/>
    <Clip time="96.0"  duration="32.0" name="Verse 2"     reference="id_c_v1"/>
    <Clip time="128.0" duration="32.0" name="Chorus 2"    reference="id_c_c1"/>
    <!-- ... -->
  </Clips>
</Lanes>
```

Alias clips are emitted only when Storyteller/Counterpoint has explicitly designated a repeat. Sections that share structure but have small variations (different countermelody, different drum fill, etc.) emit as separate content clips — aliasing must be correct, not approximate.

#### 5.4.2 Notes

Each clip's `Notes` timeline contains the MIDI notes for that section. Note attributes:

- `time` — start time in beats, relative to the clip's start
- `duration` — duration in beats
- `channel` — MIDI channel (0 for most tracks; multi-channel use reserved for MPE-capable plugins)
- `key` — MIDI note number (0-127; middle C = 60)
- `vel` — note-on velocity, normalized 0.0-1.0
- `rel` — note-off/release velocity, normalized 0.0-1.0 (optional; defaults to `vel`)

**Velocity encoding.** Bannou's Storyteller produces velocity values driven by:
- Style's `dynamicsProfile.baseLevel` as the nominal velocity
- Storyteller's per-section emotional state's `energy` dimension (higher energy → higher velocity)
- Style's articulation profile (accent probability → per-note velocity boost)
- Phrase-level ramping (crescendo over a phrase → monotonically rising velocities)

**Articulation encoding via note duration.** The style's articulation profile is realized in note duration relative to the nominal value:
- **Staccato** (staccato probability): duration shortened to ~25% of the rhythmic slot (e.g., a 0.5-beat rhythmic slot emits a 0.125-beat note)
- **Legato** (legato probability): duration extended to 100-110% of the rhythmic slot, slightly overlapping the next note
- **Marcato** (marcato probability): duration at 50-60% of the slot with a velocity boost of ~15%
- **Normal/default**: duration at 85-95% of the slot

#### 5.4.3 Per-note expression

For notes that need expressive shape (sustained melodic tones, expressive leads, MPE-capable parts), the Note element contains a nested Timeline with Points timelines targeting ExpressionType values:

```xml
<Note time="12.0" duration="4.0" channel="0" key="70" vel="0.82" rel="0.6">
  <Lanes>
    <Points unit="normalized" id="id_n1_bend">
      <Target expression="pitchBend" channel="0"/>
      <RealPoint time="0.0" value="0.5"                 interpolation="linear"/>  <!-- at-center -->
      <RealPoint time="0.5" value="0.515"               interpolation="linear"/>  <!-- slight sharp drift -->
      <RealPoint time="1.0" value="0.505"               interpolation="linear"/>
      <RealPoint time="3.5" value="0.5"                 interpolation="linear"/>  <!-- settle -->
    </Points>
    <Points unit="normalized" id="id_n1_press">
      <Target expression="pressure" channel="0"/>
      <RealPoint time="0.0" value="0.4"                 interpolation="linear"/>
      <RealPoint time="2.0" value="0.75"                interpolation="linear"/>  <!-- expression crescendo -->
      <RealPoint time="3.8" value="0.3"                 interpolation="linear"/>
    </Points>
  </Lanes>
</Note>
```

Per-note expression is emitted conditionally — short, rhythmic notes don't get it (wastes file size with no audible effect), long sustained notes do. The threshold is `duration >= 0.5 beats AND style.perNoteExpressionEnabled AND plugin.supportsMpe` (the last from PluginLibraryTemplate metadata).

#### 5.4.4 Track-level automation

Automation that applies to the whole track (volume envelopes, expression CC curves, reverb send ramps, plugin-parameter automation) emits as `Points` timelines in the track's Lanes, not inside clips:

```xml
<Lanes track="id_lead_melody" id="id_lead_lanes">
  <Clips id="id_lead_clips"> ... </Clips>

  <!-- CC11 (expression) automation following tension curve -->
  <Points unit="normalized" id="id_lead_cc11">
    <Target expression="channelController" channel="0" controller="11"/>
    <RealPoint time="0.0"   value="0.3" interpolation="linear"/>
    <RealPoint time="32.0"  value="0.5" interpolation="linear"/>   <!-- rising into verse climax -->
    <RealPoint time="48.0"  value="0.7" interpolation="linear"/>   <!-- prechorus -->
    <RealPoint time="64.0"  value="0.9" interpolation="linear"/>   <!-- chorus -->
    <RealPoint time="96.0"  value="0.45" interpolation="linear"/>  <!-- verse 2 reset -->
    <!-- ... -->
  </Points>

  <!-- Volume automation (slightly below CC11, so the track breathes with the section) -->
  <Points unit="linear" id="id_lead_vol">
    <Target parameter="id_lead_channel_vol"/>
    <RealPoint time="0.0"   value="0.7" interpolation="linear"/>
    <RealPoint time="64.0"  value="0.85" interpolation="linear"/>  <!-- chorus bump -->
    <RealPoint time="160.0" value="0.6" interpolation="linear"/>   <!-- bridge pullback -->
    <RealPoint time="176.0" value="0.9" interpolation="linear"/>   <!-- final chorus push -->
  </Points>

  <!-- Reverb send level automation (opens in bridge for sense of space) -->
  <Points unit="linear" id="id_lead_send">
    <Target parameter="id_lead_send_rev_vol"/>
    <RealPoint time="0.0"   value="0.2"  interpolation="linear"/>
    <RealPoint time="160.0" value="0.5"  interpolation="linear"/>  <!-- bridge opens up -->
    <RealPoint time="176.0" value="0.25" interpolation="linear"/>  <!-- final chorus tightens -->
  </Points>
</Lanes>
```

### 5.5 Automation Derivation from Emotional State

Storyteller emits per-section emotional state — a 6-tuple of (tension, brightness, energy, warmth, stability, valence) per section. `EmotionToAutomation` derives the automation point streams:

| Emotional dimension | Automation target | Mapping formula |
|---|---|---|
| Tension (`0–1`) | CC11 expression | `value = 0.3 + tension * 0.6` — from quiet-expression to full-lean |
| Tension (`0–1`) | Channel volume | `value = 0.6 + tension * 0.25` — subtle volume breathing |
| Brightness (`0–1`) | CC74 sound-controller 5 | `value = 0.3 + brightness * 0.5` (only if plugin responds to CC74) |
| Brightness (`0–1`) | Master EQ high-shelf gain | linear cross-fade between -1.5 dB (0) and +2.0 dB (1) — builds only into the master device chain, no per-section automation |
| Energy (`0–1`) | Velocity baseline | velocities scaled by `0.6 + energy * 0.35` — not automation, baked into note velocities |
| Warmth (`0–1`) | Reverb send level | `value = 0.1 + warmth * 0.45` |
| Warmth (`0–1`) | Master EQ low-shelf gain | again applied statically at master-bus layer, not per-section |
| Stability (`0–1`) | Tempo automation smoothness | higher stability → less tempo micro-variation (no ramps between sections); lower stability permits small tempo variations at section transitions |
| Valence (`0–1`) | Mode distribution (applied upstream in style) + CC1 mod-wheel depth | `cc1 value = 0.1 + (1 - valence) * 0.3` — lower valence gets more vibrato-in (darkness of tone from expression) |

Interpolation between sections uses `linear`. Point density is 1 point per section boundary, optionally with intra-section points for Storyteller-designated expressive arcs (crescendi, decrescendi, sudden dynamic drops per Dark Revelry's dramaticContrastProbability).

### 5.6 Master Bus Device Chain

Every project ends with a Master bus device chain assembled from DAWproject built-in devices. Style-tuned defaults:

```xml
<Channel role="master" solo="false" id="id_master_channel">
  <Devices>
    <Equalizer deviceName="Bannou Master EQ" deviceRole="audioFX" id="id_master_eq">
      <Band type="highPass" order="1">
        <Freq value="30.0" unit="hertz" id="id_hp_freq"/>
        <Enabled value="true" id="id_hp_en"/>
      </Band>
      <Band type="lowShelf" order="2">
        <Freq value="120.0" unit="hertz" id="id_ls_freq"/>
        <Gain value="{style.masterEq.lowShelfGain}" unit="decibel" id="id_ls_gain"/>
        <Q value="0.7" unit="linear" id="id_ls_q"/>
      </Band>
      <Band type="bell" order="3">
        <Freq value="{style.masterEq.midFreq}" unit="hertz" id="id_bell_freq"/>
        <Gain value="{style.masterEq.midGain}" unit="decibel" id="id_bell_gain"/>
        <Q value="0.9" unit="linear" id="id_bell_q"/>
      </Band>
      <Band type="highShelf" order="4">
        <Freq value="6000.0" unit="hertz" id="id_hs_freq"/>
        <Gain value="{style.masterEq.highShelfGain}" unit="decibel" id="id_hs_gain"/>
        <Q value="0.7" unit="linear" id="id_hs_q"/>
      </Band>
      <Enabled value="true" id="id_eq_en"/>
    </Equalizer>
    <Compressor deviceName="Bannou Glue Compressor" deviceRole="audioFX" id="id_master_comp">
      <Threshold value="-12.0" unit="decibel" id="id_c_thr"/>
      <Ratio value="2.0" unit="linear" id="id_c_ratio"/>
      <Attack value="0.03" unit="seconds" id="id_c_att"/>
      <Release value="0.2" unit="seconds" id="id_c_rel"/>
      <AutoMakeup value="true" id="id_c_am"/>
      <Enabled value="true" id="id_c_en"/>
    </Compressor>
    <Limiter deviceName="Bannou Master Limiter" deviceRole="audioFX" id="id_master_lim">
      <Threshold value="-0.3" unit="decibel" id="id_l_thr"/>
      <Attack value="0.001" unit="seconds" id="id_l_att"/>
      <Release value="0.05" unit="seconds" id="id_l_rel"/>
      <Enabled value="true" id="id_l_en"/>
    </Limiter>
  </Devices>
  <!-- Mute/Pan/Volume parameters -->
</Channel>
```

Style-tuned parameters per the style schema's `masterBusConfig`:

| Style | lowShelfGain | midFreq / midGain | highShelfGain | Compressor ratio | Character |
|---|---|---|---|---|---|
| dark-revelry | +1.5 dB | 250 Hz / -1.0 dB | +2.5 dB | 2.5:1 | theatrical air + mid-range contour dip |
| celtic | +1.0 dB | 800 Hz / +0.5 dB | +1.0 dB | 2:1 | warmer, airier, vocal-friendly |
| cinematic-orchestral | +2.0 dB | 400 Hz / -1.5 dB | +2.0 dB | 1.8:1 | deeper low end, more space |
| anthemic-rock | +2.5 dB | 3 kHz / +1.5 dB | +2.0 dB | 3:1 | bigger glue, presence bump |

None of this is sacred — the user will tweak it — but it means the composition is mastered on first open, not raw stems.

### 5.7 Reverb Return Bus

A single `MixerRole.EFFECT` track carrying the reverb instrument and receiving sends from every tonal track:

```xml
<Track contentType="audio" loaded="true" id="id_reverb_track" name="Reverb Return" color="#5b6e8e">
  <Channel audioChannels="2" destination="id_master_channel" role="effect" solo="false" id="id_reverb_ch">
    <Devices>
      <Vst3Plugin deviceID="{reverb-plugin-uuid}" deviceName="{reverb-plugin-name}"
                  deviceRole="audioFX" loaded="true" id="id_reverb_plugin">
        <State path="plugins/{captured-reverb-preset}.vstpreset"/>
        <Enabled value="true" id="id_rev_en"/>
      </Vst3Plugin>
    </Devices>
    <Volume value="0.75" unit="linear" id="id_rev_vol"/>
    <Pan value="0.5" unit="normalized" id="id_rev_pan"/>
    <Mute value="false" id="id_rev_mute"/>
  </Channel>
</Track>
```

Every tonal track's `Channel.sends` has a Send to `id_reverb_ch` with a style+role-appropriate level. When no reverb plugin is registered in the user's library, the Reverb Return track is omitted entirely and sends are not emitted — the master bus compressor/limiter still run.

### 5.8 ID Allocation Rules

IDs are assigned in a deterministic depth-first traversal order:
1. Transport (`id0` Tempo, `id1` TimeSignature)
2. Structure (tracks depth-first; each track allocates ID block for Channel, devices, parameters)
3. Arrangement (`id_arrangement`, `id_arrangement_lanes`, then per-track lanes, then markers)
4. Scenes (typically empty; emits `<Scenes/>` with no children)

The numeric component increments monotonically across the entire project. The prefix `id` is fixed. IDs are never recycled within a project. The `IdAllocator` (see [Part 4.3](#43-project-structure)) owns the numbering.

For human-readable identity, IDs may have a semantic suffix (`id_master_channel`, `id_lead_cc11`) — the `id` attribute value is a single string, so either style is valid. Bannou uses semantic suffixes for top-level well-known entities (the master channel, the arrangement, the markers block) and bare numeric IDs for generated entries (individual notes, automation points).

### 5.9 MetaData Block

The companion `metadata.xml` file carries track-level metadata:

```xml
<MetaData>
  <Title>{composition.title}</Title>
  <Artist>{configured composer name}</Artist>
  <Composer>{"Bannou MusicStoryteller" or author name}</Composer>
  <Year>{current year}</Year>
  <Genre>{style.category}</Genre>
  <Comment>Generated by Bannou MusicComposer {sdkVersion} from composition request {seed}</Comment>
</MetaData>
```

The `Comment` field carries provenance information for debuggability — if the user reports unexpected behavior, Bannou can reconstruct the input from the comment.

---

## Part 6: The Plugin Library Service

The Plugin Library is the persistent, queryable store of captured plugin state files. It is the substrate that converts the user's Studio One investment (curated patches, refined sounds) into reusable assets MusicComposer can automatically insert into future compositions. This section specifies its role taxonomy, family registry, data model, API, capture workflow, and resolution semantics.

### 6.1 Design Stance

The Plugin Library is a **Category B (deprecate-only, no delete) template registry** — once a template is published, it must remain readable forever because past `.dawproject` files reference it. This is the same lifecycle pattern as Item Template, Quest Definition, Collection Entry Template, etc. The Category B designation triggers a set of established tenet obligations (T31): deprecation endpoint, includeDeprecated list parameter, clean-deprecated sweep, instance creation guard (cannot resolve a deprecated template for a new composition), reverse index for instance-count checking.

The Library is **per-game-service scoped**. A `gameServiceId` filter gates every resolution — the set of captured patches for Arcadia is separate from the set for Defenders of Ba'gata, because each game's composer sensibility is distinct. Game-services that want a shared library (e.g., multiple titles from the same studio sharing a master patch collection) can establish that convention at the application level — the infrastructure doesn't force sharing.

The Library is **admin-write, developer-read**. Only developers with the admin role (or equivalent elevated permission) register new templates; any developer working on the game composes against the library. This prevents casual additions that bloat the library with one-off patches.

### 6.2 Role Taxonomy

Role slugs are **reverse-DNS hierarchical** — dot-separated segments from most-general (family) to most-specific (modifier). The hierarchy's purpose is twofold: to make fallback resolution walk up the tree naturally (see [§6.8](#68-resolution-semantics)) and to give composers a self-documenting vocabulary where `strings.orchestral.violin.legato.warm` is instantly parseable by human and machine.

#### 6.2.1 Segment Convention

```
{family}.{subfamily}.{voice}.{articulation}.{modifier}
```

Only `{family}` is required. Each subsequent segment is optional — the user goes as deep as they want to. A one-word role like `pad` is valid and means "a generic pad of no particular subfamily"; a five-word role like `strings.orchestral.violin.legato.warm` is also valid and conveys maximum specificity. The hierarchy is not prescriptive about segment count — it's prescriptive about segment ordering (general → specific, left → right).

#### 6.2.2 Default Family Set (Shipped With Bannou)

The root segment always comes from this set. Bannou ships this list as a committed reference (`schemas/music-role-families.yaml`) that the CLI consults for validation and tab-completion. Users cannot freely invent new families without updating the reference file (prevents typo-divergence like `string` vs `strings`).

| Family | Scope |
|---|---|
| `strings` | Bowed and plucked string instruments (violin, viola, cello, bass, harp, acoustic/electric guitar when used orchestrally) |
| `brass` | All brass (trumpet, horn, trombone, tuba, flugelhorn) |
| `woodwind` | All woodwinds (flute, oboe, clarinet, bassoon, saxophone) |
| `keys` | Piano, electric piano, harpsichord, clavinet, organ |
| `synth` | Any synthesizer-derived sound that isn't lead/bass/pad-specific |
| `drum` | Percussive instruments and kits (orchestral, rock, electronic) |
| `pad` | Sustained atmospheric sounds regardless of source (synth pads, string pads, choir pads) |
| `lead` | Single-voice melodic lead in any timbre (synth lead, guitar lead, flute lead) |
| `bass` | Bass-register melodic voice in any timbre (sub bass, electric bass, acoustic bass) |
| `fx` | Non-instrumental sounds: risers, impacts, textures, atmospheres |
| `vocal` | Vocal samples, vocal synths, choir libraries, processed voices |
| `world` | Ethnic instruments not fitting above (oud, kora, sitar, shakuhachi, duduk, etc.) |

#### 6.2.3 Conventional Sub-Segment Patterns

Beyond the family, sub-segments are conventional but not fixed. Bannou ships reference conventions per family; users extend freely within each branch. Extensions are additive — a user adding `strings.orchestral.viola.sul-ponticello.icy` doesn't conflict with the shipped hierarchy or with other users' extensions.

```
strings.{subfamily}.{voice}.{articulation}.{modifier}
  strings.orchestral.violin.legato.warm
  strings.orchestral.ensemble.tremolo.aggressive
  strings.orchestral.cello.pizzicato
  strings.chamber.quartet.sustained
  strings.acoustic.guitar.fingerpicked
  strings.electric.guitar.clean
```

```
synth.{topology}.{voice-type}.{articulation}.{modifier}
  synth.analog.monophonic.bass.driven
  synth.analog.polyphonic.pad.warm
  synth.digital.monophonic.lead.cutthrough
  synth.fm.polyphonic.pad.bell-like
```

```
drum.{genre}.{kit-style}.{modifier}
  drum.orchestral.felted
  drum.orchestral.snare.marcato
  drum.rock.full-kit
  drum.electronic.808
  drum.electronic.trap
```

```
pad.{character}.{modifier}
  pad.dark.enveloping
  pad.bright.ethereal
  pad.choir.warm
```

```
lead.{source}.{character}
  lead.synth.cutthrough
  lead.guitar.dirty
  lead.sax.breathy
```

```
bass.{source}.{character}
  bass.synth.sub
  bass.electric.fingered
  bass.acoustic.upright
```

The full shipped convention lives in `schemas/music-role-taxonomy.yaml` alongside the family registry. It's a reference, not a schema constraint — the resolver accepts any hierarchical slug, conventional or not.

#### 6.2.4 Segment-Name Rules

- Lowercase only
- Words within a segment separated by hyphens: `legato-warm` is one segment; `legato.warm` is two
- ASCII alphanumerics plus hyphens only — no underscores, no dots within segments, no unicode
- Segments are case-sensitive at storage but case-insensitive during resolver lookup
- Empty segments are invalid (`strings..warm` rejected at registration)

#### 6.2.5 Stylistic Tags Are Orthogonal

A template has both a **role slug** (the hierarchical position) and **stylistic tags** (an orthogonal set). Tags describe *applicability*, not identity: a `strings.orchestral.violin.legato.warm` template might be tagged `[dark-revelry, cinematic, celtic]` if the patch fits all three styles. Tags modify the resolver's scoring ([§6.8](#68-resolution-semantics)) but don't change where the template lives in the hierarchy. Two templates can share a role slug and differ only in tags — the resolver picks the one whose tags best match the composition's style.

### 6.3 Plugin Family Registry

When hierarchical fallback exhausts itself with no match (the request's family has zero registered templates anywhere in its subtree), Tier 1 fallback still needs to emit a plugin reference. The **Plugin Family Registry** is a shipped-with-Bannou map from each family to a list of plugins that are known to provide that family's sound, so Tier 1 can pick a plausible default even without any captured template.

#### 6.3.1 Source Of Truth

The registry ships as `schemas/music-plugin-families.yaml`, committed to the Bannou repo, maintained via standard PR process. Entries cover widely-used commercial and free plugins:

```yaml
strings:
  preferredPlugins:
    - name: "Kontakt"
      vendor: "Native Instruments"
      vst3Uuid: "5af395a8-05b8-4fa4-83ff-f40b17ca02fb"
      likelyRole: "sampler host"
      notes: "Requires a strings-oriented sample library loaded (Spitfire, EastWest, etc.)"
    - name: "Play"
      vendor: "EastWest"
      likelyRole: "sampler host"
    - name: "BBC Symphony Orchestra Core"
      vendor: "Spitfire Audio"
      likelyRole: "direct strings library"
    - name: "Presence XT"
      vendor: "PreSonus"
      likelyRole: "built-in Studio One sampler (Studio One-native, not VST3)"

synth:
  preferredPlugins:
    - name: "Omnisphere 2"
      vendor: "Spectrasonics"
    - name: "Serum"
      vendor: "Xfer Records"
      vst3Uuid: "..."
    - name: "Pigments"
      vendor: "Arturia"
    - name: "Diva"
      vendor: "u-he"
    - name: "Mai Tai"
      vendor: "PreSonus"
      likelyRole: "Studio One-native polyphonic analog modeling"

# ... similar entries for brass, woodwind, keys, drum, pad, lead, bass, fx, vocal, world
```

Each entry covers: plugin name (required), vendor (required), VST3 UUID (optional — enables direct identification from captured presets), likely role/notes (free-form description).

#### 6.3.2 Resolution Integration

When PluginResolver exhausts hierarchical walk-up with no template match:

1. Extract the request's family segment (`strings`, `synth`, etc.)
2. Consult the Plugin Family Registry for that family's preferred-plugins list
3. Cross-reference against the user's library — for each preferred plugin, check whether **any** PluginLibraryTemplate exists registered against that plugin (regardless of role slug). The user having *something* registered against "Kontakt" implies Kontakt is installed and usable.
4. If one or more preferred plugins match the user's library, pick the first (registry order = preference order) and emit Tier 1 reference to that plugin in default state.
5. If zero preferred plugins are present in the user's library, the Tier 1 emission has no plugin — the DAWproject Track emits with empty Devices. The user sees an empty instrument slot and loads something manually.

This means: a user who has registered any three or four plugins gets intelligent Tier 1 fallback for most families. A brand-new user with an empty library gets raw empty slots — still a valid project, just unfurnished.

#### 6.3.3 User Override Endpoint

Users can override the shipped registry per-game-service via `POST /music/plugin-library/family-registry/override`. The override is a partial registry that layers over the shipped defaults (user additions win; user can remove a shipped entry by providing an empty `preferredPlugins` for that family). Overrides live in a dedicated state store (`plugin-family-registry-overrides`, MySQL, keyed by `gameServiceId`). This lets a user encode "for this project, prefer EastWest Play over Kontakt for strings" without forking the shipped registry.

#### 6.3.4 Maintenance Cadence

The shipped registry is intentionally shallow — 20-30 of the most-common plugins per family at most. It's a starting point, not an exhaustive catalog. Additions come via PR when a plugin becomes widely-used enough to warrant default presence. The maintainer shouldn't agonize over completeness — the override mechanism handles long-tail plugins.

### 6.4 Data Model

```csharp
// Internal storage model (plugin-library-templates, MySQL)
internal class PluginLibraryTemplateModel
{
    // Identity
    public Guid TemplateId { get; set; }
    public Guid GameServiceId { get; set; }

    // Plugin identity — one of two forms
    public PluginKind Kind { get; set; }                     // Vst3 | Vst2 | Au | Clap | StudioOneNative
    public string? PluginUuid { get; set; }                  // VST3: canonical UUID; null for StudioOneNative
    public string PluginName { get; set; } = string.Empty;   // Human-readable plugin name (always set)
    public string? PluginVendor { get; set; }                // Optional vendor ("Native Instruments", "u-he", "PreSonus")
    public string? PluginVersion { get; set; }               // Optional; blank means "whatever is installed"

    // Role and stylistic metadata
    public string RoleSlug { get; set; } = string.Empty;     // "strings-legato-warm", "dark-revelry-lead", ...
    public string DisplayName { get; set; } = string.Empty;  // Human-readable ("Kontakt: Spitfire Albion Legato Warm")
    public List<string> StylisticTags { get; set; } = new(); // ["dark-revelry", "cinematic", "low-warmth", "mpe-capable"]
    public DeviceRole DeviceRole { get; set; }               // Instrument | NoteFX | AudioFX | Analyzer

    // State file
    public Guid StateAssetId { get; set; }                   // Asset service ID of the captured preset file
    public string StateFileExtension { get; set; } = string.Empty; // ".vstpreset", ".fxp", ".preset", ".clap-preset"
    public long StateFileSizeBytes { get; set; }

    // Capability flags (used for per-note expression gating)
    public bool SupportsMpe { get; set; }                    // Does this plugin honor per-note expression?
    public bool SupportsKeySwitches { get; set; }            // Does it respond to key-switch note triggers?
    public List<string> ResponsiveCcs { get; set; } = new(); // Controller numbers this patch responds to (e.g., ["1", "11", "74"])

    // Lifecycle
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid CreatedBy { get; set; }                      // Admin account that registered this template

    // Deprecation (Category B triple-field per T31)
    public bool IsDeprecated { get; set; }
    public DateTimeOffset? DeprecatedAt { get; set; }
    public string? DeprecationReason { get; set; }
}

public enum PluginKind
{
    Vst3,                    // Standard VST3 with .vstpreset state
    Vst2,                    // Legacy VST2 with .fxp or .fxb state
    Au,                      // Audio Unit with .aupreset state
    Clap,                    // CLAP with .clap-preset state
    StudioOneNative          // PreSonus Mai Tai, Presence XT, etc. with Studio One's native state format
}
```

### 6.5 State Storage

Four new state stores declared in `schemas/state-stores.yaml`:

```yaml
plugin-library-templates:
  backend: mysql
  service: Music
  purpose: Captured plugin state templates — persistent, queryable by role/style/plugin identity

plugin-library-templates-reverse-index:
  backend: mysql
  service: Music
  prefix: "plt-idx:"
  purpose: Reverse index from template ID to usage-count (maintained by ProjectBuilder on every resolve) — required for Category B clean-deprecated sweep

plugin-library-lock:
  backend: redis
  prefix: "plt:lock:"
  service: Music
  purpose: Distributed locks for template mutations (register/deprecate) to avoid concurrent writes to the same role slug

plugin-family-registry-overrides:
  backend: mysql
  service: Music
  purpose: Per-game-service overrides of the shipped Plugin Family Registry (see §6.3) — maps family to a custom preferred-plugins list that layers over the YAML shipped with Bannou
```

**Instance tracking semantics.** The "instance" of a PluginLibraryTemplate is an individual `.dawproject` file that resolved to that template. Since `.dawproject` files are output artifacts (not persistent entities in the Bannou system), we don't track individual instance IDs. Instead, the reverse index tracks a **usage counter** — each time ProjectBuilder resolves a role to a template, the counter increments; each time a previously-generated `.dawproject` is invalidated (e.g., the user regenerates the same composition with different parameters), the previous resolution's counter decrements if the new resolution picked a different template. For Category B cleanup purposes, any non-zero usage counter prevents cleanup — the template stays in storage because some past composition references it. When all past compositions have been re-rendered and no references remain, the counter reaches zero and clean-deprecated removes the template.

Alternative considered: **"instance" is an asset-stored `.dawproject` file, instances are tracked by asset ID**. This is cleaner but requires `.dawproject` files to be persistent asset-service entities (they currently are not — they're ephemeral downloads). If the asset-persistence model shifts to persist every generated `.dawproject`, this alternative becomes more natural. Deferred decision; the usage-counter approach is the Phase 1 design.

### 6.6 API Surface

All endpoints under `/music/plugin-library/*`. Schema additions to `schemas/music-api.yaml`:

```yaml
paths:
  /music/plugin-library/register:
    post:
      operationId: registerPluginLibraryTemplate
      summary: Register a captured plugin state file as a reusable MusicComposer template
      description: |
        Admin-only endpoint. Uploads a captured plugin state file to the Asset service,
        creates a PluginLibraryTemplate record associating the state with a plugin identity
        (VST3 UUID / native plugin name), a role slug, and stylistic metadata. The template
        becomes available for MusicComposer's ProjectBuilder to resolve during composition
        export.

        Category B deprecation (per IMPLEMENTATION TENETS): once registered, templates
        cannot be deleted, only deprecated and eventually cleaned up via the clean-deprecated
        sweep when all referencing compositions have been re-rendered.
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RegisterPluginLibraryTemplateRequest'
      responses:
        '201':
          description: Template registered
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PluginLibraryTemplateResponse'
        '400':
          description: Invalid request (missing required fields, invalid plugin identity, etc.)
        '409':
          description: A template with this role slug already exists for this game service (use update or pick a different slug)

  /music/plugin-library/get:
    post:
      operationId: getPluginLibraryTemplate
      summary: Retrieve a registered template by ID
      x-permissions:
        - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetPluginLibraryTemplateRequest'
      responses:
        '200':
          description: Template details
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PluginLibraryTemplateResponse'
        '404':
          description: Template not found

  /music/plugin-library/list:
    post:
      operationId: listPluginLibraryTemplates
      summary: Paged query of registered templates with optional filters
      description: |
        Filters: gameServiceId (required), roleSlug (optional), pluginKind (optional),
        pluginUuid (optional), stylisticTags (optional — returns templates whose tag set
        is a superset of the filter), includeDeprecated (default: false).
      x-permissions:
        - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListPluginLibraryTemplatesRequest'
      responses:
        '200':
          description: Paged result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListPluginLibraryTemplatesResponse'

  /music/plugin-library/resolve:
    post:
      operationId: resolvePluginLibraryTemplate
      summary: Internal — resolve a role slug to the best-matching template for a composition
      description: |
        Used by MusicComposer.ProjectBuilder during .dawproject export. Given a role slug,
        a style ID, and a game service ID, returns the template whose stylistic tags best
        match the style. Returns 404 (and ProjectBuilder falls back to Tier 1 reference-only)
        if no template matches.

        Increments the reverse-index usage counter for the returned template.
      x-permissions:
        - role: admin  # Only lib-music itself calls this (via service-to-service mesh)
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ResolvePluginLibraryTemplateRequest'
      responses:
        '200':
          description: Resolved template with state file asset reference
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PluginLibraryTemplateResponse'
        '404':
          description: No matching template (caller falls back to Tier 1)

  /music/plugin-library/deprecate:
    post:
      operationId: deprecatePluginLibraryTemplate
      summary: Mark a template as deprecated
      description: |
        Category B deprecation (per IMPLEMENTATION TENETS). Deprecated templates are not
        returned by resolve for new compositions but remain readable for existing ones.
        Idempotent — returns OK if already deprecated.
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeprecatePluginLibraryTemplateRequest'
      responses:
        '200':
          description: Deprecated (or was already deprecated)
        '404':
          description: Template not found

  /music/plugin-library/clean-deprecated:
    post:
      operationId: cleanDeprecatedPluginLibraryTemplates
      summary: Category B cleanup sweep
      description: |
        Permanently removes deprecated templates whose reverse-index usage counter has
        reached zero (no referencing compositions remain). Admin-only. Idempotent.
        Respects a configurable grace period.
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: 'common-api.yaml#/components/schemas/CleanDeprecatedRequest'
      responses:
        '200':
          description: Sweep completed
          content:
            application/json:
              schema:
                $ref: 'common-api.yaml#/components/schemas/CleanDeprecatedResponse'

  /music/plugin-library/bulk-import:
    post:
      operationId: bulkImportPluginLibraryTemplates
      summary: Bulk-register a directory tree of preset files as Plugin Library Templates
      description: |
        Primary capture path for the Plugin Library. Accepts a manifest of preset files
        (each with its relative path, binary content, and optional sidecar overrides) plus
        a game-service ID. For each file, Bannou infers plugin identity (reading VST3 UUID
        from the preset binary when available, falling back to the Plugin Family Registry
        for name matches) and role slug (from the directory hierarchy, with sidecar overrides
        taking precedence), then creates PluginLibraryTemplate records in a single transaction.

        Supports dry-run mode (returns the proposed registrations without committing).
        Supports overwrite mode (replaces existing templates with the same slug; default: skip
        with warning).

        The underlying `bannou music-library bulk-import` CLI command wraps this endpoint —
        the CLI reads the directory tree on the user's machine, streams preset bytes to this
        endpoint, and renders the dry-run result.
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BulkImportPluginLibraryTemplatesRequest'
      responses:
        '200':
          description: Bulk import result — either a dry-run preview or a commit summary
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BulkImportPluginLibraryTemplatesResponse'
        '400':
          description: Invalid manifest (unparseable preset files, ambiguous plugin identities, etc.) — response includes per-file error details

  /music/plugin-library/capture-from-dawproject:
    post:
      operationId: captureFromDawprojectPluginLibraryTemplates
      summary: Extract plugin instances from an existing .dawproject and register each as a template
      description: |
        Secondary bulk-import path. Accepts a .dawproject file (as the user's existing
        mixing-template project, for example) and enumerates every plugin instance in every
        track's channel devices. For each plugin with an embedded state file, Bannou creates
        a PluginLibraryTemplate — inferring the role slug from the track name when possible
        (with user-provided overrides for ambiguous cases), and carrying forward the plugin
        identity, vendor, version, and state file bytes.

        Supports dry-run mode.

        Useful for users who have invested in Studio One mixing templates — one command
        extracts 15-20 templates from a single reference project, jump-starting the library.
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CaptureFromDawprojectRequest'
      responses:
        '200':
          description: Capture result
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CaptureFromDawprojectResponse'
        '400':
          description: Invalid .dawproject (unparseable, corrupt ZIP, XSD validation failure)

  /music/plugin-library/family-registry/get:
    post:
      operationId: getPluginFamilyRegistry
      summary: Retrieve the effective Plugin Family Registry (shipped defaults + overrides)
      description: |
        Returns the merged registry — the shipped `schemas/music-plugin-families.yaml`
        contents layered with any per-game-service overrides. Used by ProjectBuilder's
        PluginResolver during Tier 1 fallback.
      x-permissions:
        - role: developer
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetPluginFamilyRegistryRequest'
      responses:
        '200':
          description: Effective registry for this game service
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PluginFamilyRegistryResponse'

  /music/plugin-library/family-registry/override:
    post:
      operationId: overridePluginFamilyRegistry
      summary: Set per-game-service overrides for the Plugin Family Registry
      description: |
        Admin-only endpoint. Replaces the override entry for the specified family (or
        removes it if preferredPlugins is omitted/empty). Overrides layer on top of the
        shipped YAML registry — user entries win, and setting an empty override list for
        a family effectively removes all shipped defaults for that family within this
        game service.
      x-permissions:
        - role: admin
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/OverridePluginFamilyRegistryRequest'
      responses:
        '200':
          description: Override saved
```

### 6.7 Capture Workflow

Three ingest paths in priority order: **bulk import from directory tree** (primary), **capture from existing .dawproject** (secondary), **single-patch register** (edge case). All three funnel into the same PluginLibraryTemplate storage via `POST /music/plugin-library/register` internally — the three paths differ only in how input manifests are assembled.

#### 6.7.1 Primary Path: Bulk Import From Directory Tree

The user organizes their saved preset files into a directory tree that mirrors the role taxonomy, then runs one command:

```bash
bannou music-library bulk-import \
    --dir ~/MusicLibrary \
    --game-service-id {game} \
    --dry-run   # optional: preview before committing
```

**Directory convention.** Directory structure determines plugin identity and role slug:

```
~/MusicLibrary/           <-- library root (passed via --dir)
├── Kontakt/              <-- first level = plugin name
│   ├── strings/          <-- family
│   │   ├── orchestral/   <-- subfamily
│   │   │   ├── violin/   <-- voice
│   │   │   │   ├── legato-warm.vstpreset          <-- role: strings.orchestral.violin.legato-warm
│   │   │   │   ├── legato-bright.vstpreset        <-- role: strings.orchestral.violin.legato-bright
│   │   │   │   └── spiccato-sharp.vstpreset       <-- role: strings.orchestral.violin.spiccato-sharp
│   │   │   └── ensemble/
│   │   │       ├── legato-warm.vstpreset          <-- role: strings.orchestral.ensemble.legato-warm
│   │   │       ├── sustained.vstpreset            <-- role: strings.orchestral.ensemble.sustained
│   │   │       └── pizzicato.vstpreset            <-- role: strings.orchestral.ensemble.pizzicato
│   │   └── chamber/
│   │       └── quartet/
│   │           └── sustained.vstpreset            <-- role: strings.chamber.quartet.sustained
│   └── brass/
│       └── orchestral/
│           ├── trumpet/
│           │   └── legato-bright.vstpreset        <-- role: brass.orchestral.trumpet.legato-bright
│           └── ensemble/
│               └── marcato-aggressive.vstpreset   <-- role: brass.orchestral.ensemble.marcato-aggressive
└── Omnisphere/
    ├── pad/
    │   ├── dark-enveloping.vstpreset              <-- role: pad.dark-enveloping
    │   └── bright-ethereal.vstpreset              <-- role: pad.bright-ethereal
    └── lead/
        └── synth-cutthrough.vstpreset             <-- role: lead.synth-cutthrough
```

Interpretation rules:

1. First directory level after `--dir` is the **plugin name** (looked up in the Plugin Family Registry for VST3 UUID and vendor; if not found, registered with plugin name only)
2. Each subsequent nested directory is one **role segment**, in the order they appear
3. The filename (minus extension) is the final role segment
4. The first role segment (immediately below the plugin directory) MUST match a family from §6.2.2 — Bannou rejects files at invalid family paths with a clear error

**Dry-run output.** Before committing, the CLI prints a tabular summary:

```
Plugin              Role Slug                                  File                     Status
------------------  ----------------------------------------  ----------------------  -------------
Kontakt             strings.orchestral.violin.legato-warm     legato-warm.vstpreset   new
Kontakt             strings.orchestral.violin.legato-bright   legato-bright.vstpreset new
Kontakt             strings.orchestral.violin.spiccato-sharp  spiccato-sharp.vstpreset new
Kontakt             strings.orchestral.ensemble.legato-warm   legato-warm.vstpreset   new
Kontakt             strings.orchestral.ensemble.sustained     sustained.vstpreset     new
Kontakt             strings.orchestral.ensemble.pizzicato     pizzicato.vstpreset     overwrite (existing)
Kontakt             strings.chamber.quartet.sustained         sustained.vstpreset     new
Kontakt             brass.orchestral.trumpet.legato-bright    legato-bright.vstpreset new
Kontakt             brass.orchestral.ensemble.marcato-aggressive  marcato-aggressive.vstpreset new
Omnisphere          pad.dark-enveloping                       dark-enveloping.vstpreset new
Omnisphere          pad.bright-ethereal                       bright-ethereal.vstpreset new
Omnisphere          lead.synth-cutthrough                     synth-cutthrough.vstpreset new

Registering 11 new templates, overwriting 1. Confirm? (y/n)
```

**Supporting flags:**

| Flag | Purpose |
|---|---|
| `--dir DIR` | Library root (required) |
| `--game-service-id ID` | Target game service (required) |
| `--dry-run` | Print proposed registrations, don't commit |
| `--tag TAG1,TAG2` | Apply these stylistic tags to every registered template (e.g., `--tag dark-revelry,cinematic`) |
| `--filter GLOB` | Only import files matching the pattern (`--filter '*legato*'`) |
| `--overwrite` | Replace existing templates with the same slug (default: skip with warning) |
| `--plugin-root NAME` | If directory layout starts with family (not plugin), provide plugin identity via flag: `bannou music-library bulk-import --dir ~/strings/orchestral/violin/ --plugin-root "Kontakt" --role-prefix strings.orchestral.violin` |
| `--role-prefix PREFIX` | Used with `--plugin-root` to set the base role path for directory contents |
| `--profile NAME` | Assign a PluginLibraryProfileId to every imported template (see [§6.8](#68-resolution-semantics) for how profiles affect resolution) |

**Sidecar override files.** For edge cases where a specific preset needs deviation from the inferred slug, tags, or capability flags, a `.yaml` file alongside it provides overrides:

```yaml
# strings/orchestral/violin/legato-warm.vstpreset.yaml
role: strings.orchestral.violin.legato.warm         # override inferred slug (note: more segments)
tags: [dark-revelry, cinematic, mpe-capable]        # stylistic tags (overrides --tag flag)
supportsMpe: true
supportsKeySwitches: false
responsiveCcs: [1, 11, 74]
displayName: "Spitfire Albion: Legato Sustains (Warm)"
pluginVendor: "Spitfire Audio"                      # override if registry lookup is wrong
```

Sidecars are optional; when absent, inferred values are used. When present, every sidecar field overrides its inferred counterpart. Sidecars may be committed to version control alongside the preset files — they ARE the library's intentional metadata layer.

#### 6.7.2 Secondary Path: Capture From Existing .dawproject

For users with established Studio One mixing templates (a `.dawproject` they've curated as their starter session with 15-20 instruments already dialed in), one command extracts everything:

```bash
bannou music-library capture-from-dawproject \
    --file ~/MyMixTemplate.dawproject \
    --game-service-id {game} \
    --role-inference from-track-name   # or: from-prompt, from-file
```

Bannou parses the `.dawproject`, enumerates every track's channel devices, extracts each plugin's identity + state file, and creates PluginLibraryTemplates. The role slug comes from one of three inference modes:

- **`from-track-name`** — derive the role from the track name (e.g., a track named "Strings: Orchestral Violin Legato" becomes role `strings.orchestral.violin.legato`). Requires disciplined track-naming in the source project.
- **`from-prompt`** — interactive: for each extracted plugin, prompt the user for a role slug. Useful for a first-time import where the user wants fine control.
- **`from-file`** — reads role assignments from a sidecar YAML file the user provides alongside the `.dawproject`. Scriptable and repeatable.

Same dry-run mode and commit pattern as bulk-import-from-directory. Also supports `--tag`, `--overwrite`, `--profile` flags.

#### 6.7.3 Edge-Case Path: Single-Patch Register

Occasionally a user captures one new patch and wants to add it without re-running a bulk import. The primitive `bannou music-library register` command is still available:

```bash
bannou music-library register \
    --role strings.orchestral.violin.legato.warm \
    --plugin-kind Vst3 \
    --plugin-uuid 84e8de5f-9255-4be0-8b99-3da3c51b3e04 \
    --plugin-name "Kontakt 7" \
    --state-file /path/to/captured.vstpreset \
    --tag dark-revelry,cinematic \
    --game-service-id {game}
```

Behind the scenes this posts to the same `POST /music/plugin-library/register` endpoint that bulk-import batches. No difference in storage — just a different input manifest (a single file instead of a tree).

#### 6.7.4 Post-Capture Verification

Regardless of path, the CLI surfaces `bannou music-library list` for quick verification:

```bash
bannou music-library list --role strings --game-service-id {game}
# (shows all strings.* templates registered for this game)
```

### 6.8 Resolution Semantics

`PluginResolver.ResolveAll` (the ProjectBuilder's use of `POST /music/plugin-library/resolve`) walks the reverse-DNS hierarchy from most-specific to least-specific and returns the highest-scoring template found at the first level that has any match. Replaces the flat Levenshtein-scoring algorithm with a structurally-aware fallback.

#### 6.8.1 Algorithm

```
Input:  requestedRole  = "strings.orchestral.violin.legato.warm"
        styleId        = "dark-revelry"
        gameServiceId  = {game}
        profile        = {PluginLibraryProfileId | null}
        needsMpe       = bool (from CompositionPlan)

segments = requestedRole.split(".")       # ["strings","orchestral","violin","legato","warm"]

FOR level = 0 TO segments.length - 1:
    prefix       = segments[0..segments.length - level - 1].join(".")
    isExactMatch = (level == 0)

    candidates = plugin-library-templates WHERE
        gameServiceId == requested.gameServiceId
        AND isDeprecated == false
        AND (isExactMatch
             ? roleSlug == prefix
             : roleSlug STARTS WITH prefix + "." OR roleSlug == prefix)

    IF candidates is empty: CONTINUE (try next level up)

    FOR EACH candidate IN candidates:
        score = 100 - (level * 15)                                      # level-based base
        IF candidate.roleSlug == requestedRole:           score += 20   # exact match bonus
        FOR EACH tag IN candidate.stylisticTags:
            IF tag IN style.implicitTags:                  score += 3   # tag overlap
        IF styleId IN candidate.stylisticTags:             score += 10  # explicit style tag
        score += recencyBonus(candidate.createdAt)                      # max +5, linear decay
        IF profile != null AND candidate.profileId == profile:
                                                            score += 10
        IF needsMpe AND NOT candidate.supportsMpe:         score -= 20
        capabilityPenalty(candidate, request, &score)                   # missing key-switch maps, missing CCs

        remember (candidate, score)

    IF any candidate scored > 0:
        RETURN highest-scoring candidate (note its level for telemetry: "resolved at level N, 0=exact")

# Exhausted all levels — Tier 1 fallback
family = segments[0]
registry = EffectiveFamilyRegistry(gameServiceId)
FOR EACH preferredPlugin IN registry[family].preferredPlugins:
    IF user has any registered template against this plugin:
        RETURN Tier1FallbackResult {
            plugin = preferredPlugin,
            state = null,
            resolvedLevel = "tier1-family-registry"
        }

RETURN Tier1FallbackResult { plugin = null, state = null, resolvedLevel = "tier1-empty" }
```

#### 6.8.2 Worked Example

Request: `strings.orchestral.violin.legato.warm`, styleId `dark-revelry`, needsMpe false.

User's library has:
- `strings.orchestral.violin.legato` (Kontakt + Spitfire Albion Legato, tags `[cinematic]`)
- `strings.orchestral.ensemble.legato.warm` (Kontakt + Spitfire Albion Ensemble, tags `[dark-revelry]`)
- `strings.orchestral.violin.spiccato.sharp` (Kontakt + Spitfire Albion Spiccato, tags `[cinematic]`)

Walk:

| Level | Prefix | Candidates | Result |
|---|---|---|---|
| 0 | `strings.orchestral.violin.legato.warm` | 0 | Miss — continue |
| 1 | `strings.orchestral.violin.legato.*` | 1 (`strings.orchestral.violin.legato`) | Score = 100 - 15 = 85; +3 for `cinematic` tag overlap; +recency. Candidate score ~90. Match — return. |

Resolution result: `strings.orchestral.violin.legato` at level 1. Notice that `strings.orchestral.ensemble.legato.warm` would actually score higher (because `dark-revelry` style tag matches, giving +10), but it's at level 2 (prefix `strings.orchestral.*`) which doesn't get evaluated if level 1 has candidates. **Hierarchical specificity always wins over stylistic affinity** — the user who registered a violin-specific patch expects that patch to be used for violin roles, not replaced by an ensemble patch that happens to be stylistically better-matched.

If the user wants stylistic affinity to override specificity, they register a `strings.orchestral.violin.legato.warm` template tagged `[dark-revelry]` — then it wins at level 0 (exact match) with the style-tag bonus layered on.

#### 6.8.3 Tier 1 Fallback via Family Registry

When the hierarchy is exhausted (no template matches at any level down to the family-only segment), the resolver consults the Plugin Family Registry ([§6.3](#63-plugin-family-registry)). For the family of the requested role (`strings`), the registry lists preferred plugins in priority order. The resolver cross-references against what the user has registered anywhere — if `Kontakt` appears in any PluginLibraryTemplate for this game service, the resolver assumes Kontakt is installed and emits a Tier 1 reference to Kontakt in default state.

If none of the registry's preferred plugins appear in the user's library at all (a genuinely empty library for that family), the resolver returns a null plugin — the DAWproject track emits with empty Devices. Studio One opens to an empty instrument slot; the user loads something manually.

#### 6.8.4 Resolution Result Metadata

Every resolution returns:

```csharp
public record ResolutionResult(
    PluginLibraryTemplate? Template,      // null if Tier 1 fallback
    PluginIdentity? Tier1Plugin,          // Tier 1's picked plugin (via registry); null if empty slot
    string ResolvedLevel,                 // "exact" | "level-1" | ... | "tier1-family-registry" | "tier1-empty"
    int Score,                            // final scoring value (for diagnostics)
    IReadOnlyList<string> Diagnostics);   // human-readable trace: "walked to level 2, no match for style dark-revelry"
```

The composer sees `ResolvedLevel` in the CLI output when generating a DAWproject:

```
Resolving 14 plugin roles...
  strings.orchestral.violin.legato.warm → strings.orchestral.violin.legato  [level-1 fallback]
  strings.orchestral.cello.sustained    → strings.orchestral.cello.sustained  [exact]
  brass.orchestral.trumpet.legato       → Kontakt (default)                  [tier1-family-registry]
  woodwind.orchestral.flute.legato      → (empty slot)                       [tier1-empty]
  ... 10 more
Proceeding with export.
```

This transparency means the composer immediately knows which roles used fallbacks — and therefore which registrations would sharpen the output if added to the library.

#### 6.8.5 Profile Semantics (Soft Preference)

`PluginLibraryProfileId` is a per-composition override that biases the resolver toward a subset of the library. Profile membership is recorded as a field on each template (assigned at registration via `--profile` flag or sidecar, or via `update` calls). At resolve time, the profile is a **soft preference** — matching-profile candidates get a +10 score bump, but non-matching candidates remain eligible.

Example: a composer has a `cinematic` profile (all templates suited to cinematic scoring) and a `dark-revelry` profile (theatrical villain aesthetic). For a particular dark-revelry villain composition, they pass `--profile dark-revelry`. The resolver prefers dark-revelry profile templates at each hierarchy level but falls back to general templates when the profile doesn't cover a specific role.

**Why soft, not hard.** Hard filtering would mean "if the profile doesn't cover this role, fail the resolution" — which punishes the composer for a role the profile doesn't need to cover (profiles are usually role-sparse). Soft preference means the profile nudges without blocking.

A future addition, `pluginLibraryOverrides` on the export request, will let the user pin specific roles to specific templates explicitly — that's the hard-filter case on a per-role basis, different from a whole-composition profile.

#### 6.8.6 Recency Bias

Among candidates at the same hierarchy level with equal style/tag scores, the resolver prefers more recently-captured templates. As the user iterates on their library, newer captures usually represent refined choices. The bonus is small (+0 to +5 on a ~90-point base), so it breaks ties rather than dominating.

Users who want to explicitly prefer an older template can update its `UpdatedAt` timestamp via `POST /music/plugin-library/update` (touch-save), or promote it via a future "preferred" flag.

### 6.9 MPE and Capability Propagation

The `SupportsMpe`, `SupportsKeySwitches`, and `ResponsiveCcs` fields on each template feed back into the ProjectBuilder's decisions:

- `SupportsMpe = true` → track is authored with per-note expression timelines for pitch bend / pressure / timbre
- `SupportsMpe = false` → per-note expression is aggregated into track-level channel CC automation (CC1 mod wheel for vibrato, CC11 for expression)
- `SupportsKeySwitches = true` + multi-articulation composition → key-switch notes emitted at section boundaries (to trigger "legato", "staccato", "marcato" articulation slots)
- `SupportsKeySwitches = false` → articulation variation is realized through duration + velocity only, no key-switch notes
- `ResponsiveCcs` lists specific CCs the user has verified this patch responds to — ProjectBuilder only automates CCs in this list to avoid no-op automation noise

These fields are set by the user at registration (they know their plugins) and updated via `update` calls as they discover additional capabilities.

### 6.10 Events

The Plugin Library follows standard T5 event-publishing:

| Topic | Event Type | Trigger |
|---|---|---|
| `music.plugin-library-template.created` | `MusicPluginLibraryTemplateCreatedEvent` | New template registered |
| `music.plugin-library-template.updated` | `MusicPluginLibraryTemplateUpdatedEvent` | Template metadata or deprecation status changed |
| `music.plugin-library-template.deleted` | `MusicPluginLibraryTemplateDeletedEvent` | Template permanently removed via clean-deprecated sweep |

No consumer of these events is planned in Phase 1; they exist for downstream extensibility (analytics, audit logs, cache invalidation for future Tier-2-preview features).

### 6.11 Configuration Properties

Additions to `schemas/music-configuration.yaml`:

| Property | Env var | Default | Purpose |
|---|---|---|---|
| `PluginLibraryMaxStateFileSizeBytes` | `MUSIC_PLUGIN_LIBRARY_MAX_STATE_FILE_SIZE_BYTES` | `52428800` (50 MB) | Upper bound on individual state file size at registration — rejects absurd uploads |
| `PluginLibraryCleanupGracePeriodDays` | `MUSIC_PLUGIN_LIBRARY_CLEANUP_GRACE_PERIOD_DAYS` | `30` | Grace period after deprecation before cleanup can delete (allows re-render workflows time to complete) |
| `PluginLibraryResolveCacheTtlSeconds` | `MUSIC_PLUGIN_LIBRARY_RESOLVE_CACHE_TTL_SECONDS` | `300` | TTL for Redis-cached resolve results (same role+style+profile returns same template) |

### 6.12 Honest Limitations

1. **State file format fidelity depends on the plugin.** Some plugins don't export full state via their preset mechanism — they may rely on external factory content that doesn't round-trip through `.vstpreset`. The user captures what the plugin will save; if the plugin's save is incomplete, the captured state is incomplete. There is no workaround at the Bannou level.

2. **Studio One-native preset format is undocumented.** PreSonus does not publish the internal format of Mai Tai/Presence XT preset files. Bannou treats them as opaque binary — store the bytes, embed them in DAWproject, let Studio One parse them on open. We cannot introspect, validate, or transform these files.

3. **Preset state can be large.** A Kontakt patch with a loaded sample library is megabytes; a rich multi-sample-bank instrument can approach 100 MB. The DAWproject ZIP can hold this, but it bloats the file. `PluginLibraryMaxStateFileSizeBytes` caps individual files. In extreme cases, the DAWproject export for a rich composition could reach hundreds of megabytes — acceptable for a developer tool, worth noting.

4. **Plugin version drift.** The user captures a patch from Kontakt 7. A year later they're on Kontakt 8. The `.vstpreset` may or may not load correctly in the new version. Bannou has no way to detect or migrate this — it's the plugin's compatibility concern. Recommended user practice: re-register patches after major plugin version upgrades.

5. **Plugin-library templates do not version their state.** There is one current state file per template. If the user wants to iterate on a patch (v2 with a longer reverb tail, v3 with brighter EQ), they either update the template (losing v1's bytes) or register v2 as a separate template with a suffixed slug (`strings-legato-warm-v2`). The simple model matches real-world curation workflows; a sophisticated versioning model would be over-engineered for Phase 1.

---

## Part 7: ProjectBuilder — Mapping Table

This section enumerates, exhaustively, how every input dimension to MusicComposer maps to a DAWproject element. Use it as a reference when implementing `StorytellerAdapter`, `CounterpointAdapter`, `StyleApplication`, and `EmotionToAutomation`.

### 7.1 From MusicTheory Output

| MusicTheory output | DAWproject target | Notes |
|---|---|---|
| `Scale` (tonic + mode) | First `Marker` at `time=0` named `"{Tonic} {Mode}"` plus mode detail if not Major/Minor | Studio One infers from notes for in-timeline key display |
| `Meter` | `Transport.TimeSignature.numerator` / `.denominator` + optional `TimeSignatureAutomation` points if meter changes mid-piece | Baseline value in Transport; automation overrides if present |
| Tempo | `Transport.Tempo.value` + optional `TempoAutomation` points for rit./accel. | Same pattern as time sig |
| `ProgressionGenerator` output (chord sequence with roman numerals) | Voiced chords emitted as `Notes` on the Chord Pad track; roman numerals emitted as Markers when `dawprojectOptions.annotateChords = true` | The chord progression is the compositional spine; the marker annotations are optional |
| `VoiceLeader` output (4-voice voicings) | Four `Note` elements per chord position, on the Chord Pad track | When the composition needs SATB-style display, voicings split across 4 sibling tracks (Soprano, Alto, Tenor, Bass) instead of stacking on one |
| `MelodyGenerator` output | `Note` elements on the Lead Melody track | Directly encodes time, duration, key, velocity |
| Bass-line output (from ProgressionGenerator or a bass-specific algorithm) | `Note` elements on the Bass track | When algorithm provides one; otherwise bass line is derived from chord roots + style-specific walking pattern |

### 7.2 From MusicStoryteller Output

| Storyteller output | DAWproject target |
|---|---|
| `NarrativeTemplate` (simple_arc / tension_and_release / journey_and_return) | Dictates section count, names, and energy/tension curve shape — feeds Marker placement and per-section automation |
| `Sections[]` — per-section `{phaseName, startBar, endBar, emotionalState}` | One `Clip` per section per track (aligned at section boundaries), one Marker per section boundary |
| `EmotionalState.tension` per section | RealPoints on CC11 expression automation + channel volume automation per track |
| `EmotionalState.brightness` per section | RealPoints on CC74 (tonal plugins that respond to it) + master EQ high-shelf setting per style |
| `EmotionalState.energy` per section | Note velocity baseline + rhythmic density (note count per bar) |
| `EmotionalState.warmth` per section | RealPoints on per-track reverb-send Volume parameter |
| `EmotionalState.stability` per section | Tempo-automation smoothness: higher stability → fewer tempo points between sections; lower stability → small ramps at boundaries |
| `EmotionalState.valence` per section | Upstream in Storyteller's mode distribution (already baked into notes); additionally RealPoints on CC1 mod-wheel for vibrato depth (more vibrato for lower valence) |
| GOAP-selected actions (`IntroduceMotif`, `DevelopMotif`, `AscendingSequence`, `AuthenticCadence`, `DelayResolution`, etc.) | Action names stamped onto clip names ("Intro: IntroduceMotif", "Chorus: AscendingSequence") so the composer sees Storyteller's reasoning in the arrangement; also influence note-level choices (the `AscendingSequence` action produces an ascending melody within that clip) |
| `CompositionIntent.HarmonicCharacter` (Diatonic, Chromatic, Climactic, etc.) | Harmonic density adjustments (more extensions on chord voicings for "Climactic") + octave doubling on Bass track when Storyteller specifies `doubleBass` |
| `CompositionIntent.MusicalCharacter.RegisterHeight` | MIDI key range — high register intents move melody notes up an octave, low register moves them down |
| `CompositionIntent.MusicalCharacter.RhythmicActivity` | Note count per bar on melody and counterpoint tracks |
| `CompositionIntent.MusicalCharacter.TexturalDensity` | Number of active tracks in that section — low density may mute the Arpeggiator and Counterpoint tracks for that clip |
| `CompositionIntent.DynamicLevel` | Velocity baseline for notes in that section; additionally a single RealPoint on channel volume at the start of the section |

### 7.3 From MusicComposer.Counterpoint (when the sibling SDK lands)

Counterpoint output is always emitted as N companion `.dawproject` files ([§7.10](#710-counterpoint-companion-emission-and-verification)). Each piece's mapping into its own file uses the same MusicTheory/Storyteller table rows (§7.1–§7.2); this subsection describes only the additional counterpoint-specific mappings that each companion file carries.

| Counterpoint output | DAWproject target |
|---|---|
| Structural template (key, tempo, form, section-level energy/tension curves) | Maps identically to the MusicTheory/Storyteller fields above — applied independently to each companion file |
| Compatibility tier (Structural Echo / Harmonic Counterpoint / Full Duet) | Encoded in the counterpoint cross-reference marker at tick 0 of each companion file ([§7.10.2](#7102-cross-reference-marker-format)); also surfaced in the companion file's `metadata.xml` `Comment` field for the composer's reference |
| Designed temporal offsets | Encoded in the counterpoint cross-reference marker at tick 0 of each companion file. The mixdown tool ([§7.10.3](#7103-verification-via-mixdown)) reads these offsets to align rendered audio stems. |
| Registral assignment per piece | Determined at melody/note emission time — each companion file's notes are bounded to its registral range; also reflected in default Pan on that file's primary melodic track |
| Voice leading validator results | Never emitted into any companion file — these are authoring-time concerns; the files carry the final notes only |
| Counterpoint set identifier | The shared `set=` identifier in each companion's cross-reference marker links the files as a family; enables the mixdown tool to verify all files being mixed belong to the same set |

### 7.4 From Style Definitions

The style definition (YAML loaded by `StyleApplication`) contributes mix-bus defaults, track set, articulation behavior, and harmonic constraints:

| Style field | DAWproject target |
|---|---|
| `modeDistribution` | Applied upstream to choose the composition's mode |
| `defaultTempo`, `defaultMeter` | `Transport.Tempo`, `Transport.TimeSignature` baselines |
| `swingFactor` | Applied at note-emission time: off-beat notes shift by `tickOffset = ticksPerBeat * swingFactor / 8` |
| `intervalPreferences` | Applied in `MelodyGenerator` |
| `formTemplates` | Candidate section structures — Storyteller picks from these |
| `tuneTypes` | Within-style variant selection (e.g., "reel" vs "jig" for celtic); affects meter and tempo range |
| `harmonyStyle.primaryCadence` | `ProgressionGenerator` uses this for cadence resolution |
| `harmonyStyle.commonProgressions` | Candidate progressions — Storyteller picks by intent |
| `harmonyStyle.diminishedPassingProbability`, `secondaryDominantProbability` | Applied in `ProgressionGenerator` |
| `dynamicsProfile.baseLevel` | Default velocity level (`vel = baseLevel * (0.85 + energy * 0.3)` modulated by energy) |
| `dynamicsProfile.dramaticContrastProbability` | Per-section dice-roll: when triggered, sudden volume-automation drops or rises at section boundaries (hold-interpolated RealPoints) |
| `articulationProfile.legatoProbability` | Per-note-pair dice-roll at emission: when triggered, the note overlaps the next slightly |
| `articulationProfile.staccatoProbability` | Per-note dice-roll: shortens duration to ~25% of the slot |
| `articulationProfile.marcatoProbability` | Per-note dice-roll: shortens duration to ~55% + boosts velocity by 15% |
| `articulationProfile.accentProbability` | Per-note dice-roll: boosts velocity by 15% without duration change |
| `masterBusConfig.eq.*` (low shelf gain, mid freq/gain, high shelf gain) | Master `Equalizer` band parameter values (see [§5.6](#56-master-bus-device-chain)) |
| `masterBusConfig.compressor.*` | Master `Compressor` settings |
| `masterBusConfig.limiterCeiling` | Master `Limiter.Threshold` (default: -0.3 dB) |
| `tracks[]` — style's canonical track set | Determines which `Track` entries are created in `Project.structure` and what roles PluginResolver queries |
| `sendLevels` — per-role reverb send level defaults | Per-track `Send.volume` initial value |
| `perNoteExpressionEnabled` | Whether any Note emits nested per-note expression timelines |

### 7.5 From Plugin Library Selections

For each track role in the style's canonical track set, PluginResolver resolves to a PluginLibraryTemplate (or null, falling back to Tier 1):

| PluginLibraryTemplate field | DAWproject target |
|---|---|
| `PluginKind = Vst3` + `PluginUuid` + `PluginName` | `<Vst3Plugin deviceID="{uuid}" deviceName="{name}" ...>` |
| `PluginKind = Vst2` + `PluginUuid` (integer) + `PluginName` | `<Vst2Plugin deviceID="{integer}" deviceName="{name}" ...>` |
| `PluginKind = Clap` + `PluginUuid` (text) + `PluginName` | `<ClapPlugin deviceID="{text}" deviceName="{name}" ...>` |
| `PluginKind = Au` + `PluginUuid` + `PluginName` | `<AuPlugin deviceID="{uuid}" deviceName="{name}" ...>` |
| `PluginKind = StudioOneNative` + `PluginName` | `<Device deviceName="{name}" deviceRole="instrument" ...>` — generic Device element |
| `StateAssetId` → Asset service binary | Embedded file under `plugins/{slug-derived-filename}.{extension}`, referenced by `<State path="plugins/...">` |
| `DeviceRole` | `Device.deviceRole` attribute (`instrument` / `noteFX` / `audioFX` / `analyzer`) |
| `PluginVendor` | `Device.deviceVendor` attribute |
| `PluginVersion` | `Device.pluginVersion` attribute (VST2/VST3/CLAP/AU inherit from `plugin` base which has pluginVersion) |
| `SupportsMpe` | Influences whether per-note expression timelines are emitted (not a DAWproject field — a ProjectBuilder decision gate) |
| `SupportsKeySwitches` | Same — influences whether articulation switches as key-switch notes or via duration-only |
| `ResponsiveCcs` | Filters which CCs the automation emitter creates Points timelines for |

When PluginResolver returns null for a role (no matching template in the library), the track still emits but without `<Devices>` — the user will see an empty instrument slot when they open the project in Studio One. This is the Tier-1 fallback: reference-only → nothing to reference.

### 7.6 From Emotional State Curves to Automation Point Streams

`EmotionToAutomation` converts the per-section emotional state into RealPoint sequences. The core formula for each target:

```
for each section s with emotionalState e and boundary times [startBar, endBar]:
    point_at_start = formula(e)
    emit RealPoint(time=startBar, value=point_at_start, interpolation="linear")
    # The point at endBar is emitted when the NEXT section starts,
    # so each section contributes one point except the last which
    # contributes the closing point.

# At the end of the composition:
emit RealPoint(time=finalEndBar, value=formula(lastSection.e), interpolation="linear")
```

The per-target formulas:

| Target | Formula | Clamp range |
|---|---|---|
| Channel volume (linear unit) | `0.6 + tension * 0.25 + energy * 0.1` | `[0.4, 1.0]` |
| CC11 expression (normalized) | `0.25 + tension * 0.55 + energy * 0.15` | `[0.15, 0.98]` |
| CC1 mod-wheel (normalized) | `0.1 + (1.0 - valence) * 0.3` + accent-at-climax boost | `[0.05, 0.85]` |
| CC74 sound-controller 5 (normalized) | `0.3 + brightness * 0.5` | `[0.15, 0.90]` |
| Reverb send Volume (linear) | `0.1 + warmth * 0.45 - (1.0 - warmth) * 0.05` | `[0.05, 0.6]` |
| Tempo (only at section boundaries if rubato is allowed per stability) | `baseline * (1 + (stability * 0 + (1 - stability) * (-0.03 + sign * 0.06)))` applied only at section boundaries | `[baseline * 0.9, baseline * 1.08]` |

The coefficients are tunable — each becomes a configuration property in `music-configuration.yaml` so the defaults can be refined without recompiling:

| Property | Default | Purpose |
|---|---|---|
| `VolumeTensionCoefficient` | `0.25` | How much tension influences volume |
| `VolumeEnergyCoefficient` | `0.10` | How much energy influences volume |
| `Cc11TensionCoefficient` | `0.55` | How much tension influences CC11 |
| `Cc11EnergyCoefficient` | `0.15` | How much energy influences CC11 |
| `Cc1ValenceCoefficient` | `0.30` | How much (1-valence) influences mod wheel |
| `Cc74BrightnessCoefficient` | `0.50` | How much brightness influences CC74 |
| `ReverbWarmthCoefficient` | `0.45` | How much warmth influences reverb send |
| `TempoRubatoAmplitude` | `0.06` | Max tempo variation when stability is low |

### 7.7 Sections With Intra-Section Arcs

Storyteller may specify intra-section emotional arcs — a crescendo within a section, a tension spike mid-verse, etc. These emit as additional RealPoints within the section's time range rather than just at boundaries:

```xml
<!-- A chorus section (64-96 bars) with a mid-chorus crescendo peak at bar 80 -->
<Points unit="normalized" id="id_chorus_cc11">
  <Target expression="channelController" channel="0" controller="11"/>
  <RealPoint time="64.0" value="0.78" interpolation="linear"/>  <!-- chorus entry -->
  <RealPoint time="80.0" value="0.95" interpolation="linear"/>  <!-- mid-chorus climax -->
  <RealPoint time="88.0" value="0.82" interpolation="linear"/>  <!-- settle back -->
  <RealPoint time="96.0" value="0.68" interpolation="linear"/>  <!-- leaving chorus -->
</Points>
```

Storyteller's GOAP actions generate these: a `BuildIntensity` action within a chorus phase produces the crescendo arc; a `Caesura` action produces a sudden drop with a short RealPoint ramp. The mapping is: each GOAP action that changes emotional state mid-section emits one or more intra-section RealPoints.

### 7.8 Key-Switch and Program-Change Emission

Instruments that use key switches to select articulations (orchestral libraries frequently do — Kontakt's "Soaring Strings" has key switches at C0 for sustain, D0 for staccato, E0 for legato, etc.) receive key-switch notes at section boundaries when MusicComposer decides to switch articulation:

```xml
<!-- Key switch note to activate "legato" articulation starting at bar 64 (chorus) -->
<Note time="63.99" duration="0.01" channel="0" key="16" vel="0.5"/>
<!-- Actual chorus notes follow, in the same clip -->
<Note time="64.0" duration="2.0" channel="0" key="67" vel="0.82"/>
```

The key-switch `key` numbers come from the `PluginLibraryTemplate.customProperties.keySwitches` dictionary (set by the user at template registration). When the template doesn't provide a key-switch map, MusicComposer doesn't emit switch notes (the instrument stays on whatever articulation its preset was captured with — typically a flagship articulation like sustain).

Program-change events are a more portable alternative for instruments that expose articulations as MIDI programs. DAWproject encodes these via `ExpressionType.PROGRAM_CHANGE`:

```xml
<Points id="id_prog_changes">
  <Target expression="programChange" channel="0"/>
  <IntegerPoint time="0.0"  value="0"/>   <!-- program 0: sustain -->
  <IntegerPoint time="32.0" value="3"/>   <!-- program 3: marcato (prechorus) -->
  <IntegerPoint time="64.0" value="2"/>   <!-- program 2: legato (chorus) -->
</Points>
```

### 7.9 Determinism and Reproducibility

Every step in the builder pipeline is deterministic given (CompositionPlan, resolved PluginLibraryTemplates, Style, SDK version):

- IdAllocator numbers strictly increasing starting from 0, depth-first traversal order
- XML attributes emitted in a fixed canonical order (matching the XSD declaration order)
- List elements emitted in source order (no sorting during emission)
- Timestamps on embedded files use the DOS epoch (1980-01-01 00:00:00) for reproducible ZIP builds
- BannouJson (already deterministic) for any metadata serialization that goes into the comment field

A seed-stable composition request produces byte-identical `.dawproject` output. This is how lib-music Redis-caches DAWproject exports.

### 7.10 Counterpoint Companion Emission and Verification

When the Counterpoint workbench exports a counterpoint set (N pieces designed to interlock at specified temporal offsets), MusicComposer emits **N companion files** — separate standalone `.dawproject` files linked via embedded cross-reference metadata — rather than a single combined project. The mixdown verification tool closes the compose-render-verify loop by aligning rendered audio stems and producing a combined WAV the composer can audition.

The rationale for companion-files-over-single-file is in [§11.2](#112-counterpoint-output-shape-same-file-or-companion-files).

#### 7.10.1 Companion File Emission

Each piece in the counterpoint set becomes its own standalone `.dawproject` following the canonical project shape from [Part 5](#part-5-dawproject-output-specification). Each companion is a complete, standalone, auditonable composition in its own right.

**Naming convention.** Files share a common base name with stable lowercase-letter suffixes:

```
villain-theme.a.dawproject
villain-theme.b.dawproject
villain-theme.c.dawproject   (if a three-way interlock)
```

The base name (`villain-theme`) comes from the composition request or an explicit `--name` flag. Suffix letters are stable — the piece designated "A" in the Counterpoint workbench is always `.a`, B is always `.b`, etc. — so that batch operations on a counterpoint set are unambiguous (glob `villain-theme.*.dawproject` matches the whole set) and so that the cross-reference markers can refer to companions by their suffix letter.

Suffix alphabet: `a`-`z` in order. A counterpoint set of more than 26 pieces is structurally implausible (no musical work benefits from 26 interlocking counterpoint lines) — if it ever happens, Phase 5 rejects the export with a clear error rather than silently overflowing.

**Shared setup.** Each companion file has its own Transport (tempo, meter), its own Structure (tracks, devices, plugin resolutions), its own Arrangement (markers, automation). Because they're designed to interlock at specific offsets, it is expected that their Transport parameters match — the counterpoint invariant ([COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md) §2.3) requires shared tempo and meter across a set. The Counterpoint workbench enforces this at authoring time; ProjectBuilder assumes the invariant holds and trusts the workbench output.

**Per-piece plugin resolution.** Each companion file's tracks are resolved independently against the Plugin Library ([§6.8](#68-resolution-semantics)). The set's pieces may use different plugin choices even when the composer wants them to — piece A might be "Strings with Kontakt + Spitfire Albion", piece B might be "Piano with Kontakt + The Giant" — the Counterpoint workbench drives the per-piece role requests, ProjectBuilder resolves each one the standard way.

#### 7.10.2 Cross-Reference Marker Format

Each companion file embeds a counterpoint metadata marker at `time=0` (same position as the key-signature marker per [§5.2.2](#522-section-markers)). Format:

```xml
<Marker time="0.0"
        name="Counterpoint: set=villain-theme-duet companions=a,b,c self=a designed-offsets=[a:0,b:8,c:16] tier=full-duet"
        color="#5b7a5b"
        id="id_counterpoint_meta"/>
```

The marker name is a structured serialization following a fixed grammar:

```
Counterpoint: set=<identifier> companions=<suffix-list> self=<suffix> designed-offsets=[<offset-spec-list>] tier=<tier>
```

Where:
- `set=` — identifier for this counterpoint family, matches across all companion files in the set. Used by the mixdown tool to verify all files being mixed belong to the same set.
- `companions=` — comma-separated list of companion suffix letters in the set (e.g., `a,b,c`).
- `self=` — the suffix letter of the file containing this marker (e.g., `a` for `villain-theme.a.dawproject`).
- `designed-offsets=` — bracketed list of per-companion offsets in bars, relative to the earliest start (which is always offset 0). Example: `[a:0,b:8,c:16]` means piece A starts at bar 0, B at bar 8, C at bar 16. The offset is measured in the shared tempo/meter.
- `tier=` — the compatibility tier per COUNTERPOINT-COMPOSER-SDK: `structural-echo`, `harmonic-counterpoint`, or `full-duet`.

The marker color `#5b7a5b` (muted green) is stable for all counterpoint-meta markers, so the composer visually distinguishes them from section markers (warm palette) and chord markers (cool blue).

**Parser resilience.** `DawProjectReader` parses the marker name via a regex-based grammar with permissive whitespace. If the marker is malformed (manual edit corruption, future format versions), the parser returns a `CounterpointMetadataParseError` with the offending marker name preserved — the mixdown tool refuses to proceed on a corrupt set and reports which file's marker is bad.

**Alternative considered.** Encoding the metadata in the Arrangement `Comment` field was considered. Rejected because (a) some DAWs hide the Arrangement comment in their UI, reducing visibility for the composer, (b) Markers round-trip more reliably through edit-in-DAW-and-save-back workflows, and (c) a visible marker lets the composer eyeball the counterpoint relationship when scanning the timeline in Studio One.

#### 7.10.3 Verification via Mixdown

After rendering each piece to audio in a DAW (the composer opens each companion file, renders to a WAV), the composer has N stem files. The mixdown tool aligns these at the designed offsets from the cross-reference markers and produces a combined WAV for auditioning:

```bash
bannou music-composer counterpoint mixdown \
    --dawprojects villain-theme.a.dawproject,villain-theme.b.dawproject \
    --stems villain-theme.a.wav,villain-theme.b.wav \
    --output villain-theme.combined.wav
```

The stems can be passed in any order — the tool matches them to their companion files by inspecting each companion's `self=` field and pairing against the filename prefix. An explicit `--pair` flag supports cases where the filenames don't match the convention (`--pair a=/path/to/custom-name.wav,b=/other.wav`).

**Algorithm:**

1. **Parse companion metadata.** For each `.dawproject` input, read the counterpoint cross-reference marker at tick 0. Validate that all files share the same `set=` identifier and that `companions=` matches across files. Reject if the files don't form a consistent set.

2. **Determine tempo.** Read `Transport.Tempo.value` from each companion. Validate that all tempos match (counterpoint invariant). Compute the tempo as bar-to-sample conversion factor: for a tempo of `T` BPM in `N/D` time signature, one bar = `(60 / T) * N` seconds = `(60 / T) * N * sampleRate` samples.

3. **Parse and validate stems.** Read each WAV file. Validate:
   - Sample rate matches across all stems (reject with error if not)
   - Channel count matches across all stems (mono or stereo — no mixed; reject if mixed)
   - Bit depth supported (16-bit PCM, 24-bit PCM, 32-bit float; reject other formats)

4. **Compute sample offsets.** For each companion, convert its `designed-offsets` entry (in bars) to samples using the tempo from step 2. `piece-a` offset 0 bars = 0 samples; `piece-b` offset 8 bars at 128 BPM in 4/4 at 44100 Hz = `8 * (60/128) * 4 * 44100 = 661,500 samples`.

5. **Allocate combined buffer.** Length = max over all stems of `(offset_samples + stem_length_samples)`. Pre-zero.

6. **Mix each stem.** For each sample index `i` in `[0, stem_length)`, add `stem[i] * stemGain` to `combined[offset_samples + i]`. Default `stemGain = 0.7` to prevent clipping from summed full-range signals; configurable via `--stem-gain` (applied uniformly) or `--stem-gains a:0.8,b:0.6` (per-stem).

7. **Normalize.** Scan the combined buffer, find the peak absolute sample value. Compute scaling factor to bring peak to `-0.3 dBFS` (factor = `10^(-0.3/20) / peak`). Apply uniformly. Default ceiling `-0.3 dBFS` is configurable via `--normalize-peak-dbfs`.

8. **Write output WAV.** 24-bit PCM at the input sample rate, same channel count as the stems. Include a `LIST/INFO` chunk identifying Bannou as the producer.

**No time-stretching or pitch correction.** The algorithm assumes rendered audio tempo matches the authored `.dawproject` tempo exactly. If the composer changed tempo in Studio One before rendering, the mixdown will misalign. See limitations in §7.10.5.

**No bus processing.** The mixdown is a linear sum. If the composer wants a polished mixdown (reverb, EQ, mastering), they do that by opening Studio One, importing the stems as audio tracks, and mixing. This tool is for *verification*, not for producing a final master.

**Per-stem panning.** Optional. `--pan a:-0.3,b:+0.3` shifts the two pieces slightly left/right in the stereo field. Useful when both pieces are mono and the composer wants spatial separation to hear them distinctly. Default: no panning (both stems sum to the same stereo position).

#### 7.10.4 Mixdown Endpoint

```yaml
/music/counterpoint/mixdown:
  post:
    operationId: mixdownCounterpointStems
    summary: Produce an aligned mixdown WAV from counterpoint-companion stems
    description: |
      Reads counterpoint metadata from companion .dawproject files, aligns rendered
      audio stems at their designed offsets, sums them sample-by-sample with configurable
      per-stem gain, normalizes to the configured peak ceiling, and returns a combined WAV.

      Used for counterpoint verification — the composer renders each piece separately
      in their DAW, then uses this endpoint to audition the overlay.
    x-permissions:
      - role: developer
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/CounterpointMixdownRequest'
    responses:
      '200':
        description: Combined WAV bytes (or Asset ID if uploadToAsset=true)
        content:
          audio/wav:
            schema:
              type: string
              format: binary
          application/json:
            schema:
              $ref: '#/components/schemas/CounterpointMixdownAssetResponse'
      '400':
        description: Set inconsistency (companions don't match across files, tempo mismatch, sample-rate mismatch, etc.) with per-file error details
```

Request body (`CounterpointMixdownRequest`):

| Field | Required | Type | Purpose |
|---|---|---|---|
| `dawprojectAssetIds` | yes | `Guid[]` | Asset service IDs for the companion `.dawproject` files. Must all be from the same counterpoint set (validated by matching `set=` in each marker). |
| `stemAssetIds` | yes | `Guid[]` | Asset service IDs for the rendered audio stems. Order doesn't have to match the dawproject order — stems are matched to companions via filename prefix or explicit `stemMap`. |
| `stemMap` | no | `Dictionary<string, Guid>?` | Explicit mapping from suffix letter (`"a"`, `"b"`, ...) to stem Asset ID. Overrides filename-prefix matching. |
| `stemGain` | no | `double?` | Default per-stem gain before summing. Default: `0.7`. |
| `stemGainMap` | no | `Dictionary<string, double>?` | Per-suffix gain overrides. Default: unset (uses `stemGain` for all). |
| `panMap` | no | `Dictionary<string, double>?` | Per-suffix stereo pan (-1.0 = full left, +1.0 = full right). Default: unset (no panning). |
| `normalizePeakDbfs` | no | `double?` | Peak ceiling after normalization. Default: `-0.3` dBFS. |
| `outputBitDepth` | no | `enum { Pcm16, Pcm24, Pcm32, Float32 }?` | Output WAV format. Default: `Pcm24`. |
| `uploadToAsset` | no | `bool?` | If true, upload combined WAV to Asset service and return an ID; otherwise stream bytes. Default: `false`. |

#### 7.10.5 Limitations

Honest scope boundaries for Phase 5:

1. **WAV-only stem format.** PCM-encoded `.wav` files (16/24/32-bit) and 32-bit float WAVs. FLAC, OGG, MP3, AIFF are out of scope for Phase 5 — the composer converts to WAV first if needed. Handling compressed formats requires a decoder library, which is avoidable complexity for a verification tool. Expanding to other formats is a possible future addition but is not planned.

2. **Sample rate must match across stems.** If one stem is 48 kHz and another is 44.1 kHz, the tool rejects the request with a clear error. The composer resamples to a common rate first using a proper resampler (Sox, FFmpeg, etc. — not something Bannou should reinvent).

3. **Channel count must match across stems.** All mono or all stereo. Mixed-channel sets reject with a clear error. The composer bounces mono stems to stereo (or vice versa) first.

4. **No time-stretching or tempo correction.** Alignment assumes the rendered audio tempo matches the authored `.dawproject` tempo exactly. If the composer changed tempo in their DAW before rendering (or used a DAW's time-stretch feature during render), the mixdown will misalign. Documented; no automatic detection.

5. **No perceptual level matching.** Per-stem gain is a simple multiplier applied before summing. There is no LUFS integration, no RMS matching, no loudness normalization. The default `stemGain=0.7` prevents clipping for typical material; adjust per-stem manually if one piece is authored at a significantly different level than another.

6. **Simple linear mix.** No bus processing, no panning effects beyond left-right stereo placement, no reverb send. If the composer wants a polished auditon, they do that in their DAW.

7. **No streaming.** The endpoint returns the full WAV in memory. For very long counterpoint sets (>30 min combined), consider streaming in a future revision.

8. **Marker grammar is fragile to hand-edit.** The cross-reference marker name follows a strict grammar. If the composer hand-edits the marker in Studio One (e.g., to annotate it with notes), the parser will fail on re-read. Documented; composer should leave counterpoint-meta markers untouched. Future enhancement: store the metadata in both the marker and the `Comment` field as belt-and-suspenders.

---

## Part 8: Gaps and Workarounds

DAWproject is rich but not complete. This section enumerates every gap discovered during schema analysis, ranked by impact, with each gap's workaround. The list is meant to be exhaustive — if you encounter a gap not documented here, update this section rather than inventing a silent workaround in code.

### 8.1 No First-Class Chord Symbol Type

**Gap.** DAWproject has no `ChordSymbol` element. Chord annotations cannot be attached to a specific time point as a typed record the way they can in MIDI files (via text events) or in some DAWs' chord tracks.

**Impact.** Moderate. The composer loses the nice visual of roman-numeral and chord-symbol labels floating above the timeline. For a Bb-minor progression `i - iv - VII - VI` emitted over 16 bars, the composer can read the harmony off the Chord Pad track's notes but doesn't see the roman-numeral analysis.

**Workaround.** Encode chord symbols as `Marker` entries when `dawprojectOptions.annotateChords = true`. Example:

```xml
<Marker time="0.0"   name="i (Bbm)"     color="#6a7a9a" id="..."/>
<Marker time="4.0"   name="iv (Ebm)"    color="#6a7a9a" id="..."/>
<Marker time="8.0"   name="VII (Ab)"    color="#6a7a9a" id="..."/>
<Marker time="12.0"  name="VI (Gb)"     color="#6a7a9a" id="..."/>
```

Default is **off** because chord markers at every chord change clutter the Markers lane and make section markers harder to find. When enabled, chord markers use a distinct color to separate them visually from section markers. A future enhancement could put chord markers on a separate Markers lane if DAWproject ever supports multiple parallel marker lanes (it doesn't today).

**Studio One-specific alternative.** Studio One has a proprietary Chord Track. DAWproject does not round-trip Chord Track data (neither format describes it). If the user wants chord-track support in Studio One, they manually enable Studio One's "Detect from Notes" feature on the Chord Pad track after opening the project — Studio One analyzes the notes and populates the Chord Track itself. MusicComposer doesn't control this.

### 8.2 No First-Class Articulation or Dynamic Markings

**Gap.** No Italian-notation primitives (pp, p, mf, f, ff, cresc., dim., staccato, legato, marcato, accent). DAWproject describes what notes *do*, not what they *look like on a score*.

**Impact.** Low for DAW composers, high for score-based composers. A DAW composer's articulation experience is: notes play staccato because they're short, notes play legato because they overlap, notes play marcato because they're accented. Visual symbols are orthogonal.

**Workaround.** MusicComposer realizes articulations as performance data (see [§5.4.2](#542-notes)):

| Articulation | Realization |
|---|---|
| Staccato | Shortened duration (~25% of rhythmic slot) |
| Legato | Duration slightly overlaps next note + higher release velocity |
| Marcato | Shortened duration (~55%) + velocity boost (~15%) |
| Accent | Velocity boost (~15%) without duration change |
| Crescendo | RealPoint ramp on CC11 or channel volume |
| Diminuendo | RealPoint ramp (descending) |
| pp / p / mf / f / ff | Scales the dynamic baseline (velocity + CC11) applied as a step automation point |

The notation is lost; the performance is faithful. Score-view composers using Studio One's Notion integration won't see notated articulation marks — they'd see the performance result on the piano roll. This is the standard DAW compromise and not new with MusicComposer.

### 8.3 No First-Class Key Signature Element

**Gap.** DAWproject's schema has `TimeSignature` / `TimeSignaturePoint` (first-class) but nothing analogous for key signature. The Bitwig reference example shows DAWs embedding key signature in their native metadata but not surfacing it in DAWproject.

**Impact.** Low. Studio One infers key from notes for its internal key display; the composer's Key Lab / scale-aware features work fine. Humans reading the timeline benefit from seeing "Bb Minor" annotated, but Studio One already shows the inferred key in its timeline ruler.

**Workaround.** First `Marker` at `time=0` named `"{Tonic} {Mode}"` (see [§5.2.2](#522-section-markers)). Modulations get additional markers. Zero cost, good-enough readability.

### 8.4 No Studio One Proprietary Feature Round-Trip

**Gap.** Studio One has several features not in the DAWproject schema: Arranger Track (section-level arrangement with drag-to-reorder), Scratch Pads (sketchpad areas for trying ideas), Chord Track (auto-populated chord analysis), Scoring Tools (advanced notation view), Sound Variations (high-level articulation switching), and various mixer workflow features.

**Impact.** Moderate if the composer relies on these features. None of them prevent opening or playing a Bannou-generated `.dawproject`; they just don't get populated from the DAWproject import.

**Workarounds.**

- **Arranger Track** → the composer enables it manually and drags section boundaries from Markers. Studio One can auto-populate Arranger sections from Markers in some versions; the user may need to do this once per project.
- **Scratch Pads** → not compositional; not relevant to export.
- **Chord Track** → "Detect from Notes" on the Chord Pad track (manual, one click).
- **Scoring Tools** → opens automatically on MIDI tracks; renders the performance as notation; articulation marks are absent (see §8.2).
- **Sound Variations** → if the PluginLibraryTemplate is a Sound Variations-aware plugin, key switches are routed through the Sound Variations slots by Studio One automatically. Articulation selection via program-change events (see [§7.8](#78-key-switch-and-program-change-emission)) also works.

### 8.5 No PreSonus-Native Plugin State Authoring

**Gap.** We cannot programmatically author Mai Tai / Presence XT / Impact XT / Mojito / Sample One XT state. We don't know the binary format.

**Impact.** High if the user's workflow depends on these instruments — they are precisely the PreSonus-subscription-exclusive content.

**Workaround.** The capture-once pattern ([§3.4](#34-the-capture-once-plugin-library-pattern)): the user saves Mai Tai / Presence XT presets from Studio One, registers them with the Plugin Library as `StudioOneNative` templates, and MusicComposer embeds the captured state files unchanged into future DAWproject exports. Effectively, the user does the work once per patch; Bannou reuses it forever.

**When the user wants a patch they haven't captured yet.** The template resolver returns 404 for that role; ProjectBuilder falls back to Tier 1 (instrument name only, no state). Studio One opens the project with Mai Tai loaded in its default state; the user manually selects a patch in the Mai Tai GUI. This is worse than the captured case but still better than nothing.

### 8.6 Missing Plugins on the Consumer's Machine

**Gap.** The `.dawproject` references a plugin by UUID or name. If the user opens it on a machine where that plugin isn't installed, Studio One marks the device as "not loaded" and leaves the track intact without playback.

**Impact.** Low within a single-user workflow (the user has the plugins they registered). High if projects move between machines (collaborator doesn't have Spitfire Albion, etc.).

**Workaround.** Two options:

1. **Same-machine-only discipline.** MusicComposer-generated projects are intended for the machine of the user whose plugin library generated them. When sharing a project between machines, the receiving user either installs the same plugins or accepts missing-plugin warnings.

2. **Fallback-plugin metadata on PluginLibraryTemplate.** Future enhancement (not Phase 1): the template records a fallback-plugin identity — "if Kontakt+Spitfire isn't available, fall back to Presence XT with the 'Ensemble Strings' patch". ProjectBuilder inserts both as alternative Devices, with the primary enabled and the fallback disabled, so Studio One can swap if the primary fails to load. Deferred — it complicates the schema and is solved cheaply today by the "install the same plugins" approach.

### 8.7 VST2 Deprecation

**Gap.** VST2 is deprecated by Steinberg (SDK removed 2018; existing licenses grandfathered). New VST2 plugins are rare; existing VST2 plugins are increasingly replaced by VST3 equivalents. DAWproject supports VST2 (the `Vst2Plugin` element), but Studio One Pro 7 may not load VST2 plugins in all configurations.

**Impact.** Low. Most modern VST plugins ship VST3. VST2 support is best-effort.

**Workaround.** The PluginLibraryTemplate has a `PluginKind` enum with `Vst2` and `Vst3` as distinct values. At registration, users specify which format their plugin is. At export, MusicComposer emits the appropriate element type. If the user has a VST2 plugin that Studio One can't load, they re-capture the state using the VST3 equivalent and register it as a new template (or replace the existing one).

### 8.8 No Clip Launcher / Scene Coverage

**Gap.** DAWproject supports Clip Launcher scenes (Bitwig/Ableton-style). Studio One has a Scratch Pads feature that overlaps conceptually but isn't clip-launcher-shaped. Bannou's music output doesn't produce scene-based content anyway.

**Impact.** None for current scope. Arcadia and the Bannou music stack are arrangement-based, not clip-launcher-based.

**Workaround.** MusicComposer emits `<Scenes/>` (empty) and never populates it. If a future Bannou feature needs clip-launcher output (e.g., "live-playable loop packs for an in-game DJ minigame"), this can be added without changing any of the rest of the format.

### 8.9 Audio Clips Not Generated in Phase 1

**Gap.** DAWproject supports audio clips with full warping, fades, and crossfades. MusicComposer Phase 1 produces only MIDI + automation — no audio stems.

**Impact.** None for the DAW workflow. Studio One renders the MIDI through the embedded plugins to produce audio on export. MusicComposer doesn't need to ship audio to produce a playable session.

**Workaround.** N/A — this isn't a gap, it's a scope decision. A future MusicRenderer plugin (if Shape 2 is ever revisited — see [Part 13](#part-13-future-extensions)) could produce pre-rendered stems and embed them in the DAWproject as audio clips alongside the MIDI. Today, audio generation is the user's DAW's job.

### 8.10 No Lyrics

**Gap.** No Lyric element. DAWproject describes instrumental music.

**Impact.** None for current scope. Bannou's music system doesn't generate lyrics (and the storyline/narrative system isn't wired to musical content).

**Workaround.** If a future Bannou feature generates lyrics for vocal tracks, they'd live in a Marker comment or a track-level comment. For now, the composer writes their own lyrics in their DAW if they're producing vocal music.

### 8.11 No Per-Track MIDI Channel Routing for Multi-Timbral Plugins

**Gap.** A Kontakt instance can host multiple sample libraries on different MIDI channels (Kontakt's multi-instrument setup). DAWproject expresses each Track with a single Channel containing one Devices list. You can't say "this Track's notes route to MIDI channel 3 of that plugin instance on another Track."

**Impact.** Low. The workaround (one plugin instance per track) is CPU-costlier at playback time but functionally equivalent for composing.

**Workaround.** PluginResolver always returns a dedicated plugin instance per track. Multi-timbral sharing is the user's decision post-import if they want to consolidate for CPU reasons.

### 8.12 Tempo Automation Is Gapped at Section Transitions Only

**Gap.** DAWproject's `TempoAutomation` can have arbitrarily-dense points — this isn't a format gap. It's a Bannou design decision: we emit tempo points only at section boundaries, not continuously. Slow rubato within a section isn't encoded.

**Impact.** Low for most styles. Specific composition traditions (late-romantic symphonic, certain jazz ballad phrasings) rely heavily on intra-section rubato — these compositions would benefit from denser tempo automation.

**Workaround.** If a future style or composition type needs intra-section rubato, the formula in [§7.6](#76-from-emotional-state-curves-to-automation-point-streams) can be extended — the stability dimension already gates whether points at section boundaries exist; a dense-rubato mode would add additional points within sections governed by Storyteller's phrasing-level intents. Not urgent.

### 8.13 XSD Validation Strictness

**Gap.** The DAWproject XSD enforces types and ranges. Some DAW-specific extensions (future PreSonus proprietary features expressed via unknown elements) would fail strict validation.

**Impact.** Low for MusicComposer output (we only emit standard DAWproject). Potentially relevant for round-trip validation: if the user saves a Bannou-generated project in Studio One and Studio One adds proprietary elements, the re-saved file may not validate against the stock XSD.

**Workaround.** DawProjectReader validates on import with the standard XSD. On validation failure, it falls back to lenient parsing (best-effort) and logs warnings. This mirrors Bitwig's reference behavior.

### 8.14 Round-Trip Fidelity Is Not Guaranteed By DAW Vendors

**Gap.** DAWproject is an **exchange** format. Each DAW maps DAWproject to its internal representation on open and back again on save. Perfect round-tripping is aspirational; there's no guarantee that a Bannou-generated `.dawproject` → Studio One open → Studio One save → load back into Bannou yields byte-identical output.

**Impact.** Moderate. The round-trip test we plan for Phase 6 ([§10.6](#106-phase-6-round-trip-validation--integration-testing)) is a best-effort fidelity check, not a correctness gate.

**Workaround.** Round-trip-variance tolerance: ID numbering can change (Studio One renumbers on save), plugin state can re-serialize in a canonically-equivalent form, redundant markers can consolidate. The test compares *semantic equivalence* — same tracks, same notes, same automation points (within floating-point tolerance) — not textual equivalence. This matches what Bitwig does with their own round-trip test suite.

---

## Part 9: Licensing and Legal

### 9.1 DAWproject License

The `bitwig/dawproject` repository is **MIT-licensed** (`Copyright (c) 2020 Bitwig`). The license is in `~/repos/dawproject/LICENSE`. MIT is a permissive license that allows use, modification, and redistribution including in commercial products, with the single requirement to preserve the copyright notice.

**T18 compliance.** T18 (Licensing Requirements) approves MIT as a preferred permissive license — MIT is first in the approved list. DAWproject is safe to consume as a reference for MusicComposer's C# port. The reference Java library is not linked into Bannou (we port to C#), but even if it were (as a future Java-interop option), MIT linking is unambiguously permitted.

### 9.2 Captured Plugin Presets — User's Own Files

Plugin Library templates contain captured plugin state files (`.vstpreset`, `.preset`, etc.) that the user saved from their legitimately-licensed copies of the plugins. The Bannou server stores these files in the Asset service and embeds them in generated `.dawproject` files.

**Copyright analysis.** A saved plugin state is a data file authored by the user's DAW at the user's request, containing parameter values and (often) references to factory sample content by name. The state file itself is typically:

- Generated and owned by the user (they made the patch, even if it uses factory samples)
- Legally copyable within the user's own workflow (saving a preset to a backup folder, emailing it to oneself, etc.)

Bannou storing and re-embedding these files is equivalent to the user placing them in an organized folder for their own reuse. No redistribution to third parties occurs within a single-user deployment.

**Multi-user deployment caveat.** If a Bannou deployment is shared across multiple users (a team project, a hosted service), the Plugin Library templates are accessible to every user with `developer` role on that game service. This is equivalent to the user "sharing patches with their team" — legally comparable to using Kontakt's patch-sharing feature within a studio. It does NOT extend to redistributing captured state outside the team; the templates live on the Bannou server and are referenced by opaque Asset IDs, not downloadable URLs. A sophisticated user could certainly extract the state files from generated `.dawproject` files (they're just ZIP entries), but that's the same access the user already has to their own saved presets.

**Red-lines.** The following would cross into genuine copyright concerns and are **not permitted**:

- Redistributing Plugin Library templates across Bannou deployments (across different organizations' installations)
- Selling access to a pre-populated Plugin Library
- Shipping Bannou with a default library containing captured presets from commercial plugins
- Publishing a public registry of captured presets

All of these would represent the Bannou project (not individual users) distributing copyrighted material. None are in scope.

### 9.3 VST3 SDK Considerations

**VST3 is a Steinberg-licensed format.** The VST3 SDK is available under either a GPLv3 license or a proprietary Steinberg license. Third-party plugins implement VST3 under one of these.

MusicComposer does not implement VST3. It references VST3 plugins by UUID in DAWproject output and embeds `.vstpreset` files produced by the plugins themselves. No VST3 source code or header files are linked into Bannou. No VST3 SDK dependency is added.

**T18 compliance.** No issue. Not linking against the VST3 SDK means the SDK's licensing terms don't apply to Bannou.

### 9.4 PreSonus and Studio One

Studio One is proprietary software. Bannou does not:

- Include Studio One in its distribution
- Bundle Studio One assets, factory content, or proprietary instruments
- Reverse-engineer Studio One's internal protocols (we rejected that path; see [§2.1](#21-rejected-studio-one-as-a-headless-render-server-the-houdini-parallel) and [§2.2](#22-rejected-studio-one-internal-javascript-scripting-lukas-ruschitzkas-path))
- Distribute PreSonus-native preset content beyond what individual users captured from their own licensed copies

The DAWproject format itself was co-developed by PreSonus and Bitwig and is openly licensed. Producing DAWproject files that Studio One can consume is exactly the intended use of the format — no copyright or trademark issue.

**Studio One-native instruments (Mai Tai, Presence XT, etc.).** These are PreSonus proprietary. The Plugin Library handles them via the capture-once pattern: the user's Studio One license is the authority for using these instruments; Bannou just stores and re-embeds state files the user produced. PreSonus's Studio One EULA permits users to save and reuse their own presets — the capture-once workflow does nothing the EULA forbids.

### 9.5 Generated Music Output

When MusicComposer produces a `.dawproject`, the output is:

- **MIDI data** (note sequences, automation points, tempo) — generated from Bannou's music system. Not copyrightable as individual notes; copyrightable as a composition when original. Composition copyright belongs to the user (they commissioned the generation with their seed and style choices).
- **DAWproject XML structure** — format-neutral, not copyrightable.
- **Embedded plugin state files** — user's own captures (see §9.2).

The composite `.dawproject` file is a work the user generated through Bannou's tooling, analogous to a file generated through any DAW. Ownership and copyright are the user's.

Studio One's output (the mixed audio after the user exports) is the user's work — Bannou has no ongoing claim.

### 9.6 Telemetry and Privacy

The Plugin Library stores filenames and plugin identities. These are not personally identifying beyond revealing the user's plugin collection preferences. Standard Bannou event telemetry (T5 lifecycle events for plugin-library-template lifecycle) does not expose any content from captured state files; only the template ID, plugin identity, and metadata.

**Account deletion.** Plugin Library templates are game-service-scoped, not account-scoped. Account deletion (T28 Account Deletion Cleanup Obligation) does not delete the Plugin Library — the library belongs to the game service, not the user. This matches how other content-template services work (Item Templates, Quest Definitions, etc.).

### 9.7 Summary Checklist

| Concern | Status |
|---|---|
| DAWproject format license (MIT) | ✅ T18-compliant |
| Reference Java library usage | ✅ Not linked; ported to C# |
| VST3 SDK dependency | ✅ Not linked; no SDK dependency |
| Steinberg licensing | ✅ No VST3 implementation in Bannou |
| User's plugin preset files | ✅ Equivalent to user's own preset management |
| PreSonus Studio One integration | ✅ Standards-based (DAWproject), not reverse-engineered |
| PreSonus-native preset formats | ✅ Capture-once; user's Studio One license is authority |
| Generated composition copyright | ✅ Belongs to the user |
| Multi-user deployment (team projects) | ⚠️  Acceptable within the team; treat as within-organization sharing |
| Cross-deployment redistribution of templates | ❌ Not permitted (out of scope by policy) |
| Public template registry | ❌ Not permitted (out of scope) |

---

## Part 10: Implementation Phases

**There is no MVP, no "minimal viable" intermediate state, and no "good enough" stopping point.** The SDK is complete when all six phases are complete and all their exit criteria are met; before that point, the SDK is incomplete and not usable for its intended purpose. A `.dawproject` file emitted before Phase 5 can't handle counterpoint sets; a Plugin Library without bulk import (Phase 2) can't be populated at practical scale; a ProjectBuilder without StorytellerAdapter (Phase 3) has no input pipeline. Every phase is load-bearing for the whole.

**Phases represent dependency ordering, not scheduling.** Phase N's position in the list tells you what must be finished before it starts, not when it ships. Implementation time for each phase is not estimated in this document and will not be — the work takes as long as it takes, and premature estimation produces bad planning.

**Each phase's exit criteria define when that phase is done.** Not "substantially done," not "done enough to start the next phase in parallel," but done — tests pass, structural validators pass, the capability the phase introduces works end-to-end. A phase that's 90% done is not done; it's a phase in progress.

The six phases below each describe their goal, deliverables, exit criteria, dependencies, and risks. Read them as a dependency graph, not a timeline.

### 10.1 Phase 1: `Bannou.MusicComposer` DAWproject DOM (C# Port)

**Goal.** A working C# library that reads and writes DAWproject files. Zero knowledge of MusicTheory, Storyteller, Bannou conventions, or plugin library — just a faithful port of the DAWproject format.

**Deliverables.**
1. Project structure under `sdks/music-composer/` with `music-composer.csproj` targeting `net8.0`. NuGet package identity `BeyondImmersion.Bannou.MusicComposer`.
2. Full DOM types under `Dom/` mirroring the Java class hierarchy (Project, Application, Transport, Track, Channel, Device, Vst3Plugin, Clip, Note, Points, Marker, etc. — see [§4.3](#43-project-structure) for the full listing).
3. All enums (`Unit`, `TimeUnit`, `MixerRole`, `SendType`, `ContentType`, `Interpolation`, `ExpressionType`, `DeviceRole`, `EqBandType`).
4. `DawProjectWriter` — serializes a `Project` + `MetaData` + embedded files to `.dawproject` bytes. Uses `System.Xml.Serialization` with attributes matching the DAWproject XSD element/attribute names. Deterministic ZIP assembly (sorted entries, null timestamps).
5. `DawProjectReader` — deserializes `.dawproject` bytes back to a `Project` + `MetaData` + embedded-file byte arrays. Used for round-trip tests.
6. `IdAllocator` — deterministic ID generator for XML IDs.
7. Unit tests in `sdks/music-composer.tests/` covering the full writer/reader round trip using fixture projects (start with Bitwig's reference `test.dawproject` as the gold-standard input).
8. Validate-against-XSD test: write a project, validate against the DAWproject XSD (copied from `~/repos/dawproject/Project.xsd` and `MetaData.xsd`), fail loudly on any validation error.

**Exit criteria.**
- Phase 1 tests pass
- `DawProject.loadProject(bitwigReferenceBytes)` and re-serialize produces XML that validates against the XSD
- Round-trip a constructed project through writer → reader → equivalence check passes

**Dependencies.** None besides .NET 8 and Bannou Core.

**Risks.**
- XML serialization attribute incompatibilities between Java JAXB (reference) and C# `System.Xml.Serialization` (target). Some attributes that JAXB implies may need explicit `XmlAttribute` in C#. Mitigation: diff-test against the reference Bitwig test output XML byte-for-byte after manual normalization.
- ZIP container reproducibility quirks (timestamp, compression method). Mitigation: use `System.IO.Compression.ZipArchive` with `DeflateLevel.Optimal`, explicit zero timestamps, alphabetical entry order.

### 10.2 Phase 2: Plugin Library Service Endpoints + Bulk Import + Family Registry

**Goal.** `lib-music` gains the full Plugin Library surface: storage, all CRUD endpoints, resolution, bulk-import from directory tree, capture-from-dawproject, family registry (shipped + overrides). The Plugin Library works as a standalone capability, without any ProjectBuilder integration yet.

**Deliverables.**

1. **Shipped reference files committed to the Bannou repo:**
   - `schemas/music-role-families.yaml` — the 12-family set from [§6.2.2](#622-default-family-set-shipped-with-bannou). Validated by CLI tab-completion and bulk-import validation.
   - `schemas/music-role-taxonomy.yaml` — conventional sub-segment patterns per family from [§6.2.3](#623-conventional-sub-segment-patterns). Advisory, not enforced at resolve time.
   - `schemas/music-plugin-families.yaml` — the initial Plugin Family Registry per [§6.3.1](#631-source-of-truth). Target ~20-30 entries covering the most-common commercial and free plugins per family.

2. **Schema additions to `schemas/music-api.yaml`:** all endpoints from [§6.6](#66-api-surface), with full request/response models, `x-permissions`, `description` on every property (T19/CS1591 compliance). Endpoints:
   - `POST /music/plugin-library/register`
   - `POST /music/plugin-library/get`
   - `POST /music/plugin-library/list`
   - `POST /music/plugin-library/resolve` (hierarchical walk-up per [§6.8.1](#681-algorithm))
   - `POST /music/plugin-library/deprecate`
   - `POST /music/plugin-library/clean-deprecated`
   - `POST /music/plugin-library/bulk-import`
   - `POST /music/plugin-library/capture-from-dawproject`
   - `POST /music/plugin-library/family-registry/get`
   - `POST /music/plugin-library/family-registry/override`

3. **Schema additions to `schemas/music-service-events.yaml`:** `x-lifecycle` entry for `MusicPluginLibraryTemplate` with `deprecation: true` and `instanceEntity: MusicPluginLibraryTemplate` (self-referential for Category B instance tracking via usage counter), `x-event-publications` for created/updated/deleted.

4. **Schema additions to `schemas/music-configuration.yaml`:** `PluginLibraryMaxStateFileSizeBytes`, `PluginLibraryCleanupGracePeriodDays`, `PluginLibraryResolveCacheTtlSeconds`, plus a new `PluginFamilyRegistryShippedPath` (default: `schemas/music-plugin-families.yaml`) for the YAML location.

5. **Schema additions to `schemas/state-stores.yaml`:** `plugin-library-templates` (MySQL), `plugin-library-templates-reverse-index` (MySQL), `plugin-library-lock` (Redis), `plugin-family-registry-overrides` (MySQL).

6. **Run the generator** (`generate(script: "all")` or minimal set covering music) to produce controllers, models, clients, events, state store definitions.

7. **Implement the endpoints in `plugins/lib-music/MusicService.PluginLibrary.cs`** (new partial class file). Follow the standard patterns: T6 state store key builders, T7 per-item error isolation in batch endpoints, T8 return tuple pattern, T30 telemetry spans, T31 Category B deprecation lifecycle.

8. **Implement the hierarchical resolver** in `plugins/lib-music/Services/HierarchicalRoleResolver.cs`. Walk-up algorithm per [§6.8.1](#681-algorithm). Unit tests cover: exact match, each fallback level, stylistic-tag scoring, profile bias, MPE-needs gate, recency tiebreaker, Tier 1 family-registry consultation when hierarchy exhausted.

9. **Implement bulk-import processing** in `plugins/lib-music/Services/BulkImportProcessor.cs`:
   - Parse VST3 preset files to extract embedded class ID (VST3 UUID) — see the VST3 preset format spec; class ID is the first 16 bytes of the preset header
   - Plugin-family-registry name matching for non-VST3 presets (VST2 `.fxp`/`.fxb`, CLAP `.clap-preset`, AU `.aupreset`, Studio One-native `.preset`)
   - Directory-path → role-slug inference with the family-segment validation
   - Sidecar `.yaml` parsing for per-file overrides
   - Dry-run mode that returns the proposed registrations table without writing
   - Overwrite mode with explicit `--overwrite` flag

10. **Implement capture-from-dawproject** in `plugins/lib-music/Services/DawprojectCaptureProcessor.cs`:
    - Uses the Phase 1 `DawProjectReader` to parse `.dawproject` inputs
    - Enumerates every channel's devices across the structure
    - For each device with embedded state, extracts state bytes + plugin identity
    - Role inference per the three modes (`from-track-name`, `from-prompt`, `from-file`)

11. **Plugin Family Registry service** in `plugins/lib-music/Services/PluginFamilyRegistryService.cs`:
    - Loads shipped YAML at startup
    - Merges with per-game-service overrides at resolve time
    - `GetEffective(gameServiceId)` returns the merged view
    - Override endpoint validates family name against `schemas/music-role-families.yaml`

12. **Wire `lib-music` to reference `lib-asset`** (soft dependency) for state file storage. Captured state files go through `IAssetClient.UploadAsync`.

13. **Unit tests in `plugins/lib-music.tests/`** covering register, get, list, resolve (all fallback levels), deprecate, clean-deprecated, bulk-import (with fixture directory tree), capture-from-dawproject (with fixture `.dawproject`), family-registry get/override — all with capture-pattern mock verification.

14. **CLI tool `tools/MusicPluginLibraryCli/`** implementing:
    - `bannou music-library bulk-import --dir DIR [--dry-run] [--tag T1,T2] [--filter GLOB] [--overwrite] [--plugin-root NAME] [--role-prefix PREFIX] [--profile NAME]`
    - `bannou music-library capture-from-dawproject --file FILE [--role-inference MODE] [--dry-run]`
    - `bannou music-library register --role R --plugin-kind K --plugin-uuid U --plugin-name N --state-file F`
    - `bannou music-library list [--role R] [--tag T] [--profile P]`
    - `bannou music-library get --template-id ID`
    - `bannou music-library deprecate --template-id ID [--reason R]`
    - `bannou music-library family-registry list [--game-service-id ID]`
    - `bannou music-library family-registry override --family F --plugin ...`

15. **Structural test** asserting the Category B reverse-index usage counter increments on resolve and decrements on re-resolve-to-different-template.

**Exit criteria.**
- Build passes with no new warnings
- Structural tests pass (permission matrix, key builders, event publishers, Category B conformance, reverse-index usage-counter maintenance)
- Unit tests pass
- User can execute the full bulk-import workflow end-to-end against a fixture directory tree and verify the registered templates via list
- User can execute capture-from-dawproject against Bitwig's reference `test.dawproject` and see plugin extraction work

**Dependencies.** Phase 1 is required for the DAWproject DOM types that capture-from-dawproject consumes. Bulk-import from directory is independent of Phase 1.

**Risks.**
- Category B deprecation + reverse-index usage counter is new territory (current Category B services use instance-entity counts; this one uses a usage counter). Mitigation: design the reverse-index maintenance semantics carefully, document in the implementation map, add the structural test above.
- VST3 preset format parsing — the class ID extraction relies on the documented preset header structure. Mitigation: use the VST3 SDK's reference preset-format documentation (publicly available, no linking required); test against a handful of real `.vstpreset` files from diverse plugins before declaring the parser robust.
- Plugin Family Registry content curation — the shipped entries need to be informed by real-world plugin popularity. Mitigation: consult the primary consumer's actual plugin library for the initial entries; accept that the registry will grow via PR over time.

### 10.3 Phase 3: `ProjectBuilder` + Adapters

**Goal.** The transformation pipeline from CompositionPlan to Project DOM. This is the core of MusicComposer's value.

**Deliverables.**
1. `Plan/` types: `CompositionPlan`, `TrackPlan`, `NotePlan`, `PerNoteExpression`, `AutomationPlan`, `SectionPlan`, `SendPlan`, `PluginRoleRequirement`, `MasterBusConfig`.
2. `StorytellerAdapter.ToCompositionPlan(CompositionResult, Style)` — maps Storyteller output to CompositionPlan. Implements the per-section emotional-state → automation-point-stream derivation per [§7.6](#76-from-emotional-state-curves-to-automation-point-streams).
3. `StyleApplication` — reads a style YAML, applies style-specific decisions (track set, mix-bus defaults, articulation probabilities).
4. `PluginResolver` — client for `POST /music/plugin-library/resolve`, handles the 404-fallback-to-Tier-1 logic.
5. `EmotionToAutomation` — extracted per-emotional-dimension formulas as testable helpers.
6. `ProjectBuilder.Build(CompositionPlan, ResolvedPlugins, Style)` → `Project`. Constructs the full DOM per [§5](#part-5-dawproject-output-specification).
7. Unit tests in `sdks/music-composer.tests/`:
   - Given a mock CompositionResult, StorytellerAdapter produces a deterministic CompositionPlan
   - Given a CompositionPlan + mock resolver output, ProjectBuilder produces a deterministic Project DOM
   - `EmotionToAutomation` per-formula tests (boundary values, clamp ranges)
   - `StyleApplication` tests for each shipped style

**Exit criteria.**
- Unit tests pass
- A known CompositionResult fixture produces a stable Project DOM (diff-testable against a committed golden fixture)
- All T25 enum boundary mappings between Style YAML types, MusicStoryteller types, and DAWproject DOM types have `EnumMappingValidator` tests (per TESTING-PATTERNS.md)

**Dependencies.** Phases 1 and 2 must be complete.

**Risks.**
- Emotional-state-to-automation formula tuning. The coefficients in [§7.6](#76-from-emotional-state-curves-to-automation-point-streams) are educated guesses. Phase 3 ships them as defaults; Phase 6 validation may reveal they need adjustment. Mitigation: expose coefficients as configuration properties so they can be refined without SDK revision.

### 10.4 Phase 4: `/music/export/dawproject` Endpoint

**Goal.** Wire the existing `/music/generate` pipeline to the new ProjectBuilder, add the export endpoint.

**Deliverables.**
1. Schema addition to `schemas/music-api.yaml`: `POST /music/export/dawproject` endpoint. Request model extends `GenerateCompositionRequest` with `pluginLibraryProfileId` (nullable string) and `dawprojectOptions` (nested object with `annotateChords: bool`, `includeTempoAutomation: bool`, etc. — all with safe defaults). Response is a binary stream content type.
2. Implementation in `plugins/lib-music/MusicService.Export.cs` (new partial class file):
   - Call the existing `GenerateCompositionAsync` to get a CompositionResult
   - Use `StorytellerAdapter.ToCompositionPlan` to transform
   - Call `PluginResolver.ResolveAll` for each PluginRoleRequirement
   - Call `ProjectBuilder.Build` to get a Project DOM
   - Fetch embedded state files from Asset service
   - Call `DawProjectWriter.Write` to produce bytes
   - Return bytes as response body
3. Redis caching of DAWproject output by (seed + style + pluginLibraryProfileId) — reuse the existing composition cache store but add a separate DAWproject cache key prefix.
4. Unit tests verifying the endpoint orchestrates correctly (capture-pattern mocks for each dependency).

**Exit criteria.**
- Build passes
- Manual test: `curl -X POST http://localhost:5012/music/export/dawproject --data @request.json --output composition.dawproject` produces a valid DAWproject file
- File opens in Studio One without errors (human verification)

**Dependencies.** Phase 3.

**Risks.**
- Binary response body pattern. lib-music hasn't emitted binary responses before. Mitigation: reference lib-asset which does, copy its controller response pattern.

### 10.5 Phase 5: Counterpoint Companion Emission + Mixdown Verification

**Goal.** Two independently-valuable capabilities shipped together: (1) MusicComposer emits counterpoint sets as N companion `.dawproject` files with embedded cross-reference metadata when the Counterpoint workbench requests an export; (2) the mixdown verification tool produces aligned combined WAVs from rendered audio stems + companion metadata, closing the compose-render-verify loop.

The mixdown tool does not depend on the Counterpoint SDK landing — it only needs companion files to exist (whether hand-authored in any DAW with the cross-reference markers added manually, or emitted by the future Counterpoint workbench). This decoupling lets Phase 5 ship the verification capability before the full Counterpoint integration is ready.

**Deliverables.**

*Independent of Counterpoint SDK (can ship any time after Phase 4):*

1. **Counterpoint cross-reference marker emission** in `ProjectBuilder`. Given a `CounterpointSetPlan` with companion names, designed offsets, and tier, emit the tick-0 marker in each companion's project per [§7.10.2](#7102-cross-reference-marker-format). Marker color fixed at `#5b7a5b`. Structured-grammar serialization tested against a round-trip parser.
2. **File-naming convention** — `{base-name}.{suffix-letter}.dawproject` output paths, with stable suffix assignment (piece A → `.a`, piece B → `.b`, etc.). `CounterpointSetPlan` carries the suffix assignment explicitly so round-trips preserve it.
3. **`CounterpointSetPlan`** intermediate type in `sdks/music-composer/Plan/` — wraps N `CompositionPlan` instances with per-piece suffix assignments, cross-set metadata (set identifier, designed offsets, tier), and an ordering invariant check (all pieces share tempo + meter).
4. **`DawProjectReader` extension** for parsing counterpoint cross-reference markers. Regex-based grammar parser with explicit `CounterpointMetadataParseError` on malformed input. Round-trip test: emit → read → structural equality.
5. **`CounterpointMixdownService`** in `plugins/lib-music/Services/`. Implements the algorithm from [§7.10.3](#7103-verification-via-mixdown). Components:
   - WAV parser for 16/24/32-bit PCM + 32-bit float (RIFF/WAVE spec — no external dependency needed)
   - Tempo → sample-count converter
   - Sample-accurate offset alignment
   - Linear per-stem-gain summing mixer
   - Peak normalizer to configurable dBFS ceiling
   - Optional per-stem stereo pan application
   - WAV writer (24-bit PCM default; configurable)
6. **Schema additions** to `schemas/music-api.yaml`: `POST /music/counterpoint/mixdown` endpoint per [§7.10.4](#7104-mixdown-endpoint), with `CounterpointMixdownRequest` and `CounterpointMixdownAssetResponse` models.
7. **Endpoint implementation** in `plugins/lib-music/MusicService.Counterpoint.cs` (new partial class file). Follows the standard T7/T8/T30 patterns. Binary response body for direct WAV return; JSON response body when `uploadToAsset=true`.
8. **CLI command** `bannou music-composer counterpoint mixdown`:
   - `--dawprojects DAWPROJECT[,DAWPROJECT...]` (required)
   - `--stems STEM[,STEM...]` (required; order doesn't have to match dawprojects)
   - `--pair SUFFIX=STEM[,SUFFIX=STEM...]` (optional; explicit stem-to-companion mapping if filenames don't match convention)
   - `--stem-gain VAL` / `--stem-gains SUFFIX=VAL,...`
   - `--pan SUFFIX=VAL,...`
   - `--normalize-peak-dbfs VAL`
   - `--output PATH` (required; writes the combined WAV)
   - `--upload-to-asset` (alternative to `--output`; returns an Asset ID)
9. **Unit tests** in `plugins/lib-music.tests/`:
   - Round-trip cross-reference marker emission + parsing
   - WAV parser fixtures covering 16/24/32-bit PCM and 32-bit float
   - Alignment correctness: given fixture offsets and fixture stems, produce sample-accurate placement (±1 sample tolerance at boundaries)
   - Mixing correctness: given known-amplitude stems, verify summed output matches expected values
   - Normalization correctness: peak reaches the requested ceiling within ±0.01 dB
   - Set-consistency validation: reject mismatched `set=` identifiers, mismatched `companions=` lists, tempo mismatches, sample-rate mismatches
10. **Integration test (user-executed)** — render a hand-authored counterpoint pair through Studio One, run mixdown, confirm audible counterpoint compatibility.

*Contingent on Counterpoint SDK moving from planning to implementation:*

11. **`CounterpointAdapter.ToCounterpointSetPlan(CounterpointAuthoringState, Style)`** — maps the Counterpoint workbench's authoring state (structural template + composed parts + counterpoint compatibility metadata) to a `CounterpointSetPlan`. Similar shape to `StorytellerAdapter.ToCompositionPlan`.
12. **Counterpoint SDK's "Export" operation** calls the MusicComposer pipeline via `CounterpointAdapter`, streams the N resulting `.dawproject` files back to the user (zip bundle or successive downloads per user preference).
13. **Adapter unit tests** verifying `CounterpointAdapter` produces a deterministic `CounterpointSetPlan` from a mock authoring state.

**Exit criteria.**
- Counterpoint cross-reference markers round-trip through ProjectBuilder → DawProjectReader cleanly, with `CounterpointMetadataParseError` surfaced on corrupt inputs
- Mixdown service produces sample-accurate alignment for fixture inputs; peak normalization hits target within tolerance
- Human verification: render a fixture counterpoint set in Studio One, run mixdown, audition combined WAV, confirm overlay is audibly coherent
- Counterpoint SDK (once implemented) can export via `CounterpointAdapter`

**Dependencies.** Deliverables 1–10 need Phase 1 (DOM) and the `music-api.yaml` surface from Phase 2 being present so the new endpoint can slot in. No dependency on Phase 3 (ProjectBuilder) for the mixdown tool itself — mixdown only reads existing `.dawproject` files and audio stems. Deliverables 11–13 need the Counterpoint SDK to exist.

**Risks.**
- **WAV parser edge cases.** Floating-point WAVs, RIFF extension chunks, multi-channel (>2) files, broadcast-wave (`bext`) metadata, malformed RIFF. Mitigation: scope to PCM 16/24/32-bit mono/stereo + 32-bit float mono/stereo at a single sample rate. Reject unsupported formats with a clear error pointing to the offending file. Advanced WAV coverage is not in Phase 5's scope; if a specific advanced case becomes necessary during implementation, it is its own sub-phase with its own exit criteria, not a "we'll add it later" item.
- **Tempo mismatch between authored `.dawproject` and rendered audio.** If the composer changes tempo in Studio One before rendering, the mixdown will misalign. Mitigation: document in [§7.10.5](#7105-limitations); a possible future `--tempo-hint` flag could override the companion's authored tempo, but is not planned for Phase 5.
- **Marker hand-edits corrupt the metadata grammar.** The composer might add notes to the counterpoint-meta marker thinking it's a regular marker. Mitigation: (a) distinctive color `#5b7a5b` signals "reserved marker — do not edit"; (b) document in §7.10.2; (c) future enhancement: store the metadata in both the marker and the Arrangement `Comment` field as belt-and-suspenders.
- **Counterpoint SDK implementation timing.** Deliverables 1–10 are ready whenever Phase 5 is scheduled; deliverables 11–13 wait for Counterpoint SDK. Mitigation: ship Phase 5 in two waves — the independent wave whenever convenient, the Counterpoint-contingent wave when the sibling SDK is ready. The planning doc ratification for the companion-files shape ([§11.2](#112-counterpoint-output-shape-same-file-or-companion-files)) is stable regardless of timing.

### 10.6 Phase 6: Round-Trip Validation + Integration Testing

**Goal.** Verify the SDK against a real Studio One install.

**Deliverables.**
1. Integration test fixture: a known CompositionResult (committed as a test asset) produces an expected Project DOM (committed as a golden fixture).
2. Round-trip test (user-executed, not CI):
   - Bannou writes `.dawproject`
   - User opens in Studio One Pro 7
   - User saves as `.dawproject` from Studio One
   - Bannou re-reads and compares semantically (same tracks, notes, automation points within floating-point tolerance)
3. Human evaluation test matrix:
   - Composition opens without error
   - Expected track structure appears in the mixer
   - Notes play back correctly when user presses Play
   - Automation curves visibly match the emotional-journey design
   - Plugin instances load with captured state (when templates are registered)
   - Master-bus chain is present and plays mastered output
4. Documentation of any DAW-specific behavior variances (if Studio One interprets something differently from Bitwig, note it).

**Exit criteria.**
- Golden-fixture DOM test passes
- Human-evaluation matrix passes on Studio One Pro 7 (sole target per [§11.4](#114-daw-target-priorities); other DAWs out of scope)
- Round-trip test produces semantically-equivalent output for Studio One (per §8.14 tolerance)

**Dependencies.** Phases 1-4; Phase 5 (Counterpoint) can be added to the human-evaluation matrix later.

**Risks.**
- Studio One-specific quirks discovered late. The per-note expression rendering, automation curve interpolation, or marker display may differ from what MusicComposer's XSD-compliant output assumes. Mitigation: maintain a "known Studio One quirks" section in the deep dive (written after phase 6), document workarounds on a case-by-case basis. Per §11.4, quirks documentation is Studio One-specific — we don't catalog equivalent behaviors in Bitwig or Cubase.

### 10.7 Phase Dependency Graph

| Phase | Depends On | Enables |
|---|---|---|
| 1: DOM C# port | — | Phases 3, 4, 5, 6 (everything downstream reads or writes `.dawproject`) |
| 2: Plugin Library endpoints + bulk import + family registry | Phase 1 is required for capture-from-dawproject (§6.7.2); the rest of Phase 2 is independent | Phase 3 (PluginResolver needs the endpoints); populated library is required for meaningful Phase 6 evaluation |
| 3: ProjectBuilder + Adapters | Phases 1 + 2 | Phase 4 |
| 4: Export endpoint | Phase 3 | Phase 6 |
| 5: Counterpoint companion emission + mixdown verification | Phases 1-2 for the mixdown tool; Phase 3 + Counterpoint SDK (when it exists) for the adapter wave | Counterpoint workflow end-to-end |
| 6: Round-trip validation | Phases 1-4 (Phase 5 optional for the initial pass) | Ships the SDK |

The graph is strictly forward — no phase depends on a later phase. Phase 5 is drawn with split dependencies because its mixdown tool is independently useful (any `.dawproject` with counterpoint markers can use it) while its adapter depends on the separate Counterpoint SDK existing.

**Phase 5's two-wave structure:** The **mixdown wave** (companion emission rules, cross-reference markers, mixdown service + endpoint + CLI) can be worked independently of the Counterpoint SDK — it operates on any `.dawproject` files carrying cross-reference markers, whether hand-authored or produced by a future Counterpoint workbench. The **adapter wave** (`CounterpointAdapter.ToCounterpointSetPlan`) requires the Counterpoint SDK to exist and expose its authoring state. The two waves can complete in either order.

**No phase is "optional" or "deferred."** Every phase is required for the SDK to fulfill its stated purpose. If a phase's scope turns out to be too large during implementation, the correct response is to decompose it into sub-phases with their own exit criteria — not to declare part of it optional.

### 10.8 Continuous Requirements Across Phases

| Requirement | How Each Phase Addresses It |
|---|---|
| T1 Schema-first | All schema changes lead code changes; generators run before manual edits |
| T4 Infrastructure libs | Phases 2, 4 use lib-state, lib-messaging, lib-asset via standard factories |
| T5 Typed events | Phase 2 emits Plugin Library lifecycle events per T5 dual-publish pattern |
| T6 Partial class decomposition | `MusicService.PluginLibrary.cs`, `MusicService.Export.cs` as new partial class files |
| T7 Error handling | Per-item isolation in batch resolve, ApiException handling in inter-service calls |
| T8 Return tuples | All new methods return `(StatusCodes, TResponse?)` |
| T11 Three-tier testing | Unit tests in each phase; integration tests in Phase 6; no HTTP integration tests required (user runs those) |
| T12 Test integrity | Capture pattern for state/event verification; no weakened assertions |
| T18 Licensing | MIT dependencies only; no GPL linking |
| T25 Type safety | All enums flow through EnumMapping boundary tests at SDK seams |
| T30 Telemetry spans | All async methods in new code get `StartActivity` spans |
| T31 Category B deprecation | Plugin Library templates follow the full Category B checklist |

---

## Part 11: Open Questions and Decision Points

Unresolved design choices that need an answer before or during implementation. Each has a recommended default based on the analysis in earlier sections; users of this document should confirm or revise.

### 11.1 Plugin Library Granularity (Individual Patches vs. Plugin + Patch Name)

**Question.** Should the PluginLibraryTemplate record a specific captured patch (one entry per captured `.vstpreset`), or should it record "this plugin is available, here are some named patches within it" (one entry per plugin, multiple patches per entry)?

**Options considered.**

- **11.1.A — Individual patches.** One PluginLibraryTemplate per (plugin + captured state). `strings.orchestral.violin.legato.warm`, `strings.orchestral.violin.spiccato.sharp`, `brass.orchestral.trumpet.marcato-bright` are all distinct templates. Composer resolves roles to specific patches via the reverse-DNS hierarchy.
- **11.1.B — Plugin + patch name references.** One PluginLibraryTemplate per plugin instance. Patch selection is a named reference within the template's metadata.
- **11.1.C — Both layered.** Plugin-level entries AND patch-level entries under them.

**Resolution — 2026-04-19: Option 11.1.A adopted, with the following design refinements.**

Individual-patch granularity is adopted because it is the only option that preserves the "DAWproject opens with the exact patch loaded, zero clicks for the composer" property that is the thesis of the SDK ([§3.1](#31-thesis--do-studio-ones-job-for-it-up-front)). Option 11.1.B breaks that property — VST3 preset-by-name loading is plugin-specific and unreliable, Studio One has no cross-plugin convention for "load preset by name" that works consistently, and the composer would still have to audition/select manually. Option 11.1.C doubles the schema complexity without proportionate value — Option 11.1.A's hierarchical fallback (see §6.8) already provides the "any Kontakt patch" semantics C was reaching for, more cleanly.

The three refinements added with this resolution:

1. **Role slugs use reverse-DNS hierarchical notation.** `strings.orchestral.violin.legato.warm` instead of flat `strings-legato-warm`. Encodes family → subfamily → voice → articulation → modifier from left to right. See [§6.2 Role Taxonomy](#62-role-taxonomy) for the full convention.
2. **Hierarchical fallback replaces Levenshtein-fuzzy scoring.** The resolver walks up the hierarchy one segment at a time until it finds a match. See [§6.8 Resolution Semantics](#68-resolution-semantics) for the algorithm. This means a library that covers `strings.orchestral.violin.legato` still serves requests for `strings.orchestral.violin.legato.warm` — the registration burden of A is mitigated because depth is optional.
3. **Bulk import is the primary capture path.** Users organize preset files into a directory tree mirroring the role taxonomy; one command (`bannou music-library bulk-import --dir ~/MusicLibrary --game-service-id {game}`) registers the entire library in one transaction. See [§6.7 Capture Workflow](#67-capture-workflow) for the full specification. This reduces A's registration burden from "weekend of work" to "an afternoon plus some tag tweaking" for a typical mature library.

Additionally accepted as part of this resolution: the **Plugin Family Registry** ([§6.3](#63-plugin-family-registry)) handles Tier 1 fallback when the hierarchy exhausts, the **capture-from-dawproject** ingest mode ([§6.7.2](#672-secondary-path-capture-from-existing-dawproject)) jump-starts libraries from existing Studio One mix templates, and the **sidecar `.yaml` overrides** ([§6.7.1](#671-primary-path-bulk-import-from-directory-tree)) handle edge cases where inferred slugs and tags need correction.

**Status.** Ratified.

### 11.2 Counterpoint Output Shape (Same File or Companion Files)

**Question.** When a composer uses the Counterpoint workbench to author two counterpoint-compatible pieces, should MusicComposer emit them in a single `.dawproject` (pieces as sibling tracks with sync markers) or as companion files (each piece as its own `.dawproject`, linked via cross-reference markers)?

**Options considered.**

- **11.2.A — Same file as parallel tracks.** Both pieces share tracks in one DAWproject. Designed-offset markers delineate sync points. Advantage: composer can audition the overlay directly from the single session via mute/unmute. Disadvantages: (1) doesn't match the COUNTERPOINT-COMPOSER-SDK framing that each piece is independently standalone, (2) scales poorly past two pieces (3+ counterpoint sets produce crowded sessions).
- **11.2.B — Separate files, metadata-linked.** Each piece gets its own `.dawproject`, cross-referenced via Marker names containing the sibling piece's identifier. Advantages: matches the "each piece is a full standalone work that happens to combine" framing; scales to 3+ pieces; each file is a complete, auditonable, shippable artifact in its own right. Disadvantage: composer can't hear the overlay by opening a single file.
- **11.2.C — Both at once (zip bundle).** Every counterpoint export produces a zip containing the single-file AND the companion files. Rejected: doubles the storage/naming complexity for uncertain benefit.
- **11.2.D — Context-sensitive.** Single-file for pairs, companion for 3+. Rejected: adds a branching rule to the emission path; treats pairs as a special case for an ergonomic reason that the mixdown tool eliminates.

**Resolution — 2026-04-19: Option 11.2.B adopted (companion files always, no single-file mode), with a verification-via-mixdown refinement.**

Companion files were chosen because:

1. **Standalone framing is preserved.** Each piece is a full standalone work — matches the philosophical framing from COUNTERPOINT-COMPOSER-SDK ([§2.2 The Core Concept](COUNTERPOINT-COMPOSER-SDK.md)) and makes each file independently shippable if the composer decides to use the pieces separately.
2. **Scales naturally to 3+ pieces.** A three-way or four-way interlock produces 3-4 manageable files rather than one overcrowded session. The mix-bus, track structure, and mixing workflow for each piece remains standard.
3. **Single-file's auditioning advantage is recovered by the mixdown tool.** The single-file case's main advantage was "press Play in Studio One and hear both pieces overlay." The mixdown verification tool ([§7.10](#710-counterpoint-companion-emission-and-verification)) provides this same auditioning capability — but on the **rendered audio**, after the composer has taken each piece through their VST palette. This is strictly better than single-file for auditioning because the composer hears the actual production sound of both pieces combined, not a MIDI preview through default instruments.
4. **Composer's auditioning workflow becomes:** export companion files → render each in Studio One → run `bannou music-composer counterpoint mixdown` → audition the combined WAV → iterate in Studio One if adjustments are needed. The overall loop is no longer than single-file's "open session → mute/unmute → audition," and the mixdown produces a shippable artifact (the duet performance as a stereo track) as a side benefit.

The `dawprojectOptions.counterpointShape` option is removed from the API surface — counterpoint output is companion-files-only, no toggle.

**Status.** Ratified. Companion-file emission + cross-reference marker format + mixdown verification specified in [§7.10](#710-counterpoint-companion-emission-and-verification). Phase 5 implementation splits into an independent-wave (mixdown tool, works for any hand-authored counterpoint) and a Counterpoint-contingent wave (`CounterpointAdapter` bridging the workbench's authoring state to MusicComposer's emission pipeline) — see [§10.5](#105-phase-5-counterpoint-companion-emission--mixdown-verification--1-week-plus-counterpoint-sdk-coordination).

### 11.3 Workbench SDK (Scope Boundary)

**Question.** Should MusicComposer include an interactive authoring workbench (the sprite-composer / scene-composer equivalent — a UI scaffold the consumer's editor hooks into), or is it strictly a file-format SDK?

**Options considered.**

- **11.3.A — File-format SDK only.** MusicComposer is DOM + ProjectBuilder + DawProjectWriter + Plugin Library + mixdown verification. No UI scaffold, no command stack, no undo/redo, no real-time preview.
- **11.3.B — Include a partial workbench.** Command-stack/undo-redo layer for editing CompositionPlans, no audio preview. Preview routes through `/music/export/dawproject` and opens Studio One.
- **11.3.C — Include a full workbench.** Partial workbench plus real-time audio preview via embedded VST hosting.

**Resolution — 2026-04-19: Option 11.3.A adopted (file-format SDK only).**

The key insight driving this choice is that any workbench worth building requires audio preview to be a real authoring environment — and audio preview requires VST hosting, which is Shape 2 ([§2.5](#25-rejected-headless-vst-hosting-via-dawdreamer-or-juce-shape-2)), which we explicitly rejected for licensing, scope, and "not-the-Composer-layer" reasons. Option 11.3.C would implicitly revive those rejected decisions. Option 11.3.B avoids the VST-hosting problem but produces a half-workbench — the composer edits abstract compositional structure without real-time audio feedback, which is an awkward half-product.

The interactive authoring role belongs to the Counterpoint workbench ([COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md)) and to Studio One / the composer's DAW, each at its own layer of the pipeline:

- **Counterpoint workbench** = structural template authoring + compatibility validation + offset analysis (command-stack editing happens here, over abstract compositional structure)
- **Studio One / DAW** = sonic authoring, mix, and final production (real-time audio preview happens here, after MusicComposer has emitted the session)
- **MusicComposer** = the export substrate between them

This three-way division mirrors how Scene works: `scene-composer` is the file-format + editing-core SDK, engine bridges translate scene documents into engine-specific render data, the game engine is the final renderer. Scene doesn't try to *be* the engine. MusicComposer shouldn't try to be the DAW.

**Future-flexibility note.** If a future non-Counterpoint authoring tool wants to emit DAWproject (an L5 Arcadia extension theme-authoring UI, for example), it uses MusicComposer as its export substrate exactly the way the Counterpoint workbench does — by building a `CompositionPlan` (or `CounterpointSetPlan`) and calling `ProjectBuilder.Build`. The calling layer provides whatever UI affordances, command stacks, and previews it wants. MusicComposer doesn't care who builds its input; it only cares about emitting valid `.dawproject` bytes.

**Status.** Ratified. The §4.3 project structure ([§4.3](#43-project-structure)) already reflects this — no `Commands/`, `Preview/`, or `Events/` directories exist in the MusicComposer SDK layout, consistent with file-format-only scope.

### 11.4 DAW Target Priorities

**Question.** MusicComposer produces valid DAWproject for all six supporting DAWs. Should the implementation prioritize any specific DAW for round-trip testing (§10.6) and quirks documentation (§8), or should it treat all DAWs as equally important?

**Options considered.**

- **11.4.A — Studio One first, others best-effort.** Primary validation target is PreSonus Studio One Pro 7; others assumed to work via XSD compliance.
- **11.4.B — Bitwig first (reference implementation).** Validate against the DAWproject reference DAW; Studio One treated as best-effort.
- **11.4.C — All equally (strict spec-compliance only).** Validate against the XSD and Bitwig's reference behavior; no DAW gets preferential quirks documentation.

**Resolution — 2026-04-19: Option 11.4.A adopted, strengthened: Studio One is the *sole* validation target, not just the primary one.**

The primary consumer (and the only identified consumer for the foreseeable future) uses Studio One and has explicitly declined to use any other DAW. There is no plan to expand to other DAWs. Therefore:

1. **Studio One Pro 7 is the only DAW in the Phase 6 validation matrix.** Round-trip tests run against Studio One. Human-evaluation matrix (composition opens, plays, automation curves visible, plugins load with captured state, master-bus chain audible) evaluates Studio One behavior only.

2. **Quirks documentation in [§8](#part-8-gaps-and-workarounds) is Studio One-specific.** Any behavioral difference between Bannou's emitted output and Studio One's rendering is documented; behavioral differences in other DAWs are out of scope.

3. **DAW-specific features we intentionally target are Studio One-specific.** The workaround suggestions in [§8.4](#84-no-studio-one-proprietary-feature-round-trip) (Arranger Track auto-populate, Chord Track "Detect from Notes", Sound Variations routing) are for Studio One. Equivalent features in other DAWs are not cataloged.

4. **Default output tuning assumes Studio One rendering.** Marker colors, preferred instrument placements on the mixer, submix group conventions — all tuned to what displays well in Studio One's UI. If other DAWs render these differently, that's not a Bannou concern.

5. **Other DAWs' behavior is "format-compliant, untested."** DAWproject files MusicComposer emits pass XSD validation, so any DAWproject-compliant DAW (Bitwig, Cubase, Cubasis, VST Live, n-Track) should in principle open them. Bannou does not test this, does not fix bugs specific to those DAWs, and makes no commitment about their fidelity. If a third-party user happens to use one of them and reports an issue, the response is "file a bug with that DAW vendor, not with Bannou" unless the issue is also reproducible in Studio One.

6. **Cross-DAW expansion is explicitly not on the roadmap.** [§13.2](#132-daw-targets-beyond-studio-one) remains in the doc as speculative future-extensions content only; any actual work in that direction would require re-opening this decision.

**Status.** Ratified. Studio One Pro 7 is the sole DAW target. Other DAWs are out of scope for validation, support, and roadmap.

### 11.5 PluginLibraryProfileId Semantics

**Question.** The `pluginLibraryProfileId` parameter lets a composition request filter to a subset of the library. How should the filtering work?

**Options considered.**

- **11.5.A — Soft preference.** The profile contributes a scoring boost ([§6.8](#68-resolution-semantics)) but doesn't exclude templates. When the profile has no matching template for a role, the resolver falls back to any template of that role.
- **11.5.B — Hard filter.** The profile strictly limits resolution to its templates; no fallback. Compositions requesting an unmatched role fail resolution (fall back to Tier 1 reference-only).
- **11.5.C — Per-role override.** A profile is itself a role → template mapping, not a filter. Applying the profile forces specific templates for specific roles.

**Resolution — 2026-04-19: Option 11.5.A adopted (soft preference).**

Soft preference is the only option that composes cleanly with the §6.8 hierarchical fallback. The resolver's scoring algorithm already gives +10 for profile-match ([§6.8.1 Algorithm](#681-algorithm)) alongside its level-based base score, stylistic-tag bonus, and recency tiebreaker — the profile becomes one more contributor to the scoring rather than a filter that fights the hierarchy.

This matters because **profiles are expected to be sparse.** A typical user might have a `cinematic` profile that covers 15-20 role-slugs relevant to cinematic scoring (strings, brass, woodwind, pad, cinematic percussion) but not every role their compositions might request (their `drum.electronic.*` templates, their `synth.fm.*` templates, their `lead.dark-revelry.*` templates). Hard filtering (Option B) on a sparse profile would force most roles to Tier 1 fallback — defeating the point of having a registered library. Soft preference lets the cinematic-profile templates win their 15-20 roles while the rest of the composition still benefits from the broader library.

Option 11.5.C (per-role override) is useful and remains on the table — but as a **separate feature**, not as the meaning of `pluginLibraryProfileId`. A future `pluginLibraryOverrides` field on the export request can carry explicit role → template-ID mappings for the user who wants surgical precision on a specific composition (e.g., "for this villain theme, use template X for the lead role specifically, regardless of any other scoring"). That addition wouldn't conflict with profile soft-preference semantics.

**Status.** Ratified. Profile soft-preference semantics implemented per [§6.8.1 Algorithm](#681-algorithm) (the `+10 if candidate.profileId == profile` scoring rule) and [§6.8.5 Profile Semantics](#685-profile-semantics-soft-preference). Future `pluginLibraryOverrides` feature is acknowledged as a compatible extension point but not in scope for Phase 2.

### 11.6 Deferred Features

Explicit list of features considered and intentionally deferred:

- **Audio stem generation** (Shape 2 from the earlier discussion). See [§13.1](#131-audio-stem-generation-shape-2-revived).
- **Scene / clip launcher support.** DAWproject supports it; Bannou's music system doesn't need it yet.
- **Video track support.** DAWproject supports it (`Video` element); out of scope for a music SDK.
- **Fallback-plugin metadata.** Future enhancement ([§8.6](#86-missing-plugins-on-the-consumers-machine)).
- **Preset versioning.** Future enhancement ([§6.12.5](#612-honest-limitations)).
- **Non-single-user deployment sharing model.** Multi-organization Plugin Library sharing would require a cross-deployment distribution story ([§9.2](#92-captured-plugin-presets--users-own-files)).

---

## Part 12: Success Criteria

What does "this SDK works" look like? Three levels, from mechanical to narrative.

### 12.1 Mechanical Criteria (Phase Exit Gates)

Each phase has its own exit criteria in [§10](#part-10-implementation-phases). Aggregated:

- [ ] DAWproject DOM round-trips faithfully (Phase 1)
- [ ] Plugin Library endpoints pass structural + unit tests (Phase 2)
- [ ] ProjectBuilder produces deterministic output (Phase 3)
- [ ] `/music/export/dawproject` endpoint produces byte-identical output for identical inputs (Phase 4)
- [ ] Human can open a generated file in Studio One Pro 7 without error (Phase 6)
- [ ] Round-trip test shows semantic equivalence (Phase 6)

### 12.2 User-Workflow Success

The composer's workflow should shift from:

> Open Storyteller, review MIDI-JSON output, open Studio One, manually rebuild track structure, place notes by ear or transcription, assign instruments, mix, render.

To:

> Call `POST /music/export/dawproject` with the composition parameters. Download the `.dawproject` file. Open it in Studio One. Press Play — hear a mastered, instrumentally-appropriate realization of the composition. Tweak what needs tweaking. Render to WAV.

The categorical difference is that the pre-Play decisions in Studio One shift from *authoring decisions* (what tracks exist, what plugins sit on them, what notes they play, how the mix bus is wired, what automation shapes the arrangement) to *refinement decisions* (are the chosen patches the right ones, does the balance feel right, what personal signature touches fit). The composer self-reports whether this shift has happened after the SDK is complete; no pre-measurement of decision counts is useful.

### 12.3 Narrative Success: Authoring Time Reduction

The ultimate success criterion is a reduction in the time and effort a composer spends on a given piece compared to their pre-SDK workflow. This criterion cannot be unit-tested and no specific time reduction is predicted here — the number of hours a composer takes on a villain theme today and the number of hours they take after the SDK exists are both measurable by the actual user, not guessable in advance.

Evaluation method — the primary consumer self-measures their workflow:

1. Before SDK availability, or for compositions where the SDK is deliberately unused, the composer records how long a typical hand-authored track takes from blank session to finished WAV — the baseline.
2. After the SDK is complete and their Plugin Library is populated, the composer authors a comparable track using the full pipeline (Storyteller → `.dawproject` → Studio One → rendered WAV) and records how long that takes.
3. Compare. Success is any meaningful reduction where the proportion of time spent on tweaking-versus-authoring has shifted toward tweaking, and the composer reports higher satisfaction with the finished result.

Concrete test case: "Please author a two-minute villain theme for [specific game's specific villain] starting from a Bannou-generated DAWproject, using your existing Plugin Library. How long did it take, how happy are you with the result, what proportion of your time was tweaking versus authoring-from-scratch, and how does that compare to comparable work you've done without the SDK?"

No productivity ratio is predicted. If the SDK reduces workflow time at all, the value is delivered; if it doesn't, something is wrong with the design or the Plugin Library hasn't been populated enough to matter yet, and we investigate from there.

### 12.4 Non-Criteria (Things We Don't Need)

Explicit non-goals:

- **Perfect output.** MusicComposer produces a starting point, not a final mix. A generated DAWproject is a first draft, not a master.
- **Every possible DAW feature.** If Studio One adds a feature that DAWproject doesn't round-trip, MusicComposer doesn't emit it. The user enables it manually.
- **Replacement for a human composer.** MusicComposer makes the mechanical work of session setup go away. The taste, the finishing, the artistic judgment remain the human's job.
- **Audio output.** MusicComposer produces DAWproject files. Audio is the user's DAW's job.
- **Standalone authoring UI.** MusicComposer is an SDK, not an application.

---

## Part 13: Future Extensions

Features considered but deferred beyond the initial SDK. Documented here so future contributors don't have to re-derive them.

### 13.1 Audio Stem Generation (Shape 2 Revived)

**What.** A companion rendering pipeline — call it `lib-music-renderer` — modeled after `lib-procedural`. A pool of headless VST hosts (DawDreamer-based or JUCE-based) managed by the Orchestrator, consuming Bannou MIDI + PluginLibraryTemplates and producing WAV stems.

**When it makes sense to build.** When Bannou's music system needs real-time adaptive music rendering (not DAW-authored-once-then-played, but continuously generated during gameplay) at production-audio-quality levels. That's a separate product capability from what MusicComposer delivers.

**How it would compose with MusicComposer.** Shape 2 and MusicComposer share the same CompositionPlan intermediate. A future `lib-music-renderer` would accept a CompositionPlan, resolve its PluginRoleRequirements against the Plugin Library, host the VSTs, render to WAV. No interaction with DAWproject — it's a different output path for the same input.

**Constraints that still apply.** PreSonus-native instruments are still not reachable (can't be hosted outside Studio One). Commercial VST EULAs still restrict server deployment. Shape 2 is viable only for compositions whose plugin library is exclusively third-party server-licensable VSTs.

**Deferral reason.** MusicComposer's thesis is "do Studio One's job for it, up front." Shape 2 is "replace the user's Studio One with our render pool." These are different products with different constraints and different user experiences. They can coexist, but they shouldn't be conflated.

### 13.2 DAW Targets Beyond Studio One

**Not on the roadmap.** Per [§11.4](#114-daw-target-priorities), Studio One is MusicComposer's sole DAW target. Other DAWs are out of scope for validation, support, and quirks documentation. This subsection exists only to name what would have to happen if that decision ever reversed (e.g., a second consumer adopts Bannou with a different DAW preference) — it is not a list of planned work.

What would be required to legitimately add another DAW as a validation target:

- Re-open [§11.4](#114-daw-target-priorities). Replace "sole target" with "primary + {new DAW}". This is a design-level decision, not a scope-creep accretion.
- Add the new DAW to the Phase 6 round-trip matrix. The human-evaluation pass has to happen on an actual installation of that DAW, operated by someone familiar with its UI.
- Catalog DAW-specific quirks in [§8](#part-8-gaps-and-workarounds). Does the new DAW handle per-note expression the same way Studio One does? Marker colors? Automation curve interpolation? Each deviation is a new row with a workaround.
- Potentially retune default output (mixer submix layout, marker color palette, master-bus defaults) to render well in the new DAW's UI — or accept that it renders differently.

Reference directions if that work ever happens:

- **Reaper.** The [git-moss/ProjectConverter](https://github.com/git-moss/ProjectConverter) tool converts `.dawproject` ↔ `.rpp` (Reaper's format). Bannou could wrap it in a CLI. The Reaper community has robust ReaScript automation, so Reaper support would likely be the easiest addition technically.
- **Bitwig.** Co-authored DAWproject and ships the reference library; any DAWproject Bannou emits should already open cleanly. Mostly a validation effort rather than a format-compatibility effort.
- **Cubase / Cubasis / VST Live.** All three have native DAWproject support via Steinberg's implementation. Cubasis is iPad-only, which would complicate the round-trip workflow (rendering to audio requires transferring the file to an iPad).
- **Logic Pro.** No DAWproject support as of 2026-04. If Apple adds it, no Bannou changes are needed — existing output would just open. No conversion work available today.
- **Ableton Live.** No native DAWproject support. ProjectConverter has some experimental Ableton support but it's incomplete. Full Ableton compatibility would require additional conversion effort.

None of this is scheduled. The subsection exists so that if the situation changes, future contributors know the starting point without having to re-derive it.

### 13.3 Chord Track / Scale Track DAWproject Extensions

DAWproject v1.0 doesn't include a first-class Chord Track or Scale Track element. If v1.1 or v2.0 adds one, MusicComposer should emit into it rather than using Markers ([§8.1](#81-no-first-class-chord-symbol-type), [§8.3](#83-no-first-class-key-signature-element)). Tracking this is a matter of subscribing to the `bitwig/dawproject` repo for schema updates.

### 13.4 Interactive Preview via Client-Side Render

A future companion SDK (or a lib-music addition) could render a generated DAWproject's MIDI through a web-audio or .NET-embedded synth for quick preview — a "what does this composition sound like before I open my DAW?" affordance. Quality wouldn't match a full DAW render (no access to the user's plugins), but would let the composer quickly triage whether a generation run is worth opening.

This is deliberately NOT part of MusicComposer's core — it's a UX convenience layer built on top.

### 13.5 Plugin Library Cross-Reference With Arcadia World

For Arcadia specifically: the game world has conceptual vocabulary (regional styles, divine themes, character archetypes). A rich authoring experience would let the composer tag PluginLibraryTemplates with Arcadia concepts ("fits the Kingdom of Orhen", "suits Light-aspect deities") and let MusicComposer resolve composition requests by Arcadia concept rather than raw role slug. This crosses into Arcadia-specific extension territory — belongs in an L5 Arcadia extension plugin, not in core MusicComposer.

### 13.6 DAW-Format Interop With Notion

PreSonus Notion is a notation-focused application that can import/export MusicXML and MIDI. A pipeline MusicComposer → MusicXML → Notion (for composers who want to work in a notation view) is technically feasible but orthogonal to the DAWproject workflow. Deferred indefinitely unless a specific user need arises.

### 13.7 SpriteBatcher-Equivalent for Music

The Sprite domain has a planned `SpriteBatcher` — automation-driven sprite-sheet generation with no editor. The music-domain equivalent would be a batch-generation CLI: "for each entry in this YAML file, call `/music/export/dawproject` with these parameters and save the result." Useful for bulk content generation (e.g., ambient regional themes for 50 regions at once). Trivial wrapper once the export endpoint exists.

---

## References

### Bannou Internal Documents

- [docs/reference/TENETS.md](../reference/TENETS.md) — the full tenet index
- [docs/reference/tenets/FOUNDATION.md](../reference/tenets/FOUNDATION.md) — T4, T5, T6, T18, T27, T28, T29, T32
- [docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md](../reference/tenets/IMPLEMENTATION-BEHAVIOR.md) — T3, T7, T8, T9, T17, T30, T31
- [docs/reference/tenets/IMPLEMENTATION-DATA.md](../reference/tenets/IMPLEMENTATION-DATA.md) — T14, T20, T21, T25, T26
- [docs/reference/tenets/QUALITY.md](../reference/tenets/QUALITY.md) — T10, T11, T12, T16, T19, T22
- [docs/reference/tenets/TESTING-PATTERNS.md](../reference/tenets/TESTING-PATTERNS.md) — three-tier testing and capture pattern
- [docs/reference/HELPERS-AND-COMMON-PATTERNS.md](../reference/HELPERS-AND-COMMON-PATTERNS.md) — shared helpers catalog including Category B deprecation template
- [docs/reference/SCHEMA-RULES.md](../reference/SCHEMA-RULES.md) — schema authoring rules
- [docs/guides/SDK-OVERVIEW.md](../guides/SDK-OVERVIEW.md) — three-layer SDK pattern
- [docs/guides/MUSIC-SYSTEM.md](../guides/MUSIC-SYSTEM.md) — existing music system guide
- [docs/plugins/MUSIC.md](../plugins/MUSIC.md) — lib-music plugin deep dive
- [docs/maps/MUSIC.md](../maps/MUSIC.md) — lib-music implementation map
- [docs/plugins/PROCEDURAL.md](../plugins/PROCEDURAL.md) — the Houdini integration this SDK's architecture contrasts with
- [docs/planning/COUNTERPOINT-COMPOSER-SDK.md](COUNTERPOINT-COMPOSER-SDK.md) — peer planning document (structural template workbench)
- [docs/planning/DARK-REVELRY-STYLE-OBJECTIVE.md](DARK-REVELRY-STYLE-OBJECTIVE.md) — style definition reference and composition target
- [docs/planning/SPRITE-COMPOSER-SDK.md](SPRITE-COMPOSER-SDK.md) — reference for planning document format

### External Specifications and Source Repositories

- [bitwig/dawproject](https://github.com/bitwig/dawproject) — DAWproject format specification, XSD schema, Java reference implementation (MIT license)
- [bitwig/dawproject Reference.html](https://htmlpreview.github.io/?https://github.com/bitwig/dawproject/blob/main/Reference.html) — generated reference documentation
- [bitwig/dawproject Project.xsd](https://github.com/bitwig/dawproject/blob/main/Project.xsd) — XML schema for project files
- [bitwig/dawproject MetaData.xsd](https://github.com/bitwig/dawproject/blob/main/MetaData.xsd) — XML schema for metadata files
- [Bitwig and PreSonus DAWproject announcement (Synthtopia, Sept 2023)](https://www.synthtopia.com/content/2023/09/26/bitwig-and-presonus-introduce-open-dawproject-format-for-sharing-audio-projects-between-daws/)
- [PreSonus: Introducing DAW Project (Knowledge Base)](https://support.presonus.com/hc/en-us/articles/19743606863629-Introducing-DAW-Project)
- [DAWproject File Format FAQs (Bitwig)](https://www.bitwig.com/support/technical_support/dawproject-file-format-faqs-62/)

### DAW-Level Compatibility References

- [PreSonus Studio One Pro 7 Reference Manual](https://www.scribd.com/document/810207145/Studio-One-Pro-7-Reference-Manual-EN2) — the primary target DAW
- [Studio One Pro+: Scripting API feedback ticket](https://feedback-software.presonus.com/feedback/233875) — evidence that public scripting remains unshipped
- [PreSonus Sphere subscription overview (Sweetwater)](https://www.sweetwater.com/store/detail/PSSphereAnn--presonus-subscription) — subscription contents
- [PreSonus Software Developer page](https://www.presonussoftware.com/en_US/developer) — official developer resources

### Source Repositories Cited in Rejected Paths

- [fenderdigital/presonus-plugin-extensions](https://github.com/fenderdigital/presonus-plugin-extensions) — plugin-side SDK, not host-side
- [featherbear/presonus-studiolive-api](https://github.com/featherbear/presonus-studiolive-api) — hardware mixer, different product line
- [DBraun/DawDreamer](https://github.com/DBraun/DawDreamer) — headless Python/JUCE VST host (Shape 2 reference)
- [fedden/RenderMan](https://github.com/fedden/RenderMan) — older Python VST host
- [juce-framework/JUCE](https://github.com/juce-framework/JUCE) — C++ audio framework, includes `AudioPluginHost` example
- [obiwanjacobi/vst.net](https://github.com/obiwanjacobi/vst.net) — C# VST2 library (deprecated format)
- [mikeoliphant/AudioPlugSharp](https://github.com/mikeoliphant/AudioPlugSharp) — C# VST3 plugin development
- [xoofx/NPlug](https://github.com/xoofx/NPlug) — C# VST3 plugin development
- [REAPER ReaScript API documentation](https://www.reaper.fm/sdk/reascript/reascript.php) — alternative DAW pivot that was rejected
- [YatingMusic/ReaRender](https://github.com/YatingMusic/ReaRender) — Reaper batch-render toolkit (reference for future Shape 2)
- [git-moss/ProjectConverter](https://github.com/git-moss/ProjectConverter) — DAWproject ↔ Reaper converter (future DAW target)

### Community Resources

- [Studio One Toolbox (Lukas Ruschitzka)](https://studioonetoolbox.com/) — community scripting add-ons built on Studio One's internal JS engine
- [VI-Control: Why does PreSonus keep Studio One's scripting under lock and key?](https://vi-control.net/community/threads/why-does-presonus-keep-studio-ones-scripting-capabilities-under-lock-and-key.122050/)
- [KVR Audio: Scripting in Studio One](https://www.kvraudio.com/forum/viewtopic.php?t=506195)
- [KVR Audio: Mai Tai / Presence XT availability](https://www.kvraudio.com/forum/viewtopic.php?t=439186)

### Academic Foundations (Referenced by MusicStoryteller/Theory)

- Lerdahl, F. (2001). *Tonal Pitch Space*. Oxford University Press.
- Huron, D. (2006). *Sweet Anticipation: Music and the Psychology of Expectation*. MIT Press.
- Juslin, P. N. (2019). *Musical Emotions Explained*. Oxford University Press.
- Meyer, L. B. (1956). *Emotion and Meaning in Music*. University of Chicago Press.

---

*This document captures the architectural design for the MusicComposer SDK. Implementation maps (`docs/sdks/maps/MUSIC-COMPOSER.md`) and deep-dive documentation (`docs/sdks/MUSIC-COMPOSER.md`) will be authored during and after implementation of each phase.*










