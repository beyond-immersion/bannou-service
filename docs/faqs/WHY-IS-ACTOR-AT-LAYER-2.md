# Why Is Actor at Layer 2 Instead of Layer 4?

> **Short Answer**: Because behavior execution is foundational infrastructure, not an optional feature. If every NPC in the world runs an actor brain -- shopkeepers, guards, farmers, dungeon cores, regional watchers -- then the runtime that executes those brains is as fundamental as characters, realms, or items. The distinction is between the execution engine (foundational) and the content it executes (optional).

---

## The Intuitive Argument for L4

The first reaction is understandable. Actor manages NPC brains. NPC intelligence sounds like a game feature. Game features are L4. Therefore Actor should be L4.

This reasoning would be correct if Actor were "the NPC AI system." But Actor is not the NPC AI system. Actor is the behavior execution runtime. The distinction matters enormously.

---

## Compiler vs. Runtime

Consider an analogy to programming languages:

- **The C# compiler** (Roslyn) transforms source code into IL bytecode. It understands language syntax, performs optimizations, and produces a portable artifact.
- **The .NET CLR** executes that bytecode. It manages memory, handles exceptions, and provides the runtime environment. It does not understand C# syntax -- it executes instructions.

In Bannou:

- **Behavior (L4)** is the compiler. It takes ABML source (the Arcadia Behavior Markup Language), runs a multi-phase compilation pipeline, and produces portable stack-based bytecode. It understands behavior syntax, GOAP planning, and cognition pipeline stages.
- **Actor (L2)** is the runtime. It executes compiled bytecode, manages actor lifecycle (creation, tick scheduling, shutdown), handles deployment modes (local, pooled, auto-scale), and provides the execution environment. It does not understand ABML syntax -- it executes instructions.

You would not put the CLR in an optional feature layer. It is infrastructure. The same logic applies to Actor.

---

## What Depends on Actor

The classification becomes clear when you look at what depends on Actor being available:

**Things that need actors to exist (and thus need Actor at L2 or below):**
- Every NPC in every realm. Shopkeepers need to decide what to stock. Guards need to decide patrol routes. Farmers need to decide what to plant. These are not optional AI features -- they are the baseline behavior of the world.
- Quest NPCs. The Quest service (L2) exposes quest data to actors via the Variable Provider Factory pattern. Quest givers need actor brains to offer and manage quests.
- Game sessions. NPCs participate in game sessions. If Actor were L4, Game Session (L2) could not assume actors exist.

**Things that are truly optional L4 features:**
- Personality evolution (Character Personality). An actor can run without personality data -- it just has fewer variables available.
- Encounter memory (Character Encounter). An actor can run without encounter history -- NPCs just won't remember past interactions.
- Historical backstory (Character History). An actor can run without backstory data -- NPCs just won't reference their past.
- Dynamic behavior loading from the Asset service (Puppetmaster). An actor can run with compiled-in behaviors -- it just can't hot-swap them at runtime.

The pattern is: Actor provides the execution runtime that is always on. L4 services enrich that runtime with optional data and capabilities. If you disable Character Personality, NPCs still function -- they just don't have personality-driven behavior variations. If you disable Actor, NPCs don't function at all.

---

## The Variable Provider Factory Pattern

The strongest architectural argument for Actor at L2 is how it receives data from L4 services. If Actor were L4, it would simply inject `ICharacterPersonalityClient`, `ICharacterEncounterClient`, and `ICharacterHistoryClient` as constructor dependencies. Simple, direct, and completely wrong for a living world platform.

With Actor at L2, it cannot depend on L4 services. Instead, it uses the Variable Provider Factory pattern:

1. **Actor (L2) defines an interface** (`IVariableProviderFactory`) in shared code (`bannou-service/`).
2. **L4 services implement the interface** -- Character Personality provides `${personality.*}` variables, Character Encounter provides `${encounters.*}` variables, Character History provides `${backstory.*}` variables.
3. **DI discovers implementations at runtime** via `IEnumerable<IVariableProviderFactory>` injection.
4. **Graceful degradation** -- if a provider fails or isn't deployed, the actor continues with fewer variables.

This design has three critical properties:

- **L4 is genuinely optional.** Deploy without Character Personality and actors still run. They have fewer behavior variables available, and ABML expressions referencing `${personality.*}` evaluate to defaults, but the world functions.
- **New variable sources can be added without touching Actor.** A future L4 service (say, "Character Reputation") just implements `IVariableProviderFactory` and registers via DI. Actor discovers it automatically.
- **The dependency direction is correct.** L4 services depend on the shared interface. Actor depends on the shared interface. Neither depends on the other.

If Actor were L4, this entire pattern would be unnecessary -- and the ability to deploy game foundations without optional AI enrichment features would be lost.

---

## Deployment Mode Implications

Actor at L2 enables meaningful deployment configurations:

```bash
# Minimal game backend: worlds with NPCs that follow basic behaviors
BANNOU_ENABLE_GAME_FOUNDATION=true   # L2: character, realm, actor, etc.
# Result: NPCs exist and act. No personality evolution, no encounter memory,
# no dynamic behavior loading. But the world is alive.

# Full game deployment: NPCs with rich AI
BANNOU_ENABLE_GAME_FOUNDATION=true   # L2
BANNOU_ENABLE_GAME_FEATURES=true     # L4: personality, encounters, behavior compiler
# Result: NPCs have evolving personalities, remember encounters, and can
# hot-swap behaviors at runtime.
```

If Actor were L4, the minimal deployment would have no NPC behavior at all. The world would be populated with characters that stand still and do nothing. That is not a "minimal game backend" -- that is a broken one.

---

## The Scale Argument

Bannou targets 100,000+ concurrent AI NPCs. At that scale, the actor runtime is not a feature -- it is infrastructure on par with state management and messaging. Actor supports multiple deployment modes (local, pool-per-type, shared-pool, auto-scale) precisely because it needs to scale as aggressively as the rest of the foundational infrastructure.

The Orchestrator service (L3) manages processing pools for actor workers. These pools are infrastructure-level resource management -- spinning up and tearing down containers to handle NPC load. Treating the actor runtime as an optional feature while simultaneously requiring infrastructure-level scaling support for it would be architecturally incoherent.

---

## The Historical Context

Actor was originally at L4. It was moved to L2 specifically because the living world thesis requires behavior execution to be foundational. The change was documented in SERVICE-HIERARCHY.md v2.5 and prompted the addition of the Variable Provider Factory pattern documentation.

The move was not controversial once the distinction between "execution runtime" and "AI content" was clear. The runtime is L2. The content that enriches the runtime (personality, encounters, history, dynamic behavior loading) remains L4. This is the same distinction the JVM makes between the runtime (foundational) and the libraries you run on it (optional).
