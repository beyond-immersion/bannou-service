# Bannou Service Development Instructions

## ⚠️ GitHub Issues Reference

**When "Issues" or "GH Issues" is mentioned, this ALWAYS refers to the bannou-service repository issues.** Use the GH CLI to check issues:
```bash
gh issue list                  # List open issues
gh issue view <number>         # View specific issue
```
This is NOT a reference to claude-code's issues or any other repository.

---

@CLAUDE-RESTRICTIONS.md

---

## Core Architecture Reference

@docs/BANNOU-DESIGN.md

**Key Points**: All generated files are in `plugins/lib-{service}/Generated/`. Manual files are: `{Service}Service.cs` (business logic), `{Service}ServiceModels.cs` (internal data models), and `{Service}ServiceEvents.cs` (event handlers). Request/response models are generated into `bannou-service/Generated/Models/` — use `make print-models PLUGIN="service"` to inspect them instead of reading generated files directly.

**Implementation Maps**: Every service has an implementation map at `docs/maps/{SERVICE}.md` containing the detailed method-by-method pseudocode, state store key patterns, dependency tables, event inventories, DI service lists, and complete endpoint indexes with routes, roles, mutations, and published events. Deep dives (`docs/plugins/{SERVICE}.md`) provide high-level context (overview, design considerations, quirks, work tracking); implementation maps provide the detailed "what does each method do" specification. **When investigating a specific plugin's behavior, always read its implementation map** — the deep dive alone does not contain endpoint details, dependency tables, or method logic.

@docs/GENERATED-COMPOSITION-REFERENCE.md

@docs/reference/TENETS.md

