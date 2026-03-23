# Plan: Implement MCP Server Service (L3 App Features)

> **Plugin**: lib-mcp
> **Layer**: L3 App Features
> **Status**: Phase 1 complete
> **Created**: 2026-03-21
> **Phase 1 Completed**: 2026-03-23
> **NuGet**: `ModelContextProtocol.AspNetCore` v1.1.0

## Context

The MCP (Model Context Protocol) server is a new L3 App Features plugin that exposes a standards-compliant MCP endpoint for AI agents to query documentation, schemas, service capabilities, and game-specific knowledge. Game server operators deploy it alongside their game — AI integrations can describe the game, its servers, or the underlying Bannou architecture.

The plugin ships with **seed knowledge** — Bannou's own documentation compiled into embedded resources — and accepts studio-authored content at runtime. When lib-documentation (L3) is also enabled, the MCP server gains enhanced full-text search and git-synced namespace capabilities via soft dependency.

**Why a standalone plugin**: lib-mcp works even without Documentation or Website enabled. A deployment with just L0 + L1 + lib-mcp gives you an MCP-capable server that serves platform knowledge from seed data. This is the expected baseline for a mature platform — every node can explain itself.

**Why not Documentation or Website**: Documentation is a full content management service with CRUD, git-sync, trashcan, and namespaces — MCP is a transport/protocol concern. Website is browser-facing HTML — MCP is programmatic. lib-mcp composes with both without being either.

**Relationship to local `.claude/mcp`**: The local MCP server (`.claude/mcp/server.mjs`) is a development-time tool for Claude Code. lib-mcp is a runtime service for deployed game servers. Phase 1 enhances the local server as a prerequisite — the tools built there inform the seed data generation and become the development-time complement to the runtime service.

### Design Decisions

- **MCP tool definitions are configuration-driven**: The available MCP tools are determined by seed data + configuration, following the same pattern as `.claude/mcp/server.mjs` where tool registrations define the available capabilities.
- **Seed data uses `EmbeddedResourceProvider`**: Following the established pattern from lib-actor's `BehaviorSeededResourceProvider` — compile documentation into embedded resources, expose via `ISeededResourceProvider`.
- **HTTP transport, not stdio**: The runtime service uses `ModelContextProtocol.AspNetCore` (HTTP Streamable / SSE transport) integrated into the existing ASP.NET Core pipeline. Not browser-facing (all POST-based JSON-RPC) — does NOT require T15 exception.
- **The MCP spec IS the standard**: Tool definitions follow the MCP specification (2025-11-25) exactly — the same JSON Schema format used by `.claude/mcp/server.mjs`, the SignalWire MCP server, and every other MCP implementation.

### Open Questions

| Question | Status | Notes |
|----------|--------|-------|
| MCP endpoint path — `/mcp` or configurable? | Resolved: configurable | Base path from configuration, defaults to `/mcp`. Optional subdomain support via nullable `McpSubdomain` config property. |
| How does MCP middleware coexist with Bannou's controller pipeline? | **Resolved: confirmed** | `MapMcp()` uses ASP.NET Core endpoint routing — same mechanism as `MapControllers()`. Bannou controllers use `/service/method` routes; MCP uses `/mcp`. No conflict. lib-telemetry's `ConfigureWebPipeline` + `MapPrometheusScrapingEndpoint("/metrics")` is the exact reference implementation. |
| Which plugin lifecycle hook maps the MCP endpoint? | **Resolved** | `IBannouPlugin.ConfigureWebPipeline(WebApplication app)` — web-only hook, receives `WebApplication` (implements `IEndpointRouteBuilder`). Called AFTER `UseRouting().UseEndpoints()` in `Program.cs` line 342. lib-telemetry already uses this for Prometheus. |
| Should tool definitions live in schema or be purely runtime? | Resolved: hybrid | Core tool templates are seed data (embedded). Studio adds custom tools via API. All stored in state store at runtime. |
| Does NGINX need special config for MCP? | Resolved: yes | Standard location block proxying `/mcp` to the backend. SSE needs `proxy_buffering off` and chunked transfer encoding support. |
| `ModelContextProtocol.AspNetCore` targets .NET 8.0+ — compatible with net9.0? | Resolved: yes | NuGet metadata confirms net9.0 target. |

---

## Implementation Phases

