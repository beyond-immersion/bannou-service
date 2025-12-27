# Bannou Service Development Instructions

## ⛔ TENETS ARE LAW ⛔

**ALWAYS REFER TO AND FOLLOW THE TENETS.MD FILE (`docs/reference/TENETS.md`) WITHOUT EXCEPTION. IF YOU DO NOT UNDERSTAND THE TENETS PERFECTLY, YOU MUST RE-READ THE FILE. ANY SITUATION WHICH CALLS INTO QUESTION ONE OF THE TENETS MUST BE EXPLICITLY PRESENTED TO THE USER, CONTEXT PROVIDED, AND THEN APPROVED TO CONTINUE.**

---

## 🚨 CRITICAL DEVELOPMENT CONSTRAINTS

**MANDATORY**: Never declare success or move to the next step until the current task is 100% complete and verified.

**COMPLETION VERIFICATION RULES**:
- All code must compile without errors (`dotnet build` succeeds)
- If the user ran tests and they were failing, those tests must now pass
- All generated files must work together correctly
- Architectural problems must be fully resolved, not worked around
- **NOTE**: This does NOT mean "run tests after every change" - see "NEVER Run Integration Tests Unless Explicitly Asked" below

**STEP-BY-STEP ENFORCEMENT**:
- Complete each step fully before proceeding
- Verify each step works in isolation
- Never skip ahead due to complexity or difficulty
- Ask for clarification rather than making assumptions that break the architecture

**SUCCESS DECLARATION PROHIBITION**:
- Never claim success when compilation errors remain
- Never call a task "complete" when core functionality is broken
- Never mark todos as complete when issues persist
- Always demonstrate working functionality before claiming success

## ⚠️ MANDATORY REFERENCE - Architecture & Development Docs

**CRITICAL**: For ALL Bannou service design and development tasks, reference the following documentation before proceeding:

**Primary Documentation**:
- **Architecture & Philosophy**: `docs/BANNOU_DESIGN.md` - Why the system works the way it does
- **Plugin Development**: `docs/guides/PLUGIN_DEVELOPMENT.md` - How to add and extend services
- **Current Tasks**: `CURRENT_TASKS.md` - Active implementation work

**⚠️ MANDATORY REFERENCE TRIGGERS**:
- Service API design or modification → `BANNOU_DESIGN.md` + `PLUGIN_DEVELOPMENT.md`
- Creating new services → `PLUGIN_DEVELOPMENT.md`
- WebSocket protocol questions → `docs/WEBSOCKET-PROTOCOL.md`
- Debugging service issues → `PLUGIN_DEVELOPMENT.md` + `docs/reference/TENETS.md`
- Configuration questions → `docs/reference/CONFIGURATION.md`
- Deployment patterns → `docs/guides/DEPLOYMENT.md`

**IMPLEMENTATION GUIDES**: For specific service implementation details:
- **Behavior Service APIs**: Reference NPC-Behavior-Service-APIs-and-Testing.md for ABML and character behavior implementation
- **Service-Specific Guides**: Reference implementation guides when working on complex service-specific patterns

## Critical Development Rules

### NEVER Manually Implement Library Functionality
**MANDATORY**: YOU ARE FORBIDDEN FROM MANUALLY IMPLEMENTING SOMETHING THAT WE INSTALLED A LIBRARY TO HANDLE.
- **Always use installed libraries**: Use the proper library APIs instead of manual implementations
- **No custom implementations**: Never write manual JWT, crypto, serialization, or other functionality when libraries exist
- **Research first**: Always research the correct library API before implementing
- **Ask for clarification**: If library API is unclear, ask for help rather than implementing manually

