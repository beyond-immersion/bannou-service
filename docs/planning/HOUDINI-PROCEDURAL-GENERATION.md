# Houdini Procedural Generation Service - Feasibility Study

> **Status**: Research Complete - Implementation Planning
> **Created**: 2026-01-28
> **Author**: Claude (research) + Lysander (direction)

## Executive Summary

This document explores integrating SideFX Houdini as a headless procedural generation backend for Bannou services. The goal is to enable on-demand 3D asset generation using **Houdini Digital Assets (HDAs)** - parametric procedural tools with exposed controls that generate infinite variations of models, textures, and effects.

**Verdict: Highly Feasible.** Houdini provides all the necessary infrastructure:
- Built-in HTTP server (`hwebserver`) for API endpoints
- Containerized deployment with maintained Docker images
- Rich parameter system for exposing generation controls
- Multiple export formats (glTF, FBX, OBJ, USD)
- Batch processing via PDG for high-throughput generation
- Reasonable licensing ($499-795/year commercial, free for indie)

This would be a genuinely unique capability - few if any game services offer on-demand procedural 3D generation as an API.

---

## Table of Contents

1. [What Are HDAs?](#what-are-hdas)
2. [The Generation Pipeline](#the-generation-pipeline)
3. [Technical Components](#technical-components)
4. [Bannou Integration Architecture](#bannou-integration-architecture)
5. [Licensing & Costs](#licensing--costs)
6. [Implementation Phases](#implementation-phases)
7. [Challenges & Mitigations](#challenges--mitigations)
8. [Example Use Cases](#example-use-cases)
9. [Research Sources](#research-sources)

---

## What Are HDAs?

**Houdini Digital Assets (HDAs)** are exactly what you described - procedural "mini-applications" packaged as reusable nodes with exposed parameters. Think of them as 3D model generators with dials and knobs.

### Key Characteristics

| Feature | Description |
|---------|-------------|
| **Exposed Parameters** | Artists define which controls are visible: sliders, toggles, menus, color pickers, ramps/curves |
| **Procedural Core** | Internal node network generates geometry based on parameter values |
| **Deterministic** | Same seed + same parameters = identical output (reproducible) |
| **Self-Contained** | Single `.hda` or `.hdanc` file contains everything needed |
| **Portable** | Works in Houdini GUI, Houdini Engine, hython, game engines |

### Parameter Types Available

HDAs support rich parameter interfaces:

- **Numeric**: Float, Integer, Float2/3/4 (vectors), Angle
- **String**: Text, File paths, Geometry paths
- **Toggle**: Boolean checkboxes
- **Menu**: Dropdown selections (static or dynamic)
- **Ramp**: Color gradients and float curves
- **Button**: Trigger actions
- **Folders**: Organize parameters into tabs/groups

### Example: Rock Generator HDA

An HDA for procedural rock generation might expose:

```
Rock Generator v1.0
├── Shape
│   ├── Seed (integer) - randomization seed
│   ├── Base Shape (menu) - boulder, shard, smooth, jagged
│   ├── Size (float3) - X/Y/Z dimensions
│   ├── Detail Level (integer) - subdivision iterations
│   └── Asymmetry (float 0-1) - how irregular
├── Surface
│   ├── Noise Scale (float) - surface detail frequency
│   ├── Noise Amplitude (float) - surface displacement
│   ├── Erosion (float 0-1) - weathering amount
│   └── Cracks (toggle) - add crack details
├── Material
│   ├── Base Color (color) - rock tint
│   ├── Moss Coverage (float 0-1) - vegetation blend
│   └── Texture Resolution (menu) - 512/1024/2048/4096
└── Export
    ├── LOD Count (integer) - number of detail levels
    └── Format (menu) - glTF, FBX, OBJ
```

Adjusting these parameters produces dramatically different rocks from the same HDA.

---

## The Generation Pipeline

### High-Level Flow

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         BANNOU SERVICES                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  1. Client Request                                                      │
│     ─────────────►  Procedural Service                                  │
│     "Generate rock,                │                                    │
│      seed=42,                      ▼                                    │
│      style=jagged"         2. Fetch HDA from Asset Service              │
│                                    │                                    │
│                                    ▼                                    │
│                            3. Queue Generation Job                      │
│                                    │                                    │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      HOUDINI GENERATION POOL                            │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  4. Load HDA          5. Set Parameters       6. Cook & Export          │
│     ┌─────────┐          ┌─────────┐            ┌─────────┐             │
│     │  .hda   │ ──────►  │ params  │ ────────►  │  .glb   │             │
│     │  file   │          │  JSON   │            │  file   │             │
│     └─────────┘          └─────────┘            └─────────┘             │
│                                                       │                 │
│  hwebserver API endpoint handles full lifecycle       │                 │
│                                                       │                 │
└───────────────────────────────────────────────────────┼─────────────────┘
                                                        │
                                                        ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         BANNOU SERVICES                                 │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  7. Upload to Asset Service      8. Create Bundle (optional)            │
│            │                              │                             │
│            ▼                              ▼                             │
│  ┌──────────────────┐          ┌──────────────────┐                     │
│  │  MinIO storage   │          │  .bannou bundle  │                     │
│  │  (pre-signed)    │          │  (LZ4 compressed)│                     │
│  └──────────────────┘          └──────────────────┘                     │
│            │                              │                             │
│            └──────────────┬───────────────┘                             │
│                           ▼                                             │
│                  9. Return Asset Reference                              │
│                     ◄─────────────────────                              │
│                     to Client                                           │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Processing Modes

**Synchronous (Simple)**
- Client waits for generation to complete
- Best for: Small assets, real-time preview, development
- Timeout: 30-60 seconds max

**Asynchronous (Production)**
- Client receives job ID immediately
- Poll for status or receive WebSocket notification
- Best for: Complex assets, batch generation, large textures
- No timeout limit

**Batch (Mass Generation)**
- Submit multiple parameter sets in one request
- PDG handles parallel processing
- Best for: Dataset generation, procedural worlds, variations

---

## Technical Components

### 1. hwebserver - Built-in HTTP Server

Houdini includes a production-ready embedded web server that runs in hython (headless Python mode).

**Key Features:**
- Multi-threaded C++ core with Python handlers
- `@hwebserver.apiFunction` decorator for RPC-style endpoints
- `@hwebserver.urlHandler` decorator for REST-style routes
- WebSocket support
- WSGI compatibility (can host Django/Flask)
- Built-in templating (Jinja2)

**Example API Server:**

```python
#!/usr/bin/env hython
import hou
import hwebserver
import json
import tempfile
import os

@hwebserver.apiFunction()
def generate_asset(hda_path: str, parameters: dict, output_format: str = "glb"):
    """
    Generate a 3D asset from an HDA with given parameters.

    Args:
        hda_path: Path to the HDA file
        parameters: Dict of parameter name -> value
        output_format: Export format (glb, fbx, obj)

    Returns:
        Binary geometry data
    """
    # Install the HDA
    hou.hda.installFile(hda_path)

    # Get the HDA definition
    definitions = hou.hda.definitionsInFile(hda_path)
    if not definitions:
        raise ValueError(f"No HDA definitions found in {hda_path}")

    hda_def = definitions[0]

    # Create a geometry container and instance the HDA
    geo_node = hou.node("/obj").createNode("geo", "generator")
    hda_node = geo_node.createNode(hda_def.nodeTypeName())

    # Set parameters
    for param_name, param_value in parameters.items():
        parm = hda_node.parm(param_name)
        if parm:
            parm.set(param_value)

    # Cook the node
    hda_node.cook(force=True)

    # Export geometry
    with tempfile.NamedTemporaryFile(suffix=f".{output_format}", delete=False) as f:
        output_path = f.name

    # Create output node based on format
    if output_format == "glb":
        rop = geo_node.createNode("rop_gltf")
        rop.parm("file").set(output_path)
        rop.parm("binary").set(True)
    elif output_format == "fbx":
        rop = hou.node("/out").createNode("filmboxfbx")
        rop.parm("sopoutput").set(output_path)
    else:  # obj
        file_node = geo_node.createNode("file")
        file_node.setInput(0, hda_node)
        file_node.parm("file").set(output_path)
        file_node.parm("filemode").set(2)  # Write

    # Execute export
    rop.render()

    # Read and return the file
    with open(output_path, "rb") as f:
        data = f.read()

    # Cleanup
    os.unlink(output_path)
    geo_node.destroy()

    return hwebserver.Response(data, content_type="application/octet-stream")

@hwebserver.apiFunction()
def get_hda_parameters(hda_path: str):
    """
    Introspect an HDA and return its parameter schema.
    """
    hou.hda.installFile(hda_path)
    definitions = hou.hda.definitionsInFile(hda_path)

    if not definitions:
        return {"error": "No HDA found"}

    hda_def = definitions[0]
    parm_template_group = hda_def.parmTemplateGroup()

    params = []
    for parm_template in parm_template_group.parmTemplates():
        params.append({
            "name": parm_template.name(),
            "label": parm_template.label(),
            "type": str(parm_template.type()),
            "default": parm_template.defaultValue() if hasattr(parm_template, 'defaultValue') else None,
        })

    return {
        "hda_name": hda_def.nodeTypeName(),
        "description": hda_def.description(),
        "parameters": params
    }

@hwebserver.urlHandler("/health")
def health_check(request):
    return hwebserver.Response(
        json.dumps({"status": "healthy", "houdini_version": hou.applicationVersionString()}),
        content_type="application/json"
    )

if __name__ == "__main__":
    print(f"Starting Houdini Generation Server on port 8008...")
    print(f"Houdini Version: {hou.applicationVersionString()}")
    hwebserver.run(8008, debug=False, max_num_threads=4)
```

### 2. Docker Containerization

The [aaronsmithtv/Houdini-Docker](https://github.com/aaronsmithtv/Houdini-Docker) project provides optimized Docker images:

**Features:**
- 65% smaller than typical Houdini images
- Optimized for hython/headless operation
- Auto-updated daily with latest production builds
- Linux x86_64 base

**Example Dockerfile Extension:**

```dockerfile
FROM aaronsmithtv/houdini:20.5

# Install additional dependencies
RUN apt-get update && apt-get install -y \
    python3-pip \
    && rm -rf /var/lib/apt/lists/*

# Copy our server script
COPY houdini_server.py /opt/houdini/server.py

# Copy HDA library (or mount as volume)
COPY hdas/ /opt/houdini/hdas/

# Expose the API port
EXPOSE 8008

# Start the server
CMD ["hython", "/opt/houdini/server.py"]
```

### 3. HAPI (Houdini Engine API)

For deeper integration, HAPI provides a C API with C# bindings:

**Session Types:**
- **In-Process**: Link directly to libHAPI, fastest but shares process
- **Out-of-Process (HARS)**: Separate Houdini process, more isolated
- **Thrift RPC**: Remote Houdini sessions over network

**C# Integration Example:**

```csharp
// Using the Unity plugin's C# bindings as reference
// These can be adapted for standalone .NET use

using HAPI_HAPI;

public class HoudiniGenerator
{
    private HAPI_Session _session;

    public async Task<byte[]> GenerateAsync(string hdaPath, Dictionary<string, object> parameters)
    {
        // Create session
        HAPI_CreateInProcessSession(out _session);
        HAPI_Initialize(_session, /* ... */);

        // Load HDA
        HAPI_LoadAssetLibraryFromFile(_session, hdaPath, false, out int libraryId);
        HAPI_GetAvailableAssetCount(_session, libraryId, out int assetCount);

        // Create node
        HAPI_CreateNode(_session, -1, assetName, null, false, out int nodeId);

        // Set parameters
        foreach (var (name, value) in parameters)
        {
            // Get parameter info and set value based on type
            // ...
        }

        // Cook
        HAPI_CookNode(_session, nodeId, null);

        // Extract geometry
        // ...

        return geometryData;
    }
}
```

### 4. PDG for Batch Processing

PDG (Procedural Dependency Graph) enables massive parallel generation:

**HDA Processor Node:**
- Generates work items from parameter variations
- Distributes across available cores/machines
- Handles dependencies automatically
- Reports progress per-item

**Example: Generate 1000 Rock Variations:**

```python
# PDG graph setup
import pdg

# Create work items for each variation
for seed in range(1000):
    work_item = pdg.WorkItem()
    work_item.setIntAttrib("seed", seed)
    work_item.setFloatAttrib("erosion", random.uniform(0.1, 0.9))
    work_item.setStringAttrib("output", f"/output/rock_{seed:04d}.glb")
```

---

## Bannou Integration Architecture

### New Service: lib-procedural

```
plugins/lib-procedural/
├── Generated/
│   ├── ProceduralController.cs
│   ├── IProceduralService.cs
│   └── ProceduralModels.cs
├── ProceduralService.cs           # Business logic
├── HoudiniClient.cs               # HTTP client for Houdini server
└── lib-procedural.csproj
```

### Schema Design (schemas/procedural-api.yaml)

```yaml
openapi: 3.0.3
info:
  title: Procedural Generation Service
  version: 1.0.0
  description: |
    On-demand procedural 3D asset generation using Houdini Digital Assets (HDAs).

servers:
  - url: http://localhost:5012

paths:
  /procedural/generate:
    post:
      operationId: generate
      summary: Generate a 3D asset from an HDA template
      x-permissions: [procedural.generate]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GenerateRequest'
      responses:
        '200':
          description: Generation started (async) or completed (sync)
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/GenerateResponse'

  /procedural/templates:
    post:
      operationId: listTemplates
      summary: List available HDA templates
      x-permissions: [procedural.read]
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TemplateListResponse'

  /procedural/templates/inspect:
    post:
      operationId: inspectTemplate
      summary: Get parameter schema for an HDA template
      x-permissions: [procedural.read]
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/InspectTemplateRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/TemplateSchema'

  /procedural/jobs/status:
    post:
      operationId: getJobStatus
      summary: Get status of an async generation job
      x-permissions: [procedural.read]
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/JobStatusRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/JobStatusResponse'

  /procedural/batch:
    post:
      operationId: batchGenerate
      summary: Generate multiple asset variations
      x-permissions: [procedural.batch]
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/BatchGenerateRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/BatchGenerateResponse'

components:
  schemas:
    GenerateRequest:
      type: object
      required: [template_id, parameters]
      properties:
        template_id:
          type: string
          format: uuid
          description: Asset service ID of the HDA template
        parameters:
          type: object
          additionalProperties: true
          description: Parameter name-value pairs
        output_format:
          $ref: '#/components/schemas/OutputFormat'
        async:
          type: boolean
          default: false
          description: Return immediately with job ID
        seed:
          type: integer
          description: Random seed for deterministic output
        create_bundle:
          type: boolean
          default: true
          description: Package result in .bannou bundle

    GenerateResponse:
      type: object
      properties:
        job_id:
          type: string
          format: uuid
          description: Job ID for async requests
        status:
          $ref: '#/components/schemas/JobStatus'
        asset_id:
          type: string
          format: uuid
          description: Asset service ID of generated model (sync only)
        bundle_id:
          type: string
          format: uuid
          description: Bundle ID if create_bundle was true
        generation_time_ms:
          type: integer
          description: Time taken to generate (sync only)

    OutputFormat:
      type: string
      enum: [glb, gltf, fbx, obj, usd, bgeo]
      default: glb
      description: Export format for generated geometry

    JobStatus:
      type: string
      enum: [queued, processing, completed, failed]

    TemplateSchema:
      type: object
      properties:
        template_id:
          type: string
          format: uuid
        name:
          type: string
        description:
          type: string
        parameters:
          type: array
          items:
            $ref: '#/components/schemas/ParameterDefinition'

    ParameterDefinition:
      type: object
      properties:
        name:
          type: string
        label:
          type: string
        type:
          $ref: '#/components/schemas/ParameterType'
        default:
          description: Default value (type varies)
        min:
          type: number
        max:
          type: number
        menu_items:
          type: array
          items:
            type: string
          description: For menu type parameters

    ParameterType:
      type: string
      enum: [float, int, string, toggle, menu, color, ramp, float2, float3, float4]

    BatchGenerateRequest:
      type: object
      required: [template_id, variations]
      properties:
        template_id:
          type: string
          format: uuid
        variations:
          type: array
          items:
            type: object
            additionalProperties: true
          description: Array of parameter sets, one per variation
        output_format:
          $ref: '#/components/schemas/OutputFormat'
        create_metabundle:
          type: boolean
          default: true
          description: Combine all outputs into a metabundle
```

### Integration Flow

```
┌────────────────────────────────────────────────────────────────────┐
│                     BANNOU SERVICE MESH                            │
├────────────────────────────────────────────────────────────────────┤
│                                                                    │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐         │
│  │   Connect    │    │  Procedural  │    │    Asset     │         │
│  │   Service    │───►│   Service    │◄──►│   Service    │         │
│  │  (WebSocket) │    │  (lib-proc)  │    │  (lib-asset) │         │
│  └──────────────┘    └──────┬───────┘    └──────────────┘         │
│                             │                                      │
│                             │ HTTP                                 │
│                             ▼                                      │
│  ┌─────────────────────────────────────────────────────────┐      │
│  │              HOUDINI PROCESSING POOL                     │      │
│  │                                                          │      │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐               │      │
│  │  │ Houdini  │  │ Houdini  │  │ Houdini  │  ...          │      │
│  │  │ Worker 1 │  │ Worker 2 │  │ Worker N │               │      │
│  │  │ :8008    │  │ :8008    │  │ :8008    │               │      │
│  │  └──────────┘  └──────────┘  └──────────┘               │      │
│  │                                                          │      │
│  │  Managed by Orchestrator as processing pool              │      │
│  └─────────────────────────────────────────────────────────┘      │
│                                                                    │
└────────────────────────────────────────────────────────────────────┘
```

### Using Orchestrator Processing Pools

The existing Orchestrator service already supports processing pools - we can use this for Houdini workers:

```csharp
// In ProceduralService.cs
public async Task<(StatusCodes, GenerateResponse?)> GenerateAsync(GenerateRequest request)
{
    // 1. Fetch HDA from Asset Service
    var (status, hdaAsset) = await _assetClient.GetAssetAsync(new GetAssetRequest
    {
        AssetId = request.TemplateId
    });

    if (status != StatusCodes.OK || hdaAsset == null)
        return (StatusCodes.NotFound, null);

    // 2. Acquire Houdini worker from pool
    var (poolStatus, worker) = await _orchestratorClient.AcquirePoolWorkerAsync(
        new AcquirePoolWorkerRequest
        {
            PoolName = "houdini-generation",
            TimeoutSeconds = 300
        });

    if (poolStatus != StatusCodes.OK || worker == null)
        return (StatusCodes.ServiceUnavailable, null);

    try
    {
        // 3. Download HDA to worker (or use shared storage)
        var hdaUrl = await _assetClient.GetPresignedDownloadUrlAsync(request.TemplateId);

        // 4. Call Houdini worker API
        var houdiniResponse = await _httpClient.PostAsJsonAsync(
            $"http://{worker.Endpoint}:8008/api/generate_asset",
            new
            {
                hda_url = hdaUrl,
                parameters = request.Parameters,
                output_format = request.OutputFormat.ToString().ToLower(),
                seed = request.Seed ?? Random.Shared.Next()
            });

        var geometryData = await houdiniResponse.Content.ReadAsByteArrayAsync();

        // 5. Upload result to Asset Service
        var (uploadStatus, uploadResponse) = await _assetClient.UploadAssetAsync(
            new UploadAssetRequest
            {
                Name = $"generated_{request.TemplateId}_{DateTime.UtcNow:yyyyMMddHHmmss}",
                ContentType = GetContentType(request.OutputFormat),
                Data = geometryData
            });

        // 6. Optionally create bundle
        Guid? bundleId = null;
        if (request.CreateBundle)
        {
            var (bundleStatus, bundle) = await _assetClient.CreateBundleAsync(
                new CreateBundleRequest
                {
                    Name = $"proc_bundle_{uploadResponse!.AssetId}",
                    AssetIds = new[] { uploadResponse.AssetId }
                });
            bundleId = bundle?.BundleId;
        }

        return (StatusCodes.OK, new GenerateResponse
        {
            Status = JobStatus.Completed,
            AssetId = uploadResponse!.AssetId,
            BundleId = bundleId,
            GenerationTimeMs = (int)stopwatch.ElapsedMilliseconds
        });
    }
    finally
    {
        // 7. Release worker back to pool
        await _orchestratorClient.ReleasePoolWorkerAsync(
            new ReleasePoolWorkerRequest { WorkerId = worker.WorkerId });
    }
}
```

---

## Licensing & Costs

### Important Distinction: Houdini vs Houdini Engine

| Product | Annual Cost | Purpose |
|---------|-------------|---------|
| **Houdini Indie** | $269/year | Full GUI for *creating* HDAs (required for authoring) |
| **Houdini Engine Indie** | **FREE** | Headless batch processing - *running* HDAs only |
| Houdini FX (Commercial) | ~$4,500/year | Full GUI, commercial use (>$100K revenue) |

**For our server use case**: We use **Houdini Engine** (not full Houdini) since servers only *execute* pre-made HDAs. Someone still needs full Houdini to *author* the HDAs.

### Houdini Engine License Tiers (Server-Side)

| Tier | Annual Cost | Use Case |
|------|-------------|----------|
| **Houdini Engine Indie** | **Free** | < $100K annual revenue, up to 3 machines |
| Houdini Engine Workstation | $499/year | Single machine, commercial |
| Houdini Engine Floating | $795/year | Shared pool, single facility |
| Volume Floating | Contact SideFX | Large deployments |

### Licensing Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    LICENSE SERVER                            │
│                    (sesinetd)                                │
│                                                              │
│  Can be:                                                     │
│  - Dedicated VM (persistent)                                 │
│  - SideFX cloud (login-based licensing)                     │
│                                                              │
│  Requirements:                                               │
│  - Private network only (no public interface)               │
│  - Persistent machine name                                   │
│  - VPN if cloud-hosted                                       │
└─────────────────────────────────────────────────────────────┘
         │
         │ Private Network
         ▼
┌─────────────────────────────────────────────────────────────┐
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                   │
│  │ Houdini  │  │ Houdini  │  │ Houdini  │                   │
│  │ Worker 1 │  │ Worker 2 │  │ Worker N │                   │
│  └──────────┘  └──────────┘  └──────────┘                   │
│                                                              │
│  Each worker checks out license when processing              │
│  License returned when idle                                  │
└─────────────────────────────────────────────────────────────┘
```

### Cost Projection

| Scale | Workers | License Type | Annual Cost |
|-------|---------|--------------|-------------|
| **Indie/Prototype** | 1-3 | Engine Indie (Free) | $0 + $269 authoring |
| Small Production | 4-8 | Floating x2 | $1,590 + authoring |
| Medium Production | 10-20 | Floating x5 | $3,975 + authoring |
| Large Scale | 50+ | Volume | ~$200/seat + authoring |

**Notes**:
- All scales require at least one Houdini workstation for HDA authoring ($269 Indie or ~$4,500 commercial)
- Houdini Engine licenses include batch processing capability (merged with Houdini Batch license)
- Indie tier requires <$100K annual gross revenue

---

## Implementation Phases

### Phase 1: Proof of Concept (1-2 weeks)

**Goals:**
- Stand up single Houdini container with hwebserver
- Create simple rock generator HDA
- Test generation API manually
- Validate output quality and performance

**Deliverables:**
- Docker Compose with Houdini worker
- Basic hwebserver script
- Sample HDA
- Performance benchmarks

### Phase 2: Bannou Integration (2-3 weeks)

**Goals:**
- Create lib-procedural service
- Integrate with Asset Service for HDA storage
- Integrate with Orchestrator for worker pool
- WebSocket capability manifest support

**Deliverables:**
- lib-procedural plugin
- Schema (procedural-api.yaml)
- Unit tests
- HTTP integration tests

### Phase 3: Production Features (2-3 weeks)

**Goals:**
- Async job queue with status tracking
- Batch generation via PDG
- Template validation and caching
- Error handling and retry logic

**Deliverables:**
- Redis-backed job queue
- PDG integration for batch
- Comprehensive error handling
- Load testing results

### Phase 4: Advanced Features (ongoing)

**Goals:**
- Texture generation (Substance integration)
- LOD generation pipelines
- Client-side HDA preview (lightweight)
- Usage analytics and optimization

---

## Challenges & Mitigations

### Challenge 1: Cold Start Latency

**Problem**: Houdini startup + HDA loading can take 5-30 seconds.

**Mitigations:**
- Keep warm pool of pre-initialized workers
- Pre-load common HDAs on worker startup
- Cache installed HDAs in worker containers
- Use in-memory session persistence between requests

### Challenge 2: License Server Reliability

**Problem**: License server downtime = no generation.

**Mitigations:**
- Use SideFX cloud licensing (login-based)
- Redundant license server setup
- Graceful degradation (queue jobs until licenses available)
- Monitor license usage proactively

### Challenge 3: Memory/Resource Usage

**Problem**: Complex HDAs can use significant RAM/CPU.

**Mitigations:**
- Set resource limits per worker container
- Implement timeouts for generation
- Monitor and kill runaway processes
- Queue management to prevent overload

### Challenge 4: HDA Compatibility

**Problem**: HDAs may have version dependencies or require specific plugins.

**Mitigations:**
- Version-tag Docker images matching HDA requirements
- Include common plugins in base image
- Validate HDAs on upload
- Store HDA metadata including Houdini version requirements

### Challenge 5: Output Consistency

**Problem**: Same parameters should produce identical results.

**Mitigations:**
- Always require explicit seed parameter
- Document determinism requirements in HDA guidelines
- Test reproducibility in CI
- Cache generated assets by hash(template_id + parameters + seed)

---

## Example Use Cases

### 1. Dynamic Rock/Boulder Generation

```json
// Request
{
  "template_id": "uuid-of-rock-generator-hda",
  "parameters": {
    "seed": 42,
    "base_shape": "boulder",
    "size": [2.0, 1.5, 2.0],
    "detail_level": 3,
    "erosion": 0.6,
    "moss_coverage": 0.3
  },
  "output_format": "glb"
}

// Response
{
  "status": "completed",
  "asset_id": "uuid-of-generated-rock",
  "bundle_id": "uuid-of-bundle",
  "generation_time_ms": 2340
}
```

### 2. Procedural Building Facades

```json
{
  "template_id": "uuid-of-building-hda",
  "parameters": {
    "seed": 12345,
    "style": "victorian",
    "floors": 4,
    "window_density": 0.7,
    "balconies": true,
    "weathering": 0.4,
    "width_meters": 12.0
  },
  "output_format": "glb"
}
```

### 3. Batch Tree Variations

```json
{
  "template_id": "uuid-of-tree-hda",
  "variations": [
    { "seed": 1, "species": "oak", "age": "mature", "health": 0.9 },
    { "seed": 2, "species": "oak", "age": "mature", "health": 0.7 },
    { "seed": 3, "species": "oak", "age": "young", "health": 1.0 },
    // ... 97 more variations
  ],
  "output_format": "glb",
  "create_metabundle": true
}

// Response
{
  "job_id": "uuid-of-batch-job",
  "status": "processing",
  "total_variations": 100,
  "completed": 0
}
```

### 4. Terrain Chunk Generation

```json
{
  "template_id": "uuid-of-terrain-hda",
  "parameters": {
    "seed": 999,
    "chunk_x": 10,
    "chunk_y": 15,
    "biome": "alpine",
    "resolution": 256,
    "include_collision_mesh": true,
    "texture_resolution": 2048
  },
  "output_format": "glb",
  "async": true
}
```

---

## Research Sources

### Official SideFX Documentation

- [Introduction to Digital Assets](https://www.sidefx.com/docs/houdini/assets/intro.html) - HDA fundamentals
- [hwebserver Module](https://www.sidefx.com/docs/houdini/hwebserver/index.html) - Built-in HTTP server
- [hwebserver.apiFunction](https://www.sidefx.com/docs/houdini/hwebserver/apiFunction.html) - API endpoint decorator
- [Houdini Engine Overview](https://www.sidefx.com/products/houdini-engine/) - Engine capabilities
- [Houdini Engine Batch](https://www.sidefx.com/products/houdini-engine/batch/) - Batch processing
- [PDG/TOPs Introduction](https://www.sidefx.com/docs/houdini/tops/intro.html) - Procedural dependency graphs
- [HDA Processor TOP](https://www.sidefx.com/docs/houdini/nodes/top/hdaprocessor.html) - Batch HDA processing
- [glTF Export](https://www.sidefx.com/docs/houdini/io/gltf.html) - Export formats
- [Houdini APIs](https://www.sidefx.com/docs/houdini/ref/api.html) - API overview
- [Houdini Engine API (HAPI)](https://www.sidefx.com/docs/hengine/_h_a_p_i__integration.html) - C API integration
- [Operator Type Properties](https://www.sidefx.com/docs/houdini/ref/windows/optype.html) - Parameter types

### Community & Third-Party

- [Houdini-Docker GitHub](https://github.com/aaronsmithtv/Houdini-Docker) - Containerization project
- [HoudiniEngineSample GitHub](https://github.com/sideeffects/HoudiniEngineSample) - Official HAPI sample
- [Setting up Houdini on Headless Linux](https://jurajtomori.wordpress.com/2019/03/05/setting-up-houdini-on-a-headless-linux-server/) - Server setup guide
- [Houdini HDA Best Practices (tokeru.com)](https://tokeru.com/cgwiki/HoudiniHDA.html) - HDA tips
- [The Beginner's Guide to HDAs: Parameters](https://www.artstation.com/blogs/julianbragagna/yVO4/the-beginners-guide-to-hdas-parameters) - Parameter guide

### Licensing & Pricing

- [Houdini Store](https://www.sidefx.com/buy/) - Current pricing
- [Houdini Engine FAQs](https://www.sidefx.com/faq/houdini-engine-faq/) - Engine licensing details
- [Houdini Indie](https://www.sidefx.com/products/houdini-indie/) - Indie tier details
- [Cloud Usage FAQ](https://www.sidefx.com/faq/question/cloud/) - Cloud deployment licensing

### Cloud & Pipeline

- [AWS Deadline Cloud](https://aws.amazon.com/deadline-cloud/faqs/) - AWS render management
- [GridMarkets Houdini](https://www.gridmarkets.com/houdini-rendering) - Cloud rendering service
- [PDG in Game Pipelines (80.lv)](https://80.lv/articles/exploring-houdini-use-of-pdg-in-game-pipelines) - Game dev workflows
- [Houdini for 3D World Model Training (Medium)](https://medium.com/design-bootcamp/houdini-a-procedural-powerhouse-for-3d-world-model-training-53f6cea7c04b) - Procedural generation at scale

---

## Conclusion

Integrating Houdini as a procedural generation backend for Bannou is not only feasible but relatively straightforward given:

1. **Built-in Infrastructure**: hwebserver provides production-ready HTTP API capability
2. **Containerization Support**: Maintained Docker images optimized for headless operation
3. **Existing Patterns**: Orchestrator processing pools can manage Houdini workers
4. **Reasonable Costs**: Free for indie, affordable for production
5. **Rich Ecosystem**: Extensive documentation, community resources, and examples

This would genuinely differentiate Bannou - offering on-demand procedural 3D generation as an API is something few (if any) game services provide. The combination of:
- HDA-based procedural templates stored in Asset Service
- Headless Houdini workers managed by Orchestrator
- Results automatically bundled and stored for retrieval
- Full WebSocket integration for real-time generation requests

...creates a powerful, unique capability that leverages Bannou's existing architecture perfectly.

**Recommended Next Step**: Start with Phase 1 proof-of-concept - a single Docker container running hwebserver with a simple rock generator HDA, validating the basic generation loop before integrating with Bannou services.
