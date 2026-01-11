# Contributing to Bannou

Thank you for your interest in contributing to Bannou! This document will help you get started and understand how we work together to build this project.

## Welcome

Bannou is an open source game backend platform, and we genuinely appreciate contributions of all kinds: bug reports, documentation improvements, feature suggestions, and code contributions. Whether you're fixing a typo or proposing a major feature, you're helping make game development more accessible.

## Community

- **Discord**: Join us at [Beyond Immersion Discord](https://discord.gg/3eAGYwF3rE) for discussion, questions, and collaboration
- **GitHub Issues**: For bug reports and feature requests
- **GitHub Discussions**: For questions and broader conversations

## Before You Start

### Understanding the Architecture

Bannou uses a **schema-first development** approach. This is central to how the project works:

1. **OpenAPI schemas** (`/schemas/`) are the source of truth for all APIs
2. **Code generation** creates controllers, models, and clients from these schemas
3. **Service implementations** (`*Service.cs`) contain only business logic

This means:
- **Never edit files in `*/Generated/` directories** - they will be overwritten
- **To change an API**, edit the schema in `/schemas/`, then run `make generate`
- **To add business logic**, edit only the service implementation file

### Why Standards Matter Here

Bannou is designed to work with AI agents and automated tooling. The codebase follows strict conventions so that:

- AI assistants can reliably navigate and modify code
- Code generation produces consistent, predictable output
- Multiple contributors can work without stepping on each other

This isn't bureaucracy for its own sake - it's what makes a schema-driven architecture work at scale.

## How to Contribute

### Reporting Bugs

1. Check [existing issues](https://github.com/beyond-immersion/bannou-service/issues) first
2. Include:
   - What you expected to happen
   - What actually happened
   - Steps to reproduce
   - Your environment (OS, .NET version, Docker version)
3. If it's a schema or generation issue, note which service is affected

### Suggesting Features

1. Open a GitHub Discussion or Issue
2. Explain the use case - what problem are you solving?
3. If you have implementation ideas, share them, but focus on the "why" first

### Contributing Code

#### Getting Started

```bash
# Clone the repository
git clone https://github.com/beyond-immersion/bannou-service.git
cd bannou-service

# Start the local stack
make up-compose

# Run tests to verify your setup
make test
```

#### Development Workflow

1. **Create a branch** from `master`
2. **Make your changes** following the patterns below
3. **Format your code**: `make format`
4. **Run tests**: `make test`
5. **Build**: `make build`
6. **Submit a PR** with a clear description

#### Adding or Modifying a Service

If you're changing API behavior:

```bash
# 1. Edit the schema
#    schemas/{service}-api.yaml

# 2. Regenerate code
make generate

# 3. Implement business logic
#    plugins/lib-{service}/{Service}Service.cs

# 4. Format and test
make format
make test
```

#### Key Patterns to Follow

**Return Tuples, Not Exceptions**
```csharp
// Correct - return status codes
public async Task<(StatusCodes, GetAccountResponse?)> GetAsync(GetAccountRequest request)
{
    if (account == null)
        return (StatusCodes.NotFound, null);
    return (StatusCodes.OK, response);
}
```

**Use Infrastructure Libraries**
```csharp
// Correct - use lib-state
var data = await _stateStore.GetAsync(key);

// Wrong - direct database access
var data = await _redisConnection.GetAsync(key);
```

**Explicit Null Handling**
```csharp
// Correct - explicit null check
var value = variable ?? throw new ArgumentNullException(nameof(variable));

// Wrong - null-forgiving operator
var value = variable!;
```

### Documentation Contributions

Documentation improvements are always welcome! The docs live in `/docs/` and use Markdown.

Focus areas:
- Clarifying existing guides
- Adding examples
- Fixing typos and broken links
- Translating documentation

## Code Review Process

1. All PRs require review before merging
2. CI must pass (build, format check, tests)
3. We'll provide feedback within a few days for most PRs
4. Be open to suggestions - we're all learning together

## What We're Looking For

### Good First Issues

Look for issues labeled `good first issue` - these are specifically chosen to be approachable for new contributors.

### Areas Where Help is Appreciated

- **Engine SDKs**: Godot, Unity, Unreal integration
- **Documentation**: Tutorials, examples, translations
- **Testing**: Edge cases, integration scenarios
- **Performance**: Profiling and optimization

### What's Harder to Accept

- Changes that bypass schema-first workflow
- Direct edits to generated code
- Features that only work for a single use case
- Large refactors without prior discussion

## Development Environment

### Requirements

- .NET 8.0 or 9.0 SDK
- Docker and Docker Compose
- Make (or run commands manually)

### Useful Commands

```bash
make build              # Build all projects
make test               # Run unit tests
make generate           # Regenerate from schemas
make format             # Fix formatting
make up-compose         # Start local infrastructure
make down-compose       # Stop infrastructure
```

### IDE Setup

Any IDE works, but we recommend:
- **VS Code** with C# Dev Kit extension
- **JetBrains Rider**
- **Visual Studio 2022**

EditorConfig is provided for consistent formatting.

## Questions?

- **Quick questions**: Discord is fastest
- **Detailed questions**: GitHub Discussions
- **Found a bug**: GitHub Issues

## License

By contributing to Bannou, you agree that your contributions will be licensed under the MIT License. See [LICENSE](LICENSE) for details.

---

Thank you for contributing to Bannou! Every contribution helps make game development more accessible to developers everywhere.