### NEVER Use Null-Forgiving Operators or Null Casts
**MANDATORY**: Never use null-forgiving operators (`!`) or cast null to non-nullable types anywhere in Bannou code as they cause segmentation faults and hide null reference exceptions.
- **Prohibited**: `variable!`, `property!`, `method()!`, `null!`, `default!`, `(Type)null`
- **Required**: Always use explicit null checks with meaningful exceptions or proper test data
- **Example Correct**: `var value = variable ?? throw new ArgumentNullException(nameof(variable));`
- **Example Incorrect**: `var value = variable!;` or `var value = (Type)null;`
- **Test Rule**: Tests should use real data, not null casts. If testing null handling, use nullable types properly.
- **Principle**: Explicit null safety prevents segmentation faults and provides clear error messages

### NEVER Export Environment Variables
**MANDATORY**: Never use `export` commands to set environment variables on the local machine. This confuses containerization workflows and creates debugging issues.
- **Correct**: Use .env files and Docker Compose environment configuration
- **Incorrect**: `export VARIABLE=value` commands
- **Principle**: We use containerization workflows - configuration belongs in containers, not host environments

### ⛔ NEVER Run Integration Tests Unless Explicitly Asked
**MANDATORY**: NEVER run `make test-http`, `make test-edge`, `make test-infrastructure`, or `make all` unless the user EXPLICITLY asks you to run tests.
- **Verification for code changes**: A successful `dotnet build` is sufficient verification for refactoring, bug fixes, and feature implementation
- **DO NOT** add "test" or "rebuild and test" to your todo lists unless the user specifically requested testing
- **DO NOT** run container-based tests to "verify" your changes work - the build verifies compilation
- **The user will ask for tests when they want tests** - do not assume testing is needed
- **Why this matters**: Integration tests take 5-10 minutes, rebuild containers, and are disruptive. Running them without being asked wastes significant time.

### ⚠️ MANDATORY REFERENCE - TESTING.md for ALL Testing Tasks
**CRITICAL**: For ANY task involving tests, testing architecture, or test placement, you MUST ALWAYS reference the testing documentation (`docs/guides/TESTING.md`) FIRST and IN FULL before proceeding with any work.

**MANDATORY TESTING WORKFLOW**:
1. Read `docs/guides/TESTING.md` completely to understand plugin isolation boundaries
2. Use the decision guide to determine correct test placement
3. Follow architectural constraints (unit-tests cannot reference plugins, lib-testing cannot reference other plugins, etc.)
4. ALWAYS respond with "I have referred to the service testing document" to confirm you read it

**⚠️ MANDATORY REFERENCE TRIGGERS** - You MUST reference `docs/guides/TESTING.md` for:
- ANY task involving writing, modifying, or debugging tests
- Questions about where to place tests (unit tests vs infrastructure tests vs integration tests)
- Testing configuration classes, service functionality, or cross-service communication
- Debugging test failures or compilation errors in test projects
- Understanding test isolation boundaries and plugin loading constraints
- ANY testing-related architectural decisions

**TESTING ARCHITECTURE RULES** (from TESTING.md):
- `unit-tests/`: Can ONLY reference `bannou-service`. CANNOT reference ANY `lib-*` plugins
- `lib-*.tests/`: Can ONLY reference their own `lib-*` plugin + `bannou-service`. CANNOT reference other `lib-*` plugins
- `lib-testing/`: Can ONLY reference `bannou-service`. CANNOT reference ANY other `lib-*` plugins
- `http-tester/`: Can reference all services via generated clients
- `edge-tester/`: Can test all services via WebSocket protocol

**VIOLATION PREVENTION**: Never attempt to reference AuthServiceConfiguration from lib-testing, never reference plugin types from unit-tests, never test business logic in lib-testing

### Always Check GitHub Actions for Testing Workflows
**MANDATORY**: Before attempting any integration testing work, ALWAYS check `.github/workflows/ci.integration.yml` first to understand the proper testing approach.
- The GitHub Actions workflow defines the authoritative 10-step testing pipeline
- Local testing should mirror the CI approach, not invent new approaches
- Infrastructure testing, HTTP testing, and WebSocket testing all have established patterns

### Always Reference the Makefile
**MANDATORY**: The Makefile contains all established commands and patterns. Always check it before creating new commands or approaches.

