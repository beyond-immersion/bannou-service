# BeyondImmersion.Bannou.Client.Voice SDK

P2P voice chat SDK for Bannou-powered games with automatic tier transition to SIP/RTP for large rooms.

## Overview

This SDK provides:
- **VoiceRoomManager**: High-level manager for multi-peer voice rooms (P2P and Scaled)
- **IVoicePeerConnection**: Abstraction for WebRTC peer connections (swappable implementations)
- **SIPSorceryVoicePeer**: Default WebRTC implementation using SIPSorcery v8.0.14
- **IScaledVoiceConnection**: Abstraction for SIP/RTP scaled tier connections
- **ScaledVoiceConnection**: Default SIP/RTP implementation for 6+ participant rooms
- **IVideoPeerConnection**: Stub interface for 1:1 video (not multi-peer)

## Installation

```bash
dotnet add package BeyondImmersion.Bannou.Client.Voice
```

This package depends on:
- `BeyondImmersion.Bannou.Client.SDK` (main Bannou SDK)
- `SIPSorcery` v8.0.14 (WebRTC/SIP/RTP - BSD-3-Clause license)
- `SIPSorceryMedia.Encoders` v0.0.16-pre (Opus codec)

## Quick Start (Stride Engine)

```csharp
using BeyondImmersion.Bannou.Client.SDK;
using BeyondImmersion.Bannou.Client.Voice;

public class VoiceController : SyncScript
{
    private BannouClient _client;
    private VoiceRoomManager _voiceManager;
    private Microphone _microphone;

    public override void Start()
    {
        // Assume BannouClient is already connected
        _client = Game.Services.GetService<BannouClient>();

        // Create voice manager - it auto-subscribes to voice events
        // and handles tier transitions automatically
        _voiceManager = new VoiceRoomManager(_client);

        // Handle incoming audio from peers (works for both P2P and Scaled)
        _voiceManager.OnAudioReceived += PlayPeerAudio;
        _voiceManager.OnPeerJoined += (peerId, name) =>
            Log.Info($"Peer joined: {name}");
        _voiceManager.OnPeerLeft += peerId =>
            Log.Info($"Peer left: {peerId}");

        // Optional: Track tier changes
        _voiceManager.OnTierUpgraded += rtpServerUri =>
            Log.Info("Room upgraded to scaled mode via SIP/RTP");
        _voiceManager.OnScaledConnectionStateChanged += state =>
            Log.Info($"Scaled connection: {state}");
        _voiceManager.OnScaledConnectionError += (code, msg) =>
            Log.Error($"Scaled voice error: {code} - {msg}");

        // Initialize microphone
        _microphone = Microphone.Default;
    }

    public override void Update()
    {
        // Capture and send microphone audio each frame
        // This works transparently for both P2P and Scaled modes!
        if (_voiceManager.IsInRoom && !_voiceManager.IsMuted)
        {
            var samples = CaptureMicrophoneAudio();
            _voiceManager.SendAudioToAllPeers(samples, 48000, 1);
        }
    }

    private float[] CaptureMicrophoneAudio()
    {
        // Stride microphone capture - adapt to your audio system
        // Return normalized float samples (-1.0 to 1.0)
        // Typically 48kHz sample rate, mono channel
        return _microphone.ReadSamples();
    }

    private void PlayPeerAudio(string peerSessionId, float[] samples, int sampleRate, int channels)
    {
        // Play received audio via Stride's DynamicSoundSource
        // Each peer should have their own audio source for spatial positioning
        var source = GetOrCreateAudioSourceForPeer(peerSessionId);
        source.SubmitBuffer(samples, sampleRate, channels);
    }

    public override void Cancel()
    {
        _voiceManager?.Dispose();
    }
}
```

## Architecture

### Voice Room Lifecycle

1. **Room Join**: Server sends `VoiceRoomStateEvent` with room info, STUN servers, and existing peers
2. **Peer Discovery**: `VoicePeerJoinedEvent` for new peers, `VoicePeerLeftEvent` when peers leave
3. **P2P Connection**: SDK creates WebRTC connections to each peer automatically
4. **Audio Flow**: Call `SendAudioToAllPeers()` with microphone data; receive via `OnAudioReceived`
5. **Tier Upgrade**: If room exceeds 5 peers, server sends `VoiceTierUpgradeEvent` with SIP credentials

### P2P vs Scaled Mode

| Mode | Participants | Audio Path | SDK Support |
|------|--------------|------------|-------------|
| P2P | 1-5 peers | Direct peer-to-peer WebRTC | Full - VoiceRoomManager handles |
| Scaled | 6+ peers | Via SIP/RTP server (Kamailio+RTPEngine) | Full - Automatic tier transition |

