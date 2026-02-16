# Procedural Plugin Deep Dive

> **Plugin**: lib-procedural
> **Schema**: schemas/procedural-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFeatures
> **State Stores**: procedural-templates (MySQL), procedural-jobs (MySQL), procedural-cache (Redis), procedural-lock (Redis)
> **Status**: Pre-implementation (architectural specification)

---

## Overview

On-demand procedural 3D asset generation service (L4 GameFeatures) using headless Houdini Digital Assets (HDAs) — self-contained parametric procedural tools packaged as `.hda` files with exposed parameter interfaces (sliders, menus, toggles, ramps) that generate infinite geometry variations from a single authored template — as parametric generation templates. A thin orchestration layer that composes existing Bannou primitives (Asset service for HDA storage and output bundling, Orchestrator for Houdini worker pool management) to deliver procedural geometry generation as an API. Game-agnostic — the service knows nothing about what it generates, it executes HDAs and returns geometry. Internal-only, never internet-facing.

---

## Why This Service Exists

**The authoring-to-runtime bridge**: lib-procedural bridges content authoring and runtime generation. Artists author HDAs (parametric procedural tools with exposed controls) in Houdini's GUI. Those HDAs are uploaded to the Asset service as templates. At runtime, any service (dungeon cores exercising domain_expansion, regional watchers sculpting terrain, NPC builders constructing buildings, world seeding during realm creation) can request generation by providing a template ID, parameters, and a seed. The same HDA with different parameters produces dramatically different geometry -- infinite variations from a single authored template.

**Composability**: Template storage and output bundling are Asset (L3). Worker pool lifecycle is Orchestrator (L3). Job status tracking is internal. Generation execution is delegated to headless Houdini containers running hwebserver. lib-procedural orchestrates the pipeline: receive request, fetch HDA from Asset, acquire worker from Orchestrator, execute generation, upload result to Asset, optionally bundle, return reference.

**Deterministic generation**: Same template + same parameters + same seed = identical output. This enables Redis-cached generation results (keyed by hash of template_id + parameters + seed), reproducible dungeon layouts, and predictable world seeding. The cache key is canonical -- if the same generation has been requested before, the cached result is returned without invoking Houdini.

### The Content Generation Gap

Bannou can manage, store, version, spatially index, and compose 3D assets. It cannot **create** them. Every piece of geometry in the system was authored by a human and uploaded. This creates a bottleneck:

- **Dungeons** that grow new chambers need geometry for those chambers
- **Terrain** for procedurally-generated regions needs heightmaps and meshes
- **Buildings** constructed by NPC builders need facade variations
- **Vegetation** scattered across regions needs species-appropriate variations
- **World seeding** during realm creation needs massive quantities of varied geometry

lib-procedural closes this gap. With it, Bannou becomes a platform that both **manages** and **generates** content on demand.

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
        personality: "martial",    (from dungeon_core seed metadata)
        seed: 42,                  (deterministic)
        room_type: "arena",        (from dungeon cognition)
        connection_points: [...],  (from existing layout in Mapping)
        growth_phase: "Awakened",  (from dungeon_core seed phase)
        style_params: {...}        (from personality_effects configuration)
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
| lib-asset (`IAssetClient`) | HDA template storage and retrieval, generated output upload, bundle creation (L3) |
| lib-orchestrator (`IOrchestratorClient`) | Houdini worker pool management -- acquire/release workers (L3) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
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

All dependents are L4 (or L2 workflows using L4). lib-procedural is L4 and depends only on L3 (Asset, Orchestrator) and L0 (state, messaging). No hierarchy issues.

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
| `tmpl:{templateId}` | Template mutation lock (register, update, deactivate) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `procedural.job.queued` | `ProceduralJobQueuedEvent` | Generation job accepted and queued |
| `procedural.job.started` | `ProceduralJobStartedEvent` | Houdini worker acquired, generation beginning |
| `procedural.job.completed` | `ProceduralJobCompletedEvent` | Generation successful, output uploaded to Asset service |
| `procedural.job.failed` | `ProceduralJobFailedEvent` | Generation failed (timeout, HDA error, worker unavailable) |
| `procedural.template.registered` | `ProceduralTemplateRegisteredEvent` | New HDA template registered |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `asset.deleted` | `HandleAssetDeletedAsync` | If deleted asset is an HDA template, deactivate the template registration |

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