**Available Makefile Commands**:
Reference the Makefile in the repository root for all available commands and established patterns.

### Prefer .env Files Over Other Configuration
**MANDATORY**: In our containerization workflow, always prefer .env files over other configuration methods:
- **Use**: .env files for environment configuration
- **Avoid**: appsettings.json, Config.json, hardcoded configuration
- **Principle**: Container-first configuration management

### Schema-First Development (MANDATORY)
**ALL development follows schema-first architecture - never edit generated code manually.**

**🚨 CRITICAL RULE - SERVICE IMPLEMENTATION ONLY**:
**NEVER edit ANY file in a service plugin except the service implementation class (e.g., `ConnectService.cs`)**
- **Generated Files**: NEVER edit any files in `*/Generated/` directories or `.Generated.cs` files
- **Controllers**: NEVER create or edit controller files - they are auto-generated wrappers
- **Interfaces**: NEVER edit generated interfaces - service implementation is the source of truth
- **Models**: NEVER edit generated models - they come from OpenAPI schemas
- **Service Implementation Authority**: If there are conflicts between service implementation and generated types, the service implementation is authoritative

**🎯 Schema Design Best Practices**:
- **Enum Consolidation**: Use shared enum definitions in `components/schemas` section with `$ref` references to avoid duplication (e.g., Provider enum)
- **$ref Resolution**: Interface generation properly handles `$ref` enum parameters - never fallback to string types
- **Duplicate Prevention**: Fix schema duplications at source rather than using exclusions in generation scripts
- **⚠️ CRITICAL - `servers` URL**: ALL schemas MUST use the base endpoint:
  ```yaml
  servers:
    - url: http://localhost:5012
  ```
  NSwag generates controller route prefixes from this URL. Using direct paths ensures generated routes match what clients send.

**Required Workflow**:
1. **Schema First**: Edit OpenAPI YAML in `/schemas/` directory
2. **Generate**: Run `scripts/generate-all-services.sh` to create controllers/models/clients
3. **Implement**: Write business logic ONLY in service implementation classes (e.g., `SomeService.cs`)
4. **Format**: Run `make format` to fix line endings and C# syntax

**Architecture Rules**:
- **Services Return Tuples**: `(StatusCodes, ResponseModel?)` using custom enum
- **Never Edit Generated Files**: Any `*/Generated/` or `.Generated.cs` files are auto-generated
- **Use Generated Clients**: Service-to-service calls use NSwag-generated clients, not direct interfaces
- **Infrastructure Libs Pattern**: Use lib-state, lib-messaging, and lib-mesh for all infrastructure (never direct Redis/RabbitMQ/HTTP)
- **Controller = Service Wrapper**: Generated controllers are just wrappers around service implementations

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

### Safety Protocols
**Before Any Code Changes**:
- Will this affect `/Generated/` files? → Fix schema instead
- Will this modify non-C# files? → Verify file types explicitly  
- Am I bypassing schema-first workflow? → Use proper generation pipeline
- **On violations**: STOP and ask for direction

### Service Architecture
```
lib-{service}/                        # Single consolidated service plugin  
├── Generated/                        # NSwag auto-generated files
│   ├── {Service}Controller.Generated.cs  # Abstract controller base  
│   ├── I{Service}Service.cs          # Service interface (generated from controller)
│   ├── {Service}Client.cs            # Service client for inter-service calls
│   └── {Service}ServiceConfiguration.cs  # Generated configuration class
├── {Service}Service.cs               # Business logic implementation (ONLY manual file)
└── lib-{service}.csproj              # Generated project file
```

**Key Points**: All generated files are in `lib-{service}/Generated/`. Only the service implementation (business logic) is manual.

## Development Workflow

