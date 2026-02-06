# Bannou SDKs Overview

This document provides a comprehensive overview of all Bannou SDKs, their purposes, and when to use each one.

## SDK Summary

### .NET SDKs

| Package | Purpose | Target |
|---------|---------|--------|
| `BeyondImmersion.Bannou.Core` | Shared types (BannouJson, ApiException, base events) | All SDKs, server plugins |
| `BeyondImmersion.Bannou.Client` | WebSocket client for game clients | Game clients |
| `BeyondImmersion.Bannou.Server` | Server SDK with mesh service clients | Game servers, internal services |
| `BeyondImmersion.Bannou.Client.Voice` | P2P voice chat with SIP/RTP scaling | Game clients with voice |
| `BeyondImmersion.Bannou.AssetBundler` | Engine-agnostic asset bundling pipeline | Asset tooling |
| `BeyondImmersion.Bannou.AssetBundler.Stride` | Stride engine asset compilation | Stride asset pipelines |
| `BeyondImmersion.Bannou.SceneComposer` | Engine-agnostic scene editing | Scene editor tools |
| `BeyondImmersion.Bannou.SceneComposer.Stride` | Stride engine integration | Stride-based editors |
| `BeyondImmersion.Bannou.SceneComposer.Godot` | Godot 4.x engine integration | Godot-based editors |
| `BeyondImmersion.Bannou.MusicTheory` | Core music theory primitives | Music generation, theory calculations |
| `BeyondImmersion.Bannou.MusicStoryteller` | Narrative-driven composition | Emotional music generation |

### TypeScript SDK

| Package | Purpose | Target |
|---------|---------|--------|
| `@beyondimmersion/bannou-core` | Shared types (ApiResponse, BannouJson, base events) | All TypeScript SDKs |
| `@beyondimmersion/bannou-client` | WebSocket client with typed proxies | Browser and Node.js clients |

### Unreal Engine Helpers

| Artifact | Purpose | Target |
|----------|---------|--------|
| `BannouProtocol.h` | Binary protocol constants, flags, response codes | UE4/UE5 WebSocket integration |
| `BannouTypes.h` | All request/response USTRUCT definitions (814 types) | UE4/UE5 JSON serialization |
| `BannouEnums.h` | All UENUM definitions (134 enums) | UE4/UE5 type safety |
| `BannouEndpoints.h` | Endpoint constants and registry (309 endpoints) | UE4/UE5 API discovery |
| `BannouEvents.h` | Event name constants (35 event types) | UE4/UE5 event handling |

## Decision Guide

```
What platform are you targeting?
├─ .NET (C#, Unity, Stride, Godot .NET)
│  └─ See .NET SDK decision tree below
│
├─ Browser or Node.js (JavaScript/TypeScript)
│  └─ Use @beyondimmersion/bannou-client
│     └─ For Node.js, also install: npm install ws
│
└─ Unreal Engine (C++)
   └─ Use generated helper headers in sdks/unreal/Generated/
      └─ See UNREAL-INTEGRATION.md for integration guide

.NET SDK Decision Tree:

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

Are you building music-aware features?
├─ Yes, need emotional storytelling and narrative arcs
│  └─ Use BeyondImmersion.Bannou.MusicStoryteller (includes MusicTheory)
│
├─ Yes, but just need theory primitives (pitches, chords, scales)
│  └─ Use BeyondImmersion.Bannou.MusicTheory only
│
└─ No → You don't need the Music packages
```

---

## Core SDK

**Package**: `BeyondImmersion.Bannou.Core`

Foundation library providing shared types used across all Bannou SDKs and server plugins.

### Features

- **BannouJson**: Centralized JSON serialization with consistent settings (case-insensitive properties, enum string serialization, null value omission)
- **ApiException**: Typed exception classes for API error handling with status codes, headers, and response bodies
- **Base Event Types**: `BaseClientEvent` and `BaseServiceEvent` for event-driven communication
- **IBannouEvent**: Common interface for all Bannou events

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.Core
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.Core;

// JSON serialization with Bannou conventions
var json = BannouJson.Serialize(myObject);
var obj = BannouJson.Deserialize<MyType>(json);