### Phase 1: Local MCP Server Enhancement (`.claude/mcp/server.mjs`) — ✅ COMPLETE

**Goal**: Add developer-facing tools to the existing local MCP server. These tools serve two purposes: (1) immediate value for agent investigations and implementations, (2) informing the seed data format and becoming the "shared helpers" that the seed generation tool calls.

**Status**: Complete as of 2026-03-23. All tools implemented and syntax-validated.

#### 1a. Shared Helper Architecture — ✅ COMPLETE

Refactored from monolithic `server.mjs` into modular `helpers/` directory. Each helper is an ES module with pure async functions callable from both registered tools and (future) seed generation.

**Implemented helper files:**

| File | Functions | Purpose |
|------|-----------|---------|
| `helpers/file-ops.mjs` | Read tracking, gate checks, chunking | Core file operations |
| `helpers/structure.mjs` | `validateStructure()`, `formatValidationResults()` | C# structural validation |
| `helpers/commands.mjs` | `execAsync()`, `validateCommand()`, `checkCommandGate()` | Command whitelist & execution |
| `helpers/sentinel.mjs` | `processSentinel()`, `isPathProtected()` | External state injection |
| `helpers/scripts.mjs` | `writeScript()` | Sandboxed script creation |
| `helpers/context.mjs` | `prepareContext()`, `resolveProfile()` | Context composite preparation |
| `helpers/plugins.mjs` | `getPluginCatalog()`, `getPluginDocs()` | Plugin listing & docs |
| `helpers/docs.mjs` | `getDocumentCatalog()`, `getDocument()`, `searchDocs()` | Doc catalog, retrieval, search |
| `helpers/schemas.mjs` | `getSchemaCatalog()`, `getSchema()` | Schema listing & retrieval |
| `helpers/infrastructure.mjs` | `getServiceDetails()`, `getEventCatalog()`, `getStateStoreCatalog()`, `getConfigurationCatalog()`, `printModelShapes()`, `printInterfaceShapes()` | Generated references & script execution |

**Additional files (not in original plan):**
- `state.mjs` — Shared mutable state singleton (readFiles, requiredReading, constants)
- `profiles.mjs` — Context profile definitions (dev, plugin, schema) for `prepare_context`

**Design deviation from plan:** `print_models` and `print_interfaces` delegate to existing Python scripts via internal `child_process.exec` rather than porting the parsing logic to JavaScript. This keeps the scripts as the single source of truth — human operators use them via `make`; MCP tools use them server-internally. No new scripts were needed for Makefile.

#### 1b. New Tools — ✅ COMPLETE (12 tools implemented)

All 12 tools registered in `server.mjs` as thin wrappers around helper functions:

| Tool | Helper | File | Description |
|------|--------|------|-------------|
| `list_plugins` | `getPluginCatalog()` | `plugins.mjs` | All plugins by layer with endpoint count & doc availability |
| `get_plugin_docs` | `getPluginDocs(name)` | `plugins.mjs` | Deep dive + implementation map for a plugin |
| `list_documents` | `getDocumentCatalog()` | `docs.mjs` | All 6 catalog files, optionally filtered by category |
| `get_document` | `getDocument(path)` | `docs.mjs` | Specific document by path (relative to project or docs/) |
| `search_docs` | `searchDocs(query)` | `docs.mjs` | Keyword search with lazy-built inverted index, mtime invalidation |
| `list_schemas` | `getSchemaCatalog()` | `schemas.mjs` | All schema files organized by service |
| `get_schema` | `getSchema(name)` | `schemas.mjs` | Specific schema file or all schemas for a service |
| `get_service_details` | `getServiceDetails(layer?)` | `infrastructure.mjs` | Service details, optionally by layer |
| `get_events` | `getEventCatalog()` | `infrastructure.mjs` | Generated events reference |
| `get_state_stores` | `getStateStoreCatalog()` | `infrastructure.mjs` | Generated state stores reference |
| `get_configuration` | `getConfigurationCatalog()` | `infrastructure.mjs` | Generated configuration reference |
| `print_models` | `printModelShapes(plugin)` | `infrastructure.mjs` | Compact model shapes via internal `python3 scripts/print-model-shapes.py` |
| `print_interfaces` | `printInterfaceShapes(name?)` | `infrastructure.mjs` | Interface shapes via internal `python3 scripts/print-interface-shapes.py` |

