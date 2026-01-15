# Bannou SDKs Overview

This document provides a comprehensive overview of all Bannou SDKs, their purposes, and when to use each one.

## SDK Summary

| Package | Purpose | Target |
|---------|---------|--------|
| `BeyondImmersion.Bannou.Client` | WebSocket client for game clients | Game clients |
| `BeyondImmersion.Bannou.Server` | Server SDK with mesh service clients | Game servers, internal services |
| `BeyondImmersion.Bannou.Client.Voice` | P2P voice chat with SIP/RTP scaling | Game clients with voice |
| `BeyondImmersion.Bannou.AssetBundler` | Engine-agnostic asset bundling pipeline | Asset tooling |
| `BeyondImmersion.Bannou.AssetBundler.Stride` | Stride engine asset compilation | Stride asset pipelines |
| `BeyondImmersion.Bannou.SceneComposer` | Engine-agnostic scene editing | Scene editor tools |
| `BeyondImmersion.Bannou.SceneComposer.Stride` | Stride engine integration | Stride-based editors |
| `BeyondImmersion.Bannou.SceneComposer.Godot` | Godot 4.x engine integration | Godot-based editors |

## Decision Guide

```
Are you building a game client?
├─ Yes → Use BeyondImmersion.Bannou.Client
│        └─ Need voice chat? → Also add BeyondImmersion.Bannou.Client.Voice
│
└─ No, building a game server or internal service
   └─ Use BeyondImmersion.Bannou.Server (includes everything from Client)

Are you building asset bundling tools?
├─ Yes → Start with BeyondImmersion.Bannou.AssetBundler (engine-agnostic core)
│        └─ Compiling for Stride? → Add BeyondImmersion.Bannou.AssetBundler.Stride
│
└─ No → You don't need the AssetBundler packages

Are you building a scene editor?
├─ Yes → Start with BeyondImmersion.Bannou.SceneComposer (engine-agnostic core)
│        └─ Using Stride? → Add BeyondImmersion.Bannou.SceneComposer.Stride
│        └─ Using Godot? → Add BeyondImmersion.Bannou.SceneComposer.Godot
│
└─ No → You don't need the SceneComposer packages
```

---

## Client SDK

**Package**: `BeyondImmersion.Bannou.Client`

Lightweight SDK for game clients connecting to Bannou services via WebSocket.

### Features

- **WebSocket client** with automatic reconnection and binary protocol support
- **Request/response messaging** with typed payloads
- **Event reception** for real-time updates pushed from services
- **Capability manifest** for dynamic API discovery
- **Game transport helpers**: MessagePack DTOs + LiteNetLib client transport for UDP gameplay

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.Client;

var client = new BannouClient();
await client.ConnectWithTokenAsync(connectUrl, accessToken);

// Invoke an API endpoint
var response = await client.InvokeAsync<MyRequest, MyResponse>(
    "POST",
    "/character/get",
    new MyRequest { CharacterId = "abc123" },
    timeout: TimeSpan.FromSeconds(5));

// Handle events
client.OnEvent += (sender, eventData) =>
{
    Console.WriteLine($"Event: {eventData.EventName}");
};
```

### Game Transport (UDP)

```csharp
using BeyondImmersion.Bannou.Protocol;
using BeyondImmersion.Bannou.Transport;

var transport = new LiteNetLibClientTransport();
await transport.ConnectAsync("127.0.0.1", 9000, GameProtocolEnvelope.CurrentVersion);

transport.OnServerMessage += (ver, type, payload) =>
{
    if (type == GameMessageType.ArenaStateSnapshot)
    {
        var snap = MessagePackSerializer.Deserialize<ArenaStateSnapshot>(payload);
    }
};

// Send input
var input = new PlayerInputMessage { Tick = 1, MoveX = 1, MoveY = 0 };
var bytes = MessagePackSerializer.Serialize(input);
await transport.SendAsync(GameMessageType.PlayerInput, bytes, reliable: true);
```

---

## Server SDK

**Package**: `BeyondImmersion.Bannou.Server`

Server SDK for game servers and internal services communicating with Bannou.

### Features

- **Generated service clients** for type-safe API calls (`IAuthClient`, `IAccountClient`, etc.)
- **Mesh service routing** for dynamic service-to-service communication
- **Event subscription** for real-time updates via pub/sub
- **All models and events** from Client SDK plus server-specific infrastructure
- **Includes BannouClient** for WebSocket communication (you don't need Client separately)

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.Server
```

