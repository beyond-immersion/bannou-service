# Voice Streaming - RTMP I/O for Scaled Voice Rooms

> **Status**: Ready for Implementation
> **Last Updated**: 2025-12-23
> **Author**: Claude Code assisted design
> **Depends On**: Voice Plugin (lib-voice) - Implemented

## Executive Summary

This document outlines the design for adding RTMP streaming capabilities to Bannou scaled voice rooms:

- **RTMP Input**: Receive live video from game cameras as room background
- **RTMP Output**: Stream mixed audio + video to Twitch, YouTube, or custom endpoints

The design extends the existing voice plugin with a `StreamingCoordinator` helper service. Game cameras publish discovery events via RabbitMQ, which GameSession service consumes to configure voice room streaming.

### Key Capabilities

| Feature | Description |
|---------|-------------|
| **RTMP Output** | Stream room audio + background to any RTMP endpoint |
| **RTMP Input** | Receive game camera RTMP streams as video background |
| **Fallback Cascade** | Auto-fallback: primary → backup stream → image → default → black |
| **Connectivity Validation** | FFmpeg probe validates RTMP URLs before accepting |
| **Mid-Session Updates** | Switch cameras or destinations with brief interruption |
| **Health Monitoring** | Auto-restart FFmpeg on failure, publish health events |

### Service Dependency Direction

**GameSession → Voice** (one-way): GameSession service knows about Voice plugin and calls its API. Voice plugin has no knowledge of GameSession - it just provides streaming capabilities that GameSession orchestrates.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     SERVICE DEPENDENCY DIRECTION                         │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│   ┌───────────────┐                      ┌───────────────┐              │
│   │ Game Engines  │──RabbitMQ Events────►│  GameSession  │              │
│   │ (Stride/etc)  │  CameraStreamStarted │    Service    │              │
│   │               │  CameraStreamUpdated │               │              │
│   │               │  CameraStreamEnded   │               │              │
│   └───────────────┘                      └───────┬───────┘              │
│                                                  │                      │
│                                          API Calls (Dapr)               │
│                                                  │                      │
│                                                  ▼                      │
│                                          ┌───────────────┐              │
│                                          │ Voice Service │              │
│                                          │ (lib-voice)   │              │
│                                          │               │              │
│                                          │ - Room mgmt   │              │
│                                          │ - Streaming   │              │
│                                          │ - P2P/Scaled  │              │
│                                          └───────────────┘              │
│                                                                         │
│   Voice has NO knowledge of GameSession - it just provides              │
│   streaming capabilities that GameSession orchestrates.                 │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## License Compliance (Tenet 18)

### Approved Components

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **SIPSorcery** | BSD-3-Clause | ✅ Approved | Pinned to v8.0.14 |
| **MediaMTX** | MIT | ✅ Approved | Optional RTMP relay |
| **FFmpeg** | LGPL v2.1+ | ✅ Conditional | Must build WITHOUT `--enable-gpl` |

### Infrastructure Components (Network-Only, Tenet 4 Exception)

| Component | License | Status | Notes |
|-----------|---------|--------|-------|
| **RTPEngine** | GPLv3 | ⚠️ Infrastructure | UDP ng protocol - not linked |
| **Kamailio** | GPLv2+ | ⚠️ Infrastructure | HTTP JSONRPC - not linked |
| **FFmpeg** | LGPL v2.1+ | ⚠️ Infrastructure | Subprocess - not linked |

**FFmpeg Infrastructure Exception**: Like RTPEngine and Kamailio, FFmpeg runs as a separate process. We communicate via stdin/stdout and process lifecycle management, not code linking. This is explicitly documented in Tenet 4's exception list.

### FFmpeg LGPL Build Requirements

```bash
# REQUIRED: LGPL-compliant FFmpeg build
./configure \
  --enable-gpl=no \           # Critical: do NOT enable GPL
  --enable-nonfree=no \       # Do not use non-free codecs
  --enable-libopus \          # Opus is BSD-licensed
  --enable-libvpx \           # VP8/VP9 is BSD-licensed
  --disable-libx264 \         # x264 is GPL - must disable
  --disable-libx265           # x265 is GPL - must disable
```

---

## Architecture Overview