All endpoints require `developer` role.

- **RegisterTemplate** (`/procedural/template/register`): Downloads HDA from Asset service, introspects parameter schema via Houdini worker, stores template metadata with parameter definitions. Validates HDA is loadable.
- **GetTemplate** (`/procedural/template/get`): Returns template metadata including full parameter schema.
- **ListTemplates** (`/procedural/template/list`): Paged query with optional category filter.
- **DeactivateTemplate** (`/procedural/template/deactivate`): Soft-deactivate. Active jobs using this template continue; new requests rejected.

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

COPY houdini_server.py /opt/houdini/server.py
COPY hdas/ /opt/houdini/hdas/    # Pre-cached common HDAs

EXPOSE 8008

CMD ["hython", "/opt/houdini/server.py"]
```

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
        min_workers: 1          (always keep one warm)
        max_workers: 10         (scale ceiling)
        idle_timeout: 300s      (shutdown idle workers after 5 min)
        warmup_hdas: [...]      (pre-load common HDA templates)
```

### Cold Start Mitigation

Houdini startup + HDA loading can take 5-30 seconds. Mitigations:
- Keep minimum 1 warm worker at all times
- Pre-load common HDAs on worker startup
- Cache HDA installations in worker container filesystem
- Use in-memory session persistence between requests (hython stays running)
- Async generation mode is the primary pattern (callers don't block)

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
- Create procedural-events.yaml schema
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
- Texture generation (Substance integration potential)
- LOD pipeline (generate multiple detail levels per asset)
- Client-side parameter preview (lightweight HDA preview without full generation)
- Generation analytics (template popularity, average generation times, cache effectiveness)

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
    |                                    |
    v                                    v
Quest generation               lib-procedural
(narrative content)            (physical content)
    |                              |
    |                    Terrain changes from world events
    |                    Buildings from NPC construction
    |                    Dungeon chambers from core growth
    |                    Environmental effects from magic
    |                              |
    v                              v
New Player Experiences (richer because the WORLD PHYSICALLY CHANGED)
    |
    v
More Player Actions --> loop
```

The flywheel doesn't just generate stories -- it generates the **geometry those stories inhabit**. A war that scarred a region isn't just recorded in realm history; the terrain is regenerated with battle damage. An NPC builder doesn't just claim to have built a house; a unique building facade appears. A dungeon doesn't just conceptually "grow"; new chambers with unique geometry materialize.

---

## Potential Extensions

1. **Texture generation**: Substance Designer integration for procedural textures alongside geometry. Same worker pool pattern, different tool.

2. **Audio generation**: Procedural sound effects from parameters (material impacts, environmental ambience). Lighter-weight than geometry generation.

3. **Batch pre-generation**: Pre-generate common variations during off-peak hours. Cache warming for anticipated generation requests.

4. **HDA marketplace**: Third-party artists create and sell HDA templates through the Asset service. lib-procedural becomes a platform for procedural content creation.

5. **Client-side preview**: Lightweight parameter preview using simplified HDA execution or pre-computed thumbnails. Useful for dungeon masters choosing chamber styles.

6. **Version-aware generation**: HDA templates have versions. Existing cached generations reference the template version. Template updates don't invalidate existing geometry but new requests use the latest version.

7. **Composite generation**: Chain multiple HDAs -- generate terrain, then place buildings on it, then add vegetation. Pipeline orchestration for complex environments.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Parameters are schema-less (additionalProperties: true)**: Different HDAs have completely different parameter sets. lib-procedural stores the parameter schema per template (from introspection) but the generation request accepts arbitrary key-value pairs. Validation happens at introspection time, not in the API schema.

2. **Determinism requires explicit seeds**: If the caller doesn't provide a seed, lib-procedural generates a random one. But the random seed is stored in the job record, so the generation can always be reproduced later.

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

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase.*
