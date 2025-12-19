# RTP/Media Plugin for Bannou

*Carrier-Grade Voice/Video Architecture for Stride Game Engine*
*Designed for Thousands of Concurrent Calls*
*Created: December 2025*

## Executive Summary

This document outlines a **two-tier media architecture** for Bannou that scales from simple peer-to-peer calls to thousands of concurrent voice channels:

| Tier | Use Case | Components | Capacity |
|------|----------|------------|----------|
| **P2P Direct** | 1:1 calls, small parties (≤6) | SIPSorcery only | Unlimited (no server) |
| **Scaled Infrastructure** | Large conferences, PSTN, NAT traversal | Kamailio + RTPEngine | 10,000+ concurrent streams |

### Architecture Summary

| Component | Solution | Role |
|-----------|----------|------|
| **C# SIP/RTP Client** | **SIPSorcery** | SIP signaling + raw RTP in Stride clients |
| **SIP Proxy** | **Kamailio** | Registration, routing, load balancing |
| **Media Relay** | **RTPEngine** | SFU forwarding, NAT traversal, kernel-mode performance |
| **PSTN Bridge** | **SignalWire** | SIP trunk for external phone numbers |
| **Audio Codec** | **Concentus** | Pure C# Opus encoder/decoder |
| **Stride Audio** | **DynamicSoundSource** | PCM buffer injection with spatial positioning |

### Key Design Decisions