### Complete System Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        COMPLETE STREAMING ARCHITECTURE                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  GAME ENGINE LAYER                                                          │
│  ════════════════                                                           │
│                                                                             │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  Stride Game Server Node                                              │  │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────┐                            │  │
│  │  │ Camera 1 │  │ Camera 2 │  │ Camera N │   ← In-game cameras        │  │
│  │  │ (race)   │  │ (crowd)  │  │ (arena)  │     track voice room       │  │
│  │  └────┬─────┘  └────┬─────┘  └────┬─────┘     participants           │  │
│  │       │             │             │                                   │  │
│  │       └─────────────┼─────────────┘                                   │  │
│  │                     │                                                 │  │
│  │                RTMP Output                                            │  │
│  │                     │                                                 │  │
│  └─────────────────────┼────────────────────────────────────────────────┘  │
│                        ▼                                                    │
│  ┌──────────────────────────────────────┐                                   │
│  │  RabbitMQ Event Bus                   │                                  │
│  │  CameraStreamStarted/Updated/Ended    │                                  │
│  └─────────────────┬────────────────────┘                                   │
│                    │                                                        │
│  BANNOU SERVICE LAYER                                                       │
│  ════════════════════                                                       │
│                    │                                                        │
│                    ▼                                                        │
│  ┌──────────────────────────────────────┐                                   │
│  │  GameSession Service                  │                                  │
│  │                                       │                                  │
│  │  - Subscribes to camera events        │                                  │
│  │  - Manages camera registry            │                                  │
│  │  - Creates voice rooms                │                                  │
│  │  - Configures stream backgrounds      │                                  │
│  │  - Configures outbound destinations   │                                  │
│  │                                       │                                  │
│  └─────────────────┬────────────────────┘                                   │
│                    │                                                        │
│                    │ Dapr service invocation                                │
│                    │ POST /voice/room/stream/start                          │
│                    │ POST /voice/room/stream/update                         │
│                    ▼                                                        │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  Voice Service (lib-voice)                                            │  │
│  │  ┌────────────────────────────────────────────────────────────────┐  │  │
│  │  │  StreamingCoordinator (Helper Service)                          │  │  │
│  │  │                                                                  │  │  │
│  │  │  ┌─────────────────────────────────────────────────────────┐   │  │  │
│  │  │  │                    FFmpeg Process                        │   │  │  │
│  │  │  │                                                          │   │  │  │
│  │  │  │  Input 1: RTP audio (from RTPEngine mixed output)        │   │  │  │
│  │  │  │           ↓                                              │   │  │  │
│  │  │  │  Input 2: RTMP video (from game camera)         ───────► │   │  │  │
│  │  │  │           ↓                                              │   │  │  │
│  │  │  │  Transcode + Mux                                         │   │  │  │
│  │  │  │           ↓                                              │   │  │  │
│  │  │  │  Output: RTMP stream ──────────────────────────────────► │   │  │  │
│  │  │  │                                                          │   │  │  │
│  │  │  └─────────────────────────────────────────────────────────┘   │  │  │
│  │  │                                                                  │  │  │
│  │  │  - Validates RTMP connectivity (probe)                          │  │  │
│  │  │  - Manages FFmpeg process lifecycle                             │  │  │
│  │  │  - Fallback cascade on input failure                            │  │  │
│  │  │  - Health monitoring + auto-restart                             │  │  │
│  │  │  - Publishes client events                                      │  │  │
│  │  │                                                                  │  │  │
│  │  └────────────────────────────────────────────────────────────────┘  │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                    │                                                        │
│  EXTERNAL DESTINATIONS                                                      │
│  ════════════════════                                                       │
│                    │                                                        │
│                    ▼                                                        │
│       ┌───────────────────────────────────────────────────┐                │
│       │                                                   │                │
│       ▼                    ▼                    ▼         │                │
│  ┌──────────┐        ┌──────────┐        ┌──────────┐    │                │
│  │  Twitch  │        │  YouTube │        │  Custom  │    │                │
│  │  Stream  │        │  Live    │        │  RTMP    │    │                │
│  └──────────┘        └──────────┘        └──────────┘    │                │
│                                                           │                │
└───────────────────────────────────────────────────────────┘────────────────┘
```

### Fallback Cascade Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           FALLBACK CASCADE LOGIC                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   Primary RTMP Input (backgroundVideoUrl)                                   │
│   ├── Connected? ──────────────► Use primary stream                         │
│   │                                                                         │
│   └── Disconnected/Failed                                                   │
│       │                                                                     │
│       ▼                                                                     │
│   Fallback Stream (fallbackStreamUrl)                                       │
│   ├── Configured & Connected? ─► Use fallback stream                        │
│   │                             + Publish VoiceStreamSourceChangedEvent     │
│   │                                                                         │
│   └── Not configured or failed                                              │
│       │                                                                     │
│       ▼                                                                     │
│   Fallback Image (fallbackImageUrl)                                         │
│   ├── Configured? ─────────────► Use static image                           │
│   │                             + Publish VoiceStreamSourceChangedEvent     │
│   │                                                                         │
│   └── Not configured                                                        │
│       │                                                                     │
│       ▼                                                                     │
│   Default Background (VOICE_DEFAULT_BACKGROUND_VIDEO)                       │
│   ├── Configured? ─────────────► Use default video                          │
│   │                             + Publish VoiceStreamSourceChangedEvent     │
│   │                                                                         │
│   └── Not configured                                                        │
│       │                                                                     │
│       ▼                                                                     │
│   Black Video (lavfi color=black)                                           │
│   └── Always available ────────► Generate black video                       │
│                                 + Publish VoiceStreamSourceChangedEvent     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Example: Race Event Streaming

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    EXAMPLE: RACE EVENT STREAMING                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. RACE STARTS                                                             │
│     ════════════                                                            │
│     Game Server publishes: CameraStreamStarted                              │
│     {                                                                       │
│       "cameraId": "race-cam-main",                                          │
│       "sessionId": "race-session-123",                                      │
│       "rtmpUrl": "rtmp://game-server-1:1935/cameras/race-cam-main",         │
│       "capabilities": ["follow_player", "wide_angle"]                       │
│     }                                                                       │
│                                                                             │
│  2. GAMESESSION CONFIGURES VOICE ROOM                                       │
│     ═════════════════════════════════                                       │
│     POST /voice/room/stream/start                                           │
│     {                                                                       │
│       "roomId": "voice-room-456",                                           │
│       "rtmpOutputUrl": "rtmp://live.twitch.tv/app/stream_key_xxx",          │
│       "backgroundVideoUrl": "rtmp://game-server-1:1935/cameras/race-cam",   │
│       "fallbackImageUrl": "https://cdn.example.com/race-backdrop.jpg"       │
│     }                                                                       │
│                                                                             │
│  3. VOICE SERVICE STARTS STREAMING                                          │
│     ══════════════════════════════                                          │
│     - Validates RTMP connectivity to game camera                            │
│     - Validates RTMP connectivity to Twitch                                 │
│     - Spawns FFmpeg: camera + voice audio → Twitch                          │
│     - Publishes VoiceStreamStartedEvent to all room participants            │
│                                                                             │
│  4. MID-RACE CAMERA SWITCH                                                  │
│     ════════════════════════                                                │
│     Game decides to switch to crowd camera for exciting moment              │
│     POST /voice/room/stream/update                                          │
│     {                                                                       │
│       "roomId": "voice-room-456",                                           │
│       "backgroundVideoUrl": "rtmp://game-server-1:1935/cameras/crowd-cam"   │
│     }                                                                       │
│     Voice: brief FFmpeg restart (~2-3 seconds), publishes                   │
│            VoiceStreamSourceChangedEvent                                    │
│                                                                             │
│  5. RACE ENDS                                                               │
│     ═════════                                                               │
│     GameSession: POST /voice/room/stream/stop                               │
│     Voice: Terminates FFmpeg, publishes VoiceStreamStoppedEvent             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## API Design (Tenet 1: Schema-First)

### New Endpoints for `voice-api.yaml`

All endpoints use POST-only pattern for zero-copy WebSocket routing (Tenet 1).

```yaml
# ============================================
# Streaming Endpoints (admin role required)
# ============================================

