# Bannou Service Development Instructions

## ⚠️ MANDATORY REFERENCE - API-DESIGN.md

**CRITICAL**: For ALL Bannou service design and development tasks, you MUST ALWAYS reference the authoritative API-DESIGN.md documentation FIRST before proceeding with any work.

**Location**: Technical Architecture knowledge base section (API-DESIGN.md)

This document defines the single source of truth for schema-driven development approach, consolidated service architecture patterns, WebSocket-first integration, and complete implementation workflows that must be followed for all Bannou services.

**⚠️ MANDATORY REFERENCE TRIGGERS** - You MUST reference API-DESIGN.md for:
- ANY service API design or modification tasks
- Creating new services or modifying existing service architecture
- Service integration with Dapr, WebSocket protocols, or client generation
- Debugging service implementation issues or schema generation problems
- Questions about schema-first development patterns or validation
- Understanding event types, datastore requirements, or testing strategies
- API versioning, breaking changes, or backwards compatibility
- Service testing strategies and deployment patterns

**⚠️ MANDATORY UPDATE REQUIREMENT**:
When making changes to service APIs or architectural patterns, you MUST update API-DESIGN.md to reflect:
- New architectural decisions or patterns
- Changes to schema-first development workflow
- Service integration pattern modifications
- Testing strategy updates
- Any deviations from established patterns

**IMPLEMENTATION GUIDES**: For specific service implementation details beyond API-DESIGN.md, consult dedicated implementation guides in the knowledge base:
- **Behavior Service APIs**: Reference NPC-Behavior-Service-APIs-and-Testing.md for ABML and character behavior implementation
- **Service-Specific Guides**: Reference implementation guides when working on complex service-specific patterns

## Critical Development Rules

### NEVER Export Environment Variables
**MANDATORY**: Never use `export` commands to set environment variables on the local machine. This confuses containerization workflows and creates debugging issues.
- **Correct**: Use .env files and Docker Compose environment configuration
- **Incorrect**: `export VARIABLE=value` commands
- **Principle**: We use containerization workflows - configuration belongs in containers, not host environments

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

**Required Workflow**:
1. **Schema First**: Edit OpenAPI YAML in `/schemas/` directory
2. **Generate**: Run `scripts/generate-all-services.sh` to create controllers/models/clients
3. **Implement**: Write business logic ONLY in service implementation classes (e.g., `SomeService.cs`)
4. **Format**: Run `make format` to fix line endings and C# syntax

**Architecture Rules**:
- **Services Return Tuples**: `(StatusCodes, ResponseModel?)` using custom enum
- **Never Edit Generated Files**: Any `*/Generated/` or `.Generated.cs` files are auto-generated
- **Use Generated Clients**: Service-to-service calls use NSwag-generated clients, not direct interfaces
- **Dapr-First Patterns**: Use DaprClient for state/events, never Entity Framework directly
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
make test-all-v2              # Run all tests (unit + integration + websocket)

# Testing
make test-integration-v2       # Quick infrastructure validation
make test-http                 # Service-to-service HTTP testing  
make test-websocket            # WebSocket protocol testing
make ci-test-v2               # Full CI pipeline locally
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
BANNOU_EmulateDapr=True           # Enables Dapr emulation for local development
SERVICE_DOMAIN=beyond-immersion.com

# Database Configuration
ACCOUNT_DB_USER=Franklin
ACCOUNT_DB_PASSWORD=DevPassword

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
[DaprService("service-name", typeof(IServiceInterface), lifetime: ServiceLifetime.Scoped)]
public class ServiceNameService : IServiceNameService // Implement generated interface
{
    private readonly DaprClient _daprClient;           // Required for all services
    private readonly ILogger<ServiceNameService> _logger;
    private readonly ServiceNameServiceConfiguration _configuration; // Generated config class
}
```

Configuration classes are generated in `Generated/` from schema - never edit manually.

### **MANDATORY**: Complex Service Implementation
**When implementing complex service patterns or debugging service issues**, you MUST reference the detailed development procedures documentation for complete Dapr patterns, service client examples, event publishing patterns, and architectural implementation details.

### Dapr Integration Patterns
**State Management**:
```csharp
private const string STATE_STORE = "{service-name}-store";
await _daprClient.SaveStateAsync(STATE_STORE, key, data);
var data = await _daprClient.GetStateAsync<ModelType>(STATE_STORE, key);
```

**Event Publishing**:
```csharp
await _daprClient.PublishEventAsync("bannou-pubsub", "event-topic", eventModel);
```

### Assembly Loading & Service Discovery
- **Default Routing**: All services route to "bannou" (omnipotent default)
- **Production**: Event-driven service-to-app-id mapping via RabbitMQ
- **Service Attributes**: `[DaprService]` enables automatic discovery and DI registration

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
- **Service registration**: Verify `[DaprService]` and `[ServiceConfiguration]` attributes
- **Build failures**: Check schema syntax in `/schemas/` directory

### Debugging Tools
- **Swagger UI**: Available at `/swagger` in development
- **Service discovery**: Connect service provides runtime service mappings
- **Integration logs**: Docker Compose provides complete request tracing

## Git Workflow

### Commit Policy (MANDATORY)
- **Never commit** unless explicitly instructed by user
- **Always run `git diff` against last commit** before committing to review all changes
- **Always format** code with `make format` before committing
- **Run tests** locally before committing (use `make test-all-v2`)
- Present changes for user review and get explicit approval first

### Pre-Commit Checklist
```bash
# Required before every commit:
make format                    # Fix formatting and line endings
make test-all-v2              # Run full test suite
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

### Additional References:
**CI/CD Pipeline**: `/NUGET-SETUP.md` - 10-step CI pipeline, NuGet publishing, compatibility testing  
**Project Architecture**: Referenced in core memory (BANNOU_CORE_MEMORY.md)  
**Current Objectives**: Referenced in core memory (OBJECTIVES_CORE_MEMORY.md)