**Implementation notes (deviations from original plan)**:
- `print_models` and `print_interfaces` execute existing Python scripts via internal `child_process.exec` rather than porting to JavaScript. The scripts remain the single source of truth for both `make` commands and MCP tools.
- The search index is built lazily on first `search_docs` call (not at startup) with mtime-based cache invalidation on the `docs/` directory. This handles long sessions where docs are edited mid-conversation.
- `prepare_context` was added as a bonus tool (not in original plan) for efficient bulk context loading with profile-based file sets and sentinel integration.

---

### Phase 2: Seed Data Generation

**Goal**: Build a tool in `.claude/mcp/server.mjs` that calls the Phase 1 shared helpers in sequence to produce a compiled documentation bundle suitable for embedding in the lib-mcp assembly.

#### 2a. `generate_docs_seed` Tool

A new tool registered in `server.mjs` that:

1. Calls each shared helper from Phase 1 in sequence
2. Collects all output into a structured bundle
3. Writes the bundle to a file (`plugins/lib-mcp/Resources/bannou-seed.json`)

```javascript
server.registerTool("generate_docs_seed", {
  description: "Generate the Bannou documentation seed bundle for lib-mcp embedded resources",
  inputSchema: {},
}, async () => {
  const bundle = {
    generatedAt: new Date().toISOString(),
    version: "1.0",
    pluginCatalog: getPluginCatalog(),
    documents: {}, // All docs indexed by path
    schemas: {},   // All schemas indexed by name
    catalogs: {},  // All generated catalogs
    modelShapes: {},  // Per-plugin model shapes
    serviceDetails: getServiceDetails(),
    events: getEventCatalog(),
    stateStores: getStateStoreCatalog(),
    configuration: getConfigurationCatalog(),
  };

  // Iterate all documents
  const docPaths = getAllDocumentPaths();
  for (const path of docPaths) {
    bundle.documents[path] = getDocument(path);
  }

  // Write bundle
  await writeFile(bundlePath, JSON.stringify(bundle), "utf-8");
  return { content: [{ type: "text", text: `Seed bundle generated: ${bundlePath}` }] };
});
```

#### 2b. Bundle Format

The seed bundle is a single JSON file containing all documentation, schemas, and metadata. It's compressed during the .NET build into the embedded resource. The format mirrors what the runtime MCP tools will serve:

```json
{
  "generatedAt": "2026-03-21T12:00:00Z",
  "version": "1.0",
  "pluginCatalog": [ { "name": "account", "layer": "AppFoundation", "endpoints": 18, ... } ],
  "documents": {
    "docs/plugins/ACCOUNT.md": { "content": "...", "category": "plugin-deep-dive" },
    "docs/maps/ACCOUNT.md": { "content": "...", "category": "implementation-map" }
  },
  "schemas": {
    "account-api": { "content": "...", "endpoints": [...] },
    "account-service-events": { "content": "...", "topics": [...] }
  },
  "catalogs": {
    "guides": "...",
    "planning": "...",
    "faqs": "...",
    "operations": "...",
    "specifications": "...",
    "sdks": "..."
  },
  "modelShapes": {
    "account": "* AccountId: Guid\n  DisplayName: string\n  ..."
  },
  "serviceDetails": { ... },
  "events": { ... },
  "stateStores": { ... },
  "configuration": { ... }
}
```

#### 2c. Build Integration

Add a Makefile target:

```makefile
generate-mcp-seed: ## Generate MCP documentation seed bundle
	@echo "Generating MCP seed bundle..."
	# Invoke the MCP server tool (or direct Node.js call)
	@node -e "..." # Or integrate with the local MCP server
```

**Alternative**: The seed generation could also be a standalone Node.js script (`scripts/generate-mcp-seed.mjs`) that imports the shared helpers from `.claude/mcp/server.mjs` — this avoids needing the MCP server running to generate the bundle.

**Files created**: `plugins/lib-mcp/Resources/bannou-seed.json` (generated, not checked in — add to `.gitignore` or regenerate on build)

**Files modified**: `.claude/mcp/server.mjs`, `Makefile` (both frozen — requires approval)

---

### Phase 3: Create lib-mcp Plugin

**Goal**: Full L3 plugin following schema-first development.

#### 3a. Schema Files

