# Voice Streaming - RTMP I/O for Scaled Voice Rooms

> **Status**: Ready for Implementation
> **Last Updated**: 2025-12-27
> **Depends On**: Voice Plugin (lib-voice) - Implemented
> **Estimated Effort**: 5-7 days

---

## Executive Summary

Add RTMP streaming capabilities to Bannou scaled voice rooms:

- **RTMP Output**: Stream mixed audio + video background to Twitch, YouTube, or custom endpoints
- **RTMP Input**: Receive live video from game cameras as room background

The design extends the existing voice plugin with a `StreamingCoordinator` helper service. Game cameras publish discovery events via lib-messaging, which GameSession service consumes to configure voice room streaming.

| Feature | Description |
|---------|-------------|
| **RTMP Output** | Stream room audio + background to any RTMP endpoint |
| **RTMP Input** | Receive game camera RTMP streams as video background |
| **Fallback Cascade** | Auto-fallback: primary → backup stream → image → default → black |
| **Connectivity Validation** | FFmpeg probe validates RTMP URLs before accepting |
| **Mid-Session Updates** | Switch cameras or destinations (~2-3s interruption) |
| **Health Monitoring** | Auto-restart FFmpeg on failure, publish health events |

---

## Service Dependencies

**GameSession → Voice** (one-way): GameSession orchestrates voice streaming via lib-mesh service invocation. Voice plugin has no knowledge of GameSession.

```
┌─────────────────────────────────────────────────────────────────┐
│  Game Engines  ──lib-messaging──►  GameSession  ──lib-mesh──►  Voice  │
│  (cameras)       CameraStream*       Service        /stream/*   Service│
└─────────────────────────────────────────────────────────────────┘
```

---

## License Compliance (Tenet 18)

### Approved Components

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **SIPSorcery** | BSD-3-Clause | ✅ Approved | Pinned to v8.0.14 in sdks/client-voice |
| **MediaMTX** | MIT | ✅ Approved | Optional test RTMP server |
| **FFmpeg** | LGPL v2.1+ | ✅ Container | Process isolation, not linked |

### Infrastructure Components (Network-Only, Tenet 4 Exception)

FFmpeg, RTPEngine, and Kamailio run as separate processes with network/IPC communication only. No code linking occurs.

### FFmpeg LGPL Build Requirements

```bash
# REQUIRED: LGPL-compliant FFmpeg build (default in most distros)
# Must NOT include: --enable-gpl, --enable-nonfree, libx264, libx265
```

---

## Architecture Overview

```
┌───────────────────────────────────────────────────────────────────┐
│                     STREAMING ARCHITECTURE                         │
├───────────────────────────────────────────────────────────────────┤
│                                                                   │
│  GAME ENGINE LAYER                                                │
│  ┌──────────────┐                                                 │
│  │ Game Camera  │──RTMP──► rtmp://game-server:1935/cameras/xyz    │
│  └──────┬───────┘                                                 │
│         │ CameraStreamStartedEvent (lib-messaging)                │
│         ▼                                                         │
│  BANNOU SERVICE LAYER                                             │
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │  GameSession Service                                          │ │
│  │  - Subscribes to camera events                                │ │
│  │  - Manages camera registry                                    │ │
│  │  - Calls Voice API via lib-mesh                               │ │
│  └────────────────────────┬─────────────────────────────────────┘ │
│                           │ POST /voice/room/stream/start         │
│                           ▼                                       │
│  ┌──────────────────────────────────────────────────────────────┐ │
│  │  Voice Service (lib-voice)                                    │ │
│  │  ┌────────────────────────────────────────────────────────┐  │ │
│  │  │  StreamingCoordinator (Singleton helper)                │  │ │
│  │  │                                                         │  │ │
│  │  │  FFmpeg Process:                                        │  │ │
│  │  │  ┌─────────────────────────────────────────────────┐   │  │ │
│  │  │  │ Input 1: RTP audio (RTPEngine mixed output)     │   │  │ │
│  │  │  │ Input 2: RTMP video (game camera)               │   │  │ │
│  │  │  │ Output:  RTMP stream → Twitch/YouTube/custom    │   │  │ │
│  │  │  └─────────────────────────────────────────────────┘   │  │ │
│  │  │                                                         │  │ │
│  │  │  - Validates RTMP connectivity (FFprobe)                │  │ │
│  │  │  - Manages FFmpeg lifecycle                             │  │ │
│  │  │  - Fallback cascade on input failure                    │  │ │
│  │  │  - Health monitoring + auto-restart                     │  │ │
│  │  │  - Publishes client events via IClientEventPublisher    │  │ │
│  │  └────────────────────────────────────────────────────────┘  │ │
│  └──────────────────────────────────────────────────────────────┘ │
│                           │                                       │
│                           ▼                                       │
│  EXTERNAL DESTINATIONS                                            │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐                        │
│  │  Twitch  │  │  YouTube │  │  Custom  │                        │
│  └──────────┘  └──────────┘  └──────────┘                        │
└───────────────────────────────────────────────────────────────────┘
```