/voice/room/stream/start:
  post:
    summary: Start RTMP streaming for a scaled voice room
    description: |
      Enables RTMP output for a scaled-tier voice room. Spawns FFmpeg process
      to transcode RTPEngine mixed audio + video background to RTMP output.

      **Scaled tier only**: P2P rooms cannot stream (no centralized audio mix).

      **Validation**: Voice service probes RTMP URLs to validate connectivity
      before starting the stream. Invalid URLs return 400 Bad Request.

      **Fallback Cascade**: If primary video input fails, automatically tries:
      fallbackStreamUrl → fallbackImageUrl → default background → black video.
    operationId: startRoomStream
    tags:
      - Voice Streaming
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
        description: Streaming started successfully
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StreamStatusResponse'
      '400':
        description: |
          Bad request:
          - Room is P2P mode (cannot stream)
          - Invalid RTMP URL format
          - RTMP connectivity validation failed
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
      Publishes VoiceStreamStoppedEvent to all room participants.
    operationId: stopRoomStream
    tags:
      - Voice Streaming
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
        description: Streaming stopped successfully
      '404':
        description: Room not found or not streaming

/voice/room/stream/update:
  post:
    summary: Update streaming configuration mid-session
    description: |
      Update RTMP destination or video source for an active stream.
      Causes brief interruption (~2-3 seconds) while FFmpeg restarts.

      Use cases:
      - Switch game cameras during broadcast
      - Change stream destination (e.g., switch from Twitch to YouTube)
      - Update fallback sources

      Publishes VoiceStreamSourceChangedEvent on video source changes.
    operationId: updateRoomStream
    tags:
      - Voice Streaming
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
        description: Stream configuration updated
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StreamStatusResponse'
      '400':
        description: Invalid RTMP URL or connectivity validation failed
      '404':
        description: Room not found or not streaming