### Quick Start

```csharp
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Character;

// Set up DI
var services = new ServiceCollection();
services.AddBannouServiceClients();
services.AddScoped<IAuthClient, AuthClient>();
services.AddScoped<ICharacterClient, CharacterClient>();

var provider = services.BuildServiceProvider();

// Use service clients
var authClient = provider.GetRequiredService<IAuthClient>();
var response = await authClient.ValidateTokenAsync(new ValidateTokenRequest
{
    Token = "eyJhbG..."
});
```

### Internal Mode (Server-to-Server)

```csharp
var client = new BannouClient();
// Connect to internal Connect node without JWT
await client.ConnectInternalAsync("ws://bannou-internal/connect", serviceToken: "shared-secret");
```

---

## Voice SDK

**Package**: `BeyondImmersion.Bannou.Client.Voice`

P2P voice chat SDK with automatic tier transition to SIP/RTP for large rooms.

### Features

- **VoiceRoomManager**: High-level manager for multi-peer voice rooms
- **P2P Mode**: WebRTC for 1-5 participants (direct peer-to-peer)
- **Scaled Mode**: SIP/RTP for 6+ participants (via Kamailio+RTPEngine)
- **Automatic tier transition**: No code changes needed when room grows
- **Swappable implementations**: Use SIPSorcery (default) or bring your own WebRTC

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client.Voice
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.Client.Voice;

// Create voice manager - auto-subscribes to voice events
var voiceManager = new VoiceRoomManager(bannouClient);

// Handle incoming audio (works for both P2P and Scaled modes)
voiceManager.OnAudioReceived += (peerId, samples, rate, channels) =>
{
    PlayAudio(peerId, samples, rate, channels);
};

// Send microphone audio (works transparently in both modes)
if (voiceManager.IsInRoom && !voiceManager.IsMuted)
{
    var samples = CaptureMicrophoneAudio();
    voiceManager.SendAudioToAllPeers(samples, 48000, 1);
}
```

### P2P vs Scaled Mode

| Mode | Participants | Audio Path | Bandwidth |
|------|--------------|------------|-----------|
| P2P | 1-5 | Direct WebRTC | N*(N-1)/2 connections |
| Scaled | 6+ | Via SIP/RTP server | 1 connection per client |

The transition is automatic - your code doesn't need to change.

---

## AssetBundler SDK (Core)

**Package**: `BeyondImmersion.Bannou.AssetBundler`

Engine-agnostic asset bundling SDK for creating `.bannou` bundle files. Use this as the foundation, then add an engine-specific package for asset compilation.

### Features

- **Asset sources**: Extract assets from directories or ZIP archives
- **Type inference**: Automatic asset type detection (models, textures, audio, etc.)
- **Processing pipeline**: Pluggable asset processors for engine-specific compilation
- **State management**: Incremental builds with content hash tracking
- **Bundle creation**: Write `.bannou` bundles with LZ4 compression
- **Upload integration**: Upload bundles to Bannou Asset Service
- **Metabundle requests**: Request server-side metabundle creation

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetBundler
```

### Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    BundlerPipeline                       │
│  Source → Extract → Process → Bundle → Upload            │
└─────────────────────────────────────────────────────────┘
         │              │
         v              v
┌─────────────┐  ┌──────────────────┐
│ IAssetSource│  │ IAssetProcessor  │
│ - Directory │  │ - Raw (passthru) │
│ - ZIP       │  │ - Stride         │
└─────────────┘  │ - (Godot, etc.)  │
                 └──────────────────┘
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.AssetBundler.Sources;
using BeyondImmersion.Bannou.AssetBundler.Processing;
using BeyondImmersion.Bannou.AssetBundler.Pipeline;

// Create a source from a directory
var source = new DirectoryAssetSource(
    new DirectoryInfo("/path/to/assets"),
    sourceId: "my-assets",
    name: "My Asset Pack",
    version: "1.0.0");

// Create pipeline
var pipeline = new BundlerPipeline();
var state = new BundlerStateManager(new DirectoryInfo("/path/to/state"));