### Fallback Cascade

```
Primary RTMP (backgroundVideoUrl)
  └─ Failed → Fallback Stream (fallbackStreamUrl)
       └─ Failed → Fallback Image (fallbackImageUrl)
            └─ Failed → Default Background (VOICE_DEFAULT_BACKGROUND_VIDEO)
                 └─ Failed → Black Video (lavfi color=black)
```

Each fallback publishes `VoiceStreamSourceChangedEvent` to all room participants.

---

## API Design

### New Endpoints for `voice-api.yaml`

All endpoints use POST-only pattern for zero-copy WebSocket routing (Tenet 1).

```yaml
/voice/room/stream/start:
  post:
    summary: Start RTMP streaming for a scaled voice room
    operationId: startRoomStream
    tags: [Voice Streaming]
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/StartStreamRequest'
    responses:
      '200':
        description: Streaming started
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StreamStatusResponse'
      '400':
        description: Room is P2P mode, invalid URL, or connectivity failed
      '404':
        description: Room not found
      '409':
        description: Stream already active

/voice/room/stream/stop:
  post:
    summary: Stop RTMP streaming
    operationId: stopRoomStream
    tags: [Voice Streaming]
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/StopStreamRequest'
    responses:
      '200':
        description: Streaming stopped
      '404':
        description: Room not found or not streaming

/voice/room/stream/update:
  post:
    summary: Update streaming configuration (causes ~2-3s interruption)
    operationId: updateRoomStream
    tags: [Voice Streaming]
    x-permissions:
      - role: admin
        states: {}
    requestBody:
      required: true
      content:
        application/json:
          schema:
            $ref: '#/components/schemas/UpdateStreamRequest'
    responses:
      '200':
        description: Configuration updated
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StreamStatusResponse'
      '400':
        description: Invalid URL or connectivity failed
      '404':
        description: Room not found or not streaming

/voice/room/stream/status:
  post:
    summary: Get streaming status
    operationId: getRoomStreamStatus
    tags: [Voice Streaming]
    x-permissions:
      - role: admin
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
      '404':
        description: Room not found
```

### Request/Response Models

```yaml
StartStreamRequest:
  type: object
  required: [roomId, rtmpOutputUrl]
  properties:
    roomId:
      type: string
      format: uuid
    rtmpOutputUrl:
      type: string
      description: RTMP destination (e.g., rtmp://live.twitch.tv/app/{key})
    backgroundVideoUrl:
      type: string
      nullable: true
      description: Primary video source (RTMP URL, HTTP URL, or file path)
    fallbackStreamUrl:
      type: string
      nullable: true
    fallbackImageUrl:
      type: string
      nullable: true
    audioCodec:
      type: string
      default: "aac"
    audioBitrate:
      type: string
      default: "128k"

StopStreamRequest:
  type: object
  required: [roomId]
  properties:
    roomId:
      type: string
      format: uuid

UpdateStreamRequest:
  type: object
  required: [roomId]
  properties:
    roomId:
      type: string
      format: uuid
    rtmpOutputUrl:
      type: string
      nullable: true
    backgroundVideoUrl:
      type: string
      nullable: true
    fallbackStreamUrl:
      type: string
      nullable: true
    fallbackImageUrl:
      type: string
      nullable: true

StreamStatusRequest:
  type: object
  required: [roomId]
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
    rtmpOutputUrl:
      type: string
      nullable: true
      description: Masked URL (stream key hidden)
    currentVideoSource:
      type: string
      nullable: true
    videoSourceType:
      type: string
      enum: [primary_stream, fallback_stream, fallback_image, default, black]
      nullable: true
    startedAt:
      type: string
      format: date-time
      nullable: true
    durationSeconds:
      type: integer
      nullable: true
    health:
      type: string
      enum: [healthy, degraded, unhealthy, stopped]
```

---

## Client Events

Add to `voice-client-events.yaml`:

```yaml
VoiceStreamStartedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  required: [event_name, event_id, timestamp, room_id, video_source_type]
  properties:
    event_name:
      type: string
      enum: ["voice.stream_started"]
    room_id:
      type: string
      format: uuid
    video_source_type:
      type: string
      enum: [primary_stream, fallback_stream, fallback_image, default, black]

VoiceStreamStoppedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  required: [event_name, event_id, timestamp, room_id, reason]
  properties:
    event_name:
      type: string
      enum: ["voice.stream_stopped"]
    room_id:
      type: string
      format: uuid
    reason:
      type: string
      enum: [manual, error, room_closed]

VoiceStreamSourceChangedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  required: [event_name, event_id, timestamp, room_id, previous_source_type, new_source_type]
  properties:
    event_name:
      type: string
      enum: ["voice.stream_source_changed"]
    room_id:
      type: string
      format: uuid
    previous_source_type:
      type: string
      enum: [primary_stream, fallback_stream, fallback_image, default, black]
    new_source_type:
      type: string
      enum: [primary_stream, fallback_stream, fallback_image, default, black]
    reason:
      type: string
      enum: [manual_switch, source_disconnected, source_recovered]

VoiceStreamHealthEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  required: [event_name, event_id, timestamp, room_id, health]
  properties:
    event_name:
      type: string
      enum: ["voice.stream_health"]
    room_id:
      type: string
      format: uuid
    health:
      type: string
      enum: [healthy, degraded, unhealthy]
    message:
      type: string
      nullable: true
```

---

## Camera Discovery Events

Add to `common-events.yaml` for game engine camera discovery:

```yaml
CameraStreamStartedEvent:
  type: object
  description: Published by game engine when camera stream becomes available
  required: [eventId, timestamp, cameraId, gameInstanceId, rtmpUrl]
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    cameraId:
      type: string
    gameInstanceId:
      type: string
      format: uuid
    sessionId:
      type: string
      format: uuid
      nullable: true
    rtmpUrl:
      type: string
    displayName:
      type: string
      nullable: true
    capabilities:
      type: array
      items:
        type: string

CameraStreamEndedEvent:
  type: object
  required: [eventId, timestamp, cameraId, gameInstanceId]
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    cameraId:
      type: string
    gameInstanceId:
      type: string
      format: uuid
    reason:
      type: string
      enum: [normal_shutdown, error, game_ended]
      nullable: true
```

**Topic naming** (Tenet 5): `camera.stream.started`, `camera.stream.ended`

---

## Configuration

Add to `voice-configuration.yaml`:

```yaml
x-service-configuration:
  # Streaming Configuration
  StreamingEnabled:
    type: boolean
    env: VOICE_STREAMING_ENABLED
    default: false

  FfmpegPath:
    type: string
    env: VOICE_FFMPEG_PATH
    default: "/usr/bin/ffmpeg"

  DefaultBackgroundVideo:
    type: string
    env: VOICE_DEFAULT_BACKGROUND_VIDEO
    default: "/opt/bannou/backgrounds/default.mp4"

  MaxConcurrentStreams:
    type: integer
    env: VOICE_MAX_CONCURRENT_STREAMS
    default: 10

  StreamingAudioCodec:
    type: string
    env: VOICE_STREAMING_AUDIO_CODEC
    default: "aac"

  StreamingAudioBitrate:
    type: string
    env: VOICE_STREAMING_AUDIO_BITRATE
    default: "128k"

  StreamingVideoCodec:
    type: string
    env: VOICE_STREAMING_VIDEO_CODEC
    default: "libvpx"

  StreamingRestartOnFailure:
    type: boolean
    env: VOICE_STREAMING_RESTART_ON_FAILURE
    default: true

  StreamingHealthCheckIntervalSeconds:
    type: integer
    env: VOICE_STREAMING_HEALTH_CHECK_INTERVAL_SECONDS
    default: 10

  RtmpProbeTimeoutSeconds:
    type: integer
    env: VOICE_RTMP_PROBE_TIMEOUT_SECONDS
    default: 5
```

---

## Implementation

### StreamingCoordinator Service

Location: `lib-voice/Services/StreamingCoordinator.cs`

```csharp
/// <summary>
/// Manages FFmpeg processes for RTMP streaming from scaled voice rooms.
/// Singleton lifetime - survives individual request scopes.
/// </summary>
public interface IStreamingCoordinator
{
    Task<(StatusCodes, StreamStatusResponse?)> StartStreamAsync(
        Guid roomId, StartStreamRequest request, CancellationToken ct = default);

    Task<(StatusCodes, StreamStatusResponse?)> StopStreamAsync(
        Guid roomId, CancellationToken ct = default);

    Task<(StatusCodes, StreamStatusResponse?)> UpdateStreamAsync(
        Guid roomId, UpdateStreamRequest request, CancellationToken ct = default);

    StreamStatusResponse? GetStreamStatus(Guid roomId);

    IReadOnlyList<StreamStatusResponse> GetAllActiveStreams();
}
```

**Lifetime**: Singleton (per Tenet 6 - manages long-lived processes)

**Dependencies**:
- `VoiceServiceConfiguration` - streaming settings
- `IClientEventPublisher` - publish events to room participants (via lib-messaging)
- `ILogger<StreamingCoordinator>`

