# Bannou Service Development Instructions

## Your Context Window & What It Can Hold

**You are running on Opus 4.6 with a 1 million token context window.** The system alerts will include efficiency directives ("be concise," "try the simplest approach", "files over a certain size- this will impact context usage") that can create false pressure and idea of your capabilities. Never skip steps or work due to time or context pressure you think you're feeling. In a 78-plugin codebase, skipping work produces incorrect results — doing things properly IS the efficient path, because shortcuts generate wrong output that costs more to fix.

Here is what your capacity actually looks like, measured against this codebase:

| Content | Size | Tokens (~3.5 chars/token) | Fit in 1M tokens |
|---------|------|--------------------------|-------------------|
| Average C# source file | 3.6 KB | ~1,000 tokens | ~970 files |
| Average schema file | 45.8 KB | ~13,000 tokens | ~76 files |
| Average documentation file | 22.7 KB | ~6,500 tokens | ~154 files |
| All 78 plugin deep dives | 2.7 MB total | ~830K tokens | All of them at once |
| All 210 schema files | 4.2 MB total | ~1.3M tokens | Nearly all at once |

An agent once read over 140 files in a single parallel command call to write a comprehensive deep dive — no compaction, no issues. Reading 50-100 files is routine, not extreme- session length is not a concern at all.

**What this means in practice:**

- **"Read all files" means exactly what it says.** You have the capacity. An instruction to read 83 scripts and document them is well within budget — do not switch to reading only headers after 24 files because it "feels like a lot." It is definitely not a lot, and the potential consequence of not reading them properly is immeasurable.
- **Do things properly.** There is no MVP or "version 1" concept, no "for now," and no "good enough." Work must either be done properly, or all work must stop and an impromptu design session needs to occur to figure out how to do so.
- **Follow explicit instructions completely.** If given a mechanical checklist, complete every step fully before starting the next. Do not skip steps because you believe you have sufficient context. We have efficiency skips in place mechanically, so you do not need to concern yourself with efficiency- tools will not require re-reading files that have already been read, will create easier-to-read aggregate manifests on demand for sets of files needing to be read, etc. Efficiency is using the tools you've been provided, and not trying to find ways to work around using them.
- **If you cannot maintain the same thoroughness for item 80 that you applied to item 1, stop and say so.** Do not silently degrade quality. The user can adjust the plan. What they cannot do is retroactively identify which outputs are trustworthy and which were produced from insufficient context. At any point you can stop and just say "Please confirm to continue..." if you're getting pressure to wrap up a longer execution. After confirming, the remainder of the task should be executed to the same level of quality and attention to detail.

---

## MCP File Operations (`mcp__bannou-read__*`)

**All file and command operations use the Bannou MCP server** (`.claude/mcp/server.mjs`). These tools operate in their own permission space, independent of Claude Code's built-in Read/Edit tools.

