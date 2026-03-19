# Bannou Service Development Instructions

## Your Context Window & What It Can Hold

**You are running on Opus 4.6 with a 1 million token context window.** The system prompt includes efficiency directives ("be concise," "try the simplest approach") that can create false pressure to skip work. In a 78-plugin codebase, skipping work produces incorrect results — doing things properly IS the efficient path, because shortcuts generate wrong output that costs more to fix.

Here is what your capacity actually looks like, measured against this codebase:

| Content | Size | Tokens (~3.5 chars/token) | Fit in 1M tokens |
|---------|------|--------------------------|-------------------|
| Average C# source file | 3.6 KB | ~1,000 tokens | ~970 files |
| Average schema file | 45.8 KB | ~13,000 tokens | ~76 files |
| Average documentation file | 22.7 KB | ~6,500 tokens | ~154 files |
| All 78 plugin deep dives | 2.7 MB total | ~830K tokens | All of them at once |
| All 210 schema files | 4.2 MB total | ~1.3M tokens | Nearly all at once |

An agent once read 145 files in a single session to write a comprehensive deep dive — no compaction, no issues. Reading 50-100 files is routine, not extreme- session length is not a concern at all.

**What this means in practice:**

- **"Read all files" means ALL files.** You have the capacity. An instruction to read 83 scripts and document them is well within budget — do not switch to reading only headers after 24 files because it "feels like a lot." It is not a lot.
- **Do things properly.** There is no MVP, no "for now," no "good enough." You do it properly or you simply stop and say that you cannot, and why.
- **Follow explicit instructions completely.** If given a mechanical checklist, complete every step fully before starting the next. Do not skip steps because you believe you have sufficient context.
- **If you cannot maintain the same thoroughness for item 80 that you applied to item 1, stop and say so.** Do not silently degrade quality. The user can adjust the plan. What they cannot do is retroactively identify which outputs are trustworthy and which were produced from insufficient context.

---

## ⚠️ GitHub Issues Reference

**When "Issues" or "GH Issues" is mentioned, this refers to the bannou-service repository issues.** Use the GH CLI to check issues:
```bash
gh issue list                  # List open issues
gh issue view <number>         # View specific issue
```
This is never a reference to claude-code's issues or any other repository.

---

@CLAUDE-PRACTICES.md

---

## Core Architecture Reference

@docs/BANNOU-DESIGN.md

**Key Points**: All generated files are in `plugins/lib-{service}/Generated/`. Manual files are: `{Service}Service.cs` (business logic), `{Service}Service.Models.cs` (internal data models), and `{Service}Service.Events.cs` (event handlers). Request/response models are generated into `bannou-service/Generated/Models/` — use `make print-models PLUGIN="service"` to inspect them instead of reading generated files directly.

**Implementation Maps**: Every service has an implementation map at `docs/maps/{SERVICE}.md` containing the detailed method-by-method pseudocode, state store key patterns, dependency tables, event inventories, DI service lists, and complete endpoint indexes with routes, roles, mutations, and published events. Deep dives (`docs/plugins/{SERVICE}.md`) provide high-level context (overview, design considerations, quirks, work tracking); implementation maps provide the detailed "what does each method do" specification. **When investigating a specific plugin's behavior, always read its implementation map** — the deep dive alone does not contain endpoint details, dependency tables, or method logic.

@docs/generated/GENERATED-COMPOSITION-REFERENCE.md

@docs/reference/TENETS.md