// Extension methods
var json = myObject.ToJson();
var obj = jsonString.FromJson<MyType>();

// Apply Bannou settings to existing options
var options = new JsonSerializerOptions();
BannouJson.ApplyBannouSettings(options);
```

### JSON Serialization Settings

BannouJson provides these consistent settings:
- **Case-insensitive properties**: Accepts `firstName`, `FirstName`, or `FIRSTNAME`
- **Enum string serialization**: Enums serialize as strings, not integers
- **Null value omission**: Null properties are omitted from JSON output

### When to Use

The Core SDK is automatically included as a dependency of Client and Server SDKs. You typically don't need to reference it directly unless you're:
- Building a custom SDK that needs Bannou's shared types
- Working on server plugins that need BannouJson or ApiException
- Creating custom event types that extend BaseClientEvent or BaseServiceEvent

---

## Client SDK

**Package**: `BeyondImmersion.Bannou.Client`

Lightweight SDK for game clients connecting to Bannou services via WebSocket.

### Features

- **WebSocket client** with automatic reconnection and binary protocol support
- **Request/response messaging** with typed payloads
- **Event reception** for real-time updates pushed from services
- **Capability manifest** for dynamic API discovery
- **Typed service proxies** for compile-time safe API calls (`client.Auth.LoginAsync()`)
- **Typed event subscriptions** with disposable handlers (`client.OnEvent<TEvent>(...)`)
- **Service-grouped events** for IntelliSense discoverability (`client.Events.GameSession.OnChatMessageReceived()`)
- **ClientEventRegistry** for bidirectional event type ↔ name mapping
- **ClientEndpointMetadata** for runtime type discovery (`(method, path) → (RequestType, ResponseType)`)
- **IBannouClient interface** for mocking in tests
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

// Use typed service proxies (recommended)
var response = await client.Auth.LoginAsync(new LoginRequest
{
    Email = "player@example.com",
    Password = "password"
});

var character = await client.Character.GetAsync(new CharacterGetRequest
{
    CharacterId = "abc123"
});

// Handle typed events with disposable subscriptions
using var subscription = client.OnEvent<ChatMessageReceivedEvent>(evt =>
{
    Console.WriteLine($"Chat: {evt.Message}");
});

// Or use service-grouped events for IntelliSense discoverability
using var chatSub = client.Events.GameSession.OnChatMessageReceived(evt =>
{
    Console.WriteLine($"[{evt.SenderId}]: {evt.Message}");
});

using var voiceSub = client.Events.Voice.OnVoicePeerJoined(evt =>
{
    Console.WriteLine($"Peer joined: {evt.PeerId}");
});

// Or use generic event handler for all events
client.OnEvent += (sender, eventData) =>
{
    Console.WriteLine($"Event: {eventData.EventName}");
};
```

### Runtime Type Discovery

```csharp
using BeyondImmersion.Bannou.Client.Events;

// Get request/response types at runtime
var requestType = ClientEndpointMetadata.GetRequestType("POST", "/auth/login");
// Returns: typeof(LoginRequest)

var info = ClientEndpointMetadata.GetEndpointInfo("POST", "/character/get");
// Returns: { Method, Path, Service, RequestType, ResponseType, Summary }

// Filter endpoints by service
var authEndpoints = ClientEndpointMetadata.GetEndpointsByService("Auth");

// Event type ↔ name mapping
string? name = ClientEventRegistry.GetEventName<ChatMessageReceivedEvent>();
// Returns: "game_session.chat_received"

Type? type = ClientEventRegistry.GetEventType("voice.peer_joined");
// Returns: typeof(VoicePeerJoinedEvent)
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

## Godot AssetBundler

**Package**: `BeyondImmersion.Bannou.AssetBundler.Godot`

Godot 4.x engine asset processing for the AssetBundler SDK. Processes assets for Godot's runtime loading capabilities (GLB models, PNG/JPG/WebP textures, WAV/OGG/MP3 audio).

### Features

- **GodotAssetProcessor**: Full `IAssetProcessor` implementation for Godot-compatible formats
- **Pass-through processing**: Godot-compatible formats pass through unchanged
- **Format conversion**: Converts FBX→glTF, TGA/DDS/BMP→PNG, FLAC→OGG (extensible)
- **Type inference**: `GodotTypeInferencer` determines asset types from filenames
- **Content types**: MIME type helpers for Godot runtime loading

### Requirements

- .NET 9.0+
- Optional: External converters for format conversion (FBX2glTF, ImageMagick, FFmpeg)

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.AssetBundler.Godot
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.AssetBundler.Godot;
using BeyondImmersion.Bannou.AssetBundler.Godot.Processing;

// Create Godot processor
var processorOptions = new GodotProcessorOptions
{
    EnableConversion = true,
    SkipUnconvertible = true,
    MaxTextureSize = 4096,
    OptimizePng = false
};

var processor = new GodotAssetProcessor(processorOptions);

// Use with pipeline
var result = await pipeline.ExecuteAsync(source, processor, state, null, options);
```