var options = new BundlerOptions
{
    WorkingDirectory = "/tmp/bundler-work",
    OutputDirectory = "/output/bundles"
};

// Execute (uses RawAssetProcessor by default for pass-through)
var result = await pipeline.ExecuteAsync(source, null, state, null, options);
Console.WriteLine($"Created bundle: {result.BundlePath}");
```

---

## Stride AssetBundler

**Package**: `BeyondImmersion.Bannou.AssetBundler.Stride`

Stride engine asset compilation for the AssetBundler SDK. Compiles FBX models and textures through Stride's asset pipeline.

### Features

- **StrideBatchCompiler**: Full `IAssetProcessor` implementation
- **Project generation**: Creates temporary Stride projects for batch compilation
- **Index parsing**: Extracts compiled assets from Stride's output
- **Texture compression**: BC1/BC3/BC7/ETC2/ASTC options
- **Dependency collection**: Handles buffer files for models

### Requirements

- .NET 8.0+
- Stride 4.3.x (invoked via `dotnet build`, no NuGet dependency)

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetBundler.Stride
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.AssetBundler.Stride.Compilation;

// Create Stride compiler
var compilerOptions = new StrideCompilerOptions
{
    StrideVersion = "4.3.0.2507",
    TextureCompression = StrideTextureCompression.BC7,
    GenerateMipmaps = true
};

var compiler = new StrideBatchCompiler(compilerOptions);

// Use with pipeline
var result = await pipeline.ExecuteAsync(source, compiler, state, null, options);
```

### Asset Type Mapping

| Source Extension | Stride Output | Content Type |
|-----------------|---------------|--------------|
| `.fbx`, `.obj`, `.gltf` | Model | `application/x-stride-model` |
| `.png`, `.jpg`, `.tga` | Texture | `application/x-stride-texture` |
| `.anim` | Animation | `application/x-stride-animation` |

---

## SceneComposer SDK (Core)

**Package**: `BeyondImmersion.Bannou.SceneComposer`

Engine-agnostic scene composition SDK. Use this as the foundation, then add an engine-specific package.

### Features

- **Hierarchical scene graph** with node creation, deletion, and reparenting
- **Command-based undo/redo** with compound operations
- **Multi-selection** with add/remove/toggle modes
- **Transform operations** in local or world coordinate space
- **Validation system** for scene and node integrity
- **Engine bridge pattern** for integration with any game engine

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.SceneComposer
```

### Architecture

```
                    ISceneComposerBridge
                           |
                           v
+-----------------+   +-----------+   +----------------+
| SceneComposer   |-->| Commands  |-->| Engine Bridge  |
|  - Selection    |   | - Create  |   | (Stride/Godot) |
|  - CommandStack |   | - Delete  |   +----------------+
|  - Validation   |   | - Move    |
+-----------------+   +-----------+
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.SceneComposer;

// Create with your engine bridge
ISceneComposerBridge bridge = new YourEngineBridge();
var composer = new SceneComposer(bridge);

// Create nodes
var player = composer.CreateNode(NodeType.Actor, "Player");
var weapon = composer.CreateNode(NodeType.Model, "Sword", parent: player);

// Transform
composer.TranslateNode(player, new Vector3(10, 0, 5), CoordinateSpace.World);

// Undo/Redo
composer.Undo();
composer.Redo();
```

---

## Stride SceneComposer

**Package**: `BeyondImmersion.Bannou.SceneComposer.Stride`

Stride engine integration for the SceneComposer SDK.

### Features

- **StrideSceneComposerBridge**: Full `ISceneComposerBridge` implementation
- **Bundle asset loading**: Multi-tier system for `.bannou` bundle files
- **LRU asset cache**: Size-based eviction for loaded assets
- **Gizmo renderer**: Transform gizmo visualization
- **Physics picking**: Ray-based entity selection

### Requirements

- .NET 10.0 with Windows TFM (`net10.0-windows`)
- Stride 4.3.x
- Windows (full runtime functionality)

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.SceneComposer.Stride
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.SceneComposer.Stride;
using BeyondImmersion.Bannou.SceneComposer.Stride.Content;

// Set up content manager
var contentManager = new StrideContentManager(GraphicsDevice, maxCacheSize: 512 * 1024 * 1024);

// Create the bridge
var bridge = new StrideSceneComposerBridge(Entity.Scene, GraphicsDevice, contentManager);

// Create the composer
var composer = new SceneComposer(bridge);

// Load assets from bundles
contentManager.RegisterBundle("characters", "/path/to/characters.bannou");
var model = await contentManager.LoadModelAsync("characters", "warrior");
```

