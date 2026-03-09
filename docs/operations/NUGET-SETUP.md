# NuGet Package Setup for Bannou SDKs

> **Last Updated**: 2026-03-08
> **Scope**: NuGet package publishing configuration, SDK architecture, GitHub Actions CI workflows, and version management for all Bannou SDK packages

## Summary

NuGet publishing setup for the Bannou SDK ecosystem covering package architecture, GitHub environment secrets, version management via SDK_VERSION file and PR labels, and CI/CD workflows for preview and stable releases. Required reading when configuring NuGet API keys, adding new SDK packages to the publish pipeline, or understanding how SDK versioning works across the three GitHub Actions workflows (auto-preview, manual preview, stable release).

## SDK Package Architecture

Bannou publishes **multiple SDK packages** to NuGet and npm for different use cases. For the complete package registry and naming conventions, see [SDK Conventions](../../sdks/CONVENTIONS.md).

### Package Overview

| Package | Project | Purpose |
|---------|---------|---------|
| **Foundation (internal dependencies)** | | |
| `BeyondImmersion.Bannou.Core` | `sdks/core` | Shared types and abstractions |
| `BeyondImmersion.Bannou.Protocol` | `sdks/protocol` | WebSocket binary protocol (31-byte header) |
| `BeyondImmersion.Bannou.Transport` | `sdks/transport` | WebSocket transport layer |
| `BeyondImmersion.Bannou.BundleFormat` | `sdks/bundle-format` | `.bannou` archive format with LZ4 compression |
| **Public SDK packages** | | |
| `BeyondImmersion.Bannou.Server` | `sdks/server` | Server SDK with mesh service clients |
| `BeyondImmersion.Bannou.Client` | `sdks/client` | WebSocket client for game clients |
| `BeyondImmersion.Bannou.Client.Voice` | `sdks/client-voice` | P2P voice chat with SIP/RTP scaling |
| `BeyondImmersion.Bannou.SceneComposer` | `sdks/scene-composer` | Engine-agnostic scene editing |
| `BeyondImmersion.Bannou.SceneComposer.Stride` | `sdks/scene-composer-stride` | Stride engine scene integration |
| `BeyondImmersion.Bannou.SceneComposer.Godot` | `sdks/scene-composer-godot` | Godot engine scene integration |
| `BeyondImmersion.Bannou.AssetBundler` | `sdks/asset-bundler` | Engine-agnostic asset bundling pipeline |
| `BeyondImmersion.Bannou.AssetBundler.Stride` | `sdks/asset-bundler-stride` | Stride engine asset compilation |
| `BeyondImmersion.Bannou.AssetBundler.Godot` | `sdks/asset-bundler-godot` | Godot engine asset processing |
| `BeyondImmersion.Bannou.AssetLoader` | `sdks/asset-loader` | Engine-agnostic asset loading |
| `BeyondImmersion.Bannou.AssetLoader.Client` | `sdks/asset-loader-client` | Client-side asset loading |
| `BeyondImmersion.Bannou.AssetLoader.Server` | `sdks/asset-loader-server` | Server-side asset loading |
| `BeyondImmersion.Bannou.AssetLoader.Stride` | `sdks/asset-loader-stride` | Stride engine asset loading |
| `BeyondImmersion.Bannou.AssetLoader.Godot` | `sdks/asset-loader-godot` | Godot engine asset loading |
| **Music generation SDKs** | | |
| `BeyondImmersion.Bannou.MusicTheory` | `sdks/music-theory` | Music theory primitives (harmony, melody, MIDI) |
| `BeyondImmersion.Bannou.MusicStoryteller` | `sdks/music-storyteller` | Narrative-driven composition |
| **TypeScript SDKs** (published to npm) | | |
| `@beyondimmersion/bannou-core` | `sdks/typescript/core` | TypeScript shared types |
| `@beyondimmersion/bannou-client` | `sdks/typescript/client` | TypeScript WebSocket client |

### Core Packages

### **Server SDK** (`BeyondImmersion.Bannou.Server`)
For **game servers** that need service-to-service calls AND WebSocket support.