### Type Inference

The `GodotTypeInferencer` determines asset types from filenames:

| Pattern | Detected Type |
|---------|---------------|
| `*.glb`, `*.gltf`, `*.fbx`, `*.obj` | Model |
| `*.png`, `*.jpg`, `*.webp`, `*.tga` | Texture |
| `*.wav`, `*.ogg`, `*.mp3`, `*.flac` | Audio |
| `*_normal.png`, `*_n.png` | Normal Map |
| `*_emissive.png`, `*_e.png` | Emissive |
| `spr_*.png`, `ui_*.png` | UI Texture |

### Content Type Mapping

| Source Extension | Godot Output | Content Type |
|-----------------|--------------|--------------|
| `.glb` | glTF Binary | `model/gltf-binary` |
| `.gltf` | glTF JSON | `model/gltf+json` |
| `.png` | PNG | `image/png` |
| `.jpg`, `.jpeg` | JPEG | `image/jpeg` |
| `.webp` | WebP | `image/webp` |
| `.wav` | WAV | `audio/wav` |
| `.ogg` | OGG Vorbis | `audio/ogg` |
| `.mp3` | MP3 | `audio/mpeg` |

### Processing Options

| Option | Default | Description |
|--------|---------|-------------|
| `EnableConversion` | `true` | Enable format conversion |
| `SkipUnconvertible` | `true` | Skip assets that can't be converted |
| `FbxConverterPath` | `null` | Path to FBX2glTF converter |
| `MaxTextureSize` | `4096` | Maximum texture dimension (0 = unlimited) |
| `OptimizePng` | `false` | Apply PNG optimization |
| `JpegQuality` | `90` | JPEG compression quality (1-100) |
| `GenerateOrmTextures` | `false` | Generate ORM packed textures |
| `TrackOriginalFormat` | `true` | Track original format in metadata |

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

## MusicTheory SDK

**Package**: `BeyondImmersion.Bannou.MusicTheory`

Core music theory primitives for pitch, harmony, melody, and rhythm operations.

### Features

- **Pitch primitives**: `PitchClass` (0-11), `Pitch` (with octave), `Interval`
- **Scales and modes**: Major, minor, modal scales with degree operations
- **Chords and voicings**: Chord construction, voice leading algorithms
- **Rhythm patterns**: Duration, meter, tempo, rhythmic patterns
- **Style definitions**: Genre-specific configurations (Celtic, Baroque, Jazz)
- **MIDI-JSON output**: Portable JSON format for generated music

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.MusicTheory
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.MusicTheory.Pitch;
using BeyondImmersion.Bannou.MusicTheory.Collections;
using BeyondImmersion.Bannou.MusicTheory.Harmony;
using BeyondImmersion.Bannou.MusicTheory.Output;

// Create a scale
var gMajor = new Scale(PitchClass.G, ModeType.Major);

// Build a chord progression
var chords = new[] { "I", "IV", "V", "I" }
    .Select(roman => Chord.Parse(roman, gMajor))
    .ToList();

// Voice the chords with smooth voice leading
var voicings = VoiceLeading.VoiceProgression(chords);