---

## Godot SceneComposer

**Package**: `BeyondImmersion.Bannou.SceneComposer.Godot`

Godot 4.x engine integration for the SceneComposer SDK.

### Features

- **GodotSceneComposerBridge**: Full `ISceneComposerBridge` implementation
- **Native asset loading**: Uses Godot's `ResourceLoader` with `res://` paths
- **Procedural gizmos**: ArrayMesh-based transform gizmo
- **Physics picking**: Ray-based selection with AABB fallback
- **Cross-platform**: Works on Windows, Linux, macOS

### Requirements

- Godot 4.3+ with .NET support
- .NET 8.0

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.SceneComposer.Godot
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.SceneComposer.Godot;

public partial class SceneEditor : Node3D
{
    private GodotSceneComposerBridge _bridge;
    private SceneComposer _composer;

    public override void _Ready()
    {
        var sceneRoot = GetNode<Node3D>("SceneRoot");
        var camera = GetNode<Camera3D>("Camera3D");

        _bridge = new GodotSceneComposerBridge(sceneRoot, camera, GetViewport());
        _composer = new SceneComposer(_bridge);
    }
}
```

### NodeType Mapping

| SDK NodeType | Godot Node |
|--------------|------------|
| Group | Node3D |
| Mesh | Node3D + MeshInstance3D |
| Marker | Marker3D |
| Volume | Area3D + CollisionShape3D |
| Emitter | GPUParticles3D |

---

## Common Patterns

### WebSocket Connection Modes

**External Mode** (game clients with JWT):
```csharp
await client.ConnectWithTokenAsync(connectUrl, accessToken);
```

**Internal Mode** (server-to-server):
```csharp
await client.ConnectInternalAsync(connectUrl, serviceToken: "shared-secret");
```

### Game Transport (UDP)

Both Client SDK and Server SDK include game transport helpers:

- **Envelope**: `GameProtocolEnvelope` (version + `GameMessageType` byte + MessagePack payload)
- **DTOs**: Snapshots/deltas, combat events, opportunities, input
- **Transports**: `LiteNetLibServerTransport`, `LiteNetLibClientTransport`
- **Fuzz testing**: `TransportFuzzOptions` for drop/delay simulation

### Fuzz Testing

```csharp
transport.FuzzOptions.DropProbability = 0.1;   // 10% packet loss
transport.FuzzOptions.DelayProbability = 0.2; // 20% delayed
transport.FuzzOptions.MaxDelayMs = 50;         // Up to 50ms delay
```

---

## Version Compatibility

All SDKs share the same version number and are released together. The version is stored in `sdks/SDK_VERSION`.

**Current version**: Check the `sdks/SDK_VERSION` file or [NuGet packages](https://www.nuget.org/packages?q=BeyondImmersion.Bannou).

---

## For SDK Developers

If you're **developing or extending** the Bannou SDKs themselves (not just consuming them), see:

- [SDK Conventions](../../sdks/CONVENTIONS.md) - **Definitive guide** for SDK naming, structure, versioning, and development patterns

---

## Further Reading

- [Client SDK README](../../sdks/client/README.md) - Full client SDK documentation
- [Server SDK README](../../sdks/server/README.md) - Full server SDK documentation
- [Voice SDK README](../../sdks/client-voice/README.md) - Voice SDK documentation
- [AssetBundler README](../../sdks/asset-bundler/README.md) - Core AssetBundler docs
- [Stride AssetBundler README](../../sdks/asset-bundler-stride/README.md) - Stride compilation
- [SceneComposer README](../../sdks/scene-composer/README.md) - Core SceneComposer docs
- [Stride SceneComposer README](../../sdks/scene-composer-stride/README.md) - Stride integration
- [Godot SceneComposer README](../../sdks/scene-composer-godot/README.md) - Godot integration
- [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) - Binary protocol details
