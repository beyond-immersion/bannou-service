# Voice Streaming - RTMP Output for Voice Rooms

> **Status**: Proposed / Investigation Complete
> **Last Updated**: 2025-12-20
> **Author**: Claude Code assisted design
> **Depends On**: Voice Plugin (lib-voice) - Implemented

## Executive Summary

This document outlines the design for adding RTMP streaming output to Bannou voice rooms, enabling live broadcasting of voice conferences to platforms like Twitch, YouTube, or custom RTMP endpoints. The design extends the existing voice plugin rather than creating a separate streaming plugin, with a clear upgrade path for future video integration from game cameras.

### Key Capabilities

| Feature | Description | Complexity |
|---------|-------------|------------|
| **RTMP Output** | Stream room audio to any RTMP endpoint | Low |
| **Looped Video Background** | Add static or looped video behind audio | Low |
| **Mid-Session Updates** | Change RTMP URL or background during stream | Medium |
| **Live RTMP Input (Game Cameras)** | Use game camera RTMP as background | Medium |
| **Camera Switching** | Switch between game cameras live | High |

### Design Decision: Extend Voice Plugin

**Recommendation**: Add streaming as an extension to the existing voice plugin rather than creating a separate streaming plugin.

**Rationale**:
- Streaming only makes sense for scaled-tier rooms (RTPEngine handles audio mixing)
- Direct access to RTP streams from RTPEngine (no inter-service coordination overhead)
- Lower operational complexity (one service to monitor)
- Cohesive API surface for room management
- TENETS allow Voice service exception for infrastructure access (Tenet 2)

**Future Upgrade Path**: If video features become complex (scene composition, multiple video sources, overlays), a separate streaming plugin can be extracted. The API surface would remain the same.

---

## Table of Contents