// Render to MIDI-JSON
var midiJson = MidiJsonRenderer.RenderChords(voicings, ticksPerBeat: 480);
Console.WriteLine(midiJson.ToJson(indented: true));
```

### When to Use

Use MusicTheory SDK when you need:
- Low-level music theory calculations
- Custom melody or harmony generation
- Voice leading algorithms
- MIDI-JSON output for playback

For high-level emotional composition with narrative arcs, use MusicStoryteller instead.

---

## MusicStoryteller SDK

**Package**: `BeyondImmersion.Bannou.MusicStoryteller`

Narrative-driven music composition using emotional states and GOAP planning. Built on music cognition research from Lerdahl, Huron, and Juslin.

### Features

- **Emotional state model**: 6-dimensional space (tension, brightness, energy, warmth, stability, valence)
- **Narrative templates**: Pre-built emotional journeys (simple arc, tension-release, journey-return)
- **GOAP planning**: Goal-oriented action planning for musical decisions
- **Musical actions**: Library of tension, resolution, thematic, and textural actions
- **Intent generation**: Bridges storytelling decisions to theory generation
- **TPS integration**: Lerdahl's Tonal Pitch Space for harmonic calculations

### Installation

```bash
dotnet add package BeyondImmersion.Bannou.MusicStoryteller
```

### Quick Start

```csharp
using BeyondImmersion.Bannou.MusicStoryteller;
using BeyondImmersion.Bannou.MusicStoryteller.State;

// Create the storyteller
var storyteller = new Storyteller();

// Compose a 32-bar piece using the "tension and release" narrative
var result = storyteller.ComposeWithTemplate("tension_and_release", totalBars: 32);

// Access the composition
foreach (var section in result.Sections)
{
    Console.WriteLine($"Phase: {section.PhaseName}");
    Console.WriteLine($"  Bars: {section.StartBar}-{section.EndBar}");
    Console.WriteLine($"  Intents: {section.Intents.Count}");

    foreach (var intent in section.Intents)
    {
        Console.WriteLine($"    Tension: {intent.EmotionalTarget.Tension:F2}");
        Console.WriteLine($"    Character: {intent.HarmonicCharacter}");
    }
}
```

### Emotional State Presets

```csharp
// Built-in emotional presets
EmotionalState.Presets.Neutral      // Default starting point
EmotionalState.Presets.Tense        // High tension, low stability
EmotionalState.Presets.Peaceful     // Low tension, high stability
EmotionalState.Presets.Joyful       // High energy, bright, positive
EmotionalState.Presets.Melancholic  // Low energy, dark, negative
EmotionalState.Presets.Climax       // Maximum tension
EmotionalState.Presets.Resolution   // Post-climax resolution
```

### Narrative Templates

| Template | Description | Phases |
|----------|-------------|--------|
| `simple_arc` | Basic dramatic structure | Intro → Build → Climax → Resolution |
| `tension_and_release` | Repeated tension cycles | Build → Peak → Release (repeated) |
| `journey_and_return` | Hero's journey pattern | Home → Departure → Adventure → Return |

### When to Use

Use MusicStoryteller SDK when you need:
- Emotionally-aware procedural music
- Narrative-driven composition
- Dynamic music that responds to game state
- Music that follows dramatic arcs

---

## TypeScript SDK

**Packages**: `@beyondimmersion/bannou-core`, `@beyondimmersion/bannou-client`

TypeScript client SDK for browser and Node.js environments.

### Features

- **Binary WebSocket Protocol**: 31-byte request / 16-byte response headers with big-endian encoding
- **Typed Service Proxies**: Generated `AuthProxy`, `CharacterProxy`, etc. for compile-time safety (33 proxies, 214 endpoints)
- **Event Subscriptions**: Type-safe handlers for 32 server-push events
- **Capability Manifest**: Dynamic API discovery based on authentication state
- **Dual Environment**: Works in browser (native WebSocket) and Node.js (ws package)

### Installation

```bash
npm install @beyondimmersion/bannou-core @beyondimmersion/bannou-client

# For Node.js, also install:
npm install ws
```

### Quick Start

```typescript
import { BannouClient } from '@beyondimmersion/bannou-client';

const client = new BannouClient();
await client.connectAsync('wss://bannou.example.com/connect', accessToken);

// Use typed service proxies
const response = await client.auth.loginAsync({
  email: 'player@example.com',
  password: 'password'
});

