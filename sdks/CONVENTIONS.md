# SDK Naming Conventions and Structure

This document defines the naming conventions and directory structure for all Bannou SDKs. These conventions follow [Azure SDK naming guidelines](https://azure.github.io/azure-sdk/dotnet_introduction.html) and [Microsoft namespace naming guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces).

## Directory Structure

All SDK projects live under the `sdks/` directory with lowercase kebab-case naming:

```
sdks/
├── client/                      # Core client SDK
├── client.tests/                # Client SDK tests
├── client-voice/                # Voice chat extension
├── client-voice.tests/          # Voice SDK tests
├── server/                      # Server SDK (includes service clients)
├── server.tests/                # Server SDK tests
├── scene-composer/              # Engine-agnostic scene composition
├── scene-composer.tests/        # SceneComposer tests
├── scene-composer-stride/       # Stride engine integration
├── scene-composer-stride.tests/ # Stride integration tests
├── scene-composer-godot/        # Godot engine integration
├── scene-composer-godot.tests/  # Godot integration tests
├── protocol/                    # Shared protocol definitions (not a NuGet package)
└── transport/                   # Shared transport code (not a NuGet package)

examples/
└── godot-scene-composer/        # Demo project for Godot SceneComposer
```

## Package Naming

### Rules

1. **No "SDK" suffix** - Per Azure SDK guidelines, package names should NOT contain "SDK"
2. **Company prefix** - All packages use `BeyondImmersion.Bannou` prefix
3. **Feature hierarchy** - Engine-specific packages use `SceneComposer.{Engine}` pattern (not `{Engine}.SceneComposer`)
4. **Consistency** - PackageId, RootNamespace, and AssemblyName must all match

### Package Registry

| Directory | PackageId | Description |
|-----------|-----------|-------------|
| `client/` | `BeyondImmersion.Bannou.Client` | WebSocket client for game clients |
| `client-voice/` | `BeyondImmersion.Bannou.Client.Voice` | P2P voice chat with SIP/RTP scaling |
| `server/` | `BeyondImmersion.Bannou.Server` | Server SDK with mesh service clients |
| `scene-composer/` | `BeyondImmersion.Bannou.SceneComposer` | Engine-agnostic scene editing |
| `scene-composer-stride/` | `BeyondImmersion.Bannou.SceneComposer.Stride` | Stride engine integration |
| `scene-composer-godot/` | `BeyondImmersion.Bannou.SceneComposer.Godot` | Godot engine integration |

### Namespace Pattern

```
BeyondImmersion.Bannou.{Feature}[.{SubFeature}]
```

Examples:
- `BeyondImmersion.Bannou.Client`
- `BeyondImmersion.Bannou.Client.Voice`
- `BeyondImmersion.Bannou.SceneComposer`
- `BeyondImmersion.Bannou.SceneComposer.Stride`
- `BeyondImmersion.Bannou.SceneComposer.Godot`

## Project File Requirements

Every SDK project must have these properties aligned:

```xml
<PropertyGroup>
  <PackageId>BeyondImmersion.Bannou.{Feature}</PackageId>
  <RootNamespace>BeyondImmersion.Bannou.{Feature}</RootNamespace>
  <AssemblyName>BeyondImmersion.Bannou.{Feature}</AssemblyName>
</PropertyGroup>
```

For test projects:

```xml
<PropertyGroup>
  <RootNamespace>BeyondImmersion.Bannou.{Feature}.Tests</RootNamespace>
  <AssemblyName>BeyondImmersion.Bannou.{Feature}.Tests</AssemblyName>
  <IsPackable>false</IsPackable>
  <IsTestProject>true</IsTestProject>
</PropertyGroup>
```

## Directory Naming Rules

1. **Lowercase kebab-case** - All directory names use lowercase with hyphens
2. **Test suffix** - Test projects use `.tests` suffix (not `-tests` or `Tests`)
3. **Feature grouping** - Related packages share a prefix (e.g., `scene-composer-*`)

### Examples

| Correct | Incorrect |
|---------|-----------|
| `scene-composer/` | `SceneComposer/` |
| `scene-composer-stride/` | `scene-composer.stride/` |
| `client-voice/` | `ClientVoice/` |
| `client.tests/` | `client-tests/` |

## Adding a New SDK

When creating a new SDK package:

1. **Create directory** under `sdks/` with kebab-case name
2. **Create csproj** with all three identifiers aligned (PackageId, RootNamespace, AssemblyName)
3. **Add to solution** using `dotnet sln bannou-service.sln add sdks/{name}/{name}.csproj`
4. **Create test project** in `sdks/{name}.tests/` with matching conventions
5. **Update documentation** in `docs/guides/SDKs.md`

## Engine-Specific SDK Pattern

For engine integrations (Stride, Godot, Unity, Unreal, etc.):

1. **Directory**: `scene-composer-{engine}/`
2. **Package**: `BeyondImmersion.Bannou.SceneComposer.{Engine}`
3. **Namespace**: `BeyondImmersion.Bannou.SceneComposer.{Engine}`

The engine name comes AFTER `SceneComposer` to maintain logical grouping in NuGet search results and IDE autocomplete.

## Shared Code (Non-Package)

The `protocol/` and `transport/` directories contain shared code that is NOT published as separate NuGet packages. These are:

- **protocol/** - Game protocol definitions (MessagePack DTOs, envelope format)
- **transport/** - LiteNetLib transport implementations

These are referenced directly by other SDK projects via `<Compile Include="...">` or project references.

## Version Management

All SDK packages share a single version number defined in `SDK_VERSION` at the repository root. This ensures:

- Consistent versioning across all packages
- Simplified dependency management
- Clear release coordination

## References

- [Azure SDK .NET Design Guidelines](https://azure.github.io/azure-sdk/dotnet_introduction.html)
- [Microsoft Namespace Naming Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces)
- [NuGet Package Authoring Best Practices](https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)
- [GitHub Issue #107](https://github.com/BeyondImmersion/bannou/issues/107) - Original restructuring proposal