/voice/room/stream/status:
  post:
    summary: Get streaming status for a room
    description: |
      Returns current streaming status including health, duration,
      current video source, and output destination.
    operationId: getRoomStreamStatus
    tags:
      - Voice Streaming
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

### New Request/Response Models

```yaml
# ============================================
# Streaming Models
# ============================================

StartStreamRequest:
  type: object
  description: Request to start RTMP streaming for a scaled voice room.
  required:
    - roomId
    - rtmpOutputUrl
  properties:
    roomId:
      type: string
      format: uuid
      description: Voice room ID (must be scaled tier)
    rtmpOutputUrl:
      type: string
      description: |
        RTMP destination URL. Examples:
        - rtmp://live.twitch.tv/app/{stream_key}
        - rtmp://a.rtmp.youtube.com/live2/{stream_key}
        - rtmp://custom-server.example.com/live/stream1
    backgroundVideoUrl:
      type: string
      nullable: true
      description: |
        Primary video background source. Supports:
        - RTMP URL: rtmp://game-camera/stream (live game camera)
        - HTTP URL: https://cdn.example.com/background.mp4
        - File path: /opt/bannou/backgrounds/default.mp4
        If not provided, uses fallback cascade.
    fallbackStreamUrl:
      type: string
      nullable: true
      description: |
        Backup RTMP stream URL. Used if primary backgroundVideoUrl disconnects.
    fallbackImageUrl:
      type: string
      nullable: true
      description: |
        Static image URL (PNG/JPG). Used if no streams available.
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
  description: Request to stop RTMP streaming.
  required:
    - roomId
  properties:
    roomId:
      type: string
      format: uuid

UpdateStreamRequest:
  type: object
  description: Request to update streaming configuration mid-session.
  required:
    - roomId
  properties:
    roomId:
      type: string
      format: uuid
    rtmpOutputUrl:
      type: string
      nullable: true
      description: New RTMP destination (if changing)
    backgroundVideoUrl:
      type: string
      nullable: true
      description: New primary video source (for camera switching)
    fallbackStreamUrl:
      type: string
      nullable: true
      description: New fallback stream URL
    fallbackImageUrl:
      type: string
      nullable: true
      description: New fallback image URL

StreamStatusRequest:
  type: object
  description: Request to get streaming status.
  required:
    - roomId
  properties:
    roomId:
      type: string
      format: uuid

StreamStatusResponse:
  type: object
  description: Current streaming status for a room.
  properties:
    roomId:
      type: string
      format: uuid
    isStreaming:
      type: boolean
      description: Whether stream is currently active
    rtmpOutputUrl:
      type: string
      nullable: true
      description: Current output destination (masked stream key)
    currentVideoSource:
      type: string
      nullable: true
      description: Current video source being used
    videoSourceType:
      type: string
      enum: [primary_stream, fallback_stream, fallback_image, default, black]
      nullable: true
      description: Which source in the fallback cascade is active
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
      description: |
        - healthy: All systems operational
        - degraded: High CPU, dropped frames, or using fallback
        - unhealthy: Process crashed, attempting restart
        - stopped: Not streaming

StreamHealth:
  type: string
  enum: [healthy, degraded, unhealthy, stopped]
  description: Stream health status
```

---

## Client Events (Tenet 17)

Add to `voice-client-events.yaml`:

```yaml
# ============================================
# Streaming Events (pushed to all room participants)
# ============================================

VoiceStreamStartedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  description: |
    Sent to all room participants when RTMP streaming starts.
    Indicates the room is now being broadcast.
  required:
    - event_name
    - event_id
    - timestamp
    - room_id
    - video_source_type
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
      description: Which video source is being used

VoiceStreamStoppedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  description: |
    Sent to all room participants when RTMP streaming stops.
  required:
    - event_name
    - event_id
    - timestamp
    - room_id
    - reason
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
      description: Why the stream stopped

VoiceStreamSourceChangedEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  description: |
    Sent to all room participants when the video source changes.
    Occurs during camera switches or fallback cascade activation.
  required:
    - event_name
    - event_id
    - timestamp
    - room_id
    - previous_source_type
    - new_source_type
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
      description: Why the source changed

VoiceStreamHealthEvent:
  allOf:
    - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
  type: object
  x-client-event: true
  description: |
    Sent to all room participants when stream health changes.
  required:
    - event_name
    - event_id
    - timestamp
    - room_id
    - health
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
      description: Human-readable health status message
```

---

## Camera Discovery Events (Tenet 5)

Add to `common-events.yaml` for game engine camera discovery:

```yaml
# ============================================
# Camera Stream Events (Published by Game Engines)
# ============================================

CameraStreamStartedEvent:
  type: object
  description: |
    Published by game engine instances when a camera stream becomes available.
    GameSession service subscribes to discover available camera streams.
  required:
    - eventId
    - timestamp
    - cameraId
    - gameInstanceId
    - rtmpUrl
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    cameraId:
      type: string
      description: Unique identifier for this camera within the game instance
    gameInstanceId:
      type: string
      format: uuid
      description: Game instance hosting this camera
    sessionId:
      type: string
      format: uuid
      nullable: true
      description: Associated game session ID (if applicable)
    rtmpUrl:
      type: string
      description: RTMP URL to receive this camera's stream
    displayName:
      type: string
      nullable: true
      description: Human-readable camera name (e.g., "Race Start Line")
    capabilities:
      type: array
      items:
        type: string
      description: |
        Camera capabilities. Examples:
        - follow_player: Can track specific player
        - wide_angle: Wide angle view
        - ptz: Pan-tilt-zoom capable
        - audio: Includes ambient audio

CameraStreamUpdatedEvent:
  type: object
  description: |
    Published when a camera stream's properties change.
    E.g., camera moves to new location, capabilities change.
  required:
    - eventId
    - timestamp
    - cameraId
    - gameInstanceId
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
    rtmpUrl:
      type: string
      nullable: true
      description: New RTMP URL (if changed)
    displayName:
      type: string
      nullable: true
    capabilities:
      type: array
      items:
        type: string
      nullable: true

CameraStreamEndedEvent:
  type: object
  description: |
    Published when a camera stream is no longer available.
    GameSession should update voice room backgrounds if using this camera.
  required:
    - eventId
    - timestamp
    - cameraId
    - gameInstanceId
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

**Topic naming**: Following Tenet 5, use `camera.stream.started`, `camera.stream.updated`, `camera.stream.ended`.

---

## Configuration (Tenet 2)

Add to `voice-configuration.yaml` with proper `VOICE_{PROPERTY}` naming:

```yaml
x-service-configuration:
  # ... existing config ...

  # Streaming Configuration
  StreamingEnabled:
    type: boolean
    env: VOICE_STREAMING_ENABLED
    description: Whether RTMP streaming is available
    default: false

  FfmpegPath:
    type: string
    env: VOICE_FFMPEG_PATH
    description: Path to FFmpeg binary
    default: "/usr/bin/ffmpeg"

  DefaultBackgroundVideo:
    type: string
    env: VOICE_DEFAULT_BACKGROUND_VIDEO
    description: Path to default looped background video
    default: "/opt/bannou/backgrounds/default.mp4"

  MaxConcurrentStreams:
    type: integer
    env: VOICE_MAX_CONCURRENT_STREAMS
    description: Maximum concurrent RTMP streams per node
    default: 10

  StreamingAudioCodec:
    type: string
    env: VOICE_STREAMING_AUDIO_CODEC
    description: Default audio codec for RTMP output
    default: "aac"

  StreamingAudioBitrate:
    type: string
    env: VOICE_STREAMING_AUDIO_BITRATE
    description: Default audio bitrate for RTMP output
    default: "128k"

  StreamingVideoCodec:
    type: string
    env: VOICE_STREAMING_VIDEO_CODEC
    description: Video codec for RTMP output (must be LGPL-licensed)
    default: "libvpx"

  StreamingRestartOnFailure:
    type: boolean
    env: VOICE_STREAMING_RESTART_ON_FAILURE
    description: Auto-restart FFmpeg if it crashes
    default: true

  StreamingHealthCheckIntervalSeconds:
    type: integer
    env: VOICE_STREAMING_HEALTH_CHECK_INTERVAL_SECONDS
    description: How often to check FFmpeg process health
    default: 10

  RtmpProbeTimeoutSeconds:
    type: integer
    env: VOICE_RTMP_PROBE_TIMEOUT_SECONDS
    description: Timeout for RTMP connectivity validation
    default: 5