// Subscribe to events
client.onEvent('game_session.chat_received', (event) => {
  console.log(`Chat: ${event.message}`);
});
```

### When to Use

Use the TypeScript SDK when you're building:
- Web-based game clients
- Electron desktop applications
- Node.js game servers or bots
- Browser-based admin tools

**See also**: [TypeScript SDK Guide](TYPESCRIPT-SDK.md) for detailed integration instructions.

---

## Unreal Engine Helpers

**Location**: `sdks/unreal/Generated/`

Helper artifacts for integrating Bannou services into Unreal Engine 4 and 5 projects.

### Generated Files

| File | Purpose |
|------|---------|
| `BannouProtocol.h` | Binary protocol constants, message flags, response codes, byte order utilities |
| `BannouTypes.h` | 549 USTRUCT definitions for all request/response models |
| `BannouEnums.h` | All UENUM definitions for type-safe enums |
| `BannouEndpoints.h` | 217 endpoint constants with method, path, and type metadata |
| `BannouEvents.h` | 32 event name constants for server-push events |

### Why Helpers Instead of Full SDK?

Unreal Engine developers have diverse networking requirements. Rather than impose a specific architecture, Bannou provides:
- Type definitions you can use with your preferred networking approach
- Protocol constants for implementing the binary WebSocket protocol
- Comprehensive documentation for manual integration

This gives you full control while eliminating the tedious work of defining 549 structs manually.

### Quick Start

1. Copy headers to your project:
   ```bash
   cp sdks/unreal/Generated/*.h YourProject/Source/YourProject/Bannou/
   ```

2. Include in your code:
   ```cpp
   #include "Bannou/BannouTypes.h"
   #include "Bannou/BannouEndpoints.h"

   // Create request
   Bannou::FLoginRequest Request;
   Request.Email = TEXT("player@example.com");
   Request.Password = TEXT("password");

   // Serialize to JSON
   FString Json;
   FJsonObjectConverter::UStructToJsonObjectString(Request, Json);
   ```

### When to Use

Use the Unreal helpers when you're building:
- Unreal Engine 4 or 5 game clients
- Blueprint-based game logic (all types have UPROPERTY macros)
- Custom networking solutions for UE projects

**See also**: [Unreal Integration Guide](UNREAL-INTEGRATION.md) for detailed integration instructions.

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

### .NET SDKs
- [Core SDK README](../../sdks/core/README.md) - Shared types documentation
- [Client SDK README](../../sdks/client/README.md) - Full client SDK documentation
- [Server SDK README](../../sdks/server/README.md) - Full server SDK documentation
- [Voice SDK README](../../sdks/client-voice/README.md) - Voice SDK documentation
- [AssetBundler README](../../sdks/asset-bundler/README.md) - Core AssetBundler docs
- [Stride AssetBundler README](../../sdks/asset-bundler-stride/README.md) - Stride compilation
- [SceneComposer README](../../sdks/scene-composer/README.md) - Core SceneComposer docs
- [Stride SceneComposer README](../../sdks/scene-composer-stride/README.md) - Stride integration
- [Godot SceneComposer README](../../sdks/scene-composer-godot/README.md) - Godot integration
- [MusicTheory SDK README](../../sdks/music-theory/README.md) - Music theory primitives
- [MusicStoryteller SDK README](../../sdks/music-storyteller/README.md) - Narrative composition
- [Music System Guide](MUSIC_SYSTEM.md) - Comprehensive music system documentation

### TypeScript SDK
- [TypeScript SDK README](../../sdks/typescript/README.md) - Package overview
- [TypeScript SDK Guide](TYPESCRIPT-SDK.md) - Detailed integration guide

### Unreal Engine
- [Unreal Integration Guide](UNREAL-INTEGRATION.md) - C++ integration guide
- [Unreal Integration Guide (detailed)](../../sdks/unreal/Docs/INTEGRATION_GUIDE.md) - Complete walkthrough
- [Unreal Protocol Reference](../../sdks/unreal/Docs/PROTOCOL_REFERENCE.md) - Binary protocol details
- [Unreal Examples](../../sdks/unreal/Docs/EXAMPLES.md) - Full code examples

### Protocol
- [WebSocket Protocol](../WEBSOCKET-PROTOCOL.md) - Binary protocol specification
