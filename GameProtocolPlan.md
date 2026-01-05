# Game Protocol Plan (SDK-Centric)

## Purpose
Unify Stride game server ⇄ client networking with Bannou so all games reuse the same SDK pieces. This document consolidates the original Stride plan, the Bannou-side research, and a concrete implementation plan (with open questions and proposed event shapes).

## What We’re Building
- **Transport**: UDP over LiteNetLib with MessagePack payloads for low-latency 60 Hz gameplay state and selective reliability (unreliable for position deltas, reliable for events).
- **Scope**: Reusable server + client modules living in `Bannou.SDK` and `Bannou.Client.SDK`, not game-specific code.
- **Bannou integration goals**:
  - Servers can open WebSocket sessions to Connect (including internal/service-token mode) to talk to Event/Character agents.
  - Event agents can push “opportunity/QTE/cinematic extension” data to servers, which then forward to clients via the game transport.
  - Shared MessagePack DTOs for game protocol so engines don’t redefine shapes.

## Original Stride Plan (kept here so the old file can be discarded)
- **Architecture**: Stride client ⇄ game server over UDP (LiteNetLib); game server ⇄ Bannou services over WebSocket/mesh.
- **Message types** (`MessageType` byte prefix):
  - Server → Client: `ArenaStateSnapshot`, `ArenaStateDelta`, `CombatEvent`, `MatchState`, `OpportunityData`
  - Client → Server: `ConnectRequest`, `PlayerInput`, `OpportunityResponse`, `Ping`
- **Files to create**:
  - Server: `GameProtocol.cs` (MessagePack helpers), `ClientConnectionManager.cs` (LiteNetLib host), `StateBroadcaster.cs` (full + delta at 60 Hz), `InputReceiver.cs` (validate/queue input)
  - Client: `ServerConnection.cs` (LiteNetLib client, AsyncScript), `StateReceiver.cs`, `StateSynchronizer.cs` (100 ms interpolation), `InputTransmitter.cs`
- **Implementation steps**:
  1) GameProtocol (MessagePack serialize/deserialize + type prefix)
  2) ClientConnectionManager (LiteNetLib host; `PollEvents()` each tick; ReliableOrdered for events, Sequenced for state)
  3) StateBroadcaster (full snapshot every ~60 ticks, deltas otherwise)
  4) InputReceiver (validate + queue; apply per tick)
  5) Wire into game loop (`PollEvents`, broadcast per tick)
  6) Client ServerConnection (LiteNetLib client; `PollEvents` each frame)
  7) StateSynchronizer (100 ms buffer, interpolate; snap discrete values)
- **Protocol flow**:
  - Client → Server: `ConnectRequest`
  - Server → Client: immediate `ArenaStateSnapshot`, then `ArenaStateDelta` every tick; periodic full snapshot
  - Client → Server: `PlayerInput` on input; `OpportunityResponse` when prompted
  - Server → Client: `CombatEvent` on hit/damage, `OpportunityData` when available
- **Config & packages**:
  - NuGet: `LiteNetLib`, `MessagePack`, `MessagePack.Annotations`
  - Config (server): `GamePort` (UDP), `MaxPlayersPerMatch`, `StateSnapshotIntervalTicks`
  - Build order: add packages → add MessagePack attributes on protocol DTOs → implement protocol helpers → server host → input/broadcast → wire into app → client connection + parsing → interpolation → input TX

## Current State (Bannou)
- **Client SDK**: `BannouClient` (WebSocket Connect edge) for auth, capability manifests, request/response, events.
- **Server SDK**: Re-exports `BannouClient` and mesh clients; no game-loop transport utilities yet.
- **Connect internal mode**: Exists server-side (service-token or network-trust) but `BannouClient` lacks a first-class path to use it.
- **Event/Cinematic context**: Event Brain (actor) should push opportunities/QTEs/extensions to game servers; servers forward to clients. Streaming composition for cinematics is a gap but planned (continuation/attach points).