- [Voice Streaming - RTMP Output for Voice Rooms](#voice-streaming---rtmp-output-for-voice-rooms)
  - [Executive Summary](#executive-summary)
  - [Table of Contents](#table-of-contents)
  - [License Compliance Analysis](#license-compliance-analysis)
  - [Architecture Overview](#architecture-overview)
    - [Current Voice Architecture](#current-voice-architecture)
    - [Streaming Extension Architecture](#streaming-extension-architecture)
    - [Game Camera Integration Architecture](#game-camera-integration-architecture)
  - [Technical Implementation](#technical-implementation)
    - [FFmpeg Process Management](#ffmpeg-process-management)
    - [RTPEngine Audio Tap](#rtpengine-audio-tap)
    - [SDP File Generation](#sdp-file-generation)
  - [API Design](#api-design)
    - [Schema Extensions](#schema-extensions)
    - [Configuration Extensions](#configuration-extensions)
  - [Infrastructure Requirements](#infrastructure-requirements)
  - [Implementation Roadmap](#implementation-roadmap)
  - [Alternative Approaches Considered](#alternative-approaches-considered)
  - [Open Questions](#open-questions)
  - [References](#references)

---

## License Compliance Analysis

All streaming components must meet our licensing requirements: **No copyleft, no share-alike, no attribution requirements that would affect our proprietary code.**

### Approved Components

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **SIPSorcery** | BSD-3-Clause | ✅ Approved | Pinned to v8.0.14 (pre-May 2025 license change) |
| **MediaMTX** | MIT | ✅ Approved | Zero-dependency media router, RTP↔RTMP bridging |
| **FFmpeg** | LGPL v2.1+ | ✅ Conditional | Must build WITHOUT `--enable-gpl` flag |

### Rejected Components

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **Janus Gateway** | GPLv3 | ❌ Rejected | Copyleft - requires commercial license for our use |
| **libx264** | GPLv2 | ❌ Rejected | Would trigger FFmpeg GPL mode |

### Infrastructure Components (Network-Only)

These components run as separate containers - we communicate via network protocols only (no linking):

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **RTPEngine** | GPLv3 | ⚠️ Infrastructure | UDP ng protocol only - not linked/distributed |
| **Kamailio** | GPLv2+ | ⚠️ Infrastructure | HTTP JSONRPC only - not linked/distributed |

**Legal Basis**: Network communication with GPL software does not trigger copyleft obligations. We're calling these services over network protocols (UDP, HTTP), not linking their code. This is the standard "ASP loophole" interpretation - confirmed by GPL FAQ and widely accepted practice.

### FFmpeg Build Requirements

To maintain LGPL compliance, FFmpeg must be built with specific flags:

```bash
# REQUIRED: LGPL-compliant FFmpeg build
./configure \
  --enable-gpl=no \           # Critical: do NOT enable GPL
  --enable-nonfree=no \       # Do not use non-free codecs
  --enable-libopus \          # Opus is BSD-licensed
  --enable-libvpx \           # VP8/VP9 is BSD-licensed
  --disable-libx264 \         # x264 is GPL - must disable
  --disable-libx265           # x265 is GPL - must disable

# For video output, use:
# - VP8/VP9 (libvpx) - BSD licensed
# - OpenH264 (Cisco) - BSD licensed with binary distribution
# - Software H.264 via openh264
```

### SIPSorcery Version Lock

**Critical**: `Bannou.Voice.SDK` is pinned to SIPSorcery v8.0.14:

```xml
<!-- Bannou.Voice.SDK/Bannou.Voice.SDK.csproj -->
<!-- SIPSorcery v8.0.14: Last version under pure BSD-3-Clause license (pre-May 2025 license change) -->
<PackageReference Include="SIPSorcery" Version="8.0.14" />
```

**Verified**: This version is correctly pinned and documented.

---

## Architecture Overview

### Current Voice Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                    CURRENT VOICE ARCHITECTURE                        │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────┐    P2P Mode     ┌─────────────┐                   │
│  │   Client    │◄───(WebRTC)────►│   Client    │                   │
│  │  (≤6 users) │                 │             │                   │
│  └─────────────┘                 └─────────────┘                   │
│                                                                     │
│  ┌─────────────┐                 ┌─────────────┐                   │
│  │   Client    │    Scaled       │  RTPEngine  │ ◄── ng protocol   │
│  │  (>6 users) │◄───(SFU)───────►│  (SFU/MCU)  │     UDP:22222     │
│  └─────────────┘                 └──────┬──────┘                   │
│                                         │                           │
│  ┌─────────────┐                 ┌──────▼──────┐                   │
│  │VoiceService │◄───(HTTP)──────►│  Kamailio   │                   │
│  │  (Bannou)   │    JSONRPC      │  SIP Proxy  │                   │
│  └─────────────┘                 └─────────────┘                   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**Key Insight**: RTPEngine already mixes audio for scaled-tier rooms. The mixed audio is available - we just need to tap it and transcode to RTMP.

### Streaming Extension Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                    STREAMING EXTENSION ARCHITECTURE                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────┐                 ┌─────────────┐                   │
│  │  RTPEngine  │────RTP─────────►│   FFmpeg    │                   │
│  │   (mixer)   │  (mixed audio)  │ (transcode) │                   │
│  └─────────────┘                 └──────┬──────┘                   │
│                                         │                           │
│                                    ┌────▼────┐                      │
│                                    │  RTMP   │                      │
│                                    │ Output  │                      │
│                                    └────┬────┘                      │
│                                         │                           │
│                    ┌────────────────────┼────────────────────┐      │
│                    ▼                    ▼                    ▼      │
│              ┌──────────┐        ┌──────────┐        ┌──────────┐  │
│              │  Twitch  │        │  YouTube │        │  Custom  │  │
│              └──────────┘        └──────────┘        └──────────┘  │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │                     StreamingCoordinator                      │  │
│  │                                                               │  │
│  │  - FFmpeg process lifecycle management                        │  │
│  │  - SDP file generation for RTPEngine tap                      │  │
│  │  - Background video overlay (looped or RTMP input)            │  │
│  │  - Health monitoring and auto-restart                         │  │
│  │                                                               │  │
│  └──────────────────────────────────────────────────────────────┘  │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Game Camera Integration Architecture

This is where it gets exciting - game cameras can provide live video backgrounds:

```
┌─────────────────────────────────────────────────────────────────────┐
│                   GAME CAMERA + VOICE STREAMING                     │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌────────────────┐                                                 │
│  │  Game Server   │                                                 │
│  │  ┌──────────┐  │         ┌─────────────┐                        │
│  │  │ Camera 1 │──┼──RTMP──►│  MediaMTX   │◄──RTMP─┐               │
│  │  │ Camera 2 │──┼──RTMP──►│  (optional) │        │               │
│  │  │ Camera N │──┼──RTMP──►│             │        │               │
│  │  └──────────┘  │         └──────┬──────┘        │               │
│  └────────────────┘                │               │               │
│                                    ▼               │               │
│         ┌──────────────────────────────────────────┼───────────┐   │
│         │              VoiceService                │           │   │
│         │  ┌─────────────────────────────────────┐ │           │   │
│         │  │         StreamingCoordinator        │ │           │   │
│         │  │                                     │ │           │   │
│         │  │  Room 1: FFmpeg                     │ │           │   │
│         │  │    Input: RTP (audio) + RTMP (cam1) │ │           │   │
│         │  │    Output: RTMP (Twitch)            │─┼──RTMP────►│   │
│         │  │                                     │ │           │   │
│         │  │  Room 2: FFmpeg                     │ │           │   │
│         │  │    Input: RTP (audio) + Loop Video  │ │           │   │
│         │  │    Output: RTMP (YouTube)           │─┼──RTMP────►│   │
│         │  └─────────────────────────────────────┘ │           │   │
│         └──────────────────────────────────────────┘           │   │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐│
│  │                       API for Camera Control                    ││
│  │                                                                 ││
│  │  POST /voice/room/stream/start                                  ││
│  │    { roomId, rtmpUrl, backgroundVideoUrl: "rtmp://cam1" }       ││
│  │                                                                 ││
│  │  POST /voice/room/stream/update                                 ││
│  │    { roomId, backgroundVideoUrl: "rtmp://cam2" }  ← switch cam  ││
│  │                                                                 ││
│  └─────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────┘
```

**Game Camera Integration Flow**:

1. **Game Server Publishes Camera RTMP Streams**
   - Each "camera" in the game world outputs an RTMP stream
   - Could use FFmpeg, GStreamer, or render-to-RTMP game engine feature
   - Streams published to MediaMTX or direct RTMP endpoint

2. **VoiceService Accepts RTMP URL as Background**
   - `StartStreamRequest.backgroundVideoUrl` accepts RTMP://
   - FFmpeg reads live RTMP input instead of looped file
   - Audio from voice conference overlaid on game video

3. **Dynamic Camera Switching**
   - `UpdateRoomStreamAsync` changes the background source
   - Brief FFmpeg restart (~2-3 seconds transition)
   - Could implement smooth transitions with MediaMTX

---

## Technical Implementation

### FFmpeg Process Management

**New Service**: `lib-voice/Services/StreamingCoordinator.cs`

```csharp
/// <summary>
/// Manages FFmpeg processes for RTMP streaming from voice rooms.
/// Each room with streaming enabled gets a dedicated FFmpeg process.
/// </summary>
public interface IStreamingCoordinator
{
    /// <summary>
    /// Start streaming a room's mixed audio to an RTMP endpoint.
    /// </summary>
    Task<StreamingResult> StartStreamAsync(
        Guid roomId,
        string rtmpUrl,
        StreamingOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop streaming for a room.
    /// </summary>
    Task<bool> StopStreamAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update streaming configuration (RTMP URL or background).
    /// Causes brief interruption while FFmpeg restarts.
    /// </summary>
    Task<StreamingResult> UpdateStreamAsync(
        Guid roomId,
        StreamingUpdateOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get streaming status for a room.
    /// </summary>
    StreamingStatus? GetStreamStatus(Guid roomId);

    /// <summary>
    /// Get all active streams.
    /// </summary>
    IReadOnlyList<StreamingStatus> GetAllActiveStreams();
}

public class StreamingOptions
{
    /// <summary>
    /// RTMP endpoint URL (e.g., rtmp://live.twitch.tv/app/streamkey)
    /// </summary>
    public required string RtmpUrl { get; init; }

    /// <summary>
    /// Optional: URL to looped background video (MP4/WebM file path or RTMP URL)
    /// </summary>
    public string? BackgroundVideoUrl { get; init; }

    /// <summary>
    /// Optional: URL to static background image (PNG/JPG)
    /// </summary>
    public string? BackgroundImageUrl { get; init; }

    /// <summary>
    /// Audio codec for RTMP output. Default: aac
    /// </summary>
    public string AudioCodec { get; init; } = "aac";

    /// <summary>
    /// Audio bitrate for RTMP output. Default: 128k
    /// </summary>
    public string AudioBitrate { get; init; } = "128k";

    /// <summary>
    /// Video codec for RTMP output. Default: libx264 (requires license check)
    /// </summary>
    public string VideoCodec { get; init; } = "libvpx";  // BSD-licensed

    /// <summary>
    /// Video preset for encoding speed/quality tradeoff.
    /// </summary>
    public string VideoPreset { get; init; } = "ultrafast";
}

public class StreamingStatus
{
    public Guid RoomId { get; init; }
    public bool IsStreaming { get; init; }
    public string? RtmpUrl { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public TimeSpan? Duration => StartedAt.HasValue
        ? DateTimeOffset.UtcNow - StartedAt.Value
        : null;
    public int? ProcessId { get; init; }
    public string? BackgroundSource { get; init; }
    public StreamingHealth Health { get; init; }
}

public enum StreamingHealth
{
    Healthy,
    Degraded,  // High CPU, dropped frames
    Unhealthy, // Process crashed, restarting
    Stopped
}
```

### RTPEngine Audio Tap

To stream room audio, we subscribe to RTPEngine's mixed output:

```csharp
/// <summary>
/// Creates an RTP subscription to tap the mixed room audio from RTPEngine.
/// </summary>
private async Task<RtpTapInfo> CreateRtpTapAsync(
    Guid roomId,
    VoiceRoomState roomState,
    CancellationToken ct)
{
    // RTPEngine "subscribe request" creates a sendonly stream of mixed audio
    var subscribeResponse = await _rtpEngineClient.SubscribeRequestAsync(
        callId: roomState.RtpEngineCallId,
        fromTags: roomState.ParticipantFromTags.ToArray(),
        subscriberLabel: $"stream_{roomId}",
        ct);

    if (!subscribeResponse.IsSuccess)
    {
        throw new InvalidOperationException(
            $"Failed to create RTP tap: {subscribeResponse.ErrorReason}");
    }

    // Parse SDP to get the RTP port
    var sdpParser = new SdpParser(subscribeResponse.Sdp);
    var rtpPort = sdpParser.GetMediaPort("audio");
    var codec = sdpParser.GetAudioCodec();  // Should be Opus

    return new RtpTapInfo
    {
        RoomId = roomId,
        RtpPort = rtpPort,
        Codec = codec,
        Sdp = subscribeResponse.Sdp,
        SubscriberLabel = $"stream_{roomId}"
    };
}
```

### SDP File Generation

FFmpeg requires an SDP file to understand RTP stream parameters:

```csharp
/// <summary>
/// Generates SDP file for FFmpeg to read the RTP tap.
/// </summary>
private string GenerateSdpFile(RtpTapInfo tapInfo, string sdpFilePath)
{
    // SDP content for Opus audio from RTPEngine
    var sdpContent = $@"v=0
o=- 0 0 IN IP4 127.0.0.1
s=Voice Room Stream
c=IN IP4 127.0.0.1
t=0 0
m=audio {tapInfo.RtpPort} RTP/AVP 111
a=rtpmap:111 opus/48000/2
a=fmtp:111 stereo=1; sprop-stereo=1
a=recvonly
";

    File.WriteAllText(sdpFilePath, sdpContent);
    return sdpFilePath;
}
```

### FFmpeg Command Generation

```csharp
/// <summary>
/// Builds FFmpeg command for RTMP streaming.
/// </summary>
private string BuildFfmpegCommand(StreamingContext ctx)
{
    var args = new StringBuilder();

    // Input 1: RTP audio from RTPEngine (via SDP file)
    args.Append($"-protocol_whitelist \"file,udp,rtp\" ");
    args.Append($"-i \"{ctx.SdpFilePath}\" ");

    // Input 2: Background video (optional)
    if (!string.IsNullOrEmpty(ctx.Options.BackgroundVideoUrl))
    {
        if (ctx.Options.BackgroundVideoUrl.StartsWith("rtmp://"))
        {
            // Live RTMP input (game camera)
            args.Append($"-i \"{ctx.Options.BackgroundVideoUrl}\" ");
        }
        else
        {
            // Looped local video file
            args.Append($"-stream_loop -1 -i \"{ctx.Options.BackgroundVideoUrl}\" ");
        }
    }
    else if (!string.IsNullOrEmpty(ctx.Options.BackgroundImageUrl))
    {
        // Static image as video
        args.Append($"-loop 1 -i \"{ctx.Options.BackgroundImageUrl}\" ");
    }
    else
    {
        // Audio-only: generate blank video
        args.Append("-f lavfi -i color=c=black:s=1280x720:r=30 ");
    }

    // Video encoding (BSD-licensed codecs only)
    args.Append($"-c:v {ctx.Options.VideoCodec} ");
    args.Append($"-preset {ctx.Options.VideoPreset} ");
    args.Append("-tune zerolatency ");
    args.Append("-pix_fmt yuv420p ");
    args.Append("-g 60 ");  // Keyframe interval

    // Audio encoding
    args.Append($"-c:a {ctx.Options.AudioCodec} ");
    args.Append($"-b:a {ctx.Options.AudioBitrate} ");
    args.Append("-ar 48000 ");
    args.Append("-ac 2 ");

    // Output format and destination
    args.Append($"-f flv \"{ctx.Options.RtmpUrl}\"");

    return args.ToString();
}
```

---

## API Design

### Schema Extensions

Add to `schemas/voice-api.yaml`:

```yaml
# New endpoints
/voice/room/stream/start:
  post:
    summary: Start RTMP streaming for a voice room
    description: |
      Enables RTMP output for a scaled-tier voice room.
      Only works for rooms using RTPEngine (P2P rooms cannot stream).
      Spawns FFmpeg process to transcode RTP to RTMP.
    operationId: startRoomStream
    tags:
      - Voice Streaming
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/StartStreamRequest'
    responses:
      '200':
        description: Streaming started successfully
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StreamStatusResponse'
      '400':
        description: |
          Bad request. Possible reasons:
          - Room is in P2P mode (cannot stream)
          - Invalid RTMP URL format
          - Missing required parameters
      '404':
        description: Room not found
      '409':
        description: Stream already active for this room

/voice/room/stream/stop:
  post:
    summary: Stop RTMP streaming for a voice room
    description: |
      Stops the RTMP stream and terminates the FFmpeg process.
      Does not affect the voice room itself.
    operationId: stopRoomStream
    tags:
      - Voice Streaming
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/StopStreamRequest'
    responses:
      '200':
        description: Streaming stopped successfully
      '404':
        description: Room not found or not streaming

/voice/room/stream/update:
  post:
    summary: Update streaming configuration mid-session
    description: |
      Update RTMP URL or background video for an active stream.
      Causes brief interruption (~2-3 seconds) while FFmpeg restarts.
      Use for switching cameras or changing stream destination.
    operationId: updateRoomStream
    tags:
      - Voice Streaming
    x-permissions:
      - role: developer
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/UpdateStreamRequest'
    responses:
      '200':
        description: Stream configuration updated
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StreamStatusResponse'
      '404':
        description: Room not found or not streaming

/voice/room/stream/status:
  post:
    summary: Get streaming status for a room
    operationId: getRoomStreamStatus
    tags:
      - Voice Streaming
    x-permissions:
      - role: user
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/StreamStatusRequest'
    responses:
      '200':
        description: Stream status
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StreamStatusResponse'

# New models
StartStreamRequest:
  type: object
  required:
    - roomId
    - rtmpUrl
  properties:
    roomId:
      type: string
      format: uuid
      description: Voice room to stream
    rtmpUrl:
      type: string
      description: |
        RTMP endpoint URL.
        Examples:
        - rtmp://live.twitch.tv/app/{stream_key}
        - rtmp://a.rtmp.youtube.com/live2/{stream_key}
        - rtmp://custom-server.example.com/live/stream1
    backgroundVideoUrl:
      type: string
      nullable: true
      description: |
        Optional: URL to background video.
        Supports:
        - Local file path: /opt/bannou/backgrounds/default.mp4
        - RTMP URL: rtmp://game-camera-1/stream (live game camera)
        - HTTP URL: https://cdn.example.com/background.mp4
    backgroundImageUrl:
      type: string
      nullable: true
      description: |
        Optional: URL to static background image (PNG/JPG).
        Ignored if backgroundVideoUrl is provided.
    audioCodec:
      type: string
      default: "aac"
      description: Audio codec for RTMP output
    audioBitrate:
      type: string
      default: "128k"
      description: Audio bitrate for RTMP output

StopStreamRequest:
  type: object
  required:
    - roomId
  properties:
    roomId:
      type: string
      format: uuid

UpdateStreamRequest:
  type: object
  required:
    - roomId
  properties:
    roomId:
      type: string
      format: uuid
    rtmpUrl:
      type: string
      nullable: true
      description: New RTMP URL (if changing destination)
    backgroundVideoUrl:
      type: string
      nullable: true
      description: New background video URL (for camera switching)
    backgroundImageUrl:
      type: string
      nullable: true

StreamStatusRequest:
  type: object
  required:
    - roomId
  properties:
    roomId:
      type: string
      format: uuid

StreamStatusResponse:
  type: object
  properties:
    roomId:
      type: string
      format: uuid
    isStreaming:
      type: boolean
    rtmpUrl:
      type: string
      nullable: true
    startedAt:
      type: string
      format: date-time
      nullable: true
    durationSeconds:
      type: integer
      nullable: true
    backgroundSource:
      type: string
      nullable: true
      description: Current background source (file path or RTMP URL)
    health:
      type: string
      enum: [healthy, degraded, unhealthy, stopped]
```

### Configuration Extensions

Add to `x-service-configuration` in voice-api.yaml:

```yaml
x-service-configuration:
  # ... existing config ...

  # Streaming Configuration
  StreamingEnabled:
    type: boolean
    description: Whether RTMP streaming is available
    default: false
  FFmpegPath:
    type: string
    description: Path to FFmpeg binary
    default: "/usr/bin/ffmpeg"
  DefaultBackgroundVideo:
    type: string
    description: Path to default looped background video
    default: "/opt/bannou/backgrounds/default.mp4"
  MaxConcurrentStreams:
    type: integer
    description: Maximum concurrent RTMP streams per node
    default: 10
  StreamingAudioCodec:
    type: string
    description: Default audio codec for RTMP output
    default: "aac"
  StreamingAudioBitrate:
    type: string
    description: Default audio bitrate for RTMP output
    default: "128k"
  StreamingVideoCodec:
    type: string
    description: Video codec for RTMP output (must be BSD-licensed)
    default: "libvpx"
  StreamingRestartOnFailure:
    type: boolean
    description: Auto-restart FFmpeg if it crashes
    default: true
  StreamingHealthCheckIntervalSeconds:
    type: integer
    description: How often to check FFmpeg process health
    default: 10
```

---

## Infrastructure Requirements

### Docker Image Updates

The bannou Docker image needs FFmpeg installed:

```dockerfile
# In Dockerfile
RUN apt-get update && apt-get install -y \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Add default background video
COPY provisioning/streaming/backgrounds/ /opt/bannou/backgrounds/
```

### MediaMTX (Optional - For Game Cameras)

If using live game camera integration, add MediaMTX:

```yaml
# docker-compose.streaming.yml
services:
  mediamtx:
    image: bluenviron/mediamtx:latest
    container_name: mediamtx
    ports:
      - "8554:8554"   # RTSP
      - "1935:1935"   # RTMP
      - "8889:8889"   # WebRTC (for preview)
      - "8890:8890"   # SRT
    volumes:
      - ./provisioning/mediamtx/mediamtx.yml:/mediamtx.yml:ro
    restart: unless-stopped
```

MediaMTX configuration:

```yaml
# provisioning/mediamtx/mediamtx.yml
paths:
  # Game cameras publish here
  game-camera-~(.*):
    source: publisher
    # Game server publishes to: rtmp://mediamtx:1935/game-camera-1

  # Voice rooms can read from here
  all:
    readUser: bannou
    readPass: ${MEDIAMTX_READ_PASSWORD}
```

---

## Implementation Roadmap

### Phase 1: Basic RTMP Streaming (~2-3 days)

**Goal**: Stream room audio to RTMP with static background

- [ ] Create `IStreamingCoordinator` interface
- [ ] Implement `StreamingCoordinator` with FFmpeg process management
- [ ] Add SDP file generation for RTPEngine tap
- [ ] Implement `RtpEngineClient.SubscribeRequestAsync` integration
- [ ] Add streaming endpoints to voice-api.yaml
- [ ] Generate controller and implement service methods
- [ ] Add FFmpeg to Docker image
- [ ] Unit tests for StreamingCoordinator

### Phase 2: Background Video Support (~1 day)

**Goal**: Support looped video and static image backgrounds

- [ ] FFmpeg command generation for video inputs
- [ ] Default background video asset
- [ ] Static image to video conversion
- [ ] Background source validation

### Phase 3: Mid-Session Updates (~1 day)

**Goal**: Change stream configuration without room restart

- [ ] Implement `UpdateRoomStreamAsync`
- [ ] Graceful FFmpeg restart with minimal interruption
- [ ] Configuration validation before restart

### Phase 4: Health Monitoring (~0.5 days)

**Goal**: Automatic recovery from FFmpeg failures

- [ ] Process health monitoring service
- [ ] Auto-restart on crash
- [ ] Health status in API responses
- [ ] Logging and metrics

### Phase 5: Live RTMP Input (Game Cameras) (~2-3 days)

**Goal**: Use game camera RTMP streams as background

- [ ] MediaMTX deployment configuration
- [ ] RTMP URL validation and connectivity check
- [ ] FFmpeg command generation for RTMP input
- [ ] Camera switching via UpdateStreamAsync
- [ ] Integration tests with MediaMTX

### Phase 6: Testing (~1 day)

**Goal**: Comprehensive test coverage

- [ ] HTTP integration tests for streaming endpoints
- [ ] Edge tests for streaming via WebSocket
- [ ] Manual testing with Twitch/YouTube
- [ ] Load testing (max concurrent streams)

**Total Estimated Effort**: ~7-10 days

---

## Alternative Approaches Considered

### Approach A: Separate Streaming Plugin

**Concept**: Create `lib-streaming` as a separate plugin that coordinates with voice plugin via events.

**Pros**:
- Clean separation of concerns
- Independent scaling
- Natural fit for future video-only streaming

**Cons**:
- More infrastructure complexity
- Inter-service coordination overhead
- Latency for event-driven updates
- Requires RTP forwarding between services

**Decision**: Rejected for initial implementation. Can be extracted later if video features become complex.

### Approach B: GStreamer Instead of FFmpeg

**Concept**: Use GStreamer for transcoding instead of FFmpeg.

**Pros**:
- More flexible pipeline model
- Better live stream handling
- Native LGPL (no GPL trap)

**Cons**:
- More complex setup
- Less documentation for RTMP output
- Larger dependency footprint
- Team more familiar with FFmpeg

**Decision**: Start with FFmpeg, consider GStreamer if we hit FFmpeg limitations.

### Approach C: RTPEngine Recording Feature

**Concept**: Use RTPEngine's built-in recording, then post-process to RTMP.

**Pros**:
- Uses existing infrastructure
- No additional FFmpeg process

**Cons**:
- Recording is to file, not live stream
- Would require complex file→RTMP pipeline
- Not real-time

**Decision**: Rejected - doesn't meet real-time streaming requirement.

### Approach D: Janus Gateway

**Concept**: Replace RTPEngine with Janus for full media server capabilities.

**Pros**:
- Built-in streaming plugin
- More features (recording, transcoding, etc.)
- Active development

**Cons**:
- GPLv3 license - requires commercial license
- Would replace working RTPEngine infrastructure
- More complex than needed

**Decision**: Rejected due to license requirements.

---

## Open Questions

1. **FFmpeg Build Source**: Should we use distribution FFmpeg or build our own to ensure LGPL compliance?
   - Distribution packages may include GPL codecs
   - Custom build gives control but adds maintenance burden
   - **Recommendation**: Start with distribution, document codec restrictions

2. **Stream Key Security**: How to securely store/transmit RTMP stream keys?
   - Keys contain authentication for Twitch/YouTube
   - Should not be logged or exposed in status responses
   - **Recommendation**: Mask in logs, encrypt in storage

3. **Bandwidth Limits**: Should we enforce bandwidth limits per stream?
   - RTMP output is typically 2-5 Mbps for video
   - Could overwhelm network if many streams active
   - **Recommendation**: Add MaxConcurrentStreams config (default: 10)

4. **Game Camera Protocol**: What protocol should game servers use to publish camera streams?
   - RTMP is simple and widely supported
   - RTSP might be lower latency
   - WebRTC would be most modern
   - **Recommendation**: Start with RTMP via MediaMTX

5. **Audio-Only Streaming**: Should we support audio-only RTMP (no video track)?
   - Some platforms may not accept audio-only
   - Reduces bandwidth significantly
   - **Recommendation**: Generate blank video by default, add audio-only option later

---

## References

- [MediaMTX GitHub](https://github.com/bluenviron/mediamtx) - MIT-licensed media server
- [FFmpeg Licensing](https://www.ffmpeg.org/legal.html) - LGPL vs GPL considerations
- [RTPEngine GitHub](https://github.com/sipwise/rtpengine) - ng protocol documentation
- [SIPSorcery v8.0.14](https://www.nuget.org/packages/SIPSorcery/8.0.14) - BSD-3-Clause version
- [Janus License](https://janus.conf.meetecho.com/docs/COPYING.html) - GPLv3 (rejected)
- [Kamailio License](https://github.com/kamailio/kamailio) - GPLv2+ (infrastructure only)
- [RTMP Streaming with FFmpeg](https://ottverse.com/rtmp-streaming-using-ffmpeg-tutorial/) - FFmpeg RTMP guide
- [RTP with FFmpeg](https://www.kurento.org/blog/rtp-ii-streaming-ffmpeg) - RTP input handling

---

## Revision History

| Date | Version | Changes |
|------|---------|---------|
| 2025-12-20 | 0.1.0 | Initial investigation and design proposal |

---

*This document is a proposed design. Implementation will begin after approval and prioritization.*