1. **No FreeSWITCH locally** - Kamailio + RTPEngine is lighter and scales better
2. **SFU over MCU** - Clients mix audio locally (better for spatial audio, scales better)
3. **P2P when possible** - Skip media servers entirely for small groups
4. **Kernel-mode RTPEngine** - 10x performance for media forwarding
5. **SignalWire for PSTN** - SIP trunk, no local PBX complexity

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Tier 1: P2P Direct Mode](#2-tier-1-p2p-direct-mode)
3. [Tier 2: Kamailio + RTPEngine](#3-tier-2-kamailio--rtpengine)
4. [SignalWire PSTN Integration](#4-signalwire-pstn-integration)
5. [Bannou Plugin Architecture](#5-bannou-plugin-architecture)
6. [Stride Engine Integration](#6-stride-engine-integration)
7. [Service Events (Internal)](#7-event-schemas)
8. [Docker Deployment](#8-docker-deployment)
9. [Client-Side Flows](#9-client-side-flows) ← **NEW: Game Session Integration**
10. [Client Events for Voice](#10-client-events-for-voice) ← **NEW: WebSocket Events**
11. [Clean Tier Upgrade Strategy](#11-clean-tier-upgrade-strategy) ← **NEW: Zero-Loss Migration**
12. [Tier Availability Configuration](#12-tier-availability-configuration) ← **NEW: Deployment Flexibility**
13. [Implementation Roadmap](#13-implementation-roadmap)

---

## 1. Architecture Overview

### High-Level Architecture

```
                              ┌─────────────────────────────────────────────────┐
                              │              TIER 1: P2P DIRECT                  │
                              │   (1:1 calls, small parties ≤6 participants)     │
                              │                                                  │
┌──────────────┐              │   ┌──────────────┐      ┌──────────────┐        │
│ Stride Client│◄─────────────┼───│ Stride Client│◄────►│ Stride Client│        │
│  (Player A)  │   Direct RTP │   │  (Player B)  │ Mesh │  (Player C)  │        │
│              │              │   │              │      │              │        │
│ - SIPSorcery │              │   │ - SIPSorcery │      │ - SIPSorcery │        │
│ - Concentus  │              │   │ - Concentus  │      │ - Concentus  │        │
└──────┬───────┘              │   └──────────────┘      └──────────────┘        │
       │                      └─────────────────────────────────────────────────┘
       │ WebSocket
       │ (coordination)       ┌─────────────────────────────────────────────────┐
       │                      │         TIER 2: SCALED INFRASTRUCTURE            │
       │                      │   (Large conferences, PSTN, NAT traversal)       │
       ▼                      │                                                  │
┌──────────────┐              │   ┌──────────────┐      ┌──────────────┐        │
│   Bannou     │──────────────┼──►│   Kamailio   │◄────►│  RTPEngine   │        │
│ MediaService │  JSONRPC/ng  │   │  (SIP Proxy) │ ctrl │ (Media SFU)  │        │
│              │              │   │              │      │              │        │
│ - Room mgmt  │              │   │ - Registrar  │      │ - Kernel mode│        │
│ - Routing    │              │   │ - Dispatcher │      │ - NAT trav.  │        │
│ - PSTN coord │              │   │ - Dialog     │      │ - SFU fwd    │        │
└──────┬───────┘              │   └──────┬───────┘      └──────┬───────┘        │
       │                      │          │                     │                │
       │                      │          │ SIP Trunk           │ RTP            │
       │                      │          ▼                     ▼                │
       │                      │   ┌──────────────┐      ┌──────────────┐        │
       │                      │   │  SignalWire  │      │ Game Clients │        │
       │                      │   │   (PSTN)     │      │ (via SFU)    │        │
       │                      │   └──────────────┘      └──────────────┘        │
       │                      └─────────────────────────────────────────────────┘
       │
       │ Dapr Events
       ▼
┌──────────────┐
│ GameSession  │
│   Service    │
└──────────────┘
```

### Routing Decision Logic

```csharp
public VoiceRoute DetermineRoute(VoiceRoomRequest request)
{
    // Tier 1: P2P Direct
    if (request.ParticipantCount <= 6 && !request.RequiresPstn && !request.HasNatIssues)
    {
        return VoiceRoute.P2PDirect;
    }

    // Tier 2: Scaled Infrastructure
    return VoiceRoute.KamailioRtpEngine;
}
```

### Component Responsibilities

| Component | Responsibilities |
|-----------|------------------|
| **SIPSorcery (Client)** | SIP registration, call setup, RTP send/receive, raw packet access |
| **Kamailio** | SIP proxy, user registration, load balancing, dialog tracking |
| **RTPEngine** | Media relay, NAT traversal, SFU forwarding, kernel-mode performance |
| **SignalWire** | PSTN connectivity, phone number management, SIP trunk |
| **MediaService (Bannou)** | Room orchestration, route decisions, Kamailio/RTPEngine control |
| **Concentus** | Opus encode/decode in pure C# |
| **DynamicSoundSource** | Stride audio integration with spatial positioning |

---

## 2. Tier 1: P2P Direct Mode

### When to Use P2P

- **1:1 voice calls** between players
- **Small party chat** (2-6 participants)
- **Low latency priority** (direct path, no server hop)
- **Both endpoints have public IPs** or are on same network

### Architecture

```
Player A                    Player B                    Player C
    │                           │                           │
    │◄─────── Direct RTP ──────►│◄─────── Direct RTP ──────►│
    │                           │                           │
    │◄─────────────────── Direct RTP ─────────────────────►│
    │                           │                           │
    ▼                           ▼                           ▼
┌─────────┐                ┌─────────┐                ┌─────────┐
│SIPSorcery│               │SIPSorcery│               │SIPSorcery│
│Concentus │               │Concentus │               │Concentus │
│ Stride   │               │ Stride   │               │ Stride   │
└─────────┘                └─────────┘                └─────────┘
```

Each client:
1. **Registers** with Bannou (not Kamailio) for endpoint discovery
2. **Calls directly** to other players using SIPSorcery
3. **Mixes locally** in mesh topology (receives N-1 streams, mixes in client)

### SIP Registry Service (Lightweight)

Bannou tracks online players without full SIP proxy:

```csharp
public class SipEndpointRegistry
{
    private readonly ConcurrentDictionary<Guid, PlayerEndpoint> _endpoints = new();

    public void Register(Guid accountId, string publicIp, int rtpPort)
    {
        _endpoints[accountId] = new PlayerEndpoint
        {
            AccountId = accountId,
            PublicIp = publicIp,
            RtpPort = rtpPort,
            RegisteredAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5)
        };
    }

    public PlayerEndpoint? Lookup(Guid accountId)
    {
        if (_endpoints.TryGetValue(accountId, out var endpoint) &&
            endpoint.ExpiresAt > DateTime.UtcNow)
        {
            return endpoint;
        }
        return null;
    }
}
```

### P2P Voice Client (SIPSorcery)

```csharp
using SIPSorcery.Net;
using System.Net;
using System.Net.Sockets;

public class P2PVoiceClient : IDisposable
{
    private readonly ConcurrentDictionary<Guid, RtpSession> _peers = new();
    private readonly UdpClient _rtpSocket;
    private readonly OpusCodec _opus;
    private readonly VoipAudioSource _audioSource;
    private readonly int _localPort;

    public P2PVoiceClient(VoipAudioSource audioSource)
    {
        _audioSource = audioSource;
        _opus = new OpusCodec();
        _rtpSocket = new UdpClient(0); // OS assigns port
        _localPort = ((IPEndPoint)_rtpSocket.Client.LocalEndPoint!).Port;

        Task.Run(ReceiveLoop);
    }

    public int LocalPort => _localPort;

    /// <summary>
    /// Connect directly to another player
    /// </summary>
    public void ConnectToPeer(Guid peerId, string peerIp, int peerPort)
    {
        var endpoint = new IPEndPoint(IPAddress.Parse(peerIp), peerPort);
        var session = new RtpSession
        {
            PeerId = peerId,
            RemoteEndpoint = endpoint,
            Ssrc = (uint)peerId.GetHashCode(),
            SequenceNumber = 0,
            Timestamp = 0
        };
        _peers[peerId] = session;
    }

    public void DisconnectPeer(Guid peerId)
    {
        _peers.TryRemove(peerId, out _);
    }

    /// <summary>
    /// Send audio to all connected peers
    /// </summary>
    public void SendAudio(short[] pcmSamples)
    {
        byte[] opusPayload = _opus.Encode(pcmSamples);

        foreach (var session in _peers.Values)
        {
            byte[] rtpPacket = BuildRtpPacket(opusPayload, session);
            _rtpSocket.Send(rtpPacket, rtpPacket.Length, session.RemoteEndpoint);
        }
    }

    private async Task ReceiveLoop()
    {
        while (true)
        {
            try
            {
                var result = await _rtpSocket.ReceiveAsync();
                ProcessIncomingRtp(result.Buffer, result.RemoteEndPoint);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
        }
    }

    private void ProcessIncomingRtp(byte[] packet, IPEndPoint sender)
    {
        if (packet.Length < 12) return;

        // Parse RTP header
        uint ssrc = (uint)((packet[8] << 24) | (packet[9] << 16) |
                          (packet[10] << 8) | packet[11]);

        // Extract Opus payload (skip 12-byte RTP header)
        byte[] opusPayload = new byte[packet.Length - 12];
        Buffer.BlockCopy(packet, 12, opusPayload, 0, opusPayload.Length);

        // Decode to PCM
        short[] pcmSamples = _opus.Decode(opusPayload);

        // Feed to Stride audio (spatial positioning handled by caller)
        _audioSource.EnqueuePcmSamples(pcmSamples, ssrc);
    }

    private byte[] BuildRtpPacket(byte[] payload, RtpSession session)
    {
        byte[] packet = new byte[12 + payload.Length];

        // V=2, P=0, X=0, CC=0
        packet[0] = 0x80;
        // M=0, PT=111 (Opus)
        packet[1] = 111;
        // Sequence number (big-endian)
        packet[2] = (byte)(session.SequenceNumber >> 8);
        packet[3] = (byte)(session.SequenceNumber & 0xFF);
        session.SequenceNumber++;
        // Timestamp (big-endian)
        packet[4] = (byte)(session.Timestamp >> 24);
        packet[5] = (byte)((session.Timestamp >> 16) & 0xFF);
        packet[6] = (byte)((session.Timestamp >> 8) & 0xFF);
        packet[7] = (byte)(session.Timestamp & 0xFF);
        session.Timestamp += 960; // 20ms @ 48kHz
        // SSRC (big-endian)
        packet[8] = (byte)(session.Ssrc >> 24);
        packet[9] = (byte)((session.Ssrc >> 16) & 0xFF);
        packet[10] = (byte)((session.Ssrc >> 8) & 0xFF);
        packet[11] = (byte)(session.Ssrc & 0xFF);
        // Payload
        Buffer.BlockCopy(payload, 0, packet, 12, payload.Length);

        return packet;
    }

    public void Dispose()
    {
        _rtpSocket.Dispose();
    }

    private class RtpSession
    {
        public Guid PeerId { get; set; }
        public IPEndPoint RemoteEndpoint { get; set; } = null!;
        public uint Ssrc { get; set; }
        public ushort SequenceNumber { get; set; }
        public uint Timestamp { get; set; }
    }
}
```

### Mesh Topology Limits

| Participants | Connections per Client | Total Connections | Recommended |
|--------------|------------------------|-------------------|-------------|
| 2 | 1 | 1 | ✅ P2P |
| 3 | 2 | 3 | ✅ P2P |
| 4 | 3 | 6 | ✅ P2P |
| 5 | 4 | 10 | ✅ P2P |
| 6 | 5 | 15 | ✅ P2P (limit) |
| 7+ | 6+ | 21+ | ⚠️ Use Tier 2 |

Beyond 6 participants, switch to Kamailio + RTPEngine (SFU mode).

---

## 3. Tier 2: Kamailio + RTPEngine

### When to Use Tier 2

- **Large conferences** (7+ participants)
- **NAT traversal required** (clients behind different NATs)
- **PSTN connectivity** (on-call moderator pattern)
- **Recording/monitoring** needed
- **Codec transcoding** required

### Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Kamailio                                     │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐            │
│  │Registrar │  │Dispatcher│  │ Dialog   │  │RTPEngine │            │
│  │  Module  │  │  Module  │  │  Module  │  │  Module  │            │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘            │
│        │              │             │             │                  │
│        └──────────────┴─────────────┴─────────────┘                  │
│                              │                                       │
│                    JSONRPC HTTP API (:5060)                         │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
           ┌───────────────────┼───────────────────┐
           │                   │                   │
           ▼                   ▼                   ▼
    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐
    │  Bannou     │    │  RTPEngine  │    │  SignalWire │
    │MediaService │    │ (ng proto)  │    │ (SIP Trunk) │
    └─────────────┘    └─────────────┘    └─────────────┘
           │                   │
           │ UDP :22222        │ RTP :10000-20000
           │                   │
           └─────────┬─────────┘
                     │
                     ▼
              ┌─────────────┐
              │Game Clients │
              │(SIPSorcery) │
              └─────────────┘
```

### SFU Mode (Selective Forwarding Unit)

RTPEngine forwards streams **without mixing** - each client receives separate streams and mixes locally:

```
Conference Room with 4 Players:

Player A publishes ──► RTPEngine ──► forwards to B, C, D
Player B publishes ──► RTPEngine ──► forwards to A, C, D
Player C publishes ──► RTPEngine ──► forwards to A, B, D
Player D publishes ──► RTPEngine ──► forwards to A, B, C

Each player receives 3 separate audio streams and mixes locally.
```

**Benefits of SFU over MCU:**
- **Lower latency** - no mixing delay
- **Better for spatial audio** - each voice is separate, can position in 3D
- **Scales better** - forwarding is cheaper than mixing
- **End-to-end encryption possible** - streams aren't decoded on server

### Kamailio Configuration

```
#!KAMAILIO
# Game Voice Chat Configuration

# Global settings
debug=2
log_stderror=no
fork=yes
children=8
listen=udp:0.0.0.0:5060
listen=tcp:0.0.0.0:5060

# Load modules
loadmodule "tm.so"
loadmodule "sl.so"
loadmodule "rr.so"
loadmodule "pv.so"
loadmodule "maxfwd.so"
loadmodule "usrloc.so"
loadmodule "registrar.so"
loadmodule "textops.so"
loadmodule "siputils.so"
loadmodule "nathelper.so"
loadmodule "rtpengine.so"
loadmodule "dispatcher.so"
loadmodule "dialog.so"
loadmodule "xhttp.so"
loadmodule "jsonrpcs.so"

# Module parameters
modparam("usrloc", "db_mode", 0)              # Memory-only (game sessions are temp)
modparam("usrloc", "hash_size", 14)           # 16384 slots for thousands of users
modparam("registrar", "default_expires", 300) # 5 minute registration
modparam("registrar", "min_expires", 60)
modparam("rtpengine", "rtpengine_sock", "udp:127.0.0.1:22222")
modparam("dispatcher", "list_file", "/etc/kamailio/dispatcher.list")
modparam("dispatcher", "ds_ping_interval", 10)
modparam("dialog", "enable_stats", 1)
modparam("jsonrpcs", "transport", 1)          # HTTP transport

# Main routing
request_route {
    # Sanity checks
    if (!mf_process_maxfwd_header("10")) {
        sl_send_reply("483", "Too Many Hops");
        exit;
    }

    # NAT detection
    force_rport();
    if (nat_uac_test("19")) {
        setflag(1); # NAT flag
        if (is_method("REGISTER")) {
            fix_nated_register();
        } else {
            fix_nated_contact();
        }
    }

    # Handle CANCEL
    if (is_method("CANCEL")) {
        if (t_check_trans()) {
            route(RELAY);
        }
        exit;
    }

    # Handle registrations
    if (is_method("REGISTER")) {
        if (!save("location")) {
            sl_reply_error();
        }
        exit;
    }

    # Record-route for dialogs
    if (is_method("INVITE|SUBSCRIBE")) {
        record_route();
    }

    # Relay with RTPEngine
    route(RELAY);
}

route[RELAY] {
    # RTPEngine for media handling
    if (has_body("application/sdp")) {
        if (isflagset(1)) {
            # NAT traversal
            rtpengine_manage("replace-origin replace-session-connection ICE=force");
        } else {
            rtpengine_manage("replace-origin replace-session-connection");
        }
    }

    if (!t_relay()) {
        sl_reply_error();
    }
    exit;
}

# JSONRPC API
event_route[xhttp:request] {
    if ($hu =~ "^/RPC") {
        # TODO: Add authentication in production
        jsonrpc_dispatch();
        return;
    }
    xhttp_reply("404", "Not Found", "text/html", "<html><body>Not Found</body></html>");
}
```

### RTPEngine Configuration

```conf
# /etc/rtpengine/rtpengine.conf

[rtpengine]
# Network interfaces
interface = internal/10.0.0.50;external/203.0.113.50

# NG control protocol
listen-ng = 22222

# RTP port range (10,000 ports = 5,000 concurrent calls)
port-min = 10000
port-max = 20000

# Timeouts
timeout = 60
silent-timeout = 3600

# Kernel forwarding (10x performance)
table = 0

# Logging
log-level = 5
log-facility = daemon

# Recording (optional)
# recording-dir = /var/spool/rtpengine
# recording-method = pcap
```

### Kamailio JSONRPC Client (C#)

```csharp
using System.Net.Http.Json;
using System.Text.Json;

public interface IKamailioClient
{
    Task<IEnumerable<ActiveDialog>> GetActiveDialogsAsync();
    Task<bool> TerminateDialogAsync(string dialogId);
    Task<bool> ReloadDispatcherAsync();
    Task<KamailioStats> GetStatsAsync();
}

public class KamailioClient : IKamailioClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<KamailioClient> _logger;
    private int _requestId = 0;

    public KamailioClient(HttpClient httpClient, ILogger<KamailioClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ActiveDialog>> GetActiveDialogsAsync()
    {
        var response = await CallRpcAsync<DialogListResponse>("dlg.list");
        return response?.Dialogs ?? Enumerable.Empty<ActiveDialog>();
    }

    public async Task<bool> TerminateDialogAsync(string dialogId)
    {
        var response = await CallRpcAsync<BaseResponse>(
            "dlg.end_dlg", dialogId);
        return response?.Result == "ok";
    }

    public async Task<bool> ReloadDispatcherAsync()
    {
        var response = await CallRpcAsync<BaseResponse>("dispatcher.reload");
        return response?.Result == "ok";
    }

    public async Task<KamailioStats> GetStatsAsync()
    {
        var response = await CallRpcAsync<StatsResponse>("stats.get_statistics", "all");
        return new KamailioStats
        {
            ActiveDialogs = response?.GetStat("dialog:active_dialogs") ?? 0,
            ProcessedDialogs = response?.GetStat("dialog:processed_dialogs") ?? 0
        };
    }

    private async Task<T?> CallRpcAsync<T>(string method, params object[] parameters)
    {
        var request = new JsonRpcRequest
        {
            JsonRpc = "2.0",
            Method = method,
            Params = parameters,
            Id = Interlocked.Increment(ref _requestId)
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/RPC", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<JsonRpcResponse<T>>();

            if (result?.Error != null)
            {
                _logger.LogError("Kamailio RPC error: {Error}", result.Error.Message);
                return default;
            }

            return result?.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kamailio RPC call failed: {Method}", method);
            return default;
        }
    }
}

public record JsonRpcRequest
{
    public string JsonRpc { get; init; } = "2.0";
    public string Method { get; init; } = "";
    public object[] Params { get; init; } = Array.Empty<object>();
    public int Id { get; init; }
}

public record JsonRpcResponse<T>
{
    public string JsonRpc { get; init; } = "2.0";
    public T? Result { get; init; }
    public JsonRpcError? Error { get; init; }
    public int Id { get; init; }
}

public record JsonRpcError
{
    public int Code { get; init; }
    public string Message { get; init; } = "";
}
```

### RTPEngine NG Protocol Client (C#)

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

public interface IRtpEngineClient
{
    Task<OfferResponse> OfferAsync(string callId, string fromTag, string sdp, string[] flags);
    Task<AnswerResponse> AnswerAsync(string callId, string fromTag, string toTag, string sdp);
    Task<DeleteResponse> DeleteAsync(string callId, string fromTag);
    Task<PublishResponse> PublishAsync(string callId, string fromTag, string sdp);
    Task<SubscribeResponse> SubscribeRequestAsync(string callId, string[] fromTags, string subscriberLabel);
    Task<QueryResponse> QueryAsync(string callId);
}

public class RtpEngineClient : IRtpEngineClient, IDisposable
{
    private readonly UdpClient _client;
    private readonly IPEndPoint _endpoint;
    private readonly ILogger<RtpEngineClient> _logger;
    private int _cookieCounter = 0;

    public RtpEngineClient(string host, int port, ILogger<RtpEngineClient> logger)
    {
        _client = new UdpClient();
        _endpoint = new IPEndPoint(IPAddress.Parse(host), port);
        _logger = logger;
    }

    public async Task<OfferResponse> OfferAsync(string callId, string fromTag, string sdp, string[] flags)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "offer",
            ["call-id"] = callId,
            ["from-tag"] = fromTag,
            ["sdp"] = sdp
        };

        if (flags.Length > 0)
        {
            command["flags"] = flags;
        }

        var response = await SendCommandAsync(command);
        return JsonSerializer.Deserialize<OfferResponse>(response)
            ?? throw new InvalidOperationException("Invalid offer response");
    }

    public async Task<AnswerResponse> AnswerAsync(string callId, string fromTag, string toTag, string sdp)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "answer",
            ["call-id"] = callId,
            ["from-tag"] = fromTag,
            ["to-tag"] = toTag,
            ["sdp"] = sdp
        };

        var response = await SendCommandAsync(command);
        return JsonSerializer.Deserialize<AnswerResponse>(response)
            ?? throw new InvalidOperationException("Invalid answer response");
    }

    public async Task<DeleteResponse> DeleteAsync(string callId, string fromTag)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "delete",
            ["call-id"] = callId,
            ["from-tag"] = fromTag
        };

        var response = await SendCommandAsync(command);
        return JsonSerializer.Deserialize<DeleteResponse>(response)
            ?? throw new InvalidOperationException("Invalid delete response");
    }

    /// <summary>
    /// Publish a stream for SFU mode (conference participant)
    /// </summary>
    public async Task<PublishResponse> PublishAsync(string callId, string fromTag, string sdp)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "publish",
            ["call-id"] = callId,
            ["from-tag"] = fromTag,
            ["sdp"] = sdp
        };

        var response = await SendCommandAsync(command);
        return JsonSerializer.Deserialize<PublishResponse>(response)
            ?? throw new InvalidOperationException("Invalid publish response");
    }

    /// <summary>
    /// Subscribe to published streams (SFU mode)
    /// </summary>
    public async Task<SubscribeResponse> SubscribeRequestAsync(
        string callId,
        string[] fromTags,
        string subscriberLabel)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "subscribe request",
            ["call-id"] = callId,
            ["from-tags"] = fromTags,
            ["set-label"] = subscriberLabel
        };

        var response = await SendCommandAsync(command);
        return JsonSerializer.Deserialize<SubscribeResponse>(response)
            ?? throw new InvalidOperationException("Invalid subscribe response");
    }

    public async Task<QueryResponse> QueryAsync(string callId)
    {
        var command = new Dictionary<string, object>
        {
            ["command"] = "query",
            ["call-id"] = callId
        };

        var response = await SendCommandAsync(command);
        return JsonSerializer.Deserialize<QueryResponse>(response)
            ?? throw new InvalidOperationException("Invalid query response");
    }

    private async Task<string> SendCommandAsync(Dictionary<string, object> command)
    {
        // Generate unique cookie
        string cookie = $"{Interlocked.Increment(ref _cookieCounter)}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        // Serialize command to JSON
        string jsonCommand = JsonSerializer.Serialize(command);

        // Build message: "cookie json-data"
        string message = $"{cookie} {jsonCommand}";
        byte[] data = Encoding.UTF8.GetBytes(message);

        _logger.LogDebug("RTPEngine command: {Command}", jsonCommand);

        // Send
        await _client.SendAsync(data, data.Length, _endpoint);

        // Receive response (with timeout)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await _client.ReceiveAsync(cts.Token);

        string response = Encoding.UTF8.GetString(result.Buffer);

        // Parse response (skip cookie prefix)
        int spaceIndex = response.IndexOf(' ');
        if (spaceIndex > 0)
        {
            response = response.Substring(spaceIndex + 1);
        }

        _logger.LogDebug("RTPEngine response: {Response}", response);

        return response;
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

// Response models
public record BaseRtpEngineResponse
{
    public string Result { get; init; } = "";
    public string? ErrorReason { get; init; }
    public string? Warning { get; init; }
}

public record OfferResponse : BaseRtpEngineResponse
{
    public string Sdp { get; init; } = "";
}

public record AnswerResponse : BaseRtpEngineResponse
{
    public string Sdp { get; init; } = "";
}

public record DeleteResponse : BaseRtpEngineResponse
{
    public long Created { get; init; }
    public long LastSignal { get; init; }
    public Dictionary<string, object>? Totals { get; init; }
}

public record PublishResponse : BaseRtpEngineResponse
{
    public string Sdp { get; init; } = ""; // RTPEngine-generated recvonly SDP
}

public record SubscribeResponse : BaseRtpEngineResponse
{
    public string Sdp { get; init; } = ""; // sendonly SDP for receiving streams
}

public record QueryResponse : BaseRtpEngineResponse
{
    public Dictionary<string, object>? Streams { get; init; }
}
```

---

## 4. SignalWire PSTN Integration

### Architecture

```
External Phone ──► SignalWire ──► SIP Trunk ──► Kamailio ──► RTPEngine ──► Game Client
                       │
                       │ SWML webhook
                       ▼
                   Bannou API
```

### SignalWire Configuration

1. **Create SIP Endpoint** in SignalWire Space:
   - Navigate to Resources → Add New → SIP Endpoint
   - Set SIP Address (username) and Password
   - Note the endpoint URL: `username@yourspace.signalwire.com`

2. **Configure Phone Number**:
   - Navigate to Phone Numbers → select DID
   - Handle Incoming Calls → SIP Endpoint
   - Select your Kamailio endpoint

### Kamailio SIP Trunk Configuration

```
# SignalWire SIP trunk configuration
# Add to kamailio.cfg

# SignalWire authentication
modparam("uac", "reg_db_url", "")
modparam("uac", "reg_contact_addr", "kamailio.example.com:5060")

# SignalWire peer configuration
route[SIGNALWIRE] {
    # Outbound to SignalWire
    if ($ru =~ "signalwire.com") {
        # Add authentication
        uac_auth();
        t_relay();
        exit;
    }

    # Inbound from SignalWire
    if ($si == "signalwire.com") {
        # Route to internal conference or user
        route(INTERNAL_ROUTING);
        exit;
    }
}

# Dispatcher entry for SignalWire
# /etc/kamailio/dispatcher.list
# setid destination flags priority attributes
2 sip:yourspace.signalwire.com:5060 0 0 socket=tls:yourkamailio.com:5061
```

### SWML Webhook for Routing

```yaml
# SignalWire SWML script for incoming calls
version: 1.0.0
sections:
  main:
    # Notify Bannou of incoming call
    - execute:
        dest: https://bannou.example.com/v1.0/invoke/bannou/method/media/pstn/incoming
        params:
          caller: "%{call.from}"
          did: "%{call.to}"

    # Play greeting
    - play:
        url: "https://bannou.example.com/audio/connecting.mp3"

    # Connect to Kamailio
    - connect:
        to: "sip:support@kamailio.example.com"
        timeout: 30

    # Handle no answer
    - cond:
        when: "%{connect_failed}"
        then:
          - play:
              url: "https://bannou.example.com/audio/unavailable.mp3"
```

### On-Call Moderator Flow

1. **Moderator joins voice room** → Registers with Kamailio, publishes to RTPEngine
2. **External call arrives** → SignalWire → SWML webhook → Kamailio
3. **Kamailio routes call** → RTPEngine subscribes caller to conference
4. **Moderator hears caller** → Through same audio pipeline as other players
5. **Call ends** → Caller hangs up, moderator stays in room

---

## 5. Bannou Plugin Architecture

### Project Structure

```
lib-media/
├── Generated/
│   ├── MediaController.Generated.cs
│   ├── IMediaService.cs
│   ├── MediaModels.cs
│   └── MediaServiceConfiguration.cs
├── MediaService.cs                    # Main orchestration logic
├── MediaServicePlugin.cs              # Plugin registration
├── Clients/
│   ├── KamailioClient.cs              # JSONRPC HTTP client
│   ├── RtpEngineClient.cs             # ng protocol UDP client
│   └── ClientModels.cs                # Request/response models
├── P2P/
│   ├── SipEndpointRegistry.cs         # P2P endpoint tracking
│   └── P2PCoordinator.cs              # Mesh topology management
└── lib-media.csproj
```

### Schema Definition

```yaml
# schemas/media-api.yaml
openapi: 3.0.3
info:
  title: Bannou Media Service
  version: 1.0.0
  description: Voice/video communication for Stride game clients

servers:
  - url: http://localhost:3500/v1.0/invoke/bannou/method

paths:
  /media/room/create:
    post:
      operationId: CreateVoiceRoom
      summary: Create voice room for game session
      x-permissions:
        - context:gameSessionId
        - role:moderator
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateRoomRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/CreateRoomResponse'

  /media/room/{roomId}/join:
    post:
      operationId: JoinVoiceRoom
      summary: Get connection info to join voice room
      parameters:
        - name: roomId
          in: path
          required: true
          schema:
            type: string
            format: uuid
      x-permissions:
        - context:gameSessionId
        - context:accountId
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/JoinRoomRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/JoinRoomResponse'

  /media/room/{roomId}/leave:
    post:
      operationId: LeaveVoiceRoom
      summary: Leave voice room
      parameters:
        - name: roomId
          in: path
          required: true
          schema:
            type: string
            format: uuid
      x-permissions:
        - context:gameSessionId
        - context:accountId
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/LeaveRoomResponse'

  /media/p2p/register:
    post:
      operationId: RegisterP2PEndpoint
      summary: Register endpoint for P2P calls
      x-permissions:
        - context:accountId
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/P2PRegisterRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/P2PRegisterResponse'

  /media/p2p/lookup:
    post:
      operationId: LookupP2PEndpoints
      summary: Lookup endpoints for P2P mesh
      x-permissions:
        - context:accountId
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/P2PLookupRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/P2PLookupResponse'

  /media/pstn/incoming:
    post:
      operationId: HandlePstnIncoming
      summary: Webhook for incoming PSTN calls
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PstnIncomingRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PstnIncomingResponse'

components:
  schemas:
    CreateRoomRequest:
      type: object
      required:
        - session_id
      properties:
        session_id:
          type: string
          format: uuid
        max_participants:
          type: integer
          default: 20
        enable_pstn:
          type: boolean
          default: false
          description: Enable PSTN connectivity (requires Tier 2)

    CreateRoomResponse:
      type: object
      required:
        - room_id
        - tier
      properties:
        room_id:
          type: string
          format: uuid
        tier:
          type: string
          enum: [p2p, scaled]
          description: Voice infrastructure tier
        kamailio_host:
          type: string
          description: Only present for tier=scaled
        sip_domain:
          type: string
          description: Only present for tier=scaled

    JoinRoomRequest:
      type: object
      properties:
        public_ip:
          type: string
          description: Client's public IP for P2P
        rtp_port:
          type: integer
          description: Client's RTP port for P2P
        prefer_p2p:
          type: boolean
          default: true
          description: Prefer P2P if eligible

    JoinRoomResponse:
      type: object
      required:
        - tier
        - participant_id
      properties:
        tier:
          type: string
          enum: [p2p, scaled]
        participant_id:
          type: string
          format: uuid
        # P2P mode fields
        peer_endpoints:
          type: array
          items:
            $ref: '#/components/schemas/PeerEndpoint'
        # Scaled mode fields
        sip_username:
          type: string
        sip_password:
          type: string
        sip_server:
          type: string
        conference_uri:
          type: string

    PeerEndpoint:
      type: object
      properties:
        account_id:
          type: string
          format: uuid
        display_name:
          type: string
        public_ip:
          type: string
        rtp_port:
          type: integer

    LeaveRoomResponse:
      type: object
      properties:
        success:
          type: boolean

    P2PRegisterRequest:
      type: object
      required:
        - public_ip
        - rtp_port
      properties:
        public_ip:
          type: string
        rtp_port:
          type: integer
        display_name:
          type: string

    P2PRegisterResponse:
      type: object
      properties:
        success:
          type: boolean
        expires_in_seconds:
          type: integer

    P2PLookupRequest:
      type: object
      required:
        - account_ids
      properties:
        account_ids:
          type: array
          items:
            type: string
            format: uuid

    P2PLookupResponse:
      type: object
      properties:
        endpoints:
          type: array
          items:
            $ref: '#/components/schemas/PeerEndpoint'

    PstnIncomingRequest:
      type: object
      properties:
        caller:
          type: string
        did:
          type: string

    PstnIncomingResponse:
      type: object
      properties:
        route_to:
          type: string
          description: SIP URI to route to
```

### MediaService Implementation

```csharp
[DaprService("media", typeof(IMediaService), lifetime: ServiceLifetime.Scoped)]
public class MediaService : IMediaService
{
    private readonly DaprClient _daprClient;
    private readonly IKamailioClient _kamailio;
    private readonly IRtpEngineClient _rtpEngine;
    private readonly SipEndpointRegistry _p2pRegistry;
    private readonly ILogger<MediaService> _logger;
    private readonly MediaServiceConfiguration _config;

    private const string STATE_STORE = "media-statestore";
    private const int P2P_MAX_PARTICIPANTS = 6;

    public MediaService(
        DaprClient daprClient,
        IKamailioClient kamailio,
        IRtpEngineClient rtpEngine,
        SipEndpointRegistry p2pRegistry,
        ILogger<MediaService> logger,
        MediaServiceConfiguration config)
    {
        _daprClient = daprClient;
        _kamailio = kamailio;
        _rtpEngine = rtpEngine;
        _p2pRegistry = p2pRegistry;
        _logger = logger;
        _config = config;
    }

    public async Task<(StatusCodes, CreateRoomResponse?)> CreateVoiceRoomAsync(
        CreateRoomRequest request)
    {
        var roomId = Guid.NewGuid();

        // Determine tier based on requirements
        var tier = DetermineTier(request);

        var roomState = new RoomState
        {
            RoomId = roomId,
            SessionId = request.SessionId,
            Tier = tier,
            MaxParticipants = request.MaxParticipants,
            PstnEnabled = request.EnablePstn,
            Participants = new List<RoomParticipant>(),
            CreatedAt = DateTime.UtcNow
        };

        // For scaled tier, create conference in RTPEngine
        if (tier == VoiceTier.Scaled)
        {
            roomState.ConferenceCallId = $"conf-{roomId:N}";
        }

        await _daprClient.SaveStateAsync(STATE_STORE, $"room:{roomId}", roomState);

        // Publish room created event
        await _daprClient.PublishEventAsync(
            "bannou-pubsub",
            "media.room.created",
            new VoiceRoomCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                SessionId = request.SessionId,
                RoomId = roomId,
                Tier = tier.ToString()
            });

        return (StatusCodes.Status200OK, new CreateRoomResponse
        {
            RoomId = roomId,
            Tier = tier.ToString().ToLower(),
            KamailioHost = tier == VoiceTier.Scaled ? _config.KamailioHost : null,
            SipDomain = tier == VoiceTier.Scaled ? _config.SipDomain : null
        });
    }

    public async Task<(StatusCodes, JoinRoomResponse?)> JoinVoiceRoomAsync(
        Guid roomId,
        JoinRoomRequest request,
        Guid accountId)
    {
        var roomState = await _daprClient.GetStateAsync<RoomState>(
            STATE_STORE, $"room:{roomId}");

        if (roomState == null)
            return (StatusCodes.Status404NotFound, null);

        var participantId = Guid.NewGuid();

        // Check if we should upgrade to scaled tier
        if (roomState.Tier == VoiceTier.P2P &&
            roomState.Participants.Count >= P2P_MAX_PARTICIPANTS)
        {
            await UpgradeToScaledTier(roomState);
        }

        if (roomState.Tier == VoiceTier.P2P)
        {
            return await JoinP2PRoomAsync(roomState, request, accountId, participantId);
        }
        else
        {
            return await JoinScaledRoomAsync(roomState, request, accountId, participantId);
        }
    }

    private async Task<(StatusCodes, JoinRoomResponse?)> JoinP2PRoomAsync(
        RoomState roomState,
        JoinRoomRequest request,
        Guid accountId,
        Guid participantId)
    {
        // Register endpoint
        _p2pRegistry.Register(accountId, request.PublicIp!, request.RtpPort!.Value);

        // Add participant to room
        roomState.Participants.Add(new RoomParticipant
        {
            ParticipantId = participantId,
            AccountId = accountId,
            JoinedAt = DateTime.UtcNow
        });

        await _daprClient.SaveStateAsync(STATE_STORE, $"room:{roomState.RoomId}", roomState);

        // Get all peer endpoints for mesh
        var peerEndpoints = roomState.Participants
            .Where(p => p.AccountId != accountId)
            .Select(p => _p2pRegistry.Lookup(p.AccountId))
            .Where(e => e != null)
            .Select(e => new PeerEndpoint
            {
                AccountId = e!.AccountId,
                PublicIp = e.PublicIp,
                RtpPort = e.RtpPort
            })
            .ToList();

        // Notify other participants about new peer
        await _daprClient.PublishEventAsync(
            "bannou-pubsub",
            "media.participant.joined",
            new ParticipantJoinedEvent
            {
                RoomId = roomState.RoomId,
                ParticipantId = participantId,
                AccountId = accountId,
                Tier = "p2p"
            });

        return (StatusCodes.Status200OK, new JoinRoomResponse
        {
            Tier = "p2p",
            ParticipantId = participantId,
            PeerEndpoints = peerEndpoints
        });
    }

    private async Task<(StatusCodes, JoinRoomResponse?)> JoinScaledRoomAsync(
        RoomState roomState,
        JoinRoomRequest request,
        Guid accountId,
        Guid participantId)
    {
        // Generate SIP credentials
        var sipUsername = $"player-{accountId:N}";
        var sipPassword = GenerateSipPassword(accountId, roomState.RoomId);

        // Add participant
        roomState.Participants.Add(new RoomParticipant
        {
            ParticipantId = participantId,
            AccountId = accountId,
            SipUsername = sipUsername,
            PublisherTag = $"pub-{accountId:N}",
            JoinedAt = DateTime.UtcNow
        });

        await _daprClient.SaveStateAsync(STATE_STORE, $"room:{roomState.RoomId}", roomState);

        // Publish event
        await _daprClient.PublishEventAsync(
            "bannou-pubsub",
            "media.participant.joined",
            new ParticipantJoinedEvent
            {
                RoomId = roomState.RoomId,
                ParticipantId = participantId,
                AccountId = accountId,
                Tier = "scaled"
            });

        return (StatusCodes.Status200OK, new JoinRoomResponse
        {
            Tier = "scaled",
            ParticipantId = participantId,
            SipUsername = sipUsername,
            SipPassword = sipPassword,
            SipServer = _config.KamailioHost,
            ConferenceUri = $"sip:conf-{roomState.SessionId:N}@{_config.SipDomain}"
        });
    }

    public async Task<(StatusCodes, PstnIncomingResponse?)> HandlePstnIncomingAsync(
        PstnIncomingRequest request)
    {
        _logger.LogInformation("PSTN call from {Caller} to {Did}", request.Caller, request.Did);

        // Find room with PSTN enabled and on-call moderator
        // This is a simplified example - real implementation would have routing logic

        await _daprClient.PublishEventAsync(
            "bannou-pubsub",
            "media.pstn.incoming",
            new PstnIncomingEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTime.UtcNow,
                CallerNumber = request.Caller,
                Did = request.Did
            });

        return (StatusCodes.Status200OK, new PstnIncomingResponse
        {
            RouteTo = $"sip:support@{_config.SipDomain}"
        });
    }

    private VoiceTier DetermineTier(CreateRoomRequest request)
    {
        // Force scaled tier if PSTN is needed
        if (request.EnablePstn)
            return VoiceTier.Scaled;

        // Large rooms need scaled infrastructure
        if (request.MaxParticipants > P2P_MAX_PARTICIPANTS)
            return VoiceTier.Scaled;

        // Default to P2P for small rooms
        return VoiceTier.P2P;
    }

    private async Task UpgradeToScaledTier(RoomState roomState)
    {
        _logger.LogInformation("Upgrading room {RoomId} from P2P to Scaled tier", roomState.RoomId);

        roomState.Tier = VoiceTier.Scaled;
        roomState.ConferenceCallId = $"conf-{roomState.RoomId:N}";

        // Notify all participants to reconnect via scaled infrastructure
        await _daprClient.PublishEventAsync(
            "bannou-pubsub",
            "media.room.tier-upgrade",
            new RoomTierUpgradeEvent
            {
                RoomId = roomState.RoomId,
                NewTier = "scaled",
                KamailioHost = _config.KamailioHost,
                SipDomain = _config.SipDomain
            });
    }

    private string GenerateSipPassword(Guid accountId, Guid roomId)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var input = $"{accountId}:{roomId}:{_config.SipPasswordSalt}";
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hash).Substring(0, 20);
    }
}

public enum VoiceTier
{
    P2P,
    Scaled
}
```

---

## 6. Stride Engine Integration

### Audio: DynamicSoundSource with Multi-Stream Mixing

```csharp
using Stride.Audio;
using System.Collections.Concurrent;

/// <summary>
/// Audio source that receives multiple RTP streams and mixes them for Stride
/// </summary>
public class MultiStreamVoipSource : DynamicSoundSource
{
    private readonly ConcurrentDictionary<uint, StreamBuffer> _streams = new();
    private readonly short[] _mixBuffer;

    public MultiStreamVoipSource() : base(
        numberOfChannels: 1,
        sampleRate: 48000,
        maxBufferSize: 48000 * 2)
    {
        _mixBuffer = new short[960]; // 20ms frame
    }

    /// <summary>
    /// Enqueue samples from a specific stream (identified by SSRC)
    /// </summary>
    public void EnqueuePcmSamples(short[] samples, uint ssrc)
    {
        var buffer = _streams.GetOrAdd(ssrc, _ => new StreamBuffer());
        buffer.Enqueue(samples);
    }

    /// <summary>
    /// Remove a stream (player left)
    /// </summary>
    public void RemoveStream(uint ssrc)
    {
        _streams.TryRemove(ssrc, out _);
    }

    protected override void FillBuffer(Span<short> buffer, int sampleRate, int channels)
    {
        // Clear mix buffer
        Array.Clear(_mixBuffer, 0, _mixBuffer.Length);

        int samplesWritten = 0;
        int activeStreams = 0;

        while (samplesWritten < buffer.Length)
        {
            // Mix all active streams
            foreach (var kvp in _streams)
            {
                var streamBuffer = kvp.Value;
                if (streamBuffer.TryDequeue(_mixBuffer.Length - samplesWritten, out var samples))
                {
                    // Mix into buffer (simple additive mixing with clipping)
                    for (int i = 0; i < samples.Length; i++)
                    {
                        int mixed = _mixBuffer[samplesWritten + i] + samples[i];
                        _mixBuffer[samplesWritten + i] = (short)Math.Clamp(mixed, short.MinValue, short.MaxValue);
                    }
                    activeStreams++;
                }
            }

            if (activeStreams == 0)
            {
                // No audio available - fill with silence
                buffer.Slice(samplesWritten).Fill(0);
                return;
            }

            // Copy mixed audio to output
            _mixBuffer.AsSpan(0, Math.Min(_mixBuffer.Length, buffer.Length - samplesWritten))
                .CopyTo(buffer.Slice(samplesWritten));

            samplesWritten += _mixBuffer.Length;
            activeStreams = 0;
        }
    }

    private class StreamBuffer
    {
        private readonly ConcurrentQueue<short[]> _queue = new();
        private short[]? _current;
        private int _position;

        public void Enqueue(short[] samples)
        {
            _queue.Enqueue(samples);
        }

        public bool TryDequeue(int count, out short[] samples)
        {
            samples = new short[count];
            int written = 0;

            while (written < count)
            {
                if (_current == null || _position >= _current.Length)
                {
                    if (!_queue.TryDequeue(out _current))
                    {
                        if (written == 0)
                            return false;
                        break;
                    }
                    _position = 0;
                }

                int toCopy = Math.Min(count - written, _current.Length - _position);
                Array.Copy(_current, _position, samples, written, toCopy);
                written += toCopy;
                _position += toCopy;
            }

            return true;
        }
    }
}
```

### Spatial Audio Per-Player

For spatial audio, use separate `VoipAudioSource` per player with `AudioEmitter`:

```csharp
public class SpatialVoiceManager : SyncScript
{
    private readonly Dictionary<Guid, SpatialVoicePlayer> _players = new();
    private P2PVoiceClient? _voiceClient;

    public override void Start()
    {
        // Voice client setup happens in game networking
    }

    public void OnPlayerJoined(Guid accountId, Entity playerEntity)
    {
        var audioSource = new VoipAudioSource();
        var voicePlayer = new SpatialVoicePlayer
        {
            AccountId = accountId,
            AudioSource = audioSource,
            Emitter = playerEntity.Get<AudioEmitterComponent>(),
            SoundInstance = CreateSpatialSound(audioSource)
        };

        _players[accountId] = voicePlayer;
        voicePlayer.SoundInstance.Play();
    }

    public void OnPlayerLeft(Guid accountId)
    {
        if (_players.TryGetValue(accountId, out var player))
        {
            player.SoundInstance.Stop();
            _players.Remove(accountId);
        }
    }

    public void OnAudioReceived(Guid accountId, short[] pcmSamples)
    {
        if (_players.TryGetValue(accountId, out var player))
        {
            player.AudioSource.EnqueuePcmSamples(pcmSamples);
        }
    }

    public override void Update()
    {
        // Update spatial positions
        foreach (var player in _players.Values)
        {
            if (player.Emitter != null)
            {
                player.SoundInstance.Apply3D(player.Emitter.AudioEmitter);
            }
        }
    }

    private SoundInstance CreateSpatialSound(VoipAudioSource source)
    {
        return new SoundInstance(
            Audio.AudioEngine,
            Audio.AudioEngine.DefaultListener,
            source,
            sampleRate: 48000,
            mono: true,
            spatialized: true);
    }

    private class SpatialVoicePlayer
    {
        public Guid AccountId { get; set; }
        public VoipAudioSource AudioSource { get; set; } = null!;
        public AudioEmitterComponent? Emitter { get; set; }
        public SoundInstance SoundInstance { get; set; } = null!;
    }
}
```

---

## 7. Event Schemas

```yaml
# schemas/media-events.yaml

VoiceRoomCreatedEvent:
  type: object
  required:
    - event_id
    - timestamp
    - session_id
    - room_id
    - tier
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    session_id:
      type: string
      format: uuid
    room_id:
      type: string
      format: uuid
    tier:
      type: string
      enum: [p2p, scaled]

ParticipantJoinedEvent:
  type: object
  required:
    - event_id
    - timestamp
    - room_id
    - participant_id
    - account_id
    - tier
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    room_id:
      type: string
      format: uuid
    participant_id:
      type: string
      format: uuid
    account_id:
      type: string
      format: uuid
    tier:
      type: string
      enum: [p2p, scaled]
    # P2P specific
    public_ip:
      type: string
    rtp_port:
      type: integer

ParticipantLeftEvent:
  type: object
  required:
    - event_id
    - timestamp
    - room_id
    - participant_id
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    room_id:
      type: string
      format: uuid
    participant_id:
      type: string
      format: uuid
    reason:
      type: string
      enum: [left, disconnected, kicked]

RoomTierUpgradeEvent:
  type: object
  required:
    - event_id
    - timestamp
    - room_id
    - new_tier
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    room_id:
      type: string
      format: uuid
    new_tier:
      type: string
      enum: [scaled]
    kamailio_host:
      type: string
    sip_domain:
      type: string

PstnIncomingEvent:
  type: object
  properties:
    event_id:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    caller_number:
      type: string
    did:
      type: string
    room_id:
      type: string
      format: uuid
```

---

## 8. Docker Deployment

### Docker Compose

```yaml
# docker-compose.media.yml
version: '3.8'

services:
  kamailio:
    image: ghcr.io/kamailio/kamailio:latest
    container_name: bannou-kamailio
    network_mode: host  # Required for SIP
    volumes:
      - ./provisioning/kamailio/kamailio.cfg:/etc/kamailio/kamailio.cfg:ro
      - ./provisioning/kamailio/dispatcher.list:/etc/kamailio/dispatcher.list:ro
    environment:
      - KAMAILIO_DEBUG_LEVEL=2
    restart: unless-stopped

  rtpengine:
    image: drachtio/rtpengine:latest
    container_name: bannou-rtpengine
    network_mode: host  # Required for RTP
    privileged: true    # Required for kernel module
    volumes:
      - ./provisioning/rtpengine/rtpengine.conf:/etc/rtpengine/rtpengine.conf:ro
    environment:
      - TABLE=0
    restart: unless-stopped
```

### Production Recommendations

1. **Host Networking**: Required for both Kamailio and RTPEngine
2. **Kernel Module**: Enable for RTPEngine (10x performance)
3. **TLS**: Configure for SignalWire trunk and WebSocket clients
4. **Monitoring**: Export metrics to Prometheus
5. **Scaling**: Multiple RTPEngine instances with Kamailio load balancing

---

## 9. Client-Side Flows

This section describes the complete client-side experience for establishing voice communication in both tiers, from the Stride game client's perspective.

### Game Session Integration Overview

Voice can be **automatically established during the normal game session join flow**. The GameSession service coordinates with MediaService internally, so clients receive voice connection info as part of the standard `JoinGameSessionResponse`.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                   VOICE-ENABLED GAME SESSION JOIN FLOW                       │
└─────────────────────────────────────────────────────────────────────────────┘

  Stride Client              GameSession Service         Media Service
       │                            │                          │
       │  POST /sessions/join       │                          │
       │───────────────────────────>│                          │
       │                            │                          │
       │                            │  JoinVoiceRoomAsync()    │
       │                            │─────────────────────────>│
       │                            │                          │
       │                            │  VoiceConnectionInfo     │
       │                            │<─────────────────────────│
       │                            │                          │
       │  JoinGameSessionResponse   │                          │
       │  + voice: {...}            │                          │
       │<───────────────────────────│                          │
       │                            │                          │
       │  (Client uses voice info   │                          │
       │   to establish media)      │                          │
```

### Extended JoinGameSessionResponse Schema

```yaml
# Addition to schemas/game-session-api.yaml

JoinGameSessionResponse:
  type: object
  required:
    - success
    - sessionId
    - playerRole
  properties:
    success:
      type: boolean
    sessionId:
      type: string
    playerRole:
      type: string
      enum: [player, spectator, moderator]
    gameData:
      type: object
    newPermissions:
      type: array
      items:
        type: string
    # Voice connection info - present if voice is enabled for this session
    voice:
      $ref: '#/components/schemas/VoiceConnectionInfo'
      nullable: true

VoiceConnectionInfo:
  type: object
  required:
    - room_id
    - tier
    - enabled
  properties:
    room_id:
      type: string
      format: uuid
    tier:
      type: string
      enum: [p2p, scaled]
    enabled:
      type: boolean
    # P2P tier fields
    p2p_config:
      type: object
      nullable: true
      properties:
        stun_servers:
          type: array
          items:
            type: string
        turn_servers:
          type: array
          items:
            $ref: '#/components/schemas/TurnServer'
        current_peers:
          type: array
          items:
            $ref: '#/components/schemas/VoiceParticipant'
    # Scaled tier fields
    sip_credentials:
      $ref: '#/components/schemas/SipCredentials'
      nullable: true

TurnServer:
  type: object
  properties:
    url:
      type: string
    username:
      type: string
    credential:
      type: string

SipCredentials:
  type: object
  required:
    - registrar
    - username
    - password
    - conference_uri
  properties:
    registrar:
      type: string
      description: SIP registrar hostname (e.g., sip.bannou.example.com)
    username:
      type: string
      description: SIP username for this participant
    password:
      type: string
      description: Temporary SIP password (valid for session duration)
    conference_uri:
      type: string
      description: SIP URI for the conference room
    expires_at:
      type: string
      format: date-time
      description: When credentials expire (session end + buffer)

VoiceParticipant:
  type: object
  required:
    - participant_id
    - display_name
  properties:
    participant_id:
      type: string
      format: uuid
    account_id:
      type: string
      format: uuid
    display_name:
      type: string
    endpoint:
      type: string
      nullable: true
      description: IP:port for P2P tier, null for scaled
    is_muted:
      type: boolean
      default: false
    is_speaking:
      type: boolean
      default: false
```

---

### P2P Tier: Complete Client Flow

#### Step-by-Step Sequence

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        P2P TIER CLIENT FLOW                                  │
└─────────────────────────────────────────────────────────────────────────────┘

  Stride Client                    Bannou Services                   Other Peers
       │                                 │                                │
       │  1. POST /sessions/join         │                                │
       │────────────────────────────────>│                                │
       │                                 │                                │
       │  2. Response: tier="p2p"        │                                │
       │     voice.p2p_config: {...}     │                                │
       │<────────────────────────────────│                                │
       │                                 │                                │
       │  3. Create RTPSession           │                                │
       │     Bind local UDP socket       │                                │
       │     (SIPSorcery)                │                                │
       │                                 │                                │
       │  4. POST /media/p2p/register    │                                │
       │     { roomId, endpoint }        │                                │
       │────────────────────────────────>│                                │
       │                                 │                                │
       │  5. 200 OK                      │                                │
       │<────────────────────────────────│                                │
       │                                 │                                │
       │  6. WS: media.room_state        │                                │
       │     All current peer endpoints  │                                │
       │<════════════════════════════════│                                │
       │                                 │                                │
       │  7. For each peer:              │                                │
       │     Connect RTP stream ─────────┼───────────────────────────────>│
       │                                 │                                │
       │  8. Start sending/receiving     │                                │
       │     audio (Opus over RTP)       │                                │
       │<════════════════════════════════┼════════════════════════════════>│
       │                                 │                                │
       │  ══════════════════════════════════════════════════════════════  │
       │              P2P VOICE ESTABLISHED - FULL MESH                    │
       │  ══════════════════════════════════════════════════════════════  │
       │                                 │                                │
       │  (Ongoing) WS: media.peer_joined│                                │
       │  → Add new RTP connection       │                                │
       │<════════════════════════════════│                                │
       │                                 │                                │
       │  (Ongoing) WS: media.peer_left  │                                │
       │  → Remove RTP connection        │                                │
       │<════════════════════════════════│                                │
```

#### Client Implementation (Stride)

```csharp
public class VoiceManager : IDisposable
{
    private readonly IMediaClient _mediaClient;
    private readonly WebSocketConnection _websocket;
    private P2PVoiceClient? _p2pClient;
    private ScaledVoiceClient? _scaledClient;
    private VoiceTier _currentTier;
    private Guid _roomId;

    /// <summary>
    /// Initialize voice from game session join response
    /// </summary>
    public async Task InitializeAsync(JoinGameSessionResponse response)
    {
        if (response.Voice == null || !response.Voice.Enabled)
            return;

        _roomId = response.Voice.RoomId;
        _currentTier = Enum.Parse<VoiceTier>(response.Voice.Tier, true);

        // Subscribe to voice events
        _websocket.On<MediaRoomStateEvent>("media.room_state", OnRoomState);
        _websocket.On<MediaPeerJoinedEvent>("media.peer_joined", OnPeerJoined);
        _websocket.On<MediaPeerLeftEvent>("media.peer_left", OnPeerLeft);
        _websocket.On<MediaTierUpgradeEvent>("media.tier_upgrade", OnTierUpgrade);

        if (_currentTier == VoiceTier.P2P)
        {
            await InitializeP2PAsync(response.Voice);
        }
        else
        {
            await InitializeScaledAsync(response.Voice);
        }
    }

    private async Task InitializeP2PAsync(VoiceConnectionInfo voiceInfo)
    {
        // 1. Create P2P voice client (binds UDP socket)
        _p2pClient = new P2PVoiceClient(_audioSource);

        // 2. Register our endpoint with MediaService
        await _mediaClient.RegisterP2PEndpointAsync(new P2PRegisterRequest
        {
            RoomId = _roomId,
            PublicIp = GetPublicIp(),
            RtpPort = _p2pClient.LocalPort
        });

        // 3. Connect to existing peers (from join response)
        if (voiceInfo.P2PConfig?.CurrentPeers != null)
        {
            foreach (var peer in voiceInfo.P2PConfig.CurrentPeers)
            {
                if (peer.Endpoint != null)
                {
                    var parts = peer.Endpoint.Split(':');
                    _p2pClient.ConnectToPeer(peer.AccountId, parts[0], int.Parse(parts[1]));
                }
            }
        }
    }
}
```

---

### Scaled Tier: Complete Client Flow

#### Step-by-Step Sequence

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      SCALED TIER CLIENT FLOW                                 │
└─────────────────────────────────────────────────────────────────────────────┘

  Stride Client                    Bannou                      Kamailio/RTPEngine
       │                             │                                │
       │  1. POST /sessions/join     │                                │
       │────────────────────────────>│                                │
       │                             │                                │
       │  2. Response: tier="scaled" │                                │
       │     sip_credentials: {...}  │                                │
       │<────────────────────────────│                                │
       │                             │                                │
       │  3. Create SIPUserAgent     │                                │
       │     (SIPSorcery)            │                                │
       │                             │                                │
       │  4. SIP REGISTER ───────────┼───────────────────────────────>│
       │     (with credentials)      │                                │
       │                             │                                │
       │  5. 200 OK ─────────────────┼────────────────────────────────│
       │<────────────────────────────┼────────────────────────────────│
       │                             │                                │
       │  6. SIP INVITE ─────────────┼───────────────────────────────>│
       │     (conference_uri)        │                         Kamailio
       │                             │                                │
       │                             │  7. Kamailio routes to RTPEngine
       │                             │                                │
       │  8. 200 OK (SDP answer) ────┼────────────────────────────────│
       │<────────────────────────────┼────────────────────────────────│
       │                             │                                │
       │  9. Connect RTP to RTPEngine media port                      │
       │     (Single stream - SFU handles distribution)               │
       │<════════════════════════════┼════════════════════════════════>│
       │                             │                                │
       │  ══════════════════════════════════════════════════════════  │
       │              SCALED VOICE ESTABLISHED - SFU MODE              │
       │  ══════════════════════════════════════════════════════════  │
       │                             │                                │
       │  (Ongoing) WS events for UI only                             │
       │  Media routing handled by RTPEngine                          │
```

#### Client Implementation (Stride)

```csharp
private async Task InitializeScaledAsync(VoiceConnectionInfo voiceInfo)
{
    var creds = voiceInfo.SipCredentials
        ?? throw new InvalidOperationException("Missing SIP credentials for scaled tier");

    _scaledClient = new ScaledVoiceClient(_audioSource);
    await _scaledClient.ConnectAsync(creds);
}

public class ScaledVoiceClient : IDisposable
{
    private readonly SIPTransport _sipTransport;
    private readonly SIPUserAgent _userAgent;
    private VoIPMediaSession? _mediaSession;
    private SIPCall? _conferenceCall;

    public async Task ConnectAsync(SipCredentials creds)
    {
        // 1. Create SIP transport
        _sipTransport = new SIPTransport();

        // 2. Create and start registration
        var regClient = new SIPRegistrationUserAgent(
            _sipTransport,
            creds.Username,
            creds.Password,
            creds.Registrar);

        var regTask = new TaskCompletionSource<bool>();
        regClient.RegistrationSuccessful += (uri, resp) => regTask.TrySetResult(true);
        regClient.RegistrationFailed += (uri, resp, err) => regTask.TrySetResult(false);

        await regClient.Start();

        if (!await regTask.Task)
            throw new VoiceConnectionException("SIP registration failed");

        // 3. Create media session with Opus
        _mediaSession = new VoIPMediaSession();
        var audioFormat = new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.OPUS);
        _mediaSession.AddTrack(new MediaStreamTrack(audioFormat));

        // 4. INVITE the conference
        _userAgent = new SIPUserAgent(_sipTransport, null);
        _conferenceCall = await _userAgent.Call(creds.ConferenceUri, null, null, _mediaSession);

        // 5. Handle incoming audio from RTPEngine (pre-mixed or SFU streams)
        _mediaSession.OnRtpPacketReceived += OnRtpReceived;
    }

    private void OnRtpReceived(IPEndPoint ep, SDPMediaTypesEnum mediaType, RTPPacket packet)
    {
        // RTPEngine handles stream distribution
        // In SFU mode, we receive separate streams for each participant
        // In MCU mode (if configured), we receive a single mixed stream
        var samples = _opusDecoder.Decode(packet.Payload);
        _audioOutput.EnqueueSamples(samples, packet.Header.SyncSource);
    }

    public void SendAudio(short[] samples)
    {
        var encoded = _opusEncoder.Encode(samples);
        _mediaSession?.SendRtpPacket(encoded);
    }

    public async Task DisconnectAsync()
    {
        if (_conferenceCall != null)
        {
            await _conferenceCall.Hangup();
        }
        _sipTransport?.Shutdown();
    }
}
```

---

## 10. Client Events for Voice

Voice events follow the standard Bannou client event pattern, delivered via WebSocket through RabbitMQ channels.

### Event Schema (media-client-events.yaml)

```yaml
# schemas/media-client-events.yaml
openapi: 3.0.3
info:
  title: Media Client Events API
  description: |
    Server-to-client push events for voice/media communication.
    Delivered via WebSocket through RabbitMQ channels (CONNECT_SESSION_{sessionId}).
  version: 1.0.0

components:
  schemas:
    # ============================================
    # Room State Events
    # ============================================

    MediaRoomStateEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      description: |
        Sent when client joins a voice room, providing complete room state.
        Also sent after tier upgrades to confirm new connection state.
      required:
        - event_name
        - event_id
        - timestamp
        - room_id
        - tier
        - participants
      properties:
        event_name:
          type: string
          enum: ["media.room_state"]
        room_id:
          type: string
          format: uuid
        tier:
          type: string
          enum: [p2p, scaled]
        participants:
          type: array
          items:
            $ref: '#/components/schemas/VoiceParticipant'
        # P2P specific - endpoints for direct connection
        peer_endpoints:
          type: array
          items:
            $ref: '#/components/schemas/PeerEndpoint'
          nullable: true
        # Scaled specific - SIP info (only if reconnecting)
        sip_credentials:
          $ref: '#/components/schemas/SipCredentials'
          nullable: true

    # ============================================
    # Peer Lifecycle Events
    # ============================================

    MediaPeerJoinedEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      description: |
        Sent to all room participants when a new peer joins.
        For P2P: includes endpoint for direct RTP connection.
        For Scaled: informational only (RTPEngine handles routing).
      required:
        - event_name
        - event_id
        - timestamp
        - room_id
        - participant
      properties:
        event_name:
          type: string
          enum: ["media.peer_joined"]
        room_id:
          type: string
          format: uuid
        participant:
          $ref: '#/components/schemas/VoiceParticipant'
        # P2P specific
        endpoint:
          type: string
          nullable: true
          description: IP:port for P2P direct connection

    MediaPeerLeftEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      required:
        - event_name
        - event_id
        - timestamp
        - room_id
        - participant_id
      properties:
        event_name:
          type: string
          enum: ["media.peer_left"]
        room_id:
          type: string
          format: uuid
        participant_id:
          type: string
          format: uuid
        display_name:
          type: string
          nullable: true
        reason:
          type: string
          enum: [left, disconnected, kicked]
          default: left

    # ============================================
    # Tier Management Events
    # ============================================

    MediaTierUpgradeEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      description: |
        Sent when room upgrades from P2P to Scaled tier.
        Client must establish Scaled connection BEFORE disconnecting P2P.
        See Section 11 for clean upgrade protocol.
      required:
        - event_name
        - event_id
        - timestamp
        - room_id
        - new_tier
        - sip_credentials
      properties:
        event_name:
          type: string
          enum: ["media.tier_upgrade"]
        room_id:
          type: string
          format: uuid
        new_tier:
          type: string
          enum: [scaled]
        sip_credentials:
          $ref: '#/components/schemas/SipCredentials'
        reason:
          type: string
          description: Why upgrade occurred
          enum: [participant_threshold, pstn_enabled, nat_issues, manual]
        upgrade_deadline:
          type: string
          format: date-time
          nullable: true
          description: When P2P connections will be terminated (grace period)

    MediaTierUpgradeCompleteEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      description: |
        Sent after all participants have migrated to scaled tier.
        Clients should now disconnect any remaining P2P connections.
      required:
        - event_name
        - event_id
        - timestamp
        - room_id
      properties:
        event_name:
          type: string
          enum: ["media.tier_upgrade_complete"]
        room_id:
          type: string
          format: uuid
        migrated_participants:
          type: array
          items:
            type: string
            format: uuid
          description: Participant IDs that successfully migrated

    # ============================================
    # Voice Activity Events
    # ============================================

    MediaSpeakingChangedEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      description: |
        Sent when a participant starts or stops speaking.
        Used for UI indicators (speaking icons, spatial cues).
      required:
        - event_name
        - event_id
        - timestamp
        - room_id
        - participant_id
        - is_speaking
      properties:
        event_name:
          type: string
          enum: ["media.speaking_changed"]
        room_id:
          type: string
          format: uuid
        participant_id:
          type: string
          format: uuid
        is_speaking:
          type: boolean
        audio_level:
          type: number
          minimum: 0
          maximum: 1
          description: Normalized audio level (0-1)

    MediaMuteChangedEvent:
      allOf:
        - $ref: 'common-client-events.yaml#/components/schemas/BaseClientEvent'
      type: object
      x-client-event: true
      required:
        - event_name
        - event_id
        - timestamp
        - room_id
        - participant_id
        - is_muted
      properties:
        event_name:
          type: string
          enum: ["media.mute_changed"]
        room_id:
          type: string
          format: uuid
        participant_id:
          type: string
          format: uuid
        is_muted:
          type: boolean
        muted_by:
          type: string
          format: uuid
          nullable: true
          description: Who muted (if muted by moderator)

    # ============================================
    # Supporting Types
    # ============================================

    VoiceParticipant:
      type: object
      required:
        - participant_id
        - account_id
        - display_name
      properties:
        participant_id:
          type: string
          format: uuid
        account_id:
          type: string
          format: uuid
        display_name:
          type: string
        is_muted:
          type: boolean
          default: false
        is_speaking:
          type: boolean
          default: false
        joined_at:
          type: string
          format: date-time

    PeerEndpoint:
      type: object
      properties:
        participant_id:
          type: string
          format: uuid
        account_id:
          type: string
          format: uuid
        public_ip:
          type: string
        rtp_port:
          type: integer

    SipCredentials:
      type: object
      required:
        - registrar
        - username
        - password
        - conference_uri
      properties:
        registrar:
          type: string
        username:
          type: string
        password:
          type: string
        conference_uri:
          type: string
        expires_at:
          type: string
          format: date-time
```

### Client Event Handler Implementation

```csharp
public partial class VoiceManager
{
    private void OnRoomState(MediaRoomStateEvent evt)
    {
        _logger.LogInformation("Voice room state: {Tier}, {Count} participants",
            evt.Tier, evt.Participants.Count);

        // Update local participant list
        _participants.Clear();
        foreach (var p in evt.Participants)
        {
            _participants[p.ParticipantId] = p;
        }

        // For P2P: connect to any peers we haven't connected to yet
        if (_currentTier == VoiceTier.P2P && evt.PeerEndpoints != null)
        {
            foreach (var peer in evt.PeerEndpoints)
            {
                if (!_p2pClient!.IsConnectedTo(peer.AccountId))
                {
                    _p2pClient.ConnectToPeer(peer.AccountId, peer.PublicIp, peer.RtpPort);
                }
            }
        }
    }

    private void OnPeerJoined(MediaPeerJoinedEvent evt)
    {
        _logger.LogInformation("Peer joined: {DisplayName}", evt.Participant.DisplayName);
        _participants[evt.Participant.ParticipantId] = evt.Participant;

        // For P2P: establish direct connection
        if (_currentTier == VoiceTier.P2P && evt.Endpoint != null)
        {
            var parts = evt.Endpoint.Split(':');
            _p2pClient!.ConnectToPeer(evt.Participant.AccountId, parts[0], int.Parse(parts[1]));
        }

        // UI notification
        OnParticipantJoined?.Invoke(evt.Participant);
    }

    private void OnPeerLeft(MediaPeerLeftEvent evt)
    {
        _logger.LogInformation("Peer left: {ParticipantId}", evt.ParticipantId);
        _participants.Remove(evt.ParticipantId);

        // For P2P: disconnect stream
        if (_currentTier == VoiceTier.P2P)
        {
            // Find accountId from participant
            _p2pClient!.DisconnectPeer(evt.ParticipantId);
        }

        OnParticipantLeft?.Invoke(evt.ParticipantId);
    }

    private async void OnTierUpgrade(MediaTierUpgradeEvent evt)
    {
        _logger.LogInformation("Tier upgrade to {NewTier}, reason: {Reason}",
            evt.NewTier, evt.Reason);

        // IMPORTANT: Connect to scaled tier FIRST, then disconnect P2P
        // This ensures no voice data loss during transition
        await PerformCleanTierUpgradeAsync(evt);
    }

    // Events for UI
    public event Action<VoiceParticipant>? OnParticipantJoined;
    public event Action<Guid>? OnParticipantLeft;
    public event Action<Guid, bool>? OnSpeakingChanged;
    public event Action<VoiceTier>? OnTierChanged;
}
```

---

## 11. Clean Tier Upgrade Strategy

> **⚠️ IMPORTANT: This upgrade path is optional and configuration-dependent**
>
> The smooth P2P → Scaled upgrade is only relevant when **both tiers are enabled** (`P2PEnabled=true` AND `ScaledEnabled=true`). Deployments may run:
> - **P2P only**: No upgrade path exists (rooms reject participants beyond limit)
> - **Scaled only**: All rooms start on Scaled tier (no P2P to upgrade from)
> - **Both tiers**: Full upgrade protocol applies
>
> **We want the smooth upgrade path, but we are not beholden to it.** If Scaled infrastructure is unavailable when upgrade is triggered, the system gracefully handles the failure and continues on P2P.

Upgrading from P2P to Scaled tier must be handled carefully to ensure **zero voice data loss**. The client maintains both connections simultaneously until migration is confirmed.

### Key Principles

1. **Connect First, Disconnect Last**: Establish Scaled connection BEFORE disconnecting P2P
2. **Wait for Peers**: Keep P2P active until you see peers also arrive on Scaled (via events)
3. **De-duplicate During Overlap**: Same audio may arrive from both paths during transition
4. **Graceful Fallback**: If Scaled connection fails, continue on P2P with logged error
5. **Server Coordination**: Server tracks who has migrated and sends `tier_upgrade_complete` when all peers are ready

### Upgrade Protocol

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     CLEAN P2P → SCALED UPGRADE PROTOCOL                      │
└─────────────────────────────────────────────────────────────────────────────┘

  Client A (upgrading)          Bannou                    Kamailio/RTPEngine
       │                          │                              │
       │  WS: media.tier_upgrade  │                              │
       │  (includes SIP creds)    │                              │
       │<═════════════════════════│                              │
       │                          │                              │
       │  ┌─────────────────────────────────────────────────┐    │
       │  │ PHASE 1: ESTABLISH SCALED (keep P2P running)    │    │
       │  └─────────────────────────────────────────────────┘    │
       │                          │                              │
       │  SIP REGISTER ──────────────────────────────────────────>│
       │  200 OK <────────────────────────────────────────────────│
       │                          │                              │
       │  SIP INVITE (conference) ───────────────────────────────>│
       │  200 OK (SDP) <──────────────────────────────────────────│
       │                          │                              │
       │  Start sending to RTPEngine                              │
       │  (still receiving from P2P peers)                        │
       │═════════════════════════════════════════════════════════>│
       │                          │                              │
       │  ┌─────────────────────────────────────────────────┐    │
       │  │ PHASE 2: CONFIRM SCALED CONNECTION               │    │
       │  └─────────────────────────────────────────────────┘    │
       │                          │                              │
       │  POST /media/upgrade/confirm                            │
       │  { roomId, participantId }                              │
       │─────────────────────────>│                              │
       │                          │                              │
       │  200 OK                  │                              │
       │<─────────────────────────│                              │
       │                          │                              │
       │  ┌─────────────────────────────────────────────────┐    │
       │  │ PHASE 3: WAIT FOR ALL PEERS TO MIGRATE          │    │
       │  └─────────────────────────────────────────────────┘    │
       │                          │                              │
       │  (Receive audio from both P2P peers AND RTPEngine)      │
       │  (De-duplicate based on SSRC)                           │
       │                          │                              │
       │  WS: media.peer_upgraded │                              │
       │  (for each peer)         │                              │
       │<═════════════════════════│                              │
       │                          │                              │
       │  ┌─────────────────────────────────────────────────┐    │
       │  │ PHASE 4: DISCONNECT P2P WHEN ALL MIGRATED        │    │
       │  └─────────────────────────────────────────────────┘    │
       │                          │                              │
       │  WS: media.tier_upgrade_complete                        │
       │<═════════════════════════│                              │
       │                          │                              │
       │  Disconnect all P2P streams                             │
       │  (now receiving only from RTPEngine)                    │
       │                          │                              │
       │  ══════════════════════════════════════════════════════ │
       │  UPGRADE COMPLETE - ZERO VOICE LOSS                     │
       │  ══════════════════════════════════════════════════════ │
```

### Client Implementation

```csharp
public partial class VoiceManager
{
    private ScaledVoiceClient? _pendingScaledClient;
    private HashSet<Guid> _migratedPeers = new();
    private bool _upgradeInProgress = false;

    private async Task PerformCleanTierUpgradeAsync(MediaTierUpgradeEvent evt)
    {
        if (_upgradeInProgress) return;
        _upgradeInProgress = true;

        try
        {
            // PHASE 1: Connect to Scaled tier while keeping P2P running
            _logger.LogInformation("Phase 1: Establishing Scaled connection");

            _pendingScaledClient = new ScaledVoiceClient(_audioSource);
            await _pendingScaledClient.ConnectAsync(evt.SipCredentials);

            // Start sending audio to RTPEngine
            // (P2P still active - dual transmission during transition)
            _pendingScaledClient.StartSending();

            // PHASE 2: Confirm our upgrade
            _logger.LogInformation("Phase 2: Confirming upgrade to server");

            await _mediaClient.ConfirmTierUpgradeAsync(new ConfirmUpgradeRequest
            {
                RoomId = evt.RoomId,
                ParticipantId = _participantId
            });

            // PHASE 3: Wait for peer migration
            // The OnPeerUpgraded and OnTierUpgradeComplete handlers will
            // track progress and trigger Phase 4
            _logger.LogInformation("Phase 3: Waiting for peer migration");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tier upgrade failed, continuing on P2P");
            _pendingScaledClient?.Dispose();
            _pendingScaledClient = null;
            _upgradeInProgress = false;
        }
    }

    private void OnPeerUpgraded(MediaPeerUpgradedEvent evt)
    {
        _migratedPeers.Add(evt.ParticipantId);
        _logger.LogDebug("Peer {ParticipantId} migrated to scaled", evt.ParticipantId);
    }

    private void OnTierUpgradeComplete(MediaTierUpgradeCompleteEvent evt)
    {
        // PHASE 4: All peers migrated - disconnect P2P
        _logger.LogInformation("Phase 4: Upgrade complete, disconnecting P2P");

        // Stop P2P
        _p2pClient?.Dispose();
        _p2pClient = null;

        // Activate scaled as primary
        _scaledClient = _pendingScaledClient;
        _pendingScaledClient = null;
        _currentTier = VoiceTier.Scaled;
        _upgradeInProgress = false;

        // Clear migration state
        _migratedPeers.Clear();

        OnTierChanged?.Invoke(VoiceTier.Scaled);
        _logger.LogInformation("Tier upgrade complete - now on Scaled tier");
    }
}
```

### Audio De-duplication During Transition

During the overlap period, the client may receive the same audio from both P2P (direct from peer) and Scaled (via RTPEngine). De-duplicate using SSRC:

```csharp
public class DuplicateAudioFilter
{
    private readonly Dictionary<uint, Queue<uint>> _recentSequences = new();
    private const int WINDOW_SIZE = 100;

    /// <summary>
    /// Returns true if this packet is a duplicate (already received via other path)
    /// </summary>
    public bool IsDuplicate(uint ssrc, uint sequenceNumber)
    {
        if (!_recentSequences.TryGetValue(ssrc, out var sequences))
        {
            sequences = new Queue<uint>();
            _recentSequences[ssrc] = sequences;
        }

        // Check if we've seen this sequence number recently
        if (sequences.Contains(sequenceNumber))
        {
            return true; // Duplicate
        }

        // Track this sequence number
        sequences.Enqueue(sequenceNumber);
        while (sequences.Count > WINDOW_SIZE)
        {
            sequences.Dequeue();
        }

        return false; // Not a duplicate
    }
}
```

---

## 12. Tier Availability Configuration

The voice system supports flexible tier configuration based on deployment capabilities and availability. Not all deployments will have both P2P and Scaled infrastructure.

### Configuration Options

```csharp
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class MediaServiceConfiguration
{
    // === Tier Availability ===

    /// <summary>
    /// Enable P2P tier for small groups (≤6 participants)
    /// </summary>
    public bool P2PEnabled { get; set; } = true;

    /// <summary>
    /// Enable Scaled tier (Kamailio + RTPEngine)
    /// </summary>
    public bool ScaledEnabled { get; set; } = true;

    /// <summary>
    /// Maximum participants before forcing upgrade to Scaled
    /// Only relevant if both tiers are enabled
    /// </summary>
    public int P2PMaxParticipants { get; set; } = 6;

    /// <summary>
    /// Enable automatic upgrade from P2P to Scaled
    /// If false, P2P rooms will reject participants beyond P2PMaxParticipants
    /// </summary>
    public bool AutoTierUpgrade { get; set; } = true;

    // === Kamailio Configuration (Scaled tier) ===
    public string KamailioHost { get; set; } = "localhost";
    public int KamailioRpcPort { get; set; } = 5060;
    public string SipDomain { get; set; } = "bannou.local";
    public string SipPasswordSalt { get; set; } = "change-me";

    // === RTPEngine Configuration (Scaled tier) ===
    public string RtpEngineHost { get; set; } = "localhost";
    public int RtpEnginePort { get; set; } = 22222;

    // === PSTN Configuration ===
    public bool PstnEnabled { get; set; } = false;
    public string SignalWireProjectId { get; set; } = "";
    public string SignalWireApiToken { get; set; } = "";
}
```

### Environment Variables

```bash
# Tier availability
BANNOU_P2PEnabled=true                    # Enable P2P tier
BANNOU_ScaledEnabled=true                 # Enable Scaled tier
BANNOU_P2PMaxParticipants=6               # P2P participant limit
BANNOU_AutoTierUpgrade=true               # Auto-upgrade P2P → Scaled

# Scaled infrastructure
BANNOU_KamailioHost=kamailio.example.com
BANNOU_SipDomain=voice.bannou.example.com

# PSTN (requires Scaled tier)
BANNOU_PstnEnabled=false
```

### Tier Selection Logic

```csharp
public class MediaService
{
    private VoiceTier DetermineTier(CreateRoomRequest request)
    {
        // Case 1: Only P2P enabled
        if (_config.P2PEnabled && !_config.ScaledEnabled)
        {
            if (request.MaxParticipants > _config.P2PMaxParticipants)
            {
                throw new InvalidOperationException(
                    $"Room requires {request.MaxParticipants} participants but scaled tier unavailable");
            }
            if (request.EnablePstn)
            {
                throw new InvalidOperationException(
                    "PSTN requires scaled tier which is not enabled");
            }
            return VoiceTier.P2P;
        }

        // Case 2: Only Scaled enabled
        if (!_config.P2PEnabled && _config.ScaledEnabled)
        {
            return VoiceTier.Scaled;
        }

        // Case 3: Neither enabled
        if (!_config.P2PEnabled && !_config.ScaledEnabled)
        {
            throw new InvalidOperationException("Voice is disabled (no tier available)");
        }

        // Case 4: Both enabled - choose based on requirements
        if (request.EnablePstn && !_config.PstnEnabled)
        {
            throw new InvalidOperationException("PSTN is not enabled");
        }

        if (request.EnablePstn)
            return VoiceTier.Scaled;

        if (request.MaxParticipants > _config.P2PMaxParticipants)
            return VoiceTier.Scaled;

        // Default to P2P for small rooms
        return VoiceTier.P2P;
    }

    private async Task HandleParticipantJoin(RoomState room, Guid accountId)
    {
        // Check if we need to upgrade
        if (room.Tier == VoiceTier.P2P &&
            room.Participants.Count >= _config.P2PMaxParticipants)
        {
            if (_config.ScaledEnabled && _config.AutoTierUpgrade)
            {
                await UpgradeToScaledTier(room);
            }
            else if (!_config.ScaledEnabled)
            {
                throw new RoomFullException(
                    $"Room is full ({_config.P2PMaxParticipants} max) and scaled tier unavailable");
            }
            else // AutoTierUpgrade disabled
            {
                throw new RoomFullException(
                    $"Room is full ({_config.P2PMaxParticipants} max)");
            }
        }
    }
}
```

### Deployment Scenarios

| Scenario | P2P | Scaled | PSTN | Use Case |
|----------|-----|--------|------|----------|
| **Minimal** | ✅ | ❌ | ❌ | Dev/testing, small groups only |
| **Standard** | ✅ | ✅ | ❌ | Production without PSTN |
| **Full** | ✅ | ✅ | ✅ | Complete voice + phone support |
| **Enterprise** | ❌ | ✅ | ✅ | Skip P2P, all traffic through infrastructure |

### Client Handling of Unavailable Tiers

```csharp
public async Task<VoiceConnectionResult> TryConnectVoiceAsync(JoinGameSessionResponse response)
{
    if (response.Voice == null)
    {
        return VoiceConnectionResult.VoiceDisabled();
    }

    if (!response.Voice.Enabled)
    {
        return VoiceConnectionResult.VoiceNotAvailable(response.Voice.DisabledReason);
    }

    try
    {
        await InitializeAsync(response);
        return VoiceConnectionResult.Success(_currentTier);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to connect voice");
        return VoiceConnectionResult.ConnectionFailed(ex.Message);
    }
}
```

### Graceful Degradation During Runtime

The system handles infrastructure unavailability gracefully:

```csharp
private async Task UpgradeToScaledTier(RoomState room)
{
    try
    {
        // Attempt to contact Kamailio/RTPEngine
        var healthCheck = await _rtpEngine.QueryAsync("health");

        if (healthCheck.Result != "ok")
        {
            _logger.LogWarning("RTPEngine unhealthy, cannot upgrade room {RoomId}", room.RoomId);
            throw new InfrastructureUnavailableException("RTPEngine not responding");
        }

        // Proceed with upgrade...
        await NotifyTierUpgradeAsync(room);
    }
    catch (Exception ex) when (ex is not RoomFullException)
    {
        // Scaled infrastructure unavailable at runtime
        _logger.LogError(ex, "Failed to upgrade room {RoomId} to scaled tier", room.RoomId);

        // Option 1: Continue on P2P (if under hard limit)
        if (room.Participants.Count < HARD_P2P_LIMIT)
        {
            _logger.LogWarning("Continuing on P2P tier despite reaching soft limit");
            return;
        }

        // Option 2: Reject new participants
        throw new RoomFullException(
            "Room is at capacity and scaled infrastructure is unavailable");
    }
}
```

### Runtime Health Monitoring

```csharp
public class MediaInfrastructureHealthMonitor : BackgroundService
{
    private bool _scaledAvailable = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check Kamailio
                var kamailioStats = await _kamailio.GetStatsAsync();

                // Check RTPEngine
                var rtpEngineHealth = await _rtpEngine.QueryAsync("ping");

                _scaledAvailable = kamailioStats != null && rtpEngineHealth.Result == "pong";
            }
            catch
            {
                _scaledAvailable = false;
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    public bool IsScaledTierAvailable => _config.ScaledEnabled && _scaledAvailable;
}
```

---

## 13. Implementation Roadmap

### Phase 1: P2P Foundation (Week 1)

| Task | Effort | Deliverable |
|------|--------|-------------|
| P2PVoiceClient implementation | 4h | SIPSorcery-based direct RTP |
| SipEndpointRegistry service | 2h | Endpoint tracking |
| Concentus integration | 2h | Opus encode/decode |
| Stride DynamicSoundSource | 4h | PCM playback |
| MediaService P2P endpoints | 4h | Register, lookup, join |
| End-to-end P2P test | 4h | Two clients talking |

**Deliverable**: Two Stride clients can voice chat via P2P.

### Phase 2: Kamailio + RTPEngine (Week 2)

| Task | Effort | Deliverable |
|------|--------|-------------|
| Deploy Kamailio container | 2h | Docker + config |
| Deploy RTPEngine container | 2h | Docker + config |
| KamailioClient JSONRPC | 4h | .NET HTTP client |
| RtpEngineClient ng protocol | 4h | .NET UDP client |
| MediaService scaled tier | 4h | Join/leave via SIP |
| SFU conference test | 4h | Multi-party call |

**Deliverable**: 10+ participants in voice conference via SFU.

### Phase 3: SignalWire PSTN (Week 3)

| Task | Effort | Deliverable |
|------|--------|-------------|
| SignalWire account setup | 1h | SIP endpoint configured |
| Kamailio trunk config | 2h | SignalWire routing |
| SWML webhook handler | 2h | Bannou endpoint |
| On-call moderator flow | 4h | PSTN → conference |
| Moderator UI in game | 4h | Incoming call indicator |

**Deliverable**: External callers can reach on-call moderator in Stride.

### Phase 4: Production Hardening (Week 4)

| Task | Effort | Deliverable |
|------|--------|-------------|
| TLS configuration | 4h | Secure transport |
| Auto tier upgrade | 4h | P2P → Scaled seamless |
| Monitoring/metrics | 4h | Prometheus export |
| Load testing | 4h | 1000+ concurrent validation |
| Documentation | 4h | Operations runbook |

**Deliverable**: Production-ready voice infrastructure.

---

## Appendix A: NuGet Packages

```xml
<!-- Voice/RTP -->
<PackageReference Include="SIPSorcery" Version="8.0.23" />
<PackageReference Include="Concentus" Version="2.2.0" />
<PackageReference Include="NAudio" Version="2.2.1" />

<!-- HTTP Client -->
<PackageReference Include="System.Net.Http.Json" Version="8.0.0" />
```

---

## Appendix B: Configuration

### MediaServiceConfiguration

```csharp
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class MediaServiceConfiguration
{
    // Kamailio
    public string KamailioHost { get; set; } = "localhost";
    public int KamailioRpcPort { get; set; } = 5060;
    public string SipDomain { get; set; } = "bannou.local";
    public string SipPasswordSalt { get; set; } = "change-me-in-production";

    // RTPEngine
    public string RtpEngineHost { get; set; } = "localhost";
    public int RtpEnginePort { get; set; } = 22222;

    // P2P
    public int P2PMaxParticipants { get; set; } = 6;
    public int P2PRegistrationTtlSeconds { get; set; } = 300;
}
```

### Environment Variables

```bash
# Kamailio
BANNOU_KamailioHost=kamailio.example.com
BANNOU_KamailioRpcPort=5060
BANNOU_SipDomain=voice.bannou.local
BANNOU_SipPasswordSalt=secure-random-salt

# RTPEngine
BANNOU_RtpEngineHost=rtpengine.example.com
BANNOU_RtpEnginePort=22222

# P2P
BANNOU_P2PMaxParticipants=6
BANNOU_P2PRegistrationTtlSeconds=300

# SignalWire
SIGNALWIRE_PROJECT_ID=your-project-id
SIGNALWIRE_API_TOKEN=your-api-token
SIGNALWIRE_SPACE=your-space.signalwire.com
```

---

## Appendix C: References

### Components
- [SIPSorcery](https://github.com/sipsorcery-org/sipsorcery) - Pure C# SIP/RTP
- [Concentus](https://github.com/lostromb/concentus) - Pure C# Opus codec
- [Kamailio](https://www.kamailio.org/) - SIP proxy server
- [RTPEngine](https://github.com/sipwise/rtpengine) - Media relay/SFU

### Documentation
- [Kamailio JSONRPC Module](https://www.kamailio.org/docs/modules/devel/modules/jsonrpcs.html)
- [RTPEngine NG Protocol](https://rtpengine.readthedocs.io/en/latest/ng_control_protocol.html)
- [SignalWire SIP Trunking](https://developer.signalwire.com/voice/getting-started/sip/sip-trunking/)

### Stride
- [DynamicSoundSource](https://doc.stride3d.net/4.0/en/api/Stride.Audio.DynamicSoundSource.html)
- [Spatial Audio](https://doc.stride3d.net/latest/en/manual/audio/spatialized-audio.html)

---

*Document Version: 5.0*
*Last Updated: December 2025*
*Architecture: Two-tier (P2P Direct + Kamailio/RTPEngine), SignalWire PSTN, SFU for conferences*
*New in 5.0: Complete client-side flows, WebSocket event schemas, clean tier upgrade protocol, tier availability configuration*
