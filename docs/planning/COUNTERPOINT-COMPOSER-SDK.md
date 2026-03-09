# Counterpoint Composer SDK: Structural Template Music Authoring

> **Type**: Design
> **Status**: Aspirational
> **Created**: 2026-02-01
> **Last Updated**: 2026-03-09
> **North Stars**: #5
> **Related Plugins**: Music

## Summary

Proposes a MusicComposer SDK providing structural template workbench tooling for human composers to author counterpoint-compatible music. Templates capture uncopyrightable structural parameters (chord progressions, form, energy curves) and the SDK validates harmonic compatibility between pieces at specified temporal offsets, enabling interlocking regional themes, deity leitmotifs, and generational character music in Arcadia. The SDK does not yet exist; MusicTheory and MusicStoryteller SDKs provide the foundation primitives it would build on.

---

## Executive Summary

The Bannou music system generates adaptive, procedural music at runtime via MusicTheory (formal harmony/melody) and MusicStoryteller (narrative-driven emotional arcs). This works beautifully for dynamic, situational soundtrack generation. But **composed music** -- regional themes, deity leitmotifs, character themes, cinematic set pieces -- requires human authorship.

This document proposes a **Counterpoint Composer SDK** (working name: `MusicComposer`) that gives human composers a structural template workbench for authoring music that interlocks. The core concept: analyze the structural skeleton of an existing piece (or design one from scratch), then compose new music within that skeleton's constraints, with real-time validation that the new piece is harmonically compatible with other pieces sharing the same template -- including when played simultaneously at temporal offsets.

The SDK does **not** generate finished music. It provides the ruler, compass, and constraint checker. The human makes every creative decision.

---

## The Problem

Arcadia's world has overlapping musical contexts. A player walking from the market district to the temple district crosses a boundary where two themes overlap. A divine encounter layers a god's theme over the ambient regional music. A character's personal theme should echo their family's musical identity across generations.

Without structural coordination, these overlaps produce cacophony. With hand-coordination by a skilled composer, they produce magic -- but only if the composer has tooling that tells them "these two pieces will harmonize at these points and clash at these points."

Today, composers doing this kind of work rely on intuition, manual analysis, and extensive trial-and-error. The Counterpoint Composer SDK replaces trial-and-error with constraint validation and structural templates.

---

## Part 1: The Structural Template

### 1.1 What a Template Captures

A structural template is an abstract, non-copyrightable description of a piece's architecture. It contains **zero melodic content** -- no specific pitches in sequence, no rhythmic motifs, no lyrics, no arrangement details. Only structural parameters that music theory and copyright law agree are unprotectable building blocks.

```yaml
template:
  name: "power-ballad-arc-01"
  origin: "manual"  # or "analyzed" -- see Section 1.2

  # Global parameters
  key: "Bb minor"
  tempo: 90
  time_signature: "4/4"
  total_bars: 64

  # Macro form
  form:
    - section: "Intro"
      bars: 4
      energy: 0.3
      tension: 0.2
      stability: 0.8

    - section: "Verse"
      bars: 8
      energy: 0.5
      tension: 0.4
      stability: 0.6
      harmony:
        progression: [i, III, VII, VI]
        harmonic_rhythm: "whole"  # one chord per bar
        cadence: "half"

    - section: "PreChorus"
      bars: 4
      energy: 0.7
      tension: 0.7
      stability: 0.4
      harmony:
        progression: [iv, VII, III, V]
        harmonic_rhythm: "whole"
        cadence: "half"

    - section: "Chorus"
      bars: 8
      energy: 1.0
      tension: 0.5
      stability: 0.7
      harmony:
        progression: [VI, III, VII, i]
        harmonic_rhythm: "half"  # two chords per bar
        cadence: "authentic"

    - section: "Verse"
      bars: 8
      energy: 0.5
      tension: 0.4
      # ... (same template as first verse)

    - section: "Chorus"
      bars: 8
      energy: 1.0
      tension: 0.5

    - section: "Bridge"
      bars: 4
      energy: 0.4
      tension: 0.8
      stability: 0.3
      harmony:
        progression: [iv, v, VI, VII]
        harmonic_rhythm: "whole"
        cadence: "deceptive"

    - section: "FinalChorus"
      bars: 8
      energy: 0.9
      tension: 0.3
      stability: 0.9
      harmony:
        progression: [VI, III, VII, i]
        harmonic_rhythm: "half"
        cadence: "authentic"

    - section: "Outro"
      bars: 4
      energy: 0.2
      tension: 0.1
      stability: 1.0

  # Vocal/melodic contour hints (direction only, no pitches)
  contour_hints:
    verse:
      shape: "arch"
      range_semitones: 7
      step_leap_ratio: 0.8   # 80% stepwise, 20% leaps
      climax_position: 0.6   # highest note at 60% through phrase
    chorus:
      shape: "ascending"
      range_semitones: 12
      step_leap_ratio: 0.6
      climax_position: 0.8

  # Tension curve (normalized, one value per section)
  tension_curve: [0.2, 0.4, 0.7, 0.5, 0.4, 0.5, 0.8, 0.3, 0.1]

  # Energy curve
  energy_curve: [0.3, 0.5, 0.7, 1.0, 0.5, 1.0, 0.4, 0.9, 0.2]
```

### 1.2 Template Sources

Templates can originate from four sources, listed from most legally distant to closest:

**Source A: Hand-Authored (Safest)**
A composer designs a template from scratch based on their artistic intent. "I want a piece that builds slowly, peaks at the chorus, drops to a contemplative bridge, then surges to the final chorus." No source song involved.

**Source B: Genre-Averaged (Very Safe)**
Statistical analysis of many songs in a genre produces an "average" structural profile. "The typical pop-rock anthem has this energy curve, this section layout, this harmonic rhythm." The template is an abstraction of a genre, not any specific work.

**Source C: Game-Contextual (Very Safe)**
Designed for specific gameplay scenarios. "Boss fight music with two phases: tense approach and climactic confrontation." "Marketplace theme with steady energy and bright harmonic language." No source song needed.