```

---

## Security Considerations

### Stream Key Protection (Tenet 10: Logging)

RTMP stream keys are sensitive credentials. They must be:

1. **Masked in logs**: Never log full RTMP URLs containing stream keys
   ```csharp
   // WRONG
   _logger.LogInformation("Starting stream to {Url}", rtmpUrl);

   // CORRECT
   _logger.LogInformation("Starting stream to {Host}", MaskRtmpUrl(rtmpUrl));

   private static string MaskRtmpUrl(string url)
   {
       // rtmp://live.twitch.tv/app/secret_key → rtmp://live.twitch.tv/app/****
       var uri = new Uri(url);
       var path = uri.AbsolutePath;
       var lastSlash = path.LastIndexOf('/');
       if (lastSlash > 0)
           return $"{uri.Scheme}://{uri.Host}{path[..lastSlash]}/****";
       return $"{uri.Scheme}://{uri.Host}/****";
   }
   ```

2. **Masked in API responses**: `StreamStatusResponse.rtmpOutputUrl` returns masked URL

3. **Not stored in state store**: Stream configuration is in-memory only. On service restart, streaming must be re-initiated by GameSession.

### RTMP URL Validation

Validate RTMP URLs before accepting:

```csharp
private async Task<bool> ValidateRtmpConnectivityAsync(string rtmpUrl, CancellationToken ct)
{
    // Use FFmpeg's probe capability to test connectivity
    var probeArgs = $"-i \"{rtmpUrl}\" -t 1 -v error -f null -";

    using var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = _configuration.FfmpegPath,
            Arguments = probeArgs,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    process.Start();

    var timeoutTask = Task.Delay(
        TimeSpan.FromSeconds(_configuration.RtmpProbeTimeoutSeconds), ct);
    var waitTask = process.WaitForExitAsync(ct);

    if (await Task.WhenAny(timeoutTask, waitTask) == timeoutTask)
    {
        process.Kill();
        return false;
    }

    return process.ExitCode == 0;
}
```

---

## Implementation Guide

### StreamingCoordinator Service

Location: `lib-voice/Services/StreamingCoordinator.cs`

```csharp
/// <summary>
/// Manages FFmpeg processes for RTMP streaming from scaled voice rooms.
/// Each room with streaming enabled gets a dedicated FFmpeg process.
///
/// Responsibilities:
/// - FFmpeg process lifecycle (spawn, monitor, restart, terminate)
/// - RTMP URL validation via FFprobe
/// - Fallback cascade management
/// - Health monitoring and client event publishing
/// </summary>
public interface IStreamingCoordinator
{
    /// <summary>
    /// Start streaming a room's mixed audio to an RTMP endpoint.
    /// Validates connectivity before starting.
    /// </summary>
    Task<(StatusCodes, StreamStatusResponse?)> StartStreamAsync(
        Guid roomId,
        StartStreamRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Stop streaming for a room. Terminates FFmpeg process.
    /// </summary>
    Task<(StatusCodes, StreamStatusResponse?)> StopStreamAsync(
        Guid roomId,
        CancellationToken ct = default);

    /// <summary>
    /// Update streaming configuration. Causes brief FFmpeg restart.
    /// </summary>
    Task<(StatusCodes, StreamStatusResponse?)> UpdateStreamAsync(
        Guid roomId,
        UpdateStreamRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Get streaming status for a room.
    /// </summary>
    StreamStatusResponse? GetStreamStatus(Guid roomId);

    /// <summary>
    /// Get all active streams on this node.
    /// </summary>
    IReadOnlyList<StreamStatusResponse> GetAllActiveStreams();
}
```

### Lifetime Considerations (Tenet 6)

`IStreamingCoordinator` should be **Singleton** because:
- Manages long-lived FFmpeg processes
- Tracks state across multiple requests
- Must survive individual request scopes

The `VoiceService` is Scoped, so it injects `IStreamingCoordinator` (Scoped → Singleton is valid per Tenet 6).

---

## Rejected Alternatives (Summary)

| Alternative | Reason Rejected |
|-------------|-----------------|
| **Separate lib-streaming plugin** | Unnecessary complexity; streaming is tightly coupled to scaled voice rooms |
| **GStreamer instead of FFmpeg** | Team more familiar with FFmpeg; GStreamer has larger dependency footprint |
| **RTPEngine recording feature** | Writes to file, not live stream; doesn't meet real-time requirement |
| **Janus Gateway** | GPLv3 license requires commercial license for our use case |
| **Direct RTP forwarding** | RTMP platforms require transcoding; can't forward raw RTP |

---

## Implementation Phases

### Phase 1: Core Streaming (~3 days)
- [ ] Create `IStreamingCoordinator` interface and implementation
- [ ] FFmpeg process management (spawn, monitor, terminate)
- [ ] RTMP URL validation via FFprobe
- [ ] Basic fallback to black video
- [ ] Add streaming endpoints to voice-api.yaml
- [ ] Generate and implement service methods

### Phase 2: Fallback Cascade (~1 day)
- [ ] Implement full fallback cascade logic
- [ ] FFmpeg input failure detection
- [ ] Automatic fallback switching
- [ ] VoiceStreamSourceChangedEvent publishing

### Phase 3: Client Events (~1 day)
- [ ] Add streaming events to voice-client-events.yaml
- [ ] Implement event publishing in StreamingCoordinator
- [ ] VoiceStreamStartedEvent, VoiceStreamStoppedEvent
- [ ] VoiceStreamHealthEvent for health changes

### Phase 4: Camera Discovery Events (~0.5 days)
- [ ] Add camera events to common-events.yaml
- [ ] Document GameSession subscription pattern
- [ ] Integration example in documentation

### Phase 5: Health & Recovery (~0.5 days)
- [ ] Process health monitoring
- [ ] Auto-restart on FFmpeg crash
- [ ] Health status in API responses

### Phase 6: Testing (~1 day)
- [ ] Unit tests for StreamingCoordinator
- [ ] Integration tests via HTTP tester
- [ ] Manual testing with test RTMP server (MediaMTX)

**Total Estimated Effort**: ~7 days

---

## Infrastructure Requirements

### Docker Image Updates

```dockerfile
# Add FFmpeg (LGPL build) to bannou image
RUN apt-get update && apt-get install -y \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*

# Add default backgrounds
COPY provisioning/streaming/backgrounds/ /opt/bannou/backgrounds/
```

### Optional: MediaMTX for Testing

For local testing without real Twitch/YouTube:

```yaml
# docker-compose.streaming-test.yml
services:
  mediamtx:
    image: bluenviron/mediamtx:latest
    container_name: mediamtx
    ports:
      - "1935:1935"   # RTMP
      - "8889:8889"   # WebRTC preview
    restart: unless-stopped
```

---

## Future Extensibility: Multi-Stream Layouts

The current single-stream design supports future multi-stream compositing (side-by-side, picture-in-picture, grid layouts) **without requiring technology changes**.

### Multi-Stream Compositing with FFmpeg

FFmpeg's `filter_complex` handles multi-input composition natively:

```bash
# Example: 2x2 grid layout with 4 camera inputs
ffmpeg \
  -i rtmp://camera1 -i rtmp://camera2 -i rtmp://camera3 -i rtmp://camera4 \
  -filter_complex "
    [0:v]scale=960:540[v0];
    [1:v]scale=960:540[v1];
    [2:v]scale=960:540[v2];
    [3:v]scale=960:540[v3];
    [v0][v1][v2][v3]xstack=inputs=4:layout=0_0|960_0|0_540|960_540[vout]
  " \
  -map "[vout]" -map "0:a" -c:v libvpx -c:a aac \
  -f flv rtmp://output

# Example: Picture-in-picture (main camera + small overlay)
ffmpeg \
  -i rtmp://main-camera -i rtmp://pip-camera \
  -filter_complex "
    [1:v]scale=320:180[pip];
    [0:v][pip]overlay=W-w-20:H-h-20[vout]
  " \
  -map "[vout]" -c:v libvpx \
  -f flv rtmp://output
```

### Architecture Extension Path

```
Future Multi-Stream Architecture:
┌──────────────────────────────────────────────────────────────────┐
│  StreamingCoordinator                                            │
│  ├── FFmpeg Process (per room)                                   │
│  │   ├── Input: RTPEngine mixed audio                            │
│  │   ├── Input: Camera A (slot1)                                 │
│  │   ├── Input: Camera B (slot2)                                 │
│  │   ├── Input: Camera N (slotN)                                 │
│  │   ├── Filter: Layout-based filter_complex composition         │
│  │   └── Output: Twitch/YouTube RTMP                             │
│  │                                                               │
│  └── LayoutManager (new helper service)                          │
│      ├── Predefined layouts (side-by-side, PiP, 2x2 grid, etc.) │
│      └── Slot → Position mapping per layout                      │
└──────────────────────────────────────────────────────────────────┘
```

### Layout Switching Considerations

| Approach | Interruption | Complexity | When to Use |
|----------|--------------|------------|-------------|
| **FFmpeg restart** | ~2-3 seconds | Low | Infrequent layout changes (recommended initial approach) |
| **FFdynamic library** | Seamless | Medium | Frequent real-time layout switching |
| **GStreamer** | Seamless | High | Complex dynamic pipelines (major architecture change) |

**Recommendation**: Start with FFmpeg restart for layout changes. The ~2-3 second interruption is acceptable for most broadcast scenarios. If seamless switching becomes critical, integrate [FFdynamic](https://github.com/Xingtao/FFdynamic) (Apache 2.0 license) without changing the overall architecture.

### GPU Acceleration (Recommended for Multi-Stream)

For 2+ simultaneous video streams, GPU encoding becomes essential:

| Streams | CPU-Only | NVENC GPU |
|---------|----------|-----------|
| 1 stream | Comfortable | Overkill |
| 2-4 streams | Heavy load | Comfortable |
| 4+ streams | Impractical | Required |

```bash
# GPU-accelerated multi-stream compositing
ffmpeg \
  -hwaccel cuda -hwaccel_output_format cuda \
  -i rtmp://camera1 -i rtmp://camera2 \
  -filter_complex "[0:v][1:v]hstack=inputs=2[vout]" \
  -map "[vout]" \
  -c:v h264_nvenc -preset p4 -tune ll \
  -f flv rtmp://output
```

**Infrastructure note**: Add `VOICE_GPU_ACCELERATION_ENABLED` configuration when implementing multi-stream support.

### What Changes for Multi-Stream

| Component | Current | Multi-Stream Extension |
|-----------|---------|------------------------|
| `StartStreamRequest` | Single `backgroundVideoUrl` | Add `slots` array with position assignments |
| `StreamingCoordinator` | Single input FFmpeg | Multi-input with filter_complex generation |
| **New**: `LayoutManager` | N/A | Predefined layout definitions, slot→position mapping |
| **New**: `UpdateLayoutRequest` | N/A | Switch between layouts (triggers FFmpeg restart) |
| FFmpeg binary | Same | Same (no change) |
| Infrastructure | Same | Add NVIDIA GPU for 4+ streams |

### Licensing for Multi-Stream Components

| Component | License | Status |
|-----------|---------|--------|
| FFmpeg filter_complex | LGPL 2.1 | ✅ Already approved (container) |
| FFdynamic | Apache 2.0 | ✅ Fully compatible |
| NVENC (GPU encoding) | Proprietary NVIDIA | ✅ OK (hardware, not linked) |

**No additional licensing concerns for multi-stream extension.**

---

## References

- [MediaMTX](https://github.com/bluenviron/mediamtx) - MIT-licensed media server
- [FFmpeg Licensing](https://www.ffmpeg.org/legal.html) - LGPL vs GPL
- [FFmpeg filter_complex](https://ffmpeg.org/ffmpeg-filters.html) - Multi-input compositing
- [FFdynamic](https://github.com/Xingtao/FFdynamic) - Dynamic FFmpeg composition (Apache 2.0)
- [RTPEngine ng protocol](https://github.com/sipwise/rtpengine) - Audio mixing
- [SIPSorcery v8.0.14](https://www.nuget.org/packages/SIPSorcery/8.0.14) - BSD-3-Clause
- [NVIDIA NVENC FFmpeg Guide](https://docs.nvidia.com/video-technologies/video-codec-sdk/ffmpeg-with-nvidia-gpu/) - GPU acceleration

---

*This document is ready for implementation. All design decisions are finalized and TENETS-compliant.*