##### `schemas/mcp-api.yaml`
```yaml
openapi: 3.0.4
info:
  title: MCP Server Service API
  version: 1.0.0
x-service-layer: AppFeatures
servers:
  - url: http://localhost:5012
```

Endpoints for managing the MCP server's tool registry and content:

| Endpoint | Permission | Purpose |
|----------|-----------|---------|
| `POST /mcp/tools/list` | `[]` | List registered MCP tool definitions |
| `POST /mcp/tools/register` | `[role: developer]` | Register a custom MCP tool definition |
| `POST /mcp/tools/update` | `[role: developer]` | Update a tool definition |
| `POST /mcp/tools/delete` | `[role: developer]` | Remove a custom tool definition |
| `POST /mcp/content/seed` | `[role: admin]` | Trigger re-loading of seed content |
| `POST /mcp/content/list` | `[]` | List available content categories |
| `POST /mcp/content/search` | `[]` | Search content across all categories |
| `POST /mcp/content/get` | `[]` | Get specific content by identifier |
| `POST /mcp/status` | `[]` | Server status (loaded tools, content stats) |

**Note**: These are the *Bannou API endpoints* for managing the MCP server. The actual MCP protocol endpoint (`/mcp` or configured path) is handled by `ModelContextProtocol.AspNetCore` middleware and is NOT a Bannou controller endpoint — it's a separate HTTP handler in the ASP.NET Core pipeline.

##### `schemas/mcp-service-events.yaml`
- `x-lifecycle` for `McpToolDefinition` entity (created/updated/deleted)
- `x-event-publications`: `mcp.content.seeded`, `mcp.tool.invoked`

##### `schemas/mcp-configuration.yaml`

| Property | Default | Purpose |
|----------|---------|---------|
| `McpEndpointPath` | `/mcp` | Base path for MCP HTTP Streamable transport |
| `McpSubdomain` | `null` | Optional subdomain (e.g., `mcp.yourgame.com`) — nullable |
| `McpStatelessMode` | `false` | Disables session tracking for load balancing without sticky sessions |
| `McpSessionIdleTimeoutSeconds` | `7200` | Duration before idle MCP session is terminated (2 hours) |
| `McpMaxIdleSessions` | `10000` | Maximum idle sessions tracked in memory |
| `EnableBannouDocs` | `true` | Expose built-in Bannou platform documentation tools |
| `EnableGameDocs` | `true` | Expose game-specific documentation tools |
| `EnableSchemaIntrospection` | `false` | Expose OpenAPI schema browsing tools |
| `EnableDocumentationIntegration` | `true` | Integrate with lib-documentation when available |
| `SeedBundlePath` | `null` | Override path for seed bundle (null = use embedded resource) — nullable |
| `MaxSearchResults` | `20` | Maximum results from search tools |
| `ContentCacheTtlSeconds` | `300` | TTL for content cache |

##### `schemas/state-stores.yaml` (addition)

```yaml
mcp-tools:
  backend: redis
  service: Mcp
  purpose: Registered MCP tool definitions (seeded + custom)

mcp-content:
  backend: redis
  service: Mcp
  purpose: Content index and cached content for MCP tool responses
```

#### 3b. Generate and Scaffold

```bash
cd scripts && ./generate-service.sh mcp
```

This creates the full plugin scaffold: project, controllers, interfaces, models, clients, test project.

#### 3c. Plugin Implementation

##### `McpServicePlugin.cs`

Extends `BaseBannouPlugin` because it needs custom DI registration (`AddMcpServer`) in `ConfigureServices`. Uses `ConfigureWebPipeline(WebApplication)` — the web-specific pipeline hook — to map the MCP endpoint. This follows the exact pattern established by lib-telemetry's `MapPrometheusScrapingEndpoint("/metrics")`.

**Reference implementation**: `plugins/lib-telemetry/TelemetryServicePlugin.cs` lines 86-91.

