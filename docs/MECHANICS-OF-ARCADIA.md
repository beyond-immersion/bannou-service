# The Mechanics of Arcadia
## How Bannou Brings the Vision to Life

> **Purpose**: The definitive standalone guide explaining *how* Arcadia works — how the 78 Bannou service plugins and the family of pure-computation creative SDKs compose into the living world(s) that [VISION.md](reference/VISION.md) and [PLAYER-VISION.md](reference/PLAYER-VISION.md) describe. This is the bridge between the "what and why" (vision documents) and the "how to build it" (plugin deep dives, implementation maps, tenets).
>
> **Status**: Living document. Aspirational systems are marked at section-level; see Appendix A for per-plugin status.
>
> **Companion documents**: [VISION.md](reference/VISION.md) (strategic vision), [PLAYER-VISION.md](reference/PLAYER-VISION.md) (player experience), [BANNOU-DESIGN.md](BANNOU-DESIGN.md) (technical architecture), [BANNOU-DEEP-DIVE.md](BANNOU-DEEP-DIVE.md) (infrastructure inventory), [TENETS.md](reference/TENETS.md) (mandatory constraints).

---

## Table of Contents

### PART I — FOUNDATIONS

- [§1. Introduction & How to Read This Document](#1-introduction--how-to-read-this-document)
- [§2. The Metaphysical Substrate & The Knowledge-as-Power Thesis](#2-the-metaphysical-substrate--the-knowledge-as-power-thesis)
- [§3. The Realm Archetype System](#3-the-realm-archetype-system)

### PART II — THE LIVING WORLD

- [§4. The Guardian Spirit & Progressive Agency](#4-the-guardian-spirit--progressive-agency)
- [§5. The Unified Cognitive Progression & System Realms](#5-the-unified-cognitive-progression--system-realms)
- [§6. The NPC Intelligence Stack](#6-the-npc-intelligence-stack)
- [§7. The Content Flywheel](#7-the-content-flywheel)
- [§8. Life, Death, and Generational Cycles](#8-life-death-and-generational-cycles)
- [§9. The Economic Substrate](#9-the-economic-substrate)
- [§10. The Social Fabric & Cultural Emergence](#10-the-social-fabric--cultural-emergence)

### PART III — EMERGENT SYSTEMS

- [§11. Combat & Cinematic Choreography](#11-combat--cinematic-choreography)
- [§12. Procedural Creative Generation](#12-procedural-creative-generation)
- [§13. Actor-Bound Entities: Living Places and Things](#13-actor-bound-entities-living-places-and-things)
- [§14. Equipment, Enchantment & the Unified Theory of Arcadian Technology](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology)

### PART IV — REALM DESIGN GUIDES

- [§15. Designing FANTASIA Realms](#15-designing-fantasia-realms)
- [§16. Designing ARCADIA Realms](#16-designing-arcadia-realms)
- [§17. Designing OMEGA & Meta-Realms](#17-designing-omega--meta-realms)
- [§18. Designing Underworld & Afterlife Realms](#18-designing-underworld--afterlife-realms)

### PART V — PLATFORM

- [§19. The Extension Pattern (L5)](#19-the-extension-pattern-l5)
- [§20. Deployment, Engine Integration, and Scale](#20-deployment-engine-integration-and-scale)
- [§21. The Developer Experience](#21-the-developer-experience)

### APPENDICES

- [Appendix A: Plugin Role Index (by function)](#appendix-a-plugin-role-index-by-function)
- [Appendix B: SDK Reference (by domain)](#appendix-b-sdk-reference-by-domain)

---

# PART I — FOUNDATIONS

# 1. Introduction & How to Read This Document

## 1.1 Purpose

Three documents already answer closely related questions about Arcadia and Bannou:

| Document | Question Answered |
|---|---|
| [VISION.md](reference/VISION.md) | *What are we building, and why?* |
| [PLAYER-VISION.md](reference/PLAYER-VISION.md) | *What does the player actually experience?* |
| [BANNOU-DESIGN.md](BANNOU-DESIGN.md) | *What is the technical architecture?* |

This document answers the question those three leave open: **How does the architecture produce the vision?**

**Bannou** is the composable game framework — 78 service plugins (plus a planned 79th for push notifications), a family of pure-computation creative SDKs, and the infrastructure libs that make all of them portable across deployment modes. **Arcadia** is the flagship game Beyond Immersion is building on Bannou: a single persistent universe spanning many coexisting realms, with autonomous NPCs, emergent content, and progressive player agency producing realms distinct enough to feel like fundamentally different games stacked into one product. *How exactly does the framework produce the game?* Which plugins carry which weight? Which compositions produce which emergent behaviors? When a FANTASIA realm feels different from an ARCADIA realm, what configuration choices make that difference? When the content flywheel spins, which components are actually turning?

The Mechanics of Arcadia answers those questions comprehensively, in one place, without requiring the reader to chase dozens of cross-references across plugin deep dives, implementation maps, and planning documents. Where those documents provide primary-source precision, this document provides the coherent through-line that connects them.

### 1.1.1 The Five North Stars

Every section of this document ultimately serves one or more of five strategic goals articulated in [VISION.md](reference/VISION.md). Stating them up front prevents later sections from being read in isolation:

1. **Living Game Worlds** — the world is alive whether a player is watching or not. Autonomous NPCs, regional watcher gods, generational cycles, economies that run offline. Developed across §§4–10 and §13.
2. **The Content Flywheel** — play history becomes the raw material for future content. Character archives feed narrative generation; a server running five years has roughly 500× the content of one running one year. Developed in §7.
3. **100,000+ Concurrent AI NPCs** — not a nice-to-have but an architectural requirement. The NPC intelligence stack is designed specifically for this scale; if it does not work at 100K, it does not work. Developed in §6.
4. **Ship Games Fast** — Beyond Immersion's capability to iterate on Arcadia and future titles without re-engineering backend systems per project. Schema-first development, 78 integrated service plugins, auto-generated engine SDKs. Developed in §§20 and 21.
5. **Emergent Over Authored** — the design principle behind every system in Arcadia. Quests, economies, social dynamics, combat choreography, music, narrative arcs, cinematic composition, and dungeon layouts all emerge from autonomous systems interacting. Not scripted. Developed across §§7, 11, 12, and throughout the document.

If a mechanism developed in this document does not trace back to at least one of these goals, its inclusion is questionable. Every mechanism developed in the following sections does.

## 1.2 Audience

This document is written for three overlapping audiences:

- **Bannou developers** — including the solo developer and AI agents — who need to understand how platform capabilities compose into game behaviors before writing code or making architectural choices. When a new planning question arises ("should this be a new plugin or a composition of existing ones?"), this document offers the frame to think about it.

- **Game designers** working on Arcadia or any game built on Bannou, who need a coherent mental model of what the platform makes possible, what it encourages, and what variations exist within each realm archetype. When designing a specific FANTASIA realm, this document provides both the design vocabulary and the precedent analysis to make the decisions well.

- **Thoughtful readers** — writers, worldbuilders, players interested in understanding Arcadia at depth — who want the underlying coherence of the design without needing engineering background. The metaphysical substrate, the content flywheel, and the realm archetypes are explained in terms that do not require prior Bannou exposure.

The document aims to serve all three. Where technical precision matters (interface names, plugin layers, state-store key patterns), the prose makes those explicit without hiding them behind abstraction. Where conceptual coherence matters (the logos thesis, progressive agency, cultural emergence), the prose leads with the thesis and keeps the service mechanics in support.

## 1.3 Scope

**This document is:**

- A comprehensive explanation of how Bannou plugins and SDKs compose into the Arcadia vision.
- A reference catalogue of which service contributes to which aspect of the game world.
- A design guide for creating individual realms within each archetype (FANTASIA, ARCADIA, OMEGA, Underworld).
- A philosophical anchor explaining the metaphysical substrate (logos / pneuma / manifest reality) that grounds every mechanical choice.

**This document is not:**

- A technical specification. Those are the [plugin deep dives](plugins/) and [implementation maps](maps/).
- A statement of law. That is [TENETS.md](reference/TENETS.md).
- A strategic vision statement. That is [VISION.md](reference/VISION.md).
- An up-to-the-minute implementation status report. That is [planning/VISION-PROGRESS.md](planning/VISION-PROGRESS.md).

When those documents conflict with this one, they win. This is the explanatory bridge, not the authoritative source. When you find a conflict, the correct response is to (a) trust the primary document, (b) fix the bridge here.

## 1.4 Key Terminology

Several terms carry heightened precision throughout the document. Reading them carefully now prevents confusion later.

### Bannou, Beyond Immersion, Arcadia, and ARCADIA

These four names denote very different things and are easy to conflate:

- **Bannou** — the composable game framework. 78 service plugins (plus a planned 79th push-notifications plugin), a family of pure-computation creative SDKs, and the infrastructure libs that make all of them portable across embedded, sidecar, dedicated, and multi-node deployments. Bannou is *what the framework is*.
- **Beyond Immersion** — the developer and publisher that builds games on Bannou. Also the name of the companion app (Beyond Immersion App, reachable at `app.beyond-immersion.com` as a web application and on mobile as a native app) through which players manage their Arcadia accounts, access in-world configuration surfaces, and interact with companion features (push notifications, mini-games that affect in-world state, compendiums, message boards).
- **Arcadia** (the real-world product) — Beyond Immersion's flagship game. A single persistent universe spanning many coexisting realms of many archetypes. "Arcadia" here is the name on the box.
- **Arcadia** (in-universe) — the name of the VR-machine meta-game that OMEGA-realm characters play. The fourth-wall coincidence is deliberate: the in-universe Arcadia hardware manufacturer is also named Beyond Immersion, and the in-universe Beyond Immersion App exists with the same layout and options as the real-world companion. The real-world player's phone and the OMEGA character's in-world phone show the same app. This is the intended effect, not an accident (see §17).
- **ARCADIA** (all-caps) — a *realm archetype* in the game. One of four role keywords (FANTASIA / **ARCADIA** / OMEGA / UNDERWORLD) denoting a category of realm where the gods of magic are largely absent, technology dominates over direct spellcasting, and mana still powers everything. Electricity may appear in end-results but electrical engineering does not exist as a discipline; power mechanisms run on mana. The aesthetic resembles Final Fantasy 7's mana-powered industrial world rather than traditional high-fantasy tropes — survival-builder and production-oriented realms rather than adventure-and-magic realms. Developed in §3.4.2 and §16.

Where this document uses capitalization to disambiguate: **ARCADIA** (all-caps) is always the archetype. **Arcadia** (proper case) without modifier is the real-world game (and sometimes the in-universe meta-game when context makes that clear). **Bannou** is always the framework. **Beyond Immersion** is always the developer/publisher.

### Realm archetype vs. specific realm

**FANTASIA, ARCADIA, OMEGA, and UNDERWORLD are role keywords, not realm names.** Each keyword denotes a *category* of realm with characteristic design traits. A single Arcadia deployment may host many realms within each archetype, each with different god compositions, resource profiles, UX defaults, and magic/tech configurations — and every one of them is equally canonical.

- *A FANTASIA realm* could be a pre-Bronze-Age survival world, a wuxia cultivation world, a steampunk-enchantment world, or a dark necromancy world. All are FANTASIA.
- *An ARCADIA realm* could be a Victorian-industrial city, a wild-west resource frontier, or a mercantile city-state. All are ARCADIA.
- *An OMEGA realm* is any realm organized as a cyberpunk meta-dashboard over other realms' play.
- *An UNDERWORLD realm* is any realm inhabited post-mortem.

Where this document says "FANTASIA realms," it means *realms of the FANTASIA archetype* — generalizing across all possible instances. Where it names a specific realm, the name is a worked example, not a fixed canon.

This terminology decision is important because it reframes Bannou's "one binary, many realms" thesis at the design layer. The platform supports many realms architecturally; the archetype system captures the fact that those realms are not indistinguishable — they fall into distinct families of player experience, each with its own design axes.

### Logos vs. pneuma vs. manifest reality

These are the three layers of Arcadia's metaphysical substrate:

- **Logos** — pure information particles. The "words" that define what things are. Identifying a thing's True Name grants some influence over it.
- **Pneuma** — organized logos with volatile / emergent properties. The spiritual matter of the world. Active pneuma is magical energy; spent pneuma becomes physical matter.
- **Manifest reality** — the physical world that results when pneuma transitions from active to spent state.

[§2](#2-the-metaphysical-substrate--the-knowledge-as-power-thesis) develops this in depth. The short version for now: every magical operation in Arcadia is logos-assisted pneuma manipulation subject to thermodynamic conservation. Magic does not create from nothing; it redirects.

### Lexicon (plugin) vs. lexicon (canonical catalogue)

The **Lexicon plugin** (`lib-lexicon`, L4, planned) is the service that stores, validates, and serves concept-tuple ontologies for NPC communication, Hearsay propagation, and Disposition motivation.

The **canonical lexicon** (lowercase) is the conceptual *entity* — the accumulating catalogue of known logos primitives and validated combinations that defines what a civilization can think, say, build, or enchant.

[§2](#2-the-metaphysical-substrate--the-knowledge-as-power-thesis) argues that these are not merely related — they are the same thing at two altitudes, and this identity is load-bearing for Arcadia's design. The Lexicon plugin is the computational instantiation of the canonical lexicon.

### Spirit vs. character

The **guardian spirit** is the player. The spirit is a divine shard of Nexius (goddess of connections), bound to an account, persistent across characters, lifetimes, and realms. It accumulates context and capability across all play.

A **character** is a resident of a realm — a world citizen with personality, relationships, history, and autonomous behavior. Characters are *inhabited* by guardian spirits (collaboration, not possession) or run autonomously as NPCs. A character has its own lifecycle: birth, growth, marriage, death. A spirit persists regardless of any one character.

**Players do not control characters.** Characters are always autonomous. The guardian spirit learns, over time, to *collaborate with* the character's autonomy — starting from barely-visible influence (watching the character live their life) and progressing to rich directed collaboration (combat choreography, crafting technique, social negotiation). This dual-agency relationship is the foundational gameplay primitive of Arcadia. It is not a gimmick or a thematic flavor; it is the reason most of the NPC intelligence stack exists.

### Seed (plugin) vs. account seed

Two unrelated uses of the word "seed" appear throughout Bannou:

- The **Seed plugin** (`lib-seed`, L2) is the universal progressive growth primitive — skills, masteries, divinity domains, dungeon growth, everything that accumulates capability through experience.
- An **account seed** is one of up to three concurrent relationships a guardian spirit maintains with the game world (standard guardian, dungeon master, realm-specific variant, etc.).

Where this document needs to disambiguate, it uses "Seed plugin" or "account seed" explicitly. Unqualified "seed" almost always refers to the former.

### Archetype vs. instance (template vs. occurrence)

A recurring pattern in Arcadia: everything that can exist as both an abstract definition and a concrete occurrence is modelled with both layers explicitly. Item templates vs. item instances. Quest templates vs. active quests. Scenario definitions vs. instantiated scenarios. God archetypes vs. specific deities. Character templates (for NPC generation) vs. character instances. This duality is important enough that it is discussed explicitly in [§§5](#5-the-unified-cognitive-progression--system-realms), [8](#8-life-death-and-generational-cycles), [13](#13-actor-bound-entities-living-places-and-things), and [15-18](#part-iv--realm-design-guides).

Note: "archetype" has three distinct senses in this document, and context must disambiguate:

1. **Template/definition archetype** (this subsection) — the abstract definition layer that instances are created from.
2. **Realm archetype** (§3, §§15-18) — one of the four role keywords (FANTASIA / ARCADIA / OMEGA / UNDERWORLD) denoting a category of realm.
3. **Personality archetype** (§10.4.3) — emergent multi-axis personality profiles like "paranoid" or "scholarly" that arise from Character Personality's continuous traits at characteristic configurations.

These three usages do not normally interfere in prose, but a reader encountering "archetype" should check which sense is in play.

### System realm

A special kind of realm flagged with `isSystemType: true` on the Realm entity. System realms are conceptual namespaces for metaphysical entities — PANTHEON (gods), NEXIUS (guardian spirits), DUNGEON_CORES (sentient dungeons), SENTIENT_ARMS (living weapons), UNDERWORLD (the dead). Characters in a system realm get the full L2/L4 entity stack for free: personality, memory, history, growth, knowledge, bonds, cognition. [§5](#5-the-unified-cognitive-progression--system-realms) develops why this is the single most powerful architectural pattern in Bannou.

### God-actor

A long-running ABML behavior document executed by the Actor runtime, acting as a deity, regional watcher, dungeon master, or other autonomous metaphysical entity. God-actors are not a plugin — they are authored content (ABML files) running on the shared Actor infrastructure. They orchestrate the content flywheel, curate regional flavor, and enact divine interventions. [§7](#7-the-content-flywheel) and [§13](#13-actor-bound-entities-living-places-and-things) develop their role in detail.

### ABML (Arcadia Behavior Markup Language)

The YAML-based domain-specific language for authoring event-driven behavior documents. ABML compiles to portable stack-based bytecode and is executed by the Actor runtime. It is the authoring language for NPC brains, god-actors, regional watchers, dungeon cores, sentient weapons, cinematic choreographies, narrative scenarios, and any autonomous-entity behavior. Learn it once and you can author autonomous behavior for every autonomous entity in Arcadia. Developed in §§6 and 12.9.

### GOAP (Goal-Oriented Action Planning)

A* search over action spaces. Each action has preconditions, effects, and costs; the planner finds the minimum-cost action sequence that transforms the current world state into one satisfying the highest-priority goal. GOAP is Arcadia's **universal planner** — the same algorithm powers NPC decisions, narrative composition (Storyline), music composition (MusicStoryteller), combat choreography (CinematicStoryteller), and economic decisions. Improvements to the GOAP planner benefit every creative domain simultaneously. Developed in §§6.2 and 12.

### The Variable Provider Factory

The DI-inversion mechanism that lets lower-layer services consume data from higher-layer services without hierarchy violations. Concretely: when an ABML expression references `${personality.aggression}`, the Actor runtime (L2) pulls personality data from Character Personality (L4) through the shared `IVariableProviderFactory` interface. L4 services implement and register the interface; L2 discovers implementations via `IEnumerable<IVariableProviderFactory>` injection. Without this pattern, either the service hierarchy breaks or NPCs cannot think. This is arguably the single most important architectural pattern in Bannou, and it generalizes: the same inversion lets Quest (L2) consume prerequisite checks from L4, Actor consume behavior documents from L4 Puppetmaster, and Affix (L4) consume activation prerequisites from any layer. Fourteen Variable Providers are implemented; eight more are planned. Developed in §6.4.

### The Cross-Realm Transfer Rule

**Knowledge transfers between realms; resources do not.** A player's guardian spirit carries accumulated understanding — UX capability manifests, Lexicon vocabulary, cross-domain mastery — across every realm they inhabit. But realm-scoped resources (Currency wallets, Item instances, Character relationships, Faction memberships, Location bindings) stay in the realm where they were acquired. The spirit is the thread that connects realms; the characters and their belongings are realm-bound. This rule is mechanically enforced by currency scoping (`scope: realm` vs. `scope: global`) and is the foundation of multi-realm play. A few narrowly-scoped currencies (soul currency, spirit shards, divinity) cross realms by design; most do not. Developed in §3.3.3 and §9.12.

## 1.5 How to Read This Document

**For first readers**, the document is designed to be read linearly. Each section builds on the preceding ones, and later sections assume the terminology and frameworks established earlier. Parts I-III (Foundations, Living World, Emergent Systems) introduce the concepts; Part IV (Realm Design Guides) applies them; Part V (Platform) contextualizes the delivery.

**For readers with specific questions**, the reader's map below points to the sections most likely to help:

| If you want to know... | Go to |
|---|---|
| Why Arcadia is thermodynamically compliant | §2 |
| What knowledge-as-power actually means mechanically | §2 |
| How FANTASIA differs from ARCADIA from OMEGA from UNDERWORLD | §3, §§15-18 |
| What the guardian spirit actually is | §4 |
| Why every metaphysical entity is "a Character in a system realm" | §5 |
| How entities grow from inert objects into autonomous agents (Dormant → Stirring → Awakened) | §5, §13 |
| How NPCs think at scale | §6 |
| Where game content actually comes from | §7 |
| How death creates content | §§7, 8, 18 |
| How economies emerge without players | §9 |
| How NPCs talk, gossip, and form cultures | §10 |
| The Combat Dream and why it matters | §11 |
| What SDKs exist and what they generate | §12, Appendix B |
| How dungeons, weapons, and gods work mechanically | §13 |
| How technology progresses in a magic world | §14 |
| How to design a specific FANTASIA / ARCADIA / OMEGA / Underworld realm | §§15-18 |
| When to build an L5 Extension | §19 |
| How deployment modes differ | §20 |
| What developing for Arcadia actually feels like | §21 |
| Which plugin contributes to which aspect | Appendix A |
| The full SDK ecosystem | Appendix B |

**For readers skimming specific sections**, every section is self-contained enough to be read out of order, but assumes terminology from §§1-3 (this introduction, metaphysics, realm archetypes). If you're skimming §9 and run into "pneuma" without context, [§2](#2-the-metaphysical-substrate--the-knowledge-as-power-thesis) is the place to catch up — it is short enough to read in full before returning to wherever you were.

## 1.6 Structural Conventions

- **Plugin references** use the `lib-{name}` convention (e.g., `lib-actor`, `lib-currency`). Where the plain plugin name (Actor, Currency) reads better, it is used — but always capitalized to disambiguate from the common noun.
- **Layer references** use L0-L5 (Infrastructure, App Foundation, Game Foundation, App Features, Game Features, Extensions). Layer is attached to plugin names where useful: `lib-actor (L2)`, `lib-divine (L4)`.
- **SDK references** use the bare hyphenated name (e.g., `music-theory`, `cinematic-storyteller`, `sprite-composer`).
- **Three-layer SDK pattern** — most creative-domain SDKs follow a consistent structure: **Theory** SDKs provide pure-computation primitives (formal academic structures expressed as data and algorithm), **Storyteller** SDKs provide GOAP-driven procedural composition using Theory primitives as building blocks, and **Composer** SDKs provide interactive authoring tools for human creators. Some domains also ship **Bridge** layers that adapt a Composer to a specific engine (Stride, Godot, Unity). Not every domain has every layer — the pattern serves the domain, not the other way around. Storytellers and Composers emit the same output format, so a runtime consuming a scenario, composition, or scene document cannot tell whether it was hand-authored or procedurally generated. Developed in §12.2; catalogued in Appendix B.
- **System realms** are in ALL_CAPS with underscores (PANTHEON, NEXIUS, DUNGEON_CORES, SENTIENT_ARMS, UNDERWORLD).
- **Specific plugin methods, state-store names, and technical identifiers** use `code formatting`.
- **Cross-references to other sections** use §N notation (e.g., §14 for Section 14).
- **Status markers** appear where needed: "Implemented" (shipping), "In progress" (actively being built), "Planned" (design exists, no implementation), "Aspirational" (design exists but depends on other aspirational systems).
- **External references** (Brandon Sanderson, academic sources, other media) are noted where their ideas directly inform Arcadia's design. They are cited, not merely name-dropped.

The document uses long prose paragraphs interspersed with structured tables, bulleted lists, and diagrams. The prose is primary; tables and diagrams support. If you are skimming, read the opening paragraph of each subsection first to orient, then descend into structure where the content warrants.

## 1.7 On Design Maturity

Every system in this document is a firm architectural commitment. Some systems ship today; some are fully designed and awaiting implementation; none are undecided at the architectural level. Reading this document as a description of what Bannou **is** architecturally is the correct default; the present tense throughout is used intentionally.

Rather than cluttering every mention with status, the document marks status at the section-opening level where it matters and comprehensively in Appendix A. The reader can assume:

- **Part II (Living World)** foundations are shipping: Character, Actor, Behavior, Seed, Resource, Currency, Item, Inventory, Contract, Escrow, Chat, Faction, Obligation, Relationship, Collection, Realm. The Lexicon / Hearsay / Disposition / Agency complements that complete the living-world layer are fully designed and scheduled — they are architectural commitments, not open questions.
- **Part III (Emergent Systems)** mixes shipping implementations (Music theory and storyteller SDKs, Storyline SDKs, Behavior compiler and expressions, Scene composer with engine bridges, Sprite theory) with designed-and-planned layers (Cinematic theory/storyteller/composer, Voxel core/generator/builder, Sprite composer and bridges, Counterpoint composer, intent-channeling enchantment mechanics). Every designed-and-planned piece has been reasoned about against the shipping pieces; they compose cleanly at land-time.
- **Part IV (Realm Design Guides)** applies to any realm at any maturity. A realm can ship with specific systems disabled or stubbed; the guides describe the design space, not a required feature set.
- **Part V (Platform)** ships, with some deployment modes and developer tooling actively evolving.

For precise current status on any specific system, [planning/VISION-PROGRESS.md](planning/VISION-PROGRESS.md) is the authoritative source. For everything else, read the present tense as the present tense — the commitment is real, and the ordered implementation sequence closes gaps without disturbing the architecture the document describes.

---

# 2. The Metaphysical Substrate & The Knowledge-as-Power Thesis

## 2.1 Why This Section Matters

Before any plugin, any algorithm, any gameplay mechanic can be understood properly, the reader must understand the metaphysical framework Arcadia operates inside. Every mechanical choice in Bannou — from how Currency implements wallets to why the Lexicon service exists at all — descends from this substrate. Skip this section and downstream design will seem arbitrary; read it carefully and downstream design becomes almost self-evident.

Arcadia is not a fantasy setting with "magic is just a thing that happens." It is a fantasy setting built on a consistent and debuggable cosmology — closer to a formal system than a folklore tradition. The payoff for that consistency is threefold:

- **Worldbuilding coherence**: Players and NPCs alike can reason about how the world works and will not encounter contradictions.
- **Mechanical legibility**: Systems that look like "magic" from inside the world are regular, rule-governed, reproducible processes from the implementation perspective.
- **Design leverage**: Realm variations ([§§15-18](#part-iv--realm-design-guides)) can tune specific metaphysical parameters (mana density, cost transparency, domain coverage) without re-inventing the underlying laws.

The framework has six interlocking components, presented in order of foundation depth:

1. **The three-layer metaphysics** (logos / pneuma / manifest reality).
2. **Thermodynamic compliance** of all magical operations.
3. **The Lexicon-as-Logos thesis** — the computational mechanism by which the metaphysical framework becomes queryable state.
4. **The recursive lexicon flywheel** — why knowledge compounds into capability.
5. **Hearsay as civilizational anti-entropy** — why knowledge also decays, and what institutions exist to resist that decay.
6. **The knowledge-as-power conclusion** — why these elements collectively make Arcadia mechanically different from every other fantasy setting.

This section develops each in turn, then closes with the philosophical lineage and the design implications for everything downstream.

## 2.2 The Three-Layer Metaphysics

### 2.2.1 Logos

**Logos are pure information particles.** They are the "words" that define what things *are*. Every distinguishable concept — a category of object, a trait, a relationship, an action — has a corresponding logos that is its essential definition.

Knowing something's logos grants some measure of influence over it. This is the foundation of the True Names system: the more precisely you identify what something is (its logos), the more capacity you have to shape, invoke, or redirect it.

Logos in Arcadia are not generated by minds but *perceived* by minds. They exist independently of any observer — the logos for "flame" exists whether or not anyone can call it. Minds discover logos by attending to the world carefully enough to distinguish real categories. A hunter who distinguishes direwolves from ordinary wolves has discovered a more precise logos; a philosopher who distinguishes justice from power has done the same at a more abstract level.

Some logos are **primitive** — irreducible atoms of meaning that cannot be defined in terms of other logos. Others are **composite** — valid combinations of more primitive logos that produce derived meanings. The full set of all logos (primitive and composite) is conceptually infinite; the set of all *known* logos in a given civilization at a given time is finite and grows through discovery.

**Logos are space-time transcendent.** Unlike pneuma and manifest reality, logos are not bound by normal causality — they exist outside time in the sense that propositional definitions do ("two plus two equals four" is not a temporal event), they propagate across distance without mediation when conditions permit, and they pass through barriers that would stop any pneuma-based entity. This property is load-bearing for several core designs: divine barriers between realms block pneuma but not logos (information partitioning, not total isolation), which is how fragments of Nexius (reduced to pure logos, with no pneuma shell) could seed every realm when the Age of Division sealed the worlds apart. The same property underpins Hearsay propagation across vast social distance, ancestral memory surfacing in descendants' dreams, and the cosmic information substrate the underworld rides along (§2.2.6, §2.2.7).

### 2.2.2 Pneuma

**Pneuma is organized logos with volatile and emergent properties.** Where logos is the pure definition of what something is, pneuma is the energy that animates those definitions into reality. Pneuma is the spiritual matter of the Arcadian cosmos.

Pneuma exists in two states:

- **Active pneuma** is magical energy — pneuma in its volatile, energetic form. Magical operations consume active pneuma to produce effects. Environmental mana *is* active pneuma diffused through space.
- **Spent pneuma** is physical matter. When active pneuma expends its volatile properties in manifesting an effect, what remains solidifies into ordinary substance.

**Mana is pneuma internalized within a being.** When active pneuma is absorbed into a living entity's pneuma shell (§2.2.5), it becomes that entity's *mana* — the same substance, but now organized within the being's spiritual structure rather than diffused through the environment. A caster's "mana pool" is literally how much pneuma they can hold and organize inside themselves. The distinction matters: mana is personal reserve; environmental pneuma is the ambient medium the caster can draw from additionally. Small spells use internalized mana; larger spells pull ambient pneuma; massive workings open gates to pneuma-rich sources (leylines, elemental planes).

This is the central thermodynamic claim: **active pneuma transforms into spent pneuma through use, and spent pneuma can (under specific conditions) be reactivated**. The ratio between active and spent pneuma in a region determines its mana density — a leyline-rich sacred site is one where active pneuma predominates; a mana-dead zone is one where spent pneuma predominates and any magical operation quickly exhausts available fuel.

Every physical object in Arcadia began as spent pneuma organized around a logos pattern. This is why True Names have traction: the object's logos is literally part of its identity; addressing the logos directly reaches the fundamental definition underlying the spent-pneuma manifestation.

#### The Frequency Spectrum

Pneuma resonates at a particular frequency along a **continuous spectrum** — not discrete "elements." Low-frequency pneuma is dense and stable (associated with earth, metal, structure). High-frequency pneuma is volatile and energetic (associated with fire, lightning, force). Every point on the spectrum is valid; the classical elements of fantasy fiction are cultural *categorizations* of regions on the spectrum, not inherent divisions of the substrate.

This has a profound consequence: **different cultures perceive and describe the same pneuma differently**. A Fantasia shaman describing "fire-attuned pneuma," an ARCADIA engineer measuring "high-frequency thermal resonance," and an OMEGA technomancer quantifying "high-wavelength psi-matter" are describing the same underlying reality through different cultural Lexicons. This is not relativism — they are all correct at different altitudes of precision. It is the Lexicon-as-Logos thesis (§2.4) at work in its most basic form: same substrate, different vocabularies, each culture's Lexicon organizing observation differently. §2.6 develops how these cultural Lexicons diverge and stabilize; §2.4 develops why the divergence is load-bearing for Arcadia's design.

#### Leylines: The Dual-Nature Substrate Conduits

Pneuma concentrates in specific topology, most importantly **leylines** — channels of concentrated pneuma flowing through the world. Leylines have two distinct aspects at different depths:

- **Surface aspect** — Active pneuma flows near the surface of leylines. Mages tap leylines at the surface layer to power their spellcasting. Sacred sites, temple cities, and high-magic zones cluster at leyline convergences (*nexus points*) because ambient active pneuma is abundant there.
- **Depths aspect** — Deep within leylines, *logos flow* rather than pneuma. This is the **underworld** (developed in §8 and §18) — the inside of the leyline network, where the logos of deceased beings scatter, spread, and eventually reform into new life. The same leyline a surface mage taps for mana, a necromancer or skilled medium can descend into at depth to reach post-mortem souls.

Leylines can be **pinned** by *dragon veins* — naturally rare mineral formations that act as lightning rods for pneuma flow. Dungeon cores are another topology: self-sustaining pneuma generators that create their own magical ecosystems around themselves. Mana-dead zones are leyline-poor regions; nexus points are leyline convergences. This topology is gameplay-critical state — the economic and military geography of Arcadia tracks mana geography the way our world tracks petroleum geography.

### 2.2.3 Manifest Reality

**Manifest reality is the physical world that exists because pneuma has transitioned from active to spent state.** Stone, wood, flesh, water, air — all are spent pneuma holding logos patterns stably. The physical world you walk through, pick up, fight with, and die in *is* manifest reality.

A critical clarification: **physical matter is not "composed of" pneuma the way a wall is composed of bricks**. It is the *residue* of pneuma that has burned through its volatile properties — much as ash is not "composed of" fire but is what remains after fire has done its work. Active pneuma and spent pneuma are different **states**, not different concentrations of the same substance. This distinction matters because it explains the asymmetry of the transformation: active → spent happens easily (any magical expenditure produces spent pneuma); spent → active happens only under specific conditions (Holy magic deconstruction, divine intervention, certain ritual operations — see §2.3 and §2.2.6), and always at significant cost.

Crucially, manifest reality is **not inert**. It retains its logos patterns and interacts continuously with whatever active pneuma is in its environment. A stone is spent pneuma in the logos pattern of "stone" — and it draws on whatever trace active pneuma surrounds it to sustain and reinforce that pattern. In a mana-rich region, stones are more subtly *resonant* with their stone-ness; in a mana-dead region, spent pneuma is quietly decaying.

This is why the Equipment Enchantment Duality ([§14](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology)) can treat every crafted object as a pneuma antenna and every mana-density tier as a first-class gameplay variable. Manifest reality is not the opposite of magic; it is magic *at rest*.

#### Being Types: Physical, Pneumatic, Hybrid

Not everything manifest in the world is composed of spent pneuma. Arcadia has three distinct being-type compositions:

| Being Type | Body Composition | Examples | Key Vulnerabilities |
|---|---|---|---|
| **Physical beings** | Flesh (spent pneuma) + a soul (logos bundle + pneuma shell, §2.2.5) | Humans, animals, plants | Physical damage to body; spiritual damage to soul |
| **Pneumatic beings** | Body is *active pneuma* mimicking/replacing physical matter; soul has the same logos+shell structure | Monsters, spirits, demons, true majin | Body is directly deconstructible by Holy magic (§2.3); heal rapidly (body is same substance as healing magic uses); evolve by absorbing more pneuma |
| **Hybrid beings** | Mix of physical matter and active pneuma | Dhampirs, half-demons, magically-altered humans, certain enchanted creatures | Partial to both physical and Holy damage; often inherently unstable |

This taxonomy is load-bearing for several downstream mechanics. Pneumatic beings (monsters) spawn preferentially in high-pneuma environments because their bodies *require* ambient pneuma to form and sustain themselves — dungeons, leyline convergences, and nexus points provide the density needed. They grow stronger in mana-rich zones and weaken (or flee) when pneuma drains. This is why the Dungeon plugin (§13.2) can spawn creatures at a mana cost: the creatures are literally made of the pneuma being spent.

The asymmetry — some beings' bodies are the same substance as the magic that affects them — is why Holy magic matters differently against different opponents (§2.3) and why monster ecology works the way §13.5's spirit dens describe.

### 2.2.4 The Flow Between Layers

```
       ┌─────────────────┐
       │      LOGOS      │      Pure information particles
       │   (definitions) │      — what things are
       └────────┬────────┘
                │ organizes into
                ▼
       ┌─────────────────┐
       │     PNEUMA      │      Energetic / volatile
       │    (active)     │      — magical energy
       └────────┬────────┘
                │ expends volatile properties
                ▼
       ┌─────────────────┐
       │     PNEUMA      │      Stable / material
       │    (spent)      │      — physical substance
       └────────┬────────┘
                │ constitutes
                ▼
       ┌─────────────────┐
       │ MANIFEST REALITY│      The physical world —
       │   (the world)   │      spent pneuma holding
       │                 │      logos patterns
       └─────────────────┘
```

**Normal magical operations do not transform spent pneuma back into active pneuma.** They take *already-active* pneuma (ambient environmental mana, or the caster's internal mana reserves) and channel it through a logos pattern to produce a manifest effect, with trace spent pneuma precipitating out as residue. The flow is:

```
       Ambient active pneuma (or internal mana)
                    +
                Will (§2.2.8)
                    +
       Logos pattern (True Name / spell / enchantment)
                    │
                    ▼
              Manifest effect
                    +
       Trace spent pneuma precipitation
```

Only **exceptional** operations run the stack in reverse (spent → active → logos-released): Holy magic deconstruction, divine intervention, and specific ritual mechanisms. These are developed in §2.3 and §2.2.6. Physical operations, by contrast, work on the bottom layer (manifest reality) through ordinary mechanical interaction. The distinction between "magic" and "physics" in Arcadia is which layer you are addressing, not whether the laws differ. Both obey the same conservation.

### 2.2.5 Soul Architecture: How the Substrate Becomes Beings

The three-layer metaphysics establishes what the substrate *is*. The next question: how does the substrate organize into *beings* capable of consciousness, memory, and agency? The answer — Arcadia's soul architecture — is the bridge between abstract metaphysics and concrete characters. It is developed in depth in §4 (Guardian Spirit) and §5 (Unified Cognitive Progression); this subsection establishes the minimum every reader needs before proceeding.

Every conscious being in Arcadia has a soul composed of three elements:

```
┌─────────────────────────────────────────────────────────────────┐
│                      PNEUMA SHELL                                │
│                  (protective boundary)                           │
│   Keeps the logos bundle cohered. If it disperses, the soul    │
│   "dies" — the logos scatter and lose their coherent identity.  │
│                                                                  │
│   ┌─────────────────────────────────────────────────────────┐   │
│   │                   LOGOS BUNDLE                          │   │
│   │   (discrete information units)                          │   │
│   │                                                          │   │
│   │   Traits stored as individual logos, not a monolithic   │   │
│   │   nucleus:                                              │   │
│   │     • Personality traits    • True Name components      │   │
│   │     • Learned skills        • Behavioral patterns       │   │
│   │     • Memory archives       • Physical body template    │   │
│   └─────────────────────────────────────────────────────────┘   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
                              │
    SENSE OF SELF ← NOT stored data, NOT spiritual energy
    (the processor running over the logos)
      • Active consciousness processing information
      • Working memory / cache (short-term experiences)
      • Emerges FROM the logos but is DISTINCT from them
```

**The three elements are categorically different:**

- The **logos bundle** is information — discrete, addressable, queryable. It is *what* the being is (its traits, memories, skills, body-template schema). Logos accumulate over a being's life; knowing a character's full bundle is knowing them completely.
- The **pneuma shell** is protective substrate — not a "pool of energy" but the *container* that keeps the bundle cohered. A thicker shell means stronger resistance to pneuma intrusion (harder to charm, curse, or extract logos from); soul death is literally shell dispersal.
- The **sense of self** is the *processor* — the active executing-state running over the stored logos. It is not data; it is not energy; it emerges from logos running. Working memory ("cache" of recent experiences) lives here before being consolidated into logos. The processor reboots at each sleep; reincarnation may preserve cache contents unpredictably.

**Soul power** comes from shell thickness × emotional intensity × available mana — not from any inherent energy in logos. Strong emotions amplify pneuma manipulation; thick shells provide more spiritual "mass" to exert. This is why a terrified mother protecting her child can momentarily exceed a trained mage through sheer emotional intensity alone.

This architecture is why §2.4's claim "a character's tech level is logos coverage" has structural grounding: logos coverage lives in the bundle, protected by the shell, activated by the sense of self. The Lexicon-as-Logos thesis operates on the bundle; the Agency service (§4) operates on what the shell's accumulated experience lets the guardian spirit perceive; the Actor runtime (§6) operates on the sense of self. Every L2/L4 service involved in characters is ultimately working on one or more of these three elements.

§4.2 develops how guardian spirits use this architecture (the spirit's own soul is a Character in NEXIUS). §5 develops how non-traditional entities (dungeons, weapons, gods) use the same architecture when they become Characters in system realms. §8 develops how death works (shell rupture, logos scatter, reincarnation economics). The architecture developed here is referenced by all of them.

### 2.2.6 The Void, Counter-Logos, and Cosmic Entropy

The metaphysics developed above establishes how reality coheres — logos organize into pneuma, pneuma manifests into stable matter. But cohesion is not the only principle in the substrate. There is also a counter-principle: **entropy**, and it is not a passive tendency but an *active force* with its own structured substrate.

**Every logos that binds has a counter that unbinds.** Every principle of creation has an antagonist. These are not "anti-information" in the trivial sense; they are structured principles of dissolution — call them *counter-logos* (the exact terminology in Arcadian scholarship is unsettled; candidates include *antilogi*, *unmakings*, *obscenities*, *void-words*). Counter-logos relate to logos as antimatter relates to matter: contact produces mutual annihilation.

**The Void is the accumulated counter-logos of the cosmos.** When the gods (§2.2.7) looked upon the Void consuming their early creations, they saw not mere emptiness but the natural counter-force to all their creative work. Left unchecked, counter-logos would routinely unbind all made things — every creation eventually meeting its counter and dissolving. This is cosmic entropy at its most structural: dissolution baked into the substrate itself.

```
       ┌────────────────────────┐
       │        LOGOS           │      Principles of cohesion
       │   (principles that     │      — what *binds*
       │        bind)           │
       └───────────┬────────────┘
                   │
                   │ every logos has a counter
                   ▼
       ┌────────────────────────┐
       │   COUNTER-LOGOS        │      Principles of dissolution
       │   (principles that     │      — what *unbinds*
       │       unbind)          │
       └───────────┬────────────┘
                   │
                   │ accumulated counter-logos
                   ▼
       ┌────────────────────────┐
       │        THE VOID        │      Active entropy
       │   (accumulated         │      — cosmic pressure
       │   counter-logos)       │        toward dissolution
       └───────────┬────────────┘
                   │
                   │ manifested and contained by the gods
                   ▼
       ┌────────────────────────┐
       │  NULLIUS, the Great    │      Entropy given form
       │        Demon           │      — sealed away as
       │   (the manifested      │        the first great act
       │    Void, sealed)       │
       └────────────────────────┘
```

**The gods manifested the Void as Nullius, the Great Demon, and sealed it away.** This was not a prison constructed to contain an enemy; it was an **entropy firewall** — the first great architectural act of the divine cosmology. Counter-logos still exist; they are still generated as a natural consequence of creation. But the seal prevents them from actively propagating through reality. The Void "eats" the counter-logos, containing them within the seal, and creation proceeds without immediate dissolution. If the seal ever broke, accumulated unmakings would cascade through reality — the worst possible outcome in Arcadian cosmology.

**Why gods cooperating strengthened them:** More consciousness creating together produces more logos of creation, more organized information, more resistance to unbinding. The collaborative creation of Arcadia was not just about making something bigger — it was about creating something with enough informational density to resist the natural pressure of dissolution. Cooperative creation is *stronger* than solo creation because the combined logos density defends against counter-logos better.

**This cosmic entropy is the deeper substrate of §2.6's Hearsay distortion.** Social-level information decay — rumors coarsening, specific concepts degrading to general ones, civilizational Lexicon desaturating over generations — is one particular expression of a principle baked into the substrate itself. When §2.6 frames Hearsay as "civilizational anti-entropy" and institutions as "fidelity anchors," that framing rhymes with the cosmic level: creation pushes toward cohesion, the Void pushes toward dissolution, and the gods sealed the Void so that creation could proceed. Institutions at the social level play the same role the divine seal plays at the cosmic level — they hold entropy back long enough for something to be built.

### 2.2.7 The Eighteen Gods and the Cosmic Architecture

The gods are not optional flavor in Arcadia's metaphysics. They are load-bearing *architects* whose existence explains why the substrate is ordered at all, and they recur as major agents in every subsequent section. A brief establishment here prevents §2.7.3's "gods as primitive-concept discoverers" and §7's "gods as content-flywheel curators" from reading as isolated claims.

**The gods are personifications of natural law.** Whether they personify pre-existing laws or whether the laws exist because the gods do is unsettled in Arcadian scholarship; the practical effect is the same. There are **eighteen** of them in the canonical cosmology — a specific, finite pantheon, not an open-ended set. Each represents a fundamental aspect of existence: war, love, commerce, death, nature, trickery, craftsmanship, knowledge, and the rest. None is "above" the others in hierarchy; they are **collaborative equals**, each essential to reality's function.

**Their creative work was collaborative, not solo:**

- Early in cosmic time, the gods attempted to create individual worlds separately. Each attempt collapsed to the Void.
- Working **together**, they created Arcadia — the largest and most complex world the cosmos has ever held. Collaborative creation produced sufficient logos density to resist dissolution (§2.2.6).
- As part of that creative act, they manifested the Void as Nullius and sealed it — paying a significant cost. The creation and seal left them weaker than before, but the world was protected.

The gods do **not** hold hierarchical authority over each other, but they do hold hierarchical access to reality manipulation:

```
┌──────────────────────────────────────────────────────────────┐
│    ACCESS TIER                      CAPABILITY                │
├──────────────────────────────────────────────────────────────┤
│    THE VOID (Nullius, sealed)       Would unmake reality      │
│    THE EIGHTEEN GODS                Direct logos manipulation │
│    NEXIUS FRAGMENTS                 Divine potential, earned  │
│    (guardian spirits)                capability through play  │
│    BLESSED BEINGS                   Invocation-granted access │
│    PRACTITIONERS                    Pneuma manipulation via   │
│    (mages, scholars, craftsmen)      knowledge and practice   │
│    COMMON BEINGS                    Instinct-level operations │
└──────────────────────────────────────────────────────────────┘
```

**This is an access hierarchy, not a worth hierarchy.** A farmer's soul is *structurally identical* to a god's — both have logos bundles, pneuma shells, and senses of self. The difference is what they can DO with reality, not how "real" they are. This matters: characters are not categorically inferior to gods in Arcadia. They are less-accessed.

**Gods operate within conservation.** Despite their power, gods are bound by thermodynamics. They harvest mana through their cosmic roles (the god of forests gains mana from all forests they created; the god of commerce gains mana from trade activity). They spend this divine mana on blessings, interventions, and avatar manifestations. A god is not omnipotent; they are extraordinarily well-resourced within their domain. §2.3.1 develops the mortal-vs-divine fuel distinction; §9.11 develops how divinity accumulates through the bag-of-kills pattern.

**The gods are the primitive-discoverers of the cosmos.** Their direct access to logos lets them recognize patterns at scales no mortal could observe. They name new primitive logos into canonical existence (§2.7.3), shape cultural expression by curating which possibilities become traditions (§10.11), and orchestrate the content flywheel by composing narratives from compressed archives (§7). These are not side activities; they are what gods *do* as civilizational intelligences. A world with attentive, diverse gods develops faster. A world whose gods are distracted, fragmented, or absent stagnates — not because anything visible breaks, but because no one is doing primitive-discovery work at scale.

The PANTHEON system realm (§5.3.2) hosts these gods as Characters. Every god in the deployment is a Character in PANTHEON with personality traits, a divinity wallet, a domain specialization, and an ABML behavior document that the Actor runtime executes. When §7 describes "Regional Watchers" and §13.4 describes "gods as Characters," they are describing these eighteen — instantiated in the PANTHEON realm, running as god-actors, observable and debuggable like any other character.

### 2.2.8 Will as the Consciousness-Substrate Interface

The metaphysics developed so far establishes *what the substrate is*. But reality manipulation — magic, thought, creation — requires an *interface* between consciousness and the substrate. That interface is **will**, and Arcadia's metaphysics is unusual in treating consciousness as privileged rather than epiphenomenal.

**Consciousness is the primary interface through which reality can be accessed and modified.** The mind is not a byproduct of physical processes; it is the operator that reads and writes the logos substrate. This is a strong metaphysical claim, and it is load-bearing for nearly everything §2 establishes downstream. Without consciousness-as-primary, the Lexicon-as-Logos thesis has no mechanism — if logos are just information and minds are just pneuma patterns, why should knowing a logos grant influence over it? Because minds are where logos are *read* and *written* in the first place.

**The five-step willpower flow:**

```
   1. Intent formation
         (the sense of self forms a desired outcome)
              │
              ▼
   2. Translation
         (the sense of self translates intent into
          "world language" by referencing relevant logos —
          True Name components, invocations, structured patterns)
              │
              ▼
   3. Authorization
         (divine blessing or natural law permits the modification —
          §2.3.2 develops this as the six-stage spellcasting pipeline)
              │
              ▼
   4. Execution
         (pneuma reorganizes according to the translated command,
          drawing from ambient pneuma or internal mana reserves)
              │
              ▼
   5. Manifestation
         (reality changes to reflect the new state —
          the spell fires, the enchantment inscribes,
          the healing restores, the effect happens)
```

This is not mystical — it is the precise mechanical pipeline every conscious act of reality manipulation follows. The sense of self acts as the processor executing this sequence, reading from the logos bundle (knowledge, skills, True Name components, schema) and directing available mana (internalized pneuma) to produce effects. The six-stage spellcasting pipeline (§2.3.2) is this flow fleshed out with failure modes at each stage; the Equipment Enchantment Duality (§14) is this flow inscribed into objects rather than held in consciousness.

**Will is a continuous spectrum, not tiered levels.** Its effective strength in any given operation depends on:

| Factor | Effect |
|---|---|
| **Pneuma shell thickness** | Denser shells provide more "spiritual mass" to exert |
| **Emotional state** | Strong emotions (desperation, love, rage, conviction) amplify will intensity |
| **Available mana** | More internalized pneuma = more force to direct |
| **Clarity of intent** | Focused, specific intentions manifest more reliably than vague ones |
| **Knowledge / understanding** | Knowing *how* something works (True Name components, mechanism) improves manipulation precision |
| **Practice / experience** | Repeated use strengthens neural and spiritual pathways |

There is no fixed "level of will." A terrified mother protecting her child can momentarily exceed a trained mage through sheer emotional intensity, even with less knowledge or practice. A deeply meditative monk with modest reserves can exceed a powerful but scattered sorcerer through clarity alone. A dying caster with nothing left but resolve can produce a final effect that their best day's work wouldn't match — because their final will is total.

**This is why §2.4's claim lands.** "A character's tech level is logos coverage" is a claim about what the sense of self can reach. "What you can conceive is exactly what your Lexicon access permits" is a claim about what will can translate. The Lexicon-as-Logos thesis *requires* will as the consciousness-substrate interface; without it, logos access would be mere information possession rather than capability. Arcadia's "knowledge is power" thesis works mechanically only because consciousness is privileged to act on the substrate directly.

**Gods and Nexius fragments are the extreme cases.** A god's will operates with extreme shell density, clarity, and mana access; their manipulation is nearly direct substrate-writing (the "blessings" tier of §2.2.7's access hierarchy). A Nexius fragment (a guardian spirit) starts with almost no capability — it is pure logos, no shell, minimal processing. Through cultivation (§4), it accumulates the shell, memory, and understanding to eventually approach divine capability. Everything in between is a matter of how much of the will-pipeline is available: practitioners have stage 1-5 via trained technique, common beings have glimmers via emotional intensity, and so on.

Subsequent sections reference "will" or "consciousness as interface" without re-establishing the concept. This subsection is the definition.

## 2.3 Thermodynamic Compliance

### 2.3.1 Conservation as Foundational Law

The single most important implication of the three-layer metaphysics: **magic conserves energy.** It does not create from nothing.

Every magical effect in Arcadia is active pneuma being channeled through a logos pattern to produce a physical outcome. The effect's magnitude is bounded by the active pneuma available; the effect's nature is constrained by the logos pattern invoked. A fire spell does not summon fire from the void — it takes in ambient active pneuma, channels it through the logos of fire (structured by the caster's inscribed spell), and manifests the resulting flame. When the active pneuma is spent, the spell ends.

This is not a soft restriction. It is the architectural commitment that distinguishes Arcadia from D&D-style "I cast fireball and a fireball exists" magic:

- **Mana-dead zones are not a handwave.** They are regions with insufficient active pneuma to fuel magical operations. Mages in them are as helpless as non-mages.
- **Caster fatigue is not optional flavor.** Casters contribute to the active pneuma pool via their own reserves; sustained casting depletes them measurably.
- **Magical scaling is bounded.** You cannot make a fire bigger than the active pneuma available to sustain it. Ritual magic, manastone caches, and environmental mana-rich zones are all techniques to *source more active pneuma*, not to escape the conservation.

Brandon Sanderson's First Law — the ability to resolve conflicts with magic is proportional to how well the magic is understood — is satisfied almost automatically in Arcadia, because the magic is mechanical and the costs are knowable. Every spell has a price tag in active pneuma. Every caster has a budget.

### 2.3.2 The Six-Stage Spellcasting Pipeline

A completed spell proceeds through six stages, each of which is an observable operation with identifiable prerequisites and failure modes. This is the mechanical pipeline that every magical operation follows, whether cast by a novice hedge-witch or a master thaumaturge.

| Stage | What Happens | What Can Go Wrong |
|---|---|---|
| **1. Accumulation** | The caster draws active pneuma into a working field — from ambient environmental mana, from personal reserves, from a manastone, or from a ritual component. | Insufficient active pneuma available. Caster's reserves depleted. Ritual components inadequate. Environment mana-dead. |
| **2. Attunement** | The caster aligns their own pneuma frequency with the logos pattern they intend to invoke. This is the "tuning" step where the caster becomes a compatible medium. | Caster unfamiliar with the logos. Competing frequency interference. Distracted or emotionally misaligned caster. |
| **3. Manifestation** | The logos pattern is impressed onto the accumulated active pneuma, giving it structure. The energy now "wants" to behave as the pattern dictates. | Incorrect logos pattern. Partial pattern. Competing pattern impressed accidentally. |
| **4. Manipulation** | The patterned active pneuma is shaped, directed, or focused — aimed at a target, sized, timed. | Loss of concentration at this stage scatters the energy. Target moves or resists. |
| **5. Concentration** | The caster sustains the patterned pneuma long enough to cross the threshold at which it will produce its manifest effect. Many spells can be interrupted here. | Distraction, interruption, damage to caster. Reserves run out before threshold reached. |
| **6. Ignition** | The patterned active pneuma discharges, producing the manifest effect. Spent pneuma precipitates out. | Effect is nominal — the "spell goes off" here. Failures at earlier stages may cause misfires or partial effects at ignition. |

This pipeline is the foundation of every magical practice in Arcadia. Different magical traditions (elemental, necromantic, healing, divinatory, etc.) differ in the logos patterns they invoke, the rituals they use to accumulate pneuma, and the techniques they use for concentration — but every practice traverses the same six stages.

This has two critical implications:

- **Magic is teachable.** A master mage can point to exactly which stage a student is failing at. Accumulation issues have different remedies than attunement issues. Instruction is diagnostic, not mystical.
- **Magic is debuggable.** Spell failures have identifiable causes. When a spell misfires, a skilled observer can reconstruct at which stage it went wrong. This is why wizard schools keep records and guilds maintain apprenticeship chains: the practice rewards systematic analysis.

### 2.3.3 Why Magic Is Inherently Empirical

The most counterintuitive implication of the six-stage pipeline: **magical practice is a better empirical domain than early-Earth chemistry was.**

In our world, natural phenomena resist systematic study for a long time. Fire, weather, disease, celestial motion — all looked teleological or voluntaristic to pre-scientific observers. Lightning struck because Zeus was angry. Plague came because sinners offended heaven. Getting to mechanism required centuries of dismantling the assumption that causes were willed rather than mechanical.

In Arcadia, magic is mechanism from day one. Spells have fuel costs. Fuel costs have predictable scaling. Logos patterns are *debuggable*. A wizard who can't predict their own outputs dies young or poor. Selection pressure favors the empirical magus. Master-apprentice relationships are already proto-peer-review: "your invocation is wrong; the logos sequence must be X-Y-Z; here is why your fire keeps fizzling."

This means the scientific method — as a culturally reproduced practice of systematic empirical inquiry — plausibly emerges *earlier* in Arcadia than in our timeline. It emerges out of magical practice first, then extends outward into what we would call physics, chemistry, biology. The Arcadian equivalent of Galileo is not a renegade natural philosopher; she is a third-generation thaumaturge who noticed that her master's spellbooks didn't quite match what her own experiments produced.

### 2.3.4 Environmental Mana as First-Class State

One further commitment falls out of thermodynamic compliance: environmental mana density is not a flavor variable — it is gameplay-critical state.

Arcadian design models mana density as **discrete tiers**, not a continuous scalar. This is both a performance choice (continuous recalculation is hostile at 100K NPC scale) and a gameplay choice (discrete thresholds produce crisp decision points). The [Equipment Enchantment Duality](planning/EQUIPMENT-ENCHANTMENT-DUALITY.md) design formalizes four tiers:

| Tier | Environment | Effect on Enchantments | Effect on Spellcasting |
|---|---|---|---|
| **Dead** (0%) | Anti-magic zones, mana-drained regions | Enchantments cease functioning. Items revert to mundane. | Only personal-reserve casting possible. Massively limited. |
| **Thin** (50%) | Depleted regions, low-mana areas | Enchantments operate at reduced effectiveness — may produce *distortion* rather than reduced benefit. | Casting is inefficient; some spells impossible. |
| **Normal** (100%) | Standard mana density | Full enchantment effect. | Normal casting. |
| **Rich** (200%) | Leyline convergences, sacred sites | Enhanced enchantment with emergent properties beyond design. | Amplified casting; spells beyond normal capacity possible. |

This tiered model is not exotic — it is the same pattern the Environment service (L4) uses for weather, temperature, and other environmental axes. Mana density is just another environmental axis that produces Status effects as characters move between zones.

But the consequences are massive. **Geography has magical weight.** Travel routes, city locations, war campaigns, and economic centers all revolve around where active pneuma is abundant versus scarce. Mana geography is to Arcadia what petroleum geography is to our industrial civilization — a strategic resource that shapes everything.

## 2.4 The Lexicon-as-Logos Thesis

With the metaphysics established, we arrive at the central thesis of this section — the claim that makes Arcadia mechanically different from every other fantasy setting, and that grounds essentially everything else in this document.

### 2.4.1 Two Altitudes of the Same Concept

The **Lexicon plugin** (`lib-lexicon`, L4, planned) is a Bannou service that stores, validates, and serves concept-tuple ontologies. It is the infrastructure that NPCs use to communicate (structured messages of the form `[INTENT] + [SUBJECT]* + [MODIFIER]* + [CONTEXT]*`), that Hearsay uses to propagate beliefs across social networks, and that Disposition uses to articulate motivational drives.

At the metaphysical level, **logos** is the substrate of identity — pure information particles that define what things are. Knowing a thing's logos grants influence over it.

**These are not two related concepts. They are the same concept at two altitudes.**

- The Lexicon plugin *is* the computational instantiation of the canonical lexicon of logos primitives available to a civilization at a given time.
- Every Lexicon entry *is* a logos particle made queryable state.
- Every concept-tuple in a message *is* a composite logos configuration.
- Validating that a speaker has "discovered" a Lexicon entry *is* the mechanical enforcement of logos-access.

This identity is load-bearing. If you separate them — if you treat the Lexicon as merely a "concept database for NPC chat" and logos as "flavor text about the metaphysics" — then most of the downstream design looks arbitrary. But under the identity, the architecture cohers:

- The Lexicon's discovery tiers are the mechanical enforcement that logos knowledge is *earned*, not given.
- Hearsay's concept-level distortion is the natural entropy that applies to logos transmitted through imperfect social channels.
- The Agency service's progressive UX expansion is the guardian spirit's accumulating logos vocabulary.
- Enchantment works by inscribing logos patterns — which are canonically Lexicon entries at the engine level.

When a dungeon's memory manifests as a painting on the wall, that painting encodes Lexicon entries that the dungeon has accumulated through experience. When a god-actor curates regional flavor, they are curating which Lexicon entries are available for composition in their domain. When a civilization forgets a craft, the Lexicon entries that encoded it have decayed from canonical catalog to rumor.

### 2.4.2 Why This Makes Knowledge = Power Mechanical, Not Thematic

Most fantasy settings claim "knowledge is power" as a theme — the clever wizard, the librarian who knows forbidden lore, the scholar-king. But the mechanical realization is usually weak: "knowing" something in D&D means picking a checkbox on a character sheet.

In Arcadia, knowing something is **queryable state**. A character's Collection records which Lexicon entries they have discovered, at what depth (tier), and with what associated context. Knowing the logos of direwolves means:

- The character can think about direwolves (Lexicon entries appear in their cognitive vocabulary).
- The character can communicate about direwolves (messages referencing direwolves pass validation against their Collection).
- The character can recombine direwolf-related concepts in plans (GOAP has more action templates available).
- The character can inscribe direwolf-related enchantments (Affix crafting requires relevant logos access).
- The character's Agency manifest exposes UX for interacting with direwolves (advanced appraisal, tracking, taming).
- The character's NPC behaviors react to direwolf-related situations more competently (variable providers return non-null data).

This is not thematic knowledge-as-power. It is **ontological capacity**. You cannot do what you cannot conceive. And what you can conceive is exactly what your Lexicon access permits.

Consequently:

- **A character's tech level is measured in logos coverage.** Not inventory, not stats, not level.
- **A civilization's tech level is measured in canonical Lexicon expanse.** Not population, not architecture, not military.
- **A player's agency is measured in their spirit's perceptual vocabulary.** Not hours played, not items owned, not achievements.

Every progression system in Arcadia is, ultimately, a logos acquisition system wearing different clothes.

### 2.4.3 Implications That Propagate Through This Document

Once the identity is accepted, a number of downstream claims become near-automatic:

- **Technological progress in Arcadia is Lexicon expansion.** [§14](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology) develops this in detail. New tech is not invented by trial-and-error alone; it is primarily discovered by identifying new primitive logos (paradigm shift) or validated by finding new valid composite patterns (normal science).
- **The content flywheel is a lexicon flywheel.** [§7](#7-the-content-flywheel) develops this. Character deaths produce archives containing logos configurations. Archives feed storylines. Storylines spawn new Lexicon candidates. New Lexicon candidates expand what future play can express.
- **Cultural emergence is a lexicon-level phenomenon.** [§10](#10-the-social-fabric--cultural-emergence) develops this. Customs, rituals, norms, identities — all are culturally-maintained logos configurations. Cultures differ because their Lexicons differ.
- **Progressive agency is lexicon-revelation.** [§4](#4-the-guardian-spirit--progressive-agency) develops this. The guardian spirit starts with minimal Lexicon access and accumulates it through play. The UX manifest *is* the spirit's logos vocabulary rendered into interactive interface.

If the reader takes only one thing from this section, take this: **the Lexicon is not a service among services. It is the computational instantiation of reality's fundamental substrate, and everything above it inherits its logic.**

## 2.5 The Recursive Lexicon Flywheel

### 2.5.1 Two Flywheels Running Together

VISION.md describes the content flywheel as: play generates events → events become archives → archives seed storylines → storylines spawn scenarios → scenarios produce more play. This is the surface flywheel, and it is real.

But beneath it runs a second, more fundamental flywheel:

```
Play generates events
    │
    ▼
Events are perceived by characters — Collection unlocks
    │
    ▼
Collection unlocks expand per-character Lexicon access
    │
    ▼
Richer Lexicon → richer Hearsay, Disposition,
communication, GOAP planning
    │
    ▼
Richer planning → more complex plans →
novel concept combinations
    │
    ▼
Novel combinations propose candidate new Lexicon entries
    │
    ▼
God-actors validate, distill, curate candidates
    │
    ▼
Canonical Lexicon grows → world-level capabilities
grow → new play becomes possible
    │
    ▼  (loop back)
```

The content flywheel is an *epiphenomenon* of the lexicon flywheel. Stories come from characters whose vocabulary grew enough to have a story worth telling. Economies emerge because characters can articulate what they want. Technologies emerge because combinations become expressible. Narrative archive compression is itself a lexicon operation — distilling a life to its essential logos configuration.

This is a critical difference from standard MMO content economics. Traditional MMOs rely on developer teams manually producing content to feed players. Arcadia's loop runs on its own exhaust: **play produces the vocabulary that produces play**.

### 2.5.2 The Compounding Loop

Because Lexicon growth is cumulative — discoveries do not un-discover themselves — the loop compounds. Year 1's ~1,000 story seeds (VISION.md) arise from a small Lexicon. Year 5's ~500,000 story seeds arise from a Lexicon five years richer. The narrative generation rate is not linear in time; it is polynomial in canonical Lexicon size.

This is why Arcadia gets better the longer it runs. A server running for one year has a vocabulary. A server running for five years has a literature. A server running for a decade has a civilization with deep, textured cultural memory that cannot be authored or copy-pasted. The gameplay value of a realm increases with its Lexicon age in the same way the scholarly value of a library increases with its catalog age — not only because more entries have been added (though that too), but because cross-references and emergent connections compound.

## 2.6 Hearsay as Civilizational Anti-Entropy

### 2.6.1 Information Decay Across Social Distance

Every lexicon flywheel runs against a counter-current: information decays when transmitted imperfectly. The Hearsay service (L4, planned) mechanically models this decay as **concept-level distortion across social hops**.

A direct witness encodes a belief as `[WARN] + [DIREWOLF] + [PACK_HUNTER] + [NORTH_GATE] + [WITCH_OF_THE_WOLVES]` — a five-element precise structured claim. The first listener, receiving this as rumor, degrades it to perhaps `[WARN] + [DIREWOLF] + [NORTH_GATE]` — the specific pack behavior and witch association have dropped. The second listener degrades it further to `[WARN] + [CANINE] + [DANGER]` — the specific identity and location have coarsened. The third listener has heard only "there's something dangerous up north."

This is not a bug; it is a feature of how information actually propagates. Rumor coarsens. Specifics erode. Confidence drops. Context is lost before subject. Associations are lost first. By the time a belief has traversed five social hops, it is a vague impression of a memory of a report.

**Consequently, at civilizational scale, any settlement without anti-entropy infrastructure watches its inherited Lexicon slowly degrade** toward coarse-grained concepts. Precise craftsmanship becomes "craft." Precise illness diagnosis becomes "sickness." Specific magical techniques become "old spells." The Lexicon does not vanish; it *desaturates*.

### 2.6.2 Institutions as Fidelity Anchors

In real history, universities, libraries, guilds, priesthoods, and printing presses all emerged as anti-entropy mechanisms for cultural information. The printing press was revolutionary not because it made books but because it made *identical* books — Lexicon preservation at scale.

In Arcadia, the Hearsay distortion model creates mechanical pressure toward the same institutions. A settlement that preserves direct-witness Lexicon records — whether through scribes, apprenticeship chains, enchanted texts, or ritual repetition — holds its civilizational capability. A settlement that relies solely on oral transmission watches its capabilities erode within generations.

This gives academies, libraries, guilds, and priesthoods an in-world function beyond mere flavor. **A university is a Lexicon-fidelity anchor.** A guild-master is an anti-entropy actor. A temple archive is civilizational memory storage. The architecture of knowledge institutions in Arcadia is not decorative — it is functionally necessary for civilizational continuity.

This also implies a design axis for realm variation ([§§15-18](#part-iv--realm-design-guides)): *which anti-entropy institutions does a given realm have, and how robust are they?* A FANTASIA realm with strong literate institutions has a deep, resilient Lexicon; a FANTASIA realm whose traditions are purely oral has a Lexicon continually threatened by forgetting.

### 2.6.3 The Collapse Scenario

A collapse in Arcadia does not look like "the great cataclysm erased the old ways." It looks like Lexicon regression. Specific concepts coarsen. Fine distinctions disappear. Whole craft domains reduce to their parent categories. Civilizational capabilities return to the precision floor that oral transmission can sustain.

This is a much more anthropologically honest model of decline than the usual fantasy trope. It also produces richer recovery mechanics. Recovering lost capability is not just finding "the last surviving master"; it is reassembling the specific Lexicon structure from fragmentary hearsay, supplemented by material evidence (artifacts, ruins, mementos), and validated by trial until confidence returns. Recovery is, in a precise sense, *re-discovery*.

## 2.7 The Dual Innovation Modes

If technology is Lexicon expansion, then there must be more than one mode of expansion. And there is.

### 2.7.1 Recombination — Normal Science

Within the existing canonical Lexicon, new *combinations* can be discovered that produce effects no individual entry would. A master enchanter who combines `[RESONANCE] + [METAL] + [HEAT] + [STABILITY]` may discover a novel property of thermal resonance stabilization, even though all four component logos were already known. The combination is new; the primitives are not.

This is **normal science** in Thomas Kuhn's sense — routine refinement within an established paradigm. It is slow, iterative, predictable. Most guild apprentices and most journeyman enchanters do this. It produces steady improvement without revolution.

In Bannou, this mode is modeled mechanically: NPCs with sufficient personality curiosity, sufficient skill in relevant domains, and sufficient Lexicon access will, via GOAP planning over their action space, occasionally stumble onto combinations that yield novel outputs. The Cryptic Trails system ([§10](#10-the-social-fabric--cultural-emergence)) formalizes this as the hypothesis-generation mechanism: when a character's discovery tiers cross certain thresholds, previously-invisible Lexicon associations become visible, generating self-generated beliefs through Hearsay's `inference` channel.

### 2.7.2 Primitive Discovery — Paradigm Shift

More rarely and more dramatically, a new *primitive logos* can be identified — a new fundamental concept that belongs in the canonical Lexicon. This is **paradigm shift**. Our equivalents in the real world are things like "oxygen" (before Lavoisier, combustion had no unified theory), "electricity-as-flow" (before Galvani), and "natural selection" (before Darwin).

Primitive discovery is hard because it requires recognizing a concept that was previously invisible or misclassified. The oxygen case is instructive: for centuries, combustion was theorized through phlogiston — a concept that was almost but not quite right. The shift to oxygen required both careful experimentation and a willingness to discard the previous category. Primitive discovery always contains this element of conceptual reorganization.

In Arcadia, primitive discovery is rare, expensive, and typically requires specific circumstances — deep archive study, cross-pollination between specialties, encounters with previously-unseen phenomena (new monster species, new realm, new celestial event, new death pattern in the archives).

### 2.7.3 Gods as Primitive Discoverers

Here is the critical architectural insight: god-actors are, in their most profound role, **primitive-concept discoverers**.

A commerce-domain god observes tens of thousands of trade interactions across a realm. She may detect a *concept* — say, `[REPUTATION_CASCADE]` or `[MARKET_SATURATION]` — that no individual merchant could articulate, because no individual merchant has the observational scope. When the god curates that concept into canonical Lexicon, a new domain of economic enchantment becomes available to the realm's merchants, economists, and guild-masters.

This provides a clear mechanical answer to "what do gods do for the world?" They are not just narrative flavor or blessing-dispensers. They are **scale-detectors** who perceive patterns at the regional or civilizational scale and distill those patterns into new primitive logos that expand civilizational capacity.

The Content Flywheel ([§7](#7-the-content-flywheel)) describes this at the narrative level — god-actors compose stories from archives. But the same mechanism extends beyond narrative. Economic concepts, combat concepts, social concepts, technological concepts — all are legitimate god-outputs. The most interesting god-actors are those whose ABML behaviors include "propose new Lexicon primitive when pattern X is observed at scale Y for duration Z."

This reframes gods as civilizational intelligences. A world with mature, attentive gods develops faster. A world whose gods are distracted, corrupted, or dead stagnates — not because anything visible broke, but because no one is doing the primitive-discovery work at scale.

## 2.8 Philosophical Lineage

Arcadia's metaphysics is not arbitrary invention. It draws on an 800-year intellectual tradition that keeps circling back to the idea that reality has an underlying symbolic substrate. Naming the lineage helps clarify what Arcadia *is* by showing what it *echoes*.

| Thinker / Tradition | Core Idea | What Arcadia Implements |
|---|---|---|
| **Ramon Llull** (1300s) — *Ars Magna* | Mechanical rotating wheels combining primitive concepts to generate all possible truths | The combinatorial engine aspect of Lexicon + GOAP |
| **Kabbalah** | Hebrew letters and Names of God as the creative substrate; new true names yield new powers | The True-Name-as-power mechanic; logos-as-identity |
| **Leibniz** (1670s) — *Characteristica Universalis* | Universal symbolic language where every concept is atomic and all disputes resolvable by computation (*calculemus*) | The canonical-lexicon-as-queryable-state commitment |
| **Wittgenstein** — *Tractatus Logico-Philosophicus* | "The limits of my language are the limits of my world" | Agency's UX-as-vocabulary-expansion thesis |
| **Chomsky** — generative grammar | Recursive infinite expression from finite primitives | The combinatorial-growth expectation |
| **Kuhn** — *Structure of Scientific Revolutions* | Distinction between normal science (paradigm-internal) and revolutionary science (paradigm-shifting) | The dual innovation modes ([§2.7](#27-the-dual-innovation-modes)) |
| **Semantic Web / OWL / RDF** | Machine-processable ontologies enabling automated reasoning | The architectural genre the Lexicon plugin belongs to |
| **Borges** — *The Library of Babel*; *The Analytical Language of John Wilkins* | The dream and the horror of a language where every possible concept has a fixed place | The ambition toward comprehensive catalog |

Leibniz is the closest direct analog. He actually believed a universal symbolic ontology would let civilization *compute its way to every truth*. He failed because 17th-century substrate couldn't carry the idea and because real-world concepts resist tidy formalization. Arcadia succeeds because its substrate *is* symbolic — pneuma really is organized logos, so formalization isn't fighting the medium, it's being the medium.

This means Leibniz's dream is not flavor text in Arcadia. **It is the working hypothesis of the universe.** Any character who accumulates enough Lexicon entries and discovers enough valid combinations approaches, asymptotically, a god's-eye view of what is possible. This is why gods have "more lexicon" as their defining trait, and why Nexius — goddess of connections — is the metaphysical frame for the guardian-spirit model. Gods are not powerful because they have magic. Gods are powerful because they have *more lexicon*.

## 2.9 Design Implications

The metaphysical substrate and the Lexicon-as-Logos thesis produce a set of design commitments that ripple through every subsequent section. Stating them explicitly here makes them easier to recognize when they reappear.

### 2.9.1 Technology IS Lexicon Expansion

Every technology in Arcadia — from flint-and-tinder to enchanted steam rail to logos-computational substrate — is ultimately a Lexicon configuration. This is not metaphorical. It is structural. When [§14](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology) discusses how technology progresses in a magic-rich world, it does so in these terms: primitive Lexicon discoveries produce paradigm shifts; valid-combination discoveries produce normal progress.

### 2.9.2 Magic IS Inherently Empirical

Because the substrate is mechanistic and the six-stage pipeline is debuggable, magical practice in Arcadia is inherently an empirical tradition. This means scientific-method-like reasoning plausibly emerges earlier in Arcadia than in our timeline, and realms with mature academies and guilds are likely to have sophisticated theoretical understanding of their own magical systems.

### 2.9.3 All Crafting IS Enchantment

The Equipment Enchantment Duality thesis (§14) follows directly from the metaphysics: any crafted object exists as a logos pattern in spent pneuma, and any crafter working in a mana-rich environment is inherently inscribing trace logos through the act of creation. A novice does this unconsciously; a grandmaster does it deliberately; both are doing the same thing at different magnitudes.

### 2.9.4 Knowledge IS the Measure of All Things

Progression systems across Arcadia — character skill advancement, dungeon awakening, divine empowerment, weapon sentience, civilizational development — all reduce to logos acquisition at different scales. The Seed service (L2) is the universal primitive for tracking this across domains. Collection (L2) is where logos discoveries are recorded. License (L4) is where structured logos configurations are purchased. The infrastructure converges because the underlying reality does.

### 2.9.5 Entropy Must Be Opposed

The Hearsay distortion model guarantees that civilizational capability is under continuous threat. Realm design must account for which anti-entropy institutions exist and how robust they are. Realm collapse scenarios must model Lexicon regression rather than sudden erasure. Recovery mechanics must model reassembly from fragmentary evidence.

## 2.10 The Grounding Principle

Here is the summary commitment that holds everything in this section together:

> **Arcadia is not a fantasy world with a knowledge theme. Arcadia is *about* the thesis that knowledge and reality are the same thing, and every mechanical choice — from the Lexicon service to the GOAP planner to the pneuma/logos metaphysics to the Agency manifest to the Hearsay distortion model — is evidence in favor of that thesis.**

Most fantasy worlds *claim* knowledge=power and deliver knowledge-as-narrative-flavor. A wizard "knowing Fireball" means they picked a checkbox. In Arcadia, knowing something is **queryable computational state** with downstream behavioral consequences across dozens of services.

Most fantasy worlds have *a* metaphysical substrate that their magic obeys — sometimes. Arcadia's substrate is binding everywhere, debuggable everywhere, and architecturally enforced everywhere. Magic does not get special rules; manifest reality and magic are the same substrate addressed at different layers.

Most fantasy worlds *describe* civilizational progress without modeling its mechanism. Arcadia models it: Lexicon growth, institutional fidelity, primitive discovery by gods, anti-entropy pressure, recombinational normal-science.

These commitments are the grounding of everything that follows. Every section after this one inherits them. When the subsequent sections discuss specific plugins and mechanics, those mechanics are specific expressions of this foundational framework — not arbitrary engineering choices but necessary consequences of the cosmology Arcadia is built inside.

---

# 3. The Realm Archetype System

## 3.1 The Core Distinction: Archetype vs. Specific Realm

One of the most important terminological commitments in this document is the separation between **realm archetypes** (role keywords) and **specific realms** (instantiated game worlds). Conflating them collapses the design space and hides the platform's actual flexibility.

- An **archetype** is a role keyword — FANTASIA, ARCADIA, OMEGA, or UNDERWORLD — that names a *category* of realm with characteristic design traits. Archetypes have no runtime existence. They exist as design vocabulary shared between developers, designers, and this document.
- A **realm** is a concrete, runtime-existing persistent world with a unique ID, a name, a species catalog, a location tree, a god-set, a Worldstate clock, and everything else that the Realm plugin (L2) tracks. Realms are entities; archetypes are categories.

A single Arcadia deployment may host any number of realms in any combination of archetypes. The canonical worked example from the Arcadia vision names three — Omega, Arcadia, and Fantasia — but this is *illustrative*, not definitive. A mature deployment could easily host:

- Three distinct FANTASIA realms (a primitive-survival world, a wuxia-cultivation world, a dark-gothic-necromancy world) — each with different god-sets, species, magic distributions, and UX defaults.
- Two ARCADIA realms (a Victorian-industrial enchantment city and a wild-west frontier) sharing economic DNA but with different political structures.
- One OMEGA realm (the meta-dashboard that mediates all the others).
- One UNDERWORLD realm (shared across all the living realms, with aspiration-based afterlife pathways from each).

All ten of these are equally *canonical* instances of the Arcadia platform. None of them is "the real" Fantasia or "the real" Arcadia. The word "Arcadia" names the platform; the archetypes name categories of realm-configuration that the platform supports.

**Why this terminology matters:**

- **It preserves design freedom.** A game designer creating a new FANTASIA realm does not need to fit their vision to an existing canonical Fantasia. They configure the axes (§3.5) to produce the realm they want, and the result is a valid FANTASIA realm.
- **It preserves design constraint.** The archetype keyword carries real meaning: a FANTASIA realm does commit to certain things (a fantasy aesthetic, a magic-dominant tech spectrum, a god-governed divine economy) even as other choices remain free. The constraint is what makes the archetype useful as a design vocabulary.
- **It aligns with the platform's actual architecture.** Bannou does not implement archetypes as first-class runtime entities. It implements realms (via lib-realm), configuration (per-realm and per-deployment), and the primitives scoped to each realm (species, locations, currencies, etc.). "Archetype" is a conceptual layer above the architecture — the way designers talk about shared patterns in how they configure those primitives.

This chapter develops the architecture that makes the archetype system work (§§3.2-3.3), introduces the four archetypes at a high level (§3.4), enumerates the variation axes that distinguish one realm from another within an archetype (§3.5), and shows how deployments compose realms into coherent multi-realm game experiences (§§3.6-3.8). The detailed design guides for each archetype appear in [Part IV (§§15-18)](#part-iv--realm-design-guides).

## 3.2 What Realms Are in Bannou

### 3.2.1 The Realm Plugin

The **Realm service** (`lib-realm`, L2) is the top-level persistent world registry. It manages CRUD for realm entities, maintains the deprecation lifecycle, and provides resource-coordinated merge for consolidation. The full deep dive is in [plugins/REALM.md](plugins/REALM.md); this section covers only what the reader needs to understand the archetype system.

A Realm entity stores:

- **Identity**: `realmId` (GUID), `code` (uppercase unique string), `name` (display name), `description`.
- **Game service binding**: `gameServiceId` (which game the realm belongs to — one game can own many realms).
- **System flag**: `isSystemType` (true for PANTHEON, NEXIUS, DUNGEON_CORES, SENTIENT_ARMS, UNDERWORLD; false for regular game realms).
- **Lifecycle state**: `isActive`, `isDeprecated`, `deprecatedAt`, `deprecationReason`.
- **Metadata**: Free-form key-value pairs for realm-specific configuration.

Realms are deliberately minimal. The plugin stores what every realm must have and delegates domain-specific concerns (species catalogs, location trees, calendars, god-sets, currency schemes) to the services that own those concerns.

### 3.2.2 Realms Are Flat, Not Hierarchical

One of the most important architectural decisions in Bannou: **realms have no parent/child relationships.** They are peer worlds with no structural dependency on each other.

The temptation to nest realms comes from real-world geography — continents contain countries, countries contain regions. But this conflates two different concepts:

- **Realms** are independent worlds with separate rules, species, cultures, and histories.
- **Locations** are hierarchical places *within* a single realm.

The hierarchy that intuition wants is real — but it belongs in the Location service (L2), not the Realm service. Within a FANTASIA realm, "Havenfall is inside Windshire is inside Aethermoor" is a correctly-modeled location tree. But FANTASIA-realm-#1 is not "inside" anything, and FANTASIA-realm-#2 is not a sibling of FANTASIA-realm-#1 in any structural sense — they are independent universes that happen to share an archetype.

Flat realms enable:

- **Independent scaling.** Each realm is a natural partition boundary. Character queries are partitioned by realm. Location trees are rooted per realm. Species are realm-scoped. Different realms can run on different service clusters.
- **Independent rules.** Realms can have fundamentally different game rules without hierarchy-induced inheritance. A FANTASIA realm and an ARCADIA realm share no mandatory rules.
- **Clean deprecation.** Deprecating a realm has zero impact on any other realm. No cascading concerns, no parent-realm updates.
- **Simple cross-realm semantics.** The cross-realm transfer rule — "knowledge transfers between realms, not resources" — is easy to enforce when realms have no parent/child relationships. Each realm's state is self-contained; only the guardian spirit crosses realm boundaries.

### 3.2.3 System Realms: The isSystemType Flag

A subset of realms are flagged `isSystemType: true`. These are not game-playable realms; they are conceptual namespaces for metaphysical entities. The Realm service stores the flag; consuming services interpret what "system realm" means in their domain.

The planned system realms are:

| System Realm | Purpose | Characters Within Are... |
|---|---|---|
| **PANTHEON** | Gods and deities | Divine characters with domain power seeds, used as god-actors for content flywheel orchestration |
| **NEXIUS** | Guardian spirits | Player account spirits with progressive agency seeds |
| **DUNGEON_CORES** | Sentient dungeons | Awakened dungeon entities with mana economies and bound dungeon-master characters |
| **SENTIENT_ARMS** | Living weapons | Awakened weapon entities with wielder bonds — the zero-plugin validation case for the actor-bound entity pattern |
| **UNDERWORLD** | Dead characters | Post-mortem afterlife entities with aspiration-based progression |

System realms are critical infrastructure for the **actor-bound entity pattern** developed in [§5](#5-the-unified-cognitive-progression--system-realms) and [§13](#13-actor-bound-entities-living-places-and-things). For the purposes of this chapter, what matters is:

- System realms are realms. They use the same Realm plugin, the same Character service, the same Seed plugin, the same cognition stack.
- System realms are not game-playable in the traditional sense. Players do not inhabit characters in PANTHEON or DUNGEON_CORES. But characters within those realms are fully real entities with all the L2/L4 infrastructure available.
- System realms are shared across game realms. A PANTHEON god can interfere in any FANTASIA, ARCADIA, or OMEGA realm. A DUNGEON_CORES dungeon can manifest within any realm that supports dungeon content.

For design purposes, system realms are "horizontal" — they cut across the game-playable realm hierarchy. A deployment typically has one instance of each relevant system realm, shared by all game realms in the deployment.

### 3.2.4 The Realm Lifecycle

Realms move through a formal lifecycle:

```
Created ──► Active ──► Deprecated ──► Merged-into-target
                              │
                              └───────► Deleted (permanent)
```

- **Created**: A realm is instantiated via `/realm/create` or `/realm/seed`. It publishes `realm.created`. Dependent services (Puppetmaster, Location, Species, etc.) receive the event and prepare.
- **Active**: The realm is fully usable. Characters can be created in it. Locations, species, currencies, and god-actors can be provisioned.
- **Deprecated**: The realm is soft-disabled. `isDeprecated: true, deprecatedAt: timestamp, deprecationReason: "..."`. New characters cannot be created, but existing characters continue functioning. Deprecation is idempotent and reversible via `/realm/undeprecate`.
- **Merged**: A deprecated realm's contents (species, locations, characters) can be migrated into a target realm via resource-coordinated migration. Each dependent service registers a migration callback that handles its domain-specific logic (species code collisions, location tree merging, character transfers).
- **Deleted**: After deprecation (and optionally merge), a realm can be permanently deleted. The Realm service checks with `lib-resource` for active references (species, locations, characters with `x-references: target: realm` declarations) and refuses deletion if any exist with RESTRICT policy.

This lifecycle gives realm operators the tooling to retire, consolidate, or migrate realms as the platform evolves — without surprising players or losing data.

### 3.2.5 Realm Time: Worldstate Clocks

Each realm has its own **Worldstate clock** (managed by `lib-worldstate`, L2). The clock tracks realm-time elapsed, calendar dates (using a per-realm calendar template), seasonal cycles, and temporal events.

Critically, each clock has a **`timeRatio`** — the ratio of realm-time to real-time. A `timeRatio` of `60.0` means one real-second equals 60 realm-seconds (i.e., one real-hour is one realm-hour at 1:1, no — let me reconsider: actually `60.0` would mean realm-time flows 60x faster than real-time, so one real-minute equals one realm-hour).

This enables a subtle but powerful design lever: **different realms can run at different time scales**. A FANTASIA realm where characters age, marry, raise children, and die across generations may have a `timeRatio: 120.0` or higher so that a player session spans realm-years. A PvP-arena ARCADIA realm may have a `timeRatio: 1.0` for synchronous combat. A fairy-realm FANTASIA may have a `timeRatio: 240.0` to produce the "time dilation" trope — characters returning from fairy realms find that years passed elsewhere.

Time ratio is per-realm, stored on the Worldstate clock. It is a primary design axis ([§3.5](#35-the-variation-axes)).

## 3.3 Realm-Scoped Primitives

### 3.3.1 What Is Scoped to a Realm

Most game-world entities in Bannou are realm-scoped — they belong to exactly one realm and have no meaningful existence outside it. Understanding this scoping is essential for realm design.

| Primitive | Service | Scoping |
|---|---|---|
| **Species** | `lib-species` (L2) | Realm-scoped. Elves exist in Fantasia-A, not Fantasia-B. Species code uniqueness is per-realm. |
| **Location** | `lib-location` (L2) | Realm-scoped. Each location belongs to exactly one realm. Locations form a tree within a realm. |
| **Character** | `lib-character` (L2) | Realm-scoped. A character lives in one realm at a time. Cross-realm character transfer is a formal operation. |
| **Currency (templates)** | `lib-currency` (L2) | Can be realm-scoped or global. Most game currencies are realm-scoped — Arcadia-gold is not Fantasia-silver. |
| **Faction** | `lib-faction` (L4) | Realm-scoped. Factions have territory within realms. |
| **Worldstate clock** | `lib-worldstate` (L2) | Realm-scoped. Each realm has its own time. |
| **Realm History** | `lib-realm-history` (L4) | Realm-scoped by definition. |
| **Item template** | `lib-item` (L2) | Can be realm-scoped or global via game service. |
| **Quest template** | `lib-quest` (L2) | Typically realm-scoped. |
| **Transit connections** | `lib-transit` (L2) | Realm-scoped within a realm; explicit cross-realm connections exist but are rare and significant. |
| **Regional watchers** | ABML content via `lib-puppetmaster` (L4) | Realm-scoped. Puppetmaster subscribes to `realm.created/updated/deleted` events to manage watcher lifecycle. |

### 3.3.2 What Crosses Realm Boundaries

Only a small number of entities exist above the realm layer, and they are the infrastructure for the multi-realm player experience:

| Entity | Service | Why It Crosses |
|---|---|---|
| **Account** | `lib-account` (L1) | The player's identity. Accounts are realm-agnostic. |
| **Guardian spirit (seed)** | `lib-seed` (L2) + NEXIUS Character | The player's spirit is a Character in the NEXIUS system realm; it is bound to the account and persists across all game realms. |
| **Subscription** | `lib-subscription` (L2) | Grants account access to a game service; does not care which realms within that service the player visits. |
| **Collection (via spirit)** | `lib-collection` (L2) | The spirit's accumulated knowledge — its Lexicon vocabulary, its discoveries — crosses realms via the spirit's own Collection. |
| **System realm characters** | `lib-character` (L2) | A PANTHEON god character is in PANTHEON; they can *interfere in* any game realm. The god is realm-bound (to PANTHEON); their influence is not. |

### 3.3.3 The "Knowledge Transfers, Resources Don't" Rule

This cross-realm scoping produces the canonical Arcadia transfer rule: **knowledge moves freely across realms via the guardian spirit; physical resources do not.**

A player's Arcadia-realm character learns blacksmithing — their Seed domain growth in the `craft.blacksmithing` domain feeds back to the guardian spirit's accumulated experience. When the player later inhabits a Fantasia-realm character, the spirit carries that knowledge: the new character benefits from the spirit's richer Lexicon and richer UX manifest, even though the new character is mechanically unskilled in their own realm.

But the gold the Arcadia character accumulated stays in Arcadia. The sword they forged stays in Arcadia. The friendships they built stay in Arcadia. These are realm-scoped concrete entities, and they do not travel.

This rule mechanically enforces a core design principle: **each realm is its own world with its own stakes, and the guardian spirit is the thread that connects them.** A player who has mastered many domains across many realms has a spirit of deep accumulated capability; their specific character assets in any one realm reflect that realm's history alone.

## 3.4 The Four Archetypes Overview

With the architecture established, here is the high-level shape of each archetype. Detailed design guides appear in Part IV.

### 3.4.1 FANTASIA — Fantasy-Based Realms

**Signature**: Magic-dominant spectrum, fantasy aesthetic, broad variation across sub-genres.

FANTASIA is the most varied archetype because "fantasy" itself covers an enormous design space. A FANTASIA realm can be any of:

- Pre-Bronze-Age survival (low tech, scarce magic, tribal societies).
- Classical-era high fantasy (medieval technology, guild magic, chivalric cultures).
- Wuxia / cultivation (Eastern fantasy, energy-cultivation magic, hierarchical sects).
- Steampunk-enchantment (near-industrial tech via enchantment, guilds-as-corporations).
- Dark gothic / necromantic (death-focused magic, crumbling civilizations, psychological horror).

All are valid FANTASIA. What unifies them is the magic-first, tech-through-enchantment character of technological progression (per [§14](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology)) and the presence of active divine agents (gods via the PANTHEON system realm) shaping the world.

### 3.4.2 ARCADIA — Contemporary / Industrial Realms

**Signature**: Enchantment-industrial, urban, political-economic emphasis.

An ARCADIA realm is the "contemporary" point on the tech-progression spectrum, adjusted for the fact that electronics is impossible in a mana-rich world. ARCADIA realms have what our world gets from industrial revolution — trains, factories, telegraphy, mass production, urbanization — but implemented through enchantment rather than electricity.

Typical ARCADIA variations:

- Victorian-industrial enchantment city (guild-dominated manufacturing, stratified class structure).
- Wild-west resource frontier (rail-linked settlements, extractive economy, weak central authority).
- Mercantile city-state (trade-dominated, naval power, cosmopolitan).

The archetype is unified by the *enchantment-industrial paradigm*: mass-production-scale enchantment capability, transit networks, and economic-political systems that feel "modern-but-not-electronic."

### 3.4.3 OMEGA — Cyberpunk Meta-Dashboard Realms

**Signature**: Cyberpunk aesthetic, diegetic configuration layer, meta-game emphasis.

An OMEGA realm is organized as a **meta-dashboard** over other realms. The player's OMEGA-realm character lives in a cyberpunk setting — typically a high-rise apartment with a VR-machine at the center — and *plays* the other realms (FANTASIA, ARCADIA, etc.) through the VR system.

The OMEGA realm's primary function is to provide **diegetic configuration**: the VR machine's literal interface in-world IS the interface through which the player configures their UX manifests, selects which account seed to inhabit, hacks UX unlocks, and meta-manages their multi-realm experience.

OMEGA variations exist but are more constrained than FANTASIA's — the archetype is defined more tightly by its functional role.

### 3.4.4 UNDERWORLD — Afterlife / Post-Mortem Realms

**Signature**: Post-mortem inhabitation, aspiration-based pathways, content-flywheel amplifier.

An UNDERWORLD realm is inhabited by dead characters from other realms. It is an instance of the UNDERWORLD system realm (`isSystemType: true`), and its inhabitants are Characters whose death elsewhere archived them here.

UNDERWORLD realms provide their own gameplay — the afterlife is not "you lost, now you wait" but a fully-realized set of aspiration-based pathways that post-mortem characters pursue. They also amplify the content flywheel: dead characters' compressed archives feed the living realms' Storyline generation; UNDERWORLD activity feeds back to the living realms as ghost NPCs, revenant quests, memento-bearing locations, and inherited legacy mechanics.

Underworld variations can draw on different cultural afterlife traditions — Greek underworld, Buddhist wheel-of-rebirth, Norse Valhalla, etc. — as long as the core mechanical function (post-mortem inhabitation with feedback to living realms) is preserved.

## 3.5 The Variation Axes

Within each archetype, concrete realm design is a matter of selecting values along a set of configuration axes. These axes are the design vocabulary for building a specific realm. A FANTASIA realm and an ARCADIA realm differ because they sit at different points on many of these axes; two FANTASIA realms can feel like fundamentally different games because they sit at different points on a few.

The primary axes are:

### 3.5.1 Magic Distribution

*Who can cast?*

| Value | Meaning |
|---|---|
| **Universal** | Everyone can cast to some degree. Magic is a general human capability. |
| **Common-graded** | Everyone has latent capability, but talent varies widely; most people cast poorly or not at all. |
| **Rare-elite** | 1-10% of the population can cast. Mages are a distinct social caste. |
| **Single-lineage** | Casting is restricted to specific bloodlines, species, or chosen individuals. |

This axis has profound downstream effects on who has power, how institutions organize, and whether the non-magical majority develops countervailing technology.

### 3.5.2 Magic Cost Transparency

*How visible are the costs of magic?*

| Value | Meaning |
|---|---|
| **Obvious and external** | Spells consume visible materials, manastones, or ritual components. Cost is a commodity. |
| **Obvious and internal** | Casters visibly fatigue or suffer somatic effects. Cost is personal. |
| **Mixed** | Some costs are external (components), some internal (fatigue). Most FANTASIA realms sit here. |
| **Obscured** | Costs are unclear or delayed. Magic looks like causality violation. |

The scientific-method emergence ([§2.3.3](#233-why-magic-is-inherently-empirical)) is faster in transparent-cost realms and slower in obscured ones.

### 3.5.3 Domain Coverage

*What can magic do?*

| Value | Meaning |
|---|---|
| **Narrow-deep** | Magic covers a narrow slice of needs but deeply (e.g., combat and communication only, but extraordinarily well). |
| **Broad-shallow** | Magic touches most domains but does nothing well; tech fills the gaps. |
| **Broad-deep** | Magic dominates almost all needs; tech is vestigial. |
| **Narrow-shallow** | Magic is rare and weak; tech is primary. |

Sanderson's Third Law suggests narrow-deep produces the most interesting fantasy worlds. Broad-deep produces worlds where civilization looks "wrong" (no need for most tech).

### 3.5.4 Pneuma-Electronics Interference

*Can electronics exist?*

This is a specific subset of domain coverage, important enough to name separately. Arcadia's canonical position (VISION.md) is that mana interferes with electrical potential, making traditional electronics impossible — but realm variation can adjust the strength.

| Value | Meaning |
|---|---|
| **Full interference** | No electronics possible. All technology is enchantment. (Default for most FANTASIA and ARCADIA.) |
| **Partial interference** | Low-power electronics work in mana-thin zones. Enchantment dominates elsewhere. |
| **Mana-free zones** | Specific regions where mana is absent, allowing electronics to exist (mad-science enclaves, ancient pre-mana ruins). |
| **No interference** | Electronics and magic coexist freely. (Rare; OMEGA realms typically have this.) |

### 3.5.5 God-Set Composition

*Which gods interfere?*

A realm's god-set is one of its most distinctive features. The PANTHEON system realm holds all gods; each game realm has a subset of those gods actively "interfering" — running god-actors (ABML behavior documents) that observe the realm, compose content, issue blessings, and curate Lexicon.

Axes within god-set composition:

- **Pantheon size** (few gods, many gods, no gods).
- **Domain coverage** (which domains have active gods — war, love, commerce, death, crafting, nature, etc.).
- **God personality distribution** (benevolent-dominant, capricious-dominant, malevolent-dominant, balanced).
- **Divine presence intensity** (gods visibly interfere vs. gods operate subtly behind the scenes).

A FANTASIA realm with a war-heavy pantheon feels militaristic; the same realm with a nature-heavy pantheon feels pastoral. The underlying mechanics are identical; the god-set choice produces the texture.

### 3.5.6 Species Catalog

*Which species exist?*

Realm-scoped species (via `lib-species`) determine both playable characters and NPC populations. Common FANTASIA species inventories include humans, elves, dwarves, orcs, halflings, kobolds, direwolves, dragons, etc. A specific realm may include or exclude any of these.

Ethology ([§10](#10-the-social-fabric--cultural-emergence)) defines behavioral archetypes per species. A realm's species catalog combined with its ethology configurations produces a distinctive ecological and social feel.

### 3.5.7 Resource Profile

*What materials and substances exist?*

Realm-scoped Item templates and world resources. A realm with abundant iron but rare gold feels different from one with abundant gold but rare iron. Specific magical materials (mithril, adamantium, logos-crystals) can be present or absent per realm.

Environment axes (weather patterns, mana density distribution, biome coverage) also fall under this category.

### 3.5.8 Technology Tier Defaults

*Where on the magic/tech progression does the realm start?*

Even within a single archetype, realms can be placed at different technology tiers:

- **Pre-agricultural** (stone tools, oral tradition, shamanic magic).
- **Agricultural** (iron tools, city-states, temple magic).
- **Classical** (early-empire tech, guild magic, written records).
- **Medieval** (feudal structures, mature enchantment guilds, universities).
- **Renaissance** (proto-scientific magic, early printing analogs, merchant capitalism).
- **Enchantment-industrial** (mass production, railroads, telegraphy via enchantment).
- **Post-industrial** (logos-computation, high-capacity enchantment networks).

FANTASIA realms typically cluster below enchantment-industrial; ARCADIA realms typically live at enchantment-industrial and beyond.

### 3.5.9 Worldstate Clock Rate

*How fast does time pass?*

The `timeRatio` on the realm's Worldstate clock. Fast realms (120x, 240x) enable generational gameplay within normal session lengths; slow realms (1:1) enable synchronous PvP and tight-cinematic content.

### 3.5.10 UX Manifest Emphasis

*Which UX domains are surfaced most readily?*

The Agency service's UX capability manifest is per-spirit and progressively expands, but a realm can bias which domains expand fastest. A combat-heavy FANTASIA surfaces combat-UX progression opportunities more frequently; a crafting-heavy FANTASIA surfaces crafting-UX. This doesn't prevent other domains from unlocking, but it shapes the default experience shape.

### 3.5.11 Anti-Entropy Institution Strength

*How resilient is the realm's Lexicon?*

Per [§2.6](#26-hearsay-as-civilizational-anti-entropy), civilizations without strong knowledge institutions suffer Lexicon regression. A realm's design choice about the density and robustness of its academies, libraries, guilds, and priesthoods is a critical texture dial:

| Value | Meaning |
|---|---|
| **Fragile** | Oral tradition only. Lexicon continually decays. Craft knowledge is scarce. |
| **Guilded** | Strong apprenticeship chains but no widespread literacy. Lexicon stable within guilds, scarce outside. |
| **Literate** | Writing exists; religious and academic institutions maintain records. Lexicon broadly preserved. |
| **Industrialized** | Printing-press equivalents exist; universal literacy; mass-produced fidelity. Lexicon grows rapidly. |

This axis strongly shapes civilizational texture — fragile realms feel archaic and mysterious; industrialized realms feel modern and knowable.

### 3.5.12 Population Density & Social Structure

*How are people organized?*

Tribal, city-state, feudal, imperial, federal, corporate. This axis affects Faction configuration, Organization types, and the social-dynamics emergent behavior of NPCs.

## 3.6 "Same Game, Many Games" as a Deployment Truth

The combination of (a) the flat-realm architecture, (b) per-realm configuration on the axes in §3.5, and (c) the guardian-spirit crossover model produces a remarkable property: **the same Bannou deployment can host realms that feel like entirely different games, and the same player can experience all of them as a unified whole.**

This is not marketing language; it is a deployment fact.

- **The platform is identical** across realms. Bannou's 78 service plugins, its WebSocket protocol, its generated client SDKs, its deployment modes — all shared.
- **The realms differ** by their configuration: which axes they sit on in §3.5, which species they include, which gods interfere, which UX manifest they emphasize.
- **The player experience is unified** by the guardian spirit: one account, one spirit, one accumulated vocabulary, navigating many realms via one OMEGA-mediated interface.

A FANTASIA-focused player who never enters the ARCADIA realm is still playing the same game as an ARCADIA-focused player who never enters the FANTASIA realm — same platform, same client, same account, same spirit — but their experienced games are utterly different because their realms differ on every axis of §3.5.

This is why PLAYER-VISION.md's "same systems, different games" thesis is *true*, not aspirational. Different players experience what feel like fundamentally different games because they have settled on different realms, developed their UX manifests in different domains, and accumulated different Lexicon vocabularies. The world beneath them is identical; the world above them diverges.

## 3.7 Realm Composition: Deployments as Realm Sets

A Bannou deployment is, at the highest conceptual level, a *set of realms*. Specifically:

- Zero or more system realms (typically one PANTHEON, one NEXIUS, plus whatever specialized system realms the deployment supports).
- One or more game realms (in any archetype mix).
- Zero or one OMEGA realm (if the deployment supports meta-dashboard play).
- Zero or one UNDERWORLD realm (if the deployment supports afterlife gameplay).

These realms group into Game Services (`lib-game-service`, L2) — a Game Service is a catalog entry that corresponds to "a game" in the market-facing sense. One Game Service can own multiple realms:

```
Game Service: "Arcadia"
  ├── Realm: "Fantasia-Primitive"   (FANTASIA archetype)
  ├── Realm: "Fantasia-Guilded"     (FANTASIA archetype)
  ├── Realm: "Arcadia-City"         (ARCADIA archetype)
  ├── Realm: "Omega-Hub"            (OMEGA archetype)
  └── Realm: "Underworld-Common"    (UNDERWORLD archetype, system)

System Realms (shared across all game realms):
  ├── Realm: "PANTHEON"             (isSystemType)
  └── Realm: "NEXIUS"               (isSystemType)
```

The above is a plausible deployment. The Game Service entry is what players subscribe to (via `lib-subscription`, L2); subscribing grants access to all realms within that Game Service.

A studio operating Bannou could alternatively split across multiple Game Services — "Our Fantasy Game" (containing only FANTASIA realms), "Our Industrial Game" (containing only ARCADIA realms) — if the target audiences are different enough that unified subscription doesn't make sense. But a canonical Arcadia deployment treats the multi-realm experience as a single product.

## 3.8 Cross-Realm Spirit Flow

The guardian spirit is the thread that ties the multi-realm deployment into a coherent player experience. The spirit's flow through realms is:

```
   1. Player connects. Spirit is in NEXIUS (account's home system realm).
          │
          ▼
   2. Spirit enters OMEGA realm (via VR machine — diegetic realm transition).
          │
          ▼
   3. In OMEGA, spirit selects an account seed (up to 3 concurrent).
          │
          ▼
   4. Spirit drops into target game realm (Fantasia, Arcadia, etc.).
          │
          ▼
   5. Spirit inhabits a character in that realm (possession-collaboration model).
          │
          ▼
   6. Character lives its life. Spirit accumulates Lexicon vocabulary and UX manifest.
          │
          ▼
   7. Session ends. Spirit returns to OMEGA (or directly to void).
          │
          ▼
   8. Spirit may select a different seed, entering a different realm.
          │
          ▼
   9. Accumulated Lexicon / UX carries forward — the spirit is richer everywhere.
```

At each transition, infrastructure handles the mechanics:

- **Cross-realm character transfer** is orchestrated by Transit (`lib-transit`, L2) when realms share explicit connections, or by the OMEGA realm's diegetic configuration for direct dropping-in.
- **Spirit state** persists in NEXIUS regardless of which game realm the spirit is inhabiting.
- **Worldstate clocks** switch per realm — a player returning to a realm after time away discovers that realm-time has progressed at its own `timeRatio`.
- **UX manifests** are spirit-level but respond to realm context — combat-UX unlocked in realm A is available in realm B, but realm B may surface it differently based on its UX emphasis.

This is how different realms can feel like different games *while still being experienced as facets of the same unified world*. The spirit is the point of view; the realms are the views.

## 3.9 Preview of the Design Guides

The remainder of this document develops the mechanics, systems, and interactions that constitute Arcadia in abstract. [Part IV](#part-iv--realm-design-guides) — specifically [§§15-18](#15-designing-fantasia-realms) — returns to the archetype system with concrete design guidance for each:

- [§15](#15-designing-fantasia-realms) develops FANTASIA realm design across the axes in §3.5, with worked examples spanning pre-Bronze-Age through steampunk-enchantment.
- [§16](#16-designing-arcadia-realms) develops ARCADIA realm design with worked examples in the Victorian-industrial, wild-west, and mercantile-city-state veins.
- [§17](#17-designing-omega--meta-realms) develops the OMEGA meta-realm archetype with special attention to the VR-machine diegetic configuration and the Beyond Immersion App integration.
- [§18](#18-designing-underworld--afterlife-realms) develops UNDERWORLD realm design with worked examples drawing on Greek, Buddhist, and Norse afterlife traditions.

Those sections assume the foundational concepts developed in Parts II-III (the living systems that every realm inherits). When a realm design guide says "a FANTASIA realm with fragile anti-entropy institutions," it is referring to the primitives and vocabulary established in the preceding sections.

The archetype system is the vocabulary for *design at the deployment level*. The living systems of Part II are the vocabulary for *design at the mechanics level*. Together they define what it means to build a game on Bannou.

---

# PART II — THE LIVING WORLD

# 4. The Guardian Spirit & Progressive Agency

## 4.1 The Player Is a Spirit, Not a Character

The most distinctive commitment in Arcadia's player experience — more than any single mechanic, more than any system architecture — is that **the player does not control a character. The player is a guardian spirit that inhabits and collaborates with characters.**

This is a philosophical commitment with mechanical consequences that ripple through dozens of services. Before developing the mechanisms in this section, it is worth stating the commitment plainly:

- The character is **always autonomous**. An NPC brain runs at all times, making decisions about movement, speech, combat, crafting, and social behavior based on the character's personality, memory, and current goals.
- The guardian spirit **inhabits** the character — provides presence, attention, and the ability to influence decisions. But "control" is the wrong word. The spirit does not send a keypress and get a character action; the spirit sends a nudge (a structured perception) and the character evaluates whether to comply based on personality and compliance factors.
- The player **is** the spirit. Not the character. When the player changes which character they are "playing," the spirit is inhabiting a different character; it is not "swapping avatars."
- The spirit **persists across lifetimes**. Characters live, age, marry, die. The spirit — bound to the account, resident in the NEXIUS system realm — continues.

This model has a name across the game's documentation: the **dual-agency model**. Both the character and the spirit have agency; gameplay emerges from their collaboration (and, occasionally, their friction).

This section develops the architecture that makes the dual-agency model work: the NEXIUS system realm where spirits exist ([§4.2](#42-the-nexius-system-realm)), the Agency service that computes progressive UX capabilities ([§4.3](#43-progressive-agency-as-earned-context)–[§4.4](#44-domain-specific-ux-expansion)), the Gardener service that orchestrates the player's moment-to-moment experience ([§4.5](#45-gardener-the-experience-orchestrator)), the void as starting point and lobby ([§4.6](#46-the-void-experience)), pair bonds for cooperative play ([§4.7](#47-pair-bonds-twin-spirits)), and the multi-seed account model for parallel game-world relationships ([§4.8](#48-multi-seed-accounts)).

## 4.2 The NEXIUS System Realm

### 4.2.1 Spirits as Characters in a System Realm

Per [§3.2.3](#323-system-realms-the-issystemtype-flag), system realms are realms flagged `isSystemType: true` that serve as conceptual namespaces for metaphysical entities. **NEXIUS is the system realm that hosts guardian spirits.**

Every player's guardian spirit is a **Character in NEXIUS**. This is not a loose metaphor — it is mechanically precise:

- The spirit has a Character entity (via `lib-character`, L2) with all normal Character infrastructure: a name, a species (or equivalent), personality traits (via `lib-character-personality`, L4), memory (via `lib-character-encounter`, L4), history (via `lib-character-history`, L4), relationships (via `lib-relationship`, L2).
- The spirit's Character lives in the NEXIUS realm — the realm context that defines its logos, its Lexicon access, its Worldstate clock.
- The spirit accumulates **Seed growth** across domains (via `lib-seed`, L2). This growth is the substrate of the spirit's progressive agency.
- The spirit can be the target of ABML behaviors if desired (a mature design might have the spirit itself running its own ABML cognition in the same way gods and dungeons do — see [§13](#13-actor-bound-entities-living-places-and-things)).

The key design consequence: **every piece of infrastructure that exists for game-world characters also works for guardian spirits**, because the spirit *is* a character. No special-case code. No "but the guardian spirit is different" exceptions. The NEXIUS realm's inhabitants are first-class residents of the same infrastructure as everyone else.

### 4.2.2 What Makes Spirits Distinctive

If spirits use the same Character infrastructure, what makes them different from regular characters?

- **They are bound to accounts.** Every Account (`lib-account`, L1) has exactly one guardian spirit. The spirit's character is scoped to the account; it is created during account provisioning and persists for the account's entire lifetime.
- **They cross realm boundaries.** Regular characters live in one realm (Fantasia-A character lives in Fantasia-A). Spirits live in NEXIUS but **inhabit** characters in other realms. The spirit's own Character stays in NEXIUS; its influence extends into whatever realm contains the character it is currently inhabiting.
- **They grow through play in other realms.** A spirit's Seed growth comes not from its own actions in NEXIUS but from the inhabited character's experiences in other realms. A combat encounter in Fantasia grows the spirit's `combat` Seed domains; a crafting session in Arcadia grows the `crafting` Seed domains.
- **They are identity-stable across characters.** When the inhabited character dies, the spirit survives to inhabit a new character. When the player switches seeds (§4.8), the same spirit inhabits a different character entirely. The character changes; the spirit does not.

### 4.2.3 Inhabitation, Not Possession

The term "inhabitation" is used deliberately. A spirit inhabits a character the way a reader inhabits a book — presence and attention are there, but the character retains its own agency. The spirit can:

- **Perceive** what the character perceives (vision, hearing, internal state — surfaced through the UX manifest).
- **Influence** the character's decisions (via the Agency `execute-influence` flow — structured perception injection that the character's ABML can accept, resist, or modify).
- **Witness** the character's life in detail that an outside observer could not.

But the spirit cannot:

- **Override** the character's autonomous behavior. There is no "the spirit takes direct control" mode.
- **Force** specific actions against the character's personality and circumstances. A spirit can nudge a pacifist character to violence, but the character may refuse or comply reluctantly (with consequences in Disposition's guardian-feelings system).
- **Change** the character's fundamental traits instantly. Personality evolves through experience; the spirit can influence the rate and direction, but not flip values.

This is why the "control" language is wrong and the "collaboration" language is right. The spirit is a co-pilot, not a driver.

## 4.3 Progressive Agency as Earned Context

### 4.3.1 The Core Principle

PLAYER-VISION.md states the fundamental principle plainly:

> The guardian spirit starts as nearly inert. Through accumulated experience, it gains understanding. Understanding manifests as increased control fidelity and richer UX surface area.

This is **progressive agency** as an architectural commitment. The spirit does not have "permanent abilities" that unlock on level-up. The spirit has *understanding*, which grows as it accumulates experience across characters and lifetimes. Understanding manifests as:

- More perceptual detail. Early spirits see characters acting and react to broad emotional tones; mature spirits perceive the tactical structure of combat, the material properties of crafting work, the subtext of social negotiation.
- Richer influence. Early spirits can only suggest direction (approach, retreat); mature spirits can direct specific stance choices, timing windows, combo sequences.
- Domain specialization. A spirit's combat understanding grows independently from its crafting understanding; a combat-focused spirit will see a rich combat UX and a minimal crafting UX.

The philosophical claim is that **agency is earned context, not granted capability**. The character was always capable of combat choreography — the combat-UX was always present in the world. What changed is the spirit's ability to perceive and influence that structure. The mature spirit doesn't unlock new actions; it unlocks new *visibility*.

### 4.3.2 The Agency Plugin

The **Agency service** (`lib-agency`, L4, planned) implements progressive agency as an engine that translates Seed growth into UX capability manifests. It is the bridge between:

- **Seed (L2)** — which tracks the spirit's capability depth per domain path as a continuous float (0.0-10.0+), driven by game events and growth rules.
- **Client-side rendering** — which loads or unloads UX modules based on what the manifest says is available.

Agency maintains three registries:

1. **UX Domains** — categories of player experience (combat, crafting, social, trade, exploration, magic, etc.) with seed-domain mappings.
2. **UX Modules** — discrete UI elements within a domain (stance selector, timing windows, material chooser, tone suggestion, etc.), each with a required Seed capability path, a depth threshold for enablement, and a fidelity curve that maps depth to an integer fidelity level (1-5).
3. **Influence Types** — specific nudges the spirit can send to the character (approach, retreat, use-stance, greet, buy), each with a required capability, a depth threshold, a perception type for Actor, and compliance factors that affect the character's acceptance.

From these registries, Agency computes a **manifest** per seed:

```json
{
  "seedId": "...",
  "computedAt": "2026-02-14T12:00:00Z",
  "domains": {
    "combat": {
      "overallFidelity": 0.65,
      "modules": [
        { "code": "combat/intention",       "enabled": true,  "fidelityLevel": 5 },
        { "code": "combat/stance_selector", "enabled": true,  "fidelityLevel": 3 },
        { "code": "combat/timing_windows",  "enabled": true,  "fidelityLevel": 1 },
        { "code": "combat/combo_direction", "enabled": false },
        { "code": "combat/style_mastery",   "enabled": false }
      ],
      "influences": [
        { "code": "combat/approach",   "available": true },
        { "code": "combat/retreat",    "available": true },
        { "code": "combat/use_stance", "available": true }
      ]
    }
  }
}
```

The manifest is computed once, cached in Redis, and re-computed when the underlying Seed capabilities change (via the `seed.capability.updated` event with a per-seed debounce). The manifest is pushed to the client through the Gardener service; the client renders accordingly.

### 4.3.3 Fidelity Levels Are Integers

A subtle but important design choice: **fidelity is quantized**. Seed depth is a continuous float (0.0-10.0+), but module fidelity is an integer (1-5). A combat stance selector at fidelity 3 shows 3 stance options; at fidelity 4, it shows 4 stance options; at fidelity 5, it shows the full tactical space.

This is intentional. Continuous fidelity would produce UI that feels inconsistent and hard to reason about. Quantized fidelity produces discrete behavior modes — the player sees clear transitions when they "level up" in a domain, and designers can author UX for each fidelity level without having to handle infinite gradations.

The quantization curve is per-module (defined in the module's `FidelityCurve`), so designers can tune when each fidelity transition happens for each module independently.

### 4.3.4 Compliance: The Character Can Say No

When a spirit executes an influence, the character's ABML behavior evaluates compliance before acting on the nudge. The compliance check considers:

- **`spirit.compliance_base`** — the character's general willingness to accept spirit influence, derived from Disposition's guardian feelings (trust, resentment, familiarity).
- **`spirit.influence.frequency`** — how often the spirit has been nudging recently; spamming reduces compliance.
- **`spirit.influence.resistance_buildup`** — accumulated resistance from frequent forced overrides.
- **Alignment with personality** — does the nudge align with the character's traits? A peaceful character nudged toward violence resists more than toward mercy.
- **Contextual appropriateness** — does the nudge make sense for the character's current situation?

If compliance passes, the character executes the nudged action (perhaps modified by personality — a fearful character nudged to "attack" may attack hesitantly). If compliance fails, the character ignores the nudge or actively resists, and the resistance is fed back to Disposition to update guardian feelings.

This is the mechanical implementation of the "character can refuse" commitment. It is not a hand-wave; it is a formal compliance evaluation with specific variables. A spirit that consistently nudges its character against the character's nature will find its relationship with the character deteriorating — and the compliance dropping — over time.

## 4.4 Domain-Specific UX Expansion

The Agency manifest expands progressively, but not uniformly. Different domains grow at different rates depending on where the spirit invests experience. The table below (summarizing PLAYER-VISION's treatment) shows how a representative subset of domains evolves:

| Domain | Early UX (seed depth ~0-2) | Developing UX (~2-5) | Mature UX (~5-10) |
|---|---|---|---|
| **Combat** | Single intention (approach / retreat / hold) | Stance selection, timing windows appear | Combo choreography direction, martial-style specialization |
| **Crafting** | Watch the character work | Material selection, technique choice | Quality targeting, process optimization |
| **Social** | Observe conversations | Tone suggestion, topic steering | Negotiation strategy, relationship management |
| **Trade** | Character handles autonomously | Price setting, partner selection | Inventory management, market analysis |
| **Exploration** | Directional drift | Route planning, landmark recognition | Danger assessment, resource tracking |
| **Magic** | Raw impulse | Stage-by-stage spellcasting control | Pneuma management, spell composition |

The domains are **independent**. A spirit at depth 7 in combat and depth 2 in social sees a rich combat UX and a minimal social UX. This independence is a critical feature, not a bug: it is what enables the "same world, different games" thesis from [§3.6](#36-same-game-many-games-as-a-deployment-truth). Different players end up with very different UX manifests because they have invested in different domains.

### 4.4.1 Cross-Seed Pollination

Each account has up to three concurrent seeds (account seeds — §4.8), and each seed has its own Seed growth. Agency applies **cross-seed pollination**: a spirit with deep combat experience from Seed 1 carries some combat agency into Seed 2, even though Seed 2's own combat growth is minimal.

The mechanism: when computing Seed 2's manifest, Agency multiplies Seed 1's relevant capability depths by a `CrossSeedPollinationFactor` (configurable, default 0.3) and merges them with Seed 2's native depths via `max()`. So Seed 2's effective combat depth is `max(seed2.combat, seed1.combat × 0.3)`.

This produces the design intent from PLAYER-VISION: accumulated experience crosses seed boundaries. A veteran player swapping seeds does not revert to a novice experience; their spirit is broadly capable, and each new seed benefits from the others' growth.

### 4.4.2 Character Overlay (Planned)

A further refinement: when a spirit inhabits a character that has its own Seed growth (e.g., a dungeon-master seed, or a realm-specific variant seed — §4.8), the character's own capabilities overlay the spirit's manifest. The character provides domain-specific capabilities the spirit's accumulated experience wouldn't reach — e.g., the dungeon-master seed provides dungeon-management UX modules that a non-dungeon-master spirit wouldn't see.

This enables scenarios where characters have capabilities the spirit doesn't have inherently. A dungeon-master character knows how to manage a dungeon even if the player's spirit has never done this before; the player receives the full dungeon-management UX while they inhabit that character.

## 4.5 Gardener: The Experience Orchestrator

### 4.5.1 What Gardener Does

The **Gardener service** (`lib-gardener`, L4) is the player-side counterpart to Puppetmaster (which orchestrates NPC experiences). Where Puppetmaster manages what NPCs encounter, Gardener manages what players encounter.

A "garden" in Gardener's terminology is an abstract conceptual space that a player inhabits — not a physical location, but a gameplay context. Every player is always in some garden; the garden determines what entities the player interacts with, what events reach them, and how the gardener behavior orchestrates their experience.

Garden types include (current and planned):

| Garden Type | Conceptual Space | Implementation Status |
|---|---|---|
| **Discovery** | The void from PLAYER-VISION — spirit drifting, encountering POIs, entering scenarios | Implemented |
| **Lobby** | Pre-game gathering (waiting room, character selection, loadout) | Planned |
| **In-Game** | Active gameplay (combat encounter, exploration, crafting session) | Planned |
| **Post-Game** | Results and transitions (score screen, growth awards, next-scenario selection) | Planned |
| **Housing** | Player-owned persistent space | Planned |
| **Cooperative** | Shared multi-player space | Planned |

Each garden has a **seed** (its state, capabilities, progression), **entity associations** (characters, inventories, collections, currencies available in this context), and a **gardener behavior** (an ABML document, or manual API calls, controlling what the player experiences).

### 4.5.2 Gardener Uses the Same Divine Actor Pattern as Puppetmaster

The Gardener and Puppetmaster services are architecturally twins. Both orchestrate long-running behavior documents executed by the Actor runtime (L2). The difference is the scope of orchestration:

- **Puppetmaster** tends physical realm regions. Its god-actors (regional watchers) monitor NPC events and orchestrate realm-level content.
- **Gardener** tends per-player conceptual gardens. Its divine actors (gardener god-actors) monitor player drift and events and orchestrate player-level experiences.

The infrastructure is shared. The ABML execution engine is shared. The dynamic character binding pattern ([§5](#5-the-unified-cognitive-progression--system-realms)) works for both. The only difference is *what they pay attention to* and *what levers they pull*.

This is why the architecture can claim "a god tending a player's experience is the same operation as a god tending a realm region" — from the god's perspective, both are ABML behavior documents running on Actor with different tool sets. The Gardener service provides the tools (garden primitives, POIs, scenarios, entity associations); the god-actor's ABML determines when and how to use them.

### 4.5.3 The Entity Session Registry

One of Gardener's critical architectural contributions (in the target design) is as the primary registrar for entity-to-session mappings. When a player inhabits a character, Gardener registers `(character, characterId) → sessionId` in the **Entity Session Registry** (hosted by Connect, L1). When the player switches characters, Gardener updates the registration.

This enables entity-based services (Status, Currency, Inventory, Collection, Seed, etc.) to publish client events to the right WebSocket sessions without having to know which player cares about which entity. When Status grants a buff to character X, Status queries the registry: `GetSessionsForEntity(character, X) → {sessionIds}`, publishes a client event to those sessions, and the event flows to the right players.

This is how UX manifest updates, influence responses, buff/debuff indicators, inventory changes, and all other "the player needs to see this" events route correctly in a dynamic multi-seed, multi-character environment.

## 4.6 The Void Experience

### 4.6.1 First Contact

Per PLAYER-VISION's opening scene:

> Upon first login, there is no character creation screen, no menu, no tutorial prompt. The player exists as a spirit — a point of light, a divine shard of Nexius — floating in a void space. Ambient, minimal. A prompt appears with arrows in cardinal directions, flashing slowly, indicating that movement is possible.

The void is the **Discovery garden** — the first garden every player occupies. Its implementation is the most mature garden type in the current Gardener service.

The void serves several functions at different deployment stages:

- **Alpha**: Isolated sandbox showcases. Each POI (Point of Interest) is a self-contained mini-game or mechanic demo. The spirit drifts, encounters POIs, accepts scenarios, experiences contained content. Scenarios are isolated instances with no persistent world state.
- **Beta**: Windows into the world. Some scenarios connect to slices of real realm state. The spirit enters with specific objectives, interacts with living NPCs, and is ejected back to the void. The content flywheel begins turning.
- **Release**: Persistent entry point. Some scenarios lead to permanent inhabitation. The spirit selects a growing seed and drops into the persistent world. The void remains as starting point, lobby, decompression space, and seed selection.

### 4.6.2 POI Mechanics

When the spirit drifts in the void, the gardener behavior spawns **POIs** (Points of Interest) along its path. Each POI is a scenario opportunity — a visual anomaly, an environmental shift, a sound or music chase, a portal. The spirit can:

- **Approach and enter** (proximity trigger) — drop into the scenario.
- **Acknowledge a prompt** (prompted trigger) — confirm entry before dropping in.
- **Avoid** (implicit decline) — drift past without engaging.
- **Explicitly decline** (if the POI presents a choice).

POI selection uses a weighted scoring algorithm combining:

- **Affinity**: How well the scenario's domain weights match the spirit's accumulated growth profile. A combat-grown spirit sees more combat POIs.
- **Diversity**: Penalty for recently-seen templates — prevents repetition.
- **Narrative**: Response to the spirit's current drift pattern — a hesitant spirit sees easier/gentler options, a directed spirit sees more challenging options.
- **Random**: A small weight for discovery and serendipity.

This produces a void experience that feels responsive to the spirit's emerging preferences without ever becoming predictable. The gardener behavior is continuously reading the spirit's actions — where it drifts, what it approaches, what it avoids, how long it spends in each scenario — and curating the next wave of POIs accordingly.

### 4.6.3 The Void Persists

Per PLAYER-VISION's alpha-to-release gradient: *the void never goes away*. Post-release, it remains as:

- **Starting point**: Every session begins here. The spirit orients, chooses a seed, drops in.
- **Lobby**: Between active gameplay sessions, a meditative floating space.
- **Mini-game arcade**: POIs and scenarios continue to appear. Some players spend significant time here — that's fine; they're generating spirit metadata and contributing to analytics.
- **Decompression**: A break from the intensity of the living world.
- **Seed selection**: Choose which account seed to enter.

Architecturally, this means the Gardener service's current "destroy garden on scenario entry" behavior must evolve to "garden-to-garden transitions where the void persists as parent context." This is a planned but not yet implemented architectural target. When complete, the player moves smoothly between gardens (discovery → lobby → in-game → post-game → discovery) without the void ever being discarded.

## 4.7 Pair Bonds: Twin Spirits

### 4.7.1 The Rynax-Inspired Pair System

A specific subset of players — "paired" — begin as **twin spirits**. Drawing inspiration from *Kurau: Phantom Memory*'s Rynax (sentient energy beings that exist exclusively in pairs), paired spirits are metaphysically linked from creation: complementary halves of a single Nexius shard rather than independent spirits.

Paired spirits have properties that normal spirits do not:

- **Intrinsic communication**: The pair can communicate across distance and realm boundaries without in-game tools. Early on this is vague (emotional impressions, directional intent); with shared experience it becomes richer (specific messages, coordinated intentions).
- **Linked households**: Their first-generation characters begin in proximity with established social connections — neighboring families, childhood friends, trade partners.
- **Narrative branching**: Key moments present the pair with explicit choices about their relationship's direction — alliance, rivalry, friendly competition, protective guardianship. None is forced; all are valid. The metaphysical bond persists regardless of the social direction.
- **Shared void**: Twin spirits can drift through the void together and enter scenarios cooperatively.

### 4.7.2 Why Pairs Exist

The pair system addresses the 95% case of cooperative multiplayer without requiring both players to independently grind through the progressive agency system to unlock explicit social mechanics.

Consider the problem: two friends buy the game. One has 200 hours of playtime; one has 20. The experienced player's spirit has rich social agency; the new player's spirit has barely any. Traditional multiplayer would put them in a party. Arcadia's progressive-agency model makes that feel wrong — the new spirit hasn't earned the coordination-level mechanics.

The solution: twin spirits start bonded. Their communication channel exists from the beginning because it is metaphysical, not technological. They do not need to unlock social systems to find each other; the pair bond IS the social system for these two spirits, existing outside the normal progression.

### 4.7.3 Pair Bonds Do Not Replace Normal Social Systems

Twin spirits are a narrow case for two co-creating players. The general social system — Chat, Lexicon-validated messaging, Hearsay propagation, faction politics, relationship networks ([§10](#10-the-social-fabric--cultural-emergence)) — applies to all players, paired or not. A paired spirit still develops its social agency through play like any other spirit. The pair bond is an *additional* channel, not a replacement.

This preserves both design goals: progressive agency is the default path for ordinary multi-player interaction; pair bonds are a special case for two players whose real-world relationship predates their in-world relationship, and whose coordination should not require in-world justification.

| Aspect | Pair Bond | Normal Social |
|---|---|---|
| **Availability** | From creation | Earned through progressive agency |
| **Communication** | Innate (metaphysical) | Requires in-game tools/enchantments |
| **Range** | Unlimited (same Nexius shard) | Limited by in-game distance/technology |
| **Discovery** | Automatic (always know where pair is) | Requires social UX modules |
| **Limit** | One pair per spirit | Expands with social agency growth |
| **Persistence** | Permanent and unbreakable | Relationships form and dissolve |

## 4.8 Multi-Seed Accounts

### 4.8.1 Up to Three Concurrent Relationships

Each account supports up to **three account seeds** — parallel relationships between the guardian spirit and the game world. These are not "alt characters" in the traditional sense. Each seed represents a fundamentally different mode of engagement with the simulation.

Canonical seed types include:

1. **Standard Guardian Spirit**: The default Arcadia experience. Manage a household across generations. Characters age, marry, have children, die. The content flywheel turns. Progressive agency deepens across lifetimes.

2. **Dungeon Master**: The spirit IS a dungeon. Your "household" is your dungeon ecosystem — spawned creatures, traps, environmental features, memory manifestations. You perceive adventurers, orchestrate encounters, grow chambers. This maps directly to the Dungeon-as-Actor pattern developed in [§13](#13-actor-bound-entities-living-places-and-things).

3. **Realm-Specific Variants**: A fundamentally different relationship to a specific realm. Examples include a merchant-dynasty seed focused on economic simulation, a god-shard seed (mini-regional-watcher) operating at broader scale than individual characters, or an Omega-realm seed with diegetic VR-machine configuration mechanics.

The three-seed limit is an architectural choice — it allows meaningful parallel relationships without fragmenting the spirit's attention across too many contexts.

### 4.8.2 Seed Independence and Cross-Pollination

Each seed:

- Has its own household / entity state (characters, inventories, currencies, all scoped to the seed's target realm).
- Can target any realm (including the same realm as another seed, if desired).
- Develops its own UX capability manifest based on engagement within that seed.
- Contributes to the overall spirit's experience pool (cross-pollination — §4.4.1).

The spirit's accumulated understanding crosses seed boundaries. A spirit with deep combat experience from Seed 1 carries that combat agency into Seed 2's dungeon master mode — perhaps manifesting as better combat choreography for spawned encounters. A spirit with deep social mastery from Seed 1 carries social agency into Seed 3's merchant dynasty — perhaps manifesting as richer trade-negotiation UX.

### 4.8.3 Void as Seed Selection Point

Post-release, the void (Discovery garden) serves as the seed selection point:

```
   1. Player connects. Spirit enters void.
          │
          ▼
   2. Void presents representations of active seeds (up to 3).
          │
          ▼
   3. Spirit drops into one — or drifts, plays mini-games, explores scenarios.
          │
          ▼
   4. Session ends. Spirit returns to void.
          │
          ▼
   5. Spirit selects another seed, or logs off.
```

This diegetic representation of seed selection means the transition between parallel game-world relationships feels like a natural part of the void experience, not a metagame menu. The void is the meta-layer for multi-realm play, and seed selection is just one of the actions available within it.

## 4.9 How It All Connects

Stepping back, the guardian spirit system weaves together an unusual number of services to produce the player experience. Summarizing the key dependencies:

```
Account (L1)
   │ owns
   ▼
Guardian Spirit Character in NEXIUS system realm (L2)
   │
   ├── Seed (L2) ◄── accumulates growth from play
   │       │
   │       └── feeds → Agency (L4) ◄── computes UX capability manifest
   │                        │
   │                        ├── publishes manifest via agency.manifest.updated
   │                        ▼
   │                   Gardener (L4) ◄── pushes manifest to client via sessions
   │                        │
   │                        └── orchestrates gardens, POIs, scenarios
   │
   ├── Character Personality (L4) ◄── personality traits for guardian character
   ├── Character Encounter (L4) ◄── memories of inhabited characters
   ├── Relationship (L2) ◄── bonds with inhabited characters, pair bonds
   │
   ├── Disposition (L4) ◄── guardian feelings toward inhabited character
   │                           (trust, resentment, familiarity)
   │
   └── inhabits → Character in a game realm (L2)
           │
           ├── ABML behavior (L4 via Behavior)
           │       │
           │       └── consumes ${spirit.*} variables via Agency's provider
           │             (compliance, domain fidelities, influence history)
           │
           └── executes Actor cognition (L2)
```

Every arrow in this diagram is a real API call or DI provider registration. The player experience is not a monolithic "spirit system" — it is an emergent property of these services working together, each contributing its own specialty. This is the architecture paying off: no single service "does" the guardian spirit, but all of them together produce the experience described in PLAYER-VISION.md.

The subsequent sections develop the infrastructure this section has leaned on. [§5](#5-the-unified-cognitive-progression--system-realms) develops system realms in full (including why NEXIUS-as-character-host works architecturally). [§6](#6-the-npc-intelligence-stack) develops the ABML/GOAP cognition stack that inhabited characters run. [§9](#9-the-economic-substrate) develops the economy that inhabited characters participate in. [§10](#10-the-social-fabric--cultural-emergence) develops the Lexicon/Hearsay/Disposition social layer that structures all communication. [§13](#13-actor-bound-entities-living-places-and-things) develops the actor-bound entity pattern that system realm entities (including guardian spirits) inherit.

Progressive agency — the thesis that agency is earned context, that the character is always autonomous, that the spirit collaborates rather than controls — is the lens through which every subsequent section should be read.

---

# 5. The Unified Cognitive Progression & System Realms

## 5.1 The Single Pattern for Every Autonomous Entity

Arcadia contains many entities that begin as inert objects and grow into autonomous agents. A dungeon begins as a location and becomes a sentient place with personality and memory. A weapon begins as an item and becomes a living partner with opinions. A god begins as an unclaimed domain and becomes a curated divine presence. A guardian spirit begins as a nearly-inert fragment of Nexius and becomes a sophisticated collaborator with its characters.

Each of these could be implemented as a custom system with its own lifecycle, its own storage, its own actor management, its own character creation. Most game engines do exactly that — and end up with dungeon code that looks nothing like weapon code that looks nothing like god code, each with its own bugs and its own integration surface.

Arcadia takes a different approach. Every one of these entity types follows **a single unified cognitive progression pattern**, implemented once and configured per entity-type via templates. This section develops:

- The three cognitive stages ([§5.2](#52-the-three-cognitive-stages)) through which any such entity progresses.
- The system realms that host these entities as first-class Characters ([§5.3](#53-system-realms)).
- The Genesis plugin that implements the pattern as reusable infrastructure ([§5.4](#54-the-genesis-plugin)).
- The consequences of unification — why this collapses dozens of integration questions into one, and why living weapons can exist with zero new plugins ([§5.5](#55-why-this-collapses-integration-questions)).

## 5.2 The Three Cognitive Stages

Every actor-bound entity progresses through the same three stages. The transitions are driven by a single signal: **currency wallet credits**. Credits accumulate in the entity's wallets; Genesis converts credits to Seed growth via template-defined mappings; Seed growth crosses phase thresholds; phase transitions trigger stage-appropriate infrastructure provisioning.

### 5.2.1 Stage 1: Dormant (No Actor)

The entity exists as a database record — a Genesis entity with an internal seed, a currency wallet or two, and optionally an inventory. It has a **physical form** (an item instance, a location, or nothing at all) that serves as its representation in the world. But no Actor is running. No ABML is being evaluated. No decisions are being made.

Growth accumulates passively. A treasure chest's mana wallet fills through ambient autogain; a dungeon's wallet fills from deaths within its domain; a weapon's experience wallet fills when its wielder fights; a god's domain wallet fills from relevant worship events. The entity "exists" but does not "think."

**System cost**: near zero. An Arcadia realm can have thousands of dormant genesis entities simultaneously — abandoned weapons in dungeons, neglected artifacts in museums, sleeping dungeons beneath cities, unclaimed divine domains — each costing only a database row and an occasional currency credit.

This matters because it makes Arcadia's world **pregnant with potential agents**. Every sword that has seen enough battles is a potential sentient weapon. Every location where enough deaths have occurred is a potential dungeon. Every domain where enough relevant events have happened is a potential deity. The world is populated with entities that might wake up someday, and the cost of having them there is trivial.

### 5.2.2 Stage 2: Stirring (Event Brain Actor)

When currency accumulation drives the internal seed past the Stirring threshold, **Genesis automatically spawns an Actor** (L2) with the entity's pre-compiled ABML behavior. The entity can now perceive, decide, and act. It has autonomous agency — but without the rich cognitive data that full character identity provides.

ABML expressions like `${personality.aggression}` resolve to null because there is no Character yet, no CharacterPersonality record, no encounter memories, no history. The behavior document, however, is **null-safe**: expressions that reference character data fall through to default paths that use instinct-level reasoning.

A stirring-phase living weapon might:

- Perceive combat events and respond with vague impulses to its wielder ("danger ahead" as an emotional perception injection).
- Make simple decisions based on its Seed capabilities (unlock passive bonuses to stats, weight certain actions).
- Accumulate encounter records against the wielder even before it has formal memory infrastructure.

The entity is aware but not yet self-aware. It reacts to the world but does not yet have the reflective capacity that characters have.

### 5.2.3 Stage 3: Awakened (Character Brain Actor)

When the seed reaches the Awakened threshold, **Genesis automatically**:

1. Creates a **Character record** in the template-specified system realm (e.g., SENTIENT_ARMS for a weapon, DUNGEON_CORES for a dungeon).
2. Seeds personality traits from the template's `initialPersonalityTraits`.
3. Calls `Actor.BindCharacter(actorId, characterId)` — binding the running Actor to the new Character **without relaunching the behavior**.
4. Publishes `genesis.entity.phase-changed`, notifying any subscribing services that this entity has awakened.

The critical detail: the ABML behavior document **does not change** at awakening. It is the same behavior that ran at Stirring. The difference is that expressions like `${personality.patience}` and `${encounters.wielder}` now return real values instead of null. On the next behavior tick, the rich cognitive variable providers become available, and the entity's reasoning grows accordingly sophisticated.

An awakened living weapon can:

- Recognize its wielder specifically. Form opinions about them. Be angry or pleased based on how they have been treated.
- Develop preferences based on its accumulated history. A weapon that has spent centuries in battle may have shifted from whatever personality it started with to something shaped by its experiences.
- Speak to its wielder (via structured perception injection using the Lexicon vocabulary — §10).
- Refuse actions that violate its values. An `active.refuse` capability, gated by high enough wielder-bond and personality alignment, allows the weapon to decline being used against its principles.
- Form relationships with other entities in its vicinity — other weapons, other characters, the dungeon it currently resides in.

The entity is now a full character in the Bannou architectural sense. Every service that works with characters works with it.

### 5.2.4 The Stage 3+ Horizon

Most entities can continue growing after awakening. Seeds typically define phases beyond "Awakened":

- **Ancient** (or equivalent): Rich inner life. Personality evolution through experiences. Weapons that have lived through many wielders, dungeons that have witnessed centuries of adventurers, gods who have observed civilizations rise and fall. These entities carry deep backstory as mechanical state.
- **Legendary / Named**: The entity has become historically significant. Its archives feed the content flywheel directly — stories are told about it, quests reference it, its eventual destruction (if it ever occurs) is an event of narrative weight.

These later phases are not qualitatively different from Stage 3 in terms of infrastructure — the same Character, same Actor, same variable providers. What differs is the quantitative depth of accumulated state: richer personality traits, longer encounter histories, more complex relationships, deeper Seed capabilities.

## 5.3 System Realms

### 5.3.1 The Architectural Keystone

Per [§3.2.3](#323-system-realms-the-issystemtype-flag), a **system realm** is a realm with `isSystemType: true`. System realms are conceptual namespaces for metaphysical entities — they are not game-playable in the traditional sense, but the Characters within them are fully real entities using all normal Character infrastructure.

The design consideration note in the Realm plugin's deep dive states the case plainly:

> System realms are an architectural keystone. The `isSystemType` flag on realms enables the actor-bound entity pattern — the single most powerful architectural pattern in Bannou. System realms are non-physical conceptual spaces where metaphysical entities exist as first-class Characters, gaining the entire L2/L4 entity stack (personality, memory, history, growth, bonds, cognition) for free.

This is what "architectural keystone" actually means: every L2 and L4 service that operates on Characters operates on system realm characters the same way. CharacterPersonality stores personality traits for a sentient weapon the same way it stores them for a farmer. CharacterEncounter stores memories the same way. Relationship tracks bonds the same way. Collection tracks knowledge the same way. The Actor runtime executes ABML cognition for a dungeon the same way it executes cognition for a shopkeeper.

The result: **thousands of service interactions that would require custom code if implemented ad-hoc instead compose automatically.**

### 5.3.2 The Inventory of System Realms

The planned system realms are:

| System Realm | Purpose | Characters Within | Seed Data Category |
|---|---|---|---|
| **PANTHEON** | Gods and deities. Hosts god-actors that orchestrate the content flywheel, curate regional flavor, and issue blessings. | Divine characters, one per deity | Template: `deity_domain` |
| **NEXIUS** | Guardian spirits. Hosts one character per player account — the spirit's own identity. | One spirit character per account | Template: `guardian_spirit` |
| **DUNGEON_CORES** | Sentient dungeons. Hosts dungeon-entity characters with mana economies, bound dungeon-masters, and spatial domains. | One character per awakened dungeon | Template: `dungeon_core` with personality sub-types |
| **SENTIENT_ARMS** | Living weapons. Hosts weapon-entity characters with wielder bonds, accumulated combat history, and personality shaped by their wielders. | One character per awakened weapon | Template: `sentient_weapon` |
| **UNDERWORLD** | Dead characters. Post-mortem inhabitation for characters who have died in game realms. Feeds the content flywheel back to the living. | One character per post-mortem entity | (via character lifecycle on death) |

Additional system realms can be added per game service — a specific deployment might add `SENTIENT_CONTAINERS` for growing treasure chests, `AWAKENED_FORESTS` for sapient wilderness, `MACHINE_SPIRITS` for enchanted factories, or any other category of metaphysical entity the game's design requires. The infrastructure is identical; only the seed data differs.

### 5.3.3 System Realms and the Flat-Realm Architecture

Despite their metaphysical role, system realms are architecturally identical to game realms. They are stored in the same Realm entity table, differentiated only by the `isSystemType: true` flag. They are not "above" game realms; they are peer realms that happen to host different kinds of inhabitants.

This matters because:

- Services that iterate or query realms treat them uniformly. `ListRealms` returns system realms and game realms alike (unless explicitly filtered). Cross-service integrations (e.g., Puppetmaster's realm-watcher lifecycle) use the same `realm.created/updated/deleted` events.
- System realms are subject to the same deprecation and merge lifecycle — though system realm *merge* is explicitly guarded against in the Realm plugin because the semantics are unclear.
- System realm Characters are characters. They have character IDs, appear in `CharacterExistsBatch` responses, can be referenced by Relationship instances, participate in Contracts if the game design requires.

The flat-realm design's payoff is that system realms slot into it with minimal ceremony.

### 5.3.4 The Shared-System-Realm Pattern

A typical deployment has **one instance of each system realm**, shared across all game realms. PANTHEON is the pantheon for Fantasia-A and Fantasia-B and Arcadia-City — the same gods interfere in all of them. UNDERWORLD is the afterlife for all of them — characters from any game realm go there when they die.

This is architecturally natural (system realms are flat peers with game realms; nothing requires a one-to-one mapping) and narratively rich. Gods that interfere across multiple realms develop comparative personalities. The afterlife becomes a mixing space for characters from different life-realms. Cross-realm narratives become possible.

A deployment *could* have per-game-realm system realms (one PANTHEON per game realm), but the more interesting design is the shared model. Arcadia's canonical design assumes shared.

## 5.4 The Genesis Plugin

The Actor-Bound Entity pattern is implemented in **`lib-genesis`** (L2 GameFoundation), which reifies the pattern as reusable infrastructure.

### 5.4.1 What Genesis Provides

Genesis encapsulates the three-stage lifecycle behind a single `CreateEntity` API call:

- **Templates** (seed data): Definitions of entity types — what currencies they accumulate, what growth domains those currencies feed, what inventories they own, what ABML behavior they run at each stage, what system realm they awaken into, what personality traits they initialize with.
- **Entity creation**: A single call with a template code provisions the internal seed, currency wallets (via `lib-currency`), inventories (via `lib-inventory`), and resource cleanup registrations. Returns an entity record that is the caller's handle.
- **Growth via currency**: The only way to grow a genesis entity is to credit its currency wallet. An `ICurrencyTransactionListener` DI interface fires when wallets change; Genesis converts credits to seed growth via template-defined mappings; the internal Seed records growth via `Seed.RecordGrowthBatch`.
- **Automatic progression**: An `ISeedEvolutionListener` fires when the seed crosses phase thresholds. Genesis spawns the Actor at Stirring, creates the Character and binds the Actor at Awakened — all automatically, without callers needing to coordinate.
- **Events**: Publishes `genesis.entity.created`, `genesis.entity.phase-changed`, and `genesis.entity.deleted` for any subscribers (domain plugins, analytics, content flywheel).
- **Cleanup**: Archives on destruction by default (feeding the content flywheel). Uses `lib-resource` for coordinated cross-service cleanup via CASCADE policies.

The caller's API surface collapses dramatically. Before Genesis, creating a dungeon required 6 cross-service calls; with Genesis, 2 calls (create genesis entity, bind physical form, done). Phase transitions that previously required 3-5 manual calls each happen automatically; domain plugins only handle domain-specific work on transition events.

### 5.4.2 Seed Encapsulation

An important property: **the seed is fully encapsulated inside Genesis**. The internal `seedId` never appears in any Genesis API response. Callers cannot discover the seedId through Genesis. All growth flows through currency wallet credits — the seed is an implementation detail of the awakening lifecycle, not a public contract.

This encapsulation is important because it prevents a common design failure: consumers poking directly at the seed and bypassing the template's growth-mapping logic. By forcing all growth through the wallet-credit path, Genesis ensures that growth always respects the template — the right domains receive the right amounts, at the right ratios, for the right entity type.

A narrow exception exists: when a caller brings its own seed to Genesis (via the nullable `seedId` parameter on create), Genesis adopts it rather than provisioning one internally. This is used by specific domain plugins (notably Divine) that need to create Seed bonds on genesis-managed seeds — the caller already holds the seedId because they created it; Genesis doesn't leak it.

### 5.4.3 Templates as Configuration

A Genesis template is seeded data — configuration, not code. A new entity type requires:

1. Defining the template (YAML document registered via the Genesis API at startup).
2. Authoring the pre-compiled ABML behavior for each phase.
3. If needed, creating a domain plugin to handle domain-specific orchestration.

No Genesis code changes are required. A game designer who wants to add "awakened fountains" as a new entity type defines a `fountain_spirit` template, writes the Stirring and Awakened ABML behaviors, and the infrastructure handles everything else.

### 5.4.4 Physical Form Flexibility

A subtle but powerful property: **the seed is agnostic to the entity's physical form**. Genesis tracks a `physicalFormType` (Item, Location, or None) and an optional `physicalFormId` reference, but these are bindings, not identity constraints.

A dungeon core's physical form is typically a Location (the dungeon space in the world). But if someone extracts the dungeon core as an item and transports it elsewhere, the genesis entity continues to exist, the wallet continues to accumulate, the seed continues to grow, and the actor continues to behave — with its ABML behavior now reasoning about being "in transit" rather than "anchored."

A living weapon's physical form is typically an Item. But if the item is destroyed and the Character Brain actor is still alive, the behavior can respond to "I have no physical form" — perhaps seeking rebinding to a new item, or becoming a ghost that haunts wielders.

This flexibility is what produces the most surprising emergent content. The seed grows into whatever the ABML behavior says it grows into, unconstrained by what the physical form "usually" does. The template, currency flows, and ABML behaviors determine what a seed grows into — not the physical form of the entity. Sometimes you'll be surprised what kind of thing a seed grows into.

### 5.4.5 Deferred Bond Pattern

Genesis supports creating bonds (relationships to partner entities) at any cognitive stage, but the actual Relationship (via `lib-relationship`) is only created when the entity reaches Character Brain. Before then, Genesis stores *bond intent* on the entity record — the target entity type and ID are known, and `${genesis.bond.*}` variables provide them to ABML behaviors, but no Relationship instance exists yet.

This solves a bootstrapping problem: Relationships require characters on both sides, but Stirring-phase entities have no Character yet. The deferred pattern lets bond intent be established early (a weapon equipped to a wielder, a dungeon-master contracting with a dungeon) while postponing the Relationship materialization until the character exists.

Once awakened, Genesis automatically creates the Relationship using the stored bond intent, and the `${relationship.*}` variable provider activates for richer relationship data (sentiment, depth, history). The ABML behavior does not need to be aware of this transition — it uses `${genesis.bond.*}` at Stirring and `${relationship.*}` after awakening without changing.

## 5.5 Why This Collapses Integration Questions

### 5.5.1 The Single-Answer Property

A common question during early Arcadia design might be: "How does X integrate with Y?" For example:

- How does a dungeon participate in Faction politics?
- How does a sentient weapon form relationships with its wielder?
- How does a god remember specific significant worshippers?
- How does a dungeon appear in Location queries?
- How does a weapon's personality affect combat decisions?
- How does a god's history get recorded for future narrative generation?

Without the unified pattern, each question would require a custom answer — a new API, a new event, a new field on the dungeon/weapon/god data model.

With the unified pattern, **every such question has the same answer**:

> X is a Character in a system realm. Normal character infrastructure applies.

- Dungeon and Faction? The dungeon's Character in DUNGEON_CORES joins a Faction like any other character. `lib-faction` doesn't need to know about dungeons specifically.
- Weapon and wielder relationship? A Relationship instance of type `weapon_wielder` between the weapon's Character in SENTIENT_ARMS and the wielder's Character in their game realm.
- God remembering worshippers? CharacterEncounter records, stored and queried the same way as any other character's memories.
- Dungeon in Location queries? If the dungeon has bound a Location as its physical form, standard Location queries find it.
- Weapon personality affecting combat? CharacterPersonality variable providers feeding the weapon's ABML, same as any other character's personality feeds their ABML.
- God history for narrative generation? CharacterHistory records, compressed by Resource on destruction (or snapshot), consumed by Storyline like any other character's history.

This is the payoff. The architecture that took patient effort to design (system realms, Genesis, the variable provider factory pattern, all the L2/L4 character services) now does enormous amounts of work automatically. Every new entity type that can be modeled as "character in a system realm" inherits the full infrastructure for free.

### 5.5.2 Living Weapons: The Zero-Plugin Validation

The strongest validation of the composability thesis is the living weapon case. Consider the requirements for a weapon that is:

- Physical (an item that can be held, equipped, traded, lost).
- Mechanically progressing (grows from novice to legendary through combat experience).
- Cognitively progressing (sleeps at first, stirs eventually, awakens fully sentient).
- Personality-bearing (has traits, has opinions, has tastes).
- Memory-bearing (remembers past wielders, remembers battles, carries grudges).
- History-bearing (has a backstory that matters for storyline generation).
- Bond-forming (develops relationships with current wielders that deepen or sour).
- Effect-granting (provides passive and active capabilities to its wielder).
- Voice-having (communicates via perception injection as its understanding grows).
- Culturally-significant (can appear in Storyline-generated narratives, in Realm History, in NPC gossip).

In most game architectures, implementing all of this would require a dedicated living-weapon subsystem — thousands of lines of code to track weapon personality, bond state, memory, history, dialog, growth.

In Arcadia, living weapons require **zero new plugins**. Every requirement maps to an existing service:

| Requirement | Service | How |
|---|---|---|
| Physical item | `lib-item` (L2) | Item instance with Genesis binding its form |
| Mechanical progression | `lib-genesis` (L2) | Template `sentient_weapon` with currency wallet and growth mappings |
| Cognitive progression | `lib-genesis` (L2) + `lib-actor` (L2) | Automatic via Genesis lifecycle |
| Personality | `lib-character-personality` (L4) | Character in SENTIENT_ARMS with personality record |
| Memory | `lib-character-encounter` (L4) | CharacterEncounter records like any character |
| History | `lib-character-history` (L4) | CharacterHistory records like any character |
| Bonds | `lib-relationship` (L2) | RelationshipInstance of type `weapon_wielder` |
| Capabilities | `lib-status` (L4) | Status effects granted based on Seed capabilities |
| Voice | `lib-chat` (L1) + `lib-lexicon` (L4) | Structured messages in the appropriate room type |
| Cultural significance | `lib-storyline` (L4), `lib-realm-history` (L4) | Same compressed-archive pipeline as any character |

The only code that needs to be written is:

- The Genesis template (seed data).
- The ABML behavior documents (authored content, not code).
- The game engine's side of crediting the weapon's experience wallet when combat occurs.

This is the strongest possible form of the composability thesis: a non-trivial game feature (living weapons with deep personality and history) requires no new Bannou plugins. It composes entirely from primitives that exist for unrelated reasons.

Dungeons, by contrast, *do* have a dedicated plugin (`lib-dungeon`, L4). But the dungeon plugin is thin: it handles dungeon-specific orchestration (spawn monsters, activate traps, manage spatial domains) on top of Genesis's entity-awakening substrate. Without Genesis, the dungeon plugin would be significantly larger. With Genesis, the dungeon plugin is a ceremony layer, and most of dungeon functionality comes from the underlying pattern.

### 5.5.3 The Comparative Table

| Aspect | Divine Actor | Dungeon Core | Living Weapon |
|---|---|---|---|
| **Plugin** | `lib-divine` (L4) on `lib-genesis` (L2) | `lib-dungeon` (L4) on `lib-genesis` (L2) | `lib-genesis` (L2) alone |
| **Physical form** | None (immaterial observer) | Location (spatial domain) | Item instance |
| **System realm** | PANTHEON | DUNGEON_CORES | SENTIENT_ARMS |
| **Template** | `deity_domain` | `dungeon_core` with personality subtypes | `sentient_weapon` |
| **Economy** | Divinity currency | Mana currency | Experience currency |
| **Bond type** | Relationship (many followers) | Contract (one master) | Relationship (one wielder) |
| **Bond cardinality** | Many | One | One |
| **Cognitive stages** | Event Brain → Character Brain | Dormant → Event Brain → Character Brain | Dormant → Event Brain → Character Brain |
| **Personality expression** | Blessings, interventions, aesthetic preferences | Trap placement, creature choice, layout design | Combat advice, refusal, protection, resonance |
| **Needs domain plugin?** | Yes (complex orchestration: divinity economy, blessings, followers) | Yes (complex orchestration: spatial, mana, creatures, masters) | No (game engine + ABML sufficient) |

The pattern is uniform at the infrastructure level. The domain plugins above Genesis exist only for each entity type's specific ceremony — and only when that ceremony is complex enough to warrant server-side orchestration.

### 5.5.4 What This Buys Design-Wise

The unification has three design consequences that ripple throughout Arcadia:

**Fast iteration on new entity types.** A designer who wants to add "awakened ships" or "sentient machines" or "living libraries" does not start from a blank plugin skeleton. They write a Genesis template, author ABML behaviors, and ship. The feature exists in days, not months.

**Rich cross-entity interactions for free.** Because all actor-bound entities are Characters, they interact with each other through normal Character infrastructure. A dungeon and a sentient weapon in the same region have a Relationship. A god and a dungeon master have a Contract. A living weapon in a dungeon's inventory has a memory of that dungeon. None of these interactions require special code.

**Content flywheel applies uniformly.** Every actor-bound entity's eventual destruction feeds the content flywheel. A weapon that shatters in battle, a dungeon that collapses, a god that fades — all produce compressed archives, feed Storyline generation, and seed future content. Arcadia's living world is populated by entities whose legacies contribute to the next generation of narratives.

## 5.6 Connecting to the Rest of the Document

This section provides the mechanical foundation for much of what follows. Specifically:

- [§7 (Content Flywheel)](#7-the-content-flywheel) — actor-bound entities feed the flywheel through their lifecycles.
- [§11 (Combat & Cinematic)](#11-combat--cinematic-choreography) — sentient weapons and other combatants participate in cinematic choreography through their Character's variable providers.
- [§12 (Procedural Creative Generation)](#12-procedural-creative-generation) — the creative SDKs that generate Genesis behaviors and scenarios.
- [§13 (Actor-Bound Entities)](#13-actor-bound-entities-living-places-and-things) — deep development of specific entity types (dungeons, weapons, gods, sacred sites) using this pattern.
- [§14 (Equipment & Technology)](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology) — living weapons' relationship to equipment enchantment, including pre-sentient intent-channeling.
- [§§15-18 (Realm Design Guides)](#part-iv--realm-design-guides) — how realm designers configure system realms per their specific game.

The rule of thumb when reading subsequent sections: whenever the text mentions a metaphysical entity — a god, a dungeon, a sentient weapon, a spirit of place, an awakened artifact — it is describing a Character in a system realm, following the three-stage progression, managed by the Genesis pattern, composing existing services. The surface varies; the substrate is this.

---

# 6. The NPC Intelligence Stack

## 6.1 The Problem: Autonomous Minds at Scale

Arcadia's vision requires 100,000+ concurrent AI NPCs making decisions every 100-500ms — pursuing their own aspirations, remembering interactions, forming relationships, running businesses, generating emergent stories whether or not players are present. This is not a nice-to-have. Without it, "the world is alive whether or not a player is watching" is marketing; with it, that claim becomes a mechanical fact.

Producing autonomous cognition at this scale is a hard engineering problem. Each NPC must have:

- **Reactive responsiveness** — the ability to respond quickly to environmental perceptions (someone attacked me, my friend died, a stranger entered).
- **Deliberative depth** — the ability to pursue long-term goals by planning action sequences, not just reacting.
- **Rich personality** — traits, preferences, skills, memories, relationships that make the NPC a *specific* individual, not a generic body.
- **World-awareness** — access to the game world's state (location, time, economy, factions) to contextualize decisions.
- **Affordable cost** — negligible per-NPC overhead, or 100K NPCs becomes catastrophic.

These requirements often pull in opposite directions. Reactive systems are fast but shallow. Deliberative systems are deep but slow. Rich personality implies significant per-NPC state. Scale implies minimizing per-NPC state.

Arcadia's NPC intelligence stack resolves these tensions through a specific architecture: a dual reactive/deliberative cognition model (ABML + GOAP), a layered data flow (the 14+ variable providers), a scaling architecture (horizontal actor pools with direct-RabbitMQ perception delivery), and an orchestration layer (Puppetmaster + regional watchers) that adds drama on top of routine life.

This section develops all of these. It is long because the NPC intelligence stack is the single largest architectural investment in Bannou, and its behavior is central to everything that follows in this document.

## 6.2 ABML and GOAP: The Dual Paradigm

Every NPC in Arcadia is governed by two complementary cognition systems:

- **ABML** (Arcadia Behavior Markup Language) — the reactive, authored, event-driven layer. Defines *what the NPC does under specific conditions*.
- **GOAP** (Goal-Oriented Action Planning) — the deliberative, search-based, goal-driven layer. Defines *how the NPC achieves long-term objectives*.

Both share a common substrate (the Actor runtime, the variable provider system, the world state) and can invoke each other seamlessly. An NPC's ABML behavior might trigger a GOAP replan; a GOAP plan might include ABML flows as individual actions.

### 6.2.1 ABML — Reactive Structured Behavior

ABML is a YAML-based domain-specific language for authoring behavior documents. A behavior document defines:

- **Flows**: Named procedures containing branching logic, state updates, intent emissions, and service calls.
- **Perception handlers**: Flows that run in response to specific perception events.
- **Goals** (for GOAP integration): Desired world states with priorities.
- **GOAP action metadata**: Preconditions, effects, and costs annotated onto flows so they can be used as GOAP actions.

An ABML flow reads like a script with conditions and actions:

```yaml
flows:
  assess_threat:
    - cond:
        if: "${personality.aggression > 0.7 && encounters.last_hostile_days < 3}"
        then:
          - set: disposition = "hostile"
          - emit_intent:
              channel: stance
              stance: "aggressive"
              urgency: 0.9
        else:
          - set: disposition = "cautious"
```

This flow is reactive: when an NPC perceives a threat, the `assess_threat` handler runs, evaluates the NPC's personality and recent encounter history, and sets appropriate disposition and stance. It does not plan; it *responds*.

ABML documents compile to portable stack-based bytecode. The compiler (in `sdks/behavior-compiler`) runs multi-phase: YAML parsing → AST construction → semantic analysis → bytecode emission. Compiled bytecode can be executed by either a tree-walking `DocumentExecutor` (flexibility) or a `BehaviorModelInterpreter` (performance).

### 6.2.2 GOAP — Deliberative Goal-Oriented Planning

GOAP is an A* search algorithm that finds action sequences achieving goal states. The planner considers:

- **Current world state**: Immutable key-value store (hunger: 0.8, has_weapon: true, gold: 50, …).
- **Goals**: Desired world states with priority (hunger ≤ 0.3, location: home).
- **Actions**: Available operations with preconditions, effects, and costs.

The planner searches for the minimum-cost sequence of actions that transforms the current world state into one satisfying the highest-priority unsatisfied goal.

**Example**: An NPC with `hunger: 0.8, gold: 100, location: market` and goal `hunger ≤ 0.3` has several candidate plans:

| Plan | Actions | Total Cost | Outcome |
|---|---|---|---|
| Buy bread | `buy_food` → `eat` | 3 | Costs 10 gold, reduces hunger to 0.2 |
| Steal food | `steal_food` → `eat` | 8 | No gold cost, but high risk weighting |
| Return home to eat stored food | `travel_home` → `eat_stored` | 6 | Slow but safe |

The planner picks the lowest-cost applicable plan (Plan 1 here). If circumstances change mid-plan (the market closes, the NPC is robbed), the cognition pipeline triggers a replan.

GOAP runs at multiple urgency levels. High-urgency replans (threats, emergencies) use shallow depth and short timeouts to produce fast decisions; low-urgency replans (routine planning) use deeper search for more optimal plans.

### 6.2.3 Division of Labor

ABML and GOAP are complementary, not competing:

| Concern | Handled By | Example |
|---|---|---|
| Reacting to perceptions | ABML perception handlers | "I was attacked — switch to aggressive stance" |
| Selecting among available options | ABML conditional logic | "If ally is endangered, prioritize protection" |
| Planning multi-step objectives | GOAP search | "I need food → buy from market → eat" |
| Discovering novel action sequences | GOAP | "Need to kill enemy → craft weapon → find materials → …" |
| Encoding personality-driven preferences | ABML with variable providers | "Aggressive characters attack; cautious ones flee" |
| Adapting to failure | Both — ABML for immediate, GOAP for strategic | Replan via GOAP if current plan invalidated |

ABML without GOAP would produce NPCs that respond intelligently to what happens in front of them but cannot pursue long-term goals. GOAP without ABML would produce NPCs that plan sophisticated multi-step objectives but cannot articulate situational personality — they would act like identical optimizers. Together, they produce the full span: quick reactions grounded in personality, pursued in service of long-term goals that emerge from deeper motivations.

## 6.3 The Five-Stage Cognition Pipeline

Every NPC brain processes perceptions through a five-stage pipeline each cognition tick. The pipeline is templated (different templates for humanoids, creatures, objects) and per-character-customizable.

### 6.3.1 The Stages

```
  ┌──────────────────────────────────────────────────────────────┐
  │  PERCEPTION ARRIVES                                          │
  │  (from Game Server via RabbitMQ character.{id}.perception)   │
  └──────────────────────┬───────────────────────────────────────┘
                         ▼
  ┌──────────────────────────────────────────────────────────────┐
  │  1. filter_attention                                         │
  │     Budget-limited perception filtering (urgency-based)      │
  │     Fast-track for high-urgency perceptions (threats)        │
  └──────────────────────┬───────────────────────────────────────┘
                         ▼
  ┌──────────────────────────────────────────────────────────────┐
  │  2. query_memory                                             │
  │     Retrieve relevant memories for context                   │
  │     Lookups: by entity, by type, by spatial region, recent   │
  └──────────────────────┬───────────────────────────────────────┘
                         ▼
  ┌──────────────────────────────────────────────────────────────┐
  │  3. assess_significance                                      │
  │     Score perception for memory storage                      │
  │     Emotional magnitude, novelty, goal relevance             │
  └──────────────────────┬───────────────────────────────────────┘
                         ▼
  ┌──────────────────────────────────────────────────────────────┐
  │  4. store_memory                                             │
  │     Persist significant experiences                          │
  │     Multi-index: entity, type, spatial, recent-circular      │
  └──────────────────────┬───────────────────────────────────────┘
                         ▼
  ┌──────────────────────────────────────────────────────────────┐
  │  5. evaluate_goal_impact + trigger_goap_replan               │
  │     Determine if perception affects active goals             │
  │     Trigger GOAP replan if needed (with urgency-based depth) │
  └──────────────────────────────────────────────────────────────┘
```

**Fast-track bypass**: High-urgency perceptions above a configurable threshold bypass the full pipeline and trigger immediate reactions. This prevents urgent threats from being delayed by memory queries and significance assessment. Threshold is per-template (e.g., `humanoid_base` uses 0.9) and can be overridden per-character.

### 6.3.2 Cognition Templates

Three embedded templates cover the common cases:

| Template | Use Case | Stages Included | Attention Budget | Memory Limit |
|---|---|---|---|---|
| `humanoid_base` | Humanoid NPCs with rich cognition | All 5 | 100 | 20 |
| `creature_base` | Animals, creatures with instinct-driven reactions | 1, 4, 5 only | 50 | 5 |
| `object_base` | Interactive but stateless objects (traps, doors) | 1, 5 only | 10 | 0 |

Creatures skip significance assessment — they don't deliberate about what's memorable; they react instinctively. Objects skip memory entirely — they are stateless responders.

### 6.3.3 Character-Specific Overrides

The `CognitionBuilder` constructs pipelines from templates with optional overrides. A battle-hardened veteran might raise their threat fast-track threshold (less reactive to threats because they've seen worse). A paranoid character might lower it (reactive to everything). A telepathic entity might add a custom handler for processing incoming thought-perceptions.

Override types include parameter tuning, handler disabling, handler adding, handler replacement, and handler reordering — all composable. This produces the combinatorial richness that lets every character's cognition feel distinct without authoring individual pipelines for each.

### 6.3.4 Memory System

The memory store uses four indices for efficient retrieval:

| Index | Query | Typical Use |
|---|---|---|
| `entity_memories` | "What do I remember about entity X?" | O(1) lookup by entity ID |
| `type_index` | "What threats have I seen?" | O(1) lookup by category |
| `spatial_index` | "What happened near here?" | O(1) lookup by region |
| `recent` | Time-ordered fallback | Circular buffer, oldest dropped |

The current implementation uses keyword-based relevance matching, sufficient for structured game events (combat encounters, dialogue exchanges, entity sightings). The `IMemoryStore` interface is designed for swappable implementations — an embedding-based implementation could be added later for semantic similarity without changing the cognition pipeline.

### 6.3.5 Emotional State

Each actor maintains 8 emotional dimensions (stress, alertness, fear, anger, joy, sadness, comfort, curiosity) on a 0.0-1.0 scale. Emotions decay toward personality-defined baselines over time — a guard doesn't stay angry forever, but an anxious character has a higher stress baseline. The *dominant emotion* simplifies behavior selection by providing a single value to check.

Emotional state is a variable provider namespace (`${emotion.*}`) accessible from ABML flows. An NPC whose dominant emotion is `fear` might choose defensive behaviors; one whose dominant emotion is `anger` might choose aggressive ones.

## 6.4 The Variable Provider Stack

### 6.4.1 The Pattern

The **Variable Provider Factory** pattern is the architectural keystone that makes NPC cognition work within the service hierarchy. It resolves a contradiction:

- Actor cognition needs access to data from L4 services (CharacterPersonality, CharacterEncounter, CharacterHistory, Obligation, Faction, etc.).
- Actor is L2. L2 cannot depend on L4.

The solution: Actor depends on the *shared interface* (`IVariableProviderFactory`), not on L4 services directly. L4 services *implement* the interface and register via DI. Actor discovers providers at runtime via `IEnumerable<IVariableProviderFactory>` injection. When an L4 service is absent, Actor just can't reference its variables — cognition gracefully degrades.

This inverts the dependency: L4 pushes data down to L2 through a neutral interface, rather than L2 pulling data up from L4.

### 6.4.2 The Implemented Providers

Fourteen variable providers are currently implemented, organized into six cognitive domains:

**Character Self-Model (what am I?)**

| Provider | Service | Namespace | Example Variables |
|---|---|---|---|
| PersonalityProvider | lib-character-personality (L4) | `${personality.*}` | `${personality.openness}`, `${personality.aggression}` |
| CombatPreferencesProvider | lib-character-personality (L4) | `${combat.*}` | `${combat.style}`, `${combat.riskTolerance}` |
| BackstoryProvider | lib-character-history (L4) | `${backstory.*}` | `${backstory.origin}`, `${backstory.fear.value}` |
| EncountersProvider | lib-character-encounter (L4) | `${encounters.*}` | `${encounters.last_hostile_days}`, `${encounters.sentiment}` |

**World Awareness (where am I, when is it?)**

| Provider | Service | Namespace | Example Variables |
|---|---|---|---|
| WorldProvider | lib-worldstate (L2) | `${world.*}` | `${world.time}`, `${world.season}` |
| LocationContextProvider | lib-location (L2) | `${location.*}` | `${location.type}`, `${location.depth}` |
| TransitProvider | lib-transit (L2) | `${transit.*}` | `${transit.connections}`, `${transit.modes}` |

**Progress and Growth (what am I working on?)**

| Provider | Service | Namespace | Example Variables |
|---|---|---|---|
| SeedProvider | lib-seed (L2) | `${seed.*}` | `${seed.phase}`, `${seed.capabilities}` |
| QuestProvider | lib-quest (L2) | `${quest.*}` | `${quest.active}`, `${quest.objectives}` |

**Economic Awareness (what do I have?)**

| Provider | Service | Namespace | Example Variables |
|---|---|---|---|
| CurrencyProvider | lib-currency (L2) | `${currency.*}` | `${currency.balance.GOLD}`, `${currency.has_wallet}` |
| InventoryProvider | lib-inventory (L2) | `${inventory.*}` | `${inventory.has_item.IRON_SWORD}`, `${inventory.count.HEALTH_POTION}` |

**Social Structures (who am I bound to?)**

| Provider | Service | Namespace | Example Variables |
|---|---|---|---|
| RelationshipProvider | lib-relationship (L2) | `${relationship.*}` | `${relationship.has.ALLY}`, `${relationship.count.FAMILY}` |
| ObligationProvider | lib-obligation (L4) | `${obligations.*}` | `${obligations.violation_cost.THEFT}` |
| FactionProvider | lib-faction (L4) | `${faction.*}` | `${faction.norms}`, `${faction.territory}` |

### 6.4.3 The Planned Providers

Eight more providers are in planning status, expected to significantly extend NPC expressiveness when implemented:

| Provider | Service (Planned) | Namespace | What It Adds |
|---|---|---|---|
| DispositionProvider | lib-disposition (L4) | `${disposition.*}` | Emotional attitudes toward specific entities; drive motivations |
| HearsayProvider | lib-hearsay (L4) | `${hearsay.*}` | What this NPC believes (possibly distorted from truth) |
| LexiconProvider | lib-lexicon (L4) | `${lexicon.*}` | Structured concept vocabulary for communication |
| SocialProvider | lib-lexicon (L4) | `${social.*}` | Recent messages, conversation state, ambient mood |
| SpiritProvider | lib-agency (L4) | `${spirit.*}` | Guardian spirit presence, compliance, influence history |
| GenesisProvider | lib-genesis (L2) | `${genesis.*}` | Genesis entity state (wallet balances, phase, capabilities, bond) |
| EnvironmentProvider | lib-environment (L4) | `${environment.*}` | Weather, temperature, mana density, ecological resources |
| NatureProvider | lib-ethology (L4) | `${nature.*}` | Species behavioral archetype traits |

When these come online, ABML expressiveness grows dramatically. An NPC's reasoning can reference not just *what they know* (Encounters) but *what they believe* (Hearsay), not just *what they feel right now* (Emotion) but *what they feel about specific things* (Disposition), not just *what they can do* (Seed) but *what they can express* (Lexicon).

### 6.4.4 Why This Matters

The variable provider stack is not a convenience; it is the architectural mechanism by which rich character data flows into cognition without tangling service hierarchies. Every time an ABML flow references `${personality.aggression > 0.7 && encounters.last_hostile_days < 3}`, the Variable Provider Factory pattern is working: personality data coming from CharacterPersonality (L4), encounter data coming from CharacterEncounter (L4), both arriving at an Actor (L2) that depends on neither service directly.

Without this pattern, either the service hierarchy breaks (Actor depends on L4 services directly) or NPCs become impossible to implement (Actor can't access the data needed for intelligent behavior). The pattern is why the vision of 100K NPCs with rich personality is achievable — it's how the data flows, at scale.

## 6.5 NPC Brain Actors and the Co-Pilot Pattern

### 6.5.1 What an NPC Brain Does

An NPC Brain is an Actor running an ABML behavior on behalf of a specific character. It:

- Subscribes to the character's perception stream (`character.{characterId}.perception` RabbitMQ topic).
- Processes perceptions through the 5-stage cognition pipeline.
- Produces state updates (feelings, goals, memories) that flow back to the game server.
- Runs GOAP planning periodically (or on-demand when goals are impacted).
- Emits intents (action, locomotion, stance, attention, expression, vocalization) that drive character behavior.

The Brain is always running. Even when a player is inhabiting the character, the Brain continues — watching, computing, waiting. This is crucial to the co-pilot pattern.

### 6.5.2 The Co-Pilot Pattern

In Arcadia, the player possesses a character as a guardian spirit ([§4](#4-the-guardian-spirit--progressive-agency)). The character has its own NPC Brain that:

- Is always running, always perceiving, always computing.
- Has intimate knowledge of the character's capabilities, personality, and preferences.
- Makes decisions when the player doesn't (or can't respond fast enough).
- Maintains personality and behavioral patterns across sessions.

When a player connects, they take **priority** over the Brain's decisions — but the Brain doesn't stop. It watches, computes, and waits. When the player misses a QTE window, the Brain's pre-computed answer executes, creating **emergent personality expression**: an aggressive character attacks anyway, a cautious one takes a defensive stance, a loyal one protects an ally first.

This is what "the character can refuse the spirit" mechanically means. The Brain is always producing its own preferred action. If the player acts, the player's choice wins (subject to compliance checks). If the player doesn't act, the Brain's choice wins. The character is never inert; there is always a next action prepared.

### 6.5.3 Output Separation

The Brain's output is deliberately layered to keep responsibilities clean:

| Output Type | Produced By | Consumed By |
|---|---|---|
| **State updates** | Brain cognition | Game server behavior stack inputs |
| **Goal updates** | Brain GOAP | Game server behavior stack priorities |
| **Memory updates** | Brain significance assessment | Persisted; read by future cognition |
| **Intents** | Behavior stack | Game engine (rendering, physics, animation) |

The Brain is the "why" (feelings, goals, memories). The behavior stack on the game server is the "what" (which actions to take). The intent channels are the "how" (how to animate, move, and emote). This separation is what lets the Brain run server-side at 100-500ms tick rates while the behavior stack evaluates at frame rate on the game server.

## 6.6 Event Brains, Regional Watchers, and God-Actors

### 6.6.1 Beyond Character Cognition

NPC Brains handle individual character cognition. But Arcadia needs higher-order actors too:

- **Event Brains** — orchestrate specific dramatic situations (a particular combat encounter, a festival, a dungeon delve). Spawned on trigger, die when event concludes.
- **Regional Watchers** — long-running actors monitoring entire regions. They observe event streams, detect "interesting" situations, and spawn Event Brains to script drama when thresholds are crossed. Regional Watchers are the mechanical implementation of **gods** in Arcadia.
- **God-Actors** — the generalized term for long-running ABML behaviors that are not bound to individual characters. Regional Watchers are god-actors. Gardener god-actors (per [§4.5](#45-gardener-the-experience-orchestrator)) are another kind. Divine actors (gods in PANTHEON, per [§13](#13-actor-bound-entities-living-places-and-things)) are another.

All of these use the same Actor runtime as NPC Brains. The difference is scope: NPC Brains perceive at character level; Event Brains perceive at encounter level; Regional Watchers perceive at realm level.

### 6.6.2 Regional Watchers as Gods

A Regional Watcher is a long-running actor (typically tied to a character in the PANTHEON system realm — i.e., a deity) that:

- Subscribes to region-wide event streams.
- Has aesthetic preferences encoded in its ABML behavior (a war-god prefers honorable duels; a trickster-god prefers dramatic reversals; a nature-god prefers ecological balance).
- Detects "interestingness" triggers (power-level proximity, antagonism scores, environmental drama, story flags, VIP presence, player involvement).
- Spawns Event Brains when thresholds are crossed, configuring them with appropriate narrative seeds.

A well-designed set of Regional Watchers produces a world that *feels* narratively alive — interesting things happen not because the game scripted them, but because aesthetic actors are watching and composing.

### 6.6.3 Connection to the Content Flywheel

Regional Watchers are the engine of the content flywheel ([§7](#7-the-content-flywheel)). They consume narrative seeds generated from character archives (via Storyline), orchestrate scenarios, experience-generate new character history, which eventually becomes new archives, which generates new seeds. The loop accelerates because more Watchers, more characters, more interactions produce polynomial compound growth in opportunities.

## 6.7 Puppetmaster and the Behavior Provider Chain

### 6.7.1 The Problem

Actor (L2) needs to load ABML behavior documents. Some behaviors come from the Asset Service (L3). But L2 cannot depend on L3 — that's a hierarchy violation.

### 6.7.2 The Solution

The `IBehaviorDocumentProvider` interface (in `bannou-service/Providers/`) defines a priority-ordered provider chain. Multiple providers register via DI, each with a different priority. Actor's `BehaviorDocumentLoader` queries them highest-priority-first; the first provider that can serve the requested behavior wins.

| Priority | Provider | Source | Service |
|---|---|---|---|
| 100 | DynamicBehaviorProvider | Asset Service via presigned URLs | lib-puppetmaster (L4) |
| 50 | SeededBehaviorProvider | Pre-defined embedded behaviors | lib-actor (L2) |
| 0 | FallbackBehaviorProvider | Stub/default behaviors | lib-actor (L2) |

Puppetmaster (L4) bridges Actor (L2) and Asset (L3) without hierarchy violation: it implements the interface defined in `bannou-service/`, loads behaviors from Asset, caches them, and handles hot-reload via `behavior.updated` event subscriptions. If Puppetmaster is absent, Actor falls through to seeded and fallback behaviors — the system degrades gracefully instead of failing.

### 6.7.3 What Puppetmaster Adds

Beyond behavior loading, Puppetmaster provides:

- **Regional Watcher lifecycle management** — spawning, monitoring, and stopping region-scoped god-actors.
- **Dynamic behavior loading** — behaviors can be updated at runtime without restarting the service (hot-reload via Asset versioning).
- **Resource snapshot caching** — loaded behaviors are cached in Redis for fast subsequent loads.

The Actor vs. Puppetmaster division is intentional: **Actor answers "what should this NPC do?"**, **Puppetmaster answers "what should be happening in this region?"**. They solve fundamentally different problems, and keeping them separate means Actor is always available (L2, no dependencies to load) while Puppetmaster provides enrichment when higher-level orchestration is needed.

## 6.8 100K NPCs: Scaling the Architecture

### 6.8.1 The Scale Requirements

The vision's target: 100,000+ concurrent AI NPCs, each making decisions every 100-500ms. At 10 perception events/second per NPC, this is 1,000,000 perception events/second flowing through the system. Traditional approaches (central message broker, per-NPC API calls, per-NPC thread) collapse under this load.

Arcadia's architecture meets the target through several coordinated design choices.

### 6.8.2 Direct RabbitMQ Delivery

Perception events flow **directly** from the game server to actor pool nodes via RabbitMQ subscriptions. The control plane is not in the delivery path. Each character's perception topic (`character.{id}.perception`) is a RabbitMQ fanout exchange; any actor subscribed to that exchange receives events directly.

```
Game Server ──publish──▶ RabbitMQ ──deliver──▶ Actor Pool Node
                                                     │
                                          enqueue in bounded
                                          perception channel
                                          (DropOldest policy)
                                                     │
                                          drain on next tick
```

This scales horizontally: adding more actor pool nodes adds more parallel consumers, with no bottleneck in the routing layer. 100K events/second is a problem RabbitMQ solves routinely.

### 6.8.3 Bounded Perception Queues

Each actor has a bounded perception channel with `DropOldest` policy. Under perception floods, newer events overwrite older ones. High-urgency perceptions fast-track through the cognition pipeline; low-urgency ones go through full deliberation. This prevents any single NPC's cognition from being overwhelmed.

### 6.8.4 Pool Node Architecture

Actor pool nodes are peers on the Bannou network with unique app-IDs. They:

- Register with the control plane on startup (via `actor.pool-node.heartbeat` events).
- Host N actors each (configurable per pool type, typically 500-1000 per node).
- Subscribe to perception topics for their hosted actors directly.
- Send state updates back to game servers via `character.{id}.state` topics.

The control plane (actor registry, template storage, pool manager) handles only lifecycle operations (spawn, stop, migrate, health monitoring). It is never in the perception event path. This decouples throughput (scales with pool nodes) from control (single control plane).

### 6.8.5 Deployment Modes

Actor deployment is configurable per game:

| Mode | Description | Use Case |
|---|---|---|
| `Local` | All actors in main process | Small scale, embedded deployments |
| `PoolPerType` | Dedicated pool per actor category | Medium scale with type-specific tuning |
| `SharedPool` | Shared pool across categories | Medium scale with flexibility |
| `AutoScale` | Dynamic scaling based on load | Large scale (planned) |

The same game code works across all modes. Deployment choice is environment configuration.

### 6.8.6 Batched Character Service Calls

The game server batches character-level operations rather than making individual API calls per NPC interaction. Chat messages, Hearsay beliefs, Collection discoveries all have batch endpoints that accept many operations in a single call. At 100K NPC scale, per-NPC API calls would saturate network; batching reduces this to reasonable rates.

### 6.8.7 Graceful Degradation — The Lizard Brain

An important scale property: **characters must function autonomously when no NPC Brain is connected.** The behavior stack on the game server evaluates with default personality and no actor enrichment. Characters still respond to threats, navigate, fight, and interact socially — they just don't grow or evolve.

This is not a degraded mode; it's the **default state**. The actor system layers growth and personality on top of already-functional characters. If a pool node fails, characters it was managing fall back to lizard-brain behavior until a new actor is assigned. The world doesn't freeze; it becomes slightly less nuanced.

This is the architecture's resilience: losing actor capacity reduces NPC richness but never crashes the game world.

## 6.9 Summary: What Makes This Work

The NPC intelligence stack is an unusual piece of engineering — 14+ services cooperating to produce autonomous cognition at 100K scale — and it works because of specific design choices that cascade into capability:

- **Dual cognition (ABML + GOAP)** produces both reactive responsiveness and deliberative depth from the same infrastructure.
- **Variable Provider Factory pattern** lets rich L4 data flow into L2 Actor without hierarchy violations, making personality + memory + history + obligations all available to cognition.
- **Five-stage cognition pipeline** gives every NPC a structured reasoning process that is customizable per-character via overrides.
- **Direct RabbitMQ perception delivery** decouples scale from the control plane, making horizontal scaling practical.
- **Bounded queues and urgency-based processing** prevent perception floods from overwhelming cognition.
- **Pool-node architecture** decouples control from throughput, letting actor capacity scale independently from actor management.
- **Lizard-brain fallback** makes the system resilient — NPC Brain absence reduces richness, not functionality.
- **Puppetmaster + Asset Service bridge** lets behaviors be dynamically loaded and hot-reloaded without Actor depending on Asset.
- **Regional Watchers as god-actors** add orchestration on top of routine cognition, making the world narratively alive.

These choices reinforce each other. Adding richer variable providers makes individual NPCs smarter without adding scaling overhead. Adding pool nodes adds capacity without touching NPC cognition. Adding Regional Watchers adds drama without touching individual NPCs. Each axis is independently improvable, which is what makes the architecture sustainable over a long development horizon.

The downstream effect for every section that follows: when this document refers to "NPCs doing X" — pursuing economic goals ([§9](#9-the-economic-substrate)), participating in social exchanges ([§10](#10-the-social-fabric--cultural-emergence)), fighting in cinematic combat ([§11](#11-combat--cinematic-choreography)), engaging with emergent scenarios ([§7](#7-the-content-flywheel)) — it is referring to this stack working. The NPCs in Arcadia are not background decoration; they are cognitive agents running on this infrastructure, and their behaviors emerge from the interaction of ABML + GOAP + variable providers + world events in real time.

---

# 7. The Content Flywheel

## 7.1 The Problem the Flywheel Solves

Every multiplayer game faces the same existential threat: **content exhaustion**. Designers author a finite amount of content. Players consume it. The game gets stale. Studios hire more designers. The treadmill accelerates until the studio can't keep up, and the game dies.

The standard industry responses each have limits:

- **Procedural generation** (Minecraft, No Man's Sky) produces infinite variety but shallow meaning — the 10,000th cave feels like the first.
- **User-generated content** (Roblox, Dreams) offloads creation to players, but quality is inconsistent and most players don't want to be content creators.
- **Seasonal content drops** (Destiny, Fortnite) keep players engaged in cycles but require permanent studio investment and the content is still finite per season.

Arcadia takes a fourth approach: **play itself generates the raw material for future content**. Not by asking players to build things, but by recording what happens and composting it into new narrative material. The world's accumulated history *is* the content pipeline. A server that has run for 5 years has 500x the content of one that has run for 1 year — because 5 years of play is 5 years of accumulated character histories, compressed into archives, generating seeds, composed into scenarios, experienced by new characters whose own lives become future archives.

This section develops how the flywheel actually works: the services that compose it ([§§7.2–7.5](#72-what-gets-recorded-while-characters-are-alive)), the compounding math ([§7.6](#76-why-it-accelerates-the-compounding-math)), the compression gameplay patterns that emerge ([§7.7](#77-compression-gameplay-patterns)), and the conditions under which the flywheel stalls ([§7.8](#78-what-breaks-the-flywheel)).

The flywheel is also the operational expression of the **lexicon flywheel** developed in [§2.5](#25-the-recursive-lexicon-flywheel). Beneath every "character died → content generated" step is a deeper "Lexicon grew → new composition became possible" step. Read this section with that metaphysical substrate in mind: the surface mechanic is content generation; the underlying mechanic is the canonical vocabulary of the world deepening.

## 7.2 What Gets Recorded While Characters Are Alive

The flywheel begins with recording — and the recording is a side effect of services doing their ordinary jobs, not a dedicated activity.

Several L4 services continuously record character life:

| Service | What It Records |
|---|---|
| **Character History** (L4) | Backstory elements (origin, trauma, goals, achievements, fears), world event participation (battles, treaties, disasters) with role and significance scores, temporal context. |
| **Character Personality** (L4) | Current trait values and how they've evolved. A character who survived repeated betrayals has measurably different personality values than one who lived peacefully. Combat preferences also evolve. |
| **Character Encounter** (L4) | Memorable interactions with other characters — alliances, betrayals, trades, rivalries, romances — with sentiment scores, encounter type, and time-weighted decay. |
| **Character Lifecycle** (L4) | Generational data — lineage, marriages, children, death manner and timing. |
| **Realm History** (L4) | World-scale events from the realm's perspective — the events characters participated in as seen by history. |

**None of these services exist "for the flywheel."** They exist because NPC cognition needs this data. CharacterPersonality traits feed the `${personality.*}` variable provider. CharacterEncounter memories feed `${encounters.*}`. CharacterHistory backstory feeds `${backstory.*}`. These are the inputs to the NPC Intelligence Stack ([§6](#6-the-npc-intelligence-stack)) that make NPCs feel like specific individuals rather than generic bodies.

The flywheel is a **beneficial side effect** of data that already needs to exist. This is architecturally important: no service does extra work "for content generation." The recording happens regardless; the flywheel is what emerges from it being done well.

## 7.3 Death and Compression

When a character dies — whether from old age, combat, disease, accident, or narrative consequence — their accumulated data must be compressed. Keeping the full live data indefinitely would explode state stores; discarding it would destroy the flywheel's raw material. The compromise: **structured compression into an archive**.

The **Resource service** (L1) is the archive coordinator. It defines an extension mechanism (`x-compression-callback`) by which any service can register to contribute data when a specific resource type is compressed. On character death:

1. Resource receives the compression request (triggered by character lifecycle).
2. It iterates through all services that registered compression callbacks for `character` resources.
3. Each service supplies its contribution: Character History supplies a template-based life summary, Character Personality supplies final trait values, Character Encounter supplies the most significant relationships, and so on.
4. Contributions are aggregated into a unified MySQL-backed archive.
5. The archive replaces the live data (which is then safely deletable).

**Compression is deliberately lossy.** A character who lived 30 in-game years might have hundreds of history entries, thousands of encounter records, and continuous personality evolution curves. The archive distills this into the narratively significant core: key events (not routine ones), defining relationships (not every encounter), personality trajectory (not every shift), unfinished business (not every completed task), manner of death. The fidelity is tuned for narrative reuse, not forensic reconstruction.

### 7.3.1 Ephemeral Snapshots vs. Permanent Archives

Resource supports two compression modes:

- **Permanent archives** (on death): Stored in MySQL, replacing the live data. The character is gone; the archive remains indefinitely.
- **Ephemeral snapshots** (on demand): Stored in Redis with configurable TTL (1-24 hours), non-destructive. Used for AI context windows, actor brain initialization, cross-service data sharing — scenarios where you want a compressed view but the character is still alive.

Both modes use the same compression callbacks, producing the same structural output. A narrative generator consuming an archive and one consuming a snapshot produce the same kind of content; the difference is whether the source character is dead or alive.

## 7.4 Narrative Seed Generation: Storyline

The **Storyline service** (L4) takes compressed archives (and snapshots) and generates narrative seeds using GOAP planning. This is not AI text generation; it is **structured planning over narrative action spaces**.

### 7.4.1 What a Narrative Seed Is

A narrative seed is a plan — a sequence of phases, each with actions, conditions, and entity requirements. Consider this seed generated from an archive:

> "A ghost of a betrayed merchant haunts the trade road where they were murdered. The haunting causes caravans to reroute, which depresses the local economy of the nearby town. This continues until someone investigates the haunting and resolves the betrayer's identity, at which point the ghost moves on and the economy recovers."

Unpacked into GOAP structure:

- **Phase 1 (Haunting)**: Actions — manifest ghost at death-location, produce supernatural ambiance, alarm travelers. Preconditions — ghost archive exists, location is accessible. Effects — location gets `haunted` status.
- **Phase 2 (Economic Impact)**: Actions — caravans reroute, market prices rise, local merchants lose income. Preconditions — location has `haunted` status, trade routes pass through. Effects — town economy degrades.
- **Phase 3 (Investigation)**: Actions — quest offered to investigators, clues distributed, confrontation at death-location. Preconditions — someone accepts the quest. Effects — betrayer identified, ghost pacified.
- **Phase 4 (Resolution)**: Actions — ghost moves on, town economy recovers. Preconditions — betrayer's identity revealed. Effects — location status clears, archive updated.

The seed is complete: it specifies phases, transitions between phases, entity requirements (the ghost, the location, the town, a potential investigator), and end conditions. What it does *not* specify is when to run, who plays which role, or whether to run it at all.

### 7.4.2 SDK Separation

The narrative generation is split across three layers:

| Layer | What | Location |
|---|---|---|
| **StorylineTheory SDK** | Pure-computation primitives: Greimas actantial model, Propp's morphology, Barthes' narrative codes — formal narrative theory as data structures | `sdks/storyline-theory` |
| **StorylineStoryteller SDK** | GOAP-planned story arc generation from archives, lazy phase evaluation via continuation points | `sdks/storyline-storyteller` |
| **lib-storyline plugin (L4)** | HTTP API wrapping both SDKs; scenario matching; dry-run testing; origin scenario for organic character creation | `plugins/lib-storyline` |

This separation (same pattern as Music — [§12](#12-procedural-creative-generation)) makes the narrative engine reusable. The SDKs are pure computation with zero service dependencies; they can be run client-side, server-side, or in content tools. The plugin adds HTTP endpoints, Redis caching of deterministic plans, and scenario composition on top.

### 7.4.3 Two Execution Modes

Storyline supports two execution modes from the same definitions:

- **Simple Mode**: Deterministic MMO-style unlocks with periodic server checks. An archive meets a threshold → a scenario becomes available → eligible characters see it in their quest log. Fast, predictable, limited emergence.
- **Emergent Mode**: Regional watcher god-actors actively search, score, and decide which characters qualify for which narratives based on their actual life histories. Slower, richer, deeper emergence.

The same underlying scenarios work in both modes; developers choose which mode fits their realm.

## 7.5 Orchestration: Regional Watchers and God-Actors

Storyline generates plans; **Regional Watchers orchestrate whether to execute them**. Regional Watchers are god-actors (see [§6.6](#66-event-brains-regional-watchers-and-god-actors) and [§13](#13-actor-bound-entities-living-places-and-things)) — long-running ABML behaviors running in the Actor runtime on behalf of specific PANTHEON characters.

### 7.5.1 Divine Aesthetic Preferences

Each Regional Watcher represents a domain-specific god (war, love, commerce, death, nature, trickery, etc.) and has ABML-encoded aesthetic preferences. When a war-god watcher receives available narrative seeds and current regional state, it evaluates:

- Is this region currently too peaceful for my taste? → maybe inject a conflict seed.
- Are these rival factions on the verge of escalation? → nudge them with an appropriate seed.
- Did someone just perform an honorable duel? → record this in Realm History with elevated significance.

A trickster-god evaluates different things: dramatic reversals, clever deceptions, ironic outcomes. A death-god evaluates transformations, last stands, legacies. The aesthetic is encoded in ABML logic, not in hardcoded rules.

### 7.5.2 The Curation Function

Regional Watchers are not spawners of random content. They are **curators**. Given:

- A set of available narrative seeds (output of Storyline consuming archives).
- The current state of their region (economic, political, social, ecological).
- Their own domain and aesthetic preferences.
- Ongoing narratives that might conflict or complement new ones.

...they select which seeds to instantiate, how to configure them, and when to execute them. A region with an already-active war seed may not need a second one. A region recovering from catastrophe may need healing-focused seeds, not more catastrophe.

This curation is what gives the flywheel its *texture*. Random deployment would produce noise. Curated deployment produces narrative coherence — regions that feel stylistically distinct because their resident gods have distinct preferences.

### 7.5.3 Gods as Primitive-Concept Discoverers

Beyond orchestrating existing narratives, mature Regional Watchers perform a deeper role: **discovering new primitive logos** (see [§2.7.3](#273-gods-as-primitive-discoverers)). A commerce-god observing ten thousand trade interactions across a decade may detect a pattern — say, `[REPUTATION_CASCADE]` or `[MARKET_SATURATION]` — that no individual merchant could articulate because no merchant has the observational scope.

When a Regional Watcher curates such a concept into the canonical Lexicon, the civilization's capability grows. Future enchanters can inscribe reputation-cascade patterns. Future merchants can have ABML behaviors referencing market-saturation. Future narratives can compose around these concepts.

This is why the flywheel accelerates more than exponentially. It's not just that more archives produce more seeds. It's that *more play produces more primitive Lexicon entries*, which expands the combinatorial space in which future play occurs, which produces richer archives, which the watchers observe, which discovers still more primitives, and so on.

## 7.6 Why It Accelerates: The Compounding Math

The loop is:

```
        ┌────────────────────────────────┐
        │                                │
        ▼                                │
  PLAY                                   │
    │                                    │
    │ (events recorded by L4 services)   │
    ▼                                    │
  CHARACTER HISTORY / PERSONALITY /      │
  ENCOUNTERS / REALM HISTORY / LEXICON   │
    │                                    │
    │ (death triggers compression)       │
    ▼                                    │
  ARCHIVE (via Resource L1)              │
    │                                    │
    │ (GOAP-planned from archive)        │
    ▼                                    │
  NARRATIVE SEED (via Storyline L4)      │
    │                                    │
    │ (watcher curates against state)    │
    ▼                                    │
  SCENARIO (instantiated in-region)      │
    │                                    │
    │ (experienced by characters)        │
    ▼                                    │
  MORE PLAY ──────────────────────────────┘
```

### 7.6.1 The Year 1 → Year 5 Projection

| Metric | Year 1 | Year 5 |
|---|---|---|
| Characters died | ~1,000 | ~500,000 |
| Archives accumulated | ~1,000 | ~500,000 |
| Narrative seeds available | ~1,000 (single-archive-based) | ~500,000+ (often multi-archive) |
| Lexicon entries canonical | ~5,000 initial | ~20,000+ accumulated |
| Cross-archive references in seeds | Rare | Routine |
| Multi-generational narratives | Impossible | Natural |

### 7.6.2 Why the Acceleration Is Not Linear

A naive reader might expect "5x the time = 5x the content." The actual math is roughly polynomial:

- **Year 1 archives** reference only initial world state. Seeds are simple: one dead character's unfinished business.
- **Year 2 archives** reference characters whose lives were shaped by Year 1's narrative seeds. A Year 2 character who fought in a war triggered by a Year 1 seed produces a richer archive — it references the war, the seed's originator, the consequences.
- **Year 3 archives** can reference characters whose lives intersected with *two* prior generations. Seeds from these archives are multi-generational.
- **Year 5 archives** carry deep historical sediment — the events of prior generations compound.

Each archive's richness is proportional to the world's cumulative history. More history → denser archives → more complex narrative seeds → more elaborate scenarios → richer play → denser archives. The compounding is real, and the polynomial is at least quadratic in world age.

### 7.6.3 The Lexicon Contribution

Underneath the archive accumulation, the canonical Lexicon is also growing (per [§2.5](#25-the-recursive-lexicon-flywheel)). Each year's play produces new valid concept combinations; some are promoted to canonical primitives by curating gods. A Year 5 narrative seed can compose logos that didn't exist in Year 1 — the combinatorial vocabulary has expanded.

Combined with the archive accumulation, Year 5 content is not just "500x Year 1 content" in volume. It is qualitatively more sophisticated because it composes concepts that only exist because the world produced them through play.

## 7.7 Compression Gameplay Patterns

The Resource service's compression infrastructure enables gameplay patterns beyond "archives feed Storyline." These are compression *gameplay patterns* — ways that dead characters continue to contribute mechanically to the living world.

### 7.7.1 The Spectrum of Return

Death in Arcadia need not be final. The compression archive enables a spectrum of post-mortem gameplay, each with different fidelity to the original:

```
Full Restoration ────────────────────────────────────────► Empty Shell
     │                 │                   │                    │
True Revival    Revenant/Clone         Ghost/Echo         Zombie/Vessel
```

| Pattern | Description | Data Usage |
|---|---|---|
| **Ghosts** | Spectral entity, full personality + memories, bound to death-location | Full archive with spectral modifications |
| **Revenants** | Willful return with single obsession, personality amplified | Full archive, goal narrowed |
| **Zombies** | Body returns, cognition degraded | Partial archive, memory scrambled |
| **Clones** | New entity initialized from archive template | Archive as seed, no identity claim |

Each pattern is realizable with existing primitives:

- **Ghosts** are Characters in UNDERWORLD (or a game-specific ghost system realm) with modified behaviors and location anchoring. The archive provides personality, memories, relationships; the ghost's ABML restricts movement and adds spectral abilities.
- **Revenants** are Characters created from archives with amplified obsession traits, often via a Genesis template like `undead_revenant` that specifies personality modifications.
- **Zombies** are Characters with degraded cognition templates (simpler than `humanoid_base`) initialized from corrupted archive subsets.
- **Clones** are fresh Characters with archive data copied into their personality/history providers.

The pattern is consistent: **archives are generative inputs for new entities**, not terminal records.

### 7.7.2 The Tombguard Pattern

A particularly elegant pattern derived from *Shangri-La Frontier*'s Setsuna/Wezaemon relationship: **living NPCs whose identity becomes defined by their relationship to a compressed character**.

A character with an extreme archived relationship (sentiment > 0.9, significance > 0.8) to a now-dead other character can become a "tombguard" — their personality, goals, combat patterns, and name shift to reflect their bond with the deceased. They defend a location. They protect a memory. They refuse to let go.

This produces some of the most emotionally resonant gameplay content possible. Players encounter a boss whose entire existence revolves around grief — and when they understand who they've lost (from archive data), the encounter becomes meaningful. The compressed character never acts, never speaks, never fights, yet defines another character's entire existence.

### 7.7.3 Unfinished Business as Quest Fuel

Every compressed character is a potential quest generator:

| Quest Type | Derived From | Example |
|---|---|---|
| Revenge | High-negative-sentiment encounters | "Avenge X who was betrayed by Y" |
| Unfinished Goals | Backstory goals that remained unresolved | "Complete what X always wanted to do" |
| Lost Knowledge | Backstory secrets that died with them | "Recover the technique X knew" |
| Closure | Family members still alive with unresolved tension | "Deliver X's final message to Y" |
| Mystery | Event participation with partial truth | "Piece together what X knew about the great fire" |

Storyline's GOAP planner extracts these quest seeds automatically from archives. Regional Watchers decide when and how to deploy them. Each generation's dead become the next generation's quest-givers.

### 7.7.4 Mementos and Spiritual Ecology

Per the [Memento Inventories design](planning/MEMENTO-INVENTORIES.md), each location in the game world accumulates **memento items** generated from real gameplay events — deaths, battles, emotional moments, masterwork creations. These mementos:

- Live in location-scoped Inventories (via `lib-inventory`, L2).
- Are Item instances (via `lib-item`, L2) with metadata referencing the originating archive.
- Accumulate passively as events happen at the location.
- Are consumable by characters with spiritual perception abilities (necromancers, mediums, historians, detectives, bards, craftsmen).

This creates a **spiritual ecology** — a layer of location-based narrative residue that characters with appropriate abilities can interact with. A bard composing a performance at a battlefield draws emotional resonance from battle mementos. A detective investigating a cold case extracts forensic evidence from memento perspectives. A necromancer summons spirit echoes from intensely-remembered locations.

The infrastructure is already present (Item, Inventory, Location). The mementos themselves are Item instances with archive references in their `customStats`. No new plugin required.

### 7.7.5 Legacy Mechanics: Descendants Inherit

When a character dies, their archive becomes a **genetic and memetic template** for descendants. Character Lifecycle handles genetic inheritance (trait predispositions based on parent archives), but further patterns extend this:

- **Ancestral memories** — Child occasionally "remembers" ancestor experiences during sleep, meditation, or significant stress. Data from ancestor archives surfaces as dream sequences or déjà vu.
- **Family reputation** — Dead character's events still affect living descendants. "Your grandmother was a hero of the Stormgate; you're welcome here." "Your grandfather betrayed us; prove yourself different."
- **Inherited obligations** — Dead character's unresolved obligations can pass to descendants as starting context. A dead merchant's unpaid debts become a son's burden; a dead warrior's honor-pledge becomes a daughter's duty.

### 7.7.6 The Content Flywheel Extension: Physical Content

The flywheel described so far generates *narrative* content — quests, memories, NPC behaviors, world events. When the Procedural service (`lib-procedural`, L4, planned) matures, the flywheel will also generate *physical* content:

- A war that scarred a region → terrain regenerated with battle damage via Houdini HDAs.
- An NPC builder claims to have built a house → unique building facade appears.
- A dungeon that grows → new chambers with unique procedural geometry.

Same compression → generation → orchestration pipeline, but the output includes Asset service references alongside Quest definitions. Regional Watchers decide not just what narrative to deploy but what physical changes to request from the Houdini worker pool.

## 7.8 What Breaks the Flywheel

The flywheel spans every layer of the service hierarchy, and it is only as strong as its weakest link. Each of these failure modes corresponds to a specific service working incorrectly:

| Failure Mode | What Goes Wrong |
|---|---|
| **Compression loses too much fidelity** | Archives become generic summaries. Storyline has nothing specific to plan from. Seeds are all bland and interchangeable. |
| **Storyline can't generate from archives** | Regional Watchers have no scenarios to deploy. Rich history exists but nobody can access it. |
| **Regional Watchers don't curate** | Scenarios deploy randomly without regard for regional narrative texture. World feels like procedural noise, not accumulated history. |
| **NPC intelligence stack is too simple** | Characters live shallow lives, produce shallow archives. Flywheel turns but never accelerates. |
| **Lexicon stays small** | New primitives aren't discovered. Combinatorial space stagnates. Year 5 seeds look like Year 1 seeds. |
| **Hearsay distortion is not countered** | Civilizational capability decays across generations. Later archives have less to work with than earlier ones. |
| **Death is too rare** | Few archives accumulate. Flywheel is starved at the input. |

The flywheel does not have a "flywheel service" that can be fixed. It has a dozen services each doing their ordinary jobs, and whether the flywheel spins depends on whether all of them do those jobs well. This is why the flywheel cannot be a single feature or a single implementation target — it is a system-level emergent property of the architecture working correctly.

## 7.9 The Cross-Layer Composition

For reference, the flywheel's service composition across the layer hierarchy:

| Stage | Service | Layer | Role |
|---|---|---|---|
| **Recording** | Character History | L4 | Append-only life events |
| **Recording** | Character Personality | L4 | Trait evolution tracking |
| **Recording** | Character Encounter | L4 | Interaction memory |
| **Recording** | Realm History | L4 | World event recording |
| **Recording** | Character Lifecycle | L4 | Generational data |
| **Compression** | Resource | L1 | Archive coordination via compression callbacks |
| **Generation** | Storyline | L4 | GOAP-based narrative planning (via StorylineTheory + StorylineStoryteller SDKs) |
| **Orchestration** | Puppetmaster | L4 | Regional Watcher lifecycle |
| **Orchestration** | Divine | L4 | Deity management; Regional Watchers as god-actors |
| **Execution** | Actor | L2 | Behavior runtime for NPCs (including Watchers) |
| **Execution** | Quest | L2 | Objective progression (quests derived from narrative seeds) |
| **Execution** | Contract | L1 | State machine backing quest flow |
| **Lexicon growth** | Lexicon | L4 (planned) | Canonical concept catalog expansion |
| **Entropy** | Hearsay | L4 (planned) | Civilizational information decay (counter-pressure) |

No single service "owns" the flywheel. Each contributes its specialty. The combined effect is accelerating content generation — an architecture-scale emergent property.

## 7.10 Connecting to the Rest of the Document

The content flywheel is not an isolated feature. It touches every section that follows:

- [§8 (Life, Death, Generations)](#8-life-death-and-generational-cycles) develops character lifecycle in detail, including death mechanics that trigger compression.
- [§9 (Economic Substrate)](#9-the-economic-substrate) — some narrative seeds are economic (supply crises, trade opportunities); the flywheel feeds the economy.
- [§10 (Social Fabric)](#10-the-social-fabric--cultural-emergence) — cultural emergence is another name for sustained Lexicon growth fed by the flywheel.
- [§11 (Combat & Cinematic)](#11-combat--cinematic-choreography) — Regional Watchers orchestrate dramatic encounters, which are a form of scenario deployment.
- [§12 (Procedural Creative Generation)](#12-procedural-creative-generation) — SDKs that feed the flywheel (Storyline, Music, Procedural) and what the flywheel can produce when those SDKs mature.
- [§13 (Actor-Bound Entities)](#13-actor-bound-entities-living-places-and-things) — all actor-bound entities' destructions feed the flywheel; gods are the watchers that curate it.
- [§18 (Underworld Realms)](#18-designing-underworld--afterlife-realms) — post-mortem inhabitation as the most direct expression of "archives become generative inputs."

Every section in Part II-III depends on the flywheel working. If you find yourself reading a later section and wondering "where does this content actually come from?" — this is the answer. It comes from accumulated play, compressed into archives, composed into seeds, curated by gods, executed as scenarios, producing more play, deepening the Lexicon, tilting the whole system toward richer and richer emergent outcomes as time goes on.

---

# 8. Life, Death, and Generational Cycles

## 8.1 Life as a Loop

Every character in Arcadia follows an arc: birth, growth, identity formation, maturity, aging, death. Unlike most games, this arc is not merely cosmetic — it is mechanically modelled, consequential, and part of the content flywheel's substrate. Characters do not persist indefinitely as timeless entities; they are born, live lives that shape them, and eventually transition through death into afterlife gameplay and archive compression.

The generational cycle extends this: characters marry, have children, die, and their descendants inherit traits, memories, reputations, and obligations. Across 80-100 year saeculums, dynasties form, traditions stabilize, and cultural continuity emerges from the interaction of thousands of individual lives.

This section develops the mechanical infrastructure that makes this work: the growth primitives ([§8.2](#82-seed-the-universal-growth-primitive)), the lifecycle stages ([§8.3](#83-the-lifecycle-arc)), plot armor as narrative pacing ([§8.4](#84-plot-armor-as-narrative-pacing)), the three phases of death ([§8.5](#85-the-three-phases-of-death)), UNDERWORLD gameplay ([§8.6](#86-underworld-gameplay)), exit paths beyond death ([§8.7](#87-exit-paths-retirement-and-transcendence)), generational inheritance ([§8.8](#88-generational-inheritance)), and Realm History as civilizational memory ([§8.9](#89-realm-history-and-saeculum-structure)).

## 8.2 Seed: The Universal Growth Primitive

Before discussing life arcs, we need the infrastructure that tracks growth across all domains. That infrastructure is the **Seed service** (`lib-seed`, L2).

### 8.2.1 What Seeds Are

A seed is a **progressive growth primitive** that starts empty and accumulates capabilities as it receives growth in named domains. Every character has seeds. Every guardian spirit has seeds. Every dungeon has seeds. Every sentient weapon has seeds. Faction territorial governance, crafting mastery, divinity accumulation, quest tracking progress — all of these are seeds.

A seed has:

- **A type** (the seed type definition — what domains it tracks, what phases it progresses through, what capabilities it unlocks at each phase).
- **An owner** (a character, an account, an item, a location, a genesis entity).
- **Per-domain growth values** (continuous floats, accumulating over time).
- **A current phase** (derived from total growth crossing threshold).
- **Active capabilities** (gated by per-capability rules evaluating against growth).

### 8.2.2 Why Seeds Are L2

Seeds are L2 (GameFoundation) because progressive growth is core game infrastructure. Too many services depend on it (Character, Actor, Quest, Faction, Divine, Genesis, Gardener, Agency) for it to be anything else. If Seed were L4, each of these would either have to implement its own progression tracking or accept failure when seeds are absent.

Consequently, Seed is always available in any Bannou deployment that has game foundation layer loaded. You get growth tracking "for free" whenever you have Characters or Genesis entities.

### 8.2.3 Capability Gating

Seed capabilities are the mechanism by which growth translates into concrete game effects. A capability rule specifies:

- A capability code (e.g., `passive.elemental_damage`, `active.read_emotions`, `spatial.expand_domain`).
- A required domain (e.g., `elemental_mastery`, `social.empathy`).
- A threshold (the minimum growth value in the required domain).

When a seed's growth in the required domain crosses the threshold, the capability unlocks. Consumer services check `seed.HasCapability(code)` before enabling the associated game mechanic.

This produces progression that feels both continuous (growth accumulates smoothly) and structured (capabilities unlock at specific thresholds). A character slowly growing combat mastery reaches the `dodge.parry` threshold at some point and gains access to parry mechanics; they did not "level up" — they accumulated enough relevant experience to cross the threshold.

## 8.3 The Lifecycle Arc

Characters in Arcadia move through stages tracked by **Character Lifecycle** (`lib-character-lifecycle`, L4). The service orchestrates aging, reproduction, death, and the generational patterns that emerge from them.

### 8.3.1 Birth

Characters enter the world at birth (or at creation, for non-biological entities). The initial state is shaped by:

- **Genetic inheritance** from parents (if procreation-based) — personality trait predispositions, species traits, aptitudes. The formula combines both parents' trait values with random variance, modulated by environmental factors (ancestral trauma can produce stress baseline shifts in descendants).
- **Cultural inheritance** from birth context — starting Lexicon access based on home region, cultural memes from household traditions, early-life encounter records with parents and siblings.
- **Narrative inheritance** from active scenarios — a child born into an ongoing war inherits scenario context; a child born in peace inherits different starting conditions.

### 8.3.2 Growth and Maturation

Children age over game-time (controlled by the Worldstate clock's `timeRatio`). Their seeds accumulate growth through experiences — formal learning, play, observed behavior, direct instruction. Personality traits shift through CharacterPersonality's experience-driven evolution system. Encounter records build the character's memory.

This is where most of a character's life is spent, and where most of the content flywheel's raw material accumulates. Growth is not a dry statistic; it is the mechanical trace of lived experience.

### 8.3.3 Social Formation

Characters form relationships (via Relationship), take on obligations (via Obligation), join factions (via Faction), develop reputations. They acquire items, earn currency, fall in love, commit crimes, practice crafts. Each of these feeds back into encounter records, personality evolution, and Lexicon discovery.

Society is not a backdrop; it is constituted by these accumulating interactions. When players encounter "a blacksmith's guild with strong traditions," that guild has those traditions because a century of blacksmith characters have accumulated encounters, passed down knowledge through apprenticeship chains, and stabilized Lexicon entries through institutional repetition (§§2.6, 10).

### 8.3.4 Reproduction

Characters can partner (various relationship types via Relationship), marry (via Contract), and have children. Procreation creates a new Character whose initial state combines parent data. The full generational mechanics (marriage contracts, inheritance law, succession politics) compose from existing services — Contract for marriage agreements, Relationship for family bonds, Currency for dowries and inheritance, Item for heirlooms, Quest for succession narratives.

### 8.3.5 Aging

As characters age, their physical capabilities shift (modeled via Status effects and CharacterPersonality trait evolution). Fighting ability may decline; wisdom may grow. Late-life characters often accumulate significant history, making their eventual deaths narratively denser (and therefore more generative for the flywheel).

Character Lifecycle manages the aging clock per character, triggering transition events at appropriate ages — maturity, middle age, elder status. These events publish to the event bus and can be consumed by any service that cares (e.g., a god-actor might notice when a significant character reaches elder status and orchestrate recognition scenarios).

### 8.3.6 Death

Eventually, every character dies. Death can come from old age (biological termination of the aging clock), combat (via game-mechanical damage resolution), disease, accident, or narrative consequence (a divine intervention, a scenario outcome, a player choice).

Death is not a failure state. It is a narrative event with its own gameplay, followed by archive compression and content flywheel contribution. The mechanics of death are substantial enough to warrant their own dedicated subsystem, covered in the rest of this section.

## 8.4 Plot Armor as Narrative Pacing

### 8.4.1 The Mechanism

Most fantasy games treat death as binary: the character survives this combat, or they do not. Arcadia introduces a more sophisticated mechanism: **plot armor**, a continuous float tracked via Status that prevents character death while above zero.

Plot armor is not hitpoints. It does not shield against damage. It is a **narrative pacing variable** that god-actors read when deciding whether to allow lethal outcomes.

The rule: **while `${status.plot_armor} > 0`, no scenario outcome, cinematic branch, or combat exchange may result in the character's death.**

Importantly, this rule is not enforced by any service. It is a behavioral convention that all god-actors follow in their ABML. A scenario with a lethal branch exists in the registry regardless; the god simply doesn't select it for a protected character, or if a lethal outcome occurs (QTE failure), the god intervenes before death resolves.

### 8.4.2 Sources and Depletion

Plot armor sources:

- **New character grant** from Gardener — full armor at character creation, training wheels for new spirits.
- **Guardian spirit growth** — certain spirit domains periodically refresh armor for their bound characters.
- **Divine blessing** — gods grant armor to favorites, especially characters whose lives are "interesting."
- **Generational reset** — new generation characters start with fresh armor.
- **Fulfillment deficit** — characters with unfinished business retain armor because the narrative "wants" them to complete their arc.

Plot armor depletes when characters survive dangerous situations. The chip amount depends on:

- **Danger intensity** — near-miss with a raid boss chips more than a tavern brawl.
- **God personality** — merciful gods chip slowly; aggressive gods chip quickly. The god's `${personality.mercy}` axis influences depletion rate.
- **Narrative position** — if the character is in the climactic phase of a storyline, the god may preserve armor. If they have no active storylines, depletion accelerates.
- **Player agency** — high combat agency means less divine protection; the player can protect themselves.

### 8.4.3 Deus Ex Machina Manifestations

When a character with plot armor faces a lethal outcome, the god-actor intervenes. The intervention style varies by god:

| God Type | Manifestation | Example |
|---|---|---|
| **Fate-weaving (Moira)** | Subtle redirection of causality | The blade glances off bone. A cobblestone causes the enemy to stumble. |
| **War-domain** | Battle fury beyond mortal limits | Adrenaline surge; defensive move the character "didn't know they could do." |
| **Nature-domain** | Environment acts | A beast crashes through the wall. Roots trip the attacker. |
| **Death-domain** | "Not yet" | The killing blow connects but death is refused. Unsettling for witnesses. |
| **Commerce-domain** | Lucky coincidence | A coin in the pocket deflects the dagger. |

Each manifestation is an ABML behavior fragment the god-actor selects. Every manifestation is recorded as an encounter — the character's archive accumulates "the time Fate saved me from the dragon," "the moment Death whispered not yet." When the character eventually does die, their archive is rich with these near-misses, producing dense narrative seeds.

### 8.4.4 Plot Armor and the Dramatic Pacing Thesis

The purpose of plot armor is to ensure **characters die at dramatically appropriate moments rather than randomly**. When plot armor finally depletes and a character falls, that death is earned — preceded by escalating near-misses that built tension, created memorable encounters, and enriched the archive.

This is the opposite of permadeath punishment design. It is narrative pacing as a mechanical feature, enabling the content flywheel to generate dramatically satisfying content rather than random tragedies.

## 8.5 The Three Phases of Death

When plot armor reaches zero and the character finally falls, death is not an instant. It unfolds across three distinct gameplay phases, each with its own mechanics and emotional purpose.

### 8.5.1 Phase 1: The Fall

The character falls. A "Death of a Hero" cinematic plays — appropriate music, appropriate camera work, appropriate text. The player knows this is death. No ambiguity.

Architecturally, the Fall corresponds to the first moment of the Underworld degradation timeline: the shell has ruptured, but the logos haven't scattered yet. Everything that follows happens within a degradation window (seconds to minutes of game time) where interventions are still possible.

Other participants witness the Fall in real-time. Each witness gets a Character Encounter record. The death is a **spectacle** — not a failure screen, but a narrative climax occurring in public.

### 8.5.2 Phase 2: The Last Stand

After the death cinematic completes, the player does *not* get a "return to menu" screen. Instead, gameplay continues in a transformed state. The guardian spirit — now unbound from the dying character's shell — has a brief window of heightened agency before the logos fully scatter.

The metaphysical justification: the spirit is a divine shard of Nexius. When the pneuma shell ruptures, the spirit is momentarily free in a way it was not while bound. Freedom manifests as gameplay possibilities:

- **Spirit form assistance** — the spirit manifests near allies as a visible presence, granting temporary buffs (via Status), revealing threats (via perception injection into allied actors), amplifying ally autonomy (increased co-pilot influence weight).
- **Temporary possession** — the most dramatic option. The spirit briefly inhabits a nearby uncontrolled NPC, applying its full accumulated agency to that NPC's capabilities. The NPC's personality resists (brave NPCs welcome the presence; cowardly ones fight it), creating mechanical friction and dramatic tension.
- **Watcher presence** — the character's dying body exists as a ghost at the death scene, able to perceive but not interact. The player watches the world react in real-time.

During the Last Stand, gods are evaluating. A god-actor's ABML scores the spirit's post-death actions (allies buffed, threats revealed, possession combat performance, allies saved from death). This valor score weights the god's resurrection decision — performance in the Last Stand determines what happens next.

### 8.5.3 Phase 3: Resolution

The Last Stand ends in one of three ways:

**Resurrection (Changed)** — A god intervenes, spending divinity to reform the shell. The character returns, but marked:

| Valor | Transformation Severity | Example |
|---|---|---|
| High (> 0.8) | Cosmetic only | A white streak in the hair. Eyes that glow faintly. |
| Medium (0.4-0.8) | Functional | One arm weaker but the other stronger. A new phobia. |
| Low (< 0.4) | Significant | Personality axis shift. Memory gaps. Changed species traits. |

Resurrection is rare (high divinity cost) and recorded in CharacterHistory permanently: "returned from death, marked by death."

**Underworld Entry** — The spirit's energy depletes, and the character's logos enter the leyline-network underworld (an instance of the UNDERWORLD system realm). This is post-mortem gameplay with its own mechanics (§8.6), not a game over.

**Passage** — The logos flow directly to the guardian spirit. The character's sense of self dissolves. The archive compresses with maximum narrative density — the entire life, including the Last Stand performance, feeds the content flywheel.

The three paths are not equally likely. Most deaths follow Passage. Underworld Entry is common for characters with unfulfilled aspirations. Resurrection is rare. The specific outcome depends on the god's evaluation, the spirit's performance, the character's narrative position, and divinity economics.

## 8.6 UNDERWORLD Gameplay

### 8.6.1 The Afterlife as a Realm

UNDERWORLD is a system realm (per [§5.3](#53-system-realms)) inhabited by dead characters. Post-mortem gameplay is not a separate system; it is *another realm* where the character (now as a post-mortem Character in UNDERWORLD) pursues afterlife activities.

This has several architectural advantages. UNDERWORLD characters use all normal Character infrastructure — personality, encounters, history, relationships. Gods from PANTHEON can interfere in UNDERWORLD just as they do in game realms. The content flywheel treats UNDERWORLD archives the same as living-realm archives.

### 8.6.2 Aspiration-Based Pathways

The character's state at death determines their initial UNDERWORLD path:

- **Fulfilled characters** (all major aspirations completed) experience peaceful dissipation — their logos flow smoothly to the guardian spirit, with minimal afterlife gameplay.
- **Unfulfilled warriors** (combat-focused, died in battle) enter a Valhalla-like pathway — battle challenges to preserve memories and earn soul currency.
- **Unfulfilled craftsmen/scholars/diplomats** enter pathway-specific challenges matching their life's work — a blacksmith faces forging trials, a diplomat faces negotiation puzzles.
- **Characters with unresolved bonds** enter a path oriented around those bonds — the grief of a lost spouse, the unfinished mentorship of a lost student.

### 8.6.3 Soul Currency

Afterlife accomplishments produce **soul currency** — a resource spendable on:

- **Resurrection** (highest cost; typically requires ally-cooperation via the Orpheus pattern).
- **Blessings for living household members** (moderate cost).
- **Artifacts imbued with the character's essence** (moderate cost; created items appear in living realms).
- **Spirit shards for trait inheritance** (variable; enhances a descendant's starting traits).

### 8.6.4 The Mobile Ethereal Base

The guardian spirit recreates the living world's home at leyline locations within the underworld. Items sacrificed by living household members on the surface appear in the underworld base. This gives UNDERWORLD characters a familiar space and creates ongoing cross-realm interaction between living and dead household members.

### 8.6.5 The Orpheus Journey

In extreme cases, a living character can attempt to descend into the underworld to rescue a dead character. The journey is high-risk (the living character risks death themselves), aspiration-driven (it requires specific narrative conditions to become available), and produces some of the most dramatic content in the game.

## 8.7 Exit Paths: Retirement and Transcendence

Death is not the only exit from active play. Two voluntary paths exist and are mechanically encouraged.

### 8.7.1 Retirement

Characters who achieve their greatest aspirations can **retire** — the guardian spirit releases its hold. The character remains in the world as an autonomous NPC running their own brain, but they are no longer the player's inhabited character.

Retirement is the cleanest exit:

- The retired character's business, relationships, property persist as simulation state.
- New players encounter them as NPCs with genuine history.
- The retired character may appear in future generations as elder, mentor, legend.
- Their CharacterHistory captures the retirement as an event.

Clean retirements produce the best legacy bonuses — incentivizing the player to conclude a character's arc rather than "dragging things out."

### 8.7.2 Transcendence

Beyond retirement: **transcendence**. The guardian spirit and the character, after a full life, choose together to merge. The character's sense of self dissolves directly into the spirit's growing divine consciousness.

Mechanically:

| Exit Type | Spirit Growth | Archive Quality | Soul Currency | Trait Transfer |
|---|---|---|---|---|
| Random death | Base | Standard | Standard | Standard shards |
| Heroic death | Base × multiplier | High (dramatic) | High | Enhanced shards |
| **Retirement** | High (fulfilled) | Full (no degradation) | N/A (alive) | Living legacy |
| **Transcendence** | Maximum | Perfect (direct integration) | Maximum | Deep integration |

Transcendence produces greater spirit progression than any other exit. It is a ritual moment — a cinematic event where the spirit and character acknowledge the life is complete. Other characters can witness it.

The Fulfillment Principle underlying these exits: **more fulfilled in life = more logos flow to guardian spirit = greater household progression**. This mechanical incentive aligns player choice with narrative completion, rather than with "keeping characters alive forever."

### 8.7.3 Why "Cannot Delete Characters"

Characters cannot be deleted by player action. Intentionally getting a character killed is harder than it sounds — the NPC brain fights to survive, other characters may intervene, and gods may protect characters they find narratively interesting regardless of the player's intent.

This prevents the degenerate strategy of farming character deaths for content and forces engagement with retirement or transcendence for players who want to move on. It also reinforces the "characters are independent entities" design principle — the player cannot simply discard an inconvenient character.

## 8.8 Generational Inheritance

### 8.8.1 Genetic Inheritance

When a child is conceived, their initial trait predispositions are computed from both parents' archives (or live data). The formula weights each parent's personality traits with variance, modulated by:

- **Environmental context** at conception (ancestral trauma can produce elevated stress baselines).
- **Species** (realm-scoped species may have distinctive inheritance patterns).
- **Recent parent experiences** (a parent recently traumatized may produce descendants with related predispositions).

### 8.8.2 Memetic Inheritance

Beyond genetics, children inherit:

- **Ancestral memories** — occasionally surfacing in dreams, meditation, or stress. Archive data from ancestors can emerge as déjà vu, forgotten-language recognition, or technique intuitions.
- **Family reputation** — dead ancestors' events still affect living descendants. "Your grandmother was a hero of the Stormgate; you're welcome here." "Your grandfather betrayed us; prove yourself different." Faction reputation modifiers decay with generations but persist.
- **Inherited obligations** — dead character's unresolved obligations pass to descendants as starting context. Unpaid debts, honor-pledges, dynastic commitments all carry forward via Contract.
- **Heirloom items** — passed down via Contract-managed inheritance. Items can accumulate their own lineage-based bonuses (activating differently for descendants of their original wielder).

### 8.8.3 Dynastic Continuity

Over multiple generations, dynasties form. A household that has run a business for three generations has accumulated capital, reputation, relationships, and institutional memory that no new household can match. Faction membership passes down. Guild affiliations stabilize across generations. Political positions become hereditary through Contract-managed succession.

Dynasties are the mechanical result of the generational system. They are not scripted as "dynastic NPCs"; they emerge from the accumulating state of characters who happen to share lineage.

## 8.9 Realm History and Saeculum Structure

### 8.9.1 Realm History as Civilizational Memory

**Realm History** (`lib-realm-history`, L4) is the civilizational-scale counterpart to CharacterHistory. It records:

- World events (wars, treaties, plagues, disasters, discoveries).
- Participant roles (which characters were heroes, villains, victims, witnesses).
- Significance scores (how important the event is for the world).
- Temporal context (when it happened, what preceded and followed).

Realm History is the layer above individual character lives — the events that involve thousands, that reshape regions, that are remembered as "the history of the realm."

### 8.9.2 Saeculum Structure

Per VISION.md's scale targets: Arcadia's generational cycle operates on **80-100 year saeculums** with 4 turnings (inspired by Strauss-Howe generational theory). This produces a civilizational rhythm:

- **First turning (spring, ~20 years)**: Institutional order, postwar recovery, generational optimism.
- **Second turning (summer, ~20 years)**: Cultural flowering, spiritual awakening, individualism rising.
- **Third turning (autumn, ~20 years)**: Institutional decay, cultural fragmentation, crisis brewing.
- **Fourth turning (winter, ~20 years)**: Crisis, upheaval, dramatic transformation, new institutional order emerging.

This structure is not hardcoded; it is emergent from the interaction of many characters across generations, curated by long-lived Regional Watchers. But it provides a design *template* for realm designers — realms can be configured to run this rhythm naturally, producing historical sediment that feels genuinely cyclical rather than randomly varied.

### 8.9.3 Time Rate Implications

Recall from [§3.2.5](#325-realm-time-worldstate-clocks) that each realm has its own `timeRatio`. A realm with `timeRatio: 120` (roughly 2 game-hours per real-minute) completes a saeculum in ~30 real days of continuous play. A realm with `timeRatio: 1.0` would require 80+ real years — impossibly slow for anything but specific-purpose realms.

Most game-playable realms sit at `timeRatio` values that produce meaningful generational play within typical player lifetimes — `60` to `240` — balancing between "time feels meaningful" and "multiple generations occur within a playable campaign."

## 8.10 The Complete Cycle

Stepping back, a character's full arc in Arcadia is:

```
Birth (lineage + environmental context shapes starting traits)
  ↓
Growth (seeds accumulate via experiences)
  ↓
Maturation (personality stabilizes through CharacterPersonality evolution)
  ↓
Social formation (Relationship, Faction, Obligation, Contract entanglements)
  ↓
Peak life (accomplishments, influence, legacy-building)
  ↓
Aging (physical capabilities shift; wisdom accumulates)
  ↓
Death OR Retirement OR Transcendence
     ↓
  ┌─────────────────────────┐
  ▼                         ▼
 The Fall                (Retirement: NPC autonomy)
  ↓                       (Transcendence: spirit merge)
 Last Stand
  ↓
 God's Judgment
  ↓
 Resolution:
  ├── Resurrection (changed)
  ├── UNDERWORLD (afterlife gameplay)
  └── Passage (archive compression)
       ↓
    Content flywheel ingests archive
       ↓
    Future narratives, quests, NPCs, mementos reference this character
       ↓
    Descendants inherit traits, reputation, obligations
       ↓
 (Next generation character begins the loop)
```

Every stage is gameplay. Every stage generates content. Every stage touches multiple services. Every stage reinforces the core commitment: **death creates, not destroys**. Characters are not game pieces to be preserved; they are lives to be lived fully, remembered richly, and passed on meaningfully.

## 8.11 Connecting to the Rest of the Document

Life-death-generations touches many other sections:

- [§7 (Content Flywheel)](#7-the-content-flywheel) — the most direct relationship; death feeds archives feed scenarios.
- [§9 (Economic Substrate)](#9-the-economic-substrate) — inheritance (Contract-managed) is a major economic mechanism; dynastic wealth accumulation is generational.
- [§10 (Social Fabric)](#10-the-social-fabric--cultural-emergence) — cultural emergence happens across generations; social norms stabilize or shift based on cohort experiences.
- [§11 (Combat & Cinematic)](#11-combat--cinematic-choreography) — the "Death of a Hero" cinematic is a scenario type; combat lethality interacts with plot armor.
- [§13 (Actor-Bound Entities)](#13-actor-bound-entities-living-places-and-things) — all actor-bound entities have their own lifecycles analogous to character lifecycles; a weapon "dies" when shattered, a dungeon when collapsed.
- [§18 (Designing Underworld Realms)](#18-designing-underworld--afterlife-realms) — design guide for realm variations of the UNDERWORLD archetype.

The key claim: **a game of Arcadia is not a single character's story. It is a lineage across generations within a saeculum within a realm's long history**. The player's guardian spirit persists through all of it, accumulating understanding while their inhabited characters live, die, and pass on. This is what makes Arcadia "living worlds" in a way that individual character permadeath games are not — the permanence is in the world and the spirit, not in the character.

---

# 9. The Economic Substrate

## 9.1 What Makes This Economy Different

Most game economies are explicit systems: an auction house, a vendor catalogue, a currency ledger, a tax collector. They are bolt-ons — rules imposed on a world that, absent those rules, would have no economy at all.

Arcadia's economy is different. It is **emergent** — produced by the interaction of foundational primitives (Currency, Item, Inventory, Contract, Escrow) with autonomous NPC behavior (ABML + GOAP + variable providers). When you see an "auction house" in Arcadia, it is not a dedicated auction-house subsystem; it is a composition of Escrow (holding bid currency and auction items), Contract (governing the auction's state machine), Currency (mediating bids), Item/Inventory (representing the goods), and a thin Market plugin that coordinates between them. When you see NPCs "running a business," there is no "business management system" — there are NPCs with personality-driven GOAP goals, currency wallets, inventory containers, and craft capabilities, producing business behavior by composition.

This is the composability thesis applied to economics: no dedicated "economy subsystem," just primitives that combine. The result is an economy that can be **varied per realm** (FANTASIA realms with feudal economies, ARCADIA realms with enchantment-industrial economies, etc.) without re-engineering.

This section develops the full substrate: the five foundation services ([§§9.2-9.6](#92-currency-the-universal-ledger)), the feature services that compose them ([§9.7](#97-feature-services-on-top-of-the-substrate)), NPC economic participation ([§9.8](#98-npc-driven-economies)), divine economic intervention ([§9.9](#99-divine-economic-intervention)), and information-as-commodity ([§9.10](#910-the-information-economy)). Implementation specifics are in the [ECONOMY-SYSTEM guide](guides/ECONOMY-SYSTEM.md); this section covers the architectural shape.

## 9.2 Currency: The Universal Ledger

**`lib-currency` (L2)** is the foundation of all economic operations. It provides:

- **Wallets** — owned by any entity (Account, Character, Item, Location, Genesis entity). Entity-agnostic; a dungeon's mana wallet uses the same API as a player's gold wallet.
- **Currency definitions** — typed currencies with properties (realm-scoped or global, per-wallet caps, exchange rates, autogain rates, minting/burning policies).
- **Transactional operations** — credit, debit, transfer with atomic guarantees. Transaction types (quest reward, vendor sale, loot drop, tax, tithe) for analytics categorization.
- **Authorization holds** — reserving funds without immediately debiting, for use in auctions, escrows, and multi-step commitments.
- **Autogain** — passive accumulation over game-time (used for dungeon mana, genesis entity wallets, any resource that grows ambiently).
- **Exchange rates** — cross-currency conversion with location scoping and modifier stacking.

The 33 endpoints of lib-currency cover every transactional pattern an economic system needs. Higher-layer services compose these operations; none bypass them.

## 9.3 Item and Inventory: The Dual Model

### 9.3.1 Item Templates vs. Instances

**`lib-item` (L2)** separates "what an item *is*" from "a specific occurrence of that item":

- **Item templates** define types (Iron Sword, Healing Potion, Arcane Scroll). Properties: category, rarity, quantity model, stack size, durability rules, binding policy.
- **Item instances** are specific occurrences. Properties: template reference, current stats, affixes, durability state, binding state, origin tracking, custom stats (client metadata).

The separation matters architecturally. Templates are shared — a realm has thousands of instances of "Iron Sword" but one template. Instances carry per-item state that the template does not care about. This prevents template-level contention under load (a thousand weapon sales don't modify the template; they each create or modify an instance).

### 9.3.2 Inventory as Container Layer

**`lib-inventory` (L2)** is the container layer — the relationship between items and the entities that hold them. Inventories have:

- **Constraint models** — unlimited (abstract holdings), slot-based (fixed positions), weight-based (carry capacity), grid-based (2D spatial), volumetric (3D). Each model enforces different placement rules.
- **Nested containers** — a backpack inside an inventory, a chest inside a house, a vault inside a castle. Containment is tree-structured.
- **Operations** — add, remove, move, split, merge, transfer. All atomic.

The Item/Inventory separation mirrors the Currency wallet separation. What a thing is (Item template). Where a thing is (Inventory). Together they form the noun-and-location layer of the economy.

## 9.4 Contract: The Agreements Engine

**`lib-contract` (L1)** is the generic state-machine engine for binding agreements. At L1 because "parties make agreements with milestone progression" is foundational infrastructure — like a database or message bus, but for multi-party consent flows. Quests, marriages, apprenticeships, escrows, treaties — all compose on top of Contract.

A Contract has:

- **Template** — defines phases, milestones, consent requirements, breach conditions, cure periods.
- **Parties** — entities bound by the contract (characters, factions, organizations).
- **State machine** — progression through milestones, each requiring specific state transitions.
- **Prebound APIs** — callbacks that fire at specific milestones (e.g., "on completion, call `/currency/credit` with these parameters").
- **Breach/cure mechanics** — what happens when a party fails an obligation, and how the breach can be remediated.

Contract is the *brain* of multi-party agreements. It does not hold assets (that is Escrow's job); it holds the *rules* that determine what happens to assets under various conditions.

## 9.5 Escrow: The Vault with a State Machine

**`lib-escrow` (L4)** is the asset custody layer. When parties need to exchange assets atomically — goods for gold, service for reward, multi-stage delivery — Escrow holds the assets until conditions are met.

Escrow's 13-state FSM covers every failure mode of multi-party asset exchange:

- **Creation** states (Pending, Created, DepositAwaiting).
- **Deposit** states (PartyADeposited, PartyBDeposited, BothDeposited).
- **Consent** states (PartyAConsented, PartyBConsented, BothConsented).
- **Resolution** states (ReleaseAuthorized, Released, RefundAuthorized, Refunded, Disputed).

This is not over-specification. Every state corresponds to a real failure scenario (party fails to deposit, consent withdrawn mid-transaction, conditions violated at release time, dispute arbitration in progress). The 13 states are the minimum needed to handle the full space without ambiguity.

Escrow's contract-bound mode delegates release/refund to a bound Contract's prebound APIs. Together, Contract + Escrow produces the "brain and vault" pattern — Contract decides *what should happen*, Escrow *holds the assets* and executes Contract's decisions.

## 9.6 Faucet/Sink Discipline

Every healthy game economy is a system of flows:

- **Faucets** — sources where currency enters (quest rewards, loot drops, vendor sales, treasure discoveries, autogain).
- **Sinks** — drains where currency exits (vendor purchases, fees, repair costs, taxes, offerings).

The cardinal rule: **every faucet has a corresponding sink.** Unbalanced faucets cause inflation; unbalanced sinks cause deflation. Currency's transaction types (quest_reward, vendor_sale, tax_collected, etc.) enable analytics to distinguish faucet and sink operations and monitor the balance.

**Redistribution over creation**: Most economic operations should *move* currency between entities rather than creating or destroying it. A god intervening to stimulate a stagnant village prefers arranging a trade opportunity (redistribution) over placing a treasure cache (faucet). True faucets and sinks are reserved for structural adjustments, maintaining the long-term meaning of currency.

## 9.7 Feature Services on Top of the Substrate

Five L4 feature services compose the L2/L1 substrate into recognizable economic gameplay:

### 9.7.1 Affix

**`lib-affix` (L4)** provides item modifiers — the "+5 Fire Damage" on an enchanted sword, the stat bonuses on magical armor. Affixes are typed definitions with tiers, mod groups (for exclusivity — two fire enchantments conflict), spawn weights (for procedural generation), and stat grants.

Affix gives *structure* to Item's opaque `instanceMetadata` field. Without Affix, enchantments would be arbitrary JSON blobs with no cross-consumer validation. With Affix, enchantments are typed entities that Craft can apply/remove, Loot can generate, Market can index for search.

Per [§14](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology), Affix is also the mechanism by which the Equipment Enchantment Duality is implemented — self-augmenting enchantments (Type 1) apply their stat grants to the item itself; intent-channeling enchantments (Type 2) apply them as Status effects on the wielder.

### 9.7.2 Craft

**`lib-craft` (L4)** orchestrates recipe-based crafting — converting input materials into output items via time-gated, skill-gated, station-gated sessions. Craft composes:

- **Item** for inputs/outputs.
- **Inventory** for material consumption and product placement.
- **Contract** for multi-step session state machines.
- **Currency** for crafting fees.
- **Affix** for procedural output variation.

Game-agnostic: recipe types, proficiency domains, station types, tool categories are opaque strings defined per game through recipe seeding. A "blacksmithing" realm and a "cybernetic engineering" realm use the same Craft infrastructure with different seed data.

### 9.7.3 Loot

**`lib-loot` (L4)** generates rewards. Loot tables specify weighted drop entries with contextual modifiers (region, season, character level, luck stat). Pity thresholds prevent streaks of unlucky pulls from becoming punishing. Loot composes Item (for the rewards) and Affix (for procedural stat rolls on equipment).

### 9.7.4 Market

**`lib-market` (L4)** provides auctions, NPC vendors, and price discovery. Three subsystems:

- **Auction house** — bid engine with authorization holds (Currency), background settlement.
- **NPC vendor system** — three pricing modes: static, dynamic formula, personality-driven GOAP.
- **Price analytics** — time-bucketed history, trend detection, supply/demand indicators.

Provides `${market.*}` and `${market.price.*}` variable providers for NPC economic GOAP (see §9.8).

### 9.7.5 Trade

**`lib-trade` (L4)** handles trade routes, shipment tracking, border crossings, tariff and tax policies, contraband, supply/demand dynamics. Uses Transit (L2) for geographic connectivity — "goods move from A to B over X game-hours along this route" — and Worldstate (L2) for game-time duration.

Trade's declarative lifecycle pattern (the plugin records state, the game drives events via API calls) mirrors Contract's milestone pattern — the plugin is infrastructure, not a micromanager.

### 9.7.6 Workshop

**`lib-workshop` (L4)** provides time-based automated production. Unlike Craft (which is active, session-based), Workshop runs in the background — a blueprint assigned to workers with a source inventory and destination inventory produces outputs over game-time.

Workshop uses **lazy evaluation**: production is not simulated continuously. When a query comes in ("how many iron bars in this workshop's output inventory?"), Workshop computes what *would* have been produced given elapsed game-time since last evaluation, up to capacity limits. This scales: thousands of workshops can run with zero per-tick CPU cost because they are only evaluated on read.

The [Location-Bound Production](planning/LOCATION-BOUND-PRODUCTION.md) design shows how Workshop + Transit can compose into Factorio-style factory networks: sub-locations hold stage-based inventories, Workshop blueprints drive time-based transformation between stages, Transit moves intermediate goods between stages, and all of it happens lazily with no server ticks for dormant facilities.

## 9.8 NPC-Driven Economies

### 9.8.1 The Principle

Arcadia's economy is **NPC-driven, not player-driven**. If every player logged off simultaneously, the economy would continue. NPCs produce goods, consume resources, trade, set prices, and respond to supply/demand — the world's economy is constituted by their behavior, not by player transactions.

This is what enables the vision's commitment to "living worlds whether or not a player is watching." Players layer on top of an existing economic substrate rather than being its sole participants.

### 9.8.2 How NPCs Participate Economically

NPCs have economic roles (merchant, craftsman, farmer, consumer) encoded through their ABML behavior documents and their Disposition drives. A typical NPC economic cycle:

1. **Perception** — the NPC's Actor receives market events (via `${market.*}` variable providers), observes their own inventory state (`${inventory.*}`), and tracks their financial position (`${currency.*}`).
2. **Drive evaluation** — Disposition provides drives (gain_wealth, provide_for_family, master_craft, earn_respect). Drives have urgency levels that shift based on unmet needs.
3. **GOAP planning** — when drives are active, GOAP plans action sequences: "I need food → I have iron bars → sell iron bars at market → buy food with proceeds."
4. **Execution** — ABML action handlers call Currency, Item, Inventory, Craft, Market APIs to realize the plan.
5. **Adaptation** — outcomes feed back into Character Personality (successful aggressive pricing makes the NPC more confident), Character Encounter (market interactions with other NPCs), and Disposition feelings (trust in customers, resentment toward suppliers).

The result: emergent market behavior. When iron becomes scarce, blacksmiths' inventories dwindle, their Craft sessions require more materials, they buy at higher prices, and they sell finished goods at higher prices. Market aggregation reflects this as rising iron and sword prices. Other NPCs notice and adjust: miners step up production, merchants reroute caravans, consumers substitute alternatives.

### 9.8.3 The NPC Economic Profile

Each NPC's economic behavior is shaped by:

- **Personality traits** — greed, frugality, risk tolerance, trustworthiness.
- **Economic role** — merchant / craftsman / farmer / consumer.
- **Current state** — inventory contents, currency balance, current goals.
- **Social context** — relationships, faction memberships, current obligations.
- **Environmental context** — realm economy health, location-specific supply/demand, seasonal factors.

All of these are available to the NPC's Actor via variable providers. The GOAP planner composes them into decisions. The Disposition service translates long-term drives into urgent needs. The result is bounded-rational economic behavior that looks emergently intelligent without any single service encoding "market logic."

### 9.8.4 Scale Strategy

Not all NPCs can be economically active simultaneously at 100K scale. Key strategies:

- **Lazy wallet creation** — NPCs don't get Currency wallets until they transact (via `/currency/wallet/get-or-create`).
- **Template-based defaults** — NPCs derive baseline wealth from species/role templates; individual state materializes only when they actually transact.
- **Tick-based processing** — Economic decisions run periodically (every 5-15 game-minutes), not every frame.
- **Regional aggregation** — NPCs share market intelligence by location via cached regional price data (`market:{realmId}:{templateCode}:price`).
- **Workshop lazy evaluation** — dormant production facilities cost zero server ticks.

These strategies let an Arcadian realm host thousands of economically-active NPCs without saturating the mesh, while allowing individual characters to still be intelligent economic agents.

## 9.9 Divine Economic Intervention

### 9.9.1 The Invisible Hand

Traditional game economies use direct manipulation (adjusting drop rates, vendor prices) to control health. Bannou's approach: **specialized divine actors observe economic metrics and spawn narrative events that adjust velocity through NPC reactions**.

An economic deity is a Regional Watcher (§6.6) tuned to economic events — a Commerce God, a Wealth God, a Poverty God, a Thief God, a Balance God. Each has:

- **Domain** — what economic events they care about (trade, prosperity, misfortune, deception, retribution).
- **Personality** — subtlety (invisible intervention vs. obvious miracle), chaos (frequent vs. rare), bias (favors wealthy, favors poor, favors specific factions).
- **Intervention repertoire** — ABML behavior fragments for specific intervention types.

### 9.9.2 Intervention Event Types

Economic interventions fall into three categories:

**Redistribution** (currency moves, not created/destroyed — preferred):

- Dropped wallet, pickpocket, inheritance, business deal, debt collection.

**True faucets** (currency created — used sparingly):

- Treasure discovery, royal grant.

**True sinks** (currency removed — essential for inflation control):

- Tax collection, disaster repair, offerings/tithes.

Each intervention is an ABML behavior that the god-actor selects. The same underlying event (velocity drop in Riverside Village) can trigger different interventions from different gods based on personality — Commerce God spawns a trade opportunity, Poverty God spawns a curse, Thief God spawns a heist.

### 9.9.3 Spillover Effects

When a god injects currency into a location, spillover is inevitable and desirable. NPCs don't hoard windfall; they spend according to their GOAP goals. A farmer who finds a wallet spends on a new plow; the blacksmith who sells the plow then buys iron from a traveling merchant; the merchant restocks in the city — velocity ripples outward from the injection.

This is the economy working as designed. Gods don't try to contain spillover; they track velocity at multiple granularities (per-currency, per-realm, per-location) to observe ripple effects and adjust future interventions.

### 9.9.4 Intentional Stagnation

Not every location should be economically healthy. Dead towns, abandoned quarters, and frontier regions may be *intentionally* stagnant for narrative purposes. Locations can have a divine attention level (active, passive, abandoned, cursed). Only when characters take significant action (clearing a monster, reopening a mine) does divine attention return and revitalization follow.

## 9.10 The Information Economy

### 9.10.1 Knowledge as Commodity

Per the [Information Economy](planning/INFORMATION-ECONOMY.md) design, **information is a first-class economic commodity in Arcadia** — reified as physical "itemized contract" items that inject Hearsay beliefs on consumption. Information follows the same physical logistics as any tradeable good: it must be discovered, recorded, transported, and can be stolen, forged, or destroyed.

This is enabled by the Lexicon plugin's structured ontology (§2.4, §10). When a character discovers something (a dungeon's location, a political secret, a crafting technique), the discovery is a Lexicon-based claim. That claim can be:

- **Recorded** into an item (a map, a journal, a contract).
- **Traded** like any other item through Market and Trade.
- **Consumed** by other characters, injecting the claim into their Hearsay (with trust based on the source's credibility).
- **Forged** — a fake map produces false Hearsay beliefs.
- **Stolen** — an information item in an inventory is subject to theft like any other item.

### 9.10.2 Expertise Differentials

Lexicon's discovery-tier model creates natural expertise differentials. A master cartographer has deeper Lexicon access in cartographic domains than a journeyman. This difference is observable and priceable:

- A cartographer's map has higher fidelity (contains more specific Lexicon entries) than an amateur's map of the same terrain.
- Merchants commissioning maps pay premiums for cartographers with reputation (measurable via seed growth in `cartography.*` domains).
- GOAP planning treats information quality as a variable in decisions — an NPC party considering a dungeon dive may hire a zoologist specifically for their expertise about dungeon wildlife.

### 9.10.3 NPC Party Composition

Information economy produces a striking emergent effect: **NPC parties include non-combat specialists when the economic math favors it**. A dungeon dive might be more efficient with a cartographer, a zoologist, and an archaeologist than with additional combatants, because their expertise reduces time and risk. GOAP planners compose parties accordingly.

This is not scripted party composition; it is the result of information-as-commodity combined with GOAP optimization. It produces the rich supporting-cast ecology that makes Arcadia's NPC populations feel specialized rather than homogeneous.

## 9.11 Divinity Generation: The Bag-of-Kills Pattern

### 9.11.1 The Economic Substrate for Gods

Per the [Divinity Generation Architecture](planning/DIVINITY-GENERATION-ARCHITECTURE.md), the economic substrate extends to metaphysical entities. Divinity — the "currency" that gods spend on blessings and interventions — is generated through **Seed bond propagation** and **Currency wallet accumulation**, not through Analytics event subscriptions.

The mechanism: a character's domain seed (e.g., "devotion to the god of commerce") bonds to the god's domain seed. When the character performs relevant acts (successful trades, new business ventures, fair dealings), growth is recorded on the character's seed. Seed's configurable bond propagation ratios transfer a portion of that growth to the god's seed. The god's seed accumulates; divinity is the result.

This is the **"Bag of Kills" pattern** — multi-consumer accumulation where the same wallet (the god's divinity wallet) is consumed independently by multiple systems (divine interventions, blessings, avatar manifestations). Each system withdraws from the wallet without contention; the wallet accumulates from many sources without needing to know about the consumers.

### 9.11.2 Why This Is Important

The bag-of-kills pattern generalizes. It is not just for gods — it is the structural pattern for any case where many small contributions accumulate into capability that many consumers can spend. Dungeons accumulate mana from deaths within their domain; the mana feeds multiple spending systems (spawning monsters, activating traps, shifting layout, memory manifestation). Faction treasuries accumulate from member tithes; the treasury funds multiple faction activities. Workshop inventories accumulate production output consumed by multiple downstream stages.

The mechanism — Currency wallet as accumulator, with growth mappings for "what feeds it" and independent spending rules for "what draws from it" — is universal infrastructure. It composes naturally with the Genesis lifecycle ([§5.4](#54-the-genesis-plugin)) because Genesis entities *are* systems of currency wallets and growth mappings.

## 9.12 Realm-Scoping and Cross-Realm Transfer

### 9.12.1 Currencies Are Realm-Scoped by Default

Most currencies are realm-scoped (per [§3.3](#33-realm-scoped-primitives)). A Fantasia-gold currency exists in Fantasia; an Arcadia-silver exists in Arcadia. Character wallets hold the relevant currencies. Cross-realm transfer is explicitly forbidden except through explicit exchange mechanisms.

This enforces the vision's commitment: "knowledge transfers between realms, not resources." A player's character in Arcadia accumulates Arcadia-gold; when the player switches to a Fantasia seed, the gold stays behind. The guardian spirit's accumulated understanding crosses realms; the resources do not.

### 9.12.2 Exceptions

Some currencies *are* cross-realm by design:

- **Soul currency** (per [§8.6.3](#863-soul-currency)) — generated in UNDERWORLD by post-mortem characters, spent on cross-realm effects (blessings for living household, artifacts in living realms).
- **Spirit shards** — trait inheritance tokens that span generations and realms.
- **Divinity** — gods operating across game realms need currency that crosses them.

These exceptions are explicit in the currency definition (`scope: global` vs. `scope: realm`). Most currencies default to realm-scope.

### 9.12.3 Information Crosses, Sometimes

Information is a tricky case. Lexicon entries themselves are per-realm (each realm has its own canonical Lexicon). But a guardian spirit's *understanding* (their accumulated Seed growth) does cross realms. This produces the situation where a spirit *knows* something they can't articulate in the current realm — a combat-mastery spirit has intuitive skill even in a realm whose Lexicon lacks the specific combat primitives they trained on.

This is deliberate. The spirit's cross-realm capability is earned context, not concrete knowledge transfer. The character must learn to express the spirit's intuitions in the local realm's vocabulary.

## 9.13 What This Produces

Stepping back, the economic substrate produces several properties that make Arcadian economies feel different from typical game economies:

- **Persistence without players** — NPCs keep trading, producing, consuming whether or not any player is logged in.
- **Emergent price discovery** — no designer sets prices; they emerge from GOAP-driven supply and demand.
- **Geographic texture** — mana density, transit costs, tariffs, local factions produce regional economic differentiation.
- **Narrative coherence** — divine economic interventions have narrative weight, not just mechanical effect.
- **Information-as-commodity** — knowledge is a priced good, producing expertise ecosystems naturally.
- **Multi-generational wealth** — inheritance and dynastic accumulation produce long-term economic stratification.
- **Cross-realm discipline** — the "knowledge crosses, resources don't" rule mechanically enforced via currency scoping.

None of these properties require a "dedicated economy subsystem." They emerge from Currency + Item + Inventory + Contract + Escrow + Affix + Craft + Loot + Market + Trade + Workshop composed together, each doing its narrow job.

## 9.14 Connecting to the Rest of the Document

The economic substrate is pervasive. Most other sections invoke it:

- [§7 (Content Flywheel)](#7-the-content-flywheel) — economic scenarios are narrative seeds; wealth and trade participation feed character archives.
- [§8 (Life, Death, Generations)](#8-life-death-and-generational-cycles) — inheritance and dynastic wealth operate through Contract and Currency.
- [§10 (Social Fabric)](#10-the-social-fabric--cultural-emergence) — disposition drives and faction obligations are partly economic.
- [§13 (Actor-Bound Entities)](#13-actor-bound-entities-living-places-and-things) — dungeons, weapons, and gods all have economic substrates via Genesis wallets.
- [§14 (Equipment & Technology)](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology) — Affix is the mechanism for enchantment; enchantment economics emerge from Craft and Market.
- [§§15-18 (Realm Design)](#part-iv--realm-design-guides) — each realm's economic character (feudal, industrial, meta-game, afterlife-based) is a configuration of this substrate.

The economy is a foundation under most of the game. Get the substrate right, and a thousand emergent economic behaviors follow naturally.

---

# 10. The Social Fabric & Cultural Emergence

## 10.1 What Makes a World Feel Inhabited

A living world is not just a set of autonomous NPCs. It is a set of NPCs **embedded in social relationships**: friendships, rivalries, obligations, professional ties, factional loyalties, marriages, inheritances, gossip networks. They talk. They remember each other. They form opinions about strangers. They keep secrets, tell lies, develop cultural customs, and police each other's behavior through informal pressure.

Without this social fabric, even a well-simulated NPC population feels like isolated particles bouncing in an empty room — each doing its own thing but never forming a society. With it, even a modest NPC population feels like a lived-in world where characters are *of* somewhere, known to someone, bound by something.

This section develops the social fabric of Arcadia: the **communication layer** (Lexicon + Chat + Hearsay — [§§10.2-10.4](#102-lexicon-the-grammar-of-thought-and-speech)), the **motivation layer** (Disposition — [§10.5](#105-disposition-the-motivational-layer)), the **bond layer** (Relationship + Obligation — [§§10.6-10.7](#106-relationship-typed-bonds)), the **institutional layer** (Faction + Organization — [§§10.8-10.9](#108-faction-the-governance-layer)), the **morality pipeline** that uses all of the above ([§10.10](#1010-the-morality-pipeline-norms-into-second-thoughts)), and **cultural emergence** as the summary phenomenon ([§10.11](#1011-cultural-emergence-customs-from-material-conditions)).

The unifying thesis: **culture is not authored; it emerges from the interaction of communication primitives, motivations, bonds, and institutions operating on real material conditions, with divine actors performing curation.**

## 10.2 Lexicon: The Grammar of Thought and Speech

### 10.2.1 The Connection to Logos

The **Lexicon service** (`lib-lexicon`, L4, planned) is the ontological substrate of all social interaction. Per [§2.4](#24-the-lexicon-as-logos-thesis), the Lexicon is the computational instantiation of logos — the pure-information particles that define what things are. An NPC can only think, communicate, or recognize concepts that exist in their accessible Lexicon.

This is not merely a data structure; it is a **grammar of reality** that constrains social operations. When NPC A wants to warn NPC B about direwolves, the warning is a structured message:

```
[WARN] + [DIREWOLF] + [PACK_HUNTER] + [NORTH_GATE]
```

Every element is a Lexicon entry. The message is machine-parseable without NLP because it IS structured concept tuples. NPC B receives it and decodes directly into cognitive variables — no translation needed.

### 10.2.2 The Grammar

Lexicon messages follow a minimal structured grammar:

```
Message = Intent + Subject* + Modifier* + Context*
```

Where:

- **Intent** is a communication-type Lexicon entry — `WARN`, `INFORM`, `REQUEST`, `TEACH`, `GREET`, `THREATEN`, `TRADE_OFFER`, etc.
- **Subject** entries specify what the message is about (zero or more).
- **Modifier** entries qualify the message (urgency, certainty, temporality).
- **Context** entries specify situational elements (location, relationship, condition).

An elaborate warning: `[WARN] + [DIREWOLF] + [PACK_HUNTER] + [NORTH_GATE] + [URGENCY_HIGH] + [CERTAINTY_HIGH]` — "a pack of direwolves, definitely hunting, near the north gate, urgent."

### 10.2.3 Discovery-Gated Vocabulary

Characters can only use Lexicon entries they have discovered (recorded in their Collection). A novice can only say what they know; an expert can compose richer messages. The same concept at different discovery levels produces different expressive precision:

| Discovery Level | Available Vocabulary | Message |
|---|---|---|
| 1 (basic) | `animal`, `danger`, `fear` | `[WARN] + [ANIMAL] + [DANGER]` |
| 2 (familiar) | `canine`, `predator`, `pack` | `[WARN] + [CANINE] + [PREDATOR] + [NORTH_GATE]` |
| 3 (knowledgeable) | `direwolf`, `pack_hunter`, `noise_averse` | `[WARN] + [DIREWOLF] + [PACK_HUNTER] + [NORTH_GATE]` |
| 5 (expert) | `witch_of_the_wolves`, `summoned_by` | `[WARN] + [DIREWOLF] + [SUMMONED_BY] + [WITCH_OF_THE_WOLVES]` |

Higher discovery yields more precise communication. An expert conveys the full picture in one message; a novice needs multiple imprecise messages. Knowledge gaps drive social interaction — a novice asks a master for clarification, and the master's answer (`[TEACH] + [DIREWOLF] + [PACK_HUNTER]`) advances the novice's Collection discovery.

This is the mechanical instantiation of "you can only say what you know," and it produces emergent information asymmetry that shapes the whole social layer.

## 10.3 Chat: The Message Transport

The **Chat service** (`lib-chat`, L1) provides the transport layer for all structured communication — both Lexicon messages between NPCs and free-text chat between players.

### 10.3.1 Room Types

Chat supports custom room types registered per game service. The `lexicon` room type is registered by `lib-lexicon` on startup and enforces:

- **Discovery-gating** — sender's Collection must contain each Lexicon entry referenced.
- **Grammar compliance** — messages must conform to `Intent + Subject* + Modifier* + Context*`.
- **Category-level fallback** — if a character lacks a specific entry but knows the parent category, they can use the category code instead (expressing imprecision naturally).

Messages that fail validation are silently dropped — the NPC literally "can't express" a concept they don't know. This is the behavioral equivalent of not having the words.

### 10.3.2 Location-Scoped Rooms

Lexicon chat rooms are **location-scoped**: each location (or sub-location) has a social room that characters automatically join when present and leave when they depart. This mirrors how real social spaces work — you hear conversations happening around you.

A character arriving at the North Gate is added to `location-northgate-social`; leaving removes them. Relevant service events (chat published, permission state updated) fire automatically.

### 10.3.3 Batch Publishing at Scale

A critical performance point: NPCs do not individually call Chat APIs in real-time. The game server runs the spatial and social simulation, then batches results to Chat for persistence, validation, and downstream consumption.

At 100K NPC scale, this is a batching problem, not an event problem. A game server aggregating "30 NPCs at the market had 12 conversations this tick" sends one `SendMessageBatch` call covering all 12 messages. Chat validates each, stores them, and delivers client events to any players present.

Bannou's batched-operation pattern (present in Chat, Seed, Currency, and Hearsay) is what makes 100K-NPC social interaction actually tractable.

## 10.4 Hearsay: Belief Propagation with Distortion

The **Hearsay service** (`lib-hearsay`, L4, planned) is the belief layer. When characters receive information — by direct observation, message reception, trusted-contact report, overheard rumor, cultural osmosis, or inference (§2.7.1) — they form **beliefs**. Beliefs may be true or false, held confidently or skeptically, remembered accurately or distorted.

### 10.4.1 The Six Channels

Hearsay beliefs come through six channels, each with different initial confidence ranges:

| Channel | Source | Initial Confidence |
|---|---|---|
| `direct_observation` | The character witnessed it | 0.85-1.0 |
| `official_decree` | Authority announcement | 0.7-0.9 |
| `trusted_contact` | A specific trusted person told them | 0.5-0.7 |
| `social_contact` | An acquaintance mentioned it | 0.3-0.5 |
| `rumor` | Overheard in a crowded room | 0.1-0.3 |
| `cultural_osmosis` | Shared cultural knowledge | medium (depends on context) |

A seventh channel, `inference` (per [§7.7.2](#2723-gods-as-primitive-discoverers)), is added for self-generated beliefs produced when Lexicon discovery tiers unlock previously-invisible associations (Cryptic Trails — §10.12).

### 10.4.2 Distortion Across Social Hops

Information propagating through social networks distorts systematically. A direct witness encodes `[WARN] + [DIREWOLF] + [PACK_HUNTER] + [NORTH_GATE] + [WITCH_OF_THE_WOLVES]`. After one hop (trusted-contact), it might degrade to `[WARN] + [DIREWOLF] + [NORTH_GATE]` — the pack behavior and witch association have dropped. After two hops (social contact), it is `[WARN] + [CANINE] + [DANGER]` — specifics coarsened. After three hops (rumor), `[DANGER] + [ANIMAL]` — vague threat remains.

Distortion rules:

1. **Association loss** — elements connected by weak associations drop first.
2. **Specificity decay** — specific entries degrade to parent category codes (direwolf → canine → animal).
3. **Context stripping** — location and modifier elements drop before subject elements.
4. **Intent preservation** — the intent (WARN, INFORM) is the most stable element.
5. **Confidence reduction** — each hop multiplies confidence by `(1 - distortionFactor)`.

### 10.4.3 Personality Modulation

Hearsay distortion is not uniform; personality shapes how information is interpreted, transmitted, and retained. Eight personality axes (from CharacterPersonality) modulate three tiers of effect:

**Tier 1 — Interpretation filters** (what information *means* to the NPC):

- Openness, Agreeableness, Neuroticism, Aggression shift how claims are accepted, how sources are credited, how threats are perceived.

**Tier 2 — Quality filters** (how much signal survives retransmission):

- Conscientiousness (detail preservation, verification), Honesty (faithful retransmission vs. intentional distortion).

**Tier 3 — Distribution properties** (how information flows socially):

- Extraversion (how many people get told), Loyalty (in-group vs. out-group source credibility).

Multi-axis personality produces emergent archetypes. High-Neuroticism + Low-Agreeableness + High-Aggression = "paranoid" (hostile interpretations, amplified threats, wants to fight back). High-Conscientiousness + High-Openness + Low-Neuroticism = "scholarly" (considers multiple explanations carefully, no panic). These archetypes emerge from continuous axes, not discrete types.

### 10.4.4 Inference: Self-Generated Beliefs

The `inference` channel is Hearsay's connection to Lexicon discovery. When a character's Collection discovery tiers cross thresholds that unlock previously-invisible Lexicon associations, a Lexicon hypothesis event fires. Hearsay consumes it and creates a self-generated belief.

Example: a character studying architectural clues reaches discovery tier 4 for `darkspawn_architecture`. The tier crossing unlocks the association `darkspawn_architecture ↔ old_quarter_foundations`. Hearsay generates an inference belief: "The old quarter's foundations may be darkspawn construction."

Inference confidence is computed from association strength × source-discovery confidence × personality factor × skill factor. The same logical connection generates different confidence levels in different characters.

This is the mechanism that turns investigation into emergent gameplay (§10.12).

## 10.5 Disposition: The Motivational Layer

The **Disposition service** (`lib-disposition`, L4, planned) synthesizes the emotional-motivational layer — what characters *want* and *feel about* specific entities. It operates on two levels:

### 10.5.1 Feelings Toward Specific Entities

Disposition tracks per-character, per-target feelings across axes like **trust**, **warmth**, **admiration**, **resentment**, **fear**, **affection**, **contempt**. These are not inferred from encounter history every time; they are maintained as mutable state that encounter outcomes update.

When NPC A has high `warmth` toward NPC B, their communication changes — they share more freely, their GOAP weighs B's welfare more heavily, their memory retention of B-related events is higher. When warmth drops toward resentment, the same flows produce guarded or hostile behavior.

Feelings are the emotional texture of social interaction, and they are queryable: `${disposition.character.{targetId}.trust}`, `${disposition.character.{targetId}.admiration}`.

### 10.5.2 Drives — Long-Term Aspirational Motivation

Beyond moment-to-moment feelings, characters have **drives** — long-term motivations that create ambient communication needs and GOAP goals. Canonical drives include:

- `protect_family` — drives warnings about danger, protective behaviors.
- `master_craft` — drives apprenticeship-seeking, teaching-offering, practice.
- `gain_wealth` — drives trade initiation, price negotiation, resource acquisition.
- `earn_respect` — drives accomplishment-sharing, helpful acts, conspicuous virtue.
- `find_love` — drives courtship behaviors, compliments, proximity-seeking.
- `seek_justice` — drives accusation, rally-support, demand-action behaviors.

Drives have **urgency levels** that shift based on unmet needs. A hungry character's `survival.food` drive urgency climbs until satisfied. A lonely character's `find_companionship` drive urgency climbs over extended solitude.

### 10.5.3 Drives Create Communication

A key insight: drives *create* communication behaviors via ABML. A hungry NPC with high-urgency `survival.food` drive broadcasts `[REQUEST] + [FOOD] + [URGENCY_HIGH]` to their current social room. Nearby helpful NPCs (with appropriate Disposition feelings toward the asker and personality traits favoring helping) respond. This creates emergent social dynamics:

- Helpful NPCs respond to broadcast needs, building relationships through mutual aid.
- Selfish NPCs ignore requests, creating reputation consequences over time.
- Hungry NPCs develop resentment toward well-fed neighbors who never helped.
- Community cohesion emerges (or fragments) from the aggregate pattern of who helps whom.

Disposition is where individual motivation meets social coordination.

## 10.6 Relationship: Typed Bonds

The **Relationship service** (`lib-relationship`, L2) stores **typed bonds** between entities (characters, factions, organizations, system-realm characters):

- Relationship types (`ally`, `family`, `rival`, `lover`, `mentor`, `apprentice`, `superior`, `subordinate`, `wielder_weapon`, `deity_follower`, etc.) form a taxonomy registered as seed data.
- Relationship instances are specific bonds with metadata (start date, strength, history).
- Bidirectional uniqueness is enforced — no two bond instances of the same type between the same entity pair.

Relationship is L2 because "who is bound to whom" is foundational — Actor variable providers query it (`${relationship.has.ALLY}`), Faction uses it to track membership, Contract uses it to verify consent authority, Genesis uses it for actor-bound entity bonds.

### 10.6.1 Relationship vs. Encounter

Relationship and CharacterEncounter (§5) have overlapping concerns but distinct purposes:

- **Relationship** is the formal structure — "these entities are bound by this type of relationship." Queryable, filterable, durable.
- **CharacterEncounter** is the memory — "NPC A remembers specific interactions with NPC B, with sentiment scores."

A Relationship exists because the bond was formally established. A CharacterEncounter exists because the interaction was memorable. A family Relationship has many encounters over time. A single powerful encounter (saving someone's life) might not establish a Relationship but will be deeply remembered.

Both flow into Variable Providers. `${relationship.has.ALLY}` tells an NPC "do I have any allies?" `${encounters.sentiment.{targetId}}` tells them "how do I actually feel about this specific person?"

## 10.7 Obligation: Contract-Aware Moral Costs

The **Obligation service** (`lib-obligation`, L4) transforms **norms** (abstract rules) and **contractual commitments** into concrete GOAP action cost modifiers. It is the mechanism by which conscience becomes mechanical.

### 10.7.1 Three-Layer Design

Obligation has a three-layer architecture, each independently graceful-degrading:

- **Base** — aggregate raw penalty values per violation type from Contract behavioral clauses and Faction norms.
- **Personality enrichment** — apply trait-weighted multipliers to base penalties (honesty makes theft cost more, loyalty makes betrayal cost more).
- **Hearsay enrichment (planned)** — filter penalties by what the NPC *believes* the norms are, not what they actually are. A character convinced (incorrectly) that their faction forbids a given act will avoid that act; a character unaware of the real norm won't avoid it.

### 10.7.2 Personality Weighting

When personality data is available, Obligation enriches raw penalties:

```
weightedPenalty = basePenalty × (1.0 + avgTraitValue × 0.5)
```

| Violation Type | Relevant Traits | Effect of High Trait |
|---|---|---|
| theft | Honesty + Conscientiousness | Costs 1.5× more |
| deception | Honesty | Costs 1.5× more |
| violence | Agreeableness | Costs 1.5× more |
| betrayal | Loyalty | Costs 1.5× more |
| honor_combat | Conscientiousness + Loyalty | Dishonorable combat costs more |

An honest merchant faced with a theft opportunity sees a 1.5× penalty relative to a dishonest one. The same norm, experienced differently based on who the character is.

### 10.7.3 The Connection to GOAP

Obligation's output is a map of `${obligations.violation_cost.{type}}` values. NPC ABML behaviors reference these in the `evaluate_consequences` stage of cognition, before GOAP planning. The planner sees cost-modified actions:

```
Without morality:          With morality (honest merchant):
  steal_food: 5.0            steal_food: 5.0 + 20.6 = 25.6   ← unaffordable
  buy_food: 8.0              buy_food: 8.0 + 0.0 = 8.0        ← chosen
  beg_for_food: 12.0         beg_for_food: 12.0 + 0.0 = 12.0
```

The planner never knew about honor or conscience. It just picked the lowest-cost plan. But the planner's output is emergent moral behavior because the costs were moral.

## 10.8 Faction: The Governance Layer

The **Faction service** (`lib-faction`, L4) is the institutional layer — organizations, territories, norms, enforcement. Factions are Seed-based: they grow through member activity and unlock governance capabilities as they mature.

### 10.8.1 Seed-Based Living Entities

Each faction owns a seed. Member activities produce growth via the Collection→Seed pipeline (member trades, fights, crafts → faction commerce/war/craft domain growth). Faction capabilities gate by seed phase:

| Phase | Min Growth | Unlocked |
|---|---|---|
| Nascent | 0 | None — faction exists but has no governance authority |
| Established | 50 | `norm.define` — can define enforceable norms |
| Influential | 200 | `norm.enforce.sanctions`, `territory.claim` |
| Dominant | 1000 | Complex governance, trade regulation, succession protocols |

This is not just numerical leveling; it is **emergent institutional power**. A nascent thieves' guild literally cannot define "honor among thieves" as a binding norm. It has not grown enough governance capability. A dominant faction wields real authority because its legitimacy has accumulated through member actions over time. This mirrors real institutional formation.

### 10.8.2 Three Kinds of Factions

Factions serve as the governance and cultural layer that Locations and Realms don't provide:

| Faction Type | Scope | Example |
|---|---|---|
| **Realm baseline** | Realm-wide cultural norms | Arcadian Cultural Council |
| **Location controlling** | Local territorial norms | Harbor Authority |
| **Guild** | Organizational norms (direct membership) | Merchant Guild, Thieves' Guild |

This is how "is this territory hostile?" gets answered. Not via a Location boolean, but via which faction controls the location and what norms that faction enforces. A "lawless district" is a location where no faction has territorial control — realm baseline is the only norm source.

### 10.8.3 Norm Resolution Hierarchy

When multiple factions' norms could apply, the most specific source wins:

```
Priority 1: Guild factions (direct memberships) — HIGHEST
Priority 2: Location controlling faction (territorial)
Priority 3: Realm baseline faction (cultural) — LOWEST
```

A merchant in the Docks District has norms from (a) Merchant Guild membership, (b) Harbor Authority territory, (c) Realm Cultural Council baseline. Each violation type picks the most specific source. Moving to the Temple District shifts the territorial source — Temple Guardians replace Harbor Authority — so territorial norms change, while guild and baseline remain.

## 10.9 Organization: Legal Entities

The **Organization service** (`lib-organization`, L4, planned) tracks legal entities — shops, households, merchant companies, criminal enterprises, religious orders — as first-class economic and social actors. Where Faction handles governance and norms, Organization handles ownership and action capacity:

- **Organizations own assets** (currency wallets, inventories, properties) distinct from member ownership.
- **Organizations enter contracts** (Contract instances) as parties.
- **Organizations have reputations** (relationship bonds with factions, other organizations, individual characters).
- **Organizations can be bought, merged, inherited** via Contract templates.

A blacksmith who runs a shop has their personal ownership and the shop's ownership. The shop has its own Currency wallet (business capital) and Inventory (stock). The shop can enter a supply contract with an ore merchant — the merchant is bound to the shop, not to the blacksmith personally. When the blacksmith dies, the shop's assets transfer according to the will's Contract terms, possibly to a descendant or sold to another character.

This produces richer economic simulation than "every economic actor is a character" — businesses can persist beyond their founders, multi-generational family enterprises form, and organizational reputation is tracked independently of personal reputation.

## 10.10 The Morality Pipeline: Norms Into Second Thoughts

Stepping back, the morality pipeline composes multiple services into emergent moral reasoning:

```
                        Current Location
                              │
                        ┌─────┴─────┐
                        │           │
                        ▼           ▼
                   Location     Character
                   (L2)         Memberships
                              (Faction L4)
                        │           │
                        └─────┬─────┘
                              ▼
                         Faction norms query
                         "What norms apply here to this character?"
                              │
                              ▼
                         Merged norm map
                         {theft: 15, deception: 10, ...}
                              │
                              ├── + Contract behavioral clauses
                              │   {contract.oath.keep: 20}
                              │
                              ▼
                         Obligation aggregation
                         {theft: 15, deception: 10, oath.keep: 20}
                              │
                              ├── × Personality weighting
                              │   {honesty: 0.8, conscientiousness: 0.7}
                              │
                              ▼
                         Weighted penalty map
                         {theft: 20.6, deception: 13.75, oath.keep: 27.5}
                              │
                              ▼
                         Actor cognition: evaluate_consequences stage
                              │
                              ▼
                         GOAP action cost modifiers
                         {steal_food: +20.6, lie_to_guard: +13.75, ...}
                              │
                              ▼
                         GOAP plan selection
                         "Choose lowest-cost plan"
                              │
                              ▼
                         NPC behavior
                         (with conscience built-in)
```

At each stage, multiple services combine. None of them knows about the end result ("morality"). Each does its narrow job. The moral behavior is emergent from the composition.

### 10.10.1 The Feedback Loops

Moral behavior doesn't just flow one direction. Actions have consequences that feed back:

- **Personality drift** — an NPC who commits a knowing violation has their relevant personality traits shift slightly toward the violation pattern. Repeated violations compound.
- **Emotional composite** — guilt surfaces as a pattern across emotion dimensions (stress ↑, sadness ↑, joy ↓).
- **Encounter memory** — witnessed violations create negative-sentiment encounters for the observers.
- **Contract breach** — contractual violations trigger enforcement flows.
- **Divine attention** — gods may notice consistent patterns (virtue OR vice) and respond with blessings or curses.

Over many iterations, these feedback loops produce character arcs: the **Corrupted Guard** (starts loyal, takes bribes under pressure, drifts toward moral laxity), the **Redeemed Thief** (starts larcenous, joins a guild with strong norms, gradually shifts toward honesty). These arcs are not scripted — they emerge from repeated interactions between norms, personality, and accumulated choices.

## 10.11 Cultural Emergence: Customs From Material Conditions

The social fabric's most profound capability is **cultural emergence** — the organic formation of customs, identities, and institutions from material conditions, curated by divine actors.

### 10.11.1 Settlement Identity

A settlement's identity — "fishing village," "mining town," "market crossroads," "temple city," "frontier outpost" — is not a flag on a Location record. It is a **Hearsay belief** with high confidence, broad propagation, and self-reinforcing feedback.

Identity crystallizes when a divine actor (typically a Regional Watcher) observes that objective conditions at a location have stabilized into a recognizable pattern:

- Workshop output distribution (dominant production categories).
- Trade route significance through the location.
- Environmental character (coastal, mountainous, forested).
- Faction governance type (theocratic, commercial, military).
- Blessing density (divine attention concentration).

When the pattern is clear, the god-actor injects a **settlement_identity** belief into Hearsay at high confidence, with `cultural_osmosis` channel (slow spread, deep roots). The god does not *invent* the identity; it *recognizes* and *names* what was already emerging.

Once named, identity reinforces: NPCs whose behaviors align with the identity are unsurprised by it; trade caravans seeking fish visit the "fishing village" because that's what they know to visit; children growing up in the community learn fishing because their parents do. The identity shapes behavior, and the behavior shapes identity.

### 10.11.2 Customs as Faction Norms

Customs are **Faction norms with specific parameters** defining recurring cultural practices:

- Coming-of-age ceremonies (gift type, source, ceremony format, prerequisites).
- Harvest and seasonal festivals (timing, participants, celebrations).
- Mourning and memorial rites (duration, practices, obligations).
- Trade and diplomatic protocols (greeting, gift exchange, hospitality).
- Craft and professional traditions (guild formation, apprenticeship, journeyman pieces).
- Marriage customs (courtship, ceremonies, inter-settlement rules).

A divine actor evaluates a settlement's identity, resources, governance, and cultural maturity, then creates Faction norms with parameters derived from what is **actually available and valued**:

- A fishing village's coming-of-age gives a first fishing net — derived from marine identity, family craft tradition, practical value.
- A mining town's gives an iron-cap helmet and pickaxe token — derived from guild governance, danger acknowledgment, institutional identity.
- A temple city's gives a blessing stone for self-inspection — derived from theocratic governance and divine economy.
- A frontier outpost's gives a short blade and garrison assignment — derived from martial governance and survival necessity.

The **same cultural concept** (marking adulthood) manifests completely differently based on what the material conditions make possible and what the divine actor's aesthetic prefers.

### 10.11.3 Tradition Outlasts Basis

When material conditions change (mines run dry, trade routes shift, a plague kills the fishing fleet), the divine actor's next evaluation may find the identity no longer fits. The response is not instant replacement but gradual erosion — Hearsay confidence decays as reality stops matching the belief.

Two outcomes: **tradition** (custom persists as heritage despite changed conditions — "we still give nets even though we farm now, because that's who we were") or **replacement** (divine actor creates a new custom matching the new reality).

The tradition outcome is particularly rich. It is how cultural identity outlives its material basis. A former fishing village that became a farming community but still gives children nets at their coming-of-age is more believable than one that instantly updates. The lag between reality and culture is where character and history live.

### 10.11.4 The Status Board Is an Instance of This

The isekai "status board" — where coming-of-age includes receiving a card or stone that reveals one's capabilities — is not a special system. It is a cultural expression emerging when specific conditions align (theocratic or knowledge-oriented governance, divine blessing infrastructure, institutional capacity, cultural value of self-knowledge). The status board is the Collection unlock (mechanical), the blessed stone is the Item (cultural vessel), the church ceremony is the Contract (lifecycle), the citizenship granted is the Faction membership (institutional).

Settlements without the custom still have inhabitants who gradually understand themselves (Agency provides progressive self-knowledge regardless). The custom is *cultural packaging* — it makes the mechanical truth into a shared social experience.

### 10.11.5 God Personality Shapes Culture

The same objective conditions produce different cultural expressions under different divine actors:

- Nature-domain god → organic, seasonal, outdoor-ceremony variants.
- Commerce-domain god → practical, market-timed, community-funded variants.
- War-domain god → trial-based, prove-worthy, martial variants.
- Death-domain god → ancestor-honoring, inheritance-themed variants.

The fishing village's coming-of-age might be a dawn ceremony with a sacred-reed net (nature god), a guild-delivered quality net at the market (commerce god), or a solo trial requiring the child to catch their first fish in open water (war god). Same identity, same material basis, aesthetically different customs.

**The gods shape culture by curating which possibilities become traditions.** This is the mechanical substrate of worldbuilding: realm designers configure which gods interfere, and the gods' aesthetics manifest through the cultures they shape.

### 10.11.6 Institutions as Anti-Entropy

Per [§2.6](#26-hearsay-as-civilizational-anti-entropy), Hearsay's distortion mechanic creates civilizational-scale entropy: information coarsens as it propagates; unmaintained Lexicon desaturates over generations.

The social fabric counteracts this through **institutions**:

- **Academies and universities** preserve specific Lexicon entries through formal instruction. A student learning from a master inherits more detail than a rumor-listener.
- **Guilds** maintain apprenticeship chains that transmit craft knowledge with fidelity. A master blacksmith teaching a journeyman preserves the guild's accumulated techniques.
- **Priesthoods and temples** stabilize cosmological Lexicon through ritual repetition. Correct chanting preserves ontological vocabulary.
- **Written records** (enchanted tomes, archives, printed books) are Lexicon storage with higher fidelity than oral transmission.

These institutions are Factions in the mechanical layer, but their *function* is anti-entropy. Their presence determines whether a realm's civilizational Lexicon grows or decays. A FANTASIA realm with strong institutions maintains complex craft, magic, and scholarship across generations. A FANTASIA realm without them watches its capabilities erode, its specific concepts coarsen into general ones, its peak masters take knowledge to their graves without transmitting it.

Realm designers should treat institutional strength as a primary design axis ([§15](#15-designing-fantasia-realms)). It determines whether the realm feels ancient and deep-rooted (strong institutions sustaining old knowledge) or recent and precarious (weak institutions letting knowledge drift into rumor).

## 10.12 Cryptic Trails: Distributed Knowledge Puzzles

As a specific expression of the social fabric's generative power, the [Cryptic Trails system](planning/CRYPTIC-TRAILS.md) creates **distributed knowledge puzzles** where NPC characters autonomously discover world secrets.

### 10.12.1 The Mechanism

When a character's discovery tiers cross thresholds that make previously-invisible Lexicon associations visible, a hypothesis event fires:

1. Clues accumulate from multiple sources (mementos at locations, resonance item affix displayHints, rumors via Hearsay, historical documents, NPC conversations).
2. Each clue advances specific Collection entries — discovery tiers in relevant Lexicon categories.
3. When discovery tier crosses an association threshold, Lexicon publishes `lexicon.hypothesis.generated`.
4. Hearsay consumes the event and creates an inference belief.
5. Disposition generates an investigation drive if the character's curiosity trait supports it.
6. GOAP prioritizes investigation goals.
7. The character acts on the hypothesis.

### 10.12.2 Skill-Gated Hypothesis Generation

The same mystery has genuinely different difficulty per character:

```
effective_discovery_tier = base_tier − skill_offset
skill_offset = floor(relevant_seed_growth / threshold)
```

A master historian (seed offset -2) needs tier 4 worth of evidence for an association at base tier 6. An unskilled character needs all 6. The skill offset applies per-association, matched to the most relevant seed domain for that association's category.

This produces natural content-accessibility gradients. Mysteries aren't blocked or unlocked; they are **progressively discoverable** based on accumulated expertise. Characters without the skill can still get there with enough evidence; characters with skill get there faster.

### 10.12.3 Why This Matters for Social Fabric

Cryptic Trails demonstrate that the social fabric is not just *supporting* gameplay — it is *generating* gameplay. The same Lexicon/Hearsay/Disposition machinery that produces NPC conversations, cultural customs, and moral reasoning also produces distributed mystery content. Players uncover secrets through the same infrastructure NPCs use to gossip.

The dungeon beneath the old city is not a scripted quest. It is a Lexicon-based mystery waiting for someone — player or NPC — to accumulate enough clues and have enough relevant skill to see the association that reveals it. When a player's character starts investigating a pattern of deaths, they are using the same Hearsay inference machinery that rumor propagation uses. The puzzle-solving *is* the social fabric being read inward by an investigator.

## 10.13 The Complete Picture

Summarizing the social fabric:

```
       ┌──────────────────────────────────────────────┐
       │ LEXICON (L4)                                 │
       │ Canonical concept ontology — logos primitives │
       └──────┬───────────────────────────────────────┘
              │ vocabulary for
              ▼
       ┌──────────────┐        ┌──────────────────┐
       │ COLLECTION   │        │ CHAT (L1)         │
       │ (L2)         │        │ Lexicon room type │
       │ per-char     │◄───────│ validation,       │
       │ discovery    │        │ transport         │
       └──────┬───────┘        └─────────┬────────┘
              │ gates                    │ delivers
              │                          ▼
              │                    ┌──────────────┐
              │                    │ HEARSAY (L4) │
              │                    │ belief       │
              │                    │ propagation  │
              │                    │ + distortion │
              │                    └──────┬───────┘
              │                           │ informs
              └───────────────────────────┤
                                          ▼
              ┌────────────────────┐  ┌──────────────────┐
              │ DISPOSITION (L4)   │  │ RELATIONSHIP(L2) │
              │ feelings + drives  │  │ typed bonds       │
              └──────────┬─────────┘  └────────┬─────────┘
                         │                      │
                         └──────────┬───────────┘
                                    ▼
                    ┌─────────────────────────────┐
                    │ ACTOR (L2)                  │
                    │ ABML + GOAP cognition       │
                    └──────────┬──────────────────┘
                               │ moderated by
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
       ┌──────────┐     ┌────────────┐    ┌──────────┐
       │ FACTION  │     │OBLIGATION  │    │CONTRACT  │
       │ (L4)     │     │ (L4)       │    │ (L1)     │
       │ norms,   │     │ norms →    │    │ binding  │
       │ territory│     │ GOAP costs │    │ agreements│
       └────┬─────┘     └────────────┘    └──────────┘
            │                                   ▲
            │ observes → curates                │
            ▼                                   │
       ┌──────────────────────────────────┐     │
       │ Regional Watchers (PANTHEON)     │─────┘
       │ god-actors injecting customs     │
       │ and identity into Hearsay        │
       └──────────────────────────────────┘
```

This entire fabric produces **culture** as an emergent phenomenon. No service "makes culture"; every service contributes, and culture is what results when they interact on real conditions at scale over time.

## 10.14 Connecting to the Rest of the Document

The social fabric touches most other sections:

- [§2](#2-the-metaphysical-substrate--the-knowledge-as-power-thesis) — Lexicon-as-Logos thesis; Hearsay as civilizational anti-entropy.
- [§6](#6-the-npc-intelligence-stack) — the variable providers that make social data legible to NPC cognition.
- [§7](#7-the-content-flywheel) — cultural emergence is a long-form flywheel output; god-actors curate both narratives and customs.
- [§8](#8-life-death-and-generational-cycles) — marriage customs, inheritance rules, coming-of-age ceremonies span generations.
- [§9](#9-the-economic-substrate) — Organization as first-class economic actors; Obligation's integration with contracts.
- [§15-§18 (Realm Design)](#part-iv--realm-design-guides) — each realm is distinguished by its institutions, god-set, and emergent cultures.

If you want Arcadia to feel like a lived world rather than a simulation, this is the section that explains why. The social fabric is what makes the world *inhabited* — not just populated, but *inhabited*.

---

# PART III — EMERGENT SYSTEMS

# 11. Combat & Cinematic Choreography

## 11.1 The Combat Dream

Combat in most games is a mechanical loop: input → animation → damage calculation → animation → input. Competent implementations polish this loop with responsive controls, satisfying hit feedback, and well-designed attack patterns. The loop is the game; the cinematic is a cutscene that interrupts it.

Arcadia aspires to something different, captured in VISION.md as **the Combat Dream**: combat that feels like a choreographed cinematic generated in real-time from actual environment, character capabilities, and player input. Not a cutscene that plays *between* fights, but a cinematic that *is* the fight — dynamically composed, responsive to player choices, choreographically valid, and shot like a film.

Critically, the dream requires this to work at 100K NPC scale, to compose fresh sequences without hand-authoring, and to scale cleanly from "new spirit watching a cinematic fight" to "master spirit directing every beat." The system must span the full range of the progressive-agency gradient without fragmenting into multiple subsystems.

This section develops how that works. [§11.2](#112-the-decision-choreography-split) explains the temporal split between cognition and choreography. [§§11.3-11.4](#113-what-already-exists) catalogs existing infrastructure and the remaining composition gap. [§11.5](#115-the-anime-production-paradigm) develops the architectural paradigm (independence-as-default, synchronization-as-earned) that makes this tractable. [§11.6](#116-initiative-driven-combat) covers the initiative mechanism. [§11.7](#117-agency-gated-choreographic-density) ties it to progressive agency. [§11.8](#118-distributed-scene-sourcing) develops the extension to cross-server composition.

## 11.2 The Decision/Choreography Split

Combat in Arcadia operates on two distinct temporal scales that correspond to different architectural concerns:

| Scale | What It Does | Where It Happens |
|---|---|---|
| **Combat decisions** (100-500ms cognition ticks) | Should I fight, flee, or negotiate? What's my goal? What tactical plan suits this situation? | Actor + variable providers + GOAP (§6) |
| **Combat choreography** (5-30s cinematic sequences) | Wind up, fake left, strike right, follow through. Camera angles, dramatic timing. | Cinematic system (this section) |

These are not the same system running at different clocks. They are **fundamentally different computational domains**. Cognition operates on NPC mental state; choreography operates on spatial, capability, and dramatic grammar. Cognition produces goals and dispositions; choreography produces action sequences.

The split is architecturally important because:

- **Cognition is per-NPC**. Each NPC's Actor runs its own cognition loop, producing its own decisions.
- **Choreography is per-encounter**. A combat encounter involving multiple NPCs is a single cinematic composition, not a sum of individual performances.
- **Cognition is always-on**. Every NPC is thinking whether or not a fight is happening.
- **Choreography is event-triggered**. A cinematic is composed when an Event Brain detects combat conditions and calls `/cinematic/compose`.

This division parallels the Music and Narrative systems exactly (§12). In each, a cognition layer decides *when* to invoke the cinematic/musical/narrative layer, and a composition layer produces the actual sequence.

## 11.3 What Already Exists

The runtime infrastructure for cinematics is substantially implemented. The gap is not "we need a cinematic system"; it is "we need the composition layer that feeds the cinematic system we already have."

### 11.3.1 The CinematicInterpreter

At the core is `CinematicInterpreter` in `sdks/behavior-compiler/`. It wraps the standard behavior bytecode interpreter with cinematic-specific capabilities:

- **Continuation points** — named pause locations in ABML bytecode where external extensions can be injected. Each has a timeout and a default flow offset: if no extension arrives before the timeout, the interpreter falls through to the default choreography.
- **Streaming composition** — extensions can be pre-registered or injected mid-execution. The interpreter evaluates through base behavior, pauses at continuation points, executes injected extensions, then resumes.
- **Graceful degradation** — if an extension arrives late, the default flow executes. The cinematic never blocks indefinitely.

This is the mechanism that enables dynamic cinematics: a base choreography is composed, continuation points mark where choices can branch, and runtime events (player input, state changes, regional-watcher interventions) inject branch extensions before the default plays out.

### 11.3.2 Session Coordination

`CutsceneSession` and `CutsceneCoordinator` (in `plugins/lib-behavior/`) handle multi-entity aspects:

- **Sync points** — barriers where multiple entities must arrive before the sequence continues. Thread-safe with timeout support.
- **Input windows** — QTE/choice moments where a player can influence the choreography. Configurable timeout with behavior-default resolver.
- **Session lifecycle** — create, report sync, submit input, complete/abort with clean resource disposal.

### 11.3.3 Client Integration Surface

The `IClientCutsceneHandler` interface defines what the game client implements to render cinematics. It is deliberately generic:

- **Lifecycle**: `OnCutsceneStartAsync`, `OnCutsceneEndAsync`.
- **Choreographic actions**: `ExecuteActionAsync(entityId, action, parameters)` — passes through action names and parameter dictionaries.
- **Camera direction**: `ExecuteCameraActionAsync(action, parameters)` — same pattern, for camera movement.
- **Multi-entity coordination**: sync-point reached/released notifications.

The client is action-type-agnostic. New action types can be added to compositions without changing the client interface. The client interprets action names and maps them to animations, particle effects, camera movements — whatever the engine provides.

### 11.3.4 The ABML DomainAction

The behavior compiler handles choreographic actions through the `DomainAction` node type — a generic action with a string name and parameter dictionary. This means **ABML already has full choreographic authoring capability**:

```yaml
actions:
  - animate:
      entity: ${attacker}
      animation: sword_overhead_strike
      duration: 1.2
  - sync: strike_contact
  - animate:
      entity: ${defender}
      animation: block_high
      blend: 0.3
  - camera_action:
      action: shake
      intensity: 0.4
      duration: 0.5
```

What's missing is the system that *generates* these documents from encounter context.

## 11.4 The Composition Gap

Three components are planned but not yet implemented:

### 11.4.1 CinematicTheory SDK (Pure Computation)

Analogous to MusicTheory (harmony, melody, pitch, MIDI-JSON). The formal grammar of choreographic composition covers:

- **Dramatic grammar** — beat structure (setup, escalation, climax, resolution), tension curves, dramatic timing, emotional arc mapping.
- **Spatial reasoning** — affordance evaluation from Mapping, positioning logic, environmental interaction possibilities.
- **Capability matching** — species behavioral archetypes (Ethology), character combat preferences (`${combat.*}`), equipment options, special abilities.
- **Camera direction** — shot composition rules, cut rhythm, focus management, spatial clarity.

Output format: ABML documents with `DomainAction` nodes — the same format the existing runtime already consumes.

### 11.4.2 CinematicStoryteller SDK (GOAP-Driven Composition)

Analogous to MusicStoryteller and StorylineStoryteller. Uses GOAP planning over:

- **World state inputs**: participants (capabilities, relationships), environment (affordances, lighting, weather), dramatic context, player agency level, encounter stakes.
- **Goals**: produce a dramatically satisfying sequence, respect capabilities, include appropriate QTE density, manage tension curve, end supporting the narrative.
- **Actions**: compose_exchange, insert_environmental_interaction, add_dramatic_pause, inject_qte_window, add_camera_emphasis, compose_group_action, trigger_continuation_point.

Output: complete ABML cinematic documents ready for CinematicInterpreter execution.

### 11.4.3 lib-cinematic Plugin (L4 API Wrapper)

Analogous to lib-music and lib-storyline. Thin orchestration layer with endpoints:

| Endpoint | What It Does | Called By |
|---|---|---|
| `/cinematic/compose` | Participants + environment + constraints → sequence | Event Brain actors via ABML |
| `/cinematic/extend` | Existing sequence + continuation point + context → extension | Event Brain actors mid-encounter |
| `/cinematic/resolve-input` | QTE + player input → outcome + branch | Gardener (player-influence routing) |
| `/cinematic/list-active` | Query active instances by realm/location | Puppetmaster, admin tools |
| `/cinematic/abort` | Force-end an active cinematic | Admin, disconnect handling |

Also provides the `${cinematic.*}` variable provider for Actor (is entity in cinematic, current beat, role).

### 11.4.4 Why This Completes the Architecture

With these three components in place, the Combat Dream becomes implementable and — crucially — the entire compositional architecture is complete:

| Autonomous Domain | Decision Layer | Composition Layer | Execution Layer |
|---|---|---|---|
| NPC Cognition | Actor GOAP | Variable providers + ABML | BehaviorModelInterpreter |
| Music | ABML (mood select) | MusicStoryteller (GOAP) | MusicTheory |
| Narrative | Regional Watcher | StorylineStoryteller (GOAP) | StorylineTheory |
| Combat/Choreography | Event Brain | CinematicStoryteller (GOAP) | CinematicTheory → CinematicInterpreter |

Every domain follows the same pattern: **behavioral decision → GOAP-driven composition → formal grammar execution → client rendering**. ABML is the universal authoring language across all.

## 11.5 The Anime Production Paradigm

The deepest design insight for cinematic composition comes from anime production. Studios discovered, through decades of budget constraints, a principle that maps extraordinarily well onto Bannou's distributed architecture: **decompose complex scenes into independently-produced layers with shared spatial constraints, then composite them together**.

### 11.5.1 Independence Is the Default

In anime, characters on separate transparent cels (A/B/C layers) are animated by different artists, on different schedules, using the same **layout** (vanishing points, perspective grid, horizon). Direct character-to-character physical contact — the "same layer" moment — is the expensive exception, not the default. Strategic camera cuts hide the boundaries between independent layers.

The typical anime scene has 80%+ of its runtime composed of **independent** character performances (each on separate cels, drawn separately, blended by camera cuts) and less than 20% in **synchronized** moments (physical contact, truly simultaneous action). This ratio is what makes the production economics work.

Applied to Bannou:

| Anime Concept | Bannou Equivalent |
|---|---|
| Layout (perspective, scale) | Scene document + Mapping spatial data |
| Key animation (individual character cels) | Fingerprinted behavior components |
| Continuation points between cels | CinematicInterpreter continuation points |
| Sakuga (high-quality sync moments) | CutsceneSession sync barriers |
| Bank animation (reusable stock) | Plan cache, BehaviorModelCache |
| Satsuei (compositing) | CutsceneSession + Video Director |
| Editorial (cut selection) | GOAP-driven component selection |

### 11.5.2 A Five-Component Scene

Consider a 30-second tavern scene with two characters:

```
Component A (3s): Roy close-up, speaking
  Roy: fingerprinted "dialogue_earnest" component (full animation)
  Marth: fingerprinted "listening_attentive" component (blinks, nods)
  Sync required: NONE (separate "cels", cut hides boundaries)
    │
Component B (3s): Marth reaction shot
  Marth: fingerprinted "reaction_emotional" component (full animation)
  Roy: fingerprinted "idle_seated" component (held pose)
  Sync required: NONE
    │
Component C (4s): Wide establishing shot
  Both characters: independent idle components
  Camera: two-target framing
  Sync required: NONE (spatial context only)
    │
Component D (5s): Roy grabs Marth's shoulder — SAKUGA MOMENT
  Both characters: shared "physical_contact_shoulder_grab" component
  Sync required: YES (CutsceneSession sync barrier)
    │
Component E (3s): Roy close-up, emotional aftermath
  Roy: fingerprinted "emotional_aftermath" component
  Marth: not visible
  Sync required: NONE
```

**4 out of 5 components require zero synchronization.** Only the physical contact (D) needs a sync barrier. This is the anime ratio mirrored architecturally.

### 11.5.3 Quality Stratification

In anime, production budget concentrates on sakuga moments while everything else uses efficient limited animation. The Bannou equivalent:

| Quality Tier | Component Type | When Used |
|---|---|---|
| **Sakuga** | Full synchronized choreography, custom-authored | Climactic combat, emotional peaks, physical contact |
| **Standard** | GOAP-selected from registry, moderate density | Dialogue, exploration, moderate action |
| **Limited** | Cached idle, cycling, reaction components | Background characters, atmosphere, non-focal |

The component registry stratifies naturally: sakuga components cost more in the GOAP planner (longer duration, more sync barriers, more spatial requirements). The planner selects them only when dramatic context justifies the cost — exactly how anime directors allocate animation budget.

### 11.5.4 Combinatorial Consequence

In anime, each cel is drawn once for a specific scene. Layer separation saves production cost but doesn't generate new content.

In Bannou, components are fingerprinted, GOAP-selected, role-assignable, and continuation-point-linked. The combinatorial space explodes:

- 200 combat exchange components, each hand-crafted for cinematic quality.
- 50 dialogue components per emotional register.
- 30 transition/reaction components.
- Different character selections drive different component preferences.
- Different music drives different timing.
- Different environments provide different spatial affordances.
- Different life stages provide different flashback material.

Same five-component scene structure produces an entirely different cinematic with different characters, different music, different components. No combination is pre-authored — each is emergently composed.

## 11.6 Initiative-Driven Combat

Combat is the highest-stakes application of the independence-default principle. Each combatant's performance is an independent "cel" — their attacks, defenses, special moves are all self-contained components. They synchronize only at exchange boundaries: the moment of impact, the parry connection, the counter transition.

### 11.6.1 Initiative Determines Role

**Initiative determines which combatant is the "protagonist" of the current exchange.** The initiative holder's component is the melodic/lead role — their animations lead, the camera follows, they have the highest action freedom. The defender's component is the counterforce/bass role — reactive choreography, defensive framing.

When initiative shifts (successful counter, tactical choice), roles swap. This oscillation across exchanges creates the back-and-forth rhythm that makes combat feel like a conversation rather than a static exchange.

### 11.6.2 Exchange Boundaries as Composition Seams

Between exchanges, each combatant executes independently. The combat coordinator event brain uses continuation points as exchange boundaries. At each continuation point:

- The CinematicStoryteller has composed multiple possible extensions (tactical options — dodge, block, counter, combo opener, environmental exploit, rare composite).
- The player's choice (or auto-resolution for NPC-vs-NPC or low-fidelity spirits) determines which extension executes.
- The chosen extension determines initiative for the next exchange.

This maps precisely to the anime model:

- **Independent execution** — each combatant's attack/dodge/counter is a self-contained component.
- **Exchange boundaries** — continuation points are where one exchange ends and the next begins.
- **Sync barriers** — physical contact triggers CutsceneSession sync barriers.
- **Camera cuts hide seams** — cuts between combatants' close-ups hide the transition from attacker to defender role.

### 11.6.3 The Non-Combat Parallel

The same initiative-driven composition pattern extends beyond combat. A negotiation scene has initiative (whoever is currently pressing their position), exchanges (one side speaks, the other responds), and sync barriers (dramatic moments of direct confrontation). A courtship sequence has initiative (whoever is currently leading), exchanges (approach and response), and sync barriers (the kiss, the refusal).

Initiative-driven composition is a **general structure for choreographed interaction**, not a combat-specific mechanism.

## 11.7 Agency-Gated Choreographic Density

The progressive-agency system ties directly to cinematic composition. The same choreography with the same sync barriers plays differently based on the guardian spirit's combat fidelity (`${spirit.domain.combat.fidelity}`):

| Spirit Fidelity | Combat Experience | Cinematic Interaction |
|---|---|---|
| 0.0-0.2 | New spirit, no combat understanding | Pure cinematic — watch the fight unfold |
| 0.2-0.4 | Basic awareness | Directional intent at key moments (approach/retreat/hold) |
| 0.4-0.6 | Stance perception | Defensive/aggressive posture choices, dodge windows |
| 0.6-0.8 | Timing mastery | Parry/strike timing windows, combo direction |
| 0.8-1.0 | Full martial choreography | Beat-by-beat direction, stance selection, combo composition |

This is not a discrete unlock system. The fidelity value is a continuous float determining:

1. **QTE density** — how many continuation points become interactive vs. auto-resolving.
2. **QTE complexity** — simple binary choices vs. multi-option timing windows.
3. **QTE timing** — generous windows for lower fidelity, tight windows for higher.
4. **Default quality** — even at low fidelity, the "automatic" choreography is good. The spirit is watching a *real* fight, not a degraded one.

The critical architectural property: **the composition layer does the same amount of work regardless of fidelity**. It always plans a full choreographic sequence with continuation points. The fidelity value only determines how many points become player-interactive vs. auto-resolving. This means the system degrades gracefully — removing player agency doesn't simplify the choreography; it just changes who controls it.

## 11.8 Distributed Scene Sourcing

The final architectural insight: Bannou's distributed monoservice deployment enables something even anime cannot achieve — **multiple servers simultaneously simulating different versions of the same scene, composited by the client in real-time**.

### 11.8.1 The Three Mechanisms Already Exist

Distributed scene sourcing doesn't require new infrastructure. It is the natural extension of three existing mechanisms:

| Existing Mechanism | Cinematic Extension |
|---|---|
| **Orchestrator processing pools** (ephemeral workers for Actor brains) | Spin up ephemeral **scene servers**, each simulating one scene/location/time period |
| **Seed switching** (player in void selects which seed to drop into, each on a different server) | Cinematic system selects which scene feed to cut to |
| **Connect multi-node relay** (WebSocket mesh between Connect instances) | Client maintains connections to multiple scene servers, receives multiple feeds |

### 11.8.2 Three Levels of Distributed Composition

**Level 1: Cross-location cuts**. Two characters in different places; two different servers; client cuts between feeds. Each server runs independently; zero synchronization; the emotional connection comes from editorial juxtaposition.

```
Server A: Village yard — Roy doing sword practice
Server B: Dungeon floor 7 — Marth fighting the boss
Video Director: Beat-sync cuts between feeds
  Verse: Roy's calm practice / Chorus: Marth's desperate combat
  Bridge: Split-screen / Climax: Roy senses → Marth falls
```

**Level 2: Same-location, different cameras**. Both characters in the same tavern, single server. The anime paradigm applied locally: independent behavior components per character, camera cuts as compositional framework. This is the Part 11.5.2 example.

**Level 3: Same-location, different TIME**. This is the breakthrough. Two servers running the **same physical space** at different points in time:

```
Server A (Present, 1067): Tavern — old bartender, renovated interior, Roy alone
Server B (Past, 1042):    Tavern — young bartender (same person), original decor,
                          Roy and Marth laughing together

Client composites:
  Old bartender's face → young bartender's face (match cut)
  Empty chair across from Roy → Marth sitting there (spatial match)
  Rain on window → sunset through window (environmental contrast)
```

### 11.8.3 Why Flashbacks Are Hard in Traditional Games

Traditional single-engine architectures must unload the present scene, load historical assets, swap character models to younger versions, change weather/lighting, play the flashback, reverse everything. Loading screens break emotional momentum; asset swapping creates visible hitches. The complexity discourages developers from using flashbacks, which is why they're rare in games despite being a fundamental storytelling tool in film and anime.

With parallel servers, both versions exist simultaneously. The client receives both feeds. The cut between present and past is **instantaneous** — no loading, no asset swapping. Emotional momentum is preserved because the transition is a camera cut, not a scene load.

The flashback server is an ephemeral processing-pool worker:

```
Orchestrator
  │
  ├── Main game server (present day, persistent)
  │
  └── Processing pool: "flashback workers"
      └── Flashback Server: loads save snapshot from 1042
          - Save-Load provides versioned state
          - Resource archives provide character appearance at that age
          - Worldstate provides historical time/season/weather
          - Runs just long enough for the cinematic segment
          - Released back to pool when flashback ends
```

### 11.8.4 Extended Applications

The principle extends beyond flashbacks:

| Application | Server A | Server B | Compositional Effect |
|---|---|---|---|
| Flashback | Present-day | Historical snapshot of same location | Match-cut temporal transitions |
| Perspective split | Battle from victor's perspective (warm lighting) | Same battle from loser's perspective (chaotic) | Intercut both sides |
| Memory distortion | Objective event | Character's memory, filtered through personality | Unreliable narrator |
| Prophetic vision | Current reality | God-actor's speculation of possible future | Character sees consequences before choosing |
| Dream sequence | Waking world | Surreal remix of real archives and locations | Personality-distorted environment |
| Parallel lives | Seed A's character in Location X | Seed B's character in Location Y | Cross-seed divergent-path cinematic |

### 11.8.5 Agency and Cinematic Switching Are the Same Infrastructure

The switching mechanism between scene feeds is structurally identical to existing seed/character switching. Agency's UX capability manifest already manages "what can this spirit perceive right now." For cinematics, the manifest temporarily becomes "you are watching a directed sequence; the current feed is X." The switching infrastructure is the same; only the trigger changes from player choice to cinematic direction.

This is the architectural economy again: distributed scene sourcing looks like a new capability, but it is old infrastructure applied cinematically.

## 11.9 The Combat Dream Realized

Putting it all together, here is the end-to-end flow:

### Step 1: Event Brain Detects Encounter

An Event Brain actor (launched by Puppetmaster as a Regional Watcher) detects an encounter opportunity — two hostile characters in proximity, a dramatic scenario condition met, a player stumbling into a dangerous area. The Event Brain's ABML decides to initiate combat.

### Step 2: Composition Request

The Event Brain calls `/cinematic/compose` via ABML `service_call`:

- Participants (character IDs from the encounter).
- Environment (location ID — lib-cinematic queries Mapping for spatial affordances).
- Dramatic context (encounter type, narrative stakes, from Event Brain's scenario data).
- Constraints (duration range, intensity level, allowed action types).

### Step 3: CinematicStoryteller Plans

lib-cinematic feeds context into CinematicStoryteller:

1. Query participant capabilities (combat preferences, species archetypes, equipment).
2. Query environment affordances (spatial data, interactive objects).
3. GOAP planner composes a sequence of dramatic beats.
4. CinematicTheory validates each beat against spatial/capability constraints.
5. QTE continuation points inserted based on player agency level.
6. Output: ABML cinematic document.

### Step 4: Runtime Execution

The ABML document loads into `CinematicInterpreter` via `CinematicRunner`:

1. Runner acquires control of participating entities.
2. Interpreter evaluates through the choreography, emitting DomainActions.
3. At continuation points, the interpreter pauses for extensions (QTE inputs, runtime adaptations).
4. CutsceneSession coordinates sync points across entities.
5. IClientCutsceneHandler receives actions and renders them on the client.

### Step 5: Player Interaction (Agency-Gated)

For QTE continuation points, the flow branches based on spirit fidelity:

- **Low fidelity**: point times out; default flow executes. The fight plays out as cinematic.
- **Medium fidelity**: some points become input windows (dodge left/right, attack/defend). Key moments interactive.
- **High fidelity**: most points are interactive. Beat-by-beat direction.

### Step 6: Cascading Outcomes

The cinematic's resolution feeds back into the ongoing simulation:

- Character Encounter records the combat for all participants and witnesses.
- Character History records significant events (heroic deeds, cowardly failures).
- Realm History records world-scale events (if the stakes warrant).
- Personality evolves based on outcomes (a near-death experience shifts risk tolerance).
- Resource compression snapshots may be taken.
- Plot armor may deplete (per [§8.4](#84-plot-armor-as-narrative-pacing)).
- Music transitions update (post-combat musical states via Music system).

The cinematic is not an isolated event; it is a load-bearing node in the content flywheel.

## 11.10 Connecting to the Rest of the Document

The cinematic system ties to many other sections:

- [§6](#6-the-npc-intelligence-stack) — Event Brains that trigger cinematics are Actor instances running ABML.
- [§7](#7-the-content-flywheel) — cinematic outcomes feed Character History, Realm History, encounter memory.
- [§8](#8-life-death-and-generational-cycles) — plot armor interacts with cinematic lethality; death cinematics are scenarios.
- [§10](#10-the-social-fabric--cultural-emergence) — non-combat cinematics (negotiation, courtship) use the same machinery.
- [§12](#12-procedural-creative-generation) — cinematic is one of the creative-generation SDKs; Music, Narrative, and Cinematic all follow the Theory/Storyteller pattern.
- [§13](#13-actor-bound-entities-living-places-and-things) — actor-bound entities (sentient weapons, dungeon cores, gods) participate in cinematics via their Characters.
- [§§15-18 (Realm Design)](#part-iv--realm-design-guides) — realm choices (combat-heavy vs. social-heavy UX emphasis) influence which cinematic types the realm primarily produces.

The Combat Dream is not a feature; it is the visible peak of a compositional stack that runs through most of Bannou. When combat feels like cinematic in Arcadia, it is because the entire architecture — from Actor cognition to CinematicTheory to distributed scene servers to Agency-gated QTE density — is cooperating to produce that quality.

---

# 12. Procedural Creative Generation

## 12.1 The Fourth Pillar

Arcadia's vision has a commitment that most other platforms can't match: **no AI/LLM inference for content generation**. Music, narrative, behaviors, scenes, cinematics — every system that produces content in Arcadia does so through **formal theory and deterministic rules**, not through neural inference.

This is not a limitation; it is a strategic choice with concrete benefits:

- **Redis caching**: Deterministic generators produce identical outputs for identical inputs. Cache once, serve forever.
- **Test reproducibility**: Unit tests can validate exact outputs. No stochastic flakiness.
- **Zero external dependencies**: No API keys, no rate limits, no provider lock-in. The content stack runs in-process.
- **Constant cost scaling**: Generating the 1000th composition is the same cost as the 1st. No per-inference pricing at 100K NPC scale.
- **Offline capability**: Single-player and embedded deployments work without network.
- **Creative determinism**: A player returning to "their" NPC's whistle tune hears the same tune. Identity persists.

The strategic bet: **formal academic theory + GOAP planning can produce content that feels hand-authored**, and across enough creative domains it makes Bannou a content-generation platform unlike any other.

This section develops the full SDK ecosystem that implements this bet: the three-layer pattern ([§12.2](#122-the-three-layer-pattern)), each creative domain's SDK family ([§§12.3-12.9](#123-music)), the unified scenario pattern that ties them together ([§12.10](#1210-the-unified-scenario-pattern)), and the planned directing and composition layers that extend the ecosystem ([§§12.11-12.12](#1211-the-video-director)).

## 12.2 The Three-Layer Pattern

Bannou's SDK ecosystem follows a consistent architectural pattern across creative domains: **Theory**, **Storyteller**, **Composer**. Each layer is independently useful; not every domain has all three.

### 12.2.1 The Three Layers

**Theory** SDKs are pure-computation primitives — formal academic structures made into data-and-algorithm. Zero service dependencies, deterministic, usable in any .NET context (game client, editor tool, CI pipeline, unit test). Examples: `music-theory` implements Tonal Pitch Space, voice leading, harmonic construction; `storyline-theory` implements Greimas actantial model, Propp morphology, Barthes narrative codes; `cinematic-theory` (planned) implements Toric Space camera math, EMOTE/Laban movement transformation, Film Idiom HFSM.

**Storyteller** SDKs are GOAP-driven procedural composition. They use Theory primitives as building blocks, add a world-state model for the domain (emotional state for music, narrative arcs for storyline, choreographic beats for cinematic), and use the shared GOAP planner to produce sequences that satisfy dramatic goals. Deterministic when seeded — same parameters + same seed → identical output.

**Composer** SDKs are interactive authoring tools for human creators. Command-based undo/redo, engine-agnostic core with pluggable engine bridges, validation rules, serialization. Output: domain-format documents suitable for runtime consumption — the same format Storyteller outputs produce.

### 12.2.2 The Critical Property

**Storytellers produce the same output format as Composers.** A Bannou plugin receiving a scenario, composition, or scene document cannot tell whether it was hand-authored or procedurally generated. This enables god-actors to mix authored and generated content seamlessly in the content flywheel.

A specific practical consequence: a game can ship with 200 hand-authored combat scenarios and then procedurally generate 10,000 more. The player cannot distinguish which is which. Both are stored in the same registry. Both match conditions through the same mechanical predicates. Both are selected by god-actors via the same GOAP planning.

### 12.2.3 Why Some Domains Skip Layers

Not every domain needs all three layers. The pattern serves the domain, not the other way around:

- **Scene** has only Composer. "Procedural scene generation" isn't meaningful at the scene-document level (procedural generation happens at the voxel or procedural-geometry level instead). There's no "theory primitives" for scene composition.
- **Behavior** has Theory (`behavior-expressions`) and a compilation layer (`behavior-compiler`) but no Composer. ABML is YAML text; a text editor is the composer. A visual behavior-tree editor is possible but not planned.
- **Voxel** has Composer only. There's no "voxel theory" meaningful at the domain level; voxel generation (WFC, L-systems, noise) is built into the Composer.

### 12.2.4 SDKs vs. Plugins

A final clarification: **SDKs are pure computation libraries** with zero Bannou service dependencies. **Plugins are service implementations** that wrap SDKs behind HTTP APIs with state management, caching, and event publishing.

```
SDK (pure computation)              Plugin (service wrapper)
────────────────────                ───────────────────────
music-theory                        lib-music
music-storyteller       ──────►         ├── Redis caching
                                        ├── HTTP API endpoints
                                        └── Agency-gated invocations

storyline-theory                    lib-storyline
storyline-storyteller   ──────►         ├── Archive extraction
                                        ├── Plan storage
                                        └── Scenario registry
```

SDKs can be used independently — in game clients, editor tools, CI pipelines. The plugin layer adds persistence, caching, service-level API exposure, and cross-service integration.

## 12.3 Music

Procedural music generation grounded in formal music theory and music cognition research.

| SDK | Purpose | Status |
|---|---|---|
| `music-theory` | Pitch, scales, chords, harmony, voice leading, rhythm, MIDI-JSON output | Complete |
| `music-storyteller` | Emotional state model (6D), narrative templates, GOAP-driven composition | Complete |
| `music-composer` | Interactive music authoring with theory primitives | Planned |
| `counterpoint-composer` | Structural template workbench for counterpoint-compatible music | Planned |

### 12.3.1 How They Compose

MusicStoryteller uses GOAP planning to select musical actions (tension, resolution, thematic development) that move through a 6-dimensional emotional state space. Each action delegates to MusicTheory for actual pitch/harmony/voice-leading computation. The `lib-music` plugin (L4) wraps this as an HTTP API with Redis caching — deterministic compositions cache as content-addressed results.

The output is **portable MIDI-JSON**. The game engine renders it with any synthesizer. Identical inputs always produce identical compositions — same scenario, same emotional context, same seed → same tune. This is what lets a particular NPC have "their" whistle that players recognize.

### 12.3.2 Academic Foundations

The SDKs are rigorously grounded:

- **Lerdahl's Tonal Pitch Space** (1988) — the geometric model of pitch relationships used for harmonic distance calculation.
- **Huron's ITPRA model** (2006, *Sweet Anticipation*) — Imagination, Tension, Prediction, Reaction, Appraisal — the temporal emotional response framework.
- **Juslin's BRECVEMA** — Brain stem reflex, Rhythmic entrainment, Evaluative conditioning, Contagion, Visual imagery, Episodic memory, Anticipation — the seven-mechanism emotional-expression model.
- **Reagan et al.** emotional arc classification — the six fundamental emotional-arc shapes (rags-to-riches, tragedy, man-in-hole, Icarus, Cinderella, Oedipus) that narratives follow.
- **Meyer's music cognition** — tension/release and expectation-violation as musical affect generators.

### 12.3.3 Dark Revelry and Style Objectives

Per the [Dark Revelry](planning/DARK-REVELRY-STYLE-OBJECTIVE.md) design, specific compositional aesthetics can be codified as **style objectives** — named targets in emotional-state space with YAML parameters, GOAP constraints, and assisted-authoring guidance. "Dark revelry" occupies the counterintuitive quadrant of high-tension + positive-valence (a villain having the time of their life) that most music-cognition frameworks treat as impossible, and its explicit codification enables both the procedural storyteller and the future human-composer workbench to produce it.

Other style objectives — melancholic triumph, serene dread, tender violence — can be defined similarly. Each is a configuration of existing primitives, not a new mechanism.

### 12.3.4 What This Eliminates

For a studio building on Bannou, procedural music eliminates:

- Hand-authored adaptive music state machines (FMOD/Wwise).
- Pre-recorded emotional variants of every track.
- The need for a full-time composer for dynamic game music.

A studio can still use a human composer for key themes and motifs (the Counterpoint Composer workbench is designed for exactly this — structural templates that interlock with MusicStoryteller output), but the bulk of in-game music is procedurally generated, cached, and deterministic.

## 12.4 Storyline

Seeded narrative generation from compressed character archives, producing narrative plans that god-actors translate into quests, encounters, and world events.

| SDK | Purpose | Status |
|---|---|---|
| `storyline-theory` | Archive extraction, actant assignment, emotional arc classification, kernel/satellite identification, life value spectrums | Complete |
| `storyline-storyteller` | GOAP-driven narrative planning, story action sequencing, template-based composition | Complete |
| `storyline-composer` | Typed scenario definitions, mechanical condition evaluation, mutation descriptions | In design |

### 12.4.1 How They Compose

When a character dies, `lib-resource` compresses their archive. A god-actor perceives the archive event and calls `lib-storyline`, which:

1. **StorylineTheory** extracts kernels (critical plot points) from the archive.
2. **StorylineTheory** assigns actant roles (protagonist, antagonist, donor, helper, dispatcher) per Greimas's actantial model.
3. **StorylineTheory** classifies the emotional arc shape per Reagan's six-arc framework.
4. **StorylineStoryteller** uses GOAP to plan a narrative arc satisfying dramatic constraints.
5. Output: a StorylinePlan that the god-actor translates into concrete game actions (quest creation, NPC spawning, item placement, environmental changes).

This is the content flywheel in motion (§7). The storyteller produces the plan; the god-actor decides whether and how to execute it.

### 12.4.2 Simple vs. Emergent Modes

lib-storyline supports two execution modes from the same definitions:

- **Simple Mode** — deterministic MMO-style unlocks with periodic server checks. An archive meets a threshold → a scenario becomes available → eligible characters see it in their quest log. Fast, predictable, limited emergence.
- **Emergent Mode** — regional watcher god-actors actively search, score, and decide which characters qualify for which narratives based on their actual life histories. Slower, richer, deeper emergence.

The same underlying scenarios work in both modes; developers choose which mode fits their realm.

### 12.4.3 Origin Scenarios

One specific storyline pattern is the **origin scenario** — a special narrative that a character encounters early in life that seeds their backstory and disposition. An origin scenario can be triggered directly (for narrative control) or emerge organically (for surprise). Origin scenarios feed CharacterHistory, which feeds `${backstory.*}` variable providers, which feed NPC cognition — a character's "origin story" is mechanically present in their subsequent behavior.

### 12.4.4 What This Eliminates

Procedural narrative generation eliminates:

- Hand-scripted quest trigger systems.
- Static branching dialogue trees.
- The combinatorial explosion of pre-authoring every world-state / character-history / NPC-context condition that could fire a story event.

Hand-authored scenarios still exist (many games will want curated signature stories), but procedural generation covers the long tail — the thousand minor quests that emerge from simulated history.

## 12.5 Cinematic

Choreographed combat encounters, cutscenes, and QTE sequences with formal camera direction and movement theory.

| SDK | Purpose | Status |
|---|---|---|
| `cinematic-theory` | Toric Space camera solver, EMOTE/Laban movement transformation, Film Idiom HFSM, dramatic grammar | In design |
| `cinematic-storyteller` | GOAP-driven choreography composition, beat sequencing, tension arc management, agency-scaled QTE density | In design |
| `cinematic-composer` | Timeline/track authoring format, participant slots with capability requirements, sync barriers, continuation points | In design |

### 12.5.1 The Three Layers of Composition

Cinematic composition separates into three independent layers (detailed in [§11](#11-combat--cinematic-choreography)):

- **Structure** (WHAT happens) — exchange sequencing, beat placement.
- **Quality** (HOW it moves) — Effort/Shape transformation via EMOTE/Laban.
- **Presentation** (HOW it's shown) — camera direction via HFSM film idioms.

Each is separable, composable, and independently testable.

### 12.5.2 Academic Foundations

- **EMOTE** (Chi et al., SIGGRAPH 2000) — Laban Movement Analysis encoded as computable Effort/Shape transformation.
- **Toric Space** (Lino & Christie, SIGGRAPH 2015) — geometric camera solver for multi-character framing.
- **Virtual Cinematographer** (He/Cohen/Salesin, SIGGRAPH 1996) — HFSM for automatic camera direction.
- **Facade algorithm** (Mateas & Stern, AIIDE 2005) — greedy beat sequencing for interactive drama.
- **Film Editing Patterns** (Wu/Christie, ACM TOMM 2018) — computational film-editing grammar.
- **Murch's Rule of Six** — prioritized scoring for cut selection (emotion, story, rhythm, eye-trace, two-dimensional plane, three-dimensional space).
- **SAFD C.R.A. pattern** — stage-combat choreography as cue/reaction/action.

## 12.6 Scene

Hierarchical spatial composition for game worlds — the reference implementation of the Composer pattern.

| SDK | Purpose | Status |
|---|---|---|
| `scene-composer` | Engine-agnostic scene-graph editing, command-based undo/redo, multi-selection, transform operations, validation | Complete |
| `scene-composer-stride` | Stride engine bridge: entity creation, bundle loading, gizmos, physics picking | Complete |
| `scene-composer-godot` | Godot 4.x bridge: Node3D mapping, procedural gizmos, cross-platform | Complete |

### 12.6.1 Why Scene Is the Reference

Scene Composer established the patterns that all Composer SDKs follow:

- Engine-agnostic core with pluggable bridges.
- Command-based undo/redo.
- Validation system.
- Optional service client for persistence (`lib-scene`).

Two complete engine integrations (Stride, Godot) validate the bridge abstraction. Future Composer SDKs (Cinematic, Voxel) follow this reference.

### 12.6.2 The Engine Bridge Pattern

```
scene-composer (core)          Engine-agnostic: scene graph, commands, selection, validation
    │
    ├── ISceneComposerBridge   Contract for engine integration
    │       │
    │       ├── scene-composer-stride   Stride implementation
    │       └── scene-composer-godot    Godot implementation
    │
    └── ISceneServiceClient    Optional: checkout/commit persistence via lib-scene
```

The Bridge pattern is the mechanism that makes SDKs engine-agnostic. The core SDK knows nothing about Stride or Godot — it operates on abstract scene-graph types. Engine-specific bridges translate between the SDK's abstractions and the engine's native types.

This is how Arcadia-the-platform can support multiple engines without fragmenting the SDK ecosystem. The core SDKs are one codebase; engine bridges are per-engine adapters.

## 12.7 Voxel

Discrete spatial construction for dungeons, housing, NPC building, and lightweight procedural generation as a complement to Houdini.

| SDK | Purpose | Status |
|---|---|---|
| `voxel-core` | Sparse voxel grid (16x16x16 chunks), math, serialization (.bvox), meshing/voxelization | Planned |
| `voxel-generator` | WFC, L-systems, noise, template stamping — deterministic procedural generation | Planned |
| `voxel-builder` | Interactive editing with undo/redo, import (BlockBench/MagicaVoxel), export (.bvox/mesh) | Planned |
| `voxel-builder-{stride/godot/unity}` | Engine bridges | Planned |

### 12.7.1 Why Voxel Matters

Voxel-based geometry is a lightweight alternative to Houdini Digital Assets for discrete geometry. Primary consumers:

- **Dungeon cores** — chamber growth, layout shifting, memory manifestation.
- **NPC builders** — ABML-driven construction of simple structures.
- **Player housing** — Gardener garden type for voxel-based personal space.
- **Divine actors** — environmental marks and transformations.
- **lib-procedural** — rapid geometry generation when Houdini is overkill.

### 12.7.2 Custom Format

The `.bvox` format is chunk-aligned binary with LZ4 compression. Optimized for:

- **Streaming** — load visible chunks first.
- **Delta saves** — re-serialize only dirty chunks.
- **Server efficiency** — compression and chunking reduce storage and network costs.

Artist workflow is supported via BlockBench (.bbmodel) and MagicaVoxel (.vox) import/export, so voxel models can be authored in familiar tools and imported into Arcadia.

### 12.7.3 Why Voxel-Builder Not Voxel-Composer

Voxel editing is self-contained — a single grid, not a hierarchical document. Spatial composition of voxel *objects* happens at the Scene level: SceneComposer recognizes `voxel` node types in scene documents and delegates rendering to VoxelBuilder's engine bridge. No separate "voxel-composer" is needed because the composition is already handled by scene-composer.

## 12.8 Sprite

3D-to-2D sprite sheet pipeline for games that use pre-rendered sprites at runtime.

| SDK | Purpose | Status |
|---|---|---|
| `sprite-theory` | Camera mathematics, atlas packing (MaxRects), mirror optimization, normal maps (Sobel), sprite-sheet JSON metadata | Complete |
| `sprite-composer` | Engine-agnostic orchestrator: capture sessions, project management, preview, undo/redo | Planned |
| `sprite-composer-stride` | Stride engine bridge: FBX loading, skeletal animation, render-to-texture frame capture | Planned |

### 12.8.1 The Immediate Consumer

Per the [Sprite Composer SDK](planning/SPRITE-COMPOSER-SDK.md) design, the immediate consumer is **Defenders of Ba'gata** — which needs every playable character, troop, enemy, and boss rendered as sprite sheets from two orientations (side-view for brawler/attack phases, 55° top-down for defense and boss arena phases). Equipment variants multiply the combinatorial space enormously. Without purpose-built tooling, this is the single most labor-intensive aspect of the game.

### 12.8.2 The Bridge Direction Inversion

Sprite Composer has a unique architectural property: the bridge direction is **inverted** relative to scene-composer and voxel-builder. Where scene-composer and voxel-builder push data TO the engine for display, sprite-composer pulls rendered data FROM the engine. Every capture operation is a round-trip: configure the camera, set animation time, render to off-screen target, read pixels back.

This is a valid variation of the Composer pattern. The principle — engine-agnostic orchestration with engine-specific bridges — holds; the data flow just reverses.

## 12.9 Behavior

ABML (Arcadia Behavior Markup Language) compilation and execution — the universal authoring language for NPC brains, god-actors, and any autonomous entity.

| SDK | Purpose | Status |
|---|---|---|
| `behavior-expressions` | Expression parsing, variable resolution, type coercion, runtime evaluation | Complete |
| `behavior-compiler` | Multi-phase ABML compilation (YAML → AST → bytecode), A*-based GOAP planner, stack-based interpreter | Complete |

### 12.9.1 ABML as the Universal Language

ABML is authored as YAML. The compiler handles control flow, variables, conditions, expression evaluation, and produces portable stack-based bytecode. The interpreter executes bytecode deterministically.

This matters because **ABML is the authoring language for everything emergent in Arcadia**:

- NPC brains are ABML behavior documents.
- God-actors are long-running ABML documents.
- Regional watchers are ABML.
- Dungeon cores, sentient weapons, other actor-bound entities all run ABML.
- Cinematic choreographies output ABML.
- Narrative scenarios output ABML.
- Music behaviors (mood selection logic) can be authored as ABML.

One language, many domains. Learn ABML once; author anything.

### 12.9.2 No Behavior Composer

ABML is YAML text. The "authoring tool" is a text editor with syntax highlighting. A visual behavior-tree editor is possible but not planned — ABML's text format is expressive enough for the intended authors (game designers and god-actor behavior scripts).

## 12.10 The Unified Scenario Pattern

Storyline and Cinematic domains converge on a **unified scenario pattern**: both hand-authored and procedurally generated content produces the same typed format, consumed by passive registry plugins, with god-actors providing all judgment and decision-making.

```
     Hand-Authored                              Procedural
  (storyline-composer)                    (storyline-storyteller)
  (cinematic-composer)                    (cinematic-storyteller)
          │                                        │
          │        same typed format               │
          └──────────────┬─────────────────────────┘
                         │
                         ▼
              lib-{domain} Plugin (L4)
              — Scenario registry (CRUD)
              — Mechanical condition matching (binary predicates)
              — Trigger execution with safety (locks, cooldowns, limits)
              — Instance tracking
              — Does NOT rank or select; returns all matches
                         │
                         ▼
                   God-Actor (ABML)
              — GOAP evaluation of candidates
              — Narrative judgment (which scenario fits the moment)
              — Trigger decision
              — Post-completion mutation execution
```

### 12.10.1 Three Principles

1. **Plugins are passive registries, not intelligent selectors.** They do not rank scenarios; they return all that match binary predicates.
2. **God-actors provide all judgment.** Selection among candidates, timing of execution, post-completion consequences — all authored as ABML behaviors.
3. **Format agnosticism.** The runtime cannot distinguish hand-authored from procedural. A mixed library of 200 authored + 10,000 procedural scenarios works identically.

### 12.10.2 What This Produces

For designers, this means:

- Ship curated authored scenarios for marquee moments.
- Generate procedural scenarios to fill the long tail.
- God-actor aesthetics determine which mix appears where.
- A single content flywheel consumes and produces across both types.

Players cannot distinguish which is which, but they get the best of both — signature moments with authored weight, and an endless stream of organically emerging micro-narratives.

## 12.11 The Video Director

Per [VIDEO-DIRECTOR.md](planning/VIDEO-DIRECTOR.md), the **Video Director** is a composition layer that maps musical structure onto Bannou's narrative, choreographic, and cinematic infrastructure to generate real-time entertainment cinematics (music videos, adventure trailers, promotional content) from game-world data.

### 12.11.1 What It Composes

Inputs: a musical template, thematic intent, character selection criteria, scene preferences.

Outputs: a deterministic, seed-reproducible cinematic rendered in the game engine, orchestrating Storyline, CinematicStoryteller, MusicStoryteller, and the Actor runtime.

### 12.11.2 The Role Model

The Video Director introduces a **role-based composition model** borrowed from music composition. Characters cast in a cinematic are assigned roles:

- **Protagonist / melody** — lead role with highest action density and camera focus.
- **Counterforce / bass** — opposing force with reactive choreography.
- **Ensemble** — supporting characters with moderate action.
- **Atmosphere** — background characters with minimal action (cycling animations, crowd effects).

Role assignments determine GOAP cost budgets, camera idiom selection, and screen-time allocation. This maps directly onto the anime "cel layer" paradigm ([§11.5](#115-the-anime-production-paradigm)) — protagonist is full animation on the A cel, atmosphere is efficient limited animation on background cels.

### 12.11.3 Initiative-Driven Combat

The Video Director's role model extends to the combat system ([§11.6](#116-initiative-driven-combat)). Initiative determines which combatant is the protagonist/melody role for the current exchange. Role oscillation across exchanges produces the back-and-forth rhythm that makes combat feel like a conversation.

## 12.12 Developer Streams

A final extension: the [Developer Streams](planning/DEVELOPER-STREAMS.md) design shows how the same infrastructure used for in-game cinematic direction can orchestrate multi-feed video streaming from a developer's workspace.

### 12.12.1 The Parallel

A divine actor (Regional Watcher pattern) can orchestrate streaming from a developer's screen — selecting which terminal, IDE, or browser feed to feature based on detected activity events, compositing them into a single directed output stream via Broadcast's RTMP pipeline.

This uses **100% of the same infrastructure** as in-game cinematic direction:

- Actor runtime for the director.
- ABML behaviors for direction logic.
- Director plugin for orchestration.
- Agency for feed management.
- Broadcast for composition and RTMP output.

Improvements to directing behaviors, feed management, or composition for either use case (developer streams or in-game cinematics) directly benefit the other.

### 12.12.2 Why This Matters

This is the architecture extending beyond games. The same Bannou deployment that runs a game's content flywheel can run a developer's streaming channel. The SDK ecosystem is not game-specific; it is *compositional content generation* that applies wherever dynamic sequencing is needed.

A studio building on Bannou can use the same infrastructure for:

- In-game cinematics.
- Adventure trailers from game-world footage.
- Music videos composed from character archives.
- Developer streams with automated director.
- Promotional content generated nightly from current world state.

All from the same SDK ecosystem, the same plugins, the same ABML.

## 12.13 The Compositional Architecture Is Complete

Stepping back, the Procedural Creative Generation section reveals what makes Arcadia structurally different from other platforms.

Every autonomous domain follows the same pattern:

| Domain | Decision Layer | Composition Layer | Execution Layer |
|---|---|---|---|
| **NPC Cognition** | Actor GOAP | Variable providers + ABML | BehaviorModelInterpreter |
| **Music** | ABML (mood select) | MusicStoryteller (GOAP) | MusicTheory |
| **Narrative** | Regional Watcher | StorylineStoryteller (GOAP) | StorylineTheory |
| **Combat/Choreography** | Event Brain | CinematicStoryteller (GOAP) | CinematicTheory → CinematicInterpreter |
| **Scene** | Game server / designer | SceneComposer | Engine bridge |
| **Sprite** | Content pipeline | SpriteComposer | Engine bridge render-to-texture |
| **Voxel** | Game server / dungeon core | VoxelGenerator | VoxelBuilder bridge |

**GOAP is the universal planner**. Every composition layer uses GOAP. Improvements to the GOAP planner benefit every creative domain simultaneously.

**ABML is the universal authoring language**. Behavior documents drive NPCs, gods, cinematics, and scenario orchestration. Learn once; author anything.

**Theory/Storyteller/Composer is the universal structure**. Each new creative domain fits the same three-layer mold.

This is not just efficiency; it is a deliberate architectural choice with compounding returns. Each domain strengthens the others. The investment in GOAP pays off across all of them. The investment in ABML pays off across all of them. A studio building on Bannou inherits a content-generation stack whose components multiply rather than add.

## 12.14 Connecting to the Rest of the Document

Procedural Creative Generation underpins most emergent behavior:

- [§6](#6-the-npc-intelligence-stack) — NPC cognition uses ABML + GOAP.
- [§7](#7-the-content-flywheel) — the flywheel produces content via these SDKs.
- [§10](#10-the-social-fabric--cultural-emergence) — cultural emergence uses god-actor ABML for curation.
- [§11](#11-combat--cinematic-choreography) — the cinematic system is a prime example of this pattern.
- [§13](#13-actor-bound-entities-living-places-and-things) — all actor-bound entities run ABML.
- [§§15-18 (Realm Design)](#part-iv--realm-design-guides) — realms differ partly by which SDKs they heavily exercise (combat-heavy FANTASIA invokes Cinematic often; social-heavy invokes Music and Lexicon; etc.).

The SDKs are the engine of emergence. When the world feels alive in Arcadia, it is because formal theory + GOAP + authored ABML are quietly producing coherent content in dozens of domains at once — deterministically, cacheably, without any LLM inference in the loop.

---

# 13. Actor-Bound Entities: Living Places and Things

## 13.1 The Generalized Living World

[§5](#5-the-unified-cognitive-progression--system-realms) established the mechanism: the three-stage cognitive progression, the Genesis plugin, the system realm pattern. This section takes that mechanism and applies it concretely to specific entity categories, showing how Arcadia's distinctive gameplay possibilities — sentient dungeons, living weapons, active gods, sacred sites, memory-woven items — all emerge from the same substrate.

The through-line: **anything in Arcadia that should feel alive over time uses the Genesis lifecycle and inhabits a system realm as a Character**. This is not coincidental convergence; it is deliberate architectural economy. Every new "living thing" category that a designer imagines is built the same way, composes the same primitives, and inherits the same infrastructure.

This section develops: dungeons-as-actors ([§13.2](#132-dungeons-as-actors)), sentient weapons and the zero-plugin validation ([§13.3](#133-sentient-weapons-the-zero-plugin-validation)), gods as characters ([§13.4](#134-gods-as-characters)), sacred geography via sanctuaries and spirit dens ([§13.5](#135-sanctuaries-and-spirit-dens)), logos resonance items ([§13.6](#136-logos-resonance-items)), the memento spiritual ecology ([§13.7](#137-the-memento-spiritual-ecology)), and the cross-cutting design pattern that unites all of these ([§13.8](#138-the-cross-cutting-pattern)).

## 13.2 Dungeons as Actors

The most complex actor-bound entity in Arcadia is the **Dungeon** — a sentient spatial domain with memory, personality, mana economy, creature production, and a bonded dungeon master.

### 13.2.1 Lifecycle Applied

Dungeons follow the three-stage progression with domain-specific ceremony:

**Stage 1 — Dormant**. A dungeon begins as spent pneuma accumulating logos patterns at a location with sufficient mana density. It is a place — rooms, corridors, traps — but not yet an agent. A dungeon-spawning god-actor (typically a monster-domain or transformation-domain deity) detects conditions (battlefield pneuma, mass grief, stagnant mana, location spiritual significance) and calls `/genesis/entity/create` with template `dungeon_core`. The dungeon has a mana wallet, a genetic library inventory, memory inventory, and a physical form binding to a Location.

**Stage 2 — Stirring**. As the dungeon's mana wallet accumulates (from ambient autogain, dungeon-mana-absorption from intruders, deaths within domain), Seed growth crosses Stirring threshold. Genesis spawns the dungeon-core Actor with pre-compiled ABML behavior. The dungeon can now perceive (characters entering the domain, combat events, deaths), decide (which rooms to reshape, which traps to arm, which inhabitants to spawn), and act (calling `/mapping/domain/shift-layout`, `/item/instance/create` for pneuma-echo monsters, `/status/grant` for atmospheric effects).

**Stage 3 — Awakened**. A Character is created in DUNGEON_CORES system realm. Personality traits seeded from the formative conditions (a battlefield dungeon has high aggression; a grief dungeon has high melancholy). The Actor binds. The dungeon now has CharacterPersonality, CharacterEncounter (remembers every intruder), CharacterHistory (its formative story), Relationships (potentially bonded to a dungeon master). Its ABML behavior continues unchanged — the null-safe variable references now return real data.

### 13.2.2 What Dungeons Add Beyond the Base Pattern

The dungeon plugin (`lib-dungeon`, L4) provides dungeon-specific orchestration that cannot be handled by Genesis alone:

| Concern | Mechanism |
|---|---|
| **Spatial domain** | Rooms, corridors, floors as Location hierarchy in the dungeon's system realm. Transit connections between rooms. Mapping for spatial data. |
| **Mana economy** | Mana currency wallet. Spends mana to spawn creatures, activate traps, shift layout. Earns mana from deaths within domain. |
| **Creature production** | Two tracks: **pneuma echoes** (instant, mana-cost, dumb — via `/item/instance/create` and Actor spawn) and **habitat creatures** (Workshop-produced, grow over game-time, smarter). |
| **Dual mastery patterns** | Pattern A (account-level — dungeon IS the garden) and Pattern B (character-level — dungeon layers onto gameplay). Governed by household split mechanic. |
| **Physical construction** | Cross-service: Save-Load for persistence, Mapping for spatial index, Scene for visual composition, Procedural for generated geometry. |
| **Floor system** | Each floor is a Location with its own Environment configuration. Strategic environment selection: desert floor for resource exhaustion, jungle floor for ambush concealment. Floor creation gated by dungeon cognitive stage. |

### 13.2.3 Dungeon Mana Absorption

Per the [Dungeon Mana Absorption](planning/DUNGEON-MANA-ABSORPTION.md) design, dungeons continuously sap mana from intruders who cross their domain boundary. This creates:

- **Natural time limit on exploration** — intruders cannot linger indefinitely.
- **Economic cost for dungeon diving** — mana is a character resource.
- **Non-lethal income stream** — the dungeon earns mana without requiring kills.
- **Scaling with cognitive stage** — Dormant absorbs lightly; Ancient absorbs deeply.

This is inspired by *Solo Leveling* gate mechanics and Dungeon Core LitRPG, grounded in Arcadia's pneuma thermodynamics. It composes entirely from existing primitives (Currency, Status, Seed, Actor, Environment) with no new services.

### 13.2.4 The Dungeon Memory Dual-System

Dungeons use the memento pattern for memory management:

- **Collection** (permanent knowledge) — "I have experienced X." First combat victory, first boss kill, first adventurer death. Cannot be removed; feeds Seed growth in `memory_depth.capture`.
- **Inventory** (consumable creative resources) — Every notable event creates a "memory item" in the dungeon's memory inventory. Items have significance scores, event types, participants, emotional context. Consumable: when the dungeon manifests something (a painted mural, a phantom echo, a unique loot item), it spends a memory item. Inventory capacity scales with `memory_depth` seed growth.

This gives dungeons **both** permanent species-knowledge AND a finite creative resource pool. A dungeon can only create as many unique manifestations as it has accumulated memory items. Trading memory items between dungeons is possible — logos trading.

### 13.2.5 The Bonded Dungeon Master

A dungeon can bond to a character as its **dungeon master** via Contract. The master and the dungeon have parallel Seeds that grow together. The master can direct (suggest room expansions, request specific monster types, influence aesthetic), and the dungeon influences back (master perceives intruders through the dungeon's awareness; master can operate traps remotely).

The master bond has two patterns:

- **Pattern A** — Account-level (the player's seed IS the dungeon master). The dungeon is the player's primary gameplay; their entire experience is directing the dungeon.
- **Pattern B** — Character-level (a specific character is the dungeon master). The dungeon is one of many things that character does; it exists alongside normal character gameplay.

The pattern is chosen at dungeon creation and affects which Seed (account vs. character) is bound.

## 13.3 Sentient Weapons: The Zero-Plugin Validation

Per [§5.5.2](#552-living-weapons-the-zero-plugin-validation), living weapons are the **strongest validation** of the composability thesis. Every requirement for a weapon with deep personality, memory, wielder-bond, cultural significance, and progressive awakening maps to an existing service:

| Requirement | Service | Mechanism |
|---|---|---|
| Physical item | `lib-item` (L2) | Item instance with Genesis binding its form |
| Progression | `lib-genesis` (L2) | Template `sentient_weapon` with experience wallet |
| Cognition | `lib-genesis` + `lib-actor` (L2) | Automatic via Genesis lifecycle |
| Personality | `lib-character-personality` (L4) | Character in SENTIENT_ARMS with personality record |
| Memory | `lib-character-encounter` (L4) | CharacterEncounter records |
| History | `lib-character-history` (L4) | CharacterHistory records |
| Wielder bond | `lib-relationship` (L2) | `weapon_wielder` RelationshipType |
| Capabilities | `lib-status` (L4) | Status effects from Seed capabilities |
| Voice | `lib-chat` + `lib-lexicon` (L1/L4) | Structured messages |
| Cultural significance | `lib-storyline`, `lib-realm-history` (L4) | Same archive pipeline as any character |

**Zero new plugins.** The code required: a Genesis template (seed data), ABML behaviors (authored content), and the game engine crediting the weapon's experience wallet when combat occurs.

### 13.3.1 Weapon Lifecycle in Practice

A worked example from the [Actor-Bound Entities design](planning/ACTOR-BOUND-ENTITIES.md):

**Forging**. A divine smith-god (regional watcher) detects that a master blacksmith has forged a blade with unusual devotion over decades. The god-actor's ABML triggers: Item instance created (flagged awakening-capable); Genesis entity created with `sentient_weapon` template; initial growth credited from formative forging context.

**Dormant service**. The weapon is a fine sword with passive stat bonuses. Over years of combat, game events report: kills → Collection grants (first_hundred_kills, boss_slayer); Seed growth via the Collection→Seed pipeline; Seed phase crosses Stirring threshold.

**Stirring awakening**. Genesis spawns an event-brain Actor for the weapon. The weapon "wakes up" — begins perceiving combat events, forming impressions, sending vague impulses (danger sense, emotional perception) to its wielder. It has preferences stored in actor state but no rich inner life.

**Awakening**. Seed reaches Awakened threshold. Genesis creates a Character in SENTIENT_ARMS. Personality seeded from accumulated actor feelings. Actor binds via `/actor/bind-character`. The weapon SPEAKS. It has opinions. Remembers every battle. `${personality.loyalty}` is high because of long service. `${encounters.sentiment.current_wielder}` is deeply positive.

**Wielder transitions**. Wielder dies in battle. The weapon's CharacterEncounter records preserve the memory. The `wielder_bond` domain resets for a new wielder. Combat experience, elemental mastery, legend persist. The weapon grieves — personality shift via experience-driven evolution. A new wielder picks it up; the weapon's ABML evaluates compatibility; the bond rebuilds slowly.

**Legendary**. Centuries later (Legendary phase), the weapon has had seven wielders, participated in three wars, killed a god's avatar. Personality has evolved across centuries. CharacterHistory spans generations. It is, in every meaningful sense, a person trapped in a sword.

**End**. When finally shattered in a climactic battle, the character is compressed via lib-resource. The archive feeds Storyline (quests about "the lost blade"), future blacksmiths attempt to reforge it (guided by archive data), NPCs who wielded it have persistent memories, dungeons that witnessed battles involving it manifest memories.

### 13.3.2 Why No Weapon Plugin Exists

lib-dungeon exists because dungeons need domain-specific orchestration that can't be game-engine-coordinated: atomic multi-service operations (spawn monster + place trap + shift layout + consume mana), spatial domain management, two-track creature production. These are complex enough to warrant a dedicated L4 plugin.

Living weapons need **no such orchestration**. Every weapon operation is a single existing API call: Item creation, Seed growth, Actor spawning (via Puppetmaster), Character creation, Status grants, Relationship bonds, perception injection. The game engine coordinates these; no atomic multi-service orchestration is required.

This is the composability validation. A non-trivial feature (sentient weapons with personality, memory, centuries of history) requires zero new plugins. It composes entirely from primitives that exist for unrelated reasons.

### 13.3.3 What Living Weapons Learn From Swordians

The *Tales of Destiny* Swordian system (1997 PS1) is the definitive JRPG implementation of living weapons. Six sentient swords with personality and relationships. But Swordians have a design gap: **the personality is narrative-only, never mechanically expressed**. The weapon never refuses a command, never fights differently based on mood, never rewards relationship depth.

Arcadia's architecture closes this gap:

| Swordian Weakness | Arcadia Solution |
|---|---|
| Personality is narrative-only | Actor + CharacterPersonality → personality that mechanically affects decisions |
| Bond depth is not mechanized | Seed `wielder_bond` domain → quantified bond that unlocks capabilities |
| No independent growth | Seed growth is autonomous (accumulates from game events) |
| Weapon never refuses | ABML behavior with `active.refuse` capability gated by personality traits |
| Power is wielder-driven only | Symbiotic: weapon seed grows independently AND influences wielder via status effects |
| Fixed six weapons, no evolution | Personality evolution via CharacterPersonality's experience-driven trait shifts |
| No memory across wielders | CharacterEncounter records persist across wielder changes |

What Arcadia achieves that no existing game has: the "ideal living weapon" combining *Tales of Destiny* narrative presence, *Xenoblade 2* affinity mechanics, and *Persona* bond-equals-power progression — all emerging naturally from composing existing services.

## 13.4 Gods as Characters

Gods in Arcadia are not flavor text. They are **Characters in the PANTHEON system realm**, inhabited by god-actors running long-term ABML behaviors, with real mechanical presence.

### 13.4.1 Lifecycle Applied

Gods use the actor-bound entity pattern with some domain-specific variations:

- **Origin**: A god's Genesis entity is typically created at deployment (system-realm characters are seeded), not spawned organically. The template is `deity_domain` with personality traits, domain specializations, aesthetic preferences.
- **Stage 2 (Event Brain)**: Typically instantaneous at creation. Gods need their actor running immediately to start orchestrating.
- **Stage 3 (Character Brain)**: Typically instantaneous at creation. Gods need their full character data (personality, aesthetic) for orchestration to work.
- **Divinity economy**: Gods accumulate divinity currency from worship events, domain-relevant actions, follower activity. They spend divinity on interventions, blessings, avatar manifestations.

### 13.4.2 What Gods Do

Per the divine service (`lib-divine`, L4), gods perform several roles:

- **Regional Watchers** — monitor realm events, curate narratives (content flywheel orchestration — [§7](#7-the-content-flywheel)).
- **Blessing dispensers** — grant Status effects to followers who impress them. Blessings are expensive (divinity cost) and personality-weighted (a generous god blesses more; a jealous god less).
- **Content intervention** — economic intervention (§9.9), plot armor depletion (§8.4), cultural curation (§10.11), primitive concept discovery (§2.7.3).
- **Avatar manifestation** — a god can temporarily manifest physically, creating a character in a game realm that is "the god embodied." Extremely expensive; rare; highly significant.

### 13.4.3 The PANTHEON Realm's Special Properties

Per the [Divinity Generation Architecture](planning/DIVINITY-GENERATION-ARCHITECTURE.md), gods use the **Seed bond propagation** pattern for divinity generation. Follower characters' domain seeds (devotion.commerce, devotion.war) bond to the god's domain seeds. Growth on a follower's seed propagates to the god's seed at a configured ratio. The god's divinity wallet accumulates from all followers' activities.

This is the bag-of-kills pattern (§9.11): many small contributions accumulate into capability; many consumers (blessings, interventions, avatars) spend independently.

### 13.4.4 Divine Personality Variation

Different gods produce different content simply by running different personalities. Per [§10.11.5](#10115-god-personality-shapes-culture), a fishing village under a nature-god produces organic-seasonal coming-of-age; under a commerce-god, guild-delivered practical; under a war-god, trial-by-combat. Same material conditions, different cultural expressions. The designer's lever for "what this realm feels like" is largely **which gods interfere and how they differ**.

## 13.5 Sanctuaries and Spirit Dens

Per the [Sanctuaries and Spirit Dens design](planning/SANCTUARIES-AND-SPIRIT-DENS.md), Arcadia has two complementary sacred-geography systems that compose entirely from existing primitives.

### 13.5.1 Sanctuaries — Divine Non-Aggression Zones

A sanctuary is a location where a **divine imprint** in the local pneuma field suppresses aggressive intent, creating a genuine non-aggression zone. Predators and prey coexist peacefully. Wolves drink beside deer. Eagles perch above rabbits. Within the sanctuary core, **violence is impossible** — pneuma resonance absorbs the spiritual energy required for aggressive intent before action can form.

This is a divine act deposited into location state. A god spending significant divinity to stop a massacre, heal a dying forest, or create an oasis leaves a **standing resonance** in the pneuma field. The sanctuary persists so long as someone reinforces it — divine renewal, shrine-keeper consecration via memento consumption, worship/pilgrimage generating emotional mementos — otherwise decays over centuries.

Mechanically: Environment service provides `sanctuary_suppression` (0.0-1.0) per Location; NPC ABML GOAP modifies aggression action costs as `base_cost / (1.0 - suppression)`; at suppression 1.0, aggression costs approach infinity. **Non-aggressive actions (drinking, resting, grazing) are unaffected** — predators voluntarily enter sanctuaries for water and rest.

A sanctuary is compositional: Location (spatial identity) + Environment (suppression value) + Divine (originating god + divinity expenditure) + Status templates + ABML behaviors. **Zero new plugins.**

### 13.5.2 Spirit Dens — Crystallized Species-Logos

A spirit den is a location where a **leyline convergence** provides sufficient ambient pneuma to crystallize accumulated species-specific logos into a supernatural manifestation: the **spirit animal**. The spirit animal is not a normal member of its species that happens to be impressive — it is the Platonic ideal, the concept of the species given form. Around it, many ordinary members of the species naturally congregate.

Spirit dens form from three systems interacting:

1. **Leyline energy** (Environment) — high ambient mana density from geological pneuma features.
2. **Species memento accumulation** — every animal death creates a DEATH_MEMENTO at its Location with species data. Over centuries, locations where a species thrived accumulate deep inventories of species-specific mementos. Pruning curates to the "greatest hits" — the bravest stag, the doe who led through winter, the ancient buck who lived to extraordinary age.
3. **Undisturbed accumulation** — if shrine keepers or necromancers consume the mementos, density never reaches crystallization. Spirit dens form in secluded, hard-to-reach places *because* those places are undisturbed.

When all three align, the ambient pneuma spontaneously processes accumulated logos into a standing manifestation. Genesis creates the spirit animal entity; Seed grows through stages; at Awakened, Character in a NATURE system realm emerges.

**The spirit animal knows every creature that has ever lived and died in its domain** because it crystallized from their logos. Its personality is aggregated from memento data. Its memory spans centuries. It is, in every meaningful sense, the species' oldest and most-itself member — manifest.

### 13.5.3 Predator vs. Prey Asymmetry

Spirit dens have compelling asymmetry:

- **Prey spirit den** (deer, elk, rabbits) — approach danger is external (navigating predator territories to get there). Core atmosphere is pastoral, peaceful. Spirit deer doesn't flee (fears nothing). The contrast between journey and destination is the emotional payload.
- **Predator spirit den** (wolves, eagles, bears) — approach danger is the species itself at maximum defense intensity. The pack treats the approach as deepest-core territorial intrusion. Core atmosphere is intense, primal. Earning the spirit wolf's regard requires demonstrating something — strength, respect, kinship, spiritual resonance.

Both use the same mechanism (crystallized logos at leyline + memento accumulation); the species changes the gameplay feel entirely.

### 13.5.4 The Convergence Case

The rarest locations in Arcadia are where a divine sanctuary overlaps with a leyline convergence hosting spirit dens — a place where a god performed a miracle at a leyline, and the combined peace and density has accumulated multi-species logos over centuries. These are Arcadia's holiest sites. Players reaching them is a rare pilgrimage.

### 13.5.5 Workshop-Driven Spiritual Production

Both sanctuaries and spirit dens use Workshop's lazy evaluation pattern (§9.7.6) for their spiritual sustenance. A sanctuary has stage inventories: raw presence → accumulated reverence → consecrated sanctity → divine resonance. Each stage has a blueprint with workers and rate modifiers. A spirit den similarly stages ambient logos → species resonance → crystallized essence → spiritual manifestation.

**Workers are the entities whose presence sustains the sacred location.** For sanctuaries: peaceful creatures (weight 0.5-1.5, with predators-at-peace weighted higher because their peace IS the miracle), pilgrims (2.0), worshipers (3.0), shrine keepers performing consecration (5.0-10.0). For spirit dens: kin animals (1.0), the spirit animal itself at Stirring+ (5.0-20.0 — bootstrapping effect), visiting shamans (3.0-5.0), necromancers consuming mementos (**-2.0** — net negative contribution).

The production-vs-decay equilibrium produces the phases naturally: Phase 1 (full strength) when workers >> decay rate; Phase 4 (collapsed) when workers = 0. No hardcoded thresholds. The Workshop math organically reproduces the sanctuary/den lifecycle.

Workshop's lazy evaluation means hundreds of sacred sites can exist per realm with **zero per-tick server cost**. When a player approaches, the production history materializes in one pass. A sanctuary untouched for months gets its full spiritual history computed on the next query.

## 13.6 Logos Resonance Items

Per the [Logos Resonance Items design](planning/LOGOS-RESONANCE-ITEMS.md), Arcadia extends the actor-bound entity thinking *downward* — to equipment items that carry crystallized experience.

### 13.6.1 The Mechanism

When a character defeats a boss under significant circumstances — survives near-death encounters, uses distinctive tactics, clears a dungeon whose mementos carry the failures of prior attempts — a god-actor (regional watcher or dungeon core) may decide the moment is **formative enough** to crystallize a **logos resonance item**.

The god-actor runs multi-service queries to gather context: killer's combat identity, primary weapon, dungeon encounter history, predecessor death mementos, killer's personality. It then constructs a **dynamic loot table** for that specific moment:

- Item template based on character's weapon preference.
- Influences encoding fight circumstances (`lightning_affinity`, `high_mobility_mastery`, `dungeon_depth_7`, `boss_slayer_storm_wyrm`).
- Predecessor influences (`predecessor_echo_warrior`, `predecessor_echo_mage`) from memento data.
- Generation tier 3 (unique) with custom quality modifier based on near-death count.

The resulting item is literally unreproducible — the specific circumstances can never recur exactly.

### 13.6.2 Activation Prerequisites

The item's affixes carry **activation prerequisites** — character-side requirements evaluated at runtime. An item is always functional, always tradeable, but its affixes contribute scaled fractions based on fidelity:

```
activation_fidelity = sum(met_prerequisite.fidelityContribution × met_prerequisite.fidelity)
                    / sum(all_prerequisite.fidelityContribution)
```

A "Stormcaller's Fury" affix with three prerequisites (lightning_mastery collection entry, fought_storm_wyrm encounter, personality.courage > 0.6) might activate at 97% for the character who earned it (meets all three strongly), 48% for a buyer who meets two partially and one not at all, and 10% for an incompatible-build buyer.

The item works for all of them — but dramatically better for the earner.

### 13.6.3 The IActivationPrerequisiteProviderFactory Pattern

The prerequisite evaluation follows the same DI inversion pattern as Variable Providers and Quest Prerequisites (§§6.4, 9.9). The interface lives in `bannou-service/Providers/`; Affix (L4) discovers implementations via `IEnumerable<IActivationPrerequisiteProviderFactory>`. Collection and Seed (L2) provide always-available prerequisite checks; Character-Personality, Character-Encounter, Character-History (L4) add rich soft-dependency checks; Quest (L2) adds quest-completion prerequisites. Missing providers degrade gracefully — unmet prerequisites evaluate to fidelity 0.0.

This is architectural consistency: the same pattern that lets Actor (L2) reason about personality (L4) lets Affix (L4) reason about any character data from any layer, via the same neutral interface.

### 13.6.4 Why This Matters

Logos resonance items solve the soulbinding problem elegantly. Traditional soulbinding locks items to their earner, exits the economy, is anti-flywheel. Logos resonance items:

- **Are earned** — activation fidelity is highest for the earner.
- **Are tradeable** — the item functions for anyone.
- **Generate content** — acquirers see unmet prerequisites as gameplay goals ("I need to fight storm wyrms").
- **Carry history** — custom stats encode the originating event; NPCs can read them; narratives compose around them.

The predecessor memory layer is particularly rich. When multiple characters attempt and fail a challenge, their mementos accumulate at the dungeon's rooms. When someone eventually succeeds, the god-actor incorporates predecessor memories into the resulting item. The "Echo of the Fallen Warrior" affix gives fire resistance and carries the warrior's spirit. The "Mage's Final Lesson" affix gives melee defense and carries the mage's last insight. **The world's accumulated failures enrich the world's rare successes.**

### 13.6.5 The Content Flywheel Extension

Logos resonance items create a tertiary flywheel loop alongside narrative (§7) and spiritual (§13.7):

```
Formative event → Resonance item created
  → Item enters economy with experiential prerequisites
  → Acquirer pursues prerequisites (new gameplay goals)
  → Gameplay generates encounters, mementos, archives
  → Archives feed narrative flywheel
  → Encounters feed spiritual flywheel
  → Both feed FUTURE resonance item creation
  → Richer items with more predecessor memories
  → Loop accelerates
```

A Year 5 resonance sword from a dungeon challenged by hundreds of characters carries the spiritual weight of all those attempts. Its prerequisites reference encounters, personalities, and experiences spanning years of simulation. It is literally more valuable because the world is older.

## 13.7 The Memento Spiritual Ecology

Beneath dungeons, spirit dens, and logos resonance items, a lower-level pattern connects them all: **the memento spiritual ecology**.

### 13.7.1 What Mementos Are

Per the [Memento Inventories design](planning/MEMENTO-INVENTORIES.md), each location in the game world accumulates **memento items** generated from real gameplay events:

- **DEATH_MEMENTO** — a character or creature died here. Contains species, personality snapshot, cause of death, significance score.
- **COMBAT_MEMENTO** — a significant battle occurred. Participants, outcome, emotional resonance.
- **EMOTIONAL_MEMENTO** — a moment of grief, joy, triumph, despair. Emotional data, narrative context.
- **MASTERWORK_MEMENTO** — a crafted object of exceptional quality was made here. Craftsman, process, materials.
- **RITUAL_MEMENTO** — a ceremony, oath, wedding, funeral. Participants, type, outcome.

Mementos live in location-scoped Inventories (via `lib-inventory`, L2). They are Item instances (via `lib-item`, L2) with rich metadata in customStats referencing the originating event. They accumulate passively as events happen. They prune by significance when inventory fills.

### 13.7.2 Who Consumes Mementos

Characters with specific perception abilities interact with mementos:

- **Necromancers** summon spirit echoes from death-mementos.
- **Mediums** commune with emotional mementos.
- **Historians** extract forensic evidence from combat and ritual mementos.
- **Bards** draw emotional resonance for performances.
- **Detectives** investigate cold cases through combat/emotional mementos.
- **Craftsmen** imbue masterwork mementos into new items as lineage markers.

Consumption is the mechanical act: the character interacts with the memento; the Inventory instance may be removed (if consumable) or marked as read; effects manifest (Status grants, Character Encounter additions, Lexicon discovery advances, item modifications).

### 13.7.3 The Cross-Pattern Economy

Mementos are the **substrate for multiple actor-bound entity behaviors**:

- Dungeons consume their own room mementos to manifest phantom replays and compose unique loot.
- Spirit dens crystallize from undisturbed species mementos at leyline points.
- Logos resonance items incorporate predecessor death mementos as echo affixes.
- God-actors observe memento accumulation to identify culturally significant patterns.
- Sanctuary maintenance consumes emotional peace mementos generated by ongoing worship.

**The memento pattern is shared infrastructure for "the world remembers what happens there."** Different actor-bound entities consume it differently; the ecology emerges from the interaction.

### 13.7.4 Necromancer vs. Spirit Den Tension

A compelling emergent conflict: a necromancer raiding a spirit den's memento inventory is literally stealing the raw material that feeds the spirit animal's growth. This isn't authored rivalry — it's mechanical resource contention. The necromancer wants death mementos for summoning; the spirit den needs them for crystallization. Both are legitimate uses of the same resource.

This produces gameplay without scripting: necromancers who find spirit dens face a choice (take the mementos or leave them), spirit animals who become aware of memento-theft develop Disposition resentment, other nature-affiliated characters develop drives to protect spirit dens, conflicts emerge organically.

### 13.7.5 Zero New Infrastructure

All memento mechanics compose from:

- **Item templates** (DEATH_MEMENTO, COMBAT_MEMENTO, etc.).
- **Inventory** (location-scoped containers).
- **Location** (spatial scope).
- **Actor** (characters interact via ABML behaviors).
- **Scheduled worker** (Workshop's lazy evaluation handles accumulation-decay dynamics).

No new plugin. No new state store. No new API surface beyond standard Item/Inventory calls. The pattern is pure composition — the architectural property that makes Arcadia maintainable despite its scope.

## 13.8 The Cross-Cutting Pattern

Zooming out, the entities developed in this section share a single pattern. The surface varies; the substrate is the same.

| Entity Type | Physical Form | System Realm | Unique Complication | Needs Plugin? |
|---|---|---|---|---|
| Dungeons | Location (spatial domain) | DUNGEON_CORES | Spatial + mana + creature production + mastery | Yes (`lib-dungeon`) |
| Sentient weapons | Item | SENTIENT_ARMS | Wielder bond with combat integration | No |
| Gods | None (conceptual) | PANTHEON | Divinity economy + blessings + cross-realm | Yes (`lib-divine`) |
| Spirit animals | Character in NATURE realm | NATURE | Crystallization from mementos + leyline | No |
| Sanctuaries | Location (Environment modifier) | — | Divine maintenance + decay | No |
| Logos resonance items | Item with activation prerequisites | — | Prerequisite provider + dynamic loot | No (Affix extension only) |
| Mementos | Item in location inventory | — | Accumulation + pruning + consumption | No |

**Four of seven need no new plugin.** The others need thin orchestration layers for complex multi-service operations (dungeons' spatial-manipulation, gods' divinity economy). The vast majority of "things that feel alive" compose from existing primitives.

### 13.8.1 The Design Question: Plugin or No Plugin?

When designing a new actor-bound entity, the question is always: *does this need domain-specific atomic multi-service orchestration?*

- Yes → write a thin plugin that orchestrates.
- No → use Genesis template + ABML behaviors + game-engine coordination.

Living weapons said *no*. Treasure chests say *no*. Haunted buildings say *no*. Familiar spirits say *no*. Sentient ships *probably* say *no* (though navigation orchestration is borderline). Awakened forests *probably* say *no*.

The plugin bar is high. It exists only when the coordination genuinely cannot be handled by authored content and API composition.

### 13.8.2 What This Produces

The cumulative effect of this pattern across many entity types:

- **A world dense with potential agents** — every sword that has seen enough combat is a potential sentient weapon; every location where enough deaths occurred is a potential dungeon; every domain with enough relevant events is a potential deity; every leyline convergence with enough species mementos is a potential spirit den.
- **Surprise emerges** — the world is pregnant with awakening. Players encounter moments where a weapon they've wielded for years suddenly speaks to them, where a cave they've explored a dozen times has *changed*, where a quiet grove reveals a luminous stag.
- **History carries forward mechanically** — the fallen warrior's death feeds the weapon that defeats the next boss; the dead village's mementos feed the dungeon that grows beneath it; the forgotten spring feeds the god who blessed it centuries ago.
- **Content generation scales with world age** — every actor-bound entity is a content node. The number of potential nodes grows with every event. The number of actual awakenings grows with the number of potential ones. The flywheel accelerates.

## 13.9 Connecting to the Rest of the Document

This section draws from and feeds into many others:

- [§5](#5-the-unified-cognitive-progression--system-realms) — the core pattern these entities instantiate.
- [§7](#7-the-content-flywheel) — all actor-bound entity destructions feed the flywheel.
- [§8](#8-life-death-and-generational-cycles) — the three-stage progression is a generalized lifecycle.
- [§9](#9-the-economic-substrate) — genesis wallet + bag-of-kills pattern makes entities economic actors.
- [§10](#10-the-social-fabric--cultural-emergence) — spirit dens and sanctuaries contribute to cultural formation.
- [§14](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology) — living weapons, logos resonance items, sentient equipment all tie to the enchantment theory.
- [§§15-18 (Realm Design)](#part-iv--realm-design-guides) — realms vary partly by which actor-bound entity types they emphasize.

Actor-bound entities are the most visible demonstration of Arcadia's composability thesis. A world where any place, thing, or concept can progressively awaken into an autonomous agent — using the same infrastructure across every case, with rare plugins only when orchestration demands it — is the world that the architecture makes possible. This section shows what it looks like when that possibility becomes concrete.

---

# 14. Equipment, Enchantment & the Unified Theory of Arcadian Technology

## 14.1 The Question This Section Answers

[§2](#2-the-metaphysical-substrate--the-knowledge-as-power-thesis) established that magic in Arcadia is thermodynamically compliant — pneuma control, not creation from nothing — and that technology progression equals Lexicon expansion. That was the theory. This section develops how that theory becomes **mechanical gameplay**: how swords, armor, tools, buildings, and eventually machines work in a world where magic and matter are the same substrate addressed at different layers.

The central claim is ambitious: **all technology in Arcadia — from flint-and-tinder to enchanted steam rail to late-era logos-computational substrates — is a single coherent phenomenon**. Crafting is enchantment. Enchantment is logos-patterned pneuma circuitry. Equipment is an antenna tuned to the wielder's pneuma shell. Industrial machinery is enchanted mechanism. And the entire spectrum — thousands of years of civilizational development — operates on the same underlying substrate, differing only in sophistication, scale, and accumulated Lexicon vocabulary.

This produces a world that is internally consistent across the entire tech spectrum. A designer creating a pre-Bronze-Age FANTASIA realm and a designer creating a steampunk-enchantment ARCADIA realm are working with the same physics, placing their realms at different points on the same continuum. This section develops that continuum.

Topics: the Equipment Enchantment Duality ([§14.2](#142-the-equipment-enchantment-duality)), the self-augmenting (Type 1) mechanism ([§14.3](#143-type-1-self-augmenting-enchantments)), the intent-channeling (Type 2) mechanism ([§14.4](#144-type-2-intent-channeling-enchantments)), how all crafting is inherently enchantment ([§14.5](#145-all-crafting-is-enchantment)), environmental mana tiers ([§14.6](#146-environmental-mana-as-first-class-state)), personality drift from prolonged exposure ([§14.7](#147-personality-drift-the-mask-becomes-the-face)), immune individuals and raw-fuel mechanics ([§14.8](#148-immunes-and-raw-fuel-redirection)), technology progression as Lexicon expansion ([§14.9](#149-technology-progression-as-lexicon-expansion)), and the tech spectrum from primitive to logos-computational ([§14.10](#1410-the-tech-spectrum)).

## 14.2 The Equipment Enchantment Duality

Per the [Equipment Enchantment Duality design](planning/EQUIPMENT-ENCHANTMENT-DUALITY.md), every enchantment in Arcadia falls into one of two categories that correspond to two different directions of pneuma flow:

- **Type 1: Self-augmenting** — pneuma flows *inward*, reinforcing the item's own physical properties. Sharper edges, harder material, greater durability, resistance to elements. The logos pattern describes what the item should be more of.
- **Type 2: Intent-channeling** — pneuma flows *outward*, through the wielder, via wavelength synchronization. Steadier hands, sharper focus, combat instincts, behavioral nudges. The logos pattern describes what the wielder should do or feel.

Both types work on the same fundamental mechanism: the item is a logos-patterned conductor, environmental mana flows through the pattern, the pattern determines what the flow produces. The only difference is the direction of the output — inward or outward.

### 14.2.1 Why the Duality Matters

This is not merely a taxonomy. It is a **complete model** of how equipment affects gameplay:

- Type 1 enchantments produce measurable stat bonuses on items — this is what players see in Affix systems everywhere.
- Type 2 enchantments produce Status effects on wielders — this is what players feel as "this sword makes me better at fighting."

Most fantasy games conflate these (the flame-sword gives fire damage AND makes the wielder braver, all bundled together). Arcadia separates them mechanically, which enables:

- **Different game-system integration**: Type 1 routes through Item stat computation; Type 2 routes through Status effect application on the wielder.
- **Different failure modes**: Type 1 breaks when the item is destroyed; Type 2 breaks when the wielder is unmatched or enters a mana-dead zone.
- **Different narrative implications**: Type 1 makes items valuable; Type 2 makes items *dangerous*.
- **Different crafting traditions**: Type 1 is "making things better"; Type 2 is "inscribing intent into things."

The duality unifies the seemingly disparate systems of stat-bonus affixes, charms/curses, wielder effects, personality drift, and environmental mana tiers into **one coherent enchantment theory**.

## 14.3 Type 1: Self-Augmenting Enchantments

Type 1 enchantments direct absorbed pneuma inward, reinforcing and enhancing the item's own physical properties.

### 14.3.1 The Mechanism

The crafter inscribes a logos pattern describing a physical property: "this edge resists dulling," "this material resists deformation," "this surface repels water." Environmental pneuma flows through the inscription and manifests the described property. The stronger the flow (richer environment, higher-quality material, deeper inscription), the stronger the manifestation.

### 14.3.2 Mapping to the Affix System

Type 1 enchantments **ARE** the existing Affix system ([§9.7.1](#971-affix)):

| Concept | Affix Implementation |
|---|---|
| Logos inscription depth | Affix `tier` (Tier 1 = deepest inscription, most powerful) |
| Material's natural logos resonance | Implicit affixes via material category (ruby naturally resonates with fire) |
| Deliberate inscription by enchanter | Explicit affixes (prefix/suffix) applied via `ApplyAffix` |
| Pneuma conductivity of the material | Item `quality` (0-30) — higher quality = better conduction = stronger manifestation |
| Competing inscriptions | Mod group exclusivity — two logos patterns of the same domain conflict |
| Inscription permanence | `isFractured` state — the logos pattern has crystallized permanently |
| Inscription instability | `isCorrupted` state — the logos pattern has become chaotic |

**No changes to Affix are required for Type 1 enchantments.** The existing architecture already models this completely. What was previously "affix = item stat modifier" is now "affix = Type 1 enchantment = logos inscription directing pneuma to self-augmenting effect."

### 14.3.3 The Metaphysical Framing Is Compatible

The Affix deep dive's "Logos Inscription Model" describes Affix mechanics in metaphysical terms already — affixes are not arbitrary stat modifications, they are logos patterns that shape how pneuma manifests through the item. This section simply names the category ("Type 1") and contrasts it with what comes next.

## 14.4 Type 2: Intent-Channeling Enchantments

Type 2 is the novel category. These enchantments direct absorbed pneuma *outward* through the wielder, shaped by the crafter's imbued intent.

### 14.4.1 The Mechanism

The crafter inscribes a logos pattern describing a desired effect on a person: "steady the wielder's hands," "sharpen the wielder's focus," "fill the wielder with courage," "make the wielder aggressive and reckless." Environmental pneuma flows through this inscription and, via **wavelength synchronization** with the wielder, enters the wielder's pneuma shell carrying the intent pattern.

The wielder's pneuma shell normally resists external pneuma intrusion (the same surface tension that resists dungeon mana absorption). However, an equipped item in direct physical contact has time to auto-tune to the wielder's pneuma frequency. Once synchronized, intent-channeled pneuma enters without resistance — it appears to the shell as internally-generated pneuma. This is why the influence is **subconscious**: there is no "breach" to detect, no alarm to trigger. The wielder's body treats the external intent as an internal impulse.

### 14.4.2 The Charm/Curse Distinction

The distinction between a charm and a curse is not moral but mechanical:

| Property | Charm | Curse |
|---|---|---|
| **Pneuma flow volume** | Gentle, proportional to the intended benefit | Aggressive, disproportionate to the intended effect |
| **Intent coherence** | Clear, focused, single-purpose | May be layered, conflicting, or deliberately dissonant |
| **Personality drift rate** | Slow (weeks/months of game time for noticeable effect) | Fast (days/weeks for noticeable effect) |
| **Detectability** | Low — most characters feel nothing unusual | High — most characters feel "uneasy" around strongly cursed items |
| **Reversibility** | High — removing the item quickly restores baseline | Lower — residual personality drift persists longer after removal |

A "berserker rage" curse doesn't channel a fundamentally different kind of pneuma than a "courage" charm. It channels *more* of it, *more aggressively*, with *less coherence*. The wielder's pneuma shell is overwhelmed rather than gently guided. This is why curses are detectable — the pneuma throughput is high enough to produce perceptible shell interaction even before equipping.

### 14.4.3 Service Composition for Type 2

Type 2 enchantments compose from existing services with narrow extensions:

| Component | Service | Integration |
|---|---|---|
| Intent inscription | **Affix** (L4) | New slot types `charm` and `curse` with stat grants targeting wielder effects rather than item stats |
| Wielder effect | **Status** (L4) | Equipment-sourced Status templates (`ENCHANT_CHARM_*`, `ENCHANT_CURSE_*`) applied while equipped |
| Environmental scaling | **Environment** (L4) | `pneuma_density` environmental axis determines mana tier at wielder's location |
| Subconscious influence | **Character Personality** (L4) | Axis pressure from prolonged exposure (§14.7) |
| Detection / appraisal | **Seed** (L2) + **Agency** (L4) | Enchanting perception capabilities gate what the wielder or appraiser can detect |
| Crafter intent input | **Craft** (L4) | Crafter's personality traits influence which charm/curse affix definitions are available |
| NPC behavioral awareness | **Actor** (L2) | `${status.has.enchant_charm_*}` variables via Status provider enable NPCs to reason about own enchantment effects |

### 14.4.4 The Affix Extension

The key extension is **routing in equipment stat computation**. Currently all affix stat grants contribute to item stats. Under the duality, stat grants are routed by `slotType`:

- `implicit`, `prefix`, `suffix`, `enchant` → item stat contribution (Type 1, existing behavior).
- `charm`, `curse` → Status effect application on wielder, magnitude scaled by environmental mana tier.

This routing is internal to Affix's stat computation. No API surface changes. The Status application uses the existing `GrantStatusAsync` flow with `sourceId` set to the item instance ID (enabling cascade removal via `RemoveBySourceAsync` on unequip).

### 14.4.5 Wavelength Synchronization

Every entity with a pneuma shell has a **unique pneuma frequency** — a resonant wavelength determined by their logos structure, personality, physical form, and life experiences. This frequency changes slowly as the entity evolves.

When an item is equipped, its pneuma field interacts with the wielder's shell through physical contact. Over a brief attunement period (seconds to minutes of game time), the item's pneuma output auto-tunes to match the wielder's frequency. Once synchronized:

1. **Intent-channeled pneuma enters the shell without resistance** — it registers as self-generated.
2. **The influence is subconscious** — no perceptual cue alerts the wielder to external modification.
3. **The effect is continuous** — as long as the item is equipped and environmental mana is available.
4. **The synchronization is personal** — the same item channels differently through different wielders.

Defensive pneuma techniques (mana shields, anti-magic wards, divine protections) work by reinforcing the shell against *foreign-frequency* pneuma. They detect and resist pneuma that doesn't match the entity's frequency. Wavelength-synchronized equipment pneuma is, by definition, at the entity's own frequency — the shield doesn't resist it for the same reason an immune system doesn't attack the body's own cells.

This has implications:

- **Anti-magic zones** (which suppress all active pneuma) DO suppress intent channeling.
- **Dispel effects** CAN disrupt intent channeling if the dispeller is skilled enough to identify the equipment's contribution.
- **Pneuma sensitivity training** (high enchanting Seed capability) can learn to distinguish self-generated from synchronized pneuma — this is how detection and appraisal work.

## 14.5 All Crafting Is Enchantment

The most radical implication of the duality: **in a mana-rich environment, all crafting is inherently an act of enchantment**, whether the crafter intends it or not.

### 14.5.1 The Unconscious Mechanism

When a blacksmith hammers steel, they aren't just shaping metal — they are coaxing the steel's logos into a new arrangement while their own intent flows through the pneuma-conductive material. When a baker kneads dough with love, the dough carries trace emotional resonance. When a weaponsmith visualizes deadly precision while forging a blade, that intent inscribes into the weapon's logos structure.

This is not a separate "enchantment step" applied after crafting. It is an **inherent property** of working with materials in a mana-rich world. The distinction between "mundane crafting" and "enchantment" is one of **awareness and control**, not of kind.

VISION.md already establishes that "Mana interferes with electrical potential, making traditional electronics impossible. All technology uses sequential magical transformations via enchantment." The duality completes this picture: **all Arcadian technology — from lanterns to weapons to cooking pots — is logos-programmed pneuma circuitry**. The "enchantment" is the logos pattern. The "fuel" is environmental pneuma. The distinction between a lantern ("produce light" logos drawing ambient mana) and a combat weapon's sharpness enchantment ("maintain edge integrity" logos drawing ambient mana) is one of application, not mechanism.

### 14.5.2 The Awareness Spectrum

Crafters operate at different levels of intent-control:

| Crafter Level | Intent Mechanism | Magnitude | Control |
|---|---|---|---|
| **Novice** | Purely unconscious. Emotional state leaks into the work unpredictably. | Trace — barely detectable by master appraisers. A sword forged on a bad day carries trace frustration. | None. |
| **Journeyman** | Dimly aware. Cultural traditions ("crafting with clear mind," "blessing the materials") are empirically-discovered intent hygiene. | Noticeable — an experienced wielder might feel the difference between two otherwise-identical swords. | Negative only. Can learn to *suppress* accidental intent but not direct it. |
| **Master** | Fully aware. Understands the mechanism. Can deliberately hold intent while crafting. | Strong — deliberate mana-hammering produces clear, coherent intent patterns. | Full directional control. Chooses what intent to inscribe. Can produce both charms and curses deliberately. |
| **Grandmaster** | Transcendent. Every hammer strike is simultaneously physical forming and logos inscription. Their entire being IS the intent. | Powerful — items identifiable by their intent signature alone. | Nuanced. Can layer compatible intents. Can inscribe intents they don't personally embody by "performing" them. |

### 14.5.3 The Vonnegut Warning

A grandmaster crafter who "performs" cruelty-intent to forge a cursed blade **risks becoming cruel themselves**. The act of deliberately channeling an intent shapes the channeler's own logos patterns. This is Vonnegut's *Mother Night* principle mechanized: "we are what we pretend to be, so we must be careful about what we pretend to be."

A crafter who specializes in curse-forging gradually drifts toward the intents they inscribe, regardless of their original personality. This creates a natural narrative archetype: the **cursed forgemaster** whose personality was consumed by their own craft. It is not authored; it emerges from the personality-drift mechanism (§14.7).

### 14.5.4 Cultural Implications

Primitive cultures that treat crafting as sacred are **empirically correct without understanding the mechanism**:

- "Never forge in anger" — anger-intent leaks into the work.
- "Sing while you bake" — focused positive intent produces better results.
- "Bless the materials before working" — deliberate intent hygiene, clearing residual patterns.
- "The ore from the cursed mine carries grudges" — memento pneuma from the mine's history (§13.7) leaks into materials extracted from that location.

Scientists (logos theorists) understand *why* these practices work. Artisan traditions discovered them empirically. Both arrive at the same practices from different directions. This is civilizational knowledge converging on truth from two paths.

### 14.5.5 The Resentful Baker

Consider an NPC baker with high `${personality.resentment}` who bakes bread daily. Every loaf carries trace dissatisfaction-intent. No individual loaf has a detectable effect. But over months, the cumulative exposure of an entire village eating that bread creates a subtle statistical anomaly: `${analytics.settlement.mood_trend}` declines without obvious cause. A regional watcher god perceives the anomaly and investigates.

**The content flywheel turns from bread.** This is emergent social dynamics driven by the incidental-enchanting mechanism — not scripted, not authored, not designer-intended. It is what happens when the substrate is coherent and the mechanism is followed through.

### 14.5.6 Workshop and Automated Production

Items produced via Workshop's lazy evaluation (automated, time-based production without a crafter actor) carry **no intent enchantment**. There is no crafter personality to imprint. Workshop blueprints produce "sterile" items — functional but spiritually inert.

This creates a natural economic distinction: **hand-crafted items have character** (for better or worse); **mass-produced items are reliable but bland**. NPC merchants who can appraise intent quality price accordingly. A hand-forged sword from a master smith sells for far more than a Workshop-produced identical-spec sword because the Workshop sword carries no intent resonance.

This provides an emergent economic tension that most games have to hand-design: artisan economies remain vital alongside mass production, not because designers enforced it but because the substrate distinguishes them.

## 14.6 Environmental Mana as First-Class State

Environmental mana density is not a flavor variable — it is **gameplay-critical state**. Per [§2.3.4](#234-environmental-mana-as-first-class-state), Arcadia models mana density as discrete tiers producing distinct Status effects.

### 14.6.1 The Four Tiers

| Tier | Environment | Effect on Type 1 | Effect on Type 2 |
|---|---|---|---|
| **Dead** (0%) | Anti-magic zones, mana-drained regions | Self-augmenting affixes cease functioning. Items revert to base material properties. | Intent channeling stops. **Withdrawal debuffs** if wielder has accumulated drift. |
| **Thin** (50%) | Depleted regions, low-mana areas | Reduced effectiveness. | Inconsistent — the effect *stutters*. Specific debuffs based on intent type (assassin-focus charm at 50% makes wielder jittery rather than calm). Worse than no charm because inconsistency is actively disruptive. |
| **Normal** (100%) | Standard mana density | Full effect as inscribed. | Full intent channeling as designed. |
| **Rich** (200%) | Leyline convergence, sacred sites | Enhanced. Bonus properties manifest that are dormant at normal levels. | Amplified. Effects beyond inscription's design parameters manifest (assassin-focus at 200% creates genuine temporal perception shift). |

### 14.6.2 The Thin-Zone Inversion

At 50% mana density, Type 2 enchantments don't produce "half the benefit." They produce **distortion**. A partially-powered intent pattern is like a poorly-tuned radio — static and noise, not a quiet clear signal. This creates real gameplay incentive: either commit to mana-rich zones where gear works, or strip charmed equipment before entering thin zones.

### 14.6.3 Withdrawal in Dead Zones

A character who has worn strong intent-channeling equipment for extended periods and enters a mana-dead zone experiences **withdrawal**. The subconscious boosts disappear instantly, but the personality drift they've accumulated remains. The character feels "wrong" — their reflexes don't match their muscle memory, their confidence is shaken, their instincts misfire. Modeled as `ENCHANT_MANA_STARVED` Status debuff scaling with accumulated drift magnitude.

This gives anti-magic zones genuine narrative weight. They're not just "magic doesn't work here" — they're places where enchanted characters become visibly *changed* by being suddenly denied the influences they've grown dependent on.

### 14.6.4 The Geography of Magic

Mana tiers produce **magical geography** — places where magic abounds, places where it struggles, places where it's silent. Travel routes, city locations, war campaigns, and economic centers all revolve around where active pneuma is abundant versus scarce. Leyline maps are strategic resources. Anti-magic zones are natural fortresses. Sacred sites concentrate at mana-rich points not coincidentally but because mana-rich points *enable* sanctity.

Mana geography is to Arcadia what petroleum geography is to our industrial civilization — a strategic resource that shapes everything.

## 14.7 Personality Drift: The Mask Becomes the Face

Intent enchantments operate on two timescales:

### 14.7.1 Active Influence (Immediate, Fully Reversible)

While wearing the equipment, the character receives Status effects that modify behavior — steady hands, sharper focus, combat instincts, courage. Remove the equipment, effects end immediately. Fully reversible.

### 14.7.2 Drift Influence (Slow, Semi-Permanent)

Over extended periods, the character's **actual personality axes shift** via Character Personality's experience-driven trait evolution. This is Vonnegut's mechanism made mechanical — the mask becomes the face. The equipment makes you perform a role; over time, you become that role.

**Drift parameters**:

- **Rate**: Slow — weeks to months of continuous wear for noticeable effect. Trace incidental enchantment barely drifts. Master-crafted deliberate enchantments drift faster. Curses drift fastest.
- **Proportionality**: Magnitude proportional to pneuma flow volume. Trace novice intent produces negligible drift. Grandmaster-forged combat charm produces measurable drift.
- **Partial reversibility**: Removing equipment stops drift. Over weeks to months, axes drift back toward natural state — but not all the way. A **residue** remains, proportional to how long equipment was worn and how strong the intent was.
- **Game-time operation**: Drift operates on game time. If a player logs off, the world continues; the character's NPC brain determines whether to continue wearing the equipment.

### 14.7.3 The Equipment Dependency Trap

A character whose personality has been significantly shaped by intent enchantments becomes **dependent** on them in a narratively meaningful way. Their habits, relationships, and self-image were built on the enchanted personality. Removing the gear doesn't undo the life they lived while wearing it.

Character Encounter records reflect interactions with the "enchanted self." Disposition drives were shaped by equipment influence. The character who wore the courage charm for three years and built a reputation as a fearless warrior faces an identity crisis when the charm breaks.

This produces rich narrative content. A storyline about "finding yourself" can emerge organically when drift accumulates enough that the wielder is measurably different from their starting self. A character can grieve the loss of a magical item not just for its utility but because they were a different person while wearing it.

### 14.7.4 Detection and Appraisal

Not all characters perceive intent enchantments equally. Detection depth scales with Seed capabilities:

| Level | What's Detected |
|---|---|
| Passive (anyone) | Vague unease from high-magnitude curses. "Something feels off about this item." |
| Basic (enchant.perception.basic) | "This has an enchantment" vs. "This doesn't." No type or strength. |
| Intermediate | Type 1 vs. Type 2 distinction. Intent category (combat, craft, social). Magnitude tier. |
| Expert | Specific intent patterns ("assassin's focus"). Crafter's personality fingerprint. Detects layers and conflicts. |
| Master | Full enchantment history. Crafter identification by logos signature alone. Dormant/suppressed enchantments visible. Item's "mood" interacting with current wielder. |

Player-side UX scales with Agency manifest:

| Spirit Experience | What Client Renders |
|---|---|
| New spirit | Item name and basic stats only |
| Some enchanting exposure | "Hidden potential" hint |
| Moderate | Affix names and stat totals, activation bars without details |
| Significant | Type 1 vs. Type 2 distinction. Intent categories. Charm/curse indicators. |
| Deep | Full fidelity, specific prerequisites, memory stories, crafter fingerprints. |

## 14.8 Immunes and Raw-Fuel Redirection

Rare individuals exist who are functionally **immune** to Type 2 intent-channeling effects. Their pneuma shells either don't resonate with equipment wavelengths, have been trained to resist synchronization, or have a unique pneuma structure that absorbs incoming mana without accepting the logos-intent pattern.

### 14.8.1 The Raw Fuel Mechanism

The most interesting variant: the immune individual's pneuma shell **absorbs** channeled pneuma but **strips the logos-intent pattern**. They receive the raw energy without the directed influence. This means:

- All Type 2 "charm/curse" enchantment mana pools become available as **raw pneuma fuel**.
- The individual can direct this accumulated pneuma freely — boosting any ability they choose.
- They can release it all at once in a burst (using all equipment charges simultaneously for a massive attack).
- The "recharge" time is how long it takes equipment to re-absorb environmental mana.
- A heavily cursed sword that drives normal wielders mad is **more valuable** to an immune individual — the curse channels more mana, which means more fuel.

This is a wonderful inversion. Cursed equipment that is dangerous to normal wielders becomes powerful for immunes. A master enchanter who is secretly an immune character can deliberately craft their own cursed weapons to be their own power source — a character archetype that couldn't exist without this mechanic.

### 14.8.2 Sources of Immunity

| Source | Service | Mechanism |
|---|---|---|
| Species trait | Ethology (L4) | Some species naturally immune — pneuma shells don't synchronize. Demons and angels typically unaffected as species-level archetype. |
| Genetic mutation | Character Lifecycle (L4) | Rare hereditary trait — `${heritage.pneuma_resistance}` above threshold prevents synchronization. Can appear spontaneously or inherit. |
| Divine gift | Status (L4) + Divine (L4) | A god of clarity or truth grants a blessing that reinforces the shell. Temporary or permanent depending on divine investment. |
| Trained ability | Seed (L2) | Specific growth path in an enchanting or meditation Seed domain. Monks, mental discipline practitioners, enchanting masters. |
| Artifact | Item (L2) + Affix (L4) | An item specifically designed to disrupt synchronization — a "mental clarity" amulet that adds noise to the shell's frequency. |

### 14.8.3 GOAP Implications for Immunes

An immune NPC has a fundamentally different equipment evaluation function. Normal NPCs evaluate items by: "Does this charm match my combat style?" Immune NPCs evaluate by: "How much raw mana does this equipment generate regardless of intent type?"

This falls naturally from GOAP action-cost evaluation — the `EstimateItemValue` endpoint in Affix produces completely different results when the evaluator is immune, because the "wielder effect" stat grants become "raw fuel potential" instead. **No special code needed** — the immune individual's equipment evaluation ABML behavior simply references different variables.

## 14.9 Technology Progression as Lexicon Expansion

Per [§2.9.1](#291-technology-is-lexicon-expansion), every technology in Arcadia is ultimately a **Lexicon configuration**. This section makes that claim concrete.

### 14.9.1 What "Technology" Means Mechanically

A technology in Arcadia is a **combination of logos primitives** that produces a desired effect. "Fire-starting technology" is some arrangement of `[HEAT] + [IGNITION] + [FUEL] + [ENERGY_TRANSFER]` — the specific arrangement depends on whether the technology is flint-and-steel, lens-concentration, or enchanted ignition rod. All three produce fire; all three use the same primitive logos in different configurations.

Similarly for every other technology:

- "Grain-grinding technology" — `[FORCE] + [GRANULAR_MATTER] + [REDUCTION] + [KINETIC_TRANSFER]` — millstone, hand-quern, animal-powered mill, water-wheel mill, enchanted auto-mill.
- "Communication technology" — `[INFORMATION_ENCODING] + [MEDIUM] + [DECODING]` — speech, writing, sending-stones, enchanted telegraph.
- "Transport technology" — `[MOTIVE_FORCE] + [CARRIER] + [PATH]` — walking, animal-drawn, cart-and-animal, sailing, enchanted-rail, bound-elemental.

Each of these represents a combinatorial space of possible realizations. The specific realization depends on which primitive logos the civilization has discovered and which combinations it has validated as productive.

### 14.9.2 Progress as Vocabulary Growth

Civilizational progress is the growth of the canonical Lexicon — both the accumulation of new primitive logos (paradigm shifts — §2.7.2) and the discovery of new valid composite patterns (normal science — §2.7.1).

A realm with a richer Lexicon has more technologies available. A realm with a narrower Lexicon has fewer. A realm that has *lost* Lexicon entries through Hearsay-distortion-induced collapse has *regressed* — specific logos have coarsened to general categories, and specific technologies have become folk remembered but no-longer-executable.

This is different from traditional "tech tree" design, which is hierarchical and deterministic (research prerequisites unlock future research). Arcadian tech progression is:

- **Combinatorial** — any valid combination of known primitives is a potential technology.
- **Lossy** — progress is not guaranteed; entropy pushes back through Hearsay distortion.
- **Institution-dependent** — strong institutions preserve Lexicon fidelity; weak institutions let it decay.
- **God-influenced** — regional watcher gods can curate new primitives into canonical Lexicon, accelerating progress.
- **Emergent** — discoveries can happen anywhere characters with relevant skills experiment with novel combinations.

### 14.9.3 The Lexicon-as-Tech-Tree Thesis

Stated plainly: **a realm's technology level is literally the expanse of its canonical Lexicon**. Not the number of artifacts it has; not the scale of its cities; not the complexity of its governance. The measurable property is **how many primitive logos are catalogued, how precisely they're distinguished (high-resolution vs. Hearsay-desaturated), and how many valid combinations have been discovered**.

A FANTASIA realm with 5,000 canonical Lexicon entries is pre-industrial. One with 20,000 is capable of enchantment-industrial civilization. One with 100,000 has moved into logos-computational territory.

This is precisely what makes "same platform, many realms" work. Different realms sit at different Lexicon points. Their gameplay differs not because they run on different code, but because they run on different canonical vocabularies.

## 14.10 The Tech Spectrum

The combination of the Equipment Enchantment Duality, Lexicon expansion, and the thermodynamic metaphysics produces a **continuous technology spectrum** spanning the full range of human-civilizational development — but implemented on a unified substrate.

### 14.10.1 The Spectrum

| Phase | Characteristic | Enchantment Sophistication | Lexicon Scale |
|---|---|---|---|
| **Pre-Bronze-Age survival** | Stone tools, fire-making, basic textiles. Magic indistinguishable from religion (ritual, not technique). | Trace incidental only. Any "enchantment" is shamanic ceremony reading naturally-occurring resonances. | Small (hundreds of primitives). |
| **Agricultural / Classical** | Iron tools, city-states, temple magic. Magic becomes formalized; first wizard schools. | Deliberate but simple enchantments — sharpness, durability, minor protections. Guild apprenticeships begin. | Moderate (thousands of primitives). |
| **Medieval** | Feudal societies, mature enchantment guilds, universities. Cathedrals of mana. | Rich Type 1 vocabularies; early Type 2 (charms for travelers, weapons for knights). Masters train journeymen. | Large (tens of thousands). |
| **Renaissance / Proto-scientific** | Printing-press analogs (enchanted tomes, scroll duplication). Early experimental magic. Alchemy becomes theory. | Type 2 emerges explicitly. Intent-channeling weapons become signature aristocratic gear. Logos theorists document the mechanism. | Large (tens to low-hundreds of thousands). |
| **Enchantment-industrial** | Mass-production enchantment. Trains on enchanted rails. Telegraphy via enchantment. Factories of golem-workers. | Industrial-scale Type 1 + Type 2. Enchantment becomes corporate. Guilds evolve into corporations. | Very large (hundreds of thousands). |
| **Post-industrial / Logos-computational** | Networked enchantment. Pneuma-routed information. Symbolic substrates rival-to-exceed our electronic computing. | Seamless Type 1 + Type 2 integration. Smart equipment with partial awareness. Enchantment as everyday utility. | Vast (millions of primitives + combinations). |

### 14.10.2 Why Electronics Is Impossible

VISION.md's canonical claim: mana interferes with electrical potential, making traditional electronics impossible. This is not arbitrary. In a mana-rich environment, any electrical circuit carries trace pneuma current alongside electron flow. The pneuma's behavior is governed by logos patterns on and near the circuit — which means circuits are *always* enchantments, always Type-1-or-Type-2. The categorical separation between "electronic" and "magical" that our technology depends on does not exist in Arcadia.

The result: Arcadian civilizations cannot develop our electronic computing. But they can develop **logos-based computing** — symbolic-substrate computation that uses pneuma routing instead of electron routing. This is functionally similar but categorically different. It happens through enchanted crystals, glyph-inscribed substrates, pneuma-conductor networks — and it scales to late-industrial levels of capability, just expressed differently.

### 14.10.3 Living Weapons as Late-Spectrum Confluence

Sentient weapons (§13.3) are where the full spectrum converges. A Legendary-phase living weapon is:

- **A material object** (Type 1 enchantments maintain its physical form).
- **An intent-channeling device** (Type 2 enchantments influence its wielder).
- **An autonomous agent** (Character in SENTIENT_ARMS with personality and cognition).
- **A historical artifact** (CharacterHistory spans centuries, feeds content flywheel).
- **A logos-computational entity** (Genesis runtime, ABML execution, cognitive stack).

All of these on the same substrate. A civilization that has produced Legendary living weapons has canonical Lexicon vocabulary for everything needed — persistence of pneuma through material transformation, wavelength synchronization across generations of wielders, continuity of identity despite physical rebinding, archival memory persistence.

Living weapons are the tech spectrum's peak expression: every lower capability is present and integrated.

### 14.10.4 What Arcadian Civilizations Never Develop

The tech spectrum is not our spectrum. Some things Arcadians don't (and can't) develop:

- **Our electronic computing** — mana interference prevents. Replaced by logos-computational equivalents with different characteristics.
- **Our chemical industry** — alchemy/enchantment dominates; formal chemistry emerges as a subset of logos theory, not as our separate discipline.
- **Our fossil-fuel economy** — energy comes from ambient pneuma and specific enchantments, not from burning hydrocarbons.
- **Our communication infrastructure** — Internet analogs exist via pneuma networks, not via copper/fiber/radio.

Some things Arcadians develop that we don't:

- **Ambient mana economies** — regions with rich pneuma enable technologies impossible elsewhere.
- **Personality-integrated equipment** — intent-channeling is a universal tool category with no real-world parallel.
- **Legendary artifacts as persistent agents** — our most valuable artifacts are inert; Arcadia's are living.
- **Cultural-memory anti-entropy as engineering discipline** — institutions of Lexicon preservation are explicit infrastructure, not implicit social organization.

This gives realms authentic-feeling variation. A FANTASIA realm stuck at classical-era tech looks recognizable to us (swords, stone walls, temple magic). An ARCADIA realm at enchantment-industrial looks like an alternate-history Victorian era. An OMEGA realm with logos-computational substrate looks like cyberpunk but operating on a magical substrate. All three are valid points on a continuous spectrum.

## 14.11 The Unified Theory

Stepping back, the Equipment Enchantment Duality + all-crafting-is-enchantment + environmental-mana-tiers + personality-drift + Lexicon-progression form a single **Unified Theory of Arcadian Technology**:

1. **All technology is enchantment** — logos-patterned pneuma circuitry.
2. **Enchantment has two directions** — inward (Type 1, self-augmenting) and outward (Type 2, intent-channeling).
3. **Crafters inscribe intent whether aware or not** — magnitude scales with awareness and control.
4. **Environmental mana is first-class state** — discrete tiers determine effectiveness.
5. **Prolonged exposure drifts the wielder** — the mask becomes the face.
6. **Technology progresses through Lexicon expansion** — more primitives + more valid combinations.
7. **Civilization ages produce tech-tier transitions** — not by single breakthroughs but by cumulative Lexicon growth.

This theory is internally consistent across the full tech spectrum. A primitive-era blacksmith forging a flint knife and a late-era enchantment-engineer designing a pneuma-routing substrate are doing the **same thing** at different scales of awareness, sophistication, and Lexicon access. This is the architectural payoff of §2's metaphysical substrate: a civilization's entire technological history is one continuous phenomenon, differing only in parameters.

## 14.12 Connecting to the Rest of the Document

This section ties the metaphysical foundation (§2) to realm design (§§15-18) and to several other sections:

- [§2](#2-the-metaphysical-substrate--the-knowledge-as-power-thesis) — metaphysical foundation that this section operationalizes.
- [§7](#7-the-content-flywheel) — the Crafter Archive Amplifier; crafter archives carry intent signatures that future items inherit.
- [§9](#9-the-economic-substrate) — Affix system provides Type 1 mechanics; economy distinguishes hand-crafted from mass-produced.
- [§10](#10-the-social-fabric--cultural-emergence) — cultural taboos around crafting (no forging in anger) are empirically-discovered truths about the mechanism.
- [§13](#13-actor-bound-entities-living-places-and-things) — living weapons integrate fully with both Type 1 and Type 2 enchantments; they become aware of and may manipulate their own enchantments.
- [§§15-18 (Realm Design)](#part-iv--realm-design-guides) — each realm's tech level is a configuration point on the spectrum developed here.

The Unified Theory of Arcadian Technology is what makes the realm design guides meaningful. When §15 describes a "pre-Bronze-Age FANTASIA" or §17 describes a "logos-computational OMEGA," both are configurations on the same spectrum, using the same primitives, running the same substrate. The theory makes realm variation a matter of **which parameters to set**, not **which systems to build differently**. That is the architectural payoff we have been building toward since §1.

---

# PART IV — REALM DESIGN GUIDES

# 15. Designing FANTASIA Realms

> **Status**: Scope & context document (to be written in a future session).
> **Prerequisite reading**: [§2 Metaphysical Substrate](#2-the-metaphysical-substrate--the-knowledge-as-power-thesis), [§3 Realm Archetype System](#3-the-realm-archetype-system), [§10 Social Fabric](#10-the-social-fabric--cultural-emergence), [§13 Actor-Bound Entities](#13-actor-bound-entities-living-places-and-things), [§14 Unified Theory of Arcadian Technology](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology).
> **Estimated length**: 10,000–12,000 words. This is the most substantial realm design guide because FANTASIA covers the broadest variation space.

## Conversation Context to Preserve

This section is **the payoff of the Big Brain Mode discussion that seeded this entire document.** The framework for analyzing how magic influences technological development must be distilled here as a design tool. The conversation's key claims that must be preserved:

- **The Observability Effect** (magic makes physics *more* visible, not less) vs. **the Substitution Effect** (magic fills needs that otherwise drive tech development). These are two opposing forces; net outcome depends on the three axes.
- **Sanderson's Three Laws of Magic** as implicit framework (hard-magic systems, limits matter more than powers, expand before adding new). Cite originals from his *Mistborn* / *Stormlight* postscripts.
- **The Realist Hero principle**: technology develops when *needed*. If magic fills the need, tech doesn't develop. If magic restricts to elites, non-magical majority still has every incentive to develop tech.
- **The scientific method plausibly emerges *earlier* in Arcadia than in our timeline** — because mechanism is visible from day one; no teleological blockers to dismantle; master-apprentice relationships are proto-peer-review.
- **Civilizational collapse looks like Lexicon regression**, not erasure. Specific concepts coarsen; fine distinctions disappear; whole craft domains reduce to parent categories.
- **Technology is Lexicon expansion** — not a technology tree with research prerequisites, but combinatorial discovery of new primitives and valid combinations.

Media precedents the user explicitly named as influences — these should be cited and their specific design lessons extracted:

- **Brandon Sanderson — Mistborn (Era 1 & 2), Stormlight Archive**: Hard magic, limitations as interest, Era 2 as "magic + industrial tech coexistence" case study.
- **A Realist Hero Has Rebuilt the Kingdom**: Tech develops from need, not from research-for-progress's-sake.
- **Fushi no Kami**: Gradual reintroduction of archived knowledge in a regressed world.
- **Release That Witch**: Future-knowledge shortcut (not natural progression), but validates "tech-at-scale beats magic" where domains don't overlap.
- **Dr. Stone**: Contrast case (world without magic, tech from first principles) — shows how labor-intensive science is when you have nothing.
- **Ascendance of a Bookworm**: Class-differentiated customs emerging from material conditions (ties to §10.11).

User's research library is in `~/repos/hearthworks/` — specifically `01-genre-study/` with main-subjects (fushi-no-kami, release-that-witch) and minor-subjects (realist-hero, dr-stone, quiet-blacksmith, cooking-wild-game, isekai-farming-life, slime-tensei, dukes-daughter, enough-slow-life, grace-of-the-gods). Synthesis docs at `01-genre-study/synthesis/` (CHARACTER-AS-CAPABILITY, FAILURE-PATTERNS, NARRATIVE-ENGINES, PRODUCTION-PATTERNS). Full-reads of these are expected when writing this section.

## Content Outline

### 15.1 What Defines a FANTASIA Realm

- Fantasy aesthetic with magic-dominant tech spectrum.
- Active divine agents (PANTHEON interferes visibly).
- Technology through enchantment (not electronics — §14.10.2).
- Thermodynamically compliant magic (§2.3).
- Broad variation permitted — FANTASIA covers pre-industrial through enchantment-industrial.
- Unifying signature: worlds where "magic works and the gods are watching."

### 15.2 The Magic/Tech Analysis Applied as Design Framework

Distill the Big Brain Mode conversation into a reusable design procedure. Walk through each major claim with examples:

- Under what conditions does a FANTASIA realm stagnate vs. progress technologically?
- When does magic *accelerate* scientific emergence vs. *delay* it?
- How does mage-distribution (universal / common / rare / single-lineage) determine whether the non-magical majority develops technology?
- How does institutional strength (academies, guilds, printing) preserve Lexicon fidelity across generations?
- When does the Industrial Revolution equivalent happen? Answer: when population pressure + labor surplus + magic hitting capacity ceilings combine. This happens in enchantment-industrial ARCADIA realms but may not happen in FANTASIA realms at all.

### 15.3 The Twelve Design Axes (from §3.5) Applied to FANTASIA

For each axis, show what the axis controls and what each value produces in a FANTASIA context:

1. Magic Distribution (universal / common-graded / rare-elite / single-lineage)
2. Magic Cost Transparency (obvious-external / obvious-internal / mixed / obscured)
3. Domain Coverage (narrow-deep / broad-shallow / broad-deep / narrow-shallow)
4. Pneuma-Electronics Interference Strength
5. God-Set Composition (pantheon size, domain coverage, personality distribution, presence intensity)
6. Species Catalog (which species exist; Ethology archetypes)
7. Resource Profile (materials abundant/scarce; specific magical materials)
8. Technology Tier Defaults (pre-agricultural / classical / medieval / Renaissance / enchantment-industrial)
9. Worldstate Clock Rate (`timeRatio` — 60×, 120×, 240× — determines generational pacing)
10. UX Manifest Emphasis (combat / crafting / social / exploration / magic)
11. Anti-Entropy Institution Strength (fragile / guilded / literate / industrialized)
12. Population Density & Social Structure (tribal / city-state / feudal / imperial)

### 15.4 God-Set Composition Is the Primary Flavor Lever

Reference §10.11.5 (same material conditions under different gods produce different cultural expressions). Develop:

- Pantheon archetypes: Greek (jealous), Norse (warlike), Celtic (nature-focused), Shinto (countless kami), Zoroastrian (dualistic), Aztec (sacrificial), etc.
- Domain balance: war-heavy = militaristic texture; nature-heavy = pastoral; trickster-heavy = chaotic.
- Divine personality distribution: benevolent-dominant / capricious-dominant / malevolent-dominant / balanced.
- Examples of god-set combinations producing distinct realm experiences.

### 15.5 Four Worked Examples (Designed From First Principles)

User directive from conversation: **design-from-first-principles (Option A)**, not use-existing-canonical-realms. Each worked example should be ~1,500-2,000 words with concrete mechanical choices across all twelve axes.

**Example 1: Pre-Bronze-Age Survival FANTASIA** — Magic universal but weak (animistic ritual); cost high; domain coverage broad-shallow; shamanistic god-set; stone tools; oral tradition; fragile anti-entropy; `timeRatio: 240`; survival-heavy UX. Reference Dr. Stone for the baseline-science perspective.

**Example 2: Wuxia / Cultivation FANTASIA** — Magic rare-elite with deep domain coverage; internal cost (qi/jing/shen); cultivation as primary gameplay; Daoist/Buddhist god-set (mountain gods, immortal sages, celestial bureaucracy); iron tools, city-states, formalized magical schools (sects); guilded anti-entropy; `timeRatio: 120`; martial-arts-heavy + social-hierarchy-heavy UX.

**Example 3: Steampunk-Enchantment FANTASIA** — Magic common-graded; cost mixed; broad-deep domain coverage; industrial-age pantheon (guild gods, engineering gods, progress gods); mass-production via Workshop enchantment; enchanted trains, sending-stone telegraph; industrialized anti-entropy (printing-press equivalents, enchanted libraries, universities); `timeRatio: 60`; craft-heavy + economic-heavy + political-heavy UX. Reference *Release That Witch* for specific mechanics.

**Example 4: Dark Gothic / Necromancy FANTASIA** — Magic rare-elite; death-domain dominant; cost high with moral weight; waning-benevolent + rising-death god-set; medieval tech with failing institutions and ruins of greater civilizations; fragile-regressing anti-entropy (Lexicon collapse, knowledge dying with each master); `timeRatio: 180`; investigation + combat + mystery UX; heavy MEMENTO-INVENTORIES, spirit dens, UNDERWORLD integration.

### 15.6 Cross-Reference: Extensions (§19) for Genre-Specific Vocabulary

FANTASIA realms often need L5 Extension plugins to translate domain vocabulary:

- Fantasy-RPG Extension surfacing Seed growth as "character levels."
- Cultivation Extension exposing seed bonds as "dao-bonds."
- Survival Extension for weather/hunger/thirst as explicit drives.

### 15.7 Mana Geography Considerations

Per §14.6.4, mana tiers produce magical geography. For FANTASIA realms:

- Mana-rich sacred sites (sanctuaries, leyline convergences, temple cities).
- Mana-thin frontier regions (unreliable magic).
- Mana-dead zones (anti-magic ruins, scientific anomalies, industrial safe-havens).
- Strategic/economic implications of mana geography.

### 15.8 Content Flywheel Tuning Per Sub-Archetype

Different FANTASIA realms tune §7's content flywheel differently:

- Generational-focused realms → Character Lifecycle, Realm History, dynastic play emphasis.
- Exploration-focused realms → Cryptic Trails, spirit dens, sanctuary discovery.
- Combat-focused realms → cinematic choreography, logos resonance items.
- Social-focused realms → Faction politics, Organization dynamics, cultural emergence.

## Research Sources for the Writing Agent

Required reads before writing:

- `docs/planning/PREDATOR-ECOLOGY-PATTERNS.md` — ecological rules underpinning wilderness.
- `docs/planning/SANCTUARIES-AND-SPIRIT-DENS.md` — sacred geography.
- `docs/planning/CULTURAL-EMERGENCE.md` — how divine curation produces flavor.
- `docs/planning/EQUIPMENT-ENCHANTMENT-DUALITY.md` — technology mechanics in detail.
- `docs/plugins/DIVINE.md` and `docs/maps/DIVINE.md` — god mechanics.
- `~/repos/hearthworks/01-genre-study/` — user's extensive research library. Full-reads expected.
- External: Brandon Sanderson's Three Laws essays (publicly available on his blog and in *Mistborn* / *Stormlight* postscripts).

## Writing Guidance

- Tone: VISION.md voice for framework sections; BANNOU-DESIGN.md voice for worked examples (concrete, table-heavy).
- Each worked example must specify *all twelve* axis values so it's genuinely actionable as a design template.
- Reference cross-sections aggressively — FANTASIA is where most of Parts II-III crystallize.
- Preserve "FANTASIA is a role keyword" framing throughout — never treat any single example as canonical.
- The Big Brain analysis framework should feel like *design leverage*, not a recap. Readers should come away able to analyze their own FANTASIA design using these tools.

## What This Section Must NOT Do

- Must not prescribe "the" FANTASIA design. Many valid FANTASIA realms exist.
- Must not duplicate content from §§2, 3, 14 — those established the framework; this section applies it.
- Must not be purely abstract. Worked examples are essential for concreteness.
- Must not over-claim authorial voice on the Big Brain analysis — the analysis emerged from conversation with the user; frame it as "the design framework derived from analysis of fantasy media precedents."

---

# 16. Designing ARCADIA Realms

> **Status**: Scope & context document.
> **Prerequisite reading**: [§3 Realm Archetype System](#3-the-realm-archetype-system), [§9 Economic Substrate](#9-the-economic-substrate), [§14 Unified Theory of Arcadian Technology](#14-equipment-enchantment--the-unified-theory-of-arcadian-technology), [§15 Designing FANTASIA Realms](#15-designing-fantasia-realms).
> **Estimated length**: 7,000–9,000 words.

## Conversation Context to Preserve

The ARCADIA archetype is distinct from FANTASIA in tech tier but shares the same substrate. Key points established in earlier discussion:

- **ARCADIA = "contemporary / industrial builder" archetype.** Player experiences modern-feeling life *without electronics*, implemented through enchantment-industrial substrate.
- **Every technology that we associate with the Industrial Revolution exists — but implemented via enchantment, not electricity.** Trains run on enchanted rails or bound-elemental propulsion. Telegraphy uses sending-stone networks. Factories run on golem-workers and mass-enchantment Workshops.
- **Functional equivalent of IR happens, just later and at lower peak throughput**, because magical labor hits capacity ceilings ours didn't, AND with a vastly richer *control layer* than our early electronics (enchantment is a better control substrate than vacuum tubes).
- **Electronics is impossible** — mana interferes with electrical potential (VISION.md canonical). Any electrical circuit carries trace pneuma current; the pneuma's behavior is governed by logos patterns nearby; circuits are always enchantments.
- **Logos-computational substrate** is the late-ARCADIA or early-OMEGA peak: symbolic-substrate computation via pneuma routing instead of electron routing. Enchanted crystals, glyph-inscribed substrates, pneuma-conductor networks. Functionally similar to our computing; categorically different.
- ***Release That Witch* is the primary reference point** for mechanics (though it's future-knowledge-cheating, its infrastructure design is instructive).

## Content Outline

### 16.1 What Defines an ARCADIA Realm

- Contemporary / industrial / urban / political-economic emphasis.
- Enchantment-industrial substrate (trains, factories, telegraphy, mass production — all via enchantment).
- Guild structures, corporate forms, modern-feeling political economies.
- Magic is common-graded or rare-elite; domain coverage is broad; cost transparency is high.
- Industrialized anti-entropy institutions (printing, universities, professional societies).
- The unifying signature: worlds that feel *modern* without feeling electronic.

### 16.2 The Enchantment-Industrial Paradigm

Detail what gets built at this tier:

- **Transport**: Enchanted rail, bound-elemental-propelled ships, magical carriages, long-haul mana-powered freight.
- **Communication**: Sending-stone telegraph networks, enchanted newspapers (mass-produced), scrying-crystal broadcasts (for elites).
- **Production**: Mass-enchantment Workshop networks, golem-assisted factories, enchanted agricultural machinery.
- **Energy**: Ambient-mana harvesting, leyline-tap infrastructure, mana-condenser batteries (for portable use).
- **Information**: Logos-inscribed data crystals, mechanical computation via enchanted gear networks, proto-typewriter-equivalents.
- **Medicine**: Mass-produced healing-charm potions, alchemical pharmacies, enchantment-assisted surgery.

### 16.3 Contemporary vs. Modern

Sharp distinction. An ARCADIA realm feels *contemporary* (the present moment for its inhabitants) without being *modern* in our specific sense (which is electronic-information-economy + globalized-fossil-fuel-economy). An ARCADIA realm can have radio-equivalent communication without having radio-the-technology. Cinematography without cinema. Aviation without airplanes (via bound-elemental flying craft).

### 16.4 The Twelve Design Axes Applied to ARCADIA

Same axes as §15.3 but different typical values:

- Magic distribution: common-graded is typical (most people do simple enchantment; specialists do complex).
- Cost transparency: obvious-external typically (manastones, ritual components are commodities).
- Domain coverage: broad-deep (magic touches every aspect of industrial life).
- Pneuma-electronics interference: full (electronics impossible) OR mana-free zones (industrial safe-havens for experimentation).
- God-set: industrial-age pantheon (guild gods, engineering gods, progress gods, market gods). Often a smaller pantheon than FANTASIA — rationalization has reduced mystery.
- Tech tier: enchantment-industrial is the default; Renaissance for earlier ARCADIA, post-industrial for later.
- Worldstate clock: `timeRatio: 60` typically — multi-year campaigns within real-month play sessions.
- UX emphasis: economic-heavy + political-heavy + crafting-heavy + social-heavy.
- Anti-entropy institutions: industrialized (printing presses, universities, professional societies).
- Population density: dense urban cores + industrialized hinterlands.

### 16.5 Three Worked Examples

**Example 1: Victorian-Industrial Enchantment City-State** — Guild-dominated manufacturing, stratified class structure, enchantment as daily infrastructure (street-lights, public transit, telegraph). Political economy with labor organizing, industrial barons, corporate enchantment-houses. Reference *Release That Witch* for specific factory mechanics. UX emphasis: political intrigue, craft mastery, commercial competition.

**Example 2: Wild-West Resource Frontier** — Rail-linked settlements, extractive economy (mining, ranching), weak central authority. Independent operators, merchant caravans, territorial disputes. Mana-rich discovery zones drive boom-towns. UX emphasis: exploration, combat, economic opportunism, faction navigation.

**Example 3: Mercantile City-State Network** — Trade-dominated, naval power, cosmopolitan. Multiple city-states with competing trade leagues, enchanted merchant fleets, financial instruments (letters-of-credit, stock-exchange equivalents). Heavy Hearsay and Information-Economy mechanics. UX emphasis: economic, social, negotiation.

### 16.6 Economic Integration

ARCADIA realms lean on §9 heavily. Develop:

- How Market (auction houses, NPC vendors, price discovery) manifests at industrial scale.
- Trade routes, shipment lifecycles, tariff politics.
- Organization as first-class economic actors (corporations, guilds, merchant houses).
- Divine economic intervention at industrial scale (Commerce Gods, Labor Gods, Craftsmanship Gods).
- Information Economy mechanics (industrial espionage, market intelligence, corporate secrets).

### 16.7 Cross-Reference: Extensions (§19) for ARCADIA

- Industrial-RPG Extension surfacing Organization mechanics as "companies" and "careers."
- Economic-sim Extension exposing market analytics and trade-route optimization.
- Detective-story Extension building on Cryptic Trails for period-mystery gameplay.

## Research Sources

- `docs/planning/EQUIPMENT-ENCHANTMENT-DUALITY.md` — how all-crafting-is-enchantment at industrial scale.
- `docs/planning/LOCATION-BOUND-PRODUCTION.md` — Factorio-style factory chains via Workshop.
- `docs/planning/INFORMATION-ECONOMY.md` — knowledge as tradeable commodity at scale.
- `docs/planning/TRADE.md` and `docs/plugins/TRADE.md` — trade routes, shipments, tariffs.
- `docs/plugins/MARKET.md` — auction and vendor mechanics.
- `docs/plugins/ORGANIZATION.md` (when available) — legal entities as actors.
- `docs/plugins/CRAFT.md` and `docs/guides/ECONOMY-SYSTEM.md` — foundation economic substrate.
- External: *Release That Witch* web novel/manhua (specific factory/industrial mechanics).

## Writing Guidance

- Emphasize what's *different* from FANTASIA, not what's the same.
- Heavy use of "economic and political" framing — ARCADIA is where the social fabric becomes market-structured.
- Worked examples should include mechanical choices across all twelve axes, matching §15's format.
- Include a table comparing ARCADIA realm feel to FANTASIA realm feel for the same material conditions.

## What This Section Must NOT Do

- Must not frame ARCADIA as "the goal" of FANTASIA progression. Realms don't progress; they are configured.
- Must not assume late-ARCADIA (logos-computational) as the default. Many ARCADIA realms sit at enchantment-industrial peak without ever advancing further.
- Must not conflate "modern" with "contemporary" — the distinction in §16.3 is load-bearing.

---

# 17. Designing OMEGA & Meta-Realms

> **Status**: Scope & context document.
> **Prerequisite reading**: [§3 Realm Archetype System](#3-the-realm-archetype-system), [§4 Guardian Spirit & Progressive Agency](#4-the-guardian-spirit--progressive-agency).
> **Estimated length**: 6,000–8,000 words.

## Conversation Context to Preserve

OMEGA is architecturally distinct from FANTASIA/ARCADIA — it is a **meta-realm**, a realm whose primary function is to mediate play in other realms. Established points:

- **Cyberpunk meta-dashboard aesthetic.** The player's OMEGA character typically lives in a high-rise apartment with a VR-machine at the center, and *plays* other realms (FANTASIA, ARCADIA, etc.) through that VR system.
- **Diegetic configuration.** The VR machine's literal in-world interface IS the interface through which the player configures their UX capability manifests, selects which account seed to inhabit, hacks UX unlocks, and meta-manages their multi-realm experience. Per PLAYER-VISION.md's Omega section.
- **Hacking mechanic** — players can use UX modules on characters/seeds that haven't "earned them" (forcing a spirit-character relationship that hasn't been naturally developed). Consequences: character resists more strongly, results may be inferior, social/ethical implications within Omega's cyberpunk narrative, potential for both creative exploitation and meaningful failure.
- **Also serves as explicit configuration layer** for experienced players who want to experiment with different UX combinations across seeds without re-earning everything organically.
- **The void never goes away** — post-release it persists as starting point, lobby, mini-game arcade, decompression space, seed selection. OMEGA is a first-class realm of the void-plus-everything-around-it.
- **Beyond Immersion App integration**: the canonical Arcadia deployment includes a publisher-shell app that is *simultaneously* the real-world app and the in-universe Omega hardware manufacturer. Deliberate fourth-wall trick where the real app and the diegetic Omega device are the same product. See `docs/planning/BEYOND-IMMERSION-APP.md`.

## Content Outline

### 17.1 What Defines an OMEGA Realm

- Cyberpunk meta-dashboard aesthetic.
- Mediating function — OMEGA characters play *other realms* through in-world VR mechanisms.
- Diegetic UX configuration — the VR machine in-world IS the configuration surface.
- Agency-as-interface — Agency's UX capability manifest surfaced as navigable menu.
- Hacking as a cyberpunk narrative mechanic.

### 17.2 The Diegetic Configuration Layer

Detail how this works:

- Agency service (§4.3) manages the UX capability manifest. In OMEGA, that manifest is *visible in-world* as the VR machine's configuration screen.
- Seed selection (§4.8) surfaces as choosing which cartridge/chip to insert into the VR machine.
- Cross-seed pollination (§4.4.1) surfaces as module transferability between VR sessions.
- Pair bonds (§4.7) surface as paired VR-machine hardware (twin-rigs for co-op players).
- Transitions between seeds surface as VR boot-up / shutdown cinematics.

This diegesis is not decoration. It makes the abstract concept of "accumulated UX capability" into a **concrete in-world artifact** that OMEGA characters can discuss, modify, steal, hack, and trade.

### 17.3 Hacking Mechanics

Per PLAYER-VISION.md's Omega mechanic: using UX modules on seeds that haven't earned them. Mechanically:

- Client-side: the Agency manifest is temporarily overridden with modules the seed hasn't naturally unlocked.
- Server-side: the server is authoritative. Hacks must be mediated by genuine Agency API calls with special "hacked" flags.
- Character reaction: the character (NPC brain) resists more strongly because this spirit-character relationship hasn't been naturally developed. Compliance drops; resistance builds up.
- Results inferior: the spirit has the interface but not the intuition — execution quality degrades proportional to how unearned the module is.
- Social/ethical consequences within Omega's narrative: hackers are outlaws in some OMEGA realm configurations; tolerated in others; celebrated in still others.
- Potential for both creative exploitation (experienced players experimenting with combinations) and meaningful failure (new players over-reaching and suffering).

### 17.4 The Multi-Realm Frame

OMEGA's defining function is as a frame around other realms. Develop:

- How an OMEGA-seed player's daily loop: wake in OMEGA apartment → use VR machine → inhabit a FANTASIA or ARCADIA character → return to OMEGA → maintenance/shopping/social in OMEGA.
- How OMEGA characters themselves have lives, jobs, relationships (so gameplay isn't just "transition room"). An OMEGA character might be a VR gear technician, a pro-gamer, a corporate infiltrator, a university professor studying VR-induced psychology.
- How multi-seed accounts (§4.8) manifest in OMEGA: multiple VR cartridges, each corresponding to a different seed. Players experimenting with their guardian spirit's experience space.

### 17.5 Beyond Immersion App as Canonical OMEGA

Per `docs/planning/BEYOND-IMMERSION-APP.md`, the Beyond Immersion publisher app IS the canonical OMEGA realm integration:

- The real-world app ships Bannou as an embedded instance on mobile (not a thin client — per embedded deployment per §20).
- 18+ content gating at account tier (not app tier) — preserves legal simplicity.
- Per-game companion modules (chat, compendiums, message boards, game notifications, mini-games affecting in-game state) delivered via CDN content packs.
- `lib-push` plugin with publisher/receiver modes and two-scope push (account-scope + possession-scope).
- The fourth-wall trick: Beyond Immersion the real publisher brand = Beyond Immersion the in-universe Omega hardware manufacturer.

### 17.6 One Worked Example

**OMEGA: Neon District Apartment Block** — cyberpunk city, high-rise apartment, VR-machine centerpiece. Player is an OMEGA character (name generated per character lifecycle; typical early-30s cyberpunk-genre demographic). Daily life: apartment, building, district, city. Primary gameplay: VR sessions into FANTASIA/ARCADIA realms. Secondary gameplay: OMEGA-native (work, relationships, cyberpunk-narrative scenarios in the city). Meta-gameplay: managing UX manifests across seeds, potentially hacking, Beyond Immersion app as canonical companion.

### 17.7 Alpha → Beta → Release Continuity

OMEGA is where the alpha→beta→release gradient operationalizes (PLAYER-VISION.md):

- Alpha: Void-only OMEGA (minimal apartment setting, VR machine, scenarios = void drift).
- Beta: OMEGA apartment grows; VR scenarios drop into real-realm slices.
- Release: Full OMEGA realm with persistent apartment life + permanent drops into FANTASIA/ARCADIA seeds.

### 17.8 OMEGA-Specific Axes

The twelve axes (§3.5) apply differently to OMEGA:

- Magic distribution: typically none. Technology (electronics) works here because OMEGA is partly or fully mana-free by design.
- Pneuma-electronics interference: none or minimal (to allow cyberpunk tech).
- God-set: minimal or absent. Corporate gods (parody of the divine economy). Possibly a "System" meta-god that governs the VR infrastructure.
- Tech tier: post-industrial / information-age / cyberpunk. Our closest analog.
- Anti-entropy institutions: corporate (data centers, cloud archives), regulated (civil infrastructure), hacker-counter-institutions.
- Worldstate clock rate: `timeRatio: 1` or `2` — OMEGA runs closer to real-time because the VR sessions are the time-dilated parts.

## Research Sources

- `docs/reference/PLAYER-VISION.md` — Omega concept (read the Omega Meta-Game section in full).
- `docs/planning/BEYOND-IMMERSION-APP.md` — publisher app canonical integration.
- `docs/planning/DEPLOYMENT-MODES.md` — how OMEGA runs (embedded Bannou on mobile).
- External: cyberpunk fiction (William Gibson's *Sprawl* trilogy, Stephenson's *Snow Crash*, *Ghost in the Shell*, *SAO*, *.hack*) for aesthetic.
- *Kurau: Phantom Memory* already referenced for pair bonds — also informs twin-rig VR architecture.

## Writing Guidance

- Shorter than §15/§16 because OMEGA is more mechanically constrained (less design freedom).
- Emphasize the diegetic-configuration insight — this is what makes OMEGA architecturally interesting.
- The worked example can be more prescriptive than FANTASIA's worked examples, because OMEGA's defining feature (VR-machine-as-config) is tight.
- Reference Agency and PLAYER-VISION frequently — OMEGA is where those systems become visible to players.

## What This Section Must NOT Do

- Must not treat OMEGA as "just another realm." It is a meta-realm; its function is structural.
- Must not invent new mechanics for hacking — the hacking mechanic is *already* a normal Agency API call with a "hacked" flag and different character-resistance outcomes.
- Must not confuse OMEGA (the archetype) with the Beyond Immersion app (the canonical realization).

---

# 18. Designing Underworld & Afterlife Realms

> **Status**: Scope & context document.
> **Prerequisite reading**: [§5 Unified Cognitive Progression & System Realms](#5-the-unified-cognitive-progression--system-realms), [§7 Content Flywheel](#7-the-content-flywheel), [§8 Life, Death, Generational Cycles](#8-life-death-and-generational-cycles), [§13 Actor-Bound Entities](#13-actor-bound-entities-living-places-and-things).
> **Estimated length**: 6,000–8,000 words.

## Conversation Context to Preserve

UNDERWORLD is a system realm (`isSystemType: true`) inhabited by dead characters. Established points:

- **UNDERWORLD is a realm, not a purgatory or game-over state.** Post-mortem characters have their own gameplay, Seed growth, social structures, and feedback into the living realms via the Content Flywheel.
- **Aspiration-based pathways** determine what a dead character's post-mortem experience looks like: Valhalla for unfulfilled warriors who died in battle, Purgatory-variants for unfulfilled craftsmen/scholars/diplomats, peaceful dissipation for fulfilled characters, Orpheus journey for rescue attempts by the living.
- **Leyline network topology** — the UNDERWORLD exists *inside* the leyline network of the living realms. Not a "separate plane" floating somewhere; physically interwoven with the material world's pneuma geography.
- **Soul currency** — post-mortem accomplishments produce spendable resources: resurrection cost (highest), blessings for living household (moderate), artifacts imbued with character essence (moderate), spirit shards for trait inheritance (variable).
- **The Fulfillment Principle** (§8.7) — more fulfilled in life = more logos flow to guardian spirit = greater household progression. Mechanical incentive to let characters die well rather than clinging to life past narrative peak.
- **The mobile ethereal base** — the guardian spirit recreates the living world's home at leyline locations within the underworld. Items sacrificed by living household members on the surface appear in the underworld base. Cross-realm interaction between living and dead household members.
- **Death echoes** — rare, ambient, emergent. Dead characters' logos fragments occasionally caught in leyline currents, manifesting as brief confused apparitions in random locations. Not scripted; not interactive; atmospheric reminder that death is transformation, not erasure. Cumulative over centuries.

## Content Outline

### 18.1 What Defines an UNDERWORLD Realm

- System realm with `isSystemType: true`.
- Inhabited by Characters whose death elsewhere archived them here.
- Post-mortem gameplay with its own mechanics and progression.
- Feedback into living realms via Content Flywheel (ghosts, revenants, inherited legacies).
- Cultural and mechanical variation permitted — Greek, Norse, Buddhist, Zoroastrian, Aztec, modern-secular, custom.

### 18.2 The Character Arrives

Per §8.5 (the three phases of death) and §8.6:

- The Fall (death cinematic).
- The Last Stand (unbound-spirit actions, god evaluation).
- Resolution options: Resurrection (changed), Underworld Entry, Passage.

UNDERWORLD entry is the specific path where the character's Character record transitions to the UNDERWORLD system realm. Their seed, inventory, personality, memory — all persist, now contextualized by the afterlife realm's configuration.

### 18.3 Aspiration-Based Pathway Selection

Characters enter different UNDERWORLD sub-paths based on their state at death:

- **Fulfilled** (major aspirations completed) — peaceful dissipation path. Minimal post-mortem gameplay. Logos flow smoothly to guardian spirit. Short, serene.
- **Unfulfilled warriors** (combat-focused, died in battle) — Valhalla-like path. Battle challenges preserve memories and earn soul currency. Long, eventful.
- **Unfulfilled craftsmen** — craft-trial path. Forging challenges, masterwork trials, teaching opportunities. Long, meditative.
- **Unfulfilled scholars** — research-trial path. Knowledge puzzles, discovery challenges, teaching positions. Long, intellectual.
- **Unfulfilled diplomats** — negotiation-trial path. Political puzzles, treaty-forging, historical arbitration. Long, strategic.
- **Unresolved bonds** — grief-oriented path. Reconciling with lost loved ones, completing unfinished relationships, guiding descendants.
- **Criminal** — custom paths per realm's moral framework. Purgation-for-redemption, eternal punishment, reincarnation-demotion.

### 18.4 Soul Currency

Per §8.6.3, post-mortem accomplishments produce **soul currency** — a spendable resource:

- **Resurrection** (highest cost; typically requires Orpheus-cooperation from the living).
- **Blessings for living household members** (moderate cost).
- **Artifacts imbued with character essence** (moderate cost; appear in living realms as heirlooms).
- **Spirit shards for trait inheritance** (variable; enhances descendants' starting traits).

The currency is generated by completing aspiration-path activities. Different paths produce different currency subtypes (warrior's valor vs. scholar's insight vs. craftsman's mastery — each spends on different categories of output).

### 18.5 The Mobile Ethereal Base

The guardian spirit recreates the household home at leyline locations in the UNDERWORLD. Mechanically:

- Living household members can perform rituals that sacrifice items on the surface.
- Sacrificed items appear in the underworld base's inventory.
- Dead characters can use these items (a sword sacrificed by a son appears in the father's underworld hands).
- The dead can also leave messages / artifacts that living household members occasionally perceive (through rituals, mediumship, dreams).
- Continuous cross-realm interaction between living and dead household members.

### 18.6 The Orpheus Journey

In extreme cases, a living character can descend into the UNDERWORLD to rescue a dead character. High-risk (the living character risks their own death), aspiration-driven (requires specific narrative conditions), produces some of the most dramatic content in the game.

- Mechanic: a living character's Seed must unlock the "descent" capability; specific ritual circumstances must align; the descent is a narrative scenario with UNDERWORLD gameplay layered on top of their normal character stack.
- Success: the dead character resurrects (with transformation — per §8.5.3).
- Failure: the living character may die too (permanently or temporarily); the dead character's state may deteriorate.

### 18.7 Ghost and Undead Manifestations in Living Realms

Per §7.7.1, compression archives enable multiple post-mortem entity types in living realms:

- **Ghosts** — Characters in UNDERWORLD with spectral abilities and location-anchoring in a living realm.
- **Revenants** — full archive restoration with amplified obsession traits, operating in living realms.
- **Zombies** — degraded cognition, partial memory, operating in living realms.
- **Clones** — new entities initialized from archive templates.
- **Echoes** — ambient location-based apparitions (non-interactive, atmospheric).

### 18.8 Four Worked Examples

**Example 1: Greek Underworld (Hellenic-inspired)** — River Styx, Charon ferryman, Hades as domain god, Elysian Fields (peaceful dissipation) vs. Asphodel Meadows (undifferentiated dead) vs. Tartarus (punishment path). Orpheus journey is canonical. Characters retain identity but weaken over time unless fed memories.

**Example 2: Norse Valhalla-focused** — Warriors who died in battle go to Valhalla (Odin's hall) for eternal training and feasting. Others go to Helheim (Hel's realm) for peaceful but subdued afterlife. Ragnarök looms — a narrative event where UNDERWORLD and living realms converge in prophesied conflict.

**Example 3: Buddhist Wheel-of-Rebirth** — Dead characters progress through rebirth stages based on karma (aspiration completion + moral alignment). Enlightenment (fulfilled + morally exceptional) exits the wheel. Attachment (unfulfilled) triggers rebirth as a new character with trait residue. Multi-generational cycles.

**Example 4: Secular-Memorial (modern-feeling)** — Characters persist as "active memories" held by living household members. As memory fades, the dead fade. Soul currency is generated by living kin remembering and honoring them. When actively remembered, the dead can interact with the living via memento mechanics; when forgotten, they dissipate.

### 18.9 Cross-Realm Narrative Feedback

How UNDERWORLD realms feed the living content flywheel:

- Dead characters generate ghost quests, revenant confrontations, memento encounters.
- Household members inherit wealth, obligations, grudges.
- Realm History records what dead characters did in life; storylines generate from archives.
- Divine actors observe UNDERWORLD activity and curate narratives in living realms.
- Long-lived UNDERWORLD civilizations develop their own cultures, which eventually influence living realms via dream-passage, memento transmission, and descendants' ancestral memories.

### 18.10 Configuration Considerations

UNDERWORLD realms are typically **shared across all game realms in a deployment** (per §3.2.4 — shared-system-realm pattern). One UNDERWORLD serves as afterlife for all FANTASIA, ARCADIA, and other realms in the deployment. This means:

- Dead characters from different living realms inhabit the same UNDERWORLD (creates cross-realm narrative opportunities — two characters from different realms meeting in afterlife).
- The UNDERWORLD's culture develops as an emergent composite of all the lives that ended in it.
- Design choice: one afterlife for all living realms, OR per-living-realm afterlives. Most deployments should default to shared.

## Research Sources

- `docs/planning/DEATH-AND-PLOT-ARMOR.md` — full design for death mechanics, Last Stand, god evaluation.
- `docs/planning/COMPRESSION-GAMEPLAY-PATTERNS.md` — ghost/revenant/zombie/clone patterns from archives.
- `docs/planning/ACTOR-BOUND-ENTITIES.md` (§ UNDERWORLD subsection) — system realm specifics.
- arcadia-kb Underworld and Soul Currency System (per user's reference library — check if present in hearthworks or bannou planning).
- External: mythology sources for each cultural framework — particularly Homer, *Poetic Edda*, Buddhist sutras.

## Writing Guidance

- UNDERWORLD is narratively heavy. The mechanics are important but the *emotional weight* of death-as-transformation is the through-line.
- The worked examples should feel distinct culturally while using identical underlying mechanics.
- Reference §8 heavily — life-to-death-to-afterlife is one continuous arc; UNDERWORLD is where it completes.
- The Mobile Ethereal Base mechanic is particularly poignant — spend time developing how living-dead interaction feels.

## What This Section Must NOT Do

- Must not frame UNDERWORLD as "the end" for characters. It is a new beginning in a different realm.
- Must not dictate a specific afterlife theology. The mechanics support many; designers pick their realm's flavor.
- Must not conflate "dead character gameplay" with "game over screen." The player of a dead character's guardian spirit continues to guide them.

---

# PART V — PLATFORM

# 19. The Extension Pattern (L5)

> **Status**: Scope & context document.
> **Prerequisite reading**: [§3 Realm Archetype System](#3-the-realm-archetype-system), [§15–§18 Realm Design Guides](#part-iv--realm-design-guides).
> **Estimated length**: 5,000–7,000 words.
> **Required reading before writing**: `docs/guides/EXTENDING-BANNOU.md` (the existing 25KB guide covers this ground thoroughly — this section should distill and cross-reference, not duplicate).

## Conversation Context to Preserve

Extensions are how studios translate Bannou's generic L0–L4 primitives into **game-specific vocabulary**. Key points established:

- **No skill plugin, no magic plugin, no combat plugin** in Bannou. Those concepts emerge from composition of primitives (Seed, License, Status, Collection, Actor, Personality, etc.).
- **Developers need game-specific vocabulary** that doesn't exist at the primitive level: "levels," "classes," "spells," "skills," "crafting recipes" as domain language.
- **Extensions are thin facades** that provide game-specific vocabulary, simplified APIs, and opinionated defaults on top of generic primitives.
- **Six extension patterns** exist and cover the full design space: Semantic Facade, Composition Orchestrator, Configuration Hardener, View Aggregator, Event Translator, Variable Provider.
- **Extensions follow the same schema-first pipeline** as core services: OpenAPI schema, code generation, implementation, testing.

## Content Outline

### 19.1 Why Extensions Exist

The composability thesis produces maximum flexibility at the cost of developer-facing vocabulary. A developer building a fantasy RPG doesn't want to think about "seed domain depths" and "license board adjacency" — they want to call `SkillsClient.GetLevel("swordsmanship", characterId)` and get an integer back. Extensions make that possible without sacrificing the underlying composability.

### 19.2 The Six Extension Patterns

Reproduce from EXTENDING-BANNOU.md with cross-references:

1. **Semantic Facade** — translates domain language to primitive calls. Example: `SkillsService.GetLevel` maps to Seed domain depth query.
2. **Composition Orchestrator** — atomically calls multiple primitives. Example: "craft this item" = consume materials + create output + debit currency + grant XP as one orchestrated call.
3. **Configuration Hardener** — registers game-specific types at startup. Seed types, collection types, contract templates all defined once, registered cleanly.
4. **View Aggregator** — assembles cross-service data into game-specific views. Example: "character sheet" = character + personality + quest + inventory data in one response.
5. **Event Translator** — re-publishes primitive events as domain events. Example: `seed.phase.changed` becomes `skill.level-up`.
6. **Variable Provider** — exposes computed game state to NPC behaviors via `IVariableProviderFactory`.

Each pattern deserves ~500-1000 words with concrete examples, schema snippets, and composition showing which primitives are called.

### 19.3 When to Extend vs. Compose Directly

Decision framework:

- **Compose directly** when: the caller is a single team, the vocabulary is already clear, the composition is one-off.
- **Extend** when: the composition pattern repeats across multiple game features, domain language matters for readability, opinionated defaults would save team time.

### 19.4 Genre Kit Examples

Extensions are the layer at which **genre kits** live. Examples:

- **Fantasy-RPG Kit** — Skills (seed+license facade), Classes (permission+collection facade), Spells (status+resource facade), Guilds (faction+organization facade).
- **Builder-RPG Kit** — Resources (item categorization facade), Buildings (location+scene facade), Research (seed+license facade).
- **Social-Sim Kit** — Relationships (encounter+relationship facade), Events (scenario+quest facade), Calendar (worldstate facade).
- **Cultivation Kit** — Dao-bonds (seed-bond facade), Breakthroughs (seed-phase facade), Pills (item+status facade).
- **Survival Kit** — Hunger/Thirst/Temperature (status+disposition facade), Shelter (location+scene facade), Crafting (craft facade with opinionated recipes).

Each kit name and extension plugin should be preserved in the final write-up.

### 19.5 Schema-First Extension Development

Reproduce the extension lifecycle from EXTENDING-BANNOU.md:

1. Write OpenAPI schema for extension APIs (`skills-api.yaml`).
2. Run code generation (`generate(script: "models", service: "skills")`).
3. Implement service class (`SkillsService.cs`) with constructor-injected generic clients.
4. Wire up startup configuration (seed type registration, collection type registration).
5. Write unit tests (plugin test isolation — `lib-skills.tests` may reference `bannou-service` + `lib-skills` only).

### 19.6 Tutorial: Building a Reputation Extension

The EXTENDING-BANNOU guide includes a complete tutorial. Reproduce its outline here with cross-references — building a `lib-reputation` extension that exposes faction reputation as game-specific API with opinionated thresholds.

### 19.7 The Plugin Lifecycle Pipeline

Reference `docs/planning/PLUGIN-LIFECYCLE-PIPELINE.md` for the L0–L7 readiness levels and skill commands. This section should briefly map where extensions fit in the lifecycle.

### 19.8 Anti-Patterns

Per EXTENDING-BANNOU.md's anti-patterns section:

- Extensions should not store their own game data (state lives in primitives).
- Extensions should not bypass primitives (call them via generated clients).
- Extensions should not depend on other extensions (L5 → L5 dependencies hide flexibility).
- Extensions should not re-implement what primitives already provide.

## Research Sources

- `docs/guides/EXTENDING-BANNOU.md` — the definitive existing guide; this section distills and cross-references, never duplicates.
- `docs/planning/PLUGIN-LIFECYCLE-PIPELINE.md` — readiness levels and progression.
- `docs/guides/PLUGIN-DEVELOPMENT.md` — core plugin mechanics.
- `docs/reference/SERVICE-HIERARCHY.md` — why L5 sits where it does.

## Writing Guidance

- This section should be *shorter* than the realm design guides. EXTENDING-BANNOU is already comprehensive; this section's job is cross-referencing and connecting the extension pattern to realm design.
- Heavy use of "which primitive does what" tables.
- At least one worked example (a complete extension plugin outline) for concreteness.
- Tone: BANNOU-DESIGN.md voice (technical, table-heavy).

## What This Section Must NOT Do

- Must not duplicate EXTENDING-BANNOU.md content (refer to it instead).
- Must not prescribe specific extension plugins as canonical for any realm. Genre kits are *examples*.
- Must not conflate extensions with core plugins (L5 is architecturally distinct).

---

# 20. Deployment, Engine Integration, and Scale

> **Status**: Scope & context document.
> **Prerequisite reading**: [§3 Realm Archetype System](#3-the-realm-archetype-system), [§6 NPC Intelligence Stack](#6-the-npc-intelligence-stack), [§17 OMEGA / Beyond Immersion App](#17-designing-omega--meta-realms).
> **Estimated length**: 6,000–8,000 words.

## Conversation Context to Preserve

Bannou's "same binary, many deployment modes" thesis is central. Key points:

- **Four deployment modes**: Embedded (in-process, no network), Non-dedicated (player-hosted sidecar), Dedicated (single-node), Hyper-scaled (multi-node distributed). Same codebase; deployment is configuration.
- **Realms can run at different deployment profiles** within the same overall deployment. An FANTASIA realm with 100 concurrent players can be single-node; a flagship ARCADIA realm with 10K might be multi-node sharded.
- **Infrastructure backends swap cleanly** — Redis/MySQL for cloud; SQLite/InMemory for sidecar/embedded. Applications don't know the difference.
- **Engine integrations**: Unity, Unreal, Godot, Stride all supported. SDK generation is per-engine; core protocol is shared (WebSocket binary, 31-byte header).
- **100K NPC scale** requirements shape architecture everywhere: direct-RabbitMQ perception delivery, bounded queues, pool nodes, batch lifecycle events, Resource Transactions, lazy Workshop evaluation, batched Chat/Hearsay/Collection APIs.
- **Beyond Immersion App** ships Bannou embedded on mobile — not a thin client. Single-player and LAN co-op use the same embedded architecture.
- **Layer-level enablement controls** (`BANNOU_ENABLE_*`) provide fine-grained deployment control. Infrastructure always-on; everything else configurable.

## Content Outline

### 20.1 The Thesis: One Binary, Many Modes

Restate the thesis from VISION.md and BANNOU-DESIGN.md. Emphasize that this is load-bearing for Arcadia's commercial viability — studios can target mobile (embedded), Nintendo Switch (non-dedicated or embedded), desktop single-player (embedded), LAN co-op (non-dedicated), dedicated private servers, and massive cloud deployments all on the same platform.

### 20.2 The Four Deployment Modes

Per `docs/planning/DEPLOYMENT-MODES.md`:

**Embedded**: Bannou runs in-process inside the game. No HTTP, no WebSocket, no Docker. `IServiceClient` interfaces resolve to direct DI calls. SQLite + InMemory backends. Single-player and mobile games. The Beyond Immersion App is this.

**Non-dedicated (player-hosted sidecar)**: Bannou ships as a binary alongside the dedicated server or the host's client. SQLite for persistence, InMemory for messaging. Satisfactory/Valheim style. The server can sleep between sessions; Workshop lazy evaluation catches up on reconnect.

**Dedicated (single-node)**: Standard server deployment. Redis, MySQL, RabbitMQ. All 78 plugins loaded. Game serves hundreds to thousands of concurrent players.

**Hyper-scaled (multi-node)**: Distributed deployment with plugin sharding. Orchestrator manages node topology. Per-layer scaling (Actor pool nodes separate from State nodes separate from Asset pools). The architectural target for the flagship Arcadia deployment.

### 20.3 Layer-Level Enablement Controls

`BANNOU_ENABLE_APP_FOUNDATION`, `BANNOU_ENABLE_GAME_FOUNDATION`, `BANNOU_ENABLE_APP_FEATURES`, `BANNOU_ENABLE_GAME_FEATURES`, `BANNOU_ENABLE_EXTENSIONS` — all default true. Individual services overridable with `{SERVICE}_SERVICE_ENABLED`. Resolution order: infrastructure always on → individual override → master kill switch (`BANNOU_SERVICES_ENABLED=false`) → layer control.

Example configurations:

- Auth node: `BANNOU_ENABLE_GAME_FOUNDATION=false BANNOU_ENABLE_APP_FEATURES=false BANNOU_ENABLE_GAME_FEATURES=false`
- NPC processing node: `BANNOU_ENABLE_APP_FEATURES=false BANNOU_ENABLE_GAME_FEATURES=false` + explicit Behavior/Character/Actor overrides true
- Full game minus voice: `VOICE_SERVICE_ENABLED=false`

### 20.4 Infrastructure Backend Swapping

Per `docs/planning/SELF-HOSTED-DEPLOYMENT.md` and `docs/planning/BANNOU-EMBEDDED.md`:

- **State**: Redis + MySQL (cloud) ↔ SQLite + InMemory (local).
- **Messaging**: RabbitMQ (cloud) ↔ InMemory (local).
- **Mesh**: YARP HTTP (cloud) ↔ in-process DI calls (embedded).
- **Asset**: MinIO/S3 (cloud) ↔ local filesystem (local).

Applications call `IStateStoreFactory.GetStore<T>()` and don't know whether it returns Redis-backed or SQLite-backed storage. All five infrastructure libs (State, Messaging, Mesh, Telemetry, Asset) follow this pattern.

### 20.5 Engine Integration

Per `docs/guides/` client integration guides:

- **.NET (Unity, Stride, Godot-C#)**: `BeyondImmersion.Bannou.Client` NuGet package. Compile-time-safe typed proxies (`client.Auth.LoginAsync()`). Disposable typed event subscriptions. `IBannouClient` interface for DI/mocking.
- **Unreal Engine (C++)**: Five auto-generated headers — `BannouProtocol.h`, `BannouTypes.h` (USTRUCTs), `BannouEnums.h`, `BannouEndpoints.h`, `BannouEvents.h`. Binary protocol implementation with RFC 4122 GUID handling. Regeneration via `make generate-unreal-sdk`.
- **TypeScript (Web, Electron)**: `@beyondimmersion/bannou-client` package. Full typed client, promise-based API.
- **Any Language**: WebSocket binary protocol is documented (31-byte request headers, 16-byte response headers, JSON payloads). Engine-agnostic.

### 20.6 100K NPC Scale Architecture

Detail the architectural choices that make this work (most already developed in §6.8):

- **Direct RabbitMQ perception delivery** (control plane is not in perception path).
- **Bounded perception queues with DropOldest** (perception floods don't collapse cognition).
- **Actor pool nodes as peers** with lifecycle operations through control plane only.
- **Batch APIs** throughout (Chat `SendMessageBatch`, Hearsay bulk beliefs, Collection bulk discoveries, Seed batch growth).
- **Lazy Workshop evaluation** — dormant production facilities cost zero ticks.
- **Batch Lifecycle Events** — per `docs/planning/BATCH-LIFECYCLE-EVENTS.md`, high-frequency entity events use `batch: true` to generate only batch event types, reducing bus pressure.
- **Resource Transactions** — per `docs/planning/RESOURCE-TRANSACTIONS.md`, durable multi-service provisioning with automatic compensation prevents silent orphans at scale.

### 20.7 Connectivity Modes and Cross-Node Coordination

- **Mesh routing** via YARP with circuit breaking and Redis-backed discovery.
- **Cross-node messaging** via RabbitMQ.
- **Entity Session Registry** in Connect (L1) provides `(entityType, entityId) → Set<sessionId>` mapping for client-event routing across nodes.
- **Multi-node Connect relay** for WebSocket players who may be connected to different Connect instances.

### 20.8 Realm-Specific Deployment Profiles

Given §3's flat-realm architecture, different realms can run on different infrastructure profiles:

- FANTASIA generational realm → low `timeRatio`, lazy Workshop dominant, low concurrent-player count → single-node.
- ARCADIA flagship economic realm → high concurrent play, deep market analytics → multi-node with sharding.
- OMEGA meta-realm → player-session-heavy, VR-session routing → Connect-heavy deployment.
- UNDERWORLD → low-throughput, cross-realm feedback → shared single-node.

The **same deployment** can host all of these simultaneously via the layer-enablement and service-specific controls.

### 20.9 Saving, Loading, and Persistence

- `lib-save-load` handles versioned saves, delta saves, two-tier storage (Redis hot cache + MinIO cold storage), schema migration, cloud sync.
- Embedded mode uses SQLite for everything including Save-Load.
- Non-dedicated (player-hosted) uses local filesystem MinIO equivalents.
- Hyper-scaled uses full cloud stack.

### 20.10 Deployment Migration Paths

How a realm moves between deployment profiles:

- A single-node dedicated can scale up to multi-node by adding Actor pool nodes (no code changes).
- An embedded game can add optional cloud sync via `lib-save-load` without fundamental redeployment.
- A hyper-scaled deployment can shard further by splitting Actor pools per region.

## Research Sources

- `docs/planning/DEPLOYMENT-MODES.md` — the canonical treatment.
- `docs/planning/BANNOU-EMBEDDED.md` — embedded-mode specifics.
- `docs/planning/SELF-HOSTED-DEPLOYMENT.md` — non-dedicated/sidecar.
- `docs/planning/BATCH-LIFECYCLE-EVENTS.md` — scale-at-100K optimization.
- `docs/planning/RESOURCE-TRANSACTIONS.md` — durable provisioning.
- `docs/guides/CLIENT-INTEGRATION.md`, `docs/guides/UNREAL-INTEGRATION.md`, `docs/guides/TYPESCRIPT-SDK.md` — engine integration.
- `docs/operations/DEPLOYMENT.md` — operational deployment procedures.
- `docs/reference/SERVICE-HIERARCHY.md` — why layer enablement works.

## Writing Guidance

- Tone: BANNOU-DESIGN.md voice (technical, architectural, table-heavy).
- Include deployment mode comparison tables throughout.
- Heavy cross-referencing to §6 for scale architecture and §17 for OMEGA/embedded specifics.
- Include diagrams for each deployment mode.

## What This Section Must NOT Do

- Must not duplicate `docs/operations/DEPLOYMENT.md` — that's the operational guide. This section is architectural.
- Must not prescribe a single deployment profile for every realm. The flexibility is the point.
- Must not overstate current implementation — flag which pieces (e.g., hyper-scaled sharding) are still design-stage.

---

# 21. The Developer Experience

> **Status**: Scope & context document.
> **Prerequisite reading**: [§12 Procedural Creative Generation](#12-procedural-creative-generation), [§19 Extension Pattern](#19-the-extension-pattern-l5), [§20 Deployment](#20-deployment-engine-integration-and-scale).
> **Estimated length**: 5,000–7,000 words.

## Conversation Context to Preserve

Building on Bannou is a **fundamentally different developer experience** than building a traditional game backend. The content flywheel, schema-first pipeline, and ABML authoring produce a different daily loop. Key points:

- **Schema-first is inviolable** (Tenet 1). Contracts live in OpenAPI YAML; code generates from them. Developers edit schemas and regenerate, never edit generated files.
- **What developers *do* in Arcadia** differs from traditional games:
  1. Author ABML behavior documents (the primary creative act).
  2. Compose seed data (item templates, species, location hierarchies, currency schemes, license boards, loot tables, etc.).
  3. Build the visual/audio layer (client-side rendering).
  4. Write game-specific L5 Extensions where composition patterns repeat.
  5. Configure deployment.
- **What developers *don't* do**: backend development (the 78 plugins already exist), distributed systems engineering (the infrastructure libs abstract it), content authoring at scale (the flywheel generates it), networking, asset pipelines, etc.
- **The MCP server** (`.claude/mcp/`) is a primary developer tool — it provides introspection, code generation, plugin documentation, schema inspection, and structured file operations.
- **Testing is three-tier**: unit (Claude writes and runs), HTTP integration (Claude writes, user runs), WebSocket edge (Claude writes, user runs). Integration tests are heavy — unit tests are sufficient verification for most changes.

## Content Outline

### 21.1 What Developers Actually Do

Organize around the five things developers do in Arcadia (from conversation). Each section ~800-1200 words.

#### 21.1.1 Author ABML Behavior Documents

The primary creative act. Developers define:

- NPC goals, actions, conditions.
- God behaviors (regional watchers).
- Cinematic choreographies.
- Dungeon cognition behaviors.
- Player-experience orchestration (Gardener behaviors).

ABML is YAML-based. The compiler handles parsing, semantic analysis, bytecode generation. Developers write behaviors; the Actor runtime executes them.

Reference `docs/guides/ABML.md` and `docs/guides/BEHAVIOR-SYSTEM.md` heavily. Include example ABML snippets throughout.

#### 21.1.2 Compose Seed Data

Game worlds are defined by configuration, not code:

- Item templates (what items exist).
- Species (playable and NPC races with trait modifiers).
- Location hierarchies (world structure).
- Currency definitions (economy currencies).
- Quest templates, loot tables, seed types, transit modes, collection types, faction definitions, license boards.

All loaded via API calls at startup or through bulk seeding endpoints. No code changes, no recompilation.

#### 21.1.3 Build the Visual/Audio Layer

Client-side:

- 3D models, textures, animations, VFX.
- UI/UX implementation.
- Audio rendering of MIDI-JSON from Music SDK.
- Client-side interpretation of cinematic sequences.
- Input handling and camera systems.

This is the traditional game-client development work. Bannou doesn't eliminate it; Bannou handles everything behind the client.

#### 21.1.4 Write L5 Extensions Where Needed

For repeated composition patterns, create Extensions (per §19). Genre kits are the canonical use case.

#### 21.1.5 Configure Deployment

Choose the deployment mode (§20). Configure layer/service enablement. Engine integration. CI/CD.

### 21.2 The Daily Developer Loop

A day in the life of an Arcadia developer:

- Morning: review overnight god-actor orchestrations (what narratives emerged?).
- Mid-morning: write an ABML behavior update for a god-actor whose aesthetic should shift.
- Afternoon: define new seed data for a seasonal event (item templates, new quest templates).
- Late afternoon: test in local embedded mode (fast iteration).
- Evening: PR review, CI runs (unit tests + schema generation validation).

Compare to traditional game backend: no morning incidents about auth service downtime, no afternoon debugging of player state corruption, no evening planning for next sprint's feature backend work.

### 21.3 Schema-First Development

Per Tenet 1 and `docs/guides/PLUGIN-DEVELOPMENT.md`:

```
Schema (YAML) → Code Generation → Implementation → Testing
                     ↓
              Controllers, Models, Clients, Tests
```

Walk through an example: adding an endpoint to a plugin. Edit YAML → run `generate(script: "X", service: "Y")` → implement method → run tests → PR.

### 21.4 The MCP Server as Primary Tool

Per `docs/operations/MCP-SERVER.md`, the Bannou MCP server provides:

- `read_file`, `edit_file`, `write_file` for sandboxed file operations.
- `generate`, `print_models`, `print_interfaces` for introspection.
- `list_plugins`, `get_plugin_docs`, `list_documents`, `get_document` for documentation discovery.
- `list_schemas`, `get_schema` for schema inspection.
- `search_docs`, `list_documents` for documentation search.
- `dispatch_worker` for spawning focused implementation agents.
- `prepare_context` for efficient context loading.

For human developers, equivalent `make` commands exist (`make print-models`, `make generate-all`, etc.). For AI agents, MCP tools are the interface.

### 21.5 Testing Architecture

Three tiers:

| Tier | Purpose | Command |
|------|---------|---------|
| Unit | Service logic in isolation | `dotnet test --project plugins/lib-{service}.tests/...` |
| HTTP Integration | Service-to-service via HTTP | `make test-http` |
| Edge | Full WebSocket protocol | `make test-edge` |

Schema-driven test generation creates tests automatically from OpenAPI specs. Dual-transport validation ensures HTTP and WebSocket paths produce identical results.

### 21.6 The Embedded-Mode Advantage

Embedded deployment (§20.2) produces a uniquely productive developer loop:

- No local infrastructure required (no Docker, no Redis, no RabbitMQ).
- Game client + Bannou = single process = single debugger attach.
- Instant iteration on schema changes (regenerate, recompile, run).
- Full 78-plugin functionality available locally.

This is the developer-experience killer feature. Most game backends require hours to set up locally; Bannou-embedded is seconds.

### 21.7 How Arcadia Developers Differ from Traditional Game Developers

Frame as a comparison table:

| Traditional Game Dev | Arcadia Developer |
|---|---|
| Design features, implement features | Author behaviors, compose seed data |
| Write backend services | Use existing 78 plugins |
| Build distributed systems | Configure deployment |
| Hand-author content | Configure content flywheel to generate content |
| Playtest → find bugs → patch | Playtest → observe emergent content → refine behaviors |
| Quarterly content updates | Continuous emergent content |

The shift is from **authoring** to **designing the systems that generate content**. This is the Emergent-Over-Authored principle (§VISION.md) manifesting in developer experience.

### 21.8 The Learning Curve

Honest assessment: Arcadia is not a trivial platform to learn. The developer must understand:

- The metaphysical substrate (§2).
- The system-realm pattern (§5).
- The variable provider system (§6.4).
- ABML syntax and GOAP planning.
- Schema-first conventions.

However, once learned, Arcadia developers can ship features in hours that would take teams weeks on traditional platforms. The curve is steep but short.

### 21.9 Documentation Infrastructure

Per `docs/guides/DEVELOPMENT-QUICKSTART.md`:

- 78 plugin deep dives (`docs/plugins/*.md`).
- 78 implementation maps (`docs/maps/*.md`).
- 25+ cross-cutting guides (`docs/guides/*.md`).
- Planning documents for aspirational work (`docs/planning/*.md`).
- Vision documents (`docs/reference/VISION.md`, `PLAYER-VISION.md`).
- Tenet documents (`docs/reference/TENETS.md` and its sub-documents).

Auto-generated references regenerate on schema changes:

- Service details per layer.
- Events reference.
- State stores reference.
- Configuration reference.
- Composition reference (plugins by layer, endpoint counts).

### 21.10 AI-Agent Development

Arcadia is designed for AI-agent-augmented development:

- Schemas are machine-readable (OpenAPI).
- MCP tooling provides structured access.
- Custom agents (`bannou`, `bannou-dev`, `bannou-schema`) pre-load context via `prepare_context`.
- Pattern-based code generation means agents don't have to reason about every file.
- Strict tenets (TENETS.md) provide clear boundary conditions.

The solo-developer-plus-agent pattern is the intended development model. A solo developer with capable agent assistance can ship feature-complete Arcadia realms that would historically require teams of 20+ engineers.

## Research Sources

- `docs/guides/DEVELOPMENT-QUICKSTART.md` — getting started.
- `docs/guides/GETTING-STARTED.md` — step-by-step onboarding.
- `docs/guides/PLUGIN-DEVELOPMENT.md` — creating plugins.
- `docs/guides/ABML.md` — behavior authoring.
- `docs/operations/MCP-SERVER.md` — tooling reference.
- `docs/operations/TESTING.md` — test architecture.
- `CLAUDE.md` and `CLAUDE-PRACTICES.md` — AI-agent conventions.

## Writing Guidance

- Tone: BANNOU-DESIGN.md voice (technical but accessible).
- Include example snippets throughout (ABML flows, schema fragments, command lines).
- Frame from the developer's perspective — what does their week look like?
- Honest about the learning curve — don't oversell.

## What This Section Must NOT Do

- Must not duplicate the quickstart guides. Reference them; don't reproduce them.
- Must not prescribe a single workflow — many valid workflows exist.
- Must not overstate AI-agent capabilities. The solo-dev-plus-agent model is intended but requires capable agents.

---

# APPENDICES

# Appendix A: Plugin Role Index (by function)

> **Status**: Scope & context document.
> **Estimated length**: 3,000–5,000 words (primarily a reference table with brief descriptions).

## Purpose

This appendix catalogs **every plugin** by its *contribution to Arcadia's gameplay* rather than by architectural layer. The layer view exists in GENERATED-COMPOSITION-REFERENCE.md; this appendix organizes the same 78 plugins by what they *do for the player's experience*, which is a more useful lens for designers thinking about realm design.

## Format

Each plugin entry:

- Plugin name (`lib-X`).
- Layer (L0-L5).
- Status (Implemented / In progress / Planned / Aspirational).
- One-line role description.
- Cross-reference: which sections of this document discuss this plugin.

## Groupings to Produce

Each plugin goes in **exactly one** primary grouping. Some cross-reference notes are useful where a plugin serves multiple concerns.

### Group 1: Identity & Session (L1 primarily)

- `lib-account` — internal user account CRUD.
- `lib-auth` — internet-facing authentication.
- `lib-connect` — WebSocket edge gateway with zero-copy binary routing.
- `lib-permission` — RBAC capability manifest compilation.
- `lib-subscription` — account-to-game access mapping.

### Group 2: Infrastructure (L0)

- `lib-state` — unified state persistence (Redis/MySQL/SQLite/InMemory).
- `lib-messaging` — RabbitMQ pub/sub (with in-memory and direct-dispatch modes).
- `lib-mesh` — service-to-service invocation via YARP with circuit breaking.
- `lib-telemetry` — OpenTelemetry distributed tracing.

### Group 3: Character Foundation (L2)

- `lib-character` — game-world character management with realm partitioning.
- `lib-character-personality` (L4) — personality traits with probabilistic evolution.
- `lib-character-encounter` (L4) — NPC encounter memory.
- `lib-character-history` (L4) — historical event participation and backstory.
- `lib-character-lifecycle` (L4) — aging, marriage, procreation, death, inheritance.
- `lib-relationship` — typed bonds between entities.
- `lib-species` — realm-scoped species definitions.

### Group 4: Realm & World (L2)

- `lib-realm` — top-level persistent world management (flat peer worlds + system realms).
- `lib-location` — hierarchical location tree within realms.
- `lib-transit` — geographic connectivity graph and journey tracking.
- `lib-worldstate` — per-realm game clock and calendar.
- `lib-realm-history` (L4) — civilizational memory.

### Group 5: NPC Cognition (L2/L4)

- `lib-actor` (L2) — NPC behavior execution runtime (ABML, GOAP, perception queues).
- `lib-behavior` (L4) — ABML compiler and GOAP planner.
- `lib-puppetmaster` (L4) — dynamic behavior orchestration, regional watchers.
- `lib-ethology` (L4, planned) — species-level behavioral archetype registry.

### Group 6: Growth & Progression (L2)

- `lib-seed` — universal progressive growth primitive.
- `lib-collection` — content unlock and archive system.
- `lib-license` (L4) — grid-based progression boards (skill trees).
- `lib-genesis` — template-driven entity awakening lifecycle (dungeons, weapons, spirits).
- `lib-agency` (L4, planned) — UX capability manifest engine.

### Group 7: Economy (L2/L4)

- `lib-currency` — multi-currency wallets, transfers, exchange, autogain.
- `lib-item` — dual-model items (templates + instances).
- `lib-inventory` — containers and placement with constraint models.
- `lib-escrow` (L4) — full-custody multi-party asset exchange.
- `lib-contract` (L1) — binding agreements engine.
- `lib-affix` (L4) — item modifier definitions (Type 1/Type 2 enchantments).
- `lib-craft` (L4) — recipe-based crafting orchestration.
- `lib-loot` (L4, planned) — loot table management.
- `lib-market` (L4, planned) — auctions and NPC vendors.
- `lib-trade` (L4, planned) — trade routes and shipment tracking.
- `lib-workshop` (L4, planned) — time-based automated production.
- `lib-organization` (L4, planned) — legal entities (shops, guilds, households).

### Group 8: Social Fabric (L4 primarily)

- `lib-chat` (L1) — typed message channel primitives.
- `lib-lexicon` (L4, planned) — canonical concept ontology.
- `lib-hearsay` (L4, planned) — belief propagation with distortion.
- `lib-disposition` (L4, planned) — feelings and drives.
- `lib-obligation` (L4) — contract-aware moral cost modifiers.
- `lib-faction` (L4) — seed-based political structures.
- `lib-arbitration` (L4, planned) — dispute resolution orchestration.

### Group 9: Content Flywheel (L1/L4)

- `lib-resource` (L1) — archive coordination and cross-service cleanup.
- `lib-storyline` (L4) — GOAP narrative generation from archives.
- `lib-quest` (L2) — objective-based progression wrapping Contract.
- `lib-divine` (L4) — deity management, divinity economy, blessings.
- `lib-gardener` (L4) — player experience orchestration (void, scenarios).
- `lib-dungeon` (L4, planned) — actor-bound dungeon lifecycle.
- `lib-achievement` (L4) — achievement/trophy system.

### Group 10: Creative Generation (L4)

- `lib-music` (L4) — procedural music generation.
- `lib-scene` (L4) — hierarchical scene composition.
- `lib-cinematic` (L4, planned) — choreographic cinematic composition.
- `lib-procedural` (L4, planned) — parametric 3D generation via Houdini.

### Group 11: Environment & Ecology (L4, planned)

- `lib-environment` (L4, planned) — weather, temperature, mana density, ecological resources.

### Group 12: Status & Effects (L4)

- `lib-status` — unified entity effects query layer.

### Group 13: Matchmaking & Sessions (L2/L4)

- `lib-game-service` (L2) — registry of available games.
- `lib-game-session` (L2) — multiplayer session containers.
- `lib-matchmaking` (L4) — ticket-based matchmaking.

### Group 14: Deployment & Operations (L3)

- `lib-orchestrator` (L3) — deployment orchestration (Docker/Swarm/K8s).
- `lib-save-load` (L4) — versioned saves with two-tier storage.
- `lib-analytics` (L4) — event aggregation, skill ratings, milestones.
- `lib-broadcast` (L3) — streaming platform integration.
- `lib-voice` (L3) — voice room coordination (P2P mesh + SFU).
- `lib-director` (L4) — human-in-the-loop event coordination.
- `lib-showtime` (L4, planned) — in-game streaming metagame.

### Group 15: Asset Pipeline (L3)

- `lib-asset` (L3) — binary asset storage with bundles and pre-signed URLs.

### Group 16: Information & Discovery (L4)

- `lib-documentation` (L3) — knowledge base API for AI agents.
- `lib-website` (L3) — public-facing browser CMS.
- `lib-mapping` (L4) — spatial data management with affordance queries.

### Group 17: Meta / Additional (L4, planned)

- `lib-hearsay`, `lib-lexicon`, `lib-disposition` (cross-listed with social).
- `lib-leaderboard` (L4) — Redis sorted set leaderboards.
- `lib-utility` (L4, planned) — infrastructure network topology.
- `lib-obligation` (cross-listed with social).

## Cross-References to Document Sections

Every plugin entry should include a list of which sections of this document discuss it:

- `lib-seed` — §§4, 8, 13, 14 (universal growth primitive).
- `lib-actor` — §§5, 6, 11, 12 (cognition runtime).
- `lib-currency` — §§9, 13 (economy substrate + divinity generation).
- (...and so on for every plugin)

## Research Sources

- `docs/generated/GENERATED-COMPOSITION-REFERENCE.md` — definitive plugin list.
- `docs/generated/GENERATED-*-SERVICE-DETAILS.md` — per-layer details.
- `docs/plugins/*.md` — per-plugin deep dives.
- `docs/maps/*.md` — per-plugin implementation maps.

## Writing Guidance

- Table-heavy. Each plugin gets a row.
- Concise one-liners — this is a reference, not a tutorial.
- Every plugin in the 78-plugin catalog must appear.
- Cross-references to document sections must be comprehensive.

## What This Appendix Must NOT Do

- Must not duplicate GENERATED-COMPOSITION-REFERENCE.md's structure (it's by layer; this is by function).
- Must not provide deep-dive content per plugin (refer to `docs/plugins/`).
- Must not omit any plugin — completeness is the appendix's primary value.

---

# Appendix B: SDK Reference (by domain)

> **Status**: Scope & context document.
> **Estimated length**: 2,000–3,000 words.

## Purpose

Catalog **every SDK** organized by creative domain rather than alphabetically. For each SDK: status, dependencies, consumers, Theory/Storyteller/Composer/Bridge layer designation, and cross-references to this document's sections.

Rationale: per §12, SDKs follow a Theory/Storyteller/Composer/Bridge pattern. Organizing by domain shows the pattern clearly and helps designers pick SDKs for their game.

## Format

Per SDK:

- Name.
- Layer designation (Theory / Storyteller / Composer / Bridge — or Infrastructure for non-creative SDKs).
- Status (Complete / In progress / Planned / Aspirational).
- Dependencies (what this SDK requires).
- Consumers (who uses this SDK).
- One-line purpose.
- Cross-reference: §§ in this document that discuss it.

## Groupings

### Music Domain

- `music-theory` (Theory, Complete) — pitch, scales, chords, harmony, voice leading, rhythm, MIDI-JSON output. Zero deps. Consumers: `music-storyteller`, `lib-music`, content tools.
- `music-storyteller` (Storyteller, Complete) — 6D emotional state, narrative templates, GOAP composition. Deps: `music-theory`. Consumers: `lib-music`.
- `music-composer` (Composer, Planned) — interactive music authoring with theory primitives.
- `counterpoint-composer` (Composer, Planned) — structural template workbench for counterpoint-compatible music.
- Cross-references: §12.3.

### Narrative Domain

- `storyline-theory` (Theory, Complete) — archive extraction, Greimas actants, Reagan arcs, Barthes kernels, life-value spectrums. Deps: none. Consumers: `storyline-storyteller`, `lib-storyline`.
- `storyline-storyteller` (Storyteller, Complete) — GOAP narrative planning, template composition, archive consumption. Deps: `storyline-theory`. Consumers: `lib-storyline`.
- `storyline-composer` (Composer, In design) — typed scenario definitions, mechanical condition evaluation, mutation descriptions.
- Cross-references: §7, §12.4.

### Cinematic Domain

- `cinematic-theory` (Theory, In design) — Toric Space, EMOTE/Laban, HFSM idioms, Murch Rule of Six, dramatic grammar.
- `cinematic-storyteller` (Storyteller, In design) — GOAP choreography, Facade beat sequencing, tension arcs, agency-scaled QTE density.
- `cinematic-composer` (Composer, In design) — timeline/track authoring, sync barriers, continuation points.
- Cross-references: §11, §12.5.

### Scene Domain

- `scene-composer` (Composer, Complete) — engine-agnostic scene-graph editing. Deps: none (pure). Consumers: engine bridges, `lib-scene`.
- `scene-composer-stride` (Bridge, Complete) — Stride engine integration.
- `scene-composer-godot` (Bridge, Complete) — Godot 4.x integration.
- Cross-references: §12.6.

### Voxel Domain

- `voxel-core` (Theory, Planned) — sparse voxel grid, math, .bvox serialization, meshing/voxelization. Deps: LZ4.
- `voxel-generator` (Storyteller, Planned) — WFC, L-systems, noise, template stamping. Deps: `voxel-core`.
- `voxel-builder` (Composer, Planned) — interactive editing, import/export (BlockBench, MagicaVoxel, glTF). Deps: `voxel-core`.
- `voxel-builder-stride` (Bridge, Planned) — Stride bridge.
- `voxel-builder-godot` (Bridge, Planned) — Godot bridge.
- `voxel-builder-unity` (Bridge, Planned) — Unity bridge.
- Cross-references: §12.7.

### Sprite Domain

- `sprite-theory` (Theory, Complete) — camera math, atlas packing (MaxRects), mirror optimization, normal maps (Sobel), sprite sheet JSON.
- `sprite-composer` (Composer, Planned) — engine-agnostic 3D-to-2D capture orchestrator.
- `sprite-composer-stride` (Bridge, Planned) — Stride bridge (render-to-texture capture).
- Cross-references: §12.8.

### Behavior Domain

- `behavior-expressions` (Theory, Complete) — expression parsing, variable resolution, runtime evaluation.
- `behavior-compiler` (Storyteller-equivalent, Complete) — multi-phase ABML compilation, A* GOAP planner, stack-based bytecode interpreter.
- Cross-references: §6, §12.9.

### Connectivity Infrastructure

- `core` (Infrastructure, Complete) — BannouJson, ApiException, base events, `IBannouEvent`.
- `client` (Infrastructure, Complete) — WebSocket client, binary protocol, typed proxies.
- `client-voice` (Infrastructure, Complete) — P2P voice (WebRTC 1-5 peers) + scaled voice (SIP/RTP via Kamailio 6+).
- `server` (Infrastructure, Complete) — generated mesh clients for service-to-service calls.
- `protocol` (Infrastructure, Complete) — binary WebSocket protocol specification (31-byte headers).
- `transport` (Infrastructure, Complete) — game message transport (LiteNetLib UDP, MessagePack DTOs).
- `bundle-format` (Infrastructure, Complete) — `.bannou` bundle format specification.

### Asset Pipeline Infrastructure

- `asset-bundler` (Infrastructure, Complete) — engine-agnostic bundling pipeline.
- `asset-bundler-stride` (Bridge, Complete) — Stride asset compilation.
- `asset-bundler-godot` (Bridge, Complete) — Godot format processing.
- `asset-loader` (Infrastructure, Complete) — async download, LRU cache, bundle registry.
- `asset-loader-client` (Bridge, Complete) — WebSocket-based asset source.
- `asset-loader-server` (Bridge, Complete) — mesh-based asset source.
- `asset-loader-stride` (Bridge, Complete) — Stride type loaders.
- `asset-loader-godot` (Bridge, Complete) — Godot type loaders.

### TypeScript Infrastructure

- `@beyondimmersion/bannou-core` (Infrastructure, Complete) — shared types for TS.
- `@beyondimmersion/bannou-client` (Infrastructure, Complete) — TypeScript WebSocket client.

### Unreal Helpers (Infrastructure, Generated)

- `BannouProtocol.h`, `BannouTypes.h`, `BannouEnums.h`, `BannouEndpoints.h`, `BannouEvents.h` — auto-generated C++ headers per `make generate-unreal-sdk`.

## Cross-References

Each SDK entry lists which sections discuss it:

- `music-theory`, `music-storyteller` — §12.3, referenced from §11 (cinematic-music coordination).
- `storyline-theory`, `storyline-storyteller` — §§7, 12.4.
- `behavior-compiler`, `behavior-expressions` — §§6, 12.9.
- (...and so on)

## Research Sources

- `docs/guides/SDK-OVERVIEW.md` — the existing comprehensive SDK catalog.
- `docs/guides/MUSIC-SYSTEM.md` — music SDKs detailed.
- `docs/guides/STORY-SYSTEM.md` — storyline SDKs detailed.
- `docs/guides/BEHAVIOR-SYSTEM.md` — behavior SDKs detailed.
- `docs/guides/SCENE-SYSTEM.md` — scene SDKs detailed.
- `docs/guides/ASSET-SDK.md` — asset pipeline SDKs detailed.
- `docs/planning/CINEMATIC-SYSTEM.md` — cinematic SDK design.
- `docs/planning/SPRITE-COMPOSER-SDK.md` — sprite SDK design.
- `docs/planning/VOXEL-BUILDER-SDK.md` — voxel SDK design.
- `docs/planning/COUNTERPOINT-COMPOSER-SDK.md` — counterpoint SDK design.
- `docs/sdks/*.md` and `docs/sdks/maps/*.md` — per-SDK deep dives and implementation maps.

## Writing Guidance

- Table-heavy. Each SDK gets a row with status, layer, dependencies, consumers.
- Concise purpose descriptions.
- Every SDK in `sdks/` and planned future SDKs must appear.
- Cross-references to document sections must be comprehensive.

## What This Appendix Must NOT Do

- Must not duplicate `docs/guides/SDK-OVERVIEW.md` — reference it instead.
- Must not provide deep-dive content (refer to `docs/sdks/`).
- Must not omit any SDK from the `sdks/` directory or any planned future SDK.

---

*This document is a living guide. It is complete only when every section above is written. See [planning/VISION-PROGRESS.md](planning/VISION-PROGRESS.md) for current implementation status of the systems this document describes.*

---

*This document is a living guide. It is complete only when every section above is written. See [planning/VISION-PROGRESS.md](planning/VISION-PROGRESS.md) for current implementation status of the systems this document describes.*