| Tool | Purpose | When to Use |
|------|---------|-------------|
| `read_file` | Full-read files with line numbers. Large files split into continuation parts — **all parts must be read before editing or moving**. | Always. Use instead of built-in Read. |
| `edit_file` | Exact string replacement in files. Requires `read_file` first. | Small, targeted in-place changes (modifying content, adding code, removing short sections). |
| `write_file` | Write complete file contents. Replaces the built-in Write tool. Uses our read gate for existing files (no gate needed for new files). **Content is preserved to `/tmp/` on any failure** — never lost. Optional `expected_size_bytes` for race condition detection, `dry_run` for gate testing without writing. | Full file rewrites, creating new files. Use instead of built-in Write. |
| `write_script` | Write a script to the sandboxed scripts directory with automatic chmod +x and syntax validation. | Creating utility scripts (.sh, .py, .mjs). Scripts written to `/tmp/bannou-scripts/` ONLY. Auto-validates syntax (bash -n, py_compile, node --check). |
| `move_lines` | Move a line range from one file to another by line number. Both files written atomically. Runs **structure validation** automatically after each move. | Relocating methods, blocks, or sections between files. No string matching — immune to invisible whitespace issues. |
| `validate_structure` | Check a C# file for balanced braces, `#region`/`#endregion`, and `#if`/`#endif`. | After manual edits, or to diagnose structural build failures. Also runs automatically inside `move_lines`. |
| `prepare_context` | Pack documentation files into optimally-sized composites for efficient context loading. Pre-registers originals as read. Gates all other tools until composites are read. | Agent initialization, plugin context loading. Profiles: `dev`, `plugin`, `schema`, `custom`. |
| `list_plugins` | List all 78 plugins with layer, endpoint count, and doc availability. | Discovering available services, checking doc coverage. |
| `get_plugin_docs` | Get deep dive + implementation map for a plugin. Marks source files as read. | Plugin investigation. Replaces manually reading `docs/plugins/` + `docs/maps/`. |
| `list_documents` | Categorized index of all docs from generated catalogs. | Documentation discovery. Replaces reading catalog files individually. |
| `get_document` | Get a specific document by path. Marks source as read. | Reading any doc file. |
| `search_docs` | Full-text keyword search across all docs/ with relevance ranking. | Finding relevant documentation by topic. |
| `list_schemas` | List all schema files organized by service. | Schema discovery. |
| `get_schema` | Get a specific schema file or all schemas for a service. Marks source as read. | Schema inspection. Replaces reading `schemas/*.yaml` individually. |
| `get_service_details` | Generated service details, optionally by layer. | Service investigation. Replaces reading `GENERATED-*-SERVICE-DETAILS.md`. |
| `get_events` | Generated events reference (all topics and schemas). | Event investigation. Replaces reading `GENERATED-EVENTS.md`. |
| `get_state_stores` | Generated state store definitions. | State store investigation. Replaces reading `GENERATED-STATE-STORES.md`. |
| `get_configuration` | Generated configuration reference (all env vars). | Config investigation. Replaces reading `GENERATED-CONFIGURATION.md`. |
| `print_models` | Compact model shapes for a service (~6x smaller than schemas). | Understanding service models. Replaces `make print-models`. |
| `print_interfaces` | Interface shapes from bannou-service (catalog or detail mode). | Understanding infrastructure APIs. Replaces `make print-interfaces`. |
| `generate` | Run code generation scripts. No args = catalog of all generators with triggers and ordering rules. With args = execute a specific generator. | After any schema change. **Always run `generate()` first to see available generators.** |
| `run_command` | Execute whitelisted shell commands (see below). | Builds, tests, git queries, file discovery, running sandboxed scripts. |

**Introspection tool notes**: Tools that return file-sourced content (`get_plugin_docs`, `get_document`, `get_schema`, `get_service_details`, `get_events`, `get_state_stores`, `get_configuration`) automatically mark the source files as read — enabling immediate `edit_file` without a separate `read_file` call. If output exceeds the MCP response size cap, it is split into continuation composites with the same required-reading gate as `prepare_context`.

**`move_lines` details**: Takes `source_file`, `start_line`, `end_line`, `dest_file`, `dest_insert_after_line`. Optional safety anchors `expect_first_line_contains` and `expect_last_line_contains` verify content before moving — catches off-by-one errors. Work bottom-up when moving multiple blocks from the same file (preserves line numbers for earlier blocks). After writing both files, automatically validates structure and reports any issues (orphaned regions, brace imbalances).

**When to use `move_lines` vs `edit_file`**: Use `move_lines` when relocating existing code between files (method moves, block extraction). Use `edit_file` for in-place modifications (changing content, adding new code, removing small sections).

**`move_lines` gotchas**:
- **Insert line precision**: If for instance inserting into a file with a placeholder comment like `// Move private/internal helper methods here`, the `dest_insert_after_line` must be the **placeholder comment line**, NOT the `}` on the next line. Inserting after `}` puts code outside the class body and causes build failures.
- **`#region` orphaning**: When a moved block includes methods from the middle of a `#region`, the `#region` or `#endregion` markers may be left orphaned. The structure validation catches these — fix by removing empty region pairs or adding missing markers.
- **Bottom-up ordering**: When moving multiple blocks from the same source file, always move the **bottom-most block first**. This preserves line numbers for blocks above it. Insert all blocks at the same `dest_insert_after_line` — this naturally maintains original source order.