**Includes:**
- Service Clients (`AccountClient`, `AuthClient`, etc.) for mesh invocation
- Request/Response Models from all services
- Event Models for pub/sub messaging
- WebSocket Binary Protocol (31-byte header)
- `BannouClient.cs` for WebSocket connections
- Infrastructure abstractions (lib-mesh, lib-messaging, lib-state)

**Dependencies:** `System.Net.WebSockets.Client`, infrastructure lib references

### **Client SDK** (`BeyondImmersion.Bannou.Client`)
For **game clients** that ONLY communicate via WebSocket (no server infrastructure).

**Includes:**
- Request/Response Models from all services
- Event Models for pub/sub messaging
- WebSocket Binary Protocol (31-byte header)
- `BannouClient.cs` for WebSocket connections

**Dependencies:** `System.Net.WebSockets.Client` (no server infrastructure)

**What's NOT Included:**
- Service Clients (use `BeyondImmersion.Bannou.Server` if you need these)
- Server infrastructure libs

## Package Configuration

### **API Key Scope Pattern**
When creating your NuGet API key, use this glob pattern for security:
```
BeyondImmersion.Bannou.*
```

This allows publishing both SDK packages while preventing accidental publishing to other namespaces.

## GitHub Secrets Setup

### Repository Secrets
The SDK publishing workflows use **repository-level secrets** (not environment-scoped):

**Secret Name**: `NUGET_API_KEY`
**Secret Value**: `[Your NuGet API Key from nuget.org]`
**Used by**: `ci.sdk-preview-auto.yml`, `ci.sdk-preview.yml`, `ci.sdk-release.yml`

**Secret Name**: `DISCORD_WEBHOOK`
**Secret Value**: `[Discord webhook URL for build notifications]`
**Used by**: All SDK publishing workflows

**Secret Name**: `NPM_TOKEN` (if publishing TypeScript SDKs)
**Secret Value**: `[npm auth token for @beyondimmersion scope]`
**Used by**: `ci.sdk-release.yml`

**Branch protection**: Only merges to `master` trigger auto-preview publishing. Stable releases require manual workflow dispatch.

### Steps to get API Key:
1. Go to https://www.nuget.org/account/apikeys
2. Click "Create"
3. **Key Name**: `Bannou-SDK-CI`
4. **Glob Pattern**: `BeyondImmersion.Bannou.*`
5. **Select Scopes**: Push new packages and package versions
6. Copy the generated key to GitHub Secrets

## Package Metadata

### Server SDK
```xml
<PackageId>BeyondImmersion.Bannou.Server</PackageId>
<Authors>BeyondImmersion</Authors>
<Description>Server SDK for Bannou service platform with service clients, models, events, and WebSocket protocol support. Use this for game servers and internal services.</Description>
<PackageTags>bannou;microservices;websocket;server;sdk;service-client</PackageTags>
```

### Client SDK
```xml
<PackageId>BeyondImmersion.Bannou.Client</PackageId>
<Authors>BeyondImmersion</Authors>
<Description>Client SDK for Bannou service platform with models, events, and WebSocket protocol support. For game clients - lightweight with no server infrastructure.</Description>
<PackageTags>bannou;microservices;websocket;client;sdk;game-client</PackageTags>
```

## Version Management

- **Base Version**: Stored in `sdks/SDK_VERSION` file (currently `2.0.0`), representing the last stable release
- **Preview Versions**: Auto-generated with patch bump + `-preview.N` suffix (e.g., SDK_VERSION `2.0.0` produces `2.0.1-preview.42`). The patch bump ensures previews sort higher than the last stable release.
- **Stable Versions**: Calculated by `ci.sdk-release.yml` using PR labels since the last `sdk-v*` git tag:
  - `sdk:major` label on any merged PR triggers major bump
  - `sdk:minor` label triggers minor bump
  - `sdk:patch` label (or no label) triggers patch bump
  - Biggest bump wins when multiple labels exist
