# Scene Loader SDK: Runtime Scene Instantiation for Game Engines

> **Type**: Design
> **Status**: Draft (Defenders-authored problem statement + first-consumer requirements; Developer expanding into full plan)
> **Created**: 2026-04-21
> **Last Updated**: 2026-04-21
> **Related SDKs**: scene-composer (authoring-time counterpart), asset-loader (runtime asset-bytes companion), asset-bundler (authoring-time asset packaging)
> **Related Repos**: defenders-kb (first consumer surfacing the architectural gap)
> **Primary Consumer**: Defenders of Ba'hara — single-player embedded-mode Bannou consumer; first game using Bannou without a remote server tier
> **Services**: `scene-loader` (new, engine-agnostic), `scene-loader-stride` (new, Stride bridge — Defenders' immediate need), `scene-loader-godot` (new, Godot bridge, future)

## Summary

A new SDK family filling the runtime half of Bannou's scene pipeline. Where **scene-composer** handles authoring (edit-time scene-graph editing with undo/redo / gizmos / selection) and **asset-loader** handles runtime asset-bytes loading, **scene-loader** handles **runtime scene-structure instantiation** — reading Bannou scene documents and materializing them as engine-native entity hierarchies in a shipping-game binary without editor baggage.

No implementation exists yet. This document captures the problem statement and Defenders-as-first-consumer requirements; the full architectural + phased-implementation plan is Developer-authored from here.

---

## Part 1: The Problem

### The Architectural Gap

Bannou's SDK layering for game-authored content has this shape today:

| Domain | Authoring-time | Runtime |
|---|---|---|
| Assets (meshes, textures, animations, bundles) | `asset-bundler` + `asset-bundler-godot` + `asset-bundler-stride` | `asset-loader` + `asset-loader-client` + `asset-loader-server` + `asset-loader-godot` + `asset-loader-stride` |
| Scenes (scene-graph structure, entity placements, spatial markers) | `scene-composer` + `scene-composer-godot` + `scene-composer-stride` | **MISSING** |
| Cutscenes | `cinematic-composer` (planned per [CINEMATIC-PHASE-1-COMPOSER-SDK.md](../plans/CINEMATIC-PHASE-1-COMPOSER-SDK.md)) | `CinematicInterpreter` + `CutsceneSession` (existing, in `behavior-compiler` + `lib-behavior`) |
| Sprites | `sprite-composer` (planned per [SPRITE-COMPOSER-SDK.md](SPRITE-COMPOSER-SDK.md)) + `sprite-theory` (implemented) | game-side sprite-renderer code (runtime responsibility is game-side by design) |

The runtime scene-structure counterpart — a library that reads Bannou scene documents at shipping-game runtime and instantiates the entity hierarchy into the target engine — is missing. Per [SCENE-SYSTEM.md § Integration Patterns](../guides/SCENE-SYSTEM.md) (lines 610-618, Game Engine Integration pattern), this step is acknowledged as "game engine integrator does this themselves" with no SDK support:

```
Game Engine Integration
1. Load scene via POST /scene/get
2. Instantiate geometry using asset references and transforms   ← NO SDK FOR THIS STEP
3. Notify Bannou via POST /scene/instantiate
4. Other services react to scene.instantiated event
```

`/scene/instantiate` is a **notification endpoint** called AFTER instantiation; it does not perform instantiation.

### Why the Gap Was Tolerable and Is No Longer

In Bannou's original remote-client/server consumer model, the "game engine geometry instantiation" step was a **server-side** responsibility. The server hosted the authoritative game world; clients received view state. Each consumer game wrote its own server-side instantiation code — acceptable because (a) one consumer = one implementation, and (b) the server tier naturally houses this responsibility alongside lib-scene.

**Embedded mode** (per [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md)) collapses this. In embedded consumption, the game is both client and server — there is no separate server tier to host the instantiation code. The game's client code must do it, in the target engine's runtime, without a shared SDK.

Defenders of Ba'hara is the first embedded-mode consumer (see `defenders-kb/24-backend/BANNOU-EMBEDDED.md` + `defenders-kb/00-meta/DECISIONS.md` D2 / D128 / D135). Defenders surfaces this gap per the co-evolution pattern.

### Why scene-composer-stride at Runtime Is Not the Right Answer

scene-composer-stride's bridge methods (`CreateEntity`, `SetEntityAssetAsync`, `SetEntityParent`, `DestroyEntity`) are engine-general enough to serve runtime instantiation mechanically. But:

- scene-composer is framed in [`sdks/scene-composer/README.md`](../../sdks/scene-composer/README.md) as "a complete solution for building scene editors."
- The core orchestrator carries `CommandStack` (undo/redo), `SelectionManager`, `BeginCompoundOperation`, `GizmoController`, `SceneValidator` — editor concerns.
- The Stride bridge has `SetEntitySelected` (selection outline), `RenderGizmo`, `PickNode` (mouse-ray picking), `PickNodesInRect` (rectangular selection) — editor concerns.
- Shipping these in a shipping-game binary adds unneeded code + runtime init cost + maintenance coupling.
- Conflicts with the architectural discipline that authoring tools stay separate from runtime surfaces (same discipline applied to sprite-composer + `defenders-sprite-composer` as Phase 4 per SPRITE-COMPOSER-SDK.md).

The right answer is a purpose-built runtime SDK: no editor surface, shipping-game-optimized, pairing with asset-loader for the runtime half of the pipeline.

---

## Part 2: Defenders as First Consumer — Requirements

Defenders-side requirements captured at Q99 resolution (`defenders-kb/00-meta/QUESTIONS-RESOLVED.md` Q99) and committed via D135 (`defenders-kb/00-meta/DECISIONS.md` D135). These feed the Bannou-side SDK design as first-consumer input, analogous to the sprite-composer Part 11 + cinematic-composer Phase 1 Defenders section patterns already established.

### 2.1: Runtime-only surface (hard requirement)

**No editor concerns in the SDK surface.** The scene-loader SDK must not carry command stack / undo-redo, selection manager, multi-select, compound operations, gizmo controller, gizmo renderer, mouse-ray picking, rectangular selection, or validation UI. These stay in scene-composer for authoring.

Runtime-only operations the bridge must cover:
- Instantiate a scene-graph node (create engine entity, apply transform, parent into hierarchy, assign asset)
- Tear down a scene instance (deterministic destroy + entity removal)
- Subscribe to / publish `scene.instantiated` / `scene.destroyed` events per lib-scene's event topology as appropriate for embedded-mode consumption

### 2.2: Pairs with asset-loader for the runtime asset half

Scene-loader reads scene **structure** (node graph, transforms, parent-child, asset references by id). For the actual asset **bytes** (meshes, textures, animations, bundles), scene-loader-stride delegates to `asset-loader-stride`. This matches the existing layering where asset-bundler-stride (authoring) pairs with asset-loader-stride (runtime).

**Concrete**: `IScene­LoaderBridge.SetEntityAsset(entity, assetRef)` implementation in `StrideSceneLoaderBridge` internally calls `IAssetLoader.LoadAsync(assetRef, ct)` (or equivalent) from asset-loader-stride.

### 2.3: Engine-agnostic core + engine-specific bridges

Matches the existing scene-composer + asset-loader patterns:

- `scene-loader` (new) — engine-agnostic core: scene-document reader, scene-graph walker, `ISceneLoaderBridge` contract
- `scene-loader-stride` (new) — `StrideSceneLoaderBridge` implementing the bridge contract against Stride's `Scene` / `Entity` / component model
- `scene-loader-godot` (new, future) — `GodotSceneLoaderBridge` implementing against Godot's scene tree
- No Unity bridge needed for Defenders (D56 locks Stride)

### 2.4: Async loading with `CancellationToken`

Consistent with [BANNOU-EMBEDDED.md](BANNOU-EMBEDDED.md) + D128 Bannou-as-library pattern. Shape (illustrative — Developer picks exact signatures):

```csharp
Task<SceneInstanceHandle> InstantiateAsync(
    SceneDocument document,
    ISceneLoaderBridge bridge,
    CancellationToken ct);
```

Scene-document retrieval via either (a) lib-scene's `/scene/get` endpoint (embedded DI-resolved invocation per D128) or (b) direct `asset-loader-stride` bundle-load (for scene documents packaged as assets). Consumer (not the SDK) picks the retrieval path.

### 2.5: Whole-scene load model acceptable at Phase 1

Defenders' locked content decomposes into bounded scene sizes (`defenders-kb/11-levels/act-1/2/3/README.md`): each scene has a wave / outpost structure per D94 + D46, and wave-level enemies are spawned at runtime via wave-authoring rather than being part of the initial scene document. Scene documents are bounded.

**Streaming-within-a-scene is not required at Phase 1.** If future Defenders scope (or other embedded consumers) need it, raise as a separate Phase-2+ design question.

### 2.6: Explicit teardown semantics

`ISceneLoader.DestroyInstance(instanceHandle)` tears down all entities associated with a scene instance deterministically. Defenders controls scene-switch lifecycle (destroy current before loading next, or concurrent multiple-scene-instance for specific cases like cutscene backdrop + live scene). Matches the Defenders convention captured in D135.

### 2.7: Hot-reload hook for dev-time

Optional surface allowing a consumer to force re-load of a scene document at runtime. For Defenders, this lives in Defenders-side editor integrations (not shipping builds). SDK-level implementation: straightforward consequence of async `InstantiateAsync` + `DestroyInstance` — no special "hot-reload" feature required at Phase 1, just a well-documented destroy-then-reload pattern.

### 2.8: Schema compatibility with scene-composer (no baking step)

Scene-loader reads the same scene document format scene-composer emits (`ComposerScene` / `ComposerSceneNode`). No intermediate "runtime scene format" that would introduce a baking step. Honors SCENE-SYSTEM.md principle: **"Scenes exist identically at edit-time and runtime — no baking step, no editor-only concepts, no format conversion."**

**Implication for data-model placement**: the scene-document types may need to move from `scene-composer` core into a shared core package, or be referenced from scene-loader directly. Developer picks during implementation design (see Part 4 open questions).

---

## Part 3: Scope Exclusions

Defenders does NOT consume in Phase 1 (confirming scope):

- **scene-loader-godot / scene-loader-unity** — Defenders uses Stride per D56. Other-engine bridges benefit future Bannou consumers but aren't Defenders' concern.
- **Multi-player scene reconciliation** — Defenders is single-player per D2. Server-authoritative scene state / client-side extrapolation are out of scope.
- **Server-side scene instantiation** (hypothetical `scene-loader-server` if ever planned) — Defenders has no server tier.
- **Runtime scene editing** — scene modifications at shipping-game runtime (player-built structures, destructible environment persistence) are game-side concerns, not scene-loader's. Scene-loader is read + instantiate + teardown; runtime mutation is the game's responsibility.
- **Editor tooling** — scene-composer remains the editor; scene-loader does not duplicate authoring features.

---

## Part 4: Open Questions for Bannou-side Design

Items the Developer expands during full-plan authoring (not Defenders-side decisions):

1. **Data-model placement**: do `ComposerScene` / `ComposerSceneNode` types stay in scene-composer core, move to a shared core package, or get duplicated into scene-loader? Choice affects dependency graph.
2. **lib-scene runtime interaction model**: does scene-loader call `POST /scene/get` via a generated client (embedded DI-resolved per D128) OR does it consume scene documents directly from asset-loader-packaged bundles? Both patterns are viable; both may need to be supported.
3. **`scene.instantiated` event responsibility**: does scene-loader publish the event post-instantiation (satisfying the pattern in SCENE-SYSTEM.md lines 610-618) automatically, or does the calling game code publish it explicitly? Defenders leans "scene-loader publishes automatically" for ergonomic consistency, but this is a Bannou-side design choice.
4. **Asset-loader coupling shape**: scene-loader-stride depends on asset-loader-stride for asset loading. Direct project reference, DI injection, or runtime discovery? Affects assembly layout + testing.
5. **Phased implementation**: Phase 1 could ship just the core + Stride bridge (Defenders' immediate need); Godot bridge in Phase 2. Matches precedent in [SPRITE-COMPOSER-SDK.md](SPRITE-COMPOSER-SDK.md) phased plan.
6. **Hot-reload API surface**: explicit `ReloadAsync(instanceHandle, newSceneDocument)` or just `DestroyInstance(h)` + `InstantiateAsync(newDoc)` sequence? Minor ergonomic call.
7. **Transform / Vector3 primitive reuse**: scene-composer uses double-precision math types (large-world accuracy per the sprite-theory precedent in [SPRITE-COMPOSER-SDK.md § Decision 10.16](SPRITE-COMPOSER-SDK.md)). Does scene-loader match scene-composer's double precision or adopt a different primitive?

---

## Part 5: Relationship to Existing Bannou SDKs

| Relationship | Detail |
|---|---|
| **scene-composer** | Authoring-time counterpart. Scene documents flow from scene-composer (author) to scene-loader (runtime) via the shared scene document format. |
| **asset-loader** | Runtime asset-bytes companion. scene-loader-stride consumes asset-loader-stride for asset loading during scene instantiation. |
| **asset-bundler** | Authoring-time packaging. Scene documents may be packaged as bundle assets loaded by asset-loader at runtime. |
| **lib-scene** (plugin) | Server-side / embedded scene document storage. scene-loader reads documents via `/scene/get` (embedded DI) or via asset-loader (bundle-packaged). |
| **behavior-compiler** / `CinematicInterpreter` | Runtime cutscene playback. Cutscenes play WITHIN scenes loaded by scene-loader (per Defenders D132). Scene-loader instantiates the stage; CinematicInterpreter plays the performance. |
| **mapping** (plugin) | `scene.instantiated` event consumer per [SCENE-SYSTEM.md](../guides/SCENE-SYSTEM.md) — spatial indexing from the event. scene-loader should publish (or cause publication of) this event consistently regardless of consumer context. |
| **actor** (plugin) | `scene.instantiated` event consumer — NPC spawn from marker nodes per SCENE-SYSTEM.md. Same event requirement applies. |

---

## Part 6: Defenders-side References

- `defenders-kb/00-meta/DECISIONS.md` **D135** — "Scene-loader SDK pattern — Defenders commits to scene-loader-stride for runtime scene instantiation; flags Bannou-side SDK gap" (Defenders-side commitment to consuming this SDK family)
- `defenders-kb/00-meta/QUESTIONS-RESOLVED.md` **Q99** — resolution documenting the architectural-gap investigation + evidence
- `defenders-kb/24-backend/BANNOU-EMBEDDED.md` — Defenders' embedded-mode Bannou consumption architecture (D2 + D128 grounds)
- `defenders-kb/11-levels/act-1/act-2/act-3/README.md` — locked Defenders scene content driving the consumer scope (bounded scene sizes, wave-authoring separate from scene structure, scene-composer-authored scene documents as input)

---

## Part 7: Co-evolution Commitment

Defenders becomes the first consumer when the SDK lands. **Phase 1 acceptance criterion**: Defenders' Stride runtime successfully loads a scene-composer-authored scene document and instantiates it into a playable Stride scene hierarchy via scene-loader-stride, without any dependency on scene-composer-stride in the shipping binary.

Additional Phase 1 requirements may emerge during Defenders' first real scene-loading implementation; appended as further decisions in `defenders-kb` referencing this doc + D135.

---

*This document captures the Defenders-first-consumer problem statement and requirements for the Scene Loader SDK family. The Developer expands this into the full architectural + phased-implementation plan on the Bannou side. See `defenders-kb/00-meta/DECISIONS.md` D135 for the Defenders-side commitment.*