```csharp
public class McpServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "mcp";
    public override string DisplayName => "MCP Server";

    public override void ConfigureServices(IServiceCollection services)
    {
        // Register MCP server with AspNetCore HTTP Streamable transport
        services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                // HttpServerTransportOptions configuration
                // IdleTimeout, Stateless, MaxIdleSessionCount configured here
                // or via McpServiceConfiguration in ConfigureWebPipeline
            })
            .WithToolsFromAssembly(); // Discovers [McpServerToolType] classes
    }

    /// <summary>
    /// Maps the MCP protocol endpoint in the ASP.NET Core pipeline.
    /// Called only in web hosting mode (not embedded).
    /// </summary>
    /// <remarks>
    /// Uses the same lifecycle hook as lib-telemetry's Prometheus endpoint.
    /// Called after UseRouting().UseEndpoints() in Program.cs,
    /// so controller routes are already mapped — no conflict.
    /// WebApplication implements IEndpointRouteBuilder, so MapMcp() works directly.
    /// </remarks>
    public void ConfigureWebPipeline(WebApplication app)
    {
        var config = app.Services.GetRequiredService<McpServiceConfiguration>();
        app.MapMcp(config.McpEndpointPath ?? "/mcp");
    }
}
```

**Pipeline order** (from `Program.cs`):
1. `webApp.UseRouting().UseEndpoints()` — maps controller routes (line 334)
2. `PluginLoader.ConfigureApplication(webApp.Services)` — deployment-agnostic config (line 341)
3. `PluginLoader.ConfigureWebPipeline(webApp)` — **MCP endpoint mapped here** (line 342)

**Transport options** (`HttpServerTransportOptions`):

| Option | Default | Config Property | Purpose |
|--------|---------|----------------|---------|
| `Stateless` | `false` | `McpStatelessMode` | Disables session tracking — enables load balancing without sticky sessions |
| `IdleTimeout` | 2 hours | `McpSessionIdleTimeoutSeconds` | Duration before idle session is terminated |
| `MaxIdleSessionCount` | 10,000 | `McpMaxIdleSessions` | Cap on tracked idle sessions |
| `ConfigureSessionOptions` | null | — | Per-session MCP options from HttpContext (e.g., per-tenant tool sets) |
| `EventStreamStore` | null | — | SSE resumability via `IDistributedCache` (Redis-backed) |

##### `McpService.cs`

Standard Bannou service with:

**Constructor dependencies**:
- `IStateStoreFactory` — tool definitions and content stores
- `IMessageBus` — event publishing
- `ILogger<McpService>` — structured logging
- `McpServiceConfiguration` — typed config
- `IEventConsumer` — event handler registration
- `ITelemetryProvider` — span instrumentation
- `IServiceProvider` — soft dependency resolution for lib-documentation

**Soft dependencies** (resolved at runtime):
- `IDocumentationClient` — enhanced search when lib-documentation is enabled

##### MCP Tool Registration (`Services/McpToolService.cs`)

A `[BannouHelperService]` that bridges between Bannou's tool definition store and `ModelContextProtocol`'s tool registration:

- At startup, loads tool definitions from state store (seeded + custom)
- Registers each as an MCP tool with the `ModelContextProtocol` server
- Tool invocations route through this service, which dispatches to the appropriate content handler

##### Content Handlers (`Services/McpContentHandlers.cs`)

A `[BannouHelperService]` with handler methods for each tool category:

- `HandleSearchAsync(query)` — dispatches to lib-documentation client (if available) or local content index
- `HandleGetDocumentAsync(path)` — returns content from seed data or state store
- `HandleListPluginsAsync()` — returns plugin catalog from seed data
- `HandleGetSchemaAsync(name)` — returns schema content from seed data

##### Seeded Resource Provider (`Providers/McpDocsSeedProvider.cs`)

```csharp
[BannouHelperService("mcp-docs-seed", typeof(IMcpService), typeof(ISeededResourceProvider),
    lifetime: ServiceLifetime.Singleton)]
public sealed class McpDocsSeedProvider : EmbeddedResourceProvider
{
    public override string ResourceType => "mcp-docs";
    public override string ContentType => "application/json";
    protected override Assembly ResourceAssembly => typeof(McpDocsSeedProvider).Assembly;
    protected override string ResourcePrefix => "BeyondImmersion.BannouService.Mcp.Resources.";
}
```

##### Seed Loading (in `McpServicePlugin.OnRunningAsync`)

```csharp
// Load seed bundle from embedded resources
var seedProvider = scope.ServiceProvider.GetService<McpDocsSeedProvider>();
var bundle = await seedProvider.GetSeededAsync("bannou-seed", ct);
if (bundle != null)
{
    var content = bundle.GetContentAsString();
    await mcpService.LoadSeedBundleAsync(content, ct);
}
```