**Key Implementation Details**:
- Track FFmpeg processes in `ConcurrentDictionary<Guid, StreamContext>`
- Validate RTMP URLs via FFprobe before accepting
- Monitor process health via stderr parsing
- Auto-restart on crash if `StreamingRestartOnFailure` enabled
- Publish events via `IClientEventPublisher.PublishToRoomAsync()`

### Security: Stream Key Protection (Tenet 10)

```csharp
private static string MaskRtmpUrl(string url)
{
    // rtmp://live.twitch.tv/app/secret_key → rtmp://live.twitch.tv/app/****
    var uri = new Uri(url);
    var lastSlash = uri.AbsolutePath.LastIndexOf('/');
    return lastSlash > 0
        ? $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath[..lastSlash]}/****"
        : $"{uri.Scheme}://{uri.Host}/****";
}
```

- Never log full RTMP URLs
- Return masked URLs in API responses
- Stream config is in-memory only (not persisted to state store)

### RTMP URL Validation

```csharp
private async Task<bool> ValidateRtmpConnectivityAsync(string rtmpUrl, CancellationToken ct)
{
    var probeArgs = $"-i \"{rtmpUrl}\" -t 1 -v error -f null -";
    using var process = new Process { /* FFprobe settings */ };

    process.Start();
    var completed = await process.WaitForExitAsync(ct)
        .WaitAsync(TimeSpan.FromSeconds(_configuration.RtmpProbeTimeoutSeconds), ct);

    if (!completed) { process.Kill(); return false; }
    return process.ExitCode == 0;
}
```

---

## Infrastructure Requirements

### Docker Image Update

```dockerfile
# Add FFmpeg (LGPL build) to bannou image
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*

# Add default backgrounds
COPY provisioning/streaming/backgrounds/ /opt/bannou/backgrounds/
```

### Optional: MediaMTX for Testing

```yaml
# docker-compose.streaming-test.yml
services:
  mediamtx:
    image: bluenviron/mediamtx:latest
    ports:
      - "1935:1935"   # RTMP
      - "8889:8889"   # WebRTC preview
```

---

## Implementation Phases

### Phase 1: Core Streaming (~3 days)
- [ ] Create `IStreamingCoordinator` interface and `StreamingCoordinator` implementation
- [ ] FFmpeg process management (spawn, monitor, terminate)
- [ ] RTMP URL validation via FFprobe
- [ ] Basic fallback to black video
- [ ] Add streaming endpoints to `voice-api.yaml`
- [ ] Run `scripts/generate-all-services.sh`
- [ ] Implement `VoiceService.StartRoomStreamAsync()` etc. delegating to coordinator

### Phase 2: Fallback Cascade (~1 day)
- [ ] Implement full fallback cascade logic
- [ ] FFmpeg input failure detection (stderr parsing)
- [ ] Automatic fallback switching
- [ ] Publish `VoiceStreamSourceChangedEvent`

### Phase 3: Client Events (~0.5 day)
- [ ] Add streaming events to `voice-client-events.yaml`
- [ ] Run generation scripts
- [ ] Implement event publishing in StreamingCoordinator via `IClientEventPublisher`

### Phase 4: Camera Discovery Events (~0.5 day)
- [ ] Add camera events to `common-events.yaml`
- [ ] Run generation scripts
- [ ] Document GameSession subscription pattern

### Phase 5: Health & Recovery (~0.5 day)
- [ ] Process health monitoring (stderr parsing for warnings/errors)
- [ ] Auto-restart on FFmpeg crash
- [ ] `VoiceStreamHealthEvent` publishing

### Phase 6: Testing (~1 day)
- [ ] Unit tests for StreamingCoordinator (mock Process)
- [ ] HTTP integration tests via http-tester
- [ ] Manual testing with MediaMTX

**Total**: 5-7 days

---

## Rejected Alternatives

| Alternative | Reason Rejected |
|-------------|-----------------|
| **Separate lib-streaming plugin** | Unnecessary - streaming is tightly coupled to scaled voice rooms |
| **GStreamer instead of FFmpeg** | Team more familiar with FFmpeg; GStreamer has larger dependencies |
| **RTPEngine recording** | Writes to file, not live stream |
| **Janus Gateway** | GPLv3 license incompatible |
| **Direct RTP forwarding** | RTMP platforms require transcoding |

---

## Future Extensibility

**Multi-stream layouts** (side-by-side, picture-in-picture) can be added later using FFmpeg's `filter_complex` without technology changes. GPU acceleration (NVENC) recommended for 4+ simultaneous streams. This is out of scope for initial implementation.

---

## References

- [MediaMTX](https://github.com/bluenviron/mediamtx) - MIT-licensed media server
- [FFmpeg Licensing](https://www.ffmpeg.org/legal.html) - LGPL vs GPL
- [SIPSorcery v8.0.14](https://www.nuget.org/packages/SIPSorcery/8.0.14) - BSD-3-Clause (pinned in sdks/client-voice)