### Essential Commands
```bash
# Service Development
scripts/generate-all-services.sh     # Generate controllers/models/clients from schemas
make format                    # Fix line endings + C# formatting
make build                     # Build all services
make all                       # Complete dev cycle (clean, generate, build, all tests)

# Testing
make test                      # Run unit tests (dotnet test)
make test-infrastructure       # Infrastructure validation (Docker health)
make test-http                 # Service-to-service HTTP testing
make test-edge                 # WebSocket protocol testing
```

### Code Quality Requirements
- **XML Documentation**: All public classes/methods must have `<summary>` tags
- **EditorConfig**: LF line endings enforced across all file types
- **Generated Code**: Never edit files in `*/Generated/` directories
- **Formatting**: Always run `make format` before changes

### Testing Strategy
**Three-tier validation** ensures comprehensive coverage:
1. **Infrastructure** (Tier 1): Basic service availability via Docker Compose
2. **Service Logic** (Tier 2): HTTP API testing with generated clients  
3. **Client Experience** (Tier 3): WebSocket binary protocol validation

### **MANDATORY**: Reference Detailed Procedures
**When testing fails or debugging complex issues**, you MUST reference the detailed development procedures documentation in the knowledge base for troubleshooting guides, Docker Compose configurations, and step-by-step debugging procedures.

### Environment Configuration

**Local Development with .env Files**:
- **Primary Configuration**: Use `.env` file in repository root for all environment variables
- **Service Prefix**: Use `BANNOU_` prefix for service-specific variables (e.g., `BANNOU_HTTP_Web_Host_Port=5012`)
- **Non-Prefixed Support**: Maintain backwards compatibility with non-prefixed variables
- **Configuration Loading**: System automatically loads .env files from current or parent directories

**Environment Variable Patterns**:
```bash
# Port Configuration
HTTP_Web_Host_Port=5012
HTTPS_Web_Host_Port=5013
BANNOU_HTTP_Web_Host_Port=5012    # Service-specific with prefix
BANNOU_HTTPS_Web_Host_Port=5013

# Service Configuration
SERVICE_DOMAIN=example.com

# Database Configuration
ACCOUNT_DB_USER=testuser
ACCOUNT_DB_PASSWORD=testpassword

# Auth Configuration
AUTH_JWT_SECRET=bannou-dev-secret-key-2025-please-change-in-production
AUTH_JWT_ISSUER=bannou-auth-dev
AUTH_JWT_AUDIENCE=bannou-api-dev
AUTH_JWT_EXPIRATION_MINUTES=60
```

**Configuration Implementation Details**:
- **DotNetEnv Integration**: Automatic .env file loading via DotNetEnv package (3.1.1)
- **Service-Specific Binding**: `[ServiceConfiguration(envPrefix: "BANNOU_")]` attribute on configuration classes
- **Hierarchy**: .env files checked in current directory, then parent directory
- **Fallback**: Non-prefixed variables maintained for backwards compatibility

### Code Generation Systems

**NSwag (Primary)**: Generates controllers, models, and service clients from OpenAPI schemas
- Uses custom file-scoped namespace templates in `templates/nswag/`
- **Generated Files**: Controllers, request/response models, event models, client classes
- **Command**: `scripts/generate-all-services.sh` (unified script)

**Roslyn (Specialized)**: Generates patterns NSwag cannot handle
- Unit test projects, DI registrations
- **EventModelGenerator**: ✅ DISABLED (NSwag handles events)
- **ServiceScaffoldGenerator**: ✅ DISABLED (NSwag handles service scaffolding)
- **UnitTestGenerator**: ✅ WORKING

## Service Implementation Patterns

### Service Implementation Pattern
```csharp
// ONLY manual file - implements generated interface
[BannouService("service-name", typeof(IServiceInterface), lifetime: ServiceLifetime.Scoped)]
public class ServiceNameService : IServiceNameService // Implement generated interface
{
    private readonly IStateStore<ServiceModel> _stateStore;  // lib-state
    private readonly IMessageBus _messageBus;                 // lib-messaging
    private readonly ILogger<ServiceNameService> _logger;
    private readonly ServiceNameServiceConfiguration _configuration; // Generated config class
}
```