- **All SDK packages share the same version number** for consistency (NuGet and npm)
- **Git tags**: Stable releases create `sdk-v{VERSION}` tags (e.g., `sdk-v2.1.0`)

### Backwards Compatibility Testing
- Testing uses `Version="*"` to match **stable versions only**
- Preview versions are NOT used for backwards compatibility testing
- Backwards compat testing activates after first stable release

## Publishing Workflows

Three GitHub Actions workflows handle SDK publishing:

| Workflow | File | Trigger | Publishes |
|----------|------|---------|-----------|
| **Auto-Preview** | `ci.sdk-preview-auto.yml` | Push to `master` | Preview packages to NuGet |
| **Manual Preview** | `ci.sdk-preview.yml` | Manual dispatch | Preview packages to NuGet (bypass full CI) |
| **Stable Release** | `ci.sdk-release.yml` | Manual dispatch | Stable packages to NuGet + npm |

### Auto-Preview Flow (on every merge to master)
```
Push to master
  → Restore + Build (Release config)
  → Read SDK_VERSION, compute preview version
  → Pack all SDK projects
  → Publish to NuGet.org (--skip-duplicate)
  → Notify Discord
```

### Stable Release Flow (manual trigger)
```
Manual dispatch (with optional dry-run)
  → Read SDK_VERSION as base
  → Analyze PR labels since last sdk-v* tag
  → Calculate version bump (major/minor/patch)
  → Build + Pack all SDKs
  → Create sdk-v{version} git tag
  → Publish to NuGet.org
  → Update TypeScript SDK versions + build + publish to npm
  → Update SDK_VERSION file
  → Create GitHub Release with changelog
  → Notify Discord
```

**Note**: The `ci.integration.yml` pipeline runs tests independently via branch protection. SDK publishing workflows do not re-run the full test suite -- they rely on tests having already passed before merge.

## Installation for Consumers

### For Game Servers (with service mesh)
```bash
dotnet add package BeyondImmersion.Bannou.Server
```

### For Game Clients (WebSocket only)
```bash
dotnet add package BeyondImmersion.Bannou.Client
```

### PackageReference Format
```xml
<!-- Server SDK -->
<PackageReference Include="BeyondImmersion.Bannou.Server" Version="*" />

<!-- Client SDK -->
<PackageReference Include="BeyondImmersion.Bannou.Client" Version="*" />
```

## Security Considerations

- **Branch Protection**: Only `master` branch triggers auto-preview publishing; stable releases require manual dispatch
- **Scoped API Key**: Limited to `BeyondImmersion.Bannou.*` namespace via NuGet glob pattern
- **Repository Secrets**: API keys stored as repository-level secrets in GitHub
- **MIT License**: Compatible with open source distribution
- **Automated Only**: Auto-preview on merge reduces manual publishing errors
- **Backwards Compatibility**: Automated testing with published SDK versions (stable only)

## Testing the Setup

To test your NuGet configuration locally:

```bash
# Generate SDKs
scripts/generate-client-sdk.sh

# Build and pack individual SDK projects
dotnet pack sdks/server --configuration Release -p:PackageVersion=1.0.0-test --output ./test-packages
dotnet pack sdks/client --configuration Release -p:PackageVersion=1.0.0-test --output ./test-packages

# Verify packages were created
ls -la ./test-packages/*.nupkg

# Publish with --skip-duplicate (no dry-run flag exists for dotnet nuget push)
# Use a test/preview version to avoid polluting stable releases
dotnet nuget push ./test-packages/*.nupkg --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## Package Content Comparison

| Content | Server SDK | Client SDK |
|---------|------------|------------|
| Service Clients (`*Client.cs`) | Yes | **No** |
| Request/Response Models | Yes | Yes |
| Event Models | Yes | Yes |
| WebSocket Binary Protocol | Yes | Yes |
| `BannouClient.cs` | Yes | Yes |
| Infrastructure Libs | Yes | **No** |
| Multi-target (.NET 8, 9) | Yes | Yes |
| XML Documentation | Yes | Yes |