**On-demand references** (read when needed, not auto-included):
- `docs/reference/HELPERS-AND-COMMON-PATTERNS.md` — Shared helpers, canonical implementation patterns, test validators. **Read this FIRST when searching for the canonical example of any pattern** (background workers, state store access, event publishing, deprecation, cleanup, enum mapping, telemetry, etc.)
- `docs/reference/SERVICE-HIERARCHY.md` — Full hierarchy rules, Variable Provider Factory, DI Provider vs Listener safety, deployment modes (read when designing cross-layer communication)
- `docs/GENERATED-*-SERVICE-DETAILS.md` — Full per-service details by layer (read when investigating a specific layer's services)
- `docs/reference/ORCHESTRATION-PATTERNS.md` — Full orchestration specifications (bootstrap sequence, divine economy, dungeon patterns, living weapons)
- `docs/BANNOU-ASPIRATIONS.md` § Platform Vision — Genre case studies, developer time savings, competitive positioning

---

## ⚠️ Additional Reference Documentation

**The TENETS and BANNOU-DESIGN documents are automatically included above.** For specific tasks, also reference:

- **Plugin Development**: `docs/guides/PLUGIN-DEVELOPMENT.md` - How to add and extend services
- **WebSocket Protocol**: `docs/WEBSOCKET-PROTOCOL.md` - Protocol details
- **Deployment**: `docs/operations/DEPLOYMENT.md` - Deployment patterns
- **Claude Skills Operations**: `docs/operations/CLAUDE-SKILLS.md` - Hook and skill documentation

**Claude Code Configuration Locations**:
- **Skills (slash commands)**: `.claude/commands/*.md` — project-level skill files invoked via `/skill-name`
- **PreToolUse hooks**: `.claude/hooks/*.sh` — project-level hook scripts (blocking and reminder)
- **Hook/permission config**: `.claude/settings.json` (project, checked in) and `~/.claude/settings.json` (user-level, global)
- **Permission canary**: `.claude/permission-canary.txt` — fail-fast Edit permission gate used by all skills

**"Big Brain Mode"**: When the user says **"Big Brain Mode"**, you MUST read both vision documents in full before proceeding:
- `docs/reference/VISION.md` - The "what and why" of Bannou and Arcadia at the highest level: the five north stars, the content flywheel thesis, system interdependencies, and non-negotiable design principles.
- `docs/reference/PLAYER-VISION.md` - How players actually experience Arcadia: progressive agency (guardian spirit model), generational play, genre gradients, and the alpha-to-release deployment strategy.

These documents provide the high-level architectural north-star context for the entire project. Read them when planning cross-cutting features, evaluating whether work aligns with project goals, designing player-facing features, or needing context on how services serve the bigger picture.

### Sub-Agent Orientation (MANDATORY)

**When launching sub-agents (Task tool), do NOT have them read the entire CLAUDE.md.** Instead, instruct each agent to read only the documents relevant to its mission. This keeps agents focused and avoids context bloat.

**Orientation by mission type:**

| Agent Mission | Must Read Before Starting |
|---------------|--------------------------|
| **Investigation** (understanding services, tracing dependencies, exploring architecture) | The layer-specific service details files: `docs/GENERATED-INFRASTRUCTURE-SERVICE-DETAILS.md`, `docs/GENERATED-APP-FOUNDATION-SERVICE-DETAILS.md`, `docs/GENERATED-APP-FEATURES-SERVICE-DETAILS.md`, `docs/GENERATED-GAME-FOUNDATION-SERVICE-DETAILS.md`, `docs/GENERATED-GAME-FEATURES-SERVICE-DETAILS.md`. For specific plugin investigation, also read `docs/maps/{SERVICE}.md` (implementation map) for endpoint details, dependencies, events, and method pseudocode. |
| **Plugin work** (auditing, mapping, testing, implementing, or maintaining a specific plugin) | The plugin's deep dive `docs/plugins/{SERVICE}.md` AND its implementation map `docs/maps/{SERVICE}.md`. The deep dive provides context, quirks, and design rationale; the map provides method-level detail, state key patterns, dependency tables, and event inventories. **Always read both.** |
| **Code auditing** (reviewing implementations, checking tenet compliance, finding violations) | ALL tenet files in `docs/reference/tenets/`: `FOUNDATION.md`, `IMPLEMENTATION-BEHAVIOR.md`, `IMPLEMENTATION-DATA.md`, `QUALITY.md`, `TESTING-PATTERNS.md` |
| **Testing work** (writing, reviewing, or designing unit tests, structural tests, enum mapping tests, or any test code) | `docs/reference/tenets/TESTING-PATTERNS.md` — contains test placement rules, isolation boundaries, mocking patterns, capture patterns, structural validators, enum mapping tests, forbidden patterns, and tier scope. This is the sole testing reference for agents. |
| **Schema auditing** (reviewing OpenAPI schemas, checking schema rules, validating schema design) | `docs/reference/SCHEMA-RULES.md` |
| **High-level vision** (evaluating how services serve gameplay, cross-cutting feature planning, content flywheel analysis) | `docs/reference/VISION.md` and `docs/reference/PLAYER-VISION.md` (same as Big Brain Mode) |
| **Canonical pattern lookup** (searching for the correct way to implement a pattern — background workers, state store access, event publishing, deprecation, cleanup, enum mapping, etc.) | `docs/reference/HELPERS-AND-COMMON-PATTERNS.md` — read this FIRST before grepping the codebase for examples. It catalogs all shared helpers, canonical skeletons, and reference implementations with code samples and "when to use" guidance. |
| **Documentation search** (broad search across guides, planning docs, FAQs, or operations docs) | The relevant catalog file(s): `docs/GENERATED-GUIDES-CATALOG.md`, `docs/GENERATED-PLANNING-CATALOG.md`, `docs/GENERATED-FAQ-CATALOG.md`, `docs/GENERATED-OPERATIONS-CATALOG.md`. Read the catalog first to identify target documents from summaries, then read only the identified documents. Never glob a docs directory and read files blindly. |

**Rules:**
1. Every sub-agent prompt MUST include an explicit instruction to read the relevant documents listed above BEFORE doing any work
2. Agents may need multiple orientations (e.g., an agent auditing code for tenet compliance while investigating service dependencies would read both the tenet files AND the service details files)
3. The agent's prompt should specify which documents to read -- do not rely on the agent discovering them on its own
4. For investigation agents, also include `docs/reference/SERVICE-HIERARCHY.md` when the task involves dependency analysis or layer validation
5. For ANY agent working on a specific plugin (audit, map, test, implement, maintain), ALWAYS include both `docs/plugins/{SERVICE}.md` (deep dive) and `docs/maps/{SERVICE}.md` (implementation map) in the agent's reading list
6. For ANY agent that needs to implement a pattern (background workers, event handlers, deprecation, cleanup, state store access, enum mapping, telemetry, etc.), instruct the agent to read `docs/reference/HELPERS-AND-COMMON-PATTERNS.md` BEFORE searching the codebase for examples. The patterns file is the canonical source — grepping other services for "how they do it" risks copying violations or outdated patterns

**Other Planning References**:

- **Plan Example**: `docs/reference/templates/PLAN-EXAMPLE.md` - A preserved real implementation plan (Seed service) showing the expected structure, detail level, and patterns for planning a new Bannou service. Read this when creating implementation plans for new services or major features to match the established planning format.
- **Implementation Maps**: `docs/maps/{SERVICE}.md` - Method-by-method specifications for each plugin. Contains pseudocode, state store key patterns, dependency tables, event inventories, DI service lists, and full endpoint indexes. Every implemented service has one. **Do not confuse with deep dives** (`docs/plugins/{SERVICE}.md`) — deep dives are high-level context; maps are detailed specifications.

**Auto-Generated References** (regenerate with `make generate-docs`):

- **Service Details**: `docs/GENERATED-SERVICE-DETAILS.md` - Service descriptions and API endpoints
- **Configuration**: `docs/GENERATED-CONFIGURATION.md` - Environment variables per service
- **Events**: `docs/GENERATED-EVENTS.md` - Event schemas and topics
- **State Stores**: `docs/GENERATED-STATE-STORES.md` - Redis/MySQL state stores
- **Document Catalogs** (index-first search — see rule below):
  - `docs/GENERATED-GUIDES-CATALOG.md` — All developer guides with summaries, status, key plugins
  - `docs/GENERATED-PLANNING-CATALOG.md` — All planning/design/research docs with summaries, type, status, north stars
  - `docs/GENERATED-FAQ-CATALOG.md` — All architectural rationale FAQs with summaries and related plugins
  - `docs/GENERATED-OPERATIONS-CATALOG.md` — All operations docs with summaries and scope

### Catalog-First Documentation Search (MANDATORY)

**When searching documentation broadly** — e.g., "find documentation about X", "search docs for anything related to Y", "which guide covers Z", or when launching agents to search documentation — **read the relevant catalog FIRST before opening individual documents.** Each catalog is a single-file index (~50-200 lines) with rich summary paragraphs, metadata (status, key plugins, last updated), and direct links. Reading a catalog and identifying the 1-2 relevant documents is dramatically cheaper than globbing a directory and reading files one by one.

**Which catalog to check:**

| Looking for... | Read first |
|----------------|------------|
| How-to guides, SDK docs, system explanations, developer workflows | `docs/GENERATED-GUIDES-CATALOG.md` |
| Design documents, vision docs, research, implementation plans, architectural analysis | `docs/GENERATED-PLANNING-CATALOG.md` |
| "Why does Bannou do X?", architectural rationale, design decision justification | `docs/GENERATED-FAQ-CATALOG.md` |
| Deployment, testing, CI/CD, linting, release procedures | `docs/GENERATED-OPERATIONS-CATALOG.md` |
| Unknown or cross-cutting topic | Check all four catalogs (they're small — total ~500 lines) |

**This applies to both direct searches and sub-agent instructions.** When launching an agent to "search documentation for X", instruct it to read the relevant catalog(s) first, identify target documents from the summaries, and only then read the full documents it identified. Do not instruct agents to glob `docs/guides/` or `docs/planning/` and read files blindly.

## Development Rules

- **Research first**: Always research the correct library API before implementing

### Always Reference the Makefile
**MANDATORY**: The Makefile contains all established commands and patterns. Always check it before creating new commands or approaches.

**Available Makefile Commands**:
Reference the Makefile in the repository root for all available commands and established patterns.

### Shared Class Architecture (MANDATORY)
**For classes shared across multiple services, follow the established pattern:**

**Shared Class Location**: `bannou-service/` project (single source of truth)
- **ApiException Classes**: Located in `bannou-service/ApiException.cs`
- **Common Models**: Any model used by multiple services goes in `bannou-service/`
- **Shared Interfaces**: Cross-service interfaces belong in `bannou-service/`

**NSwag Exclusion Requirements**:
- **Exclude shared classes from generation**: Use `excludedTypeNames:ApiException,ApiException\<TResult\>` format
- **Syntax**: Comma-delimited unquoted format with escaped angle brackets for generics
- **Never use**: Semicolon-separated or quoted formats (causes shell parsing errors)

**Implementation Pattern**:
1. Create shared class in `bannou-service/` project with proper namespace
2. Add exclusion to both controller generation AND model generation in `scripts/generate-all-services.sh`
3. All services reference `bannou-service` project, so shared classes are available
4. Never duplicate shared classes across multiple service projects

**Example**: `bannou-service/ApiException.cs` provides `ApiException` and `ApiException<TResult>` for all services

## Development Workflow

### Essential Commands
```bash
# Building
make build                     # Build all services
dotnet build                   # Alternative: direct dotnet build

# Code Generation - USE THE MOST GRANULAR COMMAND POSSIBLE
# ⚠️ Some scripts must be run FROM the scripts/ directory (they use relative paths).
# Scripts marked with † below auto-cd and can be run from anywhere.

# If you only changed ONE schema type, use the specific script:
cd scripts && ./generate-config.sh <service>   # Configuration only (changed *-configuration.yaml)
scripts/generate-service-events.sh <service>   # † Events + lifecycle events (changed *-events.yaml or x-lifecycle)
cd scripts && ./generate-models.sh <service>   # Models only (changed *-api.yaml models)
scripts/generate-client-events.sh <service>    # † Client events only (changed *-client-events.yaml)

# If you changed multiple schema types for ONE service:
cd scripts && ./generate-service.sh <service>  # All generated code for one service

# ONLY use full regeneration when necessary (e.g., changed common schemas):
scripts/generate-all-services.sh               # † Regenerate ALL services (slow - avoid if possible)

# Unit Testing - SCOPED TESTS ONLY (same rule as scoped builds)
# NEVER run `make test` (full suite) when only specific plugins were changed.
# Test ONLY the affected test project:
dotnet test --project plugins/lib-{service}.tests/lib-{service}.tests.csproj --no-restore

# Structural Testing — cross-cutting convention/schema/assembly validation
make test-structural                          # All structural tests (non-informational)
make test-structural METHOD=Service_HasValidConstructor  # Specific test by method name
make test-structural-info                     # All tests including informational (SkipUnless-gated)
make test-structural-info METHOD=PackageReferences_AreLatestStableVersions  # Specific informational test
# Informational tests (gated by SkipUnless, require explicit opt-in):
#   PackageReferences_AreLatestStableVersions — NuGet version freshness (requires network)
#   PluginPackages_DoNotDuplicateBannouServicePackages — transitive duplicate detection
#   PluginPackages_AreReferencedInSource — unused plugin-specific package detection

# Model Shape Inspection (for understanding service models without loading full schemas)
# Prints compact model shapes (~6x smaller than schemas or generated C# code).
# Use this INSTEAD of reading *Models.cs or *-api.yaml when you need to understand
# all models for a service. Format: * = required, ? = nullable, = val = default.
make print-models PLUGIN="character"     # Print all model shapes for a service

# Assembly Inspection (for understanding external APIs)
make inspect-type TYPE="IChannel" PKG="RabbitMQ.Client"
make inspect-method METHOD="IChannel.BasicPublishAsync" PKG="RabbitMQ.Client"
make inspect-constructor TYPE="ConnectionFactory" PKG="RabbitMQ.Client"
make inspect-search PATTERN="*Connection*" PKG="RabbitMQ.Client"
make inspect-list PKG="RabbitMQ.Client"
```

**⚠️ Generation Script Selection Guide**:
- Changed `schemas/foo-configuration.yaml` → run `cd scripts && ./generate-config.sh foo`
- Changed `schemas/foo-events.yaml` (events, x-lifecycle, or both) → run `scripts/generate-service-events.sh foo` (handles both service events and lifecycle events)
- Changed `x-event-publications` in `schemas/foo-events.yaml` → also run `python3 scripts/generate-published-topics.py` (topic constants)
- Changed `schemas/foo-api.yaml` (models only) → run `cd scripts && ./generate-models.sh foo`
- Changed `schemas/foo-client-events.yaml` → run `scripts/generate-client-events.sh foo`
- Changed multiple schema files for `foo` → run `cd scripts && ./generate-service.sh foo`
- Changed `schemas/common-*.yaml` or multiple services → run `scripts/generate-all-services.sh`

**⚠️ Generation Order Dependency**: Event schemas `$ref` types from `*-api.yaml` and `common-api.yaml`. The events script **excludes** those types (they're already generated by their own scripts). This means: **if you change both an API schema and an events schema in the same edit, generate models FIRST, then events.** Specifically:
- Changed `foo-api.yaml` + `foo-events.yaml` → run `generate-models.sh foo` THEN `generate-service-events.sh foo`
- Changed `common-api.yaml` + any events → run `generate-all-services.sh` (handles ordering automatically)
Running events before models after adding a new `$ref` will produce duplicate types or unresolved references.

### Testing Strategy
**Claude's testing responsibilities**: WRITE all tests, but only RUN unit tests.

**Three-tier architecture** (for reference - Claude does NOT run tiers 2-3):
1. **Unit Tests**: Claude writes and runs these (`dotnet test --project plugins/lib-{service}.tests/...` — scoped to affected projects only)
2. **HTTP Integration Tests**: Claude writes these but does NOT run them (user runs `make test-http`)
3. **WebSocket Edge Tests**: Claude writes these but does NOT run them (user runs `make test-edge`)

**Why Claude only runs unit tests**: Integration tests require Docker containers, take 5-10+ minutes, and are disruptive. The user will run them when needed. A successful `dotnet build` is sufficient verification for most code changes.

### **MANDATORY**: Reference Detailed Procedures
**When testing fails or debugging complex issues**, you MUST reference the detailed development procedures documentation in the knowledge base for troubleshooting guides, Docker Compose configurations, and step-by-step debugging procedures.

### Environment Configuration

**Local Development with .env Files**:
- **Primary Configuration**: Use `.env` file in repository root for all environment variables
- **Service Prefix**: Use `BANNOU_` prefix for service-specific variables (e.g., `BANNOU_HTTP_Web_Host_Port=5012`)
- **Configuration Loading**: System automatically loads .env files from current or parent directories

**Environment Variable Patterns**:
```bash
# Port Configuration
HTTP_Web_Host_Port=5012
HTTPS_Web_Host_Port=5013
BANNOU_HTTP_Web_Host_Port=5012    # Service-specific with prefix
BANNOU_HTTPS_Web_Host_Port=5013

# Service Configuration
BANNOU_SERVICE_DOMAIN=example.com

# JWT Configuration (consolidated in main app)
BANNOU_JWT_SECRET=bannou-dev-secret-key-2025-please-change-in-production
BANNOU_JWT_ISSUER=bannou-auth-dev
BANNOU_JWT_AUDIENCE=bannou-api-dev
```

**Configuration Implementation Details**:
- **DotNetEnv Integration**: Automatic .env file loading via DotNetEnv package (3.1.1)
- **Service-Specific Binding**: `[ServiceConfiguration(envPrefix: "BANNOU_")]` attribute on configuration classes
- **Hierarchy**: .env files checked in current directory, then parent directory

### Infrastructure Libs & Service Discovery
**Follow `BANNOU-DESIGN.md` (loaded as `@reference` above) for all infrastructure patterns** — lib-state (StateStoreDefinitions, IStateStoreFactory), lib-messaging (IMessageBus), lib-mesh (generated clients, YARP routing), assembly loading, service discovery, and the omnipotent routing model. Those are the authoritative examples.

## Arcadia Game Integration

### Current Development Phase
**Focus**: NPC Behavior Systems with ABML YAML DSL for autonomous character behaviors

### Active Services
**✅ Production Ready**: Account, Auth, Connect (WebSocket gateway), Website, Behavior foundation
**🔧 In Development**: ABML YAML Parser, Character Agent Services, Cross-service event integration

## Troubleshooting Reference

### Common Issues
- **Generated file errors**: Fix underlying schema, never edit generated files directly
- **Line ending issues**: User runs `make format` (Claude does not run this)
- **Service registration**: Verify `[BannouService]` and `[ServiceConfiguration]` attributes
- **Build failures**: Check schema syntax in `/schemas/` directory
- **Enum duplicate errors**: Use consolidated enum definitions in schema `components/schemas` with `$ref` references
- **Parameter type mismatches**: Ensure interface generation properly handles `$ref` parameters (Provider not string)
- **Script not found errors**: All generation scripts moved to `scripts/` directory - use the appropriate granular script

### Debugging Tools
- **Swagger UI**: Available at `/swagger` in development
- **Service discovery**: Connect service provides runtime service mappings
- **Integration logs**: Docker Compose provides complete request tracing

## Git Workflow

### Commit Policy (MANDATORY)
- **Never commit** unless explicitly instructed by user
- **Always run `git diff` against last commit** before committing to review all changes
- Present changes for user review and get explicit approval first
- The user handles formatting and integration testing before commits

### Pre-Commit Checklist (User Responsibility)
The user will run these commands when preparing commits - Claude should NOT run them:
```bash
make format                    # User runs: Fix formatting and line endings
make all                       # User runs: Full test suite
```

**Claude's pre-commit responsibility**: Run `git diff HEAD~1` to review changes before committing.

### Standard Commit Format

**MANDATORY**: The first line of every commit message MUST be a senryu (5-7-5 syllable poem about human nature, similar to haiku but focused on human foibles/experiences rather than nature). Format all three "lines" of the senryu on a single line, separated by ` / ` (space-slash-space).

**Senryu themes** should relate to the human experience of coding: debugging frustration, refactoring satisfaction, the hubris of "quick fixes", late-night coding regrets, the joy of tests passing, etc.

**Examples**:
- `bugs hide in plain sight / we debug with tired eyes / coffee saves us all`
- `one small change they said / now the whole system is down / hubris strikes again`
- `tests were passing green / then I touched one single line / red across the board`

```bash
git commit -m "$(cat <<'EOF'
code once worked just fine / then a single change broke all / such is dev life's way

Add validation to user input handling

- Added null checks to prevent crashes on malformed input
- Updated error messages to be more descriptive

🤖 Generated with [Claude Code](https://claude.ai/code)

Co-Authored-By: Claude <noreply@anthropic.com>
EOF
)"
```

**Note**: A PreToolUse hook validates that commits follow this format. Commits without a properly-formatted senryu first line will be blocked.
