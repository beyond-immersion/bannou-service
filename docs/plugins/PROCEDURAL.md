# Procedural Plugin Deep Dive

> **Plugin**: lib-procedural
> **Schema**: schemas/procedural-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: procedural-templates (MySQL), procedural-jobs (MySQL), procedural-cache (Redis), procedural-lock (Redis)
> **Status**: Pre-implementation (architectural specification)
> **Short**: Procedural textured 3D asset generation via headless Houdini Digital Assets + SideFX Labs Substance plugin, with deterministic output

---

## Overview

On-demand procedural textured 3D asset generation service (L4 GameFeatures) using headless Houdini Digital Assets (HDAs) — self-contained parametric procedural tools packaged as `.hda` files with exposed parameter interfaces (sliders, menus, toggles, ramps) that generate infinite geometry variations from a single authored template — as parametric generation templates, optionally paired with Substance smart materials (`.sbsar` files) loaded through Houdini's SideFX Labs Substance plugin for parametric material generation. A thin orchestration layer that composes existing Bannou primitives (Asset service for HDA/sbsar storage and output bundling, Orchestrator for Houdini worker pool management) to deliver procedural textured-mesh generation as an API. Game-agnostic — the service knows nothing about what it generates, it executes HDAs (with optional Substance material baking inside the HDA) and returns textured geometry. Internal-only, never internet-facing.

---

## Why This Service Exists

**The authoring-to-runtime bridge**: lib-procedural bridges content authoring and runtime generation. Artists author HDAs (parametric procedural tools with exposed controls) in Houdini's GUI and smart materials (`.sbsar`) in Substance Designer. Both are uploaded to the Asset service as templates. At runtime, any service (dungeon cores exercising domain_expansion, regional watchers sculpting terrain, NPC builders constructing buildings, world seeding during realm creation) can request generation by providing a template ID, parameters, and a seed. The same HDA with different parameters produces dramatically different geometry; the same smart material with different parameters produces dramatically different textures; combined via Houdini's Substance COP integration, a single HDA + sbsar pair produces infinite **textured** variations from two authored primitives.