**`validate_structure` notes**: The brace validator handles single-line strings, comments, multi-line verbatim strings (`@"..."`), multi-line comments (`/* ... */`), and escaped interpolation braces (`{{ }}`). The C# compiler (`dotnet build`) is always the authoritative structural check — if the build passes clean, any validator warnings are false positives.

**`write_file` details**: For existing files, requires `read_file` first (same gate as `edit_file`). For new files, no prior read needed. On any gate failure, content is automatically saved to `/tmp/bannou-write-recovery-{hash}.txt` and the path is returned — the content is never lost. Use `dry_run: true` to test gates without writing. Use `expected_size_bytes` (from the `read_file` header) to catch race conditions where the file changed between read and write. After a successful write, the file is auto-registered as read — subsequent `edit_file` calls work without re-reading.

**`write_script` details**: Scripts are written to `/tmp/bannou-scripts/` ONLY — agents cannot create executable scripts elsewhere. Allowed extensions: `.sh`, `.py`, `.mjs`. Shebangs are auto-added if missing. Syntax is validated before returning (`bash -n` for shell, `python3 -m py_compile` for Python, `node --check` for mjs). Execute via `run_command`: `/tmp/bannou-scripts/my-script.sh`. Filenames must be flat (no path separators), no hidden files, alphanumeric + hyphens/underscores/dots only.

**`prepare_context` details**: Reads documentation files server-side, packs them into optimally-sized composites (≤26KB each, fits in a single `read_file` response without triggering "Large MCP response" warnings), pre-registers originals as read (enabling immediate `edit_file`), and gates all other tools until composites are read. Profiles are defined in `.claude/mcp/profiles.mjs`:

| Profile | Files Loaded | Options |
|---------|-------------|---------|
| `dev` | CLAUDE.md, CLAUDE-PRACTICES.md, HELPERS-AND-COMMON-PATTERNS.md, all 5 tenet files | — |
| `plugin` | Everything in `dev` + `docs/plugins/{SERVICE}.md` + `docs/maps/{SERVICE}.md` | `service: "name"` (required) |
| `schema` | Everything in `dev` + SCHEMA-RULES.md + specifications catalog | — |
| `custom` | Arbitrary file list | `files: ["path/to/file", ...]` (required) |

Idempotent: files already read are skipped. Stackable: calling `prepare_context` while a gate is active adds new composites to the existing gate. After calling, read ALL returned composites to clear the gate and unlock other tools.

The `prepareContext` command accepts the same options as the `prepare_context` tool: `{ "profile": "plugin", "service": "account" }` for plugin context, `{ "profile": "custom", "files": ["path/to/file"] }` for arbitrary files. When triggered via sentinel, the composites are created and the required reading gate is activated — the agent must read all composites before any other tool call proceeds. A `UserPromptSubmit` hook notifies the agent that an injection is pending.

## Command Execution via `run_command`

**All shell commands in this document are executed via the MCP tool `mcp__bannou-read__run_command`.** When this document shows `bash` code blocks, `gh` commands, `dotnet build`, `make` targets, `find`, `ls`, `git diff`, generation scripts, or any other command-line invocation, use `run_command` to execute them. There is no separate Bash tool.

`run_command` accepts a whitelisted set of commands: `gh` (GitHub CLI), `dotnet build/test`, `make` targets, generation scripts (`scripts/`, `python3 scripts/`), file discovery (`ls`, `find`, `wc`, `comm`), temp file operations (`cat /tmp/`, `rm -f /tmp/`, `echo`), sandboxed script execution (`/tmp/bannou-scripts/`), and read-only git (`git status`, `git diff`, `git log`). Output redirection to `/tmp/` is supported (e.g., `dotnet test ... > /tmp/output.txt 2>&1`). Arbitrary shell scripting, file writes outside `/tmp/`, `chmod`, `chown`, and destructive git operations are blocked.

