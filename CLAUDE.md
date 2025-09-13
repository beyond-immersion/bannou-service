# Bannou Service Development Instructions

## Critical Development Rules

### Schema-First Development (MANDATORY)
**ALL development follows schema-first architecture - never edit generated code manually.**

**Required Workflow**:
1. **Schema First**: Edit OpenAPI YAML in `/schemas/` directory
2. **Generate**: Run `./generate-all-services.sh` to create controllers/models/clients
3. **Implement**: Write business logic ONLY in service implementation classes
4. **Format**: Run `make format` to fix line endings and C# syntax

**Architecture Rules**:
- **Services Return Tuples**: `(StatusCodes, ResponseModel?)` using custom enum
- **Never Edit Generated Files**: Any `*/Generated/` or `.Generated.cs` files are auto-generated
- **Use Generated Clients**: Service-to-service calls use NSwag-generated clients, not direct interfaces
- **Dapr-First Patterns**: Use DaprClient for state/events, never Entity Framework directly

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
./generate-all-services.sh     # Generate controllers/models/clients from schemas
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

### Code Generation Systems

**NSwag (Primary)**: Generates controllers, models, and service clients from OpenAPI schemas
- Uses custom file-scoped namespace templates in `templates/nswag/`
- **Generated Files**: Controllers, request/response models, event models, client classes
- **Command**: `./generate-all-services.sh` (unified script)

**Roslyn (Specialized)**: Generates patterns NSwag cannot handle  
- Service scaffolding, unit test projects, DI registrations
- **EventModelGenerator**: ✅ DISABLED (NSwag handles events)
- **ServiceScaffoldGenerator**: ✅ WORKING
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