**Source D: Analyzed from Existing Song (Safe with Guidelines)**
A specific song is analyzed and its structural parameters extracted. This is the "Demons by Imagine Dragons" use case. The extracted template contains only uncopyrightable parameters (chord progressions, form, key, tempo, energy arc). See [Section 5: Legal Framework](#part-5-legal-framework) for detailed analysis.

**Analysis tools for Source D** are a convenience feature, not the SDK's core. The SDK works identically regardless of template origin -- it doesn't know or care whether a template came from hand authoring or song analysis.

### 1.3 What a Template Explicitly Does NOT Contain

To maintain the legal and creative firewall between template and source:

- No specific pitch sequences (melody)
- No specific rhythmic patterns (only density and syncopation as scalar values)
- No lyrics or syllabic rhythm
- No instrumentation or arrangement details
- No specific voicings or registral assignments
- No audio samples or recordings
- No reference to any specific copyrighted work (the template is anonymous)

A template is the musical equivalent of an architectural blueprint that says "3-story building, 2000 sq ft per floor, load-bearing walls here" -- it constrains the structure without specifying the aesthetics.

---

## Part 2: Counterpoint Compatibility

### 2.1 The Core Concept

Two pieces are **counterpoint-compatible** when they can be played simultaneously (with or without temporal offset) and produce harmonically consonant results. This is the "duet" property -- each piece works alone, but layered they create something greater than either part.

This is not a new idea. It is one of the oldest ideas in Western music:

| Historical Practice | Era | What It Proves |
|---|---|---|
| **Species counterpoint** (Fux) | 1725 | Independent melodies can be constrained to combine harmonically |
| **Invertible counterpoint** (Bach) | 1700s | Two voices can swap registers and still work -- combinability is designable |
| **Quodlibet** (Bach, Goldberg Var. 30) | 1741 | Two folk songs over shared bass = deliberate melodic interlock |
| **Jazz contrafact** (Parker, et al.) | 1940s+ | New melody over existing chords = standard creative practice |
| **Phase music** (Reich) | 1960s+ | Material designed for offset compatibility at ALL phase positions |
| **Counterpoint songs** (Berlin, Willson) | 1950s+ | Two standalone songs composed to combine ("Lida Rose"/"Will I Ever Tell You" from *The Music Man*, Irving Berlin's "Play a Simple Melody"/"You're Just in Love") |
| **Broadway ensemble** (Schonberg, Sondheim) | 1980s+ | 8+ independent vocal lines designed from the start to combine ("One Day More", "Tonight Quintet", "Prima Donna") |
| **Film counterpoint** (Bestor/Cardon) | 1993 | Two songs that sound completely different independently, revealed to interlock ("The Curse"/"The Melody Within" from *Rigoletto* film) |

#### The Independence Spectrum

These examples sit on a spectrum of **how independent the individual pieces sound** when heard alone. This spectrum is critical because our target is the high-independence end:

| Example | Independence | Combination Quality |
|---|---|---|
| Steve Reich "Piano Phase" | None (identical material) | Process-driven emergent patterns |
| "One Day More" (*Les Miserables*) | Low (fragments, clearly parts of a whole) | Dramatic theatrical climax |
| "Lida Rose"/"Will I Ever Tell You" (*The Music Man*) | Medium (each is a complete song, but clearly designed as a pair) | Satisfying theatrical reveal |
| Bach's Goldberg quodlibet | Medium (folk songs, recognizable) | Clever academic demonstration |
| "The Curse"/"The Melody Within" (*Rigoletto* film) | **High** (sound quite different, each stands alone) | Revelation -- "wait, these were connected?" |

**Our target is the Rigoletto film end of this spectrum**: pieces with high independence that each stand as complete, self-sufficient musical works. The counterpoint compatibility is a hidden structural property, not an audible surface feature. When the pieces are combined, the effect is revelatory rather than expected. This is also the most legally clean position, because high independence means each piece clearly stands on its own creative merits.

### 2.2 Compatibility Tiers

Not all counterpoint needs to be strict. The SDK defines three tiers of increasing constraint:

**Tier 1: Structural Echo**
- Same key center
- Same macro energy curve
- Independent melodies with no vertical interval checking
- Result: pieces sound "related" -- same emotional world, compatible mood
- Use case: two themes in adjacent game areas that should feel like they belong together

**Tier 2: Harmonic Counterpoint**
- Same key center and chord progression (or compatible progressions)
- Chord changes aligned at phrase boundaries
- Vertical interval checking at strong beats (consonances required)
- Weak beats allow passing dissonance
- Result: pieces harmonize when overlaid, with occasional tension that resolves
- Use case: two themes that overlap during area transitions

**Tier 3: Full Duet**
- All Tier 2 constraints, plus:
- Note-level voice leading validation (no parallel fifths/octaves)
- Complementary rhythmic patterns (when one voice is active, the other breathes)
- Contrary motion preference (when one ascends, the other descends or holds)
- Registral separation (voices occupy distinct pitch ranges)
- Result: pieces interlock as a polished duet, as if composed together
- Use case: deity encounter music, character/spirit duet themes

### 2.3 Temporal Offset Compatibility

The most technically interesting constraint: two pieces should combine not just from beat 1, but with one starting partway through the other (e.g., 30 seconds later).

**Why this works when designed correctly:**

If both pieces share:
1. **Same tonal center** throughout (or modulate in parallel)
2. **Same harmonic rhythm** (chords change at the same rate)
3. **Chord changes on phrase boundaries** (every 4 or 8 bars)

...then any offset that aligns with the phrase grid produces structural consonance. Both pieces are on compatible chords simultaneously because they share the same harmonic language.

Offsets that don't exactly align to phrase boundaries still work because:
- The shared key center means most notes are diatonic to the same scale
- Consonant interval rules at strong beats mean structural tones agree
- Passing dissonances on weak beats resolve naturally within the shared harmonic framework

**The Steve Reich insight**: In *Piano Phase*, the same 12-note pattern works at ALL 12 phase positions because the pattern was designed with compatible intervals at every alignment. Our constraint is gentler -- we only need specific offset points to work well, not all of them. The SDK lets composers define which offsets they're designing for and validates compatibility at those specific points.

**Offset specification in the compatibility model:**

```yaml
counterpoint:
  compatibility_tier: "full_duet"
  designed_offsets:
    - offset_bars: 0       # simultaneous start
    - offset_bars: 8       # one chorus ahead
    - offset_bars: 16      # two sections ahead
  registral_assignment:
    piece_a: { low: "Bb3", high: "F5" }
    piece_b: { low: "F3", high: "Db5" }
  motion_preference: "contrary"
  rhythmic_complementarity: 0.7
```

---

## Part 3: The Composer Workbench

### 3.1 Design Philosophy

The SDK is a **constraint-aware composition assistant**, not a generator. The human composer makes every creative decision: melody, rhythm, instrumentation, arrangement, dynamics. The SDK's role is to:

1. **Visualize** the template (energy curves, tension arcs, section boundaries)
2. **Validate** the composition against template constraints in real time
3. **Check compatibility** between pieces at specified offsets
4. **Suggest** alternative notes when conflicts are detected (not generate melody)
5. **Report** voice leading violations across the counterpoint pair

The distinction is critical. The SDK is a ruler and compass, not a printing press. The composer is the author. The template is a scaffold. The validation is quality assurance.

### 3.2 Workbench Capabilities

**Template Visualization**
- Energy/tension curve display with section boundaries marked
- Harmonic rhythm grid showing chord change points
- Contour hint overlay (target melodic shape per section)
- Phase-offset timeline showing which sections of Piece A align with which sections of Piece B at each designed offset

**Real-Time Constraint Validation**
As the composer writes (inputting notes via MIDI, notation software, or the SDK's own input), the workbench continuously checks:

- **Energy compliance**: "Your current section is at energy 0.3 but the template targets 0.7 -- this should be building"
- **Contour compliance**: "The template suggests an ascending contour here; your melody is descending"
- **Harmonic compliance**: "The template specifies chord iv here; your melody note (G natural) is outside the chord -- intentional tension or error?"
- **Range compliance**: "This note exceeds the registral assignment for this piece"

Violations are flagged as warnings, not errors. The composer decides whether to comply or deviate intentionally. Templates are guides, not prisons.

**Counterpoint Compatibility Checking**
When two pieces share a template (or compatible templates), the workbench can check their vertical relationship:

- **Interval analysis at strong beats**: "At bar 12, beat 1, your note creates a perfect fourth with Piece A's note at this offset -- this is dissonant in two-voice counterpoint. Consonant alternatives: [Db, F, Ab]"
- **Parallel motion detection**: "Bars 8-10 have parallel fifths between the two pieces at offset 0. Consider contrary motion."
- **Rhythmic independence check**: "Both pieces have note onsets on every beat in bars 4-8. Consider offsetting rhythmic patterns for clearer voice independence."
- **Registral crossing warning**: "At bar 15, Piece B crosses above Piece A's register. Voice crossing obscures which piece is 'on top.'"

**Offset Sweep**
A batch analysis that checks counterpoint compatibility across all designed offsets simultaneously, producing a heat map: green (consonant), yellow (acceptable tension), red (problematic clash). The composer can see at a glance which bars need attention at which offsets.

### 3.3 What the Workbench Does NOT Do

- **Does not generate melody.** It may suggest alternative *notes* at conflict points (from the current chord's pitch set), but it never produces melodic sequences.
- **Does not generate rhythm.** It may flag rhythmic density as too high/low relative to the template, but it doesn't write rhythmic patterns.
- **Does not generate arrangement.** Instrumentation, voicing, and orchestration are entirely the composer's domain.
- **Does not require a source song.** Templates can be hand-authored. The analysis tool for extracting templates from existing songs is a convenience feature, not a requirement.

---

## Part 4: Integration with Existing Music Infrastructure

### 4.1 Relationship to Existing SDKs

The MusicComposer SDK sits alongside MusicTheory and MusicStoryteller, not on top of or replacing them:

```
MusicTheory (formal primitives)
    Pitch, Interval, Scale, Chord, Voicing, VoiceLeader, MelodyGenerator
    Used by: MusicStoryteller, MusicComposer, lib-music
        |
        +---> MusicStoryteller (procedural generation)
        |       NarrativeTemplate, EmotionalState, GOAPPlanner
        |       Used by: lib-music (runtime adaptive music)
        |
        +---> MusicComposer (manual authoring aid)  <-- NEW
                StructuralTemplate, CounterpointConstraints,
                CompatibilityChecker, OffsetAnalyzer
                Used by: external composer tooling, lib-music (template registry)
```

**MusicTheory** provides the harmonic building blocks that MusicComposer uses for validation: `Interval.IsConsonant`, `VoiceLeader` violation detection, `Chord` quality analysis.

**MusicStoryteller** provides the emotional/narrative models that MusicComposer's templates parallel: `EmotionalState` (6-dimensional), tension curves, energy profiles. Templates and narrative arcs share the same vocabulary.

**lib-music** becomes the bridge: it stores registered templates, serves them to the procedural system for generating compatible transitions, and exposes template metadata via API.

### 4.2 Existing Capabilities Reused

| Existing Component | How MusicComposer Uses It |
|---|---|
| `Interval.IsConsonant` | Vertical interval checking at combination points |
| `VoiceLeader` violation detection | Parallel fifths/octaves, voice crossing, large leaps across counterpoint pair |
| `EmotionalState` (6D) | Template energy/tension/stability values map directly |
| Tension curve (TIS formula) | Validate composed sections against template tension targets |
| `Scale` / diatonic pitch sets | "Consonant alternatives" suggestions at conflict points |
| `Chord` quality / roman numeral analysis | Template harmonic progression validation |
| Multi-track MIDI-JSON rendering | Output format for composed pieces with counterpoint metadata |

### 4.3 New Components Required

| New Component | Purpose | Depends On |
|---|---|---|
| `StructuralTemplate` | Data model for template definition (Section 1.1) | `EmotionalState`, `Chord`, `Scale` |
| `TemplateParser` | YAML/JSON template loading and validation | `StructuralTemplate` |
| `CounterpointConstraints` | Compatibility tier rules, registral assignments, offset specs | `Interval` |
| `CompatibilityChecker` | Real-time vertical interval analysis between two pieces | `Interval`, `VoiceLeader`, `Chord` |
| `OffsetAnalyzer` | Batch analysis across multiple temporal offsets | `CompatibilityChecker` |
| `TemplateValidator` | Check a composition against its template constraints | `StructuralTemplate`, `EmotionalState` |
| `ConflictSuggester` | Suggest alternative notes at conflict points from chord pitch set | `Chord`, `Scale`, `Interval` |

### 4.4 Integration with lib-music

lib-music gains two new capabilities:

1. **Template Registry**: Store and retrieve structural templates. Composers register templates; the procedural system queries them. Templates are stored in Redis (ephemeral, for active sessions) or MySQL (persistent, for published templates).

2. **Procedural Bridging**: When the procedural system (MusicStoryteller) needs to transition between two composed themes, it can read both themes' templates and generate a transition that is structurally compatible with both -- matching the outgoing theme's energy decay and the incoming theme's energy ramp, staying in compatible harmonic territory.

This gives Arcadia a **composed anchors + procedural connective tissue** model: human-authored themes for important musical moments, procedurally generated transitions and ambient fill between them, with templates as the compatibility layer ensuring seamless musical continuity.

### 4.5 Integration with Arcadia Gameplay

**Regional Theme Counterpoint**
Each game region has a composed theme following a shared template (or compatible templates). As a player moves between regions, both themes play simultaneously during the transition. The counterpoint compatibility ensures the overlap sounds intentional, not chaotic.

**Deity Theme Interlock**
Each god has a leitmotif. During divine encounters (a god manifesting, bestowing a blessing, expressing displeasure), the deity's theme layers over the ambient music. Templates ensure the deity theme harmonizes with any regional theme it might overlay.

**Generational Theme Evolution**
A family's musical identity evolves across generations. The first generation's theme establishes a template. Subsequent generations' themes are composed as counterpoints to their parent's theme -- musically linked, but distinct. Playing them together produces a family "anthem" that grows richer with each generation.

**Character/Spirit Duet**
The dual-agency model (autonomous character + guardian spirit) has a musical analogue. The character's behavioral state drives one musical layer; the spirit's influence drives a counterpoint layer. The two reflect the collaboration between player and character. As progressive agency deepens, the spirit's musical voice becomes more prominent and more tightly interlocked with the character's.

**Content Flywheel Music**
When a character dies and their archive is compressed, their musical template is archived too. A ghost quest spawned from that archive uses a counterpoint to the dead character's theme -- musically linking the new content to the history that generated it. The player hears the echo of a past life in the music of their current adventure.

---

## Part 4B: The Constrained Decision Tree

### 4B.1 Why Templates Collapse the Creative Effort

A composer starting from a blank page faces decisions across every dimension of music simultaneously -- key, tempo, form, chords, contour, rhythm, register, dynamics. The decision space is effectively infinite, which is why composition is hard and slow.

A composer starting from a structural template has most of those decisions **already made**:

| Decision | From Blank Page | From Template |
|---|---|---|
| Key | 24 major/minor keys | Fixed |
| Tempo | 40-200+ BPM | Fixed |
| Time signature | Many options | Fixed |
| Form / structure | Infinite | Fixed |
| Section lengths | Arbitrary | Fixed |
| Chord progression | Enormous harmonic space | Fixed (or constrained to compatible set) |
| Energy curve | Arbitrary | Fixed |
| Harmonic rhythm | Arbitrary | Fixed |
| Register | Full range | Constrained (complementary to other parts) |
| Melodic contour | Infinite | Constrained (contour hints + consonance rules) |
| Specific notes | 12 per octave, any sequence | ~3-5 "good" options per strong beat |
| Rhythmic pattern | Infinite | Constrained (complementary density) |

The composer isn't deciding *what kind of piece to write* -- they're deciding *how to express themselves within a well-defined space*. The infinity of choice is replaced by a navigable set of options where almost every path sounds good, because the constraints encode music theory rules that define what "sounds good" means.

### 4B.2 The Convergent Decision Tree

At any given strong beat, the counterpoint note must be consonant with the template's chord at that point. In a typical diatonic context, that means 3-4 chord tones are "safe" choices, plus 2-3 scale tones that work as passing approaches. That's not "compose a melody" -- that's **pick from a short list**.

And the picks cascade. Once you've chosen your note at beat 1, your options for beat 2 are further constrained by voice leading rules (prefer stepwise motion, avoid large leaps unless followed by contrary step). Beat 1's choice narrows beat 2, which narrows beat 3.

This is a **convergent** decision tree, not a divergent one. Each choice makes subsequent choices *easier*, not harder. The deeper you go, the more the piece "writes itself" because the constraints leave fewer and fewer viable paths -- and crucially, almost all remaining paths sound good.

The decision tree has natural layers of granularity:

1. **Section-level choices** (broadest): "For the verse counterpoint, do you want an arch contour, ascending, or descending?" -- 3 options
2. **Phrase-level choices** (medium): "This 4-bar phrase starts on which chord tone? Root, third, or fifth?" -- 3 options that determine emotional color
3. **Beat-level choices** (finest): "At this climax point, step up to the 5th or leap to the octave?" -- 2-3 options

At each level, the system can **preview** what each choice sounds like -- both alone and overlaid with the original at designed offsets. The composer isn't guessing; they're auditioning curated options where bad choices have already been filtered out.

### 4B.3 The Composition Mini-Game

This constrained decision tree is, functionally, a **mini-game**. A well-designed one, because:

- **Every choice matters**: each decision shapes the character of the piece
- **There are no bad options**: the constraint system ensures all choices produce musically valid results
- **Results are immediately audible**: the composer hears the consequence of each choice
- **Complexity scales with skill**: broad choices for beginners, fine choices for experts
- **The output has real value**: the composed piece becomes a permanent artifact in the game world

**Connection to Progressive Agency**: This maps directly to Arcadia's UX expansion model. The crafting domain in PLAYER-VISION.md describes progressive fidelity -- early spirits make broad choices, experienced spirits make fine choices. Music composition fits naturally:

| Spirit Experience | Composition Fidelity | Choices Available |
|---|---|---|
| New | Section-level only | "Pick the mood for each section" (3-5 choices per section) |
| Developing | Phrase-level | "Shape each phrase's opening and climax" (3 choices per phrase) |
| Experienced | Beat-level | "Select individual notes at key moments" (2-4 choices per beat) |
| Master | Full melodic authoring | Note-by-note composition within constraints (full decision tree) |

At every fidelity level, the output sounds good. The difference is how much personal expression the spirit can inject. A new spirit's composition sounds pleasant and generic; a master spirit's composition sounds distinctive and intentional. Same system, same constraints, different depth of engagement.

### 4B.4 The Combinatorial Web

Templates don't just enable one counterpoint -- they enable a **family** of interchangeable parts.

Given an original piece A composed against Template T:

```
Template T (structural skeleton)
    |
    +---> Piece A  (original, hand-authored)
    +---> Piece B  (counterpoint to A, composed via decision tree)
    +---> Piece B' (different counterpoint to A, different choices)
    +---> Piece B" (yet another counterpoint, different character)
```

Since B, B', and B" all satisfy the same template constraints, they are all compatible with A. But they're also potentially compatible with **each other** (same key, same form, same harmonic rhythm). The system can verify this.

Now extend: compose A' as a counterpoint to B (using B's derived template T'):

```
Template T ←──────────────────── Template T'
    |                                |
    +---> Piece A  ←──duet──→ Piece B
    +---> Piece A' ←──duet──→ Piece B   (A' composed against T')
    +---> Piece A  ←──duet──→ Piece B'
    +---> Piece A' ←──duet──→ Piece B'  (cross-compatible)
```

Author 3 A-variants and 3 B-variants: **9 valid duet combinations**, plus 6 standalone tracks. 15 distinct musical experiences from 6 compositions, all guaranteed harmonically compatible because they share structural DNA.

This is the content flywheel applied to music: a small number of authored seeds produces a combinatorial explosion of valid musical experiences.

---

## Part 4C: Role-Based Ensemble Composition

### 4C.1 Beyond Duets: The Ensemble Template

The duet model (two counterpoint positions) generalizes naturally to **N positions**. A template can define not just the structural skeleton but a set of **ensemble roles** -- named slots with distinct constraints that together form a complete arrangement.

```yaml
template:
  name: "tavern-ballad-01"
  # ... (key, tempo, form, harmony as before) ...

  ensemble_roles:
    - role: "melody"
      register: { low: "C4", high: "C6" }
      density: 0.7
      contour_freedom: "high"       # most melodic liberty
      rhythmic_character: "lyrical"  # sustained, flowing
      doubling: false                # only one melody at a time

    - role: "bass_line"
      register: { low: "C2", high: "C4" }
      density: 0.4
      contour_freedom: "low"         # root-heavy, foundational
      rhythmic_character: "steady"   # drives the pulse
      doubling: false

    - role: "harmony_pad"
      register: { low: "G3", high: "G5" }
      density: 0.3
      contour_freedom: "minimal"     # sustained chords, slow movement
      rhythmic_character: "sustained"
      doubling: true                 # multiple harmony instruments welcome

    - role: "rhythmic_counterpoint"
      register: { low: "C3", high: "C5" }
      density: 0.8
      contour_freedom: "medium"      # syncopated fills
      rhythmic_character: "syncopated"
      doubling: true

    - role: "ornamental_fill"
      register: { low: "C5", high: "C7" }
      density: 0.2
      contour_freedom: "high"        # decorative, sparse
      rhythmic_character: "sparse"   # fills gaps between phrases
      doubling: true
```

Each role is a different set of constraints applied to the same structural skeleton. A composer (or a player in the mini-game) **picks a role first**, then composes within that role's constraints. The role selection determines the character of their contribution -- bass is foundational and steady, ornamental fill is decorative and sparse, melody is expressive and free.

### 4C.2 Self-Organizing Ensemble

The ensemble model requires no external conductor or synchronization mechanism. The template IS the synchronization:

1. **Shared tempo**: all roles play at the same BPM (from the template)
2. **Shared form**: all roles follow the same section structure (verse, chorus, bridge at the same bar numbers)
3. **Shared harmonic rhythm**: all roles change chords at the same points
4. **Shared key**: all notes are diatonic to the same scale (with chromatic exceptions validated by the constraint system)

When multiple actors (NPC bards, player-influenced characters) want to play together, they:

1. Check which roles are currently being performed at this location
2. Pick an unfilled role (or a role that allows doubling)
3. Start playing their independently-composed track for that role
4. The shared structural DNA ensures automatic harmonic coherence

This is how a **jazz combo** works in real life: everyone knows the chart (the template), everyone plays their role (bass walks, piano comps, sax solos), nobody needs a conductor. The chart IS the synchronization. The Counterpoint Composer SDK formalizes this into a system that guarantees the result sounds good even when the performers have never rehearsed together.

### 4C.3 Emergent Ensemble in Arcadia

Picture a tavern scene in Arcadia:

- A **lutist NPC** has composed a melody track (section-level choices made by the NPC's personality traits via GOAP)
- A **drummer NPC** has composed a rhythmic counterpoint track (different personality, different choices, same template)
- A **player's bard character** enters the tavern and decides to join in
- The player, through the composition mini-game, composes a bass line for the same template
- The player's bard starts playing. The three tracks interlock automatically.
- A fourth NPC picks up a flute and joins on the ornamental fill role
- The music grows richer with each additional voice, and every combination was never explicitly authored by a developer

**No two tavern performances are alike.** Different NPCs make different compositional choices (driven by their personality traits). Different players make different choices in the mini-game. The same template produces a different ensemble every time, but every combination is guaranteed to harmonize.

This is "Emergent Over Authored" (North Star #5) applied directly to music: the developers author the template (the structural skeleton and the role definitions). The NPCs and players author the specific parts. The combinations are emergent. The content flywheel spins.

### 4C.4 Role Compatibility Matrix

Not all roles need to be checked against all other roles with equal strictness:

| | Melody | Bass | Harmony | Rhythmic | Ornamental |
|---|---|---|---|---|---|
| **Melody** | -- | Full duet | Harmonic CP | Structural echo | Structural echo |
| **Bass** | Full duet | -- | Harmonic CP | Harmonic CP | Structural echo |
| **Harmony** | Harmonic CP | Harmonic CP | -- | Structural echo | Structural echo |
| **Rhythmic** | Structural echo | Harmonic CP | Structural echo | -- | Structural echo |
| **Ornamental** | Structural echo | Structural echo | Structural echo | Structural echo | -- |

The melody and bass line need the tightest counterpoint compatibility (they define the harmonic framework). Harmony needs to align with both. Rhythmic and ornamental roles have more freedom because they fill in the texture rather than defining the structure. This mirrors how orchestration works in practice -- the outer voices (soprano and bass) carry the harmonic weight; inner voices have more latitude.

---

## Part 5: Legal Framework

### 5.1 Summary of Legal Position

The concept has been thoroughly researched against current copyright case law (as of early 2026). The conclusion: **the structural template approach is on strong legal ground**.

### 5.2 Uncopyrightable Elements (Safe to Extract and Reuse)

| Element | Legal Basis | Case Law |
|---|---|---|
| Chord progressions | Fundamental building blocks; public domain | *Gray v. Hudson* (9th Cir. 2022), *Skidmore v. Led Zeppelin* (9th Cir. 2020), *Ed Sheeran/Structured Housing* (2nd Cir. 2024-2025) |
| Song form (ABABCB, etc.) | Scenes a faire doctrine; genre convention | No case has ever found song form copyrightable |
| Key signature | Basic musical parameter, not expression | Universal consensus |
| Tempo / BPM | Functional parameter, not expression | Universal consensus |
| Time signature | Fundamental building block | Universal consensus |
| Section lengths | Structural convention | No case law against; clearly conventional |
| Dynamic contour (energy arc) | Abstract structural convention | No case law directly; clearly a convention |
| Harmonic rhythm | "Commonplace element" | *Ed Sheeran/Structured Housing* |

### 5.3 Copyrightable Elements (Never Extracted)

| Element | Status |
|---|---|
| Melody (specific pitch sequences with rhythm) | Core of musical copyright |
| Lyrics | Protected as literary works |
| Specific arrangement details (riffs, hooks, voicings) | Protected as original expression |
| Sound recording (the performance) | Separate copyright from composition |
| Vocal performance characteristics | Right of publicity protections |

### 5.4 The Critical Legal Firewall

The SDK maintains a strict separation:

```
Song A (copyrighted) --[analysis]--> Template (uncopyrightable)
                                         |
                              [human creative decisions]
                                         |
                                     Song B (new copyright, owned by composer)
```

The template is the firewall. It contains only parameters that are confirmed uncopyrightable by multiple appellate courts. The human composer provides the creative expression (melody, rhythm, arrangement) that makes Song B an original work.

**Key protections:**

1. **Templates are anonymous.** The template data model contains no reference to any source song. A template derived from analyzing "Demons" is stored as `power-ballad-arc-01`, not as `demons-template`.

2. **Templates are indistinguishable by origin.** A hand-authored template and an analyzed template have identical structure. No metadata links a template to its source.

3. **The human creative gap is mandatory.** The SDK does not generate finished music. Every melodic, rhythmic, and arrangement decision is made by a human composer. The causal chain from source song to output is broken by human authorship.

4. **Distribution is standalone.** Composed pieces are distributed as independent works. They are never bundled with or marketed in reference to any source song. The counterpoint compatibility is a property users may discover, not a marketed feature tied to specific copyrighted works.

### 5.5 The Blurred Lines Caution

The *Williams v. Gaye* (9th Cir. 2018) "Blurred Lines" verdict extended infringement analysis to the subjective "total concept and feel" of a work. This is the one area of elevated (though still low) risk.

**Mitigation:** The "feel" of a song comes from melody, rhythm, instrumentation, vocal performance, and production aesthetics -- all elements that the composer provides independently. The structural template captures none of these. A piece composed within the same template as "Demons" will not "sound like" Demons unless the composer deliberately mimics its melodic and rhythmic style, which is a human choice outside the SDK's scope.

**Further narrowing:** Subsequent rulings (*Dark Horse* 2022, *Ed Sheeran* 2024-2025, Second Circuit affirmance 2025) have significantly narrowed the Blurred Lines precedent, explicitly ruling that shared structural elements do not constitute infringement even when songs "feel" similar.

### 5.6 Comparison to Established Practices

The SDK enables a practice that has been legally accepted for decades:

| Established Practice | What It Does | Legal Status |
|---|---|---|
| Jazz contrafacts | New melody over existing chord progression | Universally accepted; standard practice since 1940s |
| Soundalike industry | New composition evoking the mood of an existing song | Established commercial practice with decades of precedent |
| "In the style of" composition | Following genre structural conventions | Not infringement per ABA 2025 commentary |
| Music theory education | "Compose in C major, I-V-vi-IV, ABABCB form" | Unquestionably legal |

The Counterpoint Composer SDK automates the analysis step of what composers have always done manually: study a piece's structure, internalize its architecture, then compose something new within that framework.

---

## Part 6: Implementation Phases

### Phase 1: Template Data Model and Validation (Foundation)

**New code in MusicTheory SDK:**
- `StructuralTemplate` record type with sections, harmony, contour hints, curves
- `TemplateSection` with energy, tension, stability, harmonic progression
- `ContourHint` with shape, range, step-leap ratio, climax position
- `TemplateParser` for YAML/JSON loading
- `TemplateValidator` checking a MIDI-JSON composition against template constraints

**Estimated scope:** ~800-1200 lines of new code. Uses existing `EmotionalState`, `Chord`, `Scale`, `Interval` extensively.

**Deliverable:** A composer can define a template, import a MIDI composition, and get a report of template compliance (sections that match, sections that deviate).

### Phase 2: Counterpoint Compatibility Checking

**New code in MusicComposer SDK:**
- `CounterpointConstraints` defining compatibility tier, registral assignments, offset specs
- `CompatibilityChecker` performing vertical interval analysis between two MIDI-JSON pieces
- `OffsetAnalyzer` running compatibility checks across multiple temporal offsets
- `ConflictSuggester` offering alternative notes from the current chord's pitch set

**Estimated scope:** ~1500-2000 lines of new code. Heavy use of `Interval.IsConsonant`, `VoiceLeader` violation detection.

**Deliverable:** A composer can check two pieces for counterpoint compatibility at specified offsets and get a detailed report: consonant bars (green), acceptable tension (yellow), problematic clashes (red), with specific note suggestions at conflict points.

### Phase 3: Source Analysis Tool (Convenience)

**New code in MusicComposer SDK:**
- `StructuralAnalyzer` extracting template parameters from MIDI-JSON input
- Chord progression extraction (using existing `Chord` recognition)
- Section boundary detection (energy/density changes)
- Contour extraction (Parsons code level -- direction only, no specific pitches)
- Tension curve computation (using existing TIS formula)

**Estimated scope:** ~1000-1500 lines of new code. This is the "analyze an existing piece" convenience feature.

**Deliverable:** A composer can import a MIDI-JSON representation of an existing piece and extract a structural template automatically. The template can then be edited, renamed, and used for composing counterpart pieces.

**Note:** This phase works with MIDI-JSON input (symbolic representation), not audio. Audio-based analysis (applying MIR techniques to recordings) is a potential future phase but is significantly more complex and not required for the core use case.

### Phase 4: lib-music Integration

**Changes to lib-music plugin:**
- Template registry endpoints (CRUD for structural templates)
- Template storage in state stores (Redis for active, MySQL for published)
- Procedural bridging: MusicStoryteller reads templates to generate compatible transitions
- Template metadata in composition responses (which template a procedural piece follows)

**Estimated scope:** ~500-800 lines of new code in lib-music, plus schema changes for template registry endpoints.

**Deliverable:** Templates are first-class entities in the Bannou service ecosystem. The procedural music system can generate transitions that are structurally compatible with composed themes.

---

## Part 7: Data Model Specification

### 7.1 StructuralTemplate

```
StructuralTemplate
  Name: string                          # Human-readable identifier
  Key: Key                              # Tonal center (tonic + mode)
  Tempo: int                            # BPM
  TimeSignature: TimeSignature          # Meter
  TotalBars: int                        # Computed from sections
  Sections: TemplateSection[]           # Ordered section definitions
  TensionCurve: float[]                 # Normalized per-section tension
  EnergyCurve: float[]                  # Normalized per-section energy
```

### 7.2 TemplateSection

```
TemplateSection
  Name: string                          # "Verse", "Chorus", "Bridge", etc.
  Bars: int                             # Section length
  Energy: float                         # 0.0-1.0
  Tension: float                        # 0.0-1.0
  Stability: float                      # 0.0-1.0
  Brightness: float                     # 0.0-1.0
  Warmth: float                         # 0.0-1.0
  Valence: float                        # 0.0-1.0
  Harmony: HarmonicProfile?             # Optional harmonic specification
  ContourHint: ContourHint?             # Optional melodic shape hint
```

### 7.3 HarmonicProfile

```
HarmonicProfile
  Progression: RomanNumeral[]           # Chord functions (e.g., [i, III, VII, VI])
  HarmonicRhythm: HarmonicRhythm       # Rate of chord change
  Cadence: CadenceType                  # How the section ends
```

### 7.4 ContourHint

```
ContourHint
  Shape: ContourShape                   # Arch, Ascending, Descending, Bowl, Balanced
  RangeSemitones: int                   # Total melodic range in section
  StepLeapRatio: float                  # 0.0-1.0 (proportion of stepwise motion)
  ClimaxPosition: float                 # 0.0-1.0 (where the highest note falls)
  Density: float                        # 0.0-1.0 (note onset density)
  Syncopation: float                    # 0.0-1.0 (off-beat accent proportion)
```

### 7.5 CounterpointConstraints

```
CounterpointConstraints
  CompatibilityTier: CompatibilityTier  # StructuralEcho | HarmonicCounterpoint | FullDuet
  DesignedOffsets: OffsetSpec[]          # Temporal offsets to validate
  RegisterA: PitchRange                 # Registral assignment for piece A
  RegisterB: PitchRange                 # Registral assignment for piece B
  MotionPreference: MotionType          # Contrary | Oblique | Similar | Any
  RhythmicComplementarity: float        # 0.0-1.0 (target non-simultaneous onset ratio)
  StrongBeatRule: IntervalRule          # ConsonantOnly | AllowPerfect | AllowAll
  WeakBeatRule: IntervalRule            # PassingDissonanceAllowed | ConsonantOnly | AllowAll
```

### 7.6 CompatibilityReport

```
CompatibilityReport
  OverallScore: float                   # 0.0-1.0 aggregate compatibility
  PerOffset: OffsetReport[]             # Detailed report per designed offset
    Offset: OffsetSpec
    PerBar: BarReport[]
      BarNumber: int
      Status: Green | Yellow | Red
      StrongBeatIntervals: Interval[]   # Vertical intervals at strong beats
      Violations: Violation[]           # Parallel fifths, voice crossing, etc.
      Suggestions: NoteSuggestion[]     # Alternative notes at conflict points
```

---

## Part 8: Open Questions

### Q1: Template Sharing and Community
Should templates be shareable between composers? A library of "genre structural templates" could accelerate composition. This raises questions about template curation, quality, and whether templates themselves become a creative artifact worth protecting (despite containing no copyrightable material individually).

### Q2: Real-Time Playback with Offset Preview
Should the workbench include playback that lets the composer hear their piece simultaneously with a reference piece at various offsets? This would be the most powerful validation tool, but it means the workbench needs audio rendering capability (or at minimum MIDI playback).

### Q3: Template Versioning
If a composer modifies a template after other pieces have been composed against it, what happens? The compatibility guarantees of existing pieces may break. Should templates be immutable once published, with modifications creating new template versions?

### Q4: Audio-Based Analysis (Future Phase)
Phase 3 works with MIDI-JSON (symbolic) input. Extracting templates from audio recordings requires MIR techniques (chroma features, onset detection, beat tracking, structural segmentation). This is a significant engineering effort and may warrant a separate SDK or tool rather than being embedded in MusicComposer. Deferred for now.

### Q5: Procedural Counterpoint Generation
While the SDK is designed as a composer's workbench (human-authored output), the existing GOAP planner in MusicStoryteller could theoretically generate counterpoint automatically using the same constraints. This would be useful for runtime procedural counterpoint (e.g., dynamically generating a deity's counter-theme during gameplay). However, the legal and creative considerations differ for automated generation vs. human-assisted composition. This capability could be added later as a separate mode with appropriate safeguards.

---

## Appendix A: Music Theory Glossary

| Term | Definition |
|---|---|
| **Cantus firmus** | The "fixed melody" against which counterpoint is written |
| **Species counterpoint** | Fux's five graduated exercises for learning counterpoint |
| **Invertible counterpoint** | Counterpoint designed to work with voices swapped |
| **Quodlibet** | Composition combining multiple pre-existing melodies |
| **Contrafact** | New melody over existing chord progression |
| **Counterpoint songs** | Two standalone songs deliberately composed to combine when sung simultaneously (Broadway tradition: "Lida Rose"/"Will I Ever Tell You", "Play a Simple Melody"/"You're Just in Love") |
| **Phase music** | Identical material played at slightly different speeds, producing emergent patterns |
| **Consonance** | Intervals perceived as stable/resolved (unisons, thirds, fifths, sixths, octaves) |
| **Dissonance** | Intervals perceived as unstable/requiring resolution (seconds, sevenths, tritones) |
| **Voice leading** | The smooth connection of pitches between successive chords |
| **Parallel fifths/octaves** | Two voices moving in the same direction by a perfect interval -- forbidden in classical counterpoint because it destroys voice independence |
| **Contrary motion** | Voices moving in opposite directions -- preferred in counterpoint for maximum independence |
| **Harmonic rhythm** | The rate at which chords change |
| **Cadence** | A chord progression that marks the end of a phrase (authentic, half, deceptive, plagal) |
| **Scenes a faire** | Legal doctrine: elements naturally associated with a genre are not copyrightable |
| **Parsons code** | Melodic contour reduced to three symbols: Up, Down, Repeat |
| **Ensemble role** | A named slot in a template with distinct constraints (register, density, rhythmic character) that defines one voice in a multi-part arrangement |
| **Convergent decision tree** | A decision structure where each choice narrows subsequent options, making the process easier as it progresses |
| **Combinatorial web** | A family of interchangeable compositions sharing structural DNA, where any combination of compatible parts produces a valid ensemble |

## Appendix B: Research Sources

### Legal Research
- *Gray v. Hudson* (Katy Perry "Dark Horse," 9th Cir. 2022) -- chord progressions uncopyrightable
- *Skidmore v. Led Zeppelin* ("Stairway to Heaven," 9th Cir. 2020) -- descending chromatic line uncopyrightable
- *Structured Housing v. Ed Sheeran* ("Thinking Out Loud," 2nd Cir. 2024-2025) -- chord progressions are "commonplace elements"
- *Williams v. Gaye* ("Blurred Lines," 9th Cir. 2018) -- "total concept and feel" test (narrowed by subsequent rulings)
- U.S. Copyright Office guidance on AI-generated works (January 2025)
- American Bar Association commentary on AI music and genre copyright (2025)

### Music Theory Research
- Fux, J.J. *Gradus ad Parnassum* (1725) -- species counterpoint framework
- Lerdahl, F. & Jackendoff, R. *A Generative Theory of Tonal Music* (1983) -- structural analysis theory
- Navarro et al. (2020) -- Tonal Tension Index formula (used in existing MusicTheory SDK)
- Reich, S. "Piano Phase" (1967) -- temporal offset compatibility demonstration

### Counterpoint Composition Examples
- Bestor, K. & Cardon, S. "The Curse" / "The Melody Within" from *Rigoletto* (1993 film) -- high-independence counterpoint; two songs that sound completely different independently but combine in the finale reprise (Track 17: "Reprise - The Melody Within/The Curse"). Primary inspiration for the SDK's target: revelatory combination of independent pieces.
- Willson, M. "Lida Rose" / "Will I Ever Tell You" from *The Music Man* (1957) -- the canonical Broadway counterpoint song pair
- Berlin, I. "Play a Simple Melody" / "You're Just in Love" -- Irving Berlin's counterpoint song pairs, each standalone, combining into duets
- Schonberg, C.M. "One Day More" from *Les Miserables* (1985) -- 8-9 simultaneous vocal parts reprising earlier songs, planned from composition to combine
- Verdi, G. "Bella figlia dell'amore" quartet from *Rigoletto* (1851 opera) -- four completely different emotional vocal lines (flirtation, mockery, heartbreak, vengeance) harmonizing simultaneously; one of the greatest ensemble pieces in opera
- Sondheim, S. "A Weekend in the Country" from *A Little Night Music* -- intricate ensemble counterpoint with preserved individual melodic identity

### Computational Music Research
- Strasheela constraint-based composition system
- Optimuse variable neighbourhood search for counterpoint
- music21 computational musicology toolkit (Python, BSD)
- Audio-based structural segmentation with gammatone features (ISMIR 2016)
- Harmonic Change Detection Function for harmonic rhythm extraction