**The formal-theory / procedural-composition pattern**: This is the material analog of MusicTheory + MusicStoryteller and StorylineTheory + StorylineStoryteller. Smart materials are structural primitives (a compressed design space with parameter axes); procedural composition at runtime selects parameters per game-state context. Authoring is human-creative (Substance Designer's node graph — one-time, per primitive); consumption is LLM/game-logic-friendly (picking parameter values per biome/corruption/age/story-state — unbounded at runtime).

**Composability**: Template storage (HDAs + sbsar smart materials) and output bundling are Asset (L3). Worker pool lifecycle is Orchestrator (L3). Job status tracking is internal. Generation execution is delegated to headless Houdini containers running hwebserver. lib-procedural orchestrates the pipeline: receive request, fetch HDA (and any referenced sbsar) from Asset, acquire worker from Orchestrator, execute HDA with Substance material baking inline, upload result to Asset, optionally bundle, return reference.

**Deterministic generation**: Same template + same parameters + same seed = identical output. This enables Redis-cached generation results (keyed by hash of template_id + parameters + seed), reproducible dungeon layouts, and predictable world seeding. The cache key is canonical -- if the same generation has been requested before, the cached result is returned without invoking Houdini.

### The Content Generation Gap

Bannou can manage, store, version, spatially index, and compose 3D assets. It cannot **create** them. Every piece of geometry and every texture in the system was authored by a human and uploaded. This creates a bottleneck:

- **Dungeons** that grow new chambers need geometry AND material variations (weathered stone, corrupted surfaces, living walls)
- **Terrain** for procedurally-generated regions needs heightmaps, meshes, and biome-appropriate textures
- **Buildings** constructed by NPC builders need facade variations with culturally-appropriate materials
- **Vegetation** scattered across regions needs species-appropriate variations and seasonal material states
- **World seeding** during realm creation needs massive quantities of varied textured geometry

lib-procedural closes this gap on both axes simultaneously. With it, Bannou becomes a platform that both **manages** and **generates** textured content on demand.

### Consumers

| Consumer | Use Case | Parameters |
|----------|----------|-----------|
| **lib-dungeon** (L4, future) | Chamber generation when dungeon_core exercises `domain_expansion` | Dungeon personality, seed, growth phase, room type, connection points |
| **lib-puppetmaster** (L4) | Terrain sculpting by regional watchers, environmental changes from world events | Biome, erosion, event type (fire/flood/war), intensity |
| **lib-scene** (L4) | Batch generation of scene element variations for placement | Element type, style, weathering, LOD count |
| **Realm seeding** (L2) | Initial world geometry generation during realm creation | Region coordinates, biome map, seed, resolution |
| **NPC builders** (L2, Actor) | Building construction triggered by NPC behavior | Style, floors, materials, cultural context |
| **lib-gardener** (L4) | Void POI generation, scenario environment creation | Scenario type, difficulty, theme, player preferences |

### The Dungeon Integration Specifically

When a dungeon core's `dungeon_core` seed growth in `domain_expansion.*` crosses capability thresholds, it gains the ability to grow new chambers. The generation flow:

```
Dungeon Actor exercises domain_expansion capability
 |
 v
lib-dungeon calls lib-procedural with:
 - template_id: dungeon chamber HDA (stored in Asset service)
 - parameters: {
 personality: "martial", (from dungeon_core seed metadata)
 seed: 42, (deterministic)
 room_type: "arena", (from dungeon cognition)
 connection_points: [...], (from existing layout in Mapping)
 growth_phase: "Awakened", (from dungeon_core seed phase)
 style_params: {...} (from personality_effects configuration)
 }
 |
 v
lib-procedural:
 1. Check cache (hash of template + params + seed)
 2. If miss: acquire Houdini worker from Orchestrator pool
 3. Download HDA from Asset service
 4. Execute HDA with parameters
 5. Upload generated geometry to Asset service
 6. Bundle as .bannou (LZ4 compressed)
 7. Cache result
 |
 v
lib-dungeon receives asset/bundle reference
 |
 v
Registers new chamber in:
 - lib-mapping (spatial boundaries, room connectivity, affordances)
 - lib-scene (visual composition, decorations)
 - lib-save-load (persistent construction state)
```

**Deterministic seeds enable reproducible dungeons**: A dungeon that grew chamber X with seed 42 will always get the same geometry. This means dungeon layouts are reproducible from their growth history -- the save-load state doesn't need to store the full geometry, just the generation parameters.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Template metadata (MySQL), job records (MySQL), generation cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for concurrent generation deduplication |
| lib-messaging (`IMessageBus`) | Publishing generation lifecycle events (job.started, job.completed, job.failed) |
| `ITelemetryProvider` | Telemetry span instrumentation for all async methods (L0) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-asset (`IAssetClient`) | HDA template storage and retrieval, generated output upload, bundle creation (L3) | All template registration, generation, and introspection endpoints return error. Service starts but cannot perform its core function. |
| lib-orchestrator (`IOrchestratorClient`) | Houdini worker pool management -- acquire/release workers (L3) | Worker acquisition fails. Generation requests return error. Cache reads still work. |
| lib-analytics (`IAnalyticsClient`) | Generation metrics (template popularity, generation times, cache hit rates) | Metrics not published |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| **lib-dungeon** (L4, future) | Chamber generation when domain_expansion capabilities unlock |
| **lib-puppetmaster** (L4) | Environmental changes from regional watcher decisions |
| **lib-gardener** (L4) | Void POI and scenario environment generation |
| **lib-scene** (L4) | Batch generation of scene element variations |
| Realm seeding workflows | Initial world geometry during realm creation |

All dependents are L4 (or L2 workflows using L4). lib-procedural is L4 and depends on L3 (Asset, Orchestrator) as soft dependencies with graceful degradation, and L0 (state, messaging) as hard dependencies. Per SERVICE-HIERARCHY.md, L4→L3 dependencies require runtime resolution via `IServiceProvider.GetService<T>()` with null checks -- if either L3 service is disabled, generation capability is unavailable but the service does not crash at startup.

---

## State Storage

### Template Registry Store
**Store**: `procedural-templates` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tmpl:{templateId}` | `ProceduralTemplateModel` | Template metadata: asset ID of HDA, parameter schema (introspected), display name, description, category, output format, estimated generation time |
| `tmpl-code:{category}:{code}` | `ProceduralTemplateModel` | Code-based lookup within category (e.g., `dungeon:arena`, `terrain:alpine`) |

### Job Store
**Store**: `procedural-jobs` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `job:{jobId}` | `GenerationJobModel` | Job record: template ID, parameters, seed, status, worker ID, start time, completion time, output asset ID, output bundle ID, error details |

### Generation Cache
**Store**: `procedural-cache` (Backend: Redis, prefix: `proc:cache`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `gen:{hash}` | `CachedGenerationResult` | Cached generation result keyed by SHA256(templateId + canonicalized parameters + seed). Stores: output asset ID, output bundle ID, generation time. TTL configurable. |

### Distributed Locks
**Store**: `procedural-lock` (Backend: Redis, prefix: `proc:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `gen:{hash}` | Generation deduplication -- prevent concurrent identical generation requests |
| `tmpl:{templateId}` | Template mutation lock (register, update, deprecate) |

---

## Events

### Published Events

**Lifecycle events** (auto-generated via `x-lifecycle` with `topic_prefix: procedural`):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `procedural.template.created` | `ProceduralTemplateCreatedEvent` | New HDA template registered |
| `procedural.template.updated` | `ProceduralTemplateUpdatedEvent` | Template metadata updated, or template deprecated (with `changedFields` including deprecation fields) |
| `procedural.template.deleted` | `ProceduralTemplateDeletedEvent` | Schema-generated but never published (Category B -- templates persist forever) |

**Custom events** (job state machine transitions, not CRUD lifecycle):

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `procedural.job.queued` | `ProceduralJobQueuedEvent` | Generation job accepted and queued |
| `procedural.job.started` | `ProceduralJobStartedEvent` | Houdini worker acquired, generation beginning |
| `procedural.job.completed` | `ProceduralJobCompletedEvent` | Generation successful, output uploaded to Asset service |
| `procedural.job.failed` | `ProceduralJobFailedEvent` | Generation failed (timeout, HDA error, worker unavailable) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `asset.deleted` | `HandleAssetDeletedAsync` | If deleted asset is an HDA template, deprecate the template registration (AUDIT:NEEDS_DESIGN -- see DC#6 below: should this use lib-resource x-references instead of event subscription per tenets?) |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `HoudiniPoolName` | `PROCEDURAL_HOUDINI_POOL_NAME` | `houdini-generation` | Orchestrator processing pool name for Houdini workers |
| `HoudiniWorkerPort` | `PROCEDURAL_HOUDINI_WORKER_PORT` | `8008` | Port for hwebserver API on Houdini workers |
| `HoudiniWorkerTimeoutSeconds` | `PROCEDURAL_HOUDINI_WORKER_TIMEOUT_SECONDS` | `300` | Maximum time to wait for worker acquisition |
| `GenerationTimeoutSeconds` | `PROCEDURAL_GENERATION_TIMEOUT_SECONDS` | `120` | Maximum time for a single generation request |
| `BatchMaxVariations` | `PROCEDURAL_BATCH_MAX_VARIATIONS` | `100` | Maximum variations per batch request |
| `CacheEnabled` | `PROCEDURAL_CACHE_ENABLED` | `true` | Whether to cache generation results |
| `CacheTtlSeconds` | `PROCEDURAL_CACHE_TTL_SECONDS` | `86400` | TTL for cached generation results (default: 24 hours) |
| `DefaultOutputFormat` | `PROCEDURAL_DEFAULT_OUTPUT_FORMAT` | `glb` | Default output format when not specified |
| `MaxConcurrentGenerations` | `PROCEDURAL_MAX_CONCURRENT_GENERATIONS` | `10` | Maximum concurrent generation jobs across all workers |
| `DistributedLockTimeoutSeconds` | `PROCEDURAL_DISTRIBUTED_LOCK_TIMEOUT_SECONDS` | `30` | Timeout for distributed lock acquisition |
| `CreateBundleByDefault` | `PROCEDURAL_CREATE_BUNDLE_BY_DEFAULT` | `true` | Whether to auto-bundle generated assets in .bannou format |

---

## API Endpoints (Implementation Notes)

### Template Management (4 endpoints)

All endpoints require `developer` role. Templates are **Category B** entities (instances -- jobs and cached results -- persist independently with the template's ID). Category B means: deprecation is one-way (no undeprecate), there is no delete endpoint, and deprecated templates remain readable forever.

- **RegisterTemplate** (`/procedural/template/register`): Downloads HDA from Asset service, introspects parameter schema via Houdini worker, stores template metadata with parameter definitions. Validates HDA is loadable. MUST check that the template code is not already taken.
- **GetTemplate** (`/procedural/template/get`): Returns template metadata including full parameter schema and deprecation state.
- **ListTemplates** (`/procedural/template/list`): Paged query with optional category filter. MUST support `includeDeprecated` parameter (default: `false`).
- **DeprecateTemplate** (`/procedural/template/deprecate`): Deprecate template (Category B). Active jobs using this template continue; new generation requests rejected with `BadRequest`. Idempotent -- returns `OK` if already deprecated. Uses triple-field deprecation model (`IsDeprecated`, `DeprecatedAt`, `DeprecationReason`). State change published as `procedural.template.updated` with `changedFields`.

### Generation (4 endpoints)

All endpoints require `developer` role (or `procedural.generate` permission for runtime generation by actors/services).

- **Generate** (`/procedural/generate`): Primary generation endpoint. Accepts template ID, parameters, seed, output format, sync/async flag. Sync: waits for result, returns asset/bundle ID. Async: returns job ID immediately. Checks cache first. Deduplicates concurrent identical requests via distributed lock.
- **BatchGenerate** (`/procedural/batch/generate`): Accepts template ID + array of parameter sets. Always async. Returns batch job ID. Optionally creates metabundle combining all outputs.
- **GetJobStatus** (`/procedural/job/status`): Returns job status, progress, output references (if complete), error details (if failed).
- **CancelJob** (`/procedural/job/cancel`): Cancels a queued or in-progress job. Releases worker if acquired.

### Introspection (2 endpoints)

- **InspectHDA** (`/procedural/inspect`): Given an Asset service ID pointing to an HDA file, introspects and returns the full parameter schema without registering as a template. Useful for content authoring tools.
- **PreviewParameters** (`/procedural/preview`): Validates a parameter set against a template's schema. Returns validation errors without executing generation. Useful for client-side parameter editors.

### Cache Management (2 endpoints)

- **InvalidateCache** (`/procedural/cache/invalidate`): Invalidate specific cache entries by template ID, hash, or all entries. Used when HDA templates are updated.
- **GetCacheStats** (`/procedural/cache/stats`): Returns cache hit/miss rates, entry count, memory usage.

---

## Houdini Worker Architecture

### Worker Container

```dockerfile
FROM aaronsmithtv/houdini:20.5

# SideFX Labs (ships the Substance plugin + Substance Engine runtime)
COPY sidefx-labs/ /opt/houdini/sidefx-labs/
ENV HOUDINI_PATH=/opt/houdini/sidefx-labs;&

COPY houdini_server.py /opt/houdini/server.py
COPY hdas/ /opt/houdini/hdas/ # Pre-cached common HDAs
COPY sbsar/ /opt/houdini/sbsar/ # Pre-cached common Substance archives

EXPOSE 8008

CMD ["hython", "/opt/houdini/server.py"]
```

The Substance Engine runtime library is distributed with the free SideFX Labs Substance plugin — no Substance Automation Toolkit (SAT) license required. See § "Substance Integration" below for the authoring/consumption split and material pipeline detail.

### Worker API (hwebserver)

The Houdini worker exposes three endpoints via hwebserver:

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/generate_asset` | POST (apiFunction) | Execute HDA with parameters, return geometry |
| `/api/get_hda_parameters` | POST (apiFunction) | Introspect HDA parameter schema |
| `/health` | GET (urlHandler) | Health check with Houdini version |

### Worker Pool Management

Houdini workers are managed by Orchestrator as a processing pool:

```
Orchestrator Processing Pool: "houdini-generation"
 |
 +-- Worker 1 (warm, pre-initialized, common HDAs loaded)
 +-- Worker 2 (warm)
 +-- Worker 3 (cold, spun up on demand)
 |
 Pool Config:
 min_workers: 1 (always keep one warm)
 max_workers: 10 (scale ceiling)
 idle_timeout: 300s (shutdown idle workers after 5 min)
 warmup_hdas: [...] (pre-load common HDA templates)
```

### Cold Start Mitigation

Houdini startup + HDA loading can take 5-30 seconds. Mitigations:
- Keep minimum 1 warm worker at all times
- Pre-load common HDAs on worker startup
- Cache HDA installations in worker container filesystem
- Use in-memory session persistence between requests (hython stays running)
- Async generation mode is the primary pattern (callers don't block)

---

## Substance Integration (Material Generation)

The Houdini worker image bundles the SideFX Labs Substance plugin, which loads Substance Archive files (`.sbsar`) into Houdini's COP (compositing) context and renders textures parametrically via the embedded Substance Engine runtime. HDAs can reference `.sbsar` templates via Substance Archive COPs and use the rendered maps as materials bound to the HDA's geometry output. This lets lib-procedural deliver textured meshes from a single Generate call — no separate texture-generation service, no per-asset material authoring.

### The Authoring / Consumption Split

| Product | Role | Platform | Frequency |
|---------|------|----------|-----------|
| **Substance 3D Designer** | Author smart materials interactively (node graph editor) | Windows / macOS | Occasional — one session per primitive, batched creative work |
| **Substance 3D Painter** | Paint textures directly on 3D meshes | Windows / macOS | Rarely used by this pipeline — the sprite/HDA pipeline prefers parametric smart materials |
| **Substance Engine** (runtime library) | Renders `.sbsar` → textures at runtime | **Cross-platform, including Linux** | Per generation request |
| **SideFX Labs Substance plugin** (Houdini) | Loads `.sbsar` in Houdini's COP context, drives the Substance Engine | Wherever Houdini runs (including headless Linux containers) | Per generation request |
| **Substance Automation Toolkit (SAT)** | Standalone `sbsrender` CLI | Cross-platform | **Not used by this pipeline** — see licensing note below |

Authoring is infrequent, human-creative, and happens on a Windows or macOS workstation (Substance Indie subscription, ~$19.99/mo). Consumption is frequent, parametric, and happens on the same Linux Houdini workers that already run HDA generation. The two parts of the workflow never share a workstation — the `.sbsar` output flows from authoring → `lib-asset` → Houdini workers.

### Architecture

```
Authoring workstation (Windows/macOS)
  Substance Designer → weathered-stone.sbsar
  Houdini → dungeon-wall.hda (references sbsar via Substance Archive COP)
  Both published to lib-asset via the developer tooling.

Production (Linux Houdini workers, at scale):
  Request: {template: "dungeon-wall", corruption: 0.6, age: "old", biome: "mountain"}
   → lib-procedural → Orchestrator-managed worker (existing path)
   → Load dungeon-wall.hda; set geometry parameters
   → SideFX Labs Substance Archive COP loads weathered-stone.sbsar
   → Substance Engine renders material maps (diffuse, normal, roughness, metallic, AO) with
     the same parameters propagated through the HDA's parameter network
   → HDA binds maps to mesh UVs via standard Houdini material assignment
   → Export textured mesh (GLB/USD/bgeo)
   → Upload to lib-asset, bundle, return reference (existing path)
```

### Downstream Consumption

Output is a standard textured mesh in the target format (GLB/USD/bgeo). Downstream consumers decide what to do with it:

- **2.5D sprite pipelines** (via sprite-composer): the textured mesh is captured as a sprite atlas; Substance materials are pre-baked into the sprite color/normal atlases. Mobile runtime has zero Substance Engine overhead — textures are static.
- **3D runtime consumers** (Godot/Stride/Unity): the textured mesh with baked PBR material maps loads via standard engine asset loaders. No runtime Substance Engine required unless the consumer explicitly adds one for live material parameter changes.
- **Content authoring / preview tools**: can pass through the raw `.sbsar` for live-parameter tweaking if desired, but that's an authoring-tool concern, not a runtime concern.

### Substance Licensing Notes

| Component | Licensing | Path |
|-----------|-----------|------|
| **Substance 3D Designer** (authoring) | Per-user subscription (Indie ~$19.99/mo with revenue cap; higher tiers for larger studios) | Author buys own subscription; credentials never transit the pipeline |
| **SideFX Labs Substance plugin** (Houdini-integrated consumption) | **Free** with SideFX Labs; ships the Substance Engine runtime | Bundled in the worker image |
| **Substance Engine** (runtime library) | Free to redistribute as part of the SideFX Labs plugin | Bundled — no separate license required |
| **Substance Automation Toolkit (SAT)** | Enterprise/Team-only under Adobe (was free with Indie historically, relicensed ~2024-2025) | **NOT used by this pipeline** — the Houdini plugin path avoids the SAT license requirement entirely |

Pre-built `.sbsar` files are also available from Adobe's community library and third-party marketplaces (Cubebrush, Gumroad, ArtStation), which can seed the material primitive set without authoring from scratch.

### Substance-Specific Configuration

Substance integration inherits all the existing configuration (worker pool, timeouts, cache, etc.) — no separate Substance pool is needed. The `.sbsar` files are stored in lib-asset alongside HDAs and referenced from within HDAs via standard Houdini asset references. An HDA that doesn't reference a `.sbsar` simply doesn't use the Substance COP, and the generation pipeline works identically to pre-Substance geometry-only generation.

### Houdini + Substance References

- [SideFX Labs Substance Plugin](https://www.sidefx.com/tutorials/sidefx-labs-substance-plugin/) — official tutorial: loading `.sbsar` into Houdini COPs, chaining Substance nodes with Houdini networks
- [Labs Substance Archive COP node docs](https://www.sidefx.com/docs/houdini/nodes/cop2/labs--sbs_archive.html)
- [Adobe: Houdini Ecosystem & Plug-ins](https://helpx.adobe.com/substance-3d-integrations/3d-applications/houdini.html) — official integration page
- [SideFX Labs on GitHub](https://github.com/sideeffects/SideFXLabs) — plugin source + distribution
- [Substance 3D Automation Toolkit docs](https://helpx.adobe.com/substance-3d-sat.html) — reference only; SAT is not used by this pipeline

---

## Licensing

| Scale | Workers | License Type | Annual Cost |
|-------|---------|--------------|-------------|
| **Indie/Prototype** | 1-3 | Houdini Engine Indie (FREE) | $0 + $269 HDA authoring |
| Small Production | 4-8 | Floating x2 | $1,590 + authoring |
| Medium Production | 10-20 | Floating x5 | $3,975 + authoring |
| Large Scale | 50+ | Volume licensing | ~$200/seat + authoring |

**Key distinction**: Houdini Engine (headless execution) is separate from full Houdini (GUI authoring). Servers only need Engine licenses. Artists need full Houdini for HDA creation.

**Indie tier**: Free for < $100K annual revenue, up to 3 machines. Perfect for development and early production.

---

## Houdini Technology References

Key external documentation for implementers. Houdini provides built-in headless HTTP serving (`hwebserver`), containerized deployment, and batch processing via PDG — all of which the worker architecture above leverages.

### Official SideFX Documentation

- [Introduction to Digital Assets](https://www.sidefx.com/docs/houdini/assets/intro.html) — HDA fundamentals, parameter interfaces, packaging
- [hwebserver Module](https://www.sidefx.com/docs/houdini/hwebserver/index.html) — Built-in HTTP server for headless operation (the worker API layer)
- [hwebserver.apiFunction](https://www.sidefx.com/docs/houdini/hwebserver/apiFunction.html) — RPC-style endpoint decorator used by worker endpoints
- [Houdini Engine Overview](https://www.sidefx.com/products/houdini-engine/) — Headless execution capabilities (what workers use)
- [Houdini Engine Batch](https://www.sidefx.com/products/houdini-engine/batch/) — Batch processing licensing (merged with Engine license)
- [PDG/TOPs Introduction](https://www.sidefx.com/docs/houdini/tops/intro.html) — Procedural dependency graphs for Phase 4 massive parallel generation
- [HDA Processor TOP](https://www.sidefx.com/docs/houdini/nodes/top/hdaprocessor.html) — Batch HDA processing via PDG
- [glTF Export](https://www.sidefx.com/docs/houdini/io/gltf.html) — Primary output format (GLB) for game engine consumption
- [HAPI Integration](https://www.sidefx.com/docs/hengine/_h_a_p_i__integration.html) — C API for direct integration (alternative to hwebserver; not currently planned but available)

### Containerization & Deployment

- [Houdini-Docker](https://github.com/aaronsmithtv/Houdini-Docker) — Optimized Docker images for headless operation (65% smaller than standard)
- [Headless Linux Setup](https://jurajtomori.wordpress.com/2019/03/05/setting-up-houdini-on-a-headless-linux-server/) — Server deployment guide

### Licensing

- [Houdini Engine FAQ](https://www.sidefx.com/faq/houdini-engine-faq/) — Engine licensing details (free Indie tier for <$100K revenue)
- [Cloud Usage FAQ](https://www.sidefx.com/faq/question/cloud/) — Cloud deployment licensing (SideFX cloud licensing for login-based auth)

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists.

### Phase 1: Proof of Concept (1-2 weeks)
- Stand up single Houdini container with hwebserver
- Create sample rock generator HDA
- Test generation API manually
- Validate output quality, performance, determinism

### Phase 2: Bannou Integration (2-3 weeks)
- Create procedural-api.yaml schema with all endpoints
- Create procedural-service-events.yaml schema
- Create procedural-configuration.yaml schema
- Generate service code
- Implement template registration with HDA introspection
- Implement synchronous generation with Asset upload
- Implement generation cache with deduplication
- Wire Orchestrator pool for worker management

### Phase 3: Production Features (2-3 weeks)
- Implement async job queue with status tracking
- Implement batch generation
- Implement metabundle creation for batch outputs
- Error handling, retry logic, circuit breaker for worker failures
- Load testing and performance benchmarks

### Phase 4: Advanced Features (ongoing)
- PDG integration for massive parallel generation
- LOD pipeline (generate multiple detail levels per asset)
- Client-side parameter preview (lightweight HDA preview without full generation)
- Generation analytics (template popularity, average generation times, cache effectiveness)

> **Substance integration** was previously planned here as "Texture generation (Substance integration potential)" in Phase 4. It is promoted to Phase 1/2 scope as of 2026-04-22 (see § "Substance Integration (Material Generation)"). The Houdini Worker Image includes the SideFX Labs Substance plugin from day one; `.sbsar` template storage lands with the existing `lib-asset` integration; no separate phase is required.

---

## Integration with The Content Flywheel

lib-procedural amplifies the content flywheel in a unique way. Without it, the flywheel generates **narrative** content (quests, memories, NPC behaviors) from play history. With it, the flywheel generates **physical** content too:

```
Player Actions
 |
 v
Character History / Realm History (accumulated)
 |
 v
Resource Service (compression into archives)
 |
 v
Storyline (GOAP narrative generation from archives)
 |
 v
Regional Watchers / Puppetmaster (orchestrate scenarios)
 | |
 v v
Quest generation lib-procedural
(narrative content) (physical content)
 | |
 | Terrain changes from world events
 | Buildings from NPC construction
 | Dungeon chambers from core growth
 | Environmental effects from magic
 | |
 v v
New Player Experiences (richer because the WORLD PHYSICALLY CHANGED)
 |
 v
More Player Actions --> loop
```

The flywheel doesn't just generate stories -- it generates the **geometry those stories inhabit**. A war that scarred a region isn't just recorded in realm history; the terrain is regenerated with battle damage. An NPC builder doesn't just claim to have built a house; a unique building facade appears. A dungeon doesn't just conceptually "grow"; new chambers with unique geometry materialize.

---

## Voxel SDK Integration

lib-procedural is the primary server-side consumer of the voxel SDK family (voxel-core, voxel-builder, voxel-generator). Two integration paths exist, composable:

### Path A: Voxel Generators as Lightweight Houdini Alternative

For discrete geometry that doesn't need Houdini's power, voxel-generator's algorithms run directly in lib-procedural's process. No container, no license, sub-second generation:

```
lib-procedural receives generation request
  |
  +--> template_type = "houdini" → route to Houdini worker (existing path)
  +--> template_type = "voxel"   → execute IVoxelGenerator in-process (new path)
         |
         +-- PrimitiveGenerator: "Generate a 20x10x20 stone building"
         +-- WFC Generator: "Generate connected dungeon wing from tileset X"
         +-- NoiseGenerator: "Generate terrain chunk at coordinates Y"
         +-- TemplateStamper: "Stamp door/window/roof templates onto building shell"
         |
         VoxelGrid → GreedyMesher → MeshExporter.ExportGlb() → upload to Asset
         OR VoxelGrid → VoxelSerializer → .bvox → upload to Asset
         → return asset reference
```

The `template_type` field in the generation request discriminates between Houdini and voxel paths. Voxel generators use the same deterministic content-addressed caching as Houdini (`SHA256(generatorId + params + seed) → cached result`). The voxel path bypasses Orchestrator worker pools entirely — generation runs in the lib-procedural process.

### Path B: Terrain Overlay (Voxelize → Edit → Bake)

The terrain overlay workflow for work zones (roadwork, construction, landscaping):

```
1. Client/actor designates a work zone (bounding box in world coordinates)

2. lib-procedural fetches terrain data for the work zone:
   → lib-scene: get scene node(s) covering the work zone
   → Asset service: download the terrain asset (.glb mesh or heightmap)

3. lib-procedural converts terrain to voxels:
   → IF heightmap: HeightmapVoxelizer.Voxelize(heights, materials, palette, options)
   → IF mesh: GltfImporter.Import(glbBytes) → MeshData → MeshVoxelizer.Voxelize(meshData, ...)
   → FrozenBorderWidth voxels around edges locked to match surrounding terrain
   → Serialize to .bvox → upload to Asset service
   → Register voxel overlay in lib-scene as a "voxel" node type

4. Players/NPCs edit the voxel overlay via VoxelBuilder:
   → Dig, flatten, tunnel, build within the editable (non-frozen) region
   → Save-Load persists delta saves (modified chunks only)
   → Frozen border prevents edits that would break the terrain seam

5. Work zone completion validation:
   → Grade matching: edited surface at borders matches surrounding terrain heights
   → Structural stability: no floating islands, unsupported overhangs within tolerance
   → Drainage/access: game-specific sanity checks

6. Bake to permanent mesh:
   → VoxelGrid → GreedyMesher or MarchingCubesMesher → MeshData → .glb
   → Upload baked mesh to Asset service
   → Replace voxel scene node with mesh scene node
   → Delete voxel overlay data (Save-Load deltas, .bvox asset)
   → The work zone becomes permanent terrain — efficient to store and render
```

### GltfImporter Location

The terrain overlay workflow needs a glTF/GLB → MeshData parser to convert downloaded terrain meshes into the format MeshVoxelizer accepts. This parser lives in **voxel-builder** as `GltfImporter` alongside the BlockBench and MagicaVoxel importers — it's a format import operation, which is voxel-builder's domain. lib-procedural calls it via project reference.

The heightmap path is simpler — `HeightmapVoxelizer` in voxel-core accepts `float[,]` height arrays directly. lib-procedural reads the heightmap asset (raw float data or image) and passes the array. No intermediate parser needed.

### Consumer Table Update

| Consumer | Use Case | Path | Parameters |
|----------|----------|------|-----------|
| **lib-dungeon** (L4) | Chamber generation, layout shifting | A (WFC, TemplateStamper) | Personality, seed, room type, connection points |
| **lib-puppetmaster** (L4) | Terrain sculpting by regional watchers | A (Noise) or B (terrain overlay) | Biome, event type, intensity |
| **Gardener / Housing** (L4) | Player housing construction zones | B (terrain overlay) | Zone bounds, terrain source |
| **NPC builders** (L2, Actor) | Building construction | A (Primitives, TemplateStamper) | Style, dimensions, materials |
| **Player/NPC sculpting** | Decorative objects in housing | Client-side (no lib-procedural) | VoxelBuilder SDK directly in engine |
| **Realm seeding** | Initial world geometry batch generation | A (all generators) + Houdini | Region, biome, seed, resolution |

Note that **player/NPC sculpting** (statues, carvings, decor) does NOT route through lib-procedural. The VoxelBuilder SDK runs client-side in the game engine. The finished .bvox data is stored directly via item metadata or Asset service — no server-side generation needed for interactive sculpting.

---

## Potential Extensions

> **Note**: Substance smart-material integration was previously listed here as a potential extension. As of 2026-04-22 it is promoted to a primary integrated workflow — see § "Substance Integration (Material Generation)" above.

1. **Audio generation**: Procedural sound effects from parameters (material impacts, environmental ambience). Lighter-weight than geometry generation.

2. **Batch pre-generation**: Pre-generate common variations during off-peak hours. Cache warming for anticipated generation requests.

3. **HDA / sbsar marketplace**: Third-party artists create and sell HDA templates and smart materials through the Asset service. lib-procedural becomes a platform for procedural content creation.

4. **Client-side preview**: Lightweight parameter preview using simplified HDA execution or pre-computed thumbnails. Useful for dungeon masters choosing chamber styles, or for designers previewing Substance material variations without a full generation pass.

5. **Version-aware generation**: HDA templates and sbsar smart materials have versions. Existing cached generations reference the template + material versions. Updates don't invalidate existing geometry but new requests use the latest versions.

6. **Composite generation**: Chain multiple HDAs -- generate terrain, then place buildings on it, then add vegetation. Pipeline orchestration for complex environments.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Parameters are schema-less (additionalProperties: true)**: Different HDAs have completely different parameter sets. lib-procedural stores the parameter schema per template (from introspection) but the generation request accepts arbitrary key-value pairs. Validation happens at introspection time, not in the API schema.

2. **Determinism requires explicit seeds**: If the caller doesn't provide a seed, lib-procedural generates a random one. But the random seed is stored in the job record, so the generation can always be reproduced later. Per, the seed parameter in the generation request schema must be `nullable: true` (null = "generate random seed"), not a sentinel value like 0 or -1.

3. **Cache key is content-addressed**: SHA256(templateId + canonicalized parameters + seed). Two requests with identical inputs always hit the same cache entry, even from different callers. This is intentional -- generation is pure function of inputs.

4. **Worker acquisition is the bottleneck**: Houdini workers are heavyweight (memory, CPU). The max_concurrent_generations config gates throughput. Async mode with job status polling is the expected pattern for production use.

5. **HDA authoring is external**: lib-procedural executes HDAs but doesn't create them. HDA creation requires full Houdini ($269/year Indie). The Asset service stores HDAs as regular assets; the template registration endpoint introspects them.

6. **Output format determines downstream compatibility**: glTF/GLB for game engines, USD for composition pipelines, bgeo for Houdini-to-Houdini workflows. The .bannou bundle format wraps any of these with LZ4 compression.

### Design Considerations (Requires Planning)

1. **Worker health monitoring**: Houdini workers can crash or hang on malformed HDAs. The Orchestrator pool handles worker lifecycle, but lib-procedural needs timeout and circuit breaker logic for individual generation requests.

2. **License server reliability**: All Houdini workers check out licenses from a license server. License server downtime means no generation. Mitigation: SideFX cloud licensing (login-based) + graceful degradation (queue jobs until licenses available).

3. **HDA compatibility versioning**: HDAs may require specific Houdini versions. Worker containers are version-tagged. Template registration should validate HDA compatibility with available worker versions.

4. **Large output handling**: Complex HDAs can produce large geometry files (hundreds of MB). Pre-signed URL upload to Asset service avoids memory pressure on lib-procedural. Worker uploads directly to MinIO.

5. **Multi-output HDAs**: Some HDAs produce multiple outputs (mesh + collision mesh + LODs + textures). The generation response should support multiple asset IDs per job.

6. **(AUDIT:NEEDS_DESIGN) Asset deletion cleanup -- lib-resource vs event subscription**: Templates store Asset entity references (HDA file IDs). When the HDA asset is deleted, the template should be deprecated. forbids event subscriptions for "destroying dependent data" but permits them for "live state reactions." Deprecating (not destroying) a template is arguably a live state reaction. Decision needed: use lib-resource `x-references` with `onDelete: detach` (deprecate template via cleanup callback), or keep the `asset.deleted` event subscription as a live state reaction. If lib-resource is used, remove the event subscription.

7. **(AUDIT:NEEDS_DESIGN) lib-resource integration scope**: Should Procedural register references to Asset entities via `x-references`? Should other services that store Procedural template IDs (e.g., lib-dungeon storing the template used for chamber generation) register references back to Procedural? says services that store another service's entity ID should register references with lib-resource, but the practical scope needs definition.

8. **(AUDIT:NEEDS_DESIGN) DefaultOutputFormat -- enum vs string**: Output formats (Glb, Usd, Bgeo, etc.) are determined by what Houdini can export. favors enums for finite system-owned sets, but the set could expand with Houdini updates. Decision: define an `OutputFormat` enum (Category C, PascalCase values) or keep as string for extensibility.

9. **(AUDIT:NEEDS_DESIGN) Worker pool tunables ownership**: The deep dive shows `min_workers`, `max_workers`, `idle_timeout`, `warmup_hdas` as inline pool config values. requires all tunables in config. Decision: are these Procedural's config properties (passed to Orchestrator pool API) or Orchestrator's pool configuration? If Procedural's, add `HoudiniPoolMinWorkers`, `HoudiniPoolMaxWorkers`, `HoudiniPoolIdleTimeoutSeconds`, `HoudiniWarmupHdas` to the configuration table.

10. **(AUDIT:NEEDS_DESIGN) x-permissions model for generation endpoints**: The deep dive mentions "procedural.generate permission" which is not a valid x-permissions construct (Bannou uses roles and states, not custom permission strings). Decision needed: should generation endpoints be `x-permissions: []` (service-to-service only via mesh, not exposed to WebSocket clients) or `x-permissions: [{role: developer}]` (accessible to developer WebSocket sessions)? Template/cache/introspection endpoints should be `[{role: developer}]`.

11. **(AUDIT:NEEDS_DESIGN) documentation for HDA parameters**: The parameters field uses `additionalProperties: true` which restricts. The use case is defensible (opaque pass-through to Houdini workers, no Bannou service reads keys by convention, validated at runtime against per-template parameter schema). Decision: what exact compliance description to include in the schema property description.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. 6 design considerations (DC#6-DC#11) added by L4 audit (2026-03-07) require resolution before schema creation.*