When `OnTierUpgraded` fires, the VoiceRoomManager automatically:
1. Closes all P2P WebRTC connections
2. Creates a ScaledVoiceConnection using SIP credentials from the event
3. Registers with the SIP server (Kamailio)
4. Connects to the RTP server for audio mixing
5. Redirects `SendAudioToAllPeers()` through the RTP stream

**No code changes needed** - your game code continues using `SendAudioToAllPeers()` and `OnAudioReceived` regardless of tier.

### Scaled Tier Architecture

```
Game Client                     Voice Infrastructure
    |                                   |
    | ---- SIP REGISTER (UDP:5060) ---> | Kamailio
    | <--- 200 OK ---------------------- |
    |                                   |
    | ---- RTP Audio (UDP:22222) -----> | RTPEngine
    | <--- Mixed Audio ----------------- |
    |                                   |
```

The scaled tier uses:
- **Kamailio**: SIP server for participant registration and conference management
- **RTPEngine**: RTP media proxy for audio mixing and forwarding
- **Server-generated SIP credentials**: Per-session credentials in `VoiceTierUpgradeEvent`

### Audio Format

- **Sample Rate**: 48000 Hz (Opus native)
- **Channels**: 1 (mono) or 2 (stereo)
- **Sample Format**: `float[]` normalized to -1.0 to 1.0
- **Codec**: Opus (via SIPSorcery's AudioEncoder)

## Scaled Tier Manual Control

For advanced use cases, you can access the scaled connection directly:

```csharp
// Check if currently in scaled mode
if (_voiceManager.CurrentTier == VoiceRoomStateEventTier.Scaled)
{
    var scaledConn = _voiceManager.ScaledConnection;

    // Check connection health
    if (scaledConn?.IsAudioActive == true)
    {
        var stats = scaledConn.GetStatistics();
        Log.Info($"RTP stats: sent={stats.PacketsSent}, recv={stats.PacketsReceived}");
    }
}

// Handle registration expiring (server will send new credentials)
_voiceManager.OnScaledConnectionStateChanged += state =>
{
    if (state == ScaledVoiceConnectionState.Failed)
    {
        // Connection lost - wait for server to send new tier upgrade event
    }
};
```

### Direct ScaledVoiceConnection Usage

If you need more control than VoiceRoomManager provides:

```csharp
using BeyondImmersion.Bannou.Client.Voice;

// When VoiceTierUpgradeEvent is received:
var connection = new ScaledVoiceConnection(roomId);
connection.OnAudioFrameReceived += (samples, rate, channels) => PlayAudio(samples, rate, channels);
connection.OnConnected += () => Console.WriteLine("Connected to voice conference!");
connection.OnError += (code, message) => Console.WriteLine($"Error: {code} - {message}");
connection.OnRegistrationExpiring += (seconds) => Console.WriteLine($"Registration expires in {seconds}s");

// Build credentials from event
var credentials = SipConnectionCredentials.FromEvent(event.SipCredentials, conferenceUri);
await connection.ConnectAsync(credentials, event.RtpServerUri);

// Send audio from microphone:
connection.SendAudioFrame(microphonePcm, 48000, 1);

// Mute/unmute:
connection.IsMuted = true;

// Refresh registration when server sends new credentials:
await connection.RefreshRegistrationAsync(newCredentials);

// When leaving the room:
await connection.DisconnectAsync();
```

## Custom WebRTC Implementation

If you're using a platform with native WebRTC (Unity WebRTC package, etc.), implement `IVoicePeerConnection`:

```csharp
using BeyondImmersion.Bannou.Client.Voice;

public class UnityWebRTCPeer : IVoicePeerConnection
{
    private RTCPeerConnection _pc;

    public Guid PeerId { get; }
    public string? DisplayName { get; }
    public VoicePeerConnectionState State { get; private set; }
    public bool IsMuted { get; set; }

    public event Action<string>? OnIceCandidateGathered;
    public event Action? OnConnected;
    public event Action<string?>? OnDisconnected;
    public event Action<VoicePeerConnectionState>? OnStateChanged;
    public event Action<float[], int, int>? OnAudioFrameReceived;

    public UnityWebRTCPeer(Guid peerId, string? displayName, IEnumerable<string>? stunServers)
    {
        PeerId = peerId;
        DisplayName = displayName;

        // Create Unity WebRTC peer connection
        var config = new RTCConfiguration
        {
            iceServers = stunServers?.Select(s => new RTCIceServer { urls = new[] { s } }).ToArray()
        };
        _pc = new RTCPeerConnection(ref config);

        // Wire up Unity WebRTC events to interface events
        _pc.OnIceCandidate = candidate => OnIceCandidateGathered?.Invoke(candidate.Candidate);
        _pc.OnConnectionStateChange = state => { /* update State, fire events */ };
        _pc.OnTrack = e => { /* decode audio, fire OnAudioFrameReceived */ };
    }

    public async Task<string> CreateOfferAsync(CancellationToken ct = default)
    {
        var offer = _pc.CreateOffer();
        await offer;
        _pc.SetLocalDescription(ref offer.Desc);
        return offer.Desc.sdp;
    }

    public Task SetRemoteDescriptionAsync(string sdp, bool isOffer, CancellationToken ct = default)
    {
        var desc = new RTCSessionDescription { type = isOffer ? RTCSdpType.Offer : RTCSdpType.Answer, sdp = sdp };
        _pc.SetRemoteDescription(ref desc);
        return Task.CompletedTask;
    }

    public async Task<string> CreateAnswerAsync(CancellationToken ct = default)
    {
        var answer = _pc.CreateAnswer();
        await answer;
        _pc.SetLocalDescription(ref answer.Desc);
        return answer.Desc.sdp;
    }

    public void AddIceCandidate(string candidate)
    {
        _pc.AddIceCandidate(new RTCIceCandidate(new RTCIceCandidateInit { candidate = candidate }));
    }

    public void SendAudioFrame(ReadOnlySpan<float> pcmSamples, int sampleRate, int channels)
    {
        // Encode and send via audio track
    }

    public Task CloseAsync()
    {
        _pc.Close();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _pc.Dispose();
    }
}
```

Then provide your factory to VoiceRoomManager:

```csharp
var voiceManager = new VoiceRoomManager(
    client,
    (peerId, name, stunServers) => new UnityWebRTCPeer(peerId, name, stunServers)
);
```

## Custom Scaled Tier Implementation

You can also provide a custom scaled tier implementation:

```csharp
var voiceManager = new VoiceRoomManager(
    client,
    peerFactory: null, // Use default SIPSorcery for P2P
    scaledConnectionFactory: (roomId) => new CustomScaledConnection(roomId)
);
```

## Alternative WebRTC Libraries

If SIPSorcery doesn't meet your needs, here are alternatives:

### Unity WebRTC Package
- **Platform**: Unity 2019.4+
- **Pros**: Native performance, officially supported
- **Cons**: Unity-only
- **Install**: Unity Package Manager

### WebRTC.NET (libwebrtc wrapper)
- **Platform**: Windows, Linux, macOS
- **Pros**: Uses Google's libwebrtc (same as Chrome)
- **Cons**: Native dependencies, larger binary
- **NuGet**: `WebRTC` or `WebRTC.NET`

### Pion (Go) via Interop
- **Platform**: Cross-platform
- **Pros**: Excellent documentation, active community
- **Cons**: Requires Go interop or separate process

## Video (1:1 Only)

Video is intentionally NOT included in VoiceRoomManager because:
- P2P video bandwidth scales as N*(N-1)/2 connections
- 3 participants = 3 connections = 3x upload bandwidth per client
- 5 participants = 10 connections = 5x upload (unsustainable)

For 1:1 video calls, use `SIPSorceryVideoPeer` (currently a stub) or implement `IVideoPeerConnection`:

```csharp
// Video is NOT managed by VoiceRoomManager - handle separately
var videoPeer = new YourVideoPeerImplementation(peerId, displayName, stunServers);
videoPeer.OnVideoFrameReceived += (rgba, width, height, ts) => RenderFrame(rgba, width, height);
videoPeer.SendVideoFrame(cameraPixels, 1280, 720, timestamp);
```

## License Notes

This SDK pins **SIPSorcery v8.0.14** specifically because:

- **v8.0.14 and earlier**: BSD-3-Clause (permissive, no restrictions)
- **v8.0.15 and later**: Added geographic/political restrictions in the license

We recommend staying on v8.0.14 unless you've reviewed the new license terms and they're acceptable for your use case. The BSD-3-Clause license allows you to:
- Use commercially without royalties
- Modify and distribute
- Include in proprietary software

## Troubleshooting

### No Audio Received
1. Check `OnPeerStateChanged` - ensure peers reach `Connected` state
2. Verify STUN servers are accessible (firewall issues)
3. Check if peers are sending audio (`!IsMuted`)

### ICE Connection Failures
1. STUN servers may be blocked - try different servers
2. Symmetric NAT may require TURN server (not currently supported in SDK)
3. Check firewall rules for UDP traffic

### Scaled Tier Connection Issues
1. Check `OnScaledConnectionError` for specific error codes
2. Verify SIP server (Kamailio) is reachable on UDP:5060
3. Verify RTP server is reachable on the specified port (typically UDP:22222)
4. Check firewall rules for UDP traffic to voice infrastructure

### Echo/Feedback
- Implement echo cancellation in your audio pipeline
- Mute local playback of your own audio
- Use headphones during development

### High Latency
- Ensure 48kHz sample rate (Opus native)
- Use 20ms audio frames (960 samples at 48kHz)
- Check network conditions between peers

## API Reference

### VoiceRoomManager

| Property | Type | Description |
|----------|------|-------------|
| CurrentRoomId | Guid? | Current voice room ID, null if not in room |
| IsInRoom | bool | Whether client is in a voice room |
| CurrentTier | VoiceRoomStateEventTier | P2P or Scaled mode |
| Peers | IReadOnlyDictionary<string, IVoicePeerConnection> | Active P2P peer connections by session ID |
| ScaledConnection | IScaledVoiceConnection? | Scaled tier connection (null in P2P mode) |
| IsScaledConnectionActive | bool | Whether scaled tier audio is flowing |
| IsMuted | bool | Whether local audio is muted |

| Event | Parameters | Description |
|-------|------------|-------------|
| OnAudioReceived | (string peerSessionId, float[] samples, int rate, int channels) | Audio from peer (P2P mode) |
| OnPeerJoined | (string peerSessionId, string? name) | New peer joined room |
| OnPeerLeft | (string peerSessionId) | Peer left room |
| OnRoomClosed | (string reason) | Room was closed |
| OnTierUpgraded | (string rtpServerUri) | Upgraded to scaled mode |
| OnScaledConnectionStateChanged | (ScaledVoiceConnectionState state) | Scaled connection state change |
| OnScaledConnectionError | (ScaledVoiceErrorCode code, string message) | Scaled connection error |
| OnPeerStateChanged | (string peerSessionId, VoicePeerConnectionState state) | P2P peer connection state change |
| OnIceCandidateReady | (string peerSessionId, string candidate) | ICE candidate to relay |

| Method | Description |
|--------|-------------|
| SendAudioToAllPeers(float[], int, int) | Broadcast audio to all peers (works in both tiers) |
| ProcessSdpAnswerAsync(string, string) | Manually process SDP answer |
| AddIceCandidateForPeer(string, string) | Manually add ICE candidate |
| LeaveRoomAsync() | Leave current room |
| Dispose() | Clean up all connections |

### IScaledVoiceConnection

| Property | Type | Description |
|----------|------|-------------|
| RoomId | Guid | Voice room ID |
| State | ScaledVoiceConnectionState | Current connection state |
| IsAudioActive | bool | Whether audio is flowing |
| SipUsername | string | Registered SIP username |
| IsMuted | bool | Whether sending audio is muted |

| Event | Parameters | Description |
|-------|------------|-------------|
| OnStateChanged | (ScaledVoiceConnectionState state) | Connection state change |
| OnError | (ScaledVoiceErrorCode code, string message) | Error occurred |
| OnConnected | () | Successfully connected and registered |
| OnDisconnected | (string? reason) | Disconnected from server |
| OnRegistrationExpiring | (int seconds) | SIP registration expiring soon |
| OnAudioFrameReceived | (float[] samples, int rate, int channels) | Audio from RTP stream |

| Method | Description |
|--------|-------------|
| ConnectAsync(credentials, rtpUri) | Connect to SIP/RTP infrastructure |
| DisconnectAsync() | Disconnect from server |
| RefreshRegistrationAsync(newCredentials?) | Refresh SIP registration |
| SendAudioFrame(samples, rate, channels) | Send audio to RTP stream |
| GetStatistics() | Get RTP stream statistics |
| ReconnectAsync() | Reconnect using last credentials |

### ScaledVoiceConnectionState

| Value | Description |
|-------|-------------|
| Disconnected | Not connected |
| Registering | SIP registration in progress |
| Dialing | Connecting to RTP stream |
| Connected | Fully connected and audio active |
| Reconnecting | Attempting to reconnect |
| Failed | Connection failed |

### ScaledVoiceErrorCode

| Value | Description |
|-------|-------------|
| Unknown | Unknown error |
| RegistrationFailed | SIP registration failed |
| RegistrationTimeout | SIP registration timed out |
| CredentialsExpired | SIP credentials expired |
| RtpSetupFailed | RTP stream setup failed |
| NetworkError | Network connectivity issue |

### IVoicePeerConnection

See `IVoicePeerConnection.cs` for full interface documentation.
