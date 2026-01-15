# SDK Naming Conventions and Structure

This document defines the naming conventions and directory structure for all Bannou SDKs. These conventions follow [Azure SDK naming guidelines](https://azure.github.io/azure-sdk/dotnet_introduction.html) and [Microsoft namespace naming guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces).

## Directory Structure

All SDK projects live under the `sdks/` directory with lowercase kebab-case naming:

```
sdks/
├── bannou-sdks.sln              # SDK solution file (separate from server)
├── SDK_VERSION                  # Shared version number for all SDK packages
├── CONVENTIONS.md               # This file - SDK naming conventions
├── core/                        # Shared types (BannouJson, ApiException, base events)
├── core.tests/                  # Core library tests
├── client/                      # Core client SDK
├── client.tests/                # Client SDK tests
├── client-voice/                # Voice chat extension
├── client-voice.tests/          # Voice SDK tests
├── server/                      # Server SDK (includes service clients)
├── server.tests/                # Server SDK tests
├── asset-bundler/               # Engine-agnostic asset bundling pipeline
├── asset-bundler.tests/         # AssetBundler tests
├── asset-bundler-stride/        # Stride engine asset compilation
├── asset-bundler-stride.tests/  # Stride AssetBundler tests
├── asset-loader/                # Engine-agnostic asset loading and caching
├── asset-loader.tests/          # AssetLoader tests
├── asset-loader-client/         # WebSocket-based asset source (game clients)
├── asset-loader-client.tests/   # AssetLoader Client tests
├── asset-loader-server/         # Mesh-based asset source (game servers)
├── asset-loader-server.tests/   # AssetLoader Server tests
├── asset-loader-stride/         # Stride engine type loaders
├── asset-loader-stride.tests/   # Stride AssetLoader tests
├── scene-composer/              # Engine-agnostic scene composition
├── scene-composer.tests/        # SceneComposer tests
├── scene-composer-stride/       # Stride engine integration
├── scene-composer-stride.tests/ # Stride integration tests
├── scene-composer-godot/        # Godot engine integration
├── scene-composer-godot.tests/  # Godot integration tests
├── bundle-format/               # Shared .bannou bundle format (internal library)
├── protocol/                    # Shared protocol definitions (internal library)
└── transport/                   # Shared transport code (internal library)

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
| `core/` | `BeyondImmersion.Bannou.Core` | Shared types (BannouJson, ApiException, base events) |
| `client/` | `BeyondImmersion.Bannou.Client` | WebSocket client for game clients |
| `client-voice/` | `BeyondImmersion.Bannou.Client.Voice` | P2P voice chat with SIP/RTP scaling |
| `server/` | `BeyondImmersion.Bannou.Server` | Server SDK with mesh service clients |
| `asset-bundler/` | `BeyondImmersion.Bannou.AssetBundler` | Engine-agnostic asset bundling pipeline |
| `asset-bundler-stride/` | `BeyondImmersion.Bannou.AssetBundler.Stride` | Stride engine asset compilation |
| `asset-loader/` | `BeyondImmersion.Bannou.AssetLoader` | Engine-agnostic asset loading and caching |
| `asset-loader-client/` | `BeyondImmersion.Bannou.AssetLoader.Client` | WebSocket-based asset source for game clients |
| `asset-loader-server/` | `BeyondImmersion.Bannou.AssetLoader.Server` | Mesh-based asset source for game servers |
| `asset-loader-stride/` | `BeyondImmersion.Bannou.AssetLoader.Stride` | Stride engine type loaders |
| `scene-composer/` | `BeyondImmersion.Bannou.SceneComposer` | Engine-agnostic scene editing |
| `scene-composer-stride/` | `BeyondImmersion.Bannou.SceneComposer.Stride` | Stride engine integration |
| `scene-composer-godot/` | `BeyondImmersion.Bannou.SceneComposer.Godot` | Godot engine integration |

### Namespace Pattern

```
BeyondImmersion.Bannou.{Feature}[.{SubFeature}]
```

Examples:
- `BeyondImmersion.Bannou.Core`
- `BeyondImmersion.Bannou.Client`
- `BeyondImmersion.Bannou.Client.Voice`
- `BeyondImmersion.Bannou.AssetBundler`
- `BeyondImmersion.Bannou.AssetBundler.Stride`
- `BeyondImmersion.Bannou.AssetLoader`
- `BeyondImmersion.Bannou.AssetLoader.Client`
- `BeyondImmersion.Bannou.AssetLoader.Server`
- `BeyondImmersion.Bannou.AssetLoader.Stride`
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
3. **Add to SDK solution** using `dotnet sln sdks/bannou-sdks.sln add sdks/{name}/{name}.csproj`
4. **Create test project** in `sdks/{name}.tests/` with matching conventions
5. **Update documentation** in `docs/guides/SDKs.md`

## Building and Testing SDKs

SDKs are built and tested separately from the server using the SDK solution:

```bash
# Build all SDK projects
make build-sdks

# Run all SDK tests
make test-sdks

# Or directly with dotnet
dotnet build sdks/bannou-sdks.sln
dotnet test sdks/bannou-sdks.sln
```

**Note**: The SDK solution (`sdks/bannou-sdks.sln`) is separate from the server solution (`bannou-service.sln`) because:
- SDKs target different .NET versions (including .NET 10.0 for Stride/Godot)
- SDKs are excluded from Docker builds (server-only containers)
- CI workflows build SDKs individually for NuGet packaging

## Engine-Specific SDK Pattern

For engine integrations (Stride, Godot, Unity, Unreal, etc.):

1. **Directory**: `scene-composer-{engine}/`
2. **Package**: `BeyondImmersion.Bannou.SceneComposer.{Engine}`
3. **Namespace**: `BeyondImmersion.Bannou.SceneComposer.{Engine}`

The engine name comes AFTER `SceneComposer` to maintain logical grouping in NuGet search results and IDE autocomplete.

## Shared Internal Libraries

The following directories contain shared code that is compiled as proper projects but NOT published as separate NuGet packages. They are referenced via `<ProjectReference>` from consuming SDKs:

| Directory | PackageId | Description |
|-----------|-----------|-------------|
| `bundle-format/` | `BeyondImmersion.Bannou.Bundle.Format` | .bannou bundle file read/write |
| `protocol/` | `BeyondImmersion.Bannou.Protocol` | Game protocol definitions (MessagePack DTOs, envelope format) |
| `transport/` | `BeyondImmersion.Bannou.Transport` | LiteNetLib transport implementations |

These are proper .NET projects with `.csproj` files, included in `bannou-sdks.sln`, and follow all SDK naming conventions. They have `<IsPackable>false</IsPackable>` to prevent NuGet publishing.

### Why Projects Instead of Compile Include?

1. **Compile-time validation** - Each shared library builds independently, catching errors early
2. **IDE support** - Full IntelliSense, navigation, and refactoring support
3. **NuGet-ready** - Can be published later by setting `<IsPackable>true</IsPackable>`
4. **Clean dependency graph** - Explicit project references instead of hidden file includes
5. **Consistent namespace** - All consumers use the same namespace

### Usage Pattern

```xml
<!-- In consuming SDK .csproj -->
<ItemGroup>
  <ProjectReference Include="../transport/BeyondImmersion.Bannou.Transport.csproj" />
  <ProjectReference Include="../bundle-format/BeyondImmersion.Bannou.Bundle.Format.csproj" />
</ItemGroup>
```

### Server Plugin Usage

Server-side plugins can also reference these shared libraries:

```xml
<!-- In plugins/lib-asset/lib-asset.csproj -->
<ItemGroup>
  <ProjectReference Include="../../sdks/bundle-format/BeyondImmersion.Bannou.Bundle.Format.csproj" />
</ItemGroup>
```

## Version Management

All SDK packages share a single version number defined in `sdks/SDK_VERSION`.

### Unified Versioning Strategy

We use **unified versioning** (also called "lockstep versioning"), where all SDK packages share the same version number and are published together on every release.

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Version source | Single `SDK_VERSION` file | One source of truth |
| Publish scope | All SDKs every release | Simplicity, clear compatibility |
| Version gaps | None (all packages always in sync) | Avoids user confusion |

### Why Full Publish (Not Sparse)

We considered two patterns:

1. **Full publish** (chosen): All SDKs published every release, even if unchanged
2. **Sparse publish**: Only publish SDKs that changed

We chose full publish because:

- **Simplicity**: No complex change detection needed
- **Clear compatibility**: "All 1.5.0 packages work together" is unambiguous
- **Industry norm**: Microsoft publishes 100+ ASP.NET Core packages per release, even unchanged ones
- **NuGet handles it**: Publishing identical code with a new version is fine
- **Transitive dependencies**: When `SceneComposer` changes, `SceneComposer.Stride` and `SceneComposer.Godot` depend on it, so they'd need publishing anyway

The "wasted" NuGet bandwidth from republishing unchanged packages is negligible compared to the cognitive simplicity.

### Release Workflow

Releases are triggered manually via the `ci.sdk-release.yml` workflow:

1. **PR labels drive version bumps**: PRs that affect SDKs must have one of:
   - `sdk:major` - Breaking changes
   - `sdk:minor` - New features (backwards compatible)
   - `sdk:patch` - Bug fixes
   - `sdk:none` - No SDK impact

2. **Automatic version calculation**: The workflow scans merged PRs since the last `sdk-v*` tag and determines the appropriate bump (major > minor > patch)

3. **All packages published**: Every SDK is built, packed, and pushed to NuGet with the new version

4. **Tag created**: A git tag `sdk-v{version}` marks the release point

### What This Means for Consumers

- **Version compatibility is guaranteed**: If you use `BeyondImmersion.Bannou.Client` 1.5.0 with `BeyondImmersion.Bannou.SceneComposer` 1.5.0, they are guaranteed to be compatible
- **No version gaps**: You'll never see `SceneComposer` at 1.5.0 while `SceneComposer.Stride` is at 1.2.0
- **Simple upgrades**: Update all Bannou packages to the same version

### References for This Decision

- [Azure SDK unified versioning](https://azure.github.io/azure-sdk/policies_releases.html)
- [ASP.NET Core shared framework versioning](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/target-aspnetcore)
- Internal discussion: 2025-01-14

## References

- [Azure SDK .NET Design Guidelines](https://azure.github.io/azure-sdk/dotnet_introduction.html)
- [Microsoft Namespace Naming Guidelines](https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces)
- [NuGet Package Authoring Best Practices](https://learn.microsoft.com/en-us/nuget/create-packages/package-authoring-best-practices)
- [GitHub Issue #107](https://github.com/BeyondImmersion/bannou/issues/107) - Original restructuring proposal