#### 3d. NuGet Dependencies

Add to `lib-mcp.csproj`:

```xml
<PackageReference Include="ModelContextProtocol.AspNetCore" Version="1.1.0" />
```

**License check (T18)**: ModelContextProtocol is MIT licensed. Confirmed compatible.

#### 3e. Embedded Resources

```xml
<!-- In lib-mcp.csproj -->
<ItemGroup>
  <EmbeddedResource Include="Resources\*.json" />
</ItemGroup>
```

---

### Phase 4: NGINX Configuration

**File**: `provisioning/openresty/nginx.conf`

The existing config uses explicit path routing with OpenResty Lua for dynamic service routing. MCP is a new location block following the same pattern. The key difference from existing locations: SSE requires `proxy_buffering off` (unlike standard HTTP proxying) and `Connection ''` (unlike WebSocket which uses `Connection: upgrade`).

**Reference patterns in existing config**:
- WebSocket (`/connect`): `proxy_http_version 1.1`, `Upgrade`/`Connection` headers, 3600s timeouts
- Asset upload (`/assets/upload`): `proxy_request_buffering off`, 600s timeouts
- Standard HTTP (`/auth/*`): default buffering, standard timeouts

#### 4a. MCP Endpoint Routing

Add between the Connect and Admin endpoint sections:

```nginx
# ============================================================
# MCP SERVER ENDPOINT
# Model Context Protocol HTTP Streamable transport
# POST: Client→Server JSON-RPC messages (response: JSON or SSE)
# GET: Server→Client SSE stream for notifications
# DELETE: Session termination
# Spec: https://modelcontextprotocol.io/specification/2025-03-26/basic/transports
# ============================================================

location /mcp {
    access_by_lua_file /usr/local/openresty/lua/service_route.lua;

    # SSE streaming — MUST NOT buffer responses (events must flush immediately)
    proxy_buffering off;
    proxy_cache off;

    # HTTP/1.1 required for chunked encoding
    proxy_http_version 1.1;

    # Prevent NGINX from adding Connection headers that interfere with SSE
    # (WebSocket uses Connection: upgrade; SSE needs Connection cleared)
    proxy_set_header Connection '';

    # Long timeouts for SSE streams (matches WebSocket pattern at /connect)
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
    proxy_connect_timeout 60s;

    proxy_pass http://$target_host:$target_port;
    proxy_set_header Host $host;
    proxy_set_header X-Real-IP $remote_addr;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header Content-Type $content_type;

    # Pass MCP session management header
    proxy_set_header Mcp-Session-Id $http_mcp_session_id;

    # Pass Accept header — MCP content negotiation
    # (server returns application/json OR text/event-stream based on Accept)
    proxy_set_header Accept $http_accept;
}
```

#### 4b. Optional Subdomain Support

When `McpSubdomain` is configured (e.g., `mcp.yourgame.com`), follows the same virtual host pattern as `nginx.host.conf` (which already has `queue.localhost`, `auth.localhost`, `connect.localhost`):

```nginx
# In provisioning/openresty/nginx.host.conf or generated config
server {
    listen 80;
    server_name mcp.yourgame.com;

    location / {
        # Same SSE/proxy configuration as /mcp location block above
        access_by_lua_file /usr/local/openresty/lua/service_route.lua;
        proxy_buffering off;
        proxy_cache off;
        proxy_http_version 1.1;
        proxy_set_header Connection '';
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
        proxy_connect_timeout 60s;
        proxy_pass http://$target_host:$target_port/mcp;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Content-Type $content_type;
        proxy_set_header Mcp-Session-Id $http_mcp_session_id;
        proxy_set_header Accept $http_accept;
    }
}
```

#### 4c. Comparison with Existing Special Endpoints

| Concern | WebSocket (`/connect`) | SSE/MCP (`/mcp`) | Standard HTTP (`/auth/*`) |
|---------|----------------------|------------------|--------------------------|
| `proxy_http_version` | 1.1 | 1.1 | (default 1.0) |
| `Upgrade` header | `websocket` | not needed | not needed |
| `Connection` header | `"upgrade"` | `''` (cleared) | (default) |
| `proxy_buffering` | default (on) | **off** (critical) | default (on) |
| `proxy_read_timeout` | 3600s | 3600s | default (60s) |
| Session header | — | `Mcp-Session-Id` | `Authorization` |

