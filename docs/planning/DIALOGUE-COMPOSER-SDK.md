# Dialogue Composer SDK: Design

> **Type**: Design
> **Status**: Design Complete — Bannou-side architectural authority decisions ratified 2026-04-26. Ready for Phase 1 implementation.
> **Created**: 2026-04-25 (problem statement)
> **Decisions Ratified**: 2026-04-26 (Bannou-side architecture review)
> **Last Updated**: 2026-04-26
> **Trigger Session**: Defenders-side Godot project structure planning (defenders-kb, 2026-04-25). First-consumer surfaced the question.
> **Decision Session**: Bannou-side architecture review (2026-04-26) — owner: Lysander; analysis: Claude.
> **Decision Authority**: Bannou-side architectural authority (this document is the output of the Bannou-led planning session referenced throughout the original problem statement).
> **First Consumer**: Defenders of Ba'hara — Path B exclusive (no lib-lexicon / lib-collection / lib-hearsay / lib-disposition).
> **Related SDKs**: cinematic-composer (gains `DialogueLineRef` form on `SpeakAction.line`), storyline-composer (no overlap), behavior-compiler (compiles dialogue trees today; target of issue #689), scene-loader-sdk (precedent for problem-statement → design pattern), sprite-composer (precedent for first-consumer-driven SDK), music-composer (precedent for file-format-only SDK with no plugin).
> **Related Plugins**: lib-localization (gains optional `audioRef` field + reserved `dialogue` category), lib-chat (transport-only, unchanged), lib-storyline (orchestrator above both paths, unchanged), lib-quest (referenced but unchanged), lib-voice (orthogonal — not relevant).
> **Required Prerequisite**: Issue [#689](https://github.com/beyond-immersion/bannou-service/issues/689) — lib-localization implements `ILocalizationSource` (in `bannou-service/Behavior/`) registered via DI; the existing `FileLocalizationProvider : IAggregateLocalizationProvider` aggregates sources by priority. See § Required Adjacent Changes.
> **Phase 1 Deliverables**: `sdks/dialogue-composer/` + lib-localization `audioRef` extension + `dialogue` category in `schemas/localization-categories.yaml` + cinematic-composer `SpeakAction.line` widening + #689 lands as prerequisite.
> **Future Phases (deferred)**: `defenders-dialogue-composer-godot` UI (Defenders-side), `lib-voice-pack` or equivalent (Arcadia-shaped, far future), `dialogue-storyteller` (far future), `dialogue-theory` (far future), `lib-dialogue` plugin (only if cross-surface line-registry storage proves necessary beyond what lib-localization provides).

---

## Summary

This document presents the Bannou-side architectural design for a dialogue-composer SDK. It supersedes the original 2026-04-25 problem statement (preserved as Part 1 — Historical Context), reflects the architectural decisions ratified during the 2026-04-26 Bannou-side decision session, and serves as the input plan for Phase 1 implementation.

**The recommendation, in one paragraph**: A focused `dialogue-composer` SDK following the canonical Bannou three-layer composer pattern, sized like `storyline-composer`, integrated like `cinematic-composer`, with no plugin and no runtime SDK. The SDK owns typed `DialogueLine` and `DialogueTree` records, the `dialogue` localization-key namespace, and an `AbmlDialogueExporter` that compiles trees to portable ABML `type: dialogue` documents executable by the existing behavior runtime. Voice-line audio is solved by a small `lib-localization` extension (`audioRef` field on entries) plus a documented two-tier resolution model that preserves long-term flexibility for Arcadia's Omega voice-collection-pack metagame without committing to its design today.

**Why this shape**: Dialogue is structurally closer to **music-composer** (file-format SDK, no plugin, runtime is somebody else's stack — namely ABML's) than to **cinematic-composer** (plugin with caching, agency-gated QTE density, IBehaviorDocumentProvider). The runtime story for dialogue is fully pre-paved: branching trees → ABML → behavior runtime; cutscene-bound lines → cinematic dispatch via `IClientCutsceneHandler`; NPC chat → lib-chat transport; quest log lines → lib-localization key resolution. There is nothing for a `lib-dialogue` plugin to do that one of those four paths isn't already doing better.

---

## Bottom-Line Decisions

| Question | Resolution |
|---|---|
| Build now or defer? | **Build now** as Phase 1 (this document's scope) |
| Voice-line audio location | **lib-localization `audioRef` (canonical layer)**; future voice-pack routing layer is a separate concern with the door explicitly held open via documented 2-tier model |
| Authoring tool location | **Bannou-side SDK + Defenders-side `defenders-dialogue-composer-godot` UI** (matches cinematic-composer / sprite-composer pattern) |
| Line ID space | **lib-localization key IS the dialogue-line ID** (reserved `dialogue` category in `localization-categories.yaml`) |
| ABML wrap-or-replace | **Wrap, never replace** — SDK exports to ABML; hand-authored ABML dialogue documents continue to work unchanged |
| Runtime SDK | **None** — existing ABML / cinematic / lib-chat / lib-localization runtimes cover every consumer surface |
| `lib-dialogue` plugin | **None** — lines live in lib-localization, trees live in ABML files, no centralized state requiring distributed safety |
| Speaker model | `DialogueSpeaker` discriminated union: `Narrator` / `ParticipantSlot` (cinematic interop) / `NamedCharacter` / `Expression` |
| Variants | Phase 1: line-level conditional variants on `DialogueLine`; structural tree-level variants stay runtime-conditional via the consuming surface's branching |
| Path A / Path B reconciliation | **lib-localization is the shared rendering substrate; no SDK-level bridge.** Both paths produce localization-key references that lib-localization resolves to text |
| Migration path for existing ABML dialogue | **None** — hand-authored ABML continues working; SDK is purely additive |
| Storyline-composer relationship | **None** — different layers (storyline orchestrates *which* dialogue surface fires; dialogue-composer authors *what* the surface emits) |
| Issue #689 (lib-localization → ABML `ILocalizationProvider`) | **Hard prerequisite** for Phase 1 |
| Three-layer pattern (theory / storyteller / composer) | Composer Phase 1 only; theory + storyteller are far-future once a second consumer surfaces |
| Voice-pack metagame (Arcadia Omega) | Deferred to Phase 3+. Phase 1 architecture preserves long-term flexibility without committing to the routing layer's design |

---

## Part 1: Historical Context — The Original Problem Statement

This part preserves the 2026-04-25 problem-statement framing as historical context. The decisions in Parts 2+ are the response to this framing.

### 1.1 Canonical Framing (verbatim from the trigger session)

The Defenders project owner's framing of the problem from the 2026-04-25 session:

> *"I feel like dialogue cannot be separated from story/cutscenes/cinematics — it's a subset that can potentially run in a non-blocking way, but it's really deeply tied to others. If there's a dialogue composer at all, we would have to navigate how exactly the story and cinematic composer consumes that SDK, in order to tie dialogue elements to story and cinematic elements, or, more likely, dialogue is managed within the composer in which the dialogue takes place (story composer and cinematic composer, separately). We have a lib-localization and lib-chat (the latter having NPC dialogue) which tie into this, in a way — could a dialogue composer really just write an ABML format that all of these different composers can read and utilize independently, using the IDs to tie them together? There's a lot to potentially explore and untangle here to find the right answer."*

This framing's intuition — *"could a dialogue composer really just write an ABML format that all of these different composers can read and utilize independently, using the IDs to tie them together?"* — is essentially the architecture this document commits. The Bannou-side decision validates the intuition.

### 1.2 What "Dialogue Authoring" Means

For clarity: when this document refers to "dialogue authoring," it means the **design-time act of authoring lines of speech, conversations, branching choices, and the bindings between them and game-world entities (speakers, scenes, scenarios, choices, voice-line audio, localization)**. It does NOT mean:

- Voice-room infrastructure (lib-voice) — that's audio-pipe runtime, orthogonal.
- Speech-to-text or text-to-speech rendering — TTS is explicitly client-side per [LOCALIZATION.md § Overview](../plugins/LOCALIZATION.md#overview); this document's scope is the authoring of text that TTS would later speak.
- Real-time chat between live players — that's lib-chat's runtime concern when no authored dialogue is involved.

### 1.3 The Two Distinct Dialogue Paths

Bannou supports **two architecturally distinct paths** for "dialogue happens in the game" — diverging at *what an NPC line IS at runtime*, not just at the authoring layer.

**Path A — Concept-driven NPC communication (Arcadia's model).** NPC dialogue is structured concept tuples (`[INTENT] + [SUBJECT]* + [MODIFIER]* + [CONTEXT]*`) emitted into lib-chat via the planned `lexicon` room type. Stack: lib-lexicon (L4) owns the concept ontology + vocabulary validation; lib-collection (L2) gates which vocabulary an NPC has discovered; lib-hearsay (L4) propagates concept-level beliefs across social hops with distortion; lib-disposition (L4) provides drive-motivated communication needs; lib-actor (L2) executes ABML social behaviors composing and interpreting structured messages; lib-chat (L1) is the transport. Player-facing rendering of concept tuples — to natural-language text via lib-localization, SimSpeak symbols, emoticons, or custom UX — is a game-side / client-side concern.

**There is no DialogueComposer-shaped question in Path A.** lib-lexicon owns the authoring substrate, the runtime stack is designed, and the player-facing rendering is the consuming game's choice. This SDK does **not** address Path A's authoring needs; lib-lexicon does.

**Path B — Hand-authored script dialogue (Defenders' model and most non-Arcadia hand-authored games).** NPC dialogue is authored natural-language text strings that NPCs "speak" by following a script. NPCs do not understand each other; runtime semantics are "execute the authored line at the right beat." Existing systems: cinematic-composer's `SpeakAction` (cutscene-bound lines), ABML `type: dialogue` documents (branching trees), lib-quest + lib-localization (quest text), game-coded scripted interactions.

**The DialogueComposer question is entirely a Path B question.** The SDK in this document is Path B authoring substrate.

**Defenders is exclusively Path B and has explicitly opted out of Path A.** Defenders will NOT use lib-lexicon / lib-hearsay / lib-disposition / lib-collection. Script-following suffices for Defenders' game design.

**Path A and Path B share lib-localization as the rendering substrate.** Path A renders concepts to text via the `lexicon` localization category; Path B renders authored lines to text via the new `dialogue` localization category. This is the architectural seam between the paths — it is *not* a DialogueComposer-shaped seam.

### 1.4 Why The Question Was Genuinely Hard

Three observations made this hard to resolve cleanly before the decision session:

1. **The problem was already partially solved across multiple existing systems** (Part 2). ABML `type: dialogue` documents author branching dialogue trees today. cinematic-composer Phase 1 added typed `SpeakAction` for cutscene-embedded dialogue. lib-localization owns the canonical text + pronunciation substrate. lib-chat owns runtime communication channels. None of these is "the dialogue authoring SDK" in name, but each owned a slice of what one would do.

2. **The surfaces dialogue spans have genuinely different authoring + runtime semantics.** A cutscene line is a timeline event with timing + speaker + branch implications. A branching dialogue tree is an interactive choice gate with conditions + GOAP. An NPC chat message is a runtime broadcast. Sharing more than the text-string substrate could be premature abstraction.

3. **Authoring-tool consolidation has user-visible consequences.** A unified DialogueComposer would compete with, complement, or replace existing per-context authoring (cinematic-composer's SpeakAction, ABML dialogue-document authoring, future Lexicon-flavored NPC chat composition).

The decision session resolved these by establishing that:
- The SDK exports to ABML (preserving every existing ABML consumer)
- It widens cinematic SpeakAction without replacing it (preserving Phase 1 cinematic scope)
- It addresses Path B exclusively, leaving Path A's stack independent
- It has no authoring tool and no runtime SDK, sized proportionate to what dialogue actually is rather than maximizing surface area

---

## Part 2: Existing Systems & What They Do Today

This part is preserved from the original problem statement. Each entry is tagged with its primary path so it's clear where each system serves. The existing systems are the constraint set the dialogue-composer SDK has to compose with.

### 2.1 ABML `type: dialogue` Documents — first-class branching dialogue authoring (TODAY)

ABML supports dialogue as a first-class document type per [ABML.md § 1.1](../guides/ABML.md#11-what-is-abml) and § 2.2 (`type: dialogue` documents use the `flows` structure for branching conversations with player choices).

What's already present:
- Branching conversation trees authored in YAML via `flows` + `cond` + `goto` constructs.
- Full ABML expression language (variables, comparisons, null coalescing, ternary, `${localization()}` calls).
- GOAP integration for goal-driven dialogue selection.
- Channels and parallelism (relevant for multi-speaker scenes or simultaneous narrator-plus-speaker).
- Document composition / imports for shared dialogue libraries.
- Compilation to portable bytecode via `behavior-compiler`, reused by NPC behaviors, cutscenes, dialplans, agent cognition.
- Domain actions: `speak` (character + text + emotion), `narrate` (narrator), `choice` (prompt + options + conditions).
- Existing `IDialogueResolver` / `IExternalDialogueLoader` / `ILocalizationProvider` interfaces with file-based default implementations (per ABML.md Appendix D — Dialogue Resolution System).

What's NOT present (gaps the dialogue-composer addresses):
- Typed records for `DialogueLine`, `DialogueTree`, `DialogueChoice` — these are expressed as YAML structures in ABML, not C# types.
- A non-text authoring UI — ABML dialogue documents are authored in text editors today.
- Cross-document dialogue-line references — each ABML document is a self-contained unit; no canonical "dialogue line ID" namespace spans documents.

**Path tag**: Path B substrate (Path A could in principle use ABML for scripted NPC overlays but its primary stack is lib-lexicon + concept tuples).

### 2.2 cinematic-composer SDK — typed `SpeakAction` for cutscene-embedded dialogue (PHASE 1 PLANNED)

Per [CINEMATIC-PHASE-1-COMPOSER-SDK.md § Action catalog](../plans/CINEMATIC-PHASE-1-COMPOSER-SDK.md#action-catalog-first-class-types-for-dialogue-sound-wait-gap), cinematic-composer Phase 1 adds:

- **`SpeakAction`** — dialogue delivery. Parameters: `speaker` (participant-slot reference), `line` (localizable key OR literal string OR — added in this proposal — a `DialogueLineRef`), `duration` (seconds; explicit timing).
- **`PlaySoundAction`** — SFX/music cue.
- **`WaitAction`** — explicit timing pause.

Runtime mapping: `SpeakAction` becomes an ABML `domain_action` node via cinematic-composer's `AbmlExporter`, the existing behavior-compiler + CinematicInterpreter runtime handles dispatch, and the game-side `IClientCutsceneHandler.ExecuteActionAsync(entityId, action, parameters)` resolves the dialogue rendering (subtitle display + optional voice-line playback).

**Path tag**: Path B (cutscene-embedded authored dialogue).

### 2.3 lib-localization — canonical text + translation + pronunciation substrate (IMPLEMENTED)

Per [LOCALIZATION.md](../plugins/LOCALIZATION.md):

- **Data model**: `Category` (organizational container) → `Entry` (single translated string with optional pronunciation + ruby annotations).
- **Key scheme**: dot-separated `{prefix}.{suffix}`, e.g., `direwolf.name`, `rescue-princess.title`. Prefix stored on consumer entities (`localizationKeyPrefix` on Item, Quest, Species, Location, etc.).
- **Per-language entries**: BCP 47 language codes (`en`, `ja-JP`, `fr-FR`). Parameter placeholders `{0}`, `{1}` for runtime substitution.
- **Pronunciation**: IPA phoneme strings + Ruby annotations (CJK furigana). Exported as W3C PLS XML for SSML-consuming TTS engines.
- **Schema-first category registry**: Categories declared in `schemas/localization-categories.yaml`. Built-in categories include `items`, `quests`, `locations`, `lexicon`, `ui`, `species`, etc. **A new `dialogue` category lands as part of this proposal.**
- **DI key validation**: `ILocalizationKeyValidator.ValidateLocalizationKeyAsync(category, prefix, keyId?)` — L2+ services optionally validate keys at entity-creation time. Validation is silently skipped when localization plugin not loaded.
- **ABML integration extension** (issue [#689](https://github.com/beyond-immersion/bannou-service/issues/689)): lib-localization implements `ILocalizationSource` (defined in `bannou-service/Behavior/ILocalizationProvider.cs`) and registers it via DI; `FileLocalizationProvider` (the existing `IAggregateLocalizationProvider`) gains `IEnumerable<ILocalizationSource>` constructor injection so registered sources are auto-discovered. The existing `YamlFileLocalizationSource` is also registered as a source (currently it isn't — `FileLocalizationProvider._sources` is empty today, a defect #689 fixes as a side effect). **This is a hard prerequisite of the dialogue-composer SDK and should land first.** Note: `${localization()}` is *not* yet a registered ABML function — adding it is a separable follow-up issue that depends on #689.

What lib-localization is NOT:
- Not a dialogue-authoring system — it owns per-locale text but has no notion of speakers, conversation structure, or branching.
- Not a runtime conversation broker — clients pull localization tables once and resolve keys locally (Pattern C distribution).

**Path tag**: Shared substrate (both Path A concept rendering and Path B authored line rendering use lib-localization).

### 2.4 lib-chat — runtime conversation channel infrastructure (IMPLEMENTED)

**Serves**: Path A primarily, as transport at the bottom of Bannou's full social-fabric stack. Can also carry Path B messages (e.g., authored localized text emitted by a hand-authored game's NPC behavior), but Path B's authoring is upstream and lib-chat doesn't drive it.

Per [CHAT.md](../plugins/CHAT.md):

- Universal typed message channel primitives. Three built-in room types (`text`, `sentiment`, `emoji`) plus unlimited custom-validated room types registered per game service.
- Persistence modes: ephemeral (Redis TTL) for transient NPC chatter, persistent (MySQL) for important interactions.
- Format validation per room type — custom `ValidatorConfig` lets specific room types validate structured payloads.
- Real-time delivery: WebSocket fan-out via `IEntitySessionRegistry`.
- Contract-governed lifecycle: rooms can be tied to lib-contract instances for quest/encounter-bound conversations.

The relevant Path A planned consumer is **lib-lexicon** ([issue #454](https://github.com/beyond-immersion/bannou-service/issues/454)) — custom `lexicon` room type with messages as concept tuples. **In Path A, no DialogueComposer SDK is required.**

What lib-chat is NOT:
- Not a Path B dialogue-authoring system — it carries messages at runtime but does not author the upstream content.
- Not a localization owner — messages may carry localization keys as content, but lib-chat doesn't validate or resolve them.

**Path tag**: Path A primary (concept-tuple transport); Path B secondary (carries authored text).

### 2.5 storyline-composer + lib-storyline — scenario authoring (IMPLEMENTED); upstream choreographer of dialogue experiences

**Serves**: orchestrator above both paths. Storyline-composer doesn't author dialogue line strings inline — it authors scenario recipes that produce dialogue experiences at runtime via downstream dialogue surfaces.

Per [STORYLINE-COMPOSER-SDK.md](../plans/STORYLINE-COMPOSER-SDK.md), [STORYLINE.md](../plugins/STORYLINE.md), and [STORY-SYSTEM.md](../guides/STORY-SYSTEM.md):

- `storyline-composer` SDK: typed records for `ScenarioDefinition`, `ScenarioPhase`, `PhaseQuestHook`, `TriggerCondition`, `ScenarioMutation`. Authors scenario-level narrative recipes. **No dialogue line strings live in scenario records** — line strings live elsewhere (cutscenes, ABML dialogue documents, lib-quest text, lib-chat messages).
- lib-storyline (L4): passive registry. Stores definitions, evaluates conditions, executes triggers; does not search, decide, or orchestrate.
- God-actors / regional watchers (ABML behavior documents) read scenarios from lib-storyline and execute the phases via ABML, calling downstream services.

Runtime path from authored scenario to dialogue on screen:

```
storyline-composer (authored scenario recipe)
    └─> lib-storyline (passive scenario registry)
            └─> god-actor / regional watcher (ABML behavior orchestrates)
                    ├─> cinematic-composer-authored cutscene (SpeakAction lines play during phases) — Path B
                    ├─> ABML type:dialogue document invocation (branching conversation during a phase) — Path B
                    ├─> lib-quest definition (quest text via lib-localization keys, shown in quest log) — Path B
                    └─> lib-chat message emission — Path A in Arcadia (lexicon concept tuples),
                            or Path B in Defenders (authored localized text)
```

**dialogue-composer and storyline-composer occupy different layers.** Storyline-composer orchestrates *which* dialogue surface fires; dialogue-composer is the authoring substrate for *what* the surface emits.

**Path tag**: Orchestrator above both paths.

### 2.6 lib-voice — voice-room infrastructure (IMPLEMENTED), ORTHOGONAL

P2P mesh + Kamailio/RTPEngine SFU for real-time audio rooms; broadcast consent flows; participant TTL enforcement. **Pure audio infrastructure for live player voice chat.** Not relevant to dialogue authoring.

**Path tag**: Orthogonal.

### 2.7 ABML-Side Action Dispatch — runtime universalism for dialogue actions

The runtime's `IClientCutsceneHandler.ExecuteActionAsync(entityId, action, parameters)` is action-type-agnostic. Adding new typed action subclasses at the SDK layer doesn't require runtime changes — `domain_action` ABML nodes carry the action name + parameter dictionary; the game-side handler dispatches.

This is why the dialogue-composer SDK introduces zero runtime changes. ANY dialogue-related action types it produces reduce to `domain_action` ABML at compile time, with game-side handlers doing the actual rendering.

**Path tag**: Path B runtime substrate (the ABML execution engine).

---

## Part 3: The Recommended Architecture

### 3.1 Three-Layer Pattern Application

Bannou's canonical creative-domain SDK pattern is three layers (per [SDK-OVERVIEW.md](../guides/SDK-OVERVIEW.md)):

| Layer | Role | Phase 1 inclusion |
|---|---|---|
| **Composer** (handcrafted authoring) | Typed records, validation, serialization, ABML export | **YES** — this document's scope |
| **Storyteller** (procedural generation via GOAP) | GOAP-driven dialogue plan composition | **NO** — far future, no second consumer pulling for it |
| **Theory** (formal primitives, pure computation) | Speech-act theory, conversational structure, narrative beats, BDI utterance generation, DAMSL/SWBD-DAMSL dialogue act tagging | **NO** — far future |

The three-layer pattern *applies* to dialogue: theory has formal academic substrates (Searle/Austin speech acts, Grice's maxims, adjacency pairs, Brown & Levinson politeness theory), and a storyteller is plausible (FACADE used drama-management for dialogue; NWN had a hierarchical conversation editor with goal-shaped beats). But Phase 1 is composer-only because:

1. Defenders (the first consumer) needs hand-authored dialogue, not procedural generation.
2. There is no second Bannou consumer pulling for procedural dialogue at meaningful scale.
3. The format-agnostic principle (Storyteller and Composer produce the same format) is preserved by having the SDK output ABML — a future dialogue-storyteller would also output ABML, no migration needed.

### 3.2 SDK Shape

```
sdks/dialogue-composer/
├── dialogue-composer.csproj
├── Lines/
│   ├── DialogueLine.cs                    # Speaker + source + delivery + variants
│   ├── DialogueLineRef.cs                 # Lightweight reference (wraps localization key)
│   ├── DialogueLineSource.cs              # Discriminated: LocalizationKey | Literal | Generated
│   ├── DialogueSpeaker.cs                 # Discriminated: Narrator | NamedCharacter | ParticipantSlot | Expression
│   ├── DialogueDelivery.cs                # Optional emotion enum + intensity + animation hint
│   └── DialogueLineVariant.cs             # Conditional variant: ABML expression + line override
├── Trees/
│   ├── DialogueTree.cs                    # Root: nodes, entry node, metadata
│   ├── DialogueNode.cs                    # Discriminated: LineNode | ChoiceNode | ConditionNode | GotoNode | EndNode
│   ├── DialogueChoice.cs                  # Player choice prompt + options
│   ├── DialogueChoiceOption.cs            # label + condition (ABML expr) + nextNodeId
│   └── DialogueExpression.cs              # Validated wrapper around ABML expression strings
├── Identity/
│   ├── DialogueLineKey.cs                 # Structured wrapper: dialogue.{context}.{lineId}
│   └── DialogueKeyConvention.cs           # Validation: format + reserved prefixes
├── Validation/
│   ├── DialogueValidator.cs               # Tree integrity: reachable nodes, valid refs, conditions parseable
│   ├── ValidationResult.cs
│   └── IDialogueValidationRule.cs         # Extensible (matches storyline-composer / scene-composer pattern)
├── Export/
│   └── AbmlDialogueExporter.cs            # DialogueTree → ABML type:dialogue YAML string
└── Serialization/
    └── DialogueSerializer.cs               # JSON round-trip via BannouJson + DiscriminatedRecordConverter
```

**Dependencies**: `sdks/core/` only (BannouJson, `DiscriminatedRecordConverter<T>`). Does NOT depend on `cinematic-composer` (no spatial primitives required; the cross-SDK seam is `cinematic-composer.SpeakAction.line` widening to accept `DialogueLineRef`, which is one-directional). Does NOT depend on `storyline-composer` (uses ABML expression strings, not typed condition discriminators — different problem domain).

**AOT-compatibility**: All discriminated records, no runtime reflection on user types. Matches `storyline-composer` discipline (T34).

### 3.3 Concrete Type Definitions

```csharp
// === Lines ===

public sealed record DialogueLine
{
    /// <summary>Stable identifier within the line's containing tree or scope.</summary>
    public required string LineId { get; init; }

    /// <summary>Who speaks the line. Discriminated to cover all four binding patterns.</summary>
    public required DialogueSpeaker Speaker { get; init; }

    /// <summary>Where the line text comes from (localization key, literal string, or generated).</summary>
    public required DialogueLineSource Source { get; init; }

    /// <summary>Optional emotion / animation / pacing hint for client-side rendering.</summary>
    public DialogueDelivery? Delivery { get; init; }

    /// <summary>Conditional variants (e.g., relationship-state-dependent line swaps).</summary>
    public IReadOnlyList<DialogueLineVariant> Variants { get; init; } = [];

    /// <summary>
    /// Optional override for voice-line audio (bypasses both pack routing and
    /// lib-localization canonical audioRef). Rare; primarily for prototyping or
    /// debug-specific takes.
    /// </summary>
    public string? VoiceLineRef { get; init; }
}

[JsonConverter(typeof(DialogueLineSourceConverter))]
public abstract record DialogueLineSource(string Type);

/// <summary>Production case: text comes from lib-localization for the active locale.</summary>
public sealed record LocalizationKeyLine(string Key) : DialogueLineSource("localization");

/// <summary>Prototyping case: literal string, optionally locale-tagged. Bypasses lib-localization.</summary>
public sealed record LiteralLine(string Text, string? Locale) : DialogueLineSource("literal");

/// <summary>Future-storyteller case: dialogue-storyteller (or other generator) produces text from a template.</summary>
public sealed record GeneratedLine(
    string TemplateRef,
    IReadOnlyDictionary<string, object>? Params
) : DialogueLineSource("generated");

[JsonConverter(typeof(DialogueSpeakerConverter))]
public abstract record DialogueSpeaker(string Type);

public sealed record NarratorSpeaker() : DialogueSpeaker("narrator");

/// <summary>Cinematic interop — speaker is a named participant slot resolved at scenario instantiation.</summary>
public sealed record ParticipantSlotSpeaker(string SlotName) : DialogueSpeaker("participant_slot");

public sealed record NamedCharacterSpeaker(Guid CharacterId, string? DisplayName) : DialogueSpeaker("character");

/// <summary>Runtime resolution — speaker is whatever the ABML expression resolves to.</summary>
public sealed record ExpressionSpeaker(string Expression) : DialogueSpeaker("expression");

public sealed record DialogueDelivery
{
    public DialogueEmotion? Emotion { get; init; }       // enum: Neutral, Happy, Sad, Angry, Afraid, etc.
    public float? Intensity { get; init; }                // 0.0 - 1.0
    public string? AnimationHint { get; init; }           // free-form string consumed by client renderer
    public IReadOnlyList<string> Tags { get; init; } = []; // e.g., ["whisper", "shout", "internal-monologue"]
}

public sealed record DialogueLineVariant(
    string Condition,             // ABML expression string, validated parseable at SDK level
    DialogueLineSource Source     // Variant overrides the line's source (typically a different localization key)
);

// === Trees ===

public sealed record DialogueTree
{
    public required string TreeId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string EntryNodeId { get; init; }
    public required IReadOnlyDictionary<string, DialogueNode> Nodes { get; init; }
    public DialogueTreeMetadata? Metadata { get; init; }
}

[JsonConverter(typeof(DialogueNodeConverter))]
public abstract record DialogueNode(string Type, string NodeId);

public sealed record LineNode(
    string NodeId,
    DialogueLine Line,
    string? NextNodeId          // null = terminal
) : DialogueNode("line", NodeId);

public sealed record ChoiceNode(
    string NodeId,
    DialogueChoice Choice
) : DialogueNode("choice", NodeId);

public sealed record ConditionNode(
    string NodeId,
    IReadOnlyList<ConditionBranch> Branches,
    string? ElseNodeId
) : DialogueNode("condition", NodeId);

public sealed record GotoNode(
    string NodeId,
    string TargetNodeId
) : DialogueNode("goto", NodeId);

public sealed record EndNode(
    string NodeId,
    string? ReturnExpression    // optional ABML expression evaluated as the tree's return value
) : DialogueNode("end", NodeId);

public sealed record ConditionBranch(string Condition, string NextNodeId);

public sealed record DialogueChoice(
    DialogueLineRef? Prompt,
    IReadOnlyList<DialogueChoiceOption> Options
);

public sealed record DialogueChoiceOption(
    string OptionId,
    DialogueLineRef Label,         // The choice button text
    string? Condition,             // ABML expression — option visibility/availability
    string NextNodeId
);

// === Identity ===

public readonly record struct DialogueLineRef(string LocalizationKey)
{
    public static DialogueLineRef FromKey(string key) => new(key);
}

public sealed record DialogueLineKey(
    string Context,         // e.g., "tavern-betrayal", "act1-cutscene-3"
    string LineId           // e.g., "gee-dying-words.line-12"
)
{
    public string ToLocalizationKey() => $"dialogue.{Context}.{LineId}";
}
```

These are straightforward applications of the discriminated-record + `DiscriminatedRecordConverter<T>` pattern from `sdks/core/`, mirroring storyline-composer's recipe.

### 3.4 What's Deliberately OUT of the SDK

| Excluded | Why |
|---|---|
| Authoring UI / visual tree editor | Defenders builds `defenders-dialogue-composer-godot` (parallel to defenders-cinematic-composer / defenders-sprite-composer). Bannou-side authoring tools come later if and only if a non-Defenders consumer needs them. |
| `lib-dialogue` plugin | Lines live in lib-localization; trees live in ABML files; runtime is the existing behavior runtime. There's no centralized state or distributed safety problem to solve. |
| `dialogue-loader` runtime SDK | Same reason: no new runtime path. |
| Polymorphic "DialogueLoader-as-asset" runtime | Trees can be packaged as embedded ABML in plugin assemblies (status-quo IBehaviorDocumentProvider chain), or as `.bannou` asset bundles via lib-asset. No new format. |
| First-class authoring-time tree-level structural variants (relationship state, story flag selection at the *tree* level) | Variants at this level are runtime-conditional via the consuming surface today. Phase 1 supports line-level variants only (`DialogueLineVariant[]`). Tree-level structural variants are a future Phase 2 addition once consumer pain materializes. |
| `dialogue-storyteller` / `dialogue-theory` | Far future. No second consumer pulling for it. |
| Cross-surface flag/state semantics | ABML expressions + worldstate writes already do this via existing primitives; not the SDK's job. |
| Path A↔B bridge primitives | Both paths produce localization-key references that resolve to text via lib-localization. The "bridge" is lib-localization itself; no SDK-level glue needed. |
| Voice-pack routing logic | Future Arcadia-shaped concern. The SDK exposes `DialogueLine.VoiceLineRef` as an optional Phase 1 override, but the routing layer (active pack → character-VA mapping → audio file) is deferred. See Part 5. |

### 3.5 `AbmlDialogueExporter` Behavior

Given a `DialogueTree`, the exporter emits an ABML `type: dialogue` YAML string with:

- **Metadata**: `version: "2.0"`, `metadata.id`, `metadata.type: dialogue`.
- **Imports**: schema imports as needed (typically none beyond lib-localization category constants, depending on how the tree's expressions reference services).
- **Flows**: one flow per node. Each `LineNode` becomes a flow whose first action is `speak` (for `NamedCharacterSpeaker` / `ParticipantSlotSpeaker` / `ExpressionSpeaker`) or `narrate` (for `NarratorSpeaker`), followed by a `goto` to the next node (or absent for terminal lines).
- **Choice nodes**: emit a `choice` action with the prompt template and per-option `condition` + branching to the option's `NextNodeId`.
- **Condition nodes**: emit a `cond` action with `when` clauses for each branch and an optional `else`.
- **Goto nodes**: emit a `goto` action.
- **End nodes**: emit a `return` action with the optional return expression.
- **Variants**: each `DialogueLine` with non-empty `Variants` emits a nested `cond` block before the speak/narrate action, selecting the appropriate source expression.
- **Localization-key sources**: emit `${localization("dialogue.{context}.{line-id}", locale)}` for the text parameter (resolves via lib-localization at runtime, depends on issue #689).
- **Literal sources**: emit the raw string directly (no localization resolution).
- **Generated sources**: emit a domain action call to the generator service (future-storyteller integration; placeholder Phase 1).

The output compiles cleanly through `behavior-compiler` and runs on the existing ABML runtime. No runtime changes required, no new compiler features.

The validator before export catches:
- Unreachable nodes (dead branches).
- References to non-existent nodes (`NextNodeId` pointing nowhere).
- Conditions that fail to parse as ABML expressions.
- Choice options without `NextNodeId`.
- Localization keys that violate the `dialogue.{context}.{line-id}` convention (with severity Warning if non-conforming, Error if structurally invalid).

### 3.6 Runtime Consumption Model

No new runtime path. Each consumer surface uses an existing runtime:

| Surface | Runtime | Notes |
|---|---|---|
| Branching dialogue trees | ABML runtime via behavior-compiler + CinematicInterpreter or actor-side execution | Trees compile at design time; bytecode is loaded by IBehaviorDocumentProvider chain |
| Cutscene-embedded lines | Cinematic dispatch via `IClientCutsceneHandler.ExecuteActionAsync` | SpeakAction.line widens to accept DialogueLineRef; runtime resolution unchanged |
| NPC chat broadcasts | lib-chat send/receive | Authored line emitted as message payload (localization key); client renders via lib-localization |
| Quest log / UI text | lib-localization key resolution at the client | No change |

Game-side rendering code (subtitle UI, voice playback, choice UI) is consumer-owned. The SDK does not prescribe rendering.

---

## Part 4: Required Adjacent Changes

The SDK alone is insufficient. Three adjustments outside `sdks/dialogue-composer/` land it cleanly. All four (the SDK + three adjacent changes) are Phase 1 scope, with #689 as a hard prerequisite that lands first.

### 4.1 lib-localization Extension: `audioRef` on Entries

Add an optional `audioRef` field to `LocalizationEntry` parallel to `pronunciation` and `ruby`:

```yaml
# schemas/localization-api.yaml
LocalizationEntry:
  type: object
  properties:
    entryId: { type: string, format: uuid }
    categoryId: { type: string, format: uuid }
    key: { type: string }
    language: { type: string }
    text: { type: string }
    pronunciation: { type: string, nullable: true, description: "IPA phoneme string for TTS" }
    ruby: { type: array, items: { $ref: "#/components/schemas/RubyAnnotation" }, nullable: true }
    audioRef: { type: string, nullable: true, description: "Optional reference to canonical voice-line audio asset (e.g., asset bundle entry path). For voice-pack routing, see runtime resolution model in DIALOGUE-COMPOSER-SDK.md." }
    updatedAt: { type: string, format: date-time }
```

**Semantics**: `audioRef` is the **canonical** voice-line reference — the VO file Bannou ships with the line for that locale, before any voice-pack customization is applied. Single value per entry. If a consumer needs multiple actor takes for the same line in the same locale, that's voice-pack territory (see Part 5).

**Distribution**: included in compiled export bundles alongside text and pronunciation. Clients cache it locally with the rest of localization data.

**Backward compatibility**: nullable, additive. Existing entries gain a null `audioRef`; any consumer not interested in audio simply ignores the field.

### 4.2 New `dialogue` Category

Add to `schemas/localization-categories.yaml`:

```yaml
dialogue:
  description: Authored dialogue line text (cutscene speak actions, branching trees, scripted NPC lines)
  validationMode: WarnOnMissing
  consumers: [dialogue-composer]
```

`WarnOnMissing` is appropriate for prototyping flexibility (literal lines stay valid during early authoring; warnings flag forgotten translations).

The `LocalizationCategoryDefinitions.Dialogue` constant generated from this entry is what dialogue-composer's `DialogueLineKey` validates against.

This codifies the answer to the line-ID question: **the localization key IS the dialogue line ID**, and the namespace is `dialogue.{context}.{lineId}`.

### 4.3 cinematic-composer `SpeakAction.line` Widening

`SpeakAction.line` currently accepts "localizable key OR literal string." Widen to accept three forms:

```csharp
public sealed class SpeakAction : ActionDefinition
{
    public required string Speaker { get; init; }       // ParticipantSlot reference
    public required SpeakLineSource Line { get; init; } // Discriminated: DialogueLineRefSource | LocalizationKeySource | LiteralSource
    public required float Duration { get; init; }
}

[JsonConverter(typeof(SpeakLineSourceConverter))]
public abstract record SpeakLineSource(string Type);
public sealed record DialogueLineRefSource(DialogueLineRef Ref) : SpeakLineSource("dialogue_ref");
public sealed record LocalizationKeySource(string Key) : SpeakLineSource("localization_key");
public sealed record LiteralSource(string Text) : SpeakLineSource("literal");
```

The cinematic-composer `TimelineValidator` gains the ability to assert that `DialogueLineRefSource` references a localization key conforming to the `dialogue.{context}.{lineId}` convention. The `AbmlExporter` resolves all three forms to `${localization()}` (DialogueLineRef + LocalizationKey) or the literal string, producing the same ABML output as today for the existing two forms.

**Backward compatibility**: existing literal/key forms continue to work. cinematic-composer Phase 1 lands with all three forms supported.

### 4.4 Issue #689 — lib-localization Implements `ILocalizationSource`

This is a **hard prerequisite** of the dialogue-composer SDK because `AbmlDialogueExporter` emits `${localization("KEY", locale)}` expressions in ABML (after that function lands as a follow-up issue), which only resolve correctly at runtime when lib-localization participates in the existing `ILocalizationProvider` aggregation chain.

**Architecture** (verified against actual codebase 2026-04-26):

The existing extension point is `ILocalizationSource` (defined in `bannou-service/Behavior/ILocalizationProvider.cs`). `FileLocalizationProvider : IAggregateLocalizationProvider` (in `plugins/lib-behavior/Dialogue/FileLocalizationProvider.cs`) routes lookups across registered sources by priority. Today its `_sources` dictionary is empty — `BehaviorServicePlugin.ConfigureServices` registers no sources, which is a real defect #689 fixes as a side effect. The clean way to extend the system is to register a new source, not replace the provider.

**Scope:**

- **New helper class** in `plugins/lib-localization/Services/LocalizationServiceSource.cs` implementing `ILocalizationSource`, marked with `[BannouHelperService("localization-service-source", typeof(LocalizationService), typeof(ILocalizationSource), lifetime: ServiceLifetime.Singleton)]`. Priority `100` (higher than file source's `50` — service wins on conflict).
- **Modify `FileLocalizationProvider` constructor** to accept `IEnumerable<ILocalizationSource>` and seed `_sources` from the injected enumerable. Existing `RegisterSource` API for runtime additions still works. Optionally rename → `AggregateLocalizationProvider` (no longer file-specific); decide based on churn cost.
- **Register `YamlFileLocalizationSource`** in `BehaviorServicePlugin.ConfigureServices` as `ILocalizationSource` with priority `50` (fixes the dead-infrastructure bug).
- **3-part dotted key convention**: `LocalizationServiceSource.GetText("items.direwolf.name", "en")` parses first segment as category code, queries lib-localization for the entry. Returns null for malformed keys (fewer than 3 dotted parts, or unrecognized category) — aggregate falls through to next source.
- **Per-locale full-bundle caching**: first lookup for a locale fetches via `ILocalizationClient.ExportLocalizationAsync(language: locale)` (already Redis-cached server-side), parses into a flat dictionary, caches in process. TTL configurable via `LocalizationConfiguration.CacheDuration` (default 5 minutes). Per-key fetching is forbidden — would be one round trip per `${localization()}` call.
- **Cache invalidation via event subscription**: subscribe to `localization.category.updated` via `IEventConsumer`; invalidate the affected language's cache entry. Multi-node correctness requires this — inline invalidation only reaches the processing node (HELPERS § 11).
- **Missing-key behavior**: return null on miss (matches `ILocalizationProvider.GetText` contract). Honor `validationMode == WarnOnMissing` only as a log warning before returning null. Don't honor `RejectOnMissing` at runtime — that's a creation-time concern enforced by `ILocalizationKeyValidator`.
- **Locale fallback chain**: rely on the aggregate's existing `GetTextWithFallback` chain (`[primary, ...FallbackLocales, DefaultLocale]`). Source does exact-locale lookups only; aggregate handles fallback.
- **Reload semantics**: invalidate-only. Drop locale cache; next lookup re-fetches. Don't pre-warm.
- AOT-compatible (T34). No runtime reflection. Generated client + concrete types only.
- T20 (BannouJson), T30 (telemetry spans), T11 (capture-pattern test verification).

**Critical gap to flag separately**: `ILocalizationProvider` has zero production callers in the existing codebase. `DialogueResolver` injects `IExternalDialogueLoader` and consumes its YAML files directly with embedded `localizations:` blocks; it does NOT consult `ILocalizationProvider`. `${localization()}` is **not** a registered ABML function in `BuiltinFunctions.cs`. So #689 lands the bridge, but a follow-up issue is required to add either (a) the `${localization()}` ABML function calling into `ILocalizationProvider`, or (b) modifications to `DialogueResolver` to consult `ILocalizationProvider` as an additional resolution step. The dialogue-composer SDK's `AbmlDialogueExporter` produces `${localization()}` calls in its emitted ABML — those calls require the follow-up issue to function at runtime. **Phase 1 of dialogue-composer is gated on both #689 and the follow-up consumer issue, not just #689 alone.**

**Note**: #689 is independent of dialogue-composer but is its prerequisite. It unblocks every ABML behavior that wants centralized localization, not just dialogue. The follow-up consumer issue (function or DialogueResolver modification) becomes a separate prerequisite.

---

## Part 5: Voice-Line Audio Resolution Model

The Omega voice-collection-pack metagame (Arcadia long-term: actor A for character X, actor C for character Y, packaged as add-on packs) is a routing layer **on top of** canonical localization, not a replacement for it. Phase 1 ships only the canonical layer; the routing layer is deferred but the architecture explicitly preserves room for it.

### 5.1 The Two Concerns Are Separate

**Concern 1 — What's the canonical text + audio for this line in this locale?**
Substrate concern. Owned by lib-localization. Single `audioRef` per `(category, prefix, suffix, locale)` is sufficient because there's only one *canonical* take per line per locale by definition (the line as Bannou shipped it). Phase 1 ships this.

**Concern 2 — Which actor voices which character in this player's session?**
Metagame concern. Owned by a future `lib-voice-pack` plugin (or extension to lib-asset — TBD when designed). Routes `(activePack, characterId, lineKey, locale) → audio_file_in_bundle`, with fallback to lib-localization's canonical `audioRef` when the pack doesn't cover the line. Phase 1 explicitly defers this.

### 5.2 Phase 1 — The Canonical Layer Only

Runtime VO resolution in Phase 1:

```
Game-side renderer needs audio for (lineKey, locale, characterId)
    │
    ▼
Look up entry in lib-localization for (lineKey, locale)
    │
    ▼
entry.audioRef present?
    ├── YES → play audio (resolved via asset-loader)
    └── NO  → fall back to TTS via entry.pronunciation (IPA → Kokoro / Azure SSML)
                or no audio (text-only subtitle)
```

This is sufficient for Defenders (single canonical VO per line per locale) and for any consumer that doesn't need actor-customization metagaming.

### 5.3 Future — The Voice-Pack Routing Layer (Deferred)

When Arcadia's Omega voice-pack metagame is designed (separate future planning doc):

```
Game-side renderer needs audio for (lineKey, locale, characterId)
    │
    ▼
Active voice pack(s) loaded for this player session
    │
    ▼
Pack catalog: characterId → actorId mapping (per pack)
    │
    ├── Pack covers (characterId)?
    │     ├── YES → look up actor's audio file in pack bundle for (lineKey, locale)
    │     │         ├── present → play audio
    │     │         └── missing → fall through to canonical
    │     └── NO  → fall through to canonical
    │
    ▼
Canonical lib-localization entry.audioRef
    │
    ├── present → play audio
    └── missing → TTS fallback or text-only
```

The pack catalog is a content artifact (likely a `.bannou` asset bundle with a manifest file mapping characters → actors → audio files per locale). Whether the pack catalog is owned by:

- A new `lib-voice-pack` plugin with HTTP API for pack registration / activation per session,
- An extension to `lib-asset` with a known pack-manifest schema,
- A pure client-side convention with no server involvement,

…is a future architectural decision. None of these affect the Phase 1 substrate.

### 5.4 What This Architecture Preserves for Long-Term

Phase 1 commits **only** to:
- `audioRef` is a single optional string field on lib-localization entries (canonical layer).
- The 2-tier resolution model (pack → canonical) is documented in dialogue-composer README and lib-localization deep dive as "future Arcadia extension; not implemented today."
- `DialogueLine.VoiceLineRef` (SDK Phase 1) is a simple optional override for prototyping/debug.

Phase 1 does **not** commit to:
- How packs are stored (plugin vs. asset extension vs. convention).
- How packs are activated (per-session vs. per-character vs. global).
- How character-actor mappings are authored.
- Whether multiple packs can stack (e.g., one pack for player-character voice, another for NPCs).

This means Defenders ships with the canonical layer working correctly, and Arcadia later adds the routing layer (its own future planning doc) without migrating any Defenders content or breaking any lib-localization API.

---

## Part 6: Cross-Cutting Concerns Resolution (Original Part 3)

| § | Concern | Resolution |
|---|---|---|
| **6.1** | Identity scheme — what is a dialogue line and who owns its ID | The localization key IS the dialogue-line ID. Reserved `dialogue` category in `localization-categories.yaml`. Convention: `dialogue.{context}.{line-id}` (e.g., `dialogue.tavern-betrayal.gee-final-words.line-12`). `DialogueLineKey` validates the format. |
| **6.2** | Localization source-of-truth | Production: lib-localization owns the text (DialogueLine references key via `LocalizationKeyLine`). Prototyping: `LiteralLine` source allows inline strings. The discriminated `DialogueLineSource` makes the choice explicit, not implicit-per-call. |
| **6.3** | Voice-line / audio binding | lib-localization gains optional `audioRef` field on entries (per-locale). DialogueLine.`VoiceLineRef` is an optional override for non-default takes. Voice-pack routing is a deferred Arcadia-shaped concern with the door explicitly open (Part 5). |
| **6.4** | Branching / choice model | SDK provides typed `DialogueTree` records. `AbmlDialogueExporter` compiles to ABML `type: dialogue`. ABML remains the runtime format. Hand-authored ABML dialogue documents continue to work. |
| **6.5** | Speaker / character binding | `DialogueSpeaker` discriminated union: `NarratorSpeaker` / `ParticipantSlotSpeaker` (cinematic interop) / `NamedCharacterSpeaker` / `ExpressionSpeaker` (ABML expression for runtime resolution). All three existing patterns are first-class. |
| **6.6** | Variants and conditions | Locale variants → lib-localization (status quo). Non-locale line-level variants → `DialogueLineVariant[]` with ABML expression conditions. Tree-level structural variants stay runtime-conditional via the consuming surface's branching where authored-time variants don't fit. |
| **6.7** | Authoring tool surface | No Bannou-side authoring tool. Defenders builds `defenders-dialogue-composer-godot` Godot project (analogous to defenders-cinematic-composer). |
| **6.8** | Runtime consumption model | No new runtime path. Branching trees → ABML runtime. Cutscene lines → cinematic dispatch via `IClientCutsceneHandler`. NPC chat → lib-chat transport. Quest log lines → lib-localization key resolution. Game-side rendering code is consumer-owned. |

---

## Part 7: Open Questions Resolution (Original Part 5)

| # | Question | Resolution |
|---|---|---|
| **Q1** | "Does a story composer exist or is it planned?" — terminology | Disambiguated. The existing `storyline-composer` is scenario-narrative authoring; the trigger session's "story composer" was almost certainly a hypothetical higher-level surface. The dialogue SDK and storyline SDK don't overlap. |
| **Q2** | Should ABML `type: dialogue` documents remain canonical? | Yes. SDK wraps ABML with typed records; ABML stays the runtime format and the file format. No replacement. Hand-authored ABML dialogue documents continue to work (mirrors cinematic-composer's relationship with hand-authored ABML cutscenes — both coexist). |
| **Q3** | Voice-line binding location | lib-localization extension (`audioRef` on entries). Optional per-line override on `DialogueLine.VoiceLineRef` for non-default takes. Voice-pack routing is a deferred future concern (Part 5). |
| **Q4** | Dialogue-line ID space | Localization key IS the ID. Reserved `dialogue` category codifies the namespace. |
| **Q5** | Runtime SDK | No new runtime SDK. Existing ABML / cinematic / lib-chat / lib-localization paths suffice. |
| **Q6** | Path A/B substrate overlap | lib-localization is the shared rendering substrate; no SDK-level bridge. Both paths produce localization-key references. |
| **Q7** | Cross-surface flag/state consistency | Out of dialogue-composer scope. Game-state writes via existing primitives (worldstate, character state) plus ABML expressions handle this. |
| **Q8** | Speaker-binding scheme | `DialogueSpeaker` discriminated union covers all three existing patterns (cinematic ParticipantSlot, ABML expressions, lib-chat session/character IDs). |
| **Q9** | Variants — authoring vs. runtime | Phase 1: line-level variants are first-class on `DialogueLine`; tree-level structural variants stay runtime-conditional via consuming surface's branching. Future Phase 2 may add tree-level variants. |
| **Q10** | Authoring tool consolidation | Defenders builds `defenders-dialogue-composer` Godot project. Bannou-side authoring tool is not built. |
| **Q11** | Migration path for existing ABML dialogue | None required. Hand-authored ABML dialogue documents continue to work and continue to be a valid authoring path. The SDK is additive. |
| **Q12** | Storyline-composer relationship | None. Different layers (storyline orchestrates *which* dialogue surface fires; dialogue-composer authors *what* the surface emits). |
| **Q13** | lib-localization ABML provider (#689) | Hard prerequisite — but the architecture is `ILocalizationSource` plugin (not provider replacement) per Part 4.4. **Additional prerequisite**: a follow-up issue to add the consuming `${localization()}` ABML function or modify `DialogueResolver` to consult `ILocalizationProvider`. Today nothing calls it. |
| **Q14** | Defenders Phase 1 timing | Not blocking. Defenders' immediate cutscene need is covered by cinematic-composer SpeakAction with literal/key strings. Dialogue-composer can land alongside or after Defenders' Godot scaffolding. |
| **Q15** | Path B SDK consumer audience | Plausibly significant. Most non-Arcadia hand-authored games are Path B. The Defenders-first co-evolution model is sound. |
| **Q16** | Path A→Path B bridge for hybrid consumers | Defer. Both paths produce localization-key references; that's the bridge. SDK-level bridge primitives premature until a hybrid consumer surfaces concrete pain. |

---

## Part 8: Architectural Options Considered (Original Part 4)

The decision session evaluated four architectural options (preserved here as historical record) and selected **Option C+** (an expanded version of Option C with explicit voice-pack flexibility).

### Option A — No DialogueComposer SDK

**Considered**. lib-localization would have been the only shared substrate; each consuming surface authors its own dialogue.

**Rejected because**: leaves four real gaps unsolved — no shared dialogue-line identity (cross-surface line authoring duplicated 3×), no place for voice-line audio refs (no clean schema home), no typed branching dialogue authoring (defenders-dialogue-composer-godot would have to bind to YAML strings), no clean integration seam for cinematic SpeakAction. Not catastrophic but cumulative cost was higher than the cost of the small SDK.

### Option B — Full DialogueComposer SDK with runtime + plugin

**Considered**. Full file-format SDK, per-engine bridges for authoring, runtime SDK (`dialogue-loader`), `lib-dialogue` plugin.

**Rejected because**: dialogue is structurally closer to **music-composer** (file format, no plugin, runtime is somebody else's stack) than to **cinematic-composer** (plugin with caching + agency-gated QTE density + IBehaviorDocumentProvider). The runtime story for dialogue is fully pre-paved by ABML / cinematic dispatch / lib-chat / lib-localization. There is nothing for a `lib-dialogue` plugin to do that one of those four paths isn't already doing better. A runtime SDK creates redundancy with the ABML runtime.

### Option C — Hybrid: shared `DialogueLine` record type, no full composer SDK (the user's "more likely" lean from problem statement)

**Considered**. Small `DialogueLine` record + ID convention only, no branching tree types, no validator, no exporter.

**Rejected as too small**: the line record alone doesn't address typed branching tree authoring (Defenders' Je'rahud caste-confrontation conversation needs visual tooling, which needs typed records to project from). The voice-line binding decision (Q3) needs to land somewhere; the `dialogue` category convention (Q4) needs to land somewhere. Option C addressed the line ID but stopped short of the branching tree problem.

### Option C+ — Focused SDK with branching trees, ABML export, no plugin, no runtime SDK ✅ **SELECTED**

**Selected**. The architecture in Parts 3-5 of this document. Adds typed `DialogueTree`/`DialogueNode` records and `AbmlDialogueExporter` to Option C, while maintaining Option C's discipline of "no plugin, no runtime SDK, no Bannou-side authoring UI."

**Why this size**: it solves all four gaps Option A leaves unaddressed (line identity, voice-line binding location, typed tree authoring, cinematic seam) without absorbing the maintenance overhead of Options B (plugin + runtime SDK). The SDK is sized like storyline-composer (typed records + validator + serializer + ABML exporter) rather than like cinematic-composer (which needs a plugin for compilation caching + agency gating + document provider).

### Option D — Defer entirely: codify lib-localization conventions only

**Considered**. The `dialogue` category and key-namespace convention only. No SDK, no `audioRef` field, no `DialogueLineRef`.

**Rejected because**: leaves three things on the table that are cheap to address now and expensive to retrofit later — `DialogueLineRef` for cinematic SpeakAction (needed whether or not the SDK exists), the voice-line audio binding decision (forces hard-deadline pressure when VA recording starts), the Path A/B substrate seam (lib-localization extension serves both paths from day one). Option D was a legitimate outcome — the original problem statement explicitly admitted it — but the small Phase 1 cost is justified by the optionality preserved.

---

## Part 9: Phasing

| Phase | Scope | Trigger |
|---|---|---|
| **0a** (prerequisite) | Issue [#689](https://github.com/beyond-immersion/bannou-service/issues/689) — lib-localization implements `ILocalizationSource`; `FileLocalizationProvider` gains `IEnumerable<ILocalizationSource>` injection; `YamlFileLocalizationSource` registered. Fixes both the bridge gap and the existing zero-sources defect. | Independent of dialogue-composer; can land any time. **Must precede Phase 1.** |
| **0b** (prerequisite) | Follow-up issue (TBD) — adds `${localization()}` ABML function to `IFunctionRegistry`, OR modifies `DialogueResolver` to consult `ILocalizationProvider` as a third resolution step. Without 0b, #689's bridge has no caller and `AbmlDialogueExporter`'s emitted `${localization()}` calls are unresolvable at runtime. | Depends on 0a. **Must precede Phase 1.** |
| **1** (this document's scope) | `sdks/dialogue-composer/` SDK as designed in Part 3 + lib-localization `audioRef` extension (Part 4.1) + `dialogue` category in `schemas/localization-categories.yaml` (Part 4.2) + cinematic-composer `SpeakAction.line` widening (Part 4.3) | Lands after #689. Defenders is the first consumer; not blocking on Defenders' Godot scaffolding. |
| **2** (future, on consumer pain) | Phase 1 authoring-time first-class tree-level structural variants (relationship-state-driven tree variants, story-flag-driven branches authored as variants); Bannou-side optional CLI/MCP introspection tools (line catalog viewer, validator); `lib-dialogue` plugin **only if** cross-surface line-registry storage proves valuable beyond what lib-localization provides | Triggered by real cross-surface line-reuse pain. |
| **3** (Arcadia-shaped, far future) | `lib-voice-pack` (or lib-asset extension) — character-VA routing, pack registration, active-pack management for Arcadia's Omega voice-collection-pack metagame | Triggered by Arcadia's voice-pack metagame design and pre-launch VA recording planning. |
| **4** (far future) | `dialogue-storyteller` for procedural NPC dialogue generation; `dialogue-theory` for formal speech-act / BDI substrates | Triggered by a consumer with procedural NPC chatter at scale. |

The Phase 1 scope is a deliverable in days-to-weeks of SDK work, not weeks-to-months. It's deliberately small.

**No phase is "optional" or "deferred within Phase 1."** Every Phase 1 deliverable is required for the SDK to fulfill its stated purpose. If a phase's scope turns out to be too large during implementation, the correct response is to decompose it into sub-phases with their own exit criteria — not to declare part of it optional.

---

## Part 10: Phase 1 Acceptance Criteria

Phase 1 is "done" when:

1. **#689 lands first** (Phase 0a): `LocalizationServiceSource` in `plugins/lib-localization/Services/` implements `ILocalizationSource` (registered via `[BannouHelperService]` with priority 100). `FileLocalizationProvider` constructor accepts `IEnumerable<ILocalizationSource>`. `YamlFileLocalizationSource` registered in `BehaviorServicePlugin.ConfigureServices` (priority 50). Tests cover cache hit, cache invalidation on `localization.category.updated`, malformed-key fall-through, source priority ordering, locale fallback through aggregate's chain.
1a. **Consumer issue lands** (Phase 0b): Either `${localization()}` ABML function registered in `IFunctionRegistry` and wired to `ILocalizationProvider`, OR `DialogueResolver` modified to consult `ILocalizationProvider`. Without this, the bridge has no production caller and `AbmlDialogueExporter`'s output is unresolvable.
2. **`sdks/dialogue-composer/` exists** with the type definitions in Part 3.3 (`DialogueLine`, `DialogueTree`, `DialogueNode` discriminators, `DialogueSpeaker` discriminators, `DialogueLineSource` discriminators, `DialogueDelivery`, `DialogueLineVariant`, `DialogueChoice`, `DialogueLineRef`, `DialogueLineKey`). All discriminated records use `DiscriminatedRecordConverter<T>` from `sdks/core/`.
3. **`AbmlDialogueExporter`** exports a `DialogueTree` to a YAML string that compiles cleanly via `behavior-compiler` and executes through the existing ABML runtime. Integration test proves the full pipeline.
4. **`DialogueValidator`** catches: unreachable nodes, dangling node references, choice options without `NextNodeId`, malformed ABML expressions in conditions, localization keys violating the `dialogue.{context}.{line-id}` convention.
5. **`DialogueSerializer`** round-trips `DialogueTree` through JSON via `BannouJson` with all discriminator types preserved.
6. **`schemas/localization-api.yaml`** declares the optional `audioRef: string?` field on `LocalizationEntry`. Entry mutation endpoints accept and return it. Compiled exports include it.
7. **`schemas/localization-categories.yaml`** declares the `dialogue` category. Generated `LocalizationCategoryDefinitions.Dialogue` constant exists.
8. **cinematic-composer's `SpeakAction.line`** accepts `DialogueLineRefSource | LocalizationKeySource | LiteralSource` (discriminated). cinematic `TimelineValidator` validates `DialogueLineRefSource.Ref` keys against the `dialogue` category convention. cinematic `AbmlExporter` produces equivalent ABML for all three forms.
9. **No new plugins**: no `lib-dialogue`, no `lib-voice-pack`. Verify by checking `plugins/` directory.
10. **No new runtime SDKs**: no `dialogue-loader`. Verify by checking `sdks/` directory.
11. **AOT-clean** (T34): no runtime reflection, no `MakeGenericMethod`, no dynamic codegen in either the SDK or the lib-localization additions.
12. **Tenets compliance**: T20 (BannouJson), T30 (telemetry spans on lib-localization additions), T34 (AOT). Structural tests pass.

**Phase 1 is NOT acceptance for**:
- Voice collection pack routing (deferred to Phase 3).
- Per-character actor mapping (deferred to Phase 3).
- `lib-dialogue` plugin (deferred to Phase 2 only if pain materializes).
- `dialogue-storyteller` / `dialogue-theory` (deferred to Phase 4).
- Authoring-time tree-level structural variants (deferred to Phase 2 if pain materializes).
- `defenders-dialogue-composer-godot` UI (Defenders-side deliverable, not Bannou-side scope).
- Path A↔B SDK bridge (deferred indefinitely).

---

## Part 11: Defenders Consumer-Side Context (Original Part 7)

Defenders of Ba'hara is the trigger consumer for this planning. The decision session weighed Defenders' near-term needs alongside the broader architectural question.

### 11.0 Defenders' Path Position

Defenders is **exclusively Path B (hand-authored script dialogue)**. Defenders will NOT use lib-lexicon / lib-collection / lib-hearsay / lib-disposition. NPCs in Defenders follow authored scripts; the runtime semantics are "execute the authored line at the right beat." There is no NPC-understanding substrate, no concept-tuple emission, no social-fabric simulation — Defenders' game design doesn't require any of that.

### 11.1 Cutscene Dialogue (high priority — already in flight)

Defenders' Act 1/2/3 cutscene content leans heavily on spoken dialogue (per defenders-kb D122/D105/D91/D119/D50). This drove cinematic-composer Phase 1's `SpeakAction` addition. **Already covered** by cinematic-composer Phase 1 + Defenders' planned `defenders-cinematic-composer` Godot UI. Phase 1 of dialogue-composer adds `DialogueLineRef` to `SpeakAction.line` for typed cross-surface references; the literal/key forms continue to work for prototyping.

### 11.2 Branching Dialogue Trees (medium priority)

Defenders has dialogue-tree-shaped content (e.g., Je'rahud caste-confrontation conversation per defenders-kb D109/D110). Phase 1 of dialogue-composer provides typed `DialogueTree` records that `defenders-dialogue-composer-godot` can author and that compile to ABML. Defenders does NOT have an immediate need that forces this — current content can be authored as ABML dialogue documents starting today.

### 11.3 NPC Chat / Lexicon (low priority — speculative)

Defenders' design does not currently call for runtime NPC-to-NPC structured communication. The Path A stack is irrelevant to Defenders.

### 11.4 Voice-Line Audio (medium priority — pre-launch decision)

Defenders' voice acting strategy is undecided. Phase 1 of dialogue-composer + lib-localization `audioRef` covers the canonical-VO case if Defenders ships with VO. If Defenders ships with TTS-only, lib-localization's existing IPA + W3C PLS paths plus client-side Kokoro rendering covers the runtime. The voice-pack routing layer (Phase 3) is Arcadia-shaped, not Defenders-shaped.

### 11.5 Localization (planning-stage)

Defenders is single-locale at first launch (English). Multi-locale is a post-launch consideration. Phase 1 supports both.

### 11.6 Defenders Consumer Summary

Defenders' driving need is **(11.1) cutscene dialogue**, and that need is **already covered** by cinematic-composer Phase 1's SpeakAction. **No Defenders-side need requires the dialogue-architecture decision to land before Defenders' Godot scaffolding.** Phase 1 of this SDK lands on its own schedule; Defenders consumes it when ready.

---

## Part 12: Non-Goals (Original Part 6)

To keep scope tight, the following are explicitly **not** Phase 1 scope:

- **Defenders-side Godot UI shape.** What `defenders-cinematic-composer` and `defenders-dialogue-composer-godot` look like is a Defenders concern.
- **Voice-pack metagame design.** Arcadia-shaped, far-future. Phase 1 leaves the door open via the documented 2-tier resolution model (Part 5) without committing to the routing layer's design.
- **Cinematic-composer Phase 1 changes beyond SpeakAction.line widening.** SpeakAction/PlaySoundAction/WaitAction additions are committed per Defenders D132 input. Phase 1 of dialogue-composer adds the `DialogueLineRef` form to `SpeakAction.line` only.
- **lib-chat / lib-lexicon scope changes.** lib-chat's existing trajectory is unaffected. lib-lexicon's planned implementation is unaffected.
- **TTS / voice-rendering pipeline.** lib-localization owns IPA + W3C PLS; clients render via Kokoro/Azure. Phase 1 touches authoring + the canonical `audioRef` field, not the rendering pipeline.
- **Multi-player dialogue synchronization.** Out of scope; Defenders is single-player; cross-client dialogue state is a separate problem.
- **Procedural dialogue generation.** Out of Phase 1 scope. `dialogue-storyteller` is far-future Phase 4.
- **Localization workflow tooling.** XLIFF/PO import, translator-CAT-tool integration, completeness reports — tracked separately under lib-localization extensions.
- **Tree-level authoring-time structural variants.** Phase 2 if pain materializes. Phase 1 supports line-level variants only (`DialogueLineVariant[]`).
- **Bannou-side authoring UI / tree visualizer.** Phase 2 if a non-Defenders consumer needs it.

---

## References

### Bannou docs

- [`docs/guides/ABML.md`](../guides/ABML.md) — ABML language reference; `type: dialogue` documents are first-class. Appendix D documents the existing dialogue resolution system (`IDialogueResolver`, `IExternalDialogueLoader`, `ILocalizationProvider`).
- [`docs/plans/CINEMATIC-PHASE-1-COMPOSER-SDK.md`](../plans/CINEMATIC-PHASE-1-COMPOSER-SDK.md) — cinematic-composer Phase 1, SpeakAction action catalog (gains `DialogueLineRef` form per § 4.3).
- [`docs/plans/STORYLINE-COMPOSER-SDK.md`](../plans/STORYLINE-COMPOSER-SDK.md) — pattern source for typed-record SDK with discriminated subtypes.
- [`docs/plugins/STORYLINE.md`](../plugins/STORYLINE.md) — lib-storyline plugin deep dive (orchestrator above both paths).
- [`docs/guides/STORY-SYSTEM.md`](../guides/STORY-SYSTEM.md) — narrative system overview.
- [`docs/plugins/LOCALIZATION.md`](../plugins/LOCALIZATION.md) — lib-localization plugin deep dive (gains `audioRef` field per § 4.1, `dialogue` category per § 4.2).
- [`docs/plugins/CHAT.md`](../plugins/CHAT.md) — lib-chat plugin deep dive (transport-only; unchanged).
- [`docs/plugins/VOICE.md`](../plugins/VOICE.md) — lib-voice plugin deep dive (orthogonal; voice rooms).
- [`docs/guides/SDK-OVERVIEW.md`](../guides/SDK-OVERVIEW.md) — three-layer composer pattern (theory / storyteller / composer).
- [`docs/planning/SCENE-LOADER-SDK.md`](SCENE-LOADER-SDK.md) — format precedent for first-consumer-driven SDK.
- [`docs/planning/SPRITE-COMPOSER-SDK.md`](SPRITE-COMPOSER-SDK.md) — first-consumer-driven SDK precedent.
- [`docs/planning/MUSIC-COMPOSER-SDK.md`](MUSIC-COMPOSER-SDK.md) — file-format-only SDK precedent (parallel for dialogue-as-format-only with no plugin).
- [`docs/reference/HELPERS-AND-COMMON-PATTERNS.md`](../reference/HELPERS-AND-COMMON-PATTERNS.md) — `VariableProviderCacheBucket`, cache invalidation patterns (used by #689 implementation).

### Bannou issues referenced

- [#454](https://github.com/beyond-immersion/bannou-service/issues/454) — lib-lexicon (planned Chat consumer for structured NPC communication; Path A authoring substrate).
- [#508](https://github.com/beyond-immersion/bannou-service/issues/508) — `localizationKeyPrefix` field rollout across L2 schemas.
- [#688](https://github.com/beyond-immersion/bannou-service/issues/688) — Localization for procedurally-generated content.
- [#689](https://github.com/beyond-immersion/bannou-service/issues/689) — **Hard prerequisite for Phase 1 (Phase 0a).** lib-localization implementing `ILocalizationSource`. Architecture: source-plugin extension to existing `IAggregateLocalizationProvider`, NOT provider replacement. Also fixes pre-existing dead-infrastructure defect (FileLocalizationProvider's empty `_sources`).
- TBD follow-up issue — **Hard prerequisite for Phase 1 (Phase 0b).** Either add `${localization()}` ABML function (registers in `IFunctionRegistry`, wires to `ILocalizationProvider`) or modify `DialogueResolver` to consult `ILocalizationProvider` as a third resolution step. Without this, #689's bridge has no production caller.

### Defenders-side references (historical context only)

- `defenders-kb/00-meta/DECISIONS.md` D2 (embedded mode), D3 (Composer SDKs as content authoring path), D50, D91, D105, D119, D122 (cutscene-content decisions driving cutscene dialogue volume), D132 (cinematic-composer Phase 1 first-consumer requirements including SpeakAction), D135 (scene-loader-SDK first-consumer commit), D136 (Godot pivot), D140 (Bannou `-godot` SDK family commit).
- `defenders-kb/00-meta/QUESTIONS-RESOLVED.md` Q93 (cinematic-composer Phase 1 mapping), Q99 (scene-loader-SDK gap-investigation).

---

*This document captures the Bannou-side architectural decisions for the dialogue-composer SDK. Phase 1 implementation can begin once issue [#689](https://github.com/beyond-immersion/bannou-service/issues/689) lands. Defenders is the first consumer; `defenders-dialogue-composer-godot` is a Defenders-side deliverable parallel to `defenders-cinematic-composer`.*
