# Why Is Puppetmaster a Separate Service from Behavior?

> **Short Answer**: Behavior compiles ABML and runs the GOAP planner -- it is a pure computation service. Puppetmaster orchestrates which behaviors run where, loads them dynamically from the Asset service, manages Regional Watchers, and coordinates encounters. They solve fundamentally different problems: Behavior answers "what should this NPC do?", Puppetmaster answers "what should be happening in this region?"

---

## The Hierarchy Constraint That Forces the Split

This is the architectural reason, and it is sufficient on its own.

Actor (L2) executes behavior bytecode. To hot-reload behaviors at runtime, someone needs to fetch behavior documents from the Asset service (L3). But Actor (L2) **cannot depend on Asset (L3)** -- L2 services cannot depend on L3 services. This is not a guideline; it is enforced by the `ServiceHierarchyValidator`.

Behavior (L4) compiles ABML into bytecode. It could theoretically also handle loading from Asset. But that would mean the compilation service is also the runtime loading service, the Regional Watcher manager, and the encounter coordinator. These are four distinct responsibilities with different scaling profiles.

Puppetmaster (L4) exists specifically to bridge Actor (L2) and Asset (L3). It implements `IBehaviorDocumentProvider`, loading compiled behaviors from Asset and making them available to Actor via the provider chain pattern. Actor discovers the provider at runtime without knowing Puppetmaster exists.

Without Puppetmaster, one of two things breaks:
- Actor cannot dynamically load behaviors (no hot-reload, no runtime behavior changes)
- Actor depends on Asset directly (hierarchy violation)

---

## Different Responsibilities, Different Scaling

### Behavior: Pure Computation

The Behavior service does two things:
1. **Compile ABML** -- Transform YAML behavior documents into stack-based bytecode through a multi-phase compiler.
2. **Run GOAP planner** -- Search action spaces for goal-achieving sequences using A* search.

Both are CPU-bound, stateless, pure computations. Given inputs, they produce deterministic outputs. Behavior needs no persistent state, no event subscriptions, and no coordination with other running processes. It scales by adding compute capacity.

### Puppetmaster: Orchestration

The Puppetmaster service manages ongoing, stateful processes:

1. **Regional Watchers** -- Long-running "god" actors that monitor event streams for their region (Moira/Fate, Thanatos/Death, Ares/War, etc.), evaluate narrative seeds from the Storyline service, and decide when and where to orchestrate new scenarios. Each watcher has persistent state: current regional narrative assessment, pending seeds, cooldowns, aesthetic preference weightings.

2. **Dynamic Behavior Loading** -- Fetching compiled ABML documents from the Asset service, caching them, managing version invalidation, and providing them to Actor via `IBehaviorDocumentProvider`. This requires awareness of what behaviors exist, what versions are current, and when to invalidate cached bytecode.

3. **Encounter Coordination** -- When a Regional Watcher decides to orchestrate a scenario, Puppetmaster coordinates the entities involved: which NPCs participate, what behavior documents they switch to, when the encounter begins and ends. This is coordination logic, not computation.

4. **Resource Snapshot Caching** -- Event Brain actors (used for cinematic combat exchanges) need cached snapshots of character capabilities and environmental affordances. Puppetmaster manages these caches so the Actor runtime can access them without expensive per-tick service calls.

These are stateful, event-driven, coordination-heavy responsibilities that scale by adding orchestration capacity -- fundamentally different from Behavior's compute scaling.

---

## The Regional Watcher Problem

Regional Watchers are the strongest argument for Puppetmaster's existence as a distinct service.

A Regional Watcher is a long-running process that:
- Subscribes to event streams for a geographic region
- Maintains a mental model of the region's current narrative state
- Receives narrative seeds from the Storyline service
- Decides which seeds to activate based on aesthetic preferences and current state
- Coordinates the activation by assigning behaviors to actors and managing encounter lifecycle

If Regional Watchers lived in the Behavior service, the compiler and planner would share a process with long-running event-consuming orchestrators. A burst of compilation requests (a designer uploading 50 new behavior documents) would compete for resources with the Regional Watchers that are monitoring event streams in real-time. These have opposite scaling needs: compilation is bursty and CPU-heavy; event monitoring is steady and IO-heavy.

If Regional Watchers lived in the Actor service, an L2 foundational service would be making decisions about narrative orchestration -- an L4 concern. The Actor service should not know or care about narrative aesthetics, scenario selection, or encounter lifecycle. It executes bytecode. Period.

Puppetmaster is where Regional Watchers belong because it is the L4 service responsible for answering "what should be happening in this world" -- the orchestration layer above both the behavior compiler and the behavior runtime.

---

## The Hot-Reload Pipeline

The full pipeline for dynamically updating NPC behavior at runtime:

1. A designer uploads a new ABML document to the **Asset service** (L3).
2. **Puppetmaster** (L4) detects the new version (via event subscription or polling).
3. Puppetmaster fetches the document from Asset and sends it to **Behavior** (L4) for compilation.
4. Behavior compiles the ABML and returns bytecode.
5. Puppetmaster caches the compiled bytecode and updates its `IBehaviorDocumentProvider`.
6. **Actor** (L2) discovers the updated bytecode on the next behavior load via the provider.
7. Active NPCs running the old behavior switch to the new bytecode.

If Behavior and Puppetmaster were one service, steps 2-5 would be internal, which sounds simpler. But it would mean the compilation service also needs Asset service integration, event subscriptions for version changes, bytecode caching with invalidation, and the provider interface implementation. That is feature creep that turns a clean computation service into an orchestration service.

The split keeps Behavior focused: "give me ABML, I give you bytecode." Puppetmaster handles everything else about getting the ABML to Behavior and the bytecode to Actor.

---

## Could They Be Merged Later?

Technically, yes. If scaling evidence shows that the Behavior and Puppetmaster services never need independent scaling and the orchestration overhead of two services outweighs the architectural clarity, they could be merged into a single L4 service.

But the hierarchy constraint (Actor at L2 needing Asset at L3) would still require an `IBehaviorDocumentProvider` implementation somewhere in L4. And the computation vs. orchestration distinction would still exist within the merged service. The merge would save one service boundary at the cost of mixing two scaling profiles and two responsibility domains.

For a system targeting 100,000+ concurrent NPCs with dynamic behavior loading and Regional Watcher orchestration, the split is architecturally sound. The services have different consumers (Behavior's consumers are anything that needs compilation; Puppetmaster's consumer is the Actor runtime), different triggers (Behavior responds to compilation requests; Puppetmaster responds to world events), and different failure consequences (Behavior failure means no new compilations; Puppetmaster failure means no dynamic loading or orchestration, but existing behaviors continue running).