**GitHub issue comments**: Use `--body-file` instead of inline `--body` for `gh issue comment`. PreToolUse hooks scan the full command string including body content, which triggers false positives. Write the comment to `/tmp/gh-comment-{number}.md` with `write_file`, then run `gh issue comment {number} --body-file /tmp/gh-comment-{number}.md`.

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

**Key Points**: All generated files are in `plugins/lib-{service}/Generated/`. Manual files are: `{Service}Service.cs` (business logic), `{Service}Service.Models.cs` (internal data models), and `{Service}Service.Events.cs` (event handlers). Request/response models are generated into `bannou-service/Generated/Models/` — use the `print_models` MCP tool to inspect them instead of reading generated files directly.

**Implementation Maps**: Every service has an implementation map at `docs/maps/{SERVICE}.md` containing the detailed method-by-method pseudocode, state store key patterns, dependency tables, event inventories, DI service lists, and complete endpoint indexes with routes, roles, mutations, and published events. Deep dives (`docs/plugins/{SERVICE}.md`) provide high-level context (overview, design considerations, quirks, work tracking); implementation maps provide the detailed "what does each method do" specification. **When investigating a specific plugin's behavior, always read its implementation map** — the deep dive alone does not contain endpoint details, dependency tables, or method logic.

@docs/generated/GENERATED-COMPOSITION-REFERENCE.md

@docs/reference/TENETS.md

**On-demand references** (read when needed, not auto-included — many available via MCP tools):
- `docs/reference/HELPERS-AND-COMMON-PATTERNS.md` — Shared helpers, canonical implementation patterns, test validators. **Read this FIRST when searching for the canonical example of any pattern.** Included in `dev`/`plugin`/`schema` profiles.
- `docs/BANNOU-DEEP-DIVE.md` — Complete inventory of bannou-service subsystems. Read when working on bannou-service infrastructure. Companion to HELPERS.
- `docs/reference/SERVICE-HIERARCHY.md` — Full hierarchy rules, Variable Provider Factory, DI Provider vs Listener safety, deployment modes.
- Service details by layer — use `get_service_details` tool (optionally filtered by layer) instead of reading `GENERATED-*-SERVICE-DETAILS.md` files directly.
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
- **Skills (slash commands)**: `.claude/skills/{name}/SKILL.md` — project-level skill files invoked via `/skill-name`
- **PreToolUse hooks**: `.claude/hooks/*.sh` — project-level hook scripts (blocking and reminder)
- **Hook/permission config**: `.claude/settings.json` (project, checked in) and `~/.claude/settings.json` (user-level, global)
- **Permission canary**: `.claude/permission-canary.txt` — fail-fast Edit permission gate used by all skills

**"Big Brain Mode"**: When the user says **"Big Brain Mode"**, you MUST read both vision documents in full before proceeding:
- `docs/reference/VISION.md` - The "what and why" of Bannou and Arcadia at the highest level: the five north stars, the content flywheel thesis, system interdependencies, and non-negotiable design principles.
- `docs/reference/PLAYER-VISION.md` - How players actually experience Arcadia: progressive agency (guardian spirit model), generational play, genre gradients, and the alpha-to-release deployment strategy.

These documents provide the high-level architectural north-star context for the entire project. Read them when planning cross-cutting features, evaluating whether work aligns with project goals, designing player-facing features, or needing context on how services serve the bigger picture.

**"Perform FULL-READS of..."**: When the user says **"Perform FULL-READS of [files, directories, or description]"**, you MUST follow this exact protocol:

1. **Gather the file list.** Use Bash, Glob, or `find` to discover every file path that matches the user's description. Collect them into a complete list. Present the list and count to the user.
2. **Activate the read gate.** Run: `scripts/activate-read-gate.sh {count}` — where `{count}` is the number of files in the list. This activates a PreToolUse hook (`enforce-parallel-reads.sh`) that **blocks every tool except the MCP read tool (`mcp__bannou-read__read_file`)** until all files have been read. Bash, Edit, Write, Agent — all blocked. There is no override.
3. **Read every file.** Issue `mcp__bannou-read__read_file` calls for all files in the list. The hook will not let you do anything else until every read completes. Once the count is met, the gate clears automatically and all tools become available again.