---

### Phase 5: Documentation Integration (Soft Dependency)

When lib-documentation is enabled, lib-mcp enhances its capabilities:

| Capability | Without Documentation | With Documentation |
|-----------|----------------------|-------------------|
| Content search | Tokenized search over seed data (in-memory index) | Full-text search via `IDocumentationClient` (Redis-backed) |
| Content freshness | Static (from seed bundle at build time) | Live (git-synced, CRUD-updated) |
| Namespace support | Single "bannou" namespace (seed data) | Multiple namespaces (per studio/project) |
| Content management | Read-only seed data + tool CRUD | Full CRUD, import, archive, restore |

**Integration pattern**:

```csharp
// In McpContentHandlers
public async Task<string> HandleSearchAsync(string query, CancellationToken ct)
{
    // Try lib-documentation first (enhanced search)
    var docClient = _serviceProvider.GetService<IDocumentationClient>();
    if (docClient != null && _configuration.EnableDocumentationIntegration)
    {
        try
        {
            var results = await docClient.SearchAsync(
                new SearchDocumentsRequest { Query = query }, ct);
            return FormatSearchResults(results);
        }
        catch (ApiException)
        {
            _logger.LogWarning("Documentation service unavailable, falling back to seed search");
        }
    }

    // Fallback: search seed data
    return SearchSeedContent(query);
}
```

---

### Phase 6: Unit Tests

#### `plugins/lib-mcp.tests/McpServiceTests.cs`

Following TESTING-PATTERNS.md capture pattern:

| Test | What it validates |
|------|-------------------|
| `LoadSeedBundle_ValidBundle_LoadsToolsAndContent` | Seed loading populates state stores |
| `LoadSeedBundle_InvalidJson_ReturnsError` | Graceful error on corrupt seed |
| `RegisterTool_ValidDefinition_SavesAndPublishes` | Custom tool registration |
| `RegisterTool_DuplicateName_ReturnsConflict` | Name uniqueness enforcement |
| `SearchContent_WithDocumentation_UsesDocClient` | Soft dependency integration |
| `SearchContent_WithoutDocumentation_UsesSeedIndex` | Graceful degradation |
| `GetContent_ExistingPath_ReturnsContent` | Content retrieval from seed |
| `GetContent_MissingPath_ReturnsNotFound` | Missing content handling |
| `ListTools_ReturnsSeededAndCustom` | Tool inventory completeness |

---

## Files Created/Modified Summary

### Phase 1 (Local MCP Server)

| File | Action |
|------|--------|
| `.claude/mcp/server.mjs` | Modify (frozen — requires approval): add shared helpers + new tools |

### Phase 2 (Seed Generation)

| File | Action |
|------|--------|
| `.claude/mcp/server.mjs` | Modify: add `generate_docs_seed` tool |
| `plugins/lib-mcp/Resources/bannou-seed.json` | Create (generated artifact) |
| `Makefile` | Modify (frozen — requires approval): add `generate-mcp-seed` target |

### Phase 3 (lib-mcp Plugin)

| File | Action |
|------|--------|
| `schemas/mcp-api.yaml` | Create |
| `schemas/mcp-service-events.yaml` | Create |
| `schemas/mcp-configuration.yaml` | Create |
| `schemas/state-stores.yaml` | Modify (add 2 MCP stores) |
| `plugins/lib-mcp/McpService.cs` | Fill in (generated template) |
| `plugins/lib-mcp/McpService.Models.cs` | Fill in (generated template) |
| `plugins/lib-mcp/McpService.Events.cs` | Create |
| `plugins/lib-mcp/McpServicePlugin.cs` | Fill in (generated template) — `BaseBannouPlugin` with `ConfigureWebPipeline` (lib-telemetry pattern) |
| `plugins/lib-mcp/Services/McpToolService.cs` | Create (`[BannouHelperService]`) |
| `plugins/lib-mcp/Services/McpContentHandlers.cs` | Create (`[BannouHelperService]`) |
| `plugins/lib-mcp/Providers/McpDocsSeedProvider.cs` | Create (`EmbeddedResourceProvider`) |
| `plugins/lib-mcp/lib-mcp.csproj` | Auto-generated + add ModelContextProtocol.AspNetCore |
| `plugins/lib-mcp/Resources/bannou-seed.json` | Generated (from Phase 2) |
| `plugins/lib-mcp.tests/McpServiceTests.cs` | Fill in (generated template) |
| `bannou-service/Generated/Models/McpModels.cs` | Auto-generated |
| `bannou-service/Generated/Clients/McpClient.cs` | Auto-generated |
| `bannou-service/Generated/Events/McpEventsModels.cs` | Auto-generated |