## Implementation Plan (expanded with questions)
1) **Shared DTOs + Protocol Helpers**
   - Deliver MessagePack DTOs and `GameProtocol` helper (serialize/parse with leading `MessageType` byte) as a reusable package.
   - Question: Do we need versioning in the payload header (e.g., `ProtocolVersion` byte) for future compatibility?
2) **Server Transport Module (Bannou.SDK)**
   - Add LiteNetLib host wrapper (`ClientConnectionManager`), `StateBroadcaster`, `InputReceiver` (configurable snapshot cadence, delta compression hook).
   - Provide dependency-light defaults (no forced logging framework).
   - Question: Minimum delta format for `ArenaStateDelta` (full component? bitmask? entity-component deltas?) to balance size vs complexity.
3) **Client Transport Module (Bannou.Client.SDK)**
   - Add LiteNetLib client wrapper (`ServerConnection`), message dispatch (`StateReceiver`), interpolation buffer (`StateSynchronizer`), `InputTransmitter`.
   - Question: Target interpolation delay default (100 ms from original plan) — keep or make configurable per title?
4) **Connect Internal Mode Support in BannouClient**
   - Add config to use internal mode with `X-Service-Token` or network-trust and optional explicit Connect URL (for servers talking to internal Connect nodes).
   - Expose a simple “connect profile” (external vs internal) so game servers can subscribe to agent/event streams without duplicating code.
   - Question: Should internal mode skip login/refresh entirely and only send the service token header? (Likely yes for headless servers.)
5) **Event/Opportunity Bridge (Event Agent → Game Server → Game Clients)**
   - Define how Event Brain delivers opportunities/QTEs/extensions to game servers (WebSocket event via Connect vs mesh call). Prefer WebSocket to avoid adding latency.
   - Add a handler on the server transport to receive these and emit `OpportunityData` to clients.
   - Question: Should opportunities be time-bounded server-side (deadline/sequence) to avoid stale prompts? (Recommend yes.)
6) **Cinematic Extension Path (Streaming Composition Ready)**
   - Reserve a payload for “CinematicExtension” (attach point id, validity window, data blob/URL). Even if interpreter changes later, shape should exist.
   - Question: Carry extension as opaque blob (for future ABML bytecode) or structured today (animation hints, camera cues)? Start with opaque + metadata.
7) **Docs & Samples**
   - Add an engine guide (Stride-focused) showing how to wire the new transport, and a server-side sample loop.
   - Note how Bannou capability manifests/shortcuts feed into matchmaking/session join, and how opportunities appear.
8) **Testing**
   - Unit tests for protocol serialization/parsing, delta computation, and interpolation math.
   - Integration test harness: mock LiteNetLib host/client in-process to validate round-trips and reliability modes.
   - Question: Should we include a “lag/fuzz” test mode to simulate packet loss/reordering? (Recommended.)

## Proposed Event Shapes (draft)
Goal: Fit Bannou’s event style (JSON bodies over Connect/WebSocket) while carrying what the game transport needs.

### Opportunity (server-bound from Event Brain)
```json
{
  "eventName": "game.opportunity.created",
  "opportunityId": "opp-123",
  "exchangeId": "exch-456",
  "characterIds": ["char-a", "char-b"],
  "expiresAt": "2026-01-05T12:00:00Z",
  "prompt": "Chandelier above! Take the shot?",
  "options": [
    { "id": "throw", "label": "Throw debris", "score": 0.82, "objectId": "prop-9" },
    { "id": "brace", "label": "Brace for impact", "score": 0.55 }
  ],
  "defaultOptionId": "brace",
  "metadata": {
    "scene": "arena-1",
    "beat": 12,
    "source": "event-brain"
  }
}
```
Why: Contains expiration, participants, options with scores, and a default for timeout behavior (aligns with character-agent defaults from THE_DREAM).