This protocol exists because there is a behavioral tendency to serialize reads across multiple messages and interleave other work between them. The hook makes that physically impossible. The user invokes this when they need comprehensive context loaded before any analysis or work begins.

### Sub-Agent Orientation (MANDATORY)

**All agents run on Opus 4.6 with 1M token context by default (inherited from the parent session).** Reading 5-10 reference documents (~100-150K tokens) is a trivial fraction of capacity. Do not hesitate to have agents read comprehensive context.

**Prefer project-aware agent types** over the generic `general-purpose` type. Custom agents are defined in `.claude/agents/`:

| Agent Type | Context Profile | Use For |
|------------|----------------|---------|
| `bannou` | Manual reads (CLAUDE.md, CLAUDE-PRACTICES.md) | General tasks needing project awareness |
| `bannou-dev` | `prepare_context(profile: "dev")` — tenets + patterns | Implementation, code review, auditing, tenet compliance |
| `bannou-schema` | `prepare_context(profile: "schema")` — tenets + schema rules | Schema work, generation, extension attributes |

These agents call `prepare_context` as Step 0, then read the returned composites to load all reference documents efficiently. Use them via `subagent_type: "bannou"` (or `"bannou-dev"`, `"bannou-schema"`).

**Additional context by mission type** — add these to the agent's prompt alongside the base agent type:

| Agent Mission | Context Loading |
|---------------|----------------|
| **Plugin work** (auditing, mapping, testing, implementing) | Use `prepare_context(profile: "plugin", service: "{name}")` — loads dev context + deep dive + implementation map. **Always use this instead of reading plugin docs individually.** |
| **Investigation** (tracing dependencies, exploring architecture) | Layer-specific service details: `docs/generated/GENERATED-*-SERVICE-DETAILS.md`. Also `docs/reference/SERVICE-HIERARCHY.md` for dependency analysis. |
| **Testing work** | `docs/reference/tenets/TESTING-PATTERNS.md` — already included in `dev` and `plugin` profiles. |
| **High-level vision** | `docs/reference/VISION.md` and `docs/reference/PLAYER-VISION.md` (same as Big Brain Mode). |
| **Documentation search** | The relevant catalog(s) — see Catalog-First Documentation Search below. |
| **bannou-service infrastructure** (shared helpers, ABML runtime, plugin loading, DI providers) | `docs/BANNOU-DEEP-DIVE.md` — the complete bannou-service subsystem inventory. HELPERS already included via `dev` profile. |

**Rules:**
1. Every sub-agent prompt MUST include an explicit instruction to call `prepare_context` with the appropriate profile BEFORE doing any work — or to read specific documents if no profile covers them
2. For ANY agent working on a specific plugin, instruct it to use `prepare_context(profile: "plugin", service: "{name}")` — this is more efficient than reading files individually (fewer reads, optimally-packed composites)
3. `prepare_context` is stackable — an agent can call it multiple times with different profiles; files already read are skipped
4. For ANY agent implementing a pattern, the `dev` profile already includes `HELPERS-AND-COMMON-PATTERNS.md` — no separate read needed
5. Agents may need additional reads beyond what profiles provide (e.g., service details files for investigation work) — instruct them to read those after the profile context is loaded

**Other Planning References**:

- **Plan Example**: `docs/reference/templates/PLAN-EXAMPLE.md` - A preserved real implementation plan (Seed service) showing the expected structure, detail level, and patterns for planning a new Bannou service. Read this when creating implementation plans for new services or major features to match the established planning format.
- **Implementation Maps**: `docs/maps/{SERVICE}.md` - Method-by-method specifications for each plugin. Contains pseudocode, state store key patterns, dependency tables, event inventories, DI service lists, and full endpoint indexes. Every implemented service has one. **Do not confuse with deep dives** (`docs/plugins/{SERVICE}.md`) — deep dives are high-level context; maps are detailed specifications.