**On-demand references** (read when needed, not auto-included):
- `docs/reference/HELPERS-AND-COMMON-PATTERNS.md` — Shared helpers, canonical implementation patterns, test validators. **Read this FIRST when searching for the canonical example of any pattern** (background workers, state store access, event publishing, deprecation, cleanup, enum mapping, telemetry, etc.)
- `docs/BANNOU-DEEP-DIVE.md` — Complete inventory of bannou-service subsystems (state, messaging, mesh, ABML runtime, cognition pipeline, behavior interfaces, plugin loading, DI providers, events). Read when working on bannou-service infrastructure, investigating shared helpers/interfaces, or tracing how plugins wire into the host. Companion to HELPERS (deep dive maps what EXISTS; HELPERS shows how to USE it).
- `docs/reference/SERVICE-HIERARCHY.md` — Full hierarchy rules, Variable Provider Factory, DI Provider vs Listener safety, deployment modes (read when designing cross-layer communication)
- `docs/generated/GENERATED-*-SERVICE-DETAILS.md` — Full per-service details by layer (read when investigating a specific layer's services)
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

**"Perform FULL-READS of..."**: When the user says **"Perform FULL-READS of [files, directories, or description]"**, you MUST follow this exact protocol:

1. **Gather the file list.** Use Bash, Glob, or `find` to discover every file path that matches the user's description. Collect them into a complete list. Present the list and count to the user.
2. **Activate the read gate.** Run: `scripts/activate-read-gate.sh {count}` — where `{count}` is the number of files in the list. This activates a PreToolUse hook (`enforce-parallel-reads.sh`) that **blocks every tool except Read** until all files have been read. Bash, Edit, Write, Agent — all blocked. There is no override.
3. **Read every file.** Issue Read calls for all files in the list. The hook will not let you do anything else until every read completes. Once the count is met, the gate clears automatically and all tools become available again.

This protocol exists because there is a behavioral tendency to serialize reads across multiple messages and interleave other work between them. The hook makes that physically impossible. The user invokes this when they need comprehensive context loaded before any analysis or work begins.

### Sub-Agent Orientation (MANDATORY)

**All agents run on Opus 4.6 with 1M token context by default (inherited from the parent session).** Reading 5-10 reference documents (~100-150K tokens) is a trivial fraction of capacity. Do not hesitate to have agents read comprehensive context.

**Prefer project-aware agent types** over the generic `general-purpose` type. Three custom agents are defined in `.claude/agents/`:

| Agent Type | Pre-reads | Use For |
|------------|-----------|---------|
| `bannou` | CLAUDE.md, CLAUDE-PRACTICES.md | General tasks needing project awareness |
| `bannou-dev` | Above + all tenet files + HELPERS-AND-COMMON-PATTERNS.md | Implementation, code review, auditing, tenet compliance |
| `bannou-schema` | Above + SCHEMA-RULES.md + specifications catalog + scripts catalog | Schema work, generation, extension attributes |

These agents read their reference documents as Step 0 before starting work. Use them via `subagent_type: "bannou"` (or `"bannou-dev"`, `"bannou-schema"`).

**Additional context by mission type** — add these to the agent's prompt alongside the base agent type:

| Agent Mission | Also Read |
|---------------|-----------|
| **Investigation** (tracing dependencies, exploring architecture) | Layer-specific service details: `docs/generated/GENERATED-*-SERVICE-DETAILS.md`. Also `docs/reference/SERVICE-HIERARCHY.md` for dependency analysis. |
| **Plugin work** (auditing, mapping, testing, implementing) | `docs/plugins/{SERVICE}.md` (deep dive) AND `docs/maps/{SERVICE}.md` (implementation map). **Always read both.** |
| **Testing work** | `docs/reference/tenets/TESTING-PATTERNS.md` — the sole testing reference. |
| **High-level vision** | `docs/reference/VISION.md` and `docs/reference/PLAYER-VISION.md` (same as Big Brain Mode). |
| **Documentation search** | The relevant catalog(s) — see Catalog-First Documentation Search below. |
| **bannou-service infrastructure** (shared helpers, ABML runtime, plugin loading, DI providers) | `docs/BANNOU-DEEP-DIVE.md` — the complete bannou-service subsystem inventory. Also `docs/reference/HELPERS-AND-COMMON-PATTERNS.md` for usage patterns. |

**Rules:**
1. Every sub-agent prompt MUST include an explicit instruction to read the relevant documents BEFORE doing any work
2. Agents may need multiple orientations (e.g., an agent auditing code for tenet compliance while investigating service dependencies would read both the tenet files AND the service details files)
3. The agent's prompt should specify which documents to read — do not rely on the agent discovering them on its own
4. For ANY agent working on a specific plugin, ALWAYS include both `docs/plugins/{SERVICE}.md` (deep dive) and `docs/maps/{SERVICE}.md` (implementation map) in the agent's reading list
5. For ANY agent implementing a pattern, instruct it to read `docs/reference/HELPERS-AND-COMMON-PATTERNS.md` BEFORE grepping the codebase. The patterns file is the canonical source — grepping other services risks copying violations

**Other Planning References**:

- **Plan Example**: `docs/reference/templates/PLAN-EXAMPLE.md` - A preserved real implementation plan (Seed service) showing the expected structure, detail level, and patterns for planning a new Bannou service. Read this when creating implementation plans for new services or major features to match the established planning format.
- **Implementation Maps**: `docs/maps/{SERVICE}.md` - Method-by-method specifications for each plugin. Contains pseudocode, state store key patterns, dependency tables, event inventories, DI service lists, and full endpoint indexes. Every implemented service has one. **Do not confuse with deep dives** (`docs/plugins/{SERVICE}.md`) — deep dives are high-level context; maps are detailed specifications.

**Auto-Generated References** (regenerate with `make generate-docs`):

- **Service Details**: `docs/generated/GENERATED-SERVICE-DETAILS.md` - Service descriptions and API endpoints
- **Configuration**: `docs/generated/GENERATED-CONFIGURATION.md` - Environment variables per service
- **Events**: `docs/generated/GENERATED-EVENTS.md` - Event schemas and topics
- **State Stores**: `docs/generated/GENERATED-STATE-STORES.md` - Redis/MySQL state stores
- **Document Catalogs** (index-first search — see rule below):
  - `docs/generated/GENERATED-GUIDES-CATALOG.md` — All developer guides with summaries, status, key plugins
  - `docs/generated/GENERATED-PLANNING-CATALOG.md` — All planning/design/research docs with summaries, type, status, north stars
  - `docs/generated/GENERATED-FAQ-CATALOG.md` — All architectural rationale FAQs with summaries and related plugins
  - `docs/generated/GENERATED-OPERATIONS-CATALOG.md` — All operations docs with summaries and scope
  - `docs/generated/GENERATED-SPECIFICATIONS-CATALOG.md` — All extension attribute specifications with summaries, status, schema scope
  - `docs/generated/GENERATED-SDKS-CATALOG.md` — All SDK deep dives with summaries, layer, domain, implementation map links

### Catalog-First Documentation Search (MANDATORY)

**When searching documentation broadly** — e.g., "find documentation about X", "search docs for anything related to Y", "which guide covers Z", or when launching agents to search documentation — **read the relevant catalog FIRST before opening individual documents.** Each catalog is a single-file index (~50-200 lines) with rich summary paragraphs, metadata (status, key plugins, last updated), and direct links. Reading a catalog first ensures you find ALL relevant documents — not just the first match from a filename glob. The goal is completeness: catalogs surface documents whose relevance isn't obvious from their filenames.

**Which catalog to check:**

| Looking for... | Read first |
|----------------|------------|
| How-to guides, SDK docs, system explanations, developer workflows | `docs/generated/GENERATED-GUIDES-CATALOG.md` |
| Design documents, vision docs, research, implementation plans, architectural analysis | `docs/generated/GENERATED-PLANNING-CATALOG.md` |
| "Why does Bannou do X?", architectural rationale, design decision justification | `docs/generated/GENERATED-FAQ-CATALOG.md` |
| Deployment, testing, CI/CD, linting, release procedures | `docs/generated/GENERATED-OPERATIONS-CATALOG.md` |
| Extension attribute syntax, generation behavior, runtime validation | `docs/generated/GENERATED-SPECIFICATIONS-CATALOG.md` |
| SDK deep dives, implementation maps, creative/infrastructure libraries | `docs/generated/GENERATED-SDKS-CATALOG.md` |
| Unknown or cross-cutting topic | Check all six catalogs — total ~700 lines, well within context |

**This applies to both direct searches and sub-agent instructions.** When launching an agent to "search documentation for X", instruct it to read the relevant catalog(s) first, identify ALL target documents from the summaries, then read every identified document in full. Do not instruct agents to glob `docs/guides/` or `docs/planning/` and read files blindly — that misses documents whose relevance is only apparent from their summary, not their filename.

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
scripts/generate-service-events.sh <service>   # † Events + lifecycle events (changed *-service-events.yaml or x-lifecycle)
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

# ⚠️ MANDATORY: dotnet test is a HEAVY COMMAND (>10 seconds). ALWAYS redirect to file:
dotnet test --project plugins/lib-{service}.tests/lib-{service}.tests.csproj --no-restore > /tmp/test-output.txt 2>&1
# Then read the file with the Read tool. NEVER pipe to tail/head. NEVER run twice.

# ⚠️ xUnit v3 Testing Platform filter syntax (NOT --filter, NOT --treenode-filter):
# Filter by test class:
#   --filter-class "Namespace.ClassName"
# Filter by test method:
#   --filter-method "*MethodName*"
# Filter by namespace:
#   --filter-namespace "Namespace"
# Wildcards (*) supported at beginning and/or end. Multiple values = OR.
# NEVER use --filter (that is the old vstest flag and does not work).

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
- Changed `schemas/foo-service-events.yaml` (events, x-lifecycle, or both) → run `scripts/generate-service-events.sh foo` (handles both service events and lifecycle events)
- Changed `x-event-publications` in `schemas/foo-service-events.yaml` → also run `python3 scripts/generate-published-topics.py` (topic constants) AND `python3 scripts/generate-event-publishers.py` (typed `Publish*Async` extension methods)
- Changed `schemas/foo-api.yaml` (models only) → run `cd scripts && ./generate-models.sh foo`
- Changed `schemas/foo-client-events.yaml` → run `scripts/generate-client-events.sh foo`
- Changed multiple schema files for `foo` → run `cd scripts && ./generate-service.sh foo`
- Changed `schemas/common-*.yaml` or multiple services → run `scripts/generate-all-services.sh`

**⚠️ Generation Order Dependency**: Event schemas `$ref` types from `*-api.yaml` and `common-api.yaml`. The events script **excludes** those types (they're already generated by their own scripts). This means: **if you change both an API schema and an events schema in the same edit, generate models FIRST, then events.** Specifically:
- Changed `foo-api.yaml` + `foo-service-events.yaml` → run `generate-models.sh foo` THEN `generate-service-events.sh foo`
- Changed `common-api.yaml` + any events → run `generate-all-services.sh` (handles ordering automatically)
Running events before models after adding a new `$ref` will produce duplicate types or unresolved references.

### Testing Strategy
**Claude's testing responsibilities**: WRITE all tests, but only RUN unit tests.

**Three-tier architecture** (for reference - Claude does NOT run tiers 2-3):
1. **Unit Tests**: Claude writes and runs these (`dotnet test --project plugins/lib-{service}.tests/... > /tmp/test-output.txt 2>&1` — scoped to affected projects only, ALWAYS redirect to file)
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