### Opportunity Response (client → server → Event Brain)
```json
{
  "eventName": "game.opportunity.responded",
  "opportunityId": "opp-123",
  "exchangeId": "exch-456",
  "characterId": "char-a",
  "selectedOptionId": "throw",
  "clientLatencyMs": 42
}
```
Why: Correlates to the opportunity, identifies the actor, and provides latency for diagnostics.

### Cinematic Extension Offer (Event Brain → game server)
```json
{
  "eventName": "game.cinematic.extension",
  "exchangeId": "exch-456",
  "attachPoint": "before_resolution",
  "validUntil": "2026-01-05T12:00:02Z",
  "payloadType": "abml-bytecode",
  "payloadUrl": "https://assets.example.com/cinematics/exch-456-ext1.bin",
  "hint": "Environmental kill via chandelier"
}
```
Why: Declares where to attach, a validity window, and an opaque payload reference (future-proof for streaming composition).

### Server → Client Opportunity Data (game transport)
```json
{
  "messageType": "OpportunityData",
  "opportunityId": "opp-123",
  "prompt": "Chandelier above! Take the shot?",
  "options": [
    { "id": "throw", "label": "Throw debris" },
    { "id": "brace", "label": "Brace" }
  ],
  "defaultOptionId": "brace",
  "deadlineMs": 1500,
  "exchangeId": "exch-456"
}
```
Why: Minimal client payload; deadline in ms for UI timers.

### Server → Client Combat Event (game transport)
```json
{
  "messageType": "CombatEvent",
  "tick": 12345,
  "events": [
    { "type": "Hit", "source": "char-a", "target": "char-b", "damage": 12, "remainingHp": 34 },
    { "type": "Stagger", "target": "char-b", "durationMs": 600 }
  ]
}
```
Why: Batch per tick, aligns with MessagePack DTO and selective reliability (reliable).

## Use-Case Coverage (why the shapes work)
- **Monster Arena (small scale)**: Opportunities are simple (environmental props, comeback moves). The shapes above cover prompt + options + default + expiry. Cinematic extension carries attach point for optional dramatic finish.
- **Arcadia (large scale / THE_DREAM)**: Event agents run many exchanges; attach points and validity windows prevent stale extensions. Metadata fields allow routing/observability without bloating the client payload. Optional scores let servers choose top-N options per participant.

## Open Questions (answers locked in)
1) Protocol version byte: **Yes** — include versioning in MessagePack envelopes to allow live protocol updates.
2) Interpolation delay: **Configurable with recommended default** (keep 100 ms as default, allow override).
3) Delta format: **Diffs/bitmask/entity-component deltas** (not full component) for `ArenaStateDelta`.
4) Internal Connect mode: **Skip auth flows** — use shared internal secret (`X-Service-Token`/config) only.
5) Opportunity expiry vs forced cutscene: **Both** — support optional “forced” flag; otherwise treat as opportunities with deadlines.
6) Cinematic extension payload extras: **Optional `initiate_state` KVP bag** to seed next behavior; otherwise keep opaque blob + metadata.
7) Lag/fuzz harness: **Yes** — include a test mode to simulate packet loss/reordering.

## Next Actions (execution-ready)
1) Add internal-mode path to `BannouClient` (service-token header, explicit connect URL) + unit test.
2) Introduce shared `GameProtocol` DTOs/helpers (MessagePack + type byte) in both SDKs; no LiteNetLib dependency yet.
3) Add server LiteNetLib wrapper + broadcaster/input pipeline with config knobs; unit tests for protocol + deltas.
4) Add client LiteNetLib wrapper + receiver/interpolator + input TX; interpolation unit tests.
5) Implement event bridge handler to translate Event Brain opportunity/extension events into game transport messages; include expiry/sequence handling.
6) Reserve cinematic extension handling in the transport (payload type/url + attach point) even if interpreter hookup comes later.
7) Write Stride-focused guide and sample wiring (server loop + client scripts).
8) Build integration test harness with optional lag/fuzz injection.