Configuration classes are generated in `Generated/` from schema - never edit manually.

### **MANDATORY**: Complex Service Implementation
**When implementing complex service patterns or debugging service issues**, you MUST reference the detailed development procedures documentation for complete infrastructure lib patterns, service client examples, event publishing patterns, and architectural implementation details.

### Infrastructure Lib Patterns
**State Management (lib-state)**:
```csharp
_stateStore = stateStoreFactory.Create<ServiceModel>("service-name");
await _stateStore.SaveAsync(key, data);
var data = await _stateStore.GetAsync(key);
```

**Event Publishing (lib-messaging)**:
```csharp
await _messageBus.PublishAsync("event.topic", eventModel);
```

### Assembly Loading & Service Discovery
- **Default Routing**: All services route to "bannou" (omnipotent default)
- **Production**: Event-driven service-to-app-id mapping via Redis routing tables
- **Service Attributes**: `[BannouService]` enables automatic discovery and DI registration

## Arcadia Game Integration

### Current Development Phase
**Focus**: NPC Behavior Systems with ABML YAML DSL for autonomous character behaviors

### Active Services
**✅ Production Ready**: Accounts, Auth, Connect (WebSocket gateway), Website, Behavior foundation  
**🔧 In Development**: ABML YAML Parser, Character Agent Services, Cross-service event integration

## Troubleshooting Reference

### **MANDATORY**: Complex Troubleshooting  
**When encountering complex issues beyond these basics**, you MUST reference the detailed development procedures documentation in the knowledge base.

### Common Issues
- **Generated file errors**: Fix underlying schema, never edit generated files directly
- **Line ending issues**: Run `make format` after code generation
- **Service registration**: Verify `[BannouService]` and `[ServiceConfiguration]` attributes
- **Build failures**: Check schema syntax in `/schemas/` directory
- **Enum duplicate errors**: Use consolidated enum definitions in schema `components/schemas` with `$ref` references
- **Parameter type mismatches**: Ensure interface generation properly handles `$ref` parameters (Provider not string)
- **Script not found errors**: All generation scripts moved to `scripts/` directory - use `scripts/generate-all-services.sh`

### Debugging Tools
- **Swagger UI**: Available at `/swagger` in development
- **Service discovery**: Connect service provides runtime service mappings
- **Integration logs**: Docker Compose provides complete request tracing

## Git Workflow

### Commit Policy (MANDATORY)
- **Never commit** unless explicitly instructed by user
- **Always run `git diff` against last commit** before committing to review all changes
- **Always format** code with `make format` before committing
- **Run tests** locally before committing (use `make all`)
- Present changes for user review and get explicit approval first

### Pre-Commit Checklist
```bash
# Required before every commit:
make format                    # Fix formatting and line endings
make all                       # Run full test suite (or individual: make test && make test-http && make test-edge)
git diff HEAD~1               # Review ALL changes since last commit
# Only commit after user explicitly requests it
```

### Standard Commit Format
```bash
git commit -m "Descriptive message

🤖 Generated with [Claude Code](https://claude.ai/code)

Co-Authored-By: Claude <noreply@anthropic.com>"
```

---

## Reference Documentation

### **MANDATORY References** (Must Use When Applicable):
**Development Procedures**: See knowledge base implementation guides
- Required for: Testing failures, debugging, complex service implementation, troubleshooting

**Orchestrator Tasks**: See `docs/ORCHESTRATOR-SERVICE-DESIGN.md`
- Required for: Deployment topology, preset creation, service discovery patterns, container orchestration
- Key patterns: ExtraHosts IP injection, Redis heartbeat system, mesh routing

### Additional References:
**CI/CD Pipeline**: `/NUGET-SETUP.md` - 10-step CI pipeline, NuGet publishing, compatibility testing  
**Project Architecture**: Referenced in core memory (BANNOU_CORE_MEMORY.md)  
**Current Objectives**: Referenced in core memory (OBJECTIVES_CORE_MEMORY.md)