### Phase 4 (NGINX)

| File | Action |
|------|--------|
| `provisioning/openresty/nginx.conf` | Modify: add `/mcp` location block with SSE proxy settings |

### Phase 5 (Integration)

| File | Action |
|------|--------|
| `plugins/lib-mcp/McpService.cs` | Modify: add documentation integration |
| `plugins/lib-mcp/Services/McpContentHandlers.cs` | Modify: add fallback logic |

---

## Verification

1. **Phase 1**: Local MCP tools return correct data — test each tool manually via Claude Code
2. **Phase 2**: `generate_docs_seed` produces valid JSON bundle, embeddable, covers all content
3. **Phase 3**: `dotnet build` compiles. `make test-structural` passes (service hierarchy, constructor validation, key builders). Unit tests pass. MCP endpoint responds to `tools/list` and `tools/call` via HTTP.
4. **Phase 4**: NGINX routes `/mcp` correctly with SSE support
5. **Phase 5**: With Documentation enabled, search returns enhanced results. Without it, seed search works.
6. **End-to-end**: An MCP client (e.g., Claude Desktop configured with HTTP transport) connects to the running service and successfully lists tools, searches docs, retrieves content.

---

## Dependency Graph

```
Phase 1: Local MCP tools (.claude/mcp/server.mjs)
    │
    ├── Shared helpers (getPluginCatalog, getDocumentCatalog, etc.)
    │
    v
Phase 2: Seed generation tool
    │
    ├── Calls shared helpers → writes bundle
    │
    v
Phase 3: lib-mcp plugin ──────────────────┐
    │                                       │
    ├── Embeds seed bundle                  ├── Phase 4: NGINX config
    ├── MCP transport via AspNetCore        │
    ├── Tool registry (state store)         │
    │                                       │
    v                                       v
Phase 5: Documentation integration    Phase 6: Tests
```

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| `ModelContextProtocol.AspNetCore` middleware conflicts with Bannou's pipeline | ~~Blocks Phase 3~~ **Resolved** | Investigation confirmed: `MapMcp()` uses standard ASP.NET Core endpoint routing, same as controllers. lib-telemetry's `ConfigureWebPipeline` + `MapPrometheusScrapingEndpoint` is the exact precedent. No conflict with Bannou controllers, YARP, or Connect WebSocket. |
| Seed bundle size too large for embedded resource | Performance | Compress (GZip) the JSON bundle before embedding. Lazy-load content segments instead of full bundle at startup. |
| MCP spec evolves, breaking tool definitions | Maintenance | Pin to specification version `2025-11-25` (current authoritative schema). Tool definitions are data (seed/state store), not compiled — updateable without redeployment. |
| `.claude/mcp/server.mjs` modifications require frozen file approval | Workflow | Phase 1 is a prerequisite — get approval once, implement all shared helpers in one batch. |

---

## NGINX Exposed Endpoint Note

The MCP HTTP transport endpoint (`/mcp`) is a programmatic API (POST-based JSON-RPC + SSE), not a browser-facing endpoint. It does NOT use GET with path parameters and does NOT require the T15 browser-facing exception. However, it IS a new HTTP endpoint exposed through NGINX that is NOT part of the normal Bannou controller routing — it's handled by `ModelContextProtocol.AspNetCore` middleware.

This should be documented in ENDPOINT-PERMISSION-GUIDELINES.md as a new category alongside the existing Browser-Facing exception:

| Service | Endpoint | Transport | Why Special |
|---------|----------|-----------|-------------|
| MCP | `/mcp` (configurable) | MCP HTTP Streamable / SSE | Protocol-level endpoint handled by `ModelContextProtocol.AspNetCore` middleware, not by Bannou controllers. POST-based JSON-RPC — not browser-facing. No WebSocket GUID. |