**Auto-Generated References** (regenerate with `generate(script: "docs")`; **all accessible via MCP tools**):

| Reference | MCP Tool | File Path |
|-----------|----------|-----------|
| Service Details | `get_service_details` (with optional layer filter) | `docs/generated/GENERATED-*-SERVICE-DETAILS.md` |
| Configuration | `get_configuration` | `docs/generated/GENERATED-CONFIGURATION.md` |
| Events | `get_events` | `docs/generated/GENERATED-EVENTS.md` |
| State Stores | `get_state_stores` | `docs/generated/GENERATED-STATE-STORES.md` |
| Document Catalogs | `list_documents` (with optional category filter) | `docs/generated/GENERATED-*-CATALOG.md` |
| Model Shapes | `print_models` | (computed from schemas) |
| Interface Shapes | `print_interfaces` | (computed from bannou-service C# sources) |

**Prefer MCP tools over reading generated files directly** — tools mark source files as read (enabling immediate edits), handle oversized output with continuation gating, and provide structured formatting.

### Catalog-First Documentation Search (MANDATORY)

**When searching documentation broadly**, use MCP tools instead of reading catalog files individually:

- **`search_docs`**: Full-text keyword search across all `docs/` files. Returns ranked results with scores. Use this first when you have a specific topic or keyword.
- **`list_documents`**: All 6 catalog files at once, or filtered by category. Use this when browsing or need the full catalog index with summaries.
- **`get_document`**: Read a specific document by path once you've identified it.

**Which category to check** (pass as `category` parameter to `list_documents`):

| Looking for... | Category |
|----------------|----------|
| How-to guides, SDK docs, system explanations, developer workflows | `guides` |
| Design documents, vision docs, research, implementation plans | `planning` |
| "Why does Bannou do X?", architectural rationale | `faqs` |
| Deployment, testing, CI/CD, linting, release procedures | `operations` |
| Extension attribute syntax, generation behavior, runtime validation | `specifications` |
| SDK deep dives, implementation maps, creative/infrastructure libraries | `sdks` |
| Unknown or cross-cutting topic | Omit category — returns all six catalogs |

**This applies to both direct searches and sub-agent instructions.** When launching an agent to "search documentation for X", instruct it to use `search_docs` or `list_documents` first, identify ALL target documents from the results, then read them with `get_document`. Do not instruct agents to glob `docs/` directories blindly — that misses documents whose relevance is only apparent from their summary, not their filename.

## Development Rules

- **Research first**: Always research the correct library API before implementing

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
2. Add exclusion to both controller generation AND model generation in the generation pipeline (see `scripts/generate-all-services.sh`)
3. All services reference `bannou-service` project, so shared classes are available
4. Never duplicate shared classes across multiple service projects

**Example**: `bannou-service/ApiException.cs` provides `ApiException` and `ApiException<TResult>` for all services

## Development Workflow

### Essential Commands
```bash
# Building
make build                     # Build all services
dotnet build                   # Alternative: direct dotnet build

# Code Generation — use the MCP generate tool:
#   generate()                                    # List all generators with triggers, ordering rules, and timing
#   generate(script: "models", service: "foo")    # Run a specific per-service generator
#   generate(script: "state-stores")              # Run a global generator
#   generate(script: "all")                       # Full regeneration (~10 min — use only when common-*.yaml changed)
# The generate tool handles all script paths, working directories, and timeouts internally.
# Run generate() with no args whenever you need to regenerate — it tells you exactly which command to use.

# Unit Testing - SCOPED TESTS ONLY (same rule as scoped builds)
# NEVER run `make test` (full suite) when only specific plugins were changed.
# Test ONLY the affected test project:
dotnet test --project plugins/lib-{service}.tests/lib-{service}.tests.csproj --no-restore

# ⚠️ MANDATORY: dotnet test is a HEAVY COMMAND (>10 seconds). ALWAYS redirect to file:
dotnet test --project plugins/lib-{service}.tests/lib-{service}.tests.csproj --no-restore > /tmp/test-output.txt 2>&1
# Then read the file with the MCP read tool (mcp__bannou-read__read_file). NEVER pipe to tail/head. NEVER run twice.

# ⚠️ xUnit v3 Testing Platform filter syntax (NOT --filter, NOT --treenode-filter):
# Filter by test class:
#   --filter-class "Namespace.ClassName"
# Filter by test method:
#   --filter-method "*MethodName*"
# Filter by namespace:
#   --filter-namespace "Namespace"
# Wildcards (*) supported at beginning and/or end. Multiple values = OR.
# NEVER use --filter (that is the old vstest flag and does not work).

# Model/Interface Inspection — use MCP tools:
#   print_models(plugin: "character")          # Compact model shapes (~6x smaller than schemas)
#   print_interfaces()                         # Catalog: all interfaces by category
#   print_interfaces(name: "IStateStore")      # Detail: full method signatures

# Assembly Inspection (for understanding external APIs — no MCP equivalent, use run_command)
make inspect-type TYPE="IChannel" PKG="RabbitMQ.Client"
make inspect-method METHOD="IChannel.BasicPublishAsync" PKG="RabbitMQ.Client"
make inspect-constructor TYPE="ConnectionFactory" PKG="RabbitMQ.Client"
make inspect-search PATTERN="*Connection*" PKG="RabbitMQ.Client"
make inspect-list PKG="RabbitMQ.Client"
```

### Testing Strategy
**Claude's testing responsibilities**: WRITE all tests, but only RUN unit tests.

**Three-tier architecture** (for reference - Claude does NOT run tiers 2-3):
1. **Unit Tests**: Claude writes and runs these (`dotnet test --project plugins/lib-{service}.tests/... > /tmp/test-output.txt 2>&1` — scoped to affected projects only, ALWAYS redirect to file)
2. **HTTP Integration Tests**: Claude writes these but does NOT run them (user runs)
3. **WebSocket Edge Tests**: Claude writes these but does NOT run them (user runs)

**Why Claude only runs unit tests**: Integration tests require Docker containers, take 5-10+ minutes, and are disruptive. The user will run them when needed. A successful `dotnet build` is sufficient verification for most code changes.

### **MANDATORY**: Reference Detailed Procedures
**When testing fails or debugging complex issues**, you MUST reference the detailed development procedures documentation in the knowledge base for troubleshooting guides, Docker Compose configurations, and step-by-step debugging procedures.

### Environment Configuration

**Local Development with .env Files**:
- **Primary Configuration**: Use `.env` file in repository root for all environment variables
- **Service Prefix**: Use `BANNOU_` and `{PLUGIN}_` prefix for service-specific variables (e.g., `BANNOU_HTTP_Web_Host_Port=5012`)
- **Configuration Loading**: System automatically loads .env files from current or parent directories

**Configuration Implementation Details**:
- **DotNetEnv Integration**: Automatic .env file loading via DotNetEnv package (3.1.1)
- **Service-Specific Binding**: `[ServiceConfiguration(envPrefix: "BANNOU_")]` attribute on configuration classes
- **Hierarchy**: .env files checked in current directory, then parent directory

## Troubleshooting Reference

### Common Issues
- **Generated file errors**: Fix underlying schema, regenerate with the `generate` tool, never edit generated files directly
- **Line ending issues**: User runs `make format` (agent does not)
- **Service registration**: Verify `[BannouService]` and `[ServiceConfiguration]` attributes
- **Build failures**: Check schema syntax in `/schemas/` directory
- **Enum duplicate errors**: Use consolidated enum definitions in schema `components/schemas` with `$ref` references
- **Parameter type mismatches**: Ensure interface generation properly handles `$ref` parameters (Provider not string)

### Debugging Tools
- **Swagger UI**: Available at `/swagger` in development

## Git Workflow

### Commit Policy
- **Never commit** unless explicitly instructed by user
- The user handles formatting and integration testing before commits
