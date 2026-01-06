# SDKs Overview

## Packages
- **BeyondImmersion.Bannou.Client.SDK**: WebSocket client for Connect, MessagePack DTOs, and game UDP transport helpers (LiteNetLib client).
- **BeyondImmersion.Bannou.SDK**: Server SDK including mesh service clients, BannouClient (same WebSocket client, for servers/agents), MessagePack DTOs, game UDP transport helpers (LiteNetLib server/client).

## WebSocket (Connect)
- Use `BannouClient.ConnectWithTokenAsync` for external mode (JWT).
- Use `BannouClient.ConnectInternalAsync(connectUrl, serviceToken)` for internal mode (service-token or network-trust) when a server talks to Event/Character agents via internal Connect nodes.
- The server SDK already includes BannouClient; you do not need to reference the Client SDK separately. If you consume both packages, types share the same namespace and remain compatible (no overlap issues, but redundant).

## Game Transport (UDP)
- Envelope: `GameProtocolEnvelope` (version + `GameMessageType` byte + MessagePack payload, LZ4).
- DTOs: snapshots/deltas, combat events, opportunities (data/response), connect, input, cinematic extension.
- Transports: `LiteNetLibServerTransport`, `LiteNetLibClientTransport` with optional fuzz (`TransportFuzzOptions`) for drop/delay testing.

## Quick Patterns
- **Server**
  ```csharp
  var transport = new LiteNetLibServerTransport();
  await transport.StartAsync(new GameTransportConfig { Port = 9000, SnapshotIntervalTicks = 60 });
  transport.OnClientMessage += (id, ver, type, payload) => { /* deserialize by type */ };
  // Broadcast deltas/snapshots
  ```
- **Client**
  ```csharp
  var transport = new LiteNetLibClientTransport();
  await transport.ConnectAsync("127.0.0.1", 9000, GameProtocolEnvelope.CurrentVersion);
  transport.OnServerMessage += (ver, type, payload) => { /* handle snapshot/delta/opportunity */ };
  ```
- **Fuzz**
  ```csharp
  transport.FuzzOptions.DropProbability = 0.1;
  transport.FuzzOptions.DelayProbability = 0.2;
  transport.FuzzOptions.MaxDelayMs = 50;
  ```

## Stride Integration Notes
- Use the UDP transport for 60 Hz state + selective reliability.
- Use BannouClient (internal mode) on the game server to talk to Event/Character agents via Connect.
- Clients/game servers do not need both packages; `BeyondImmersion.Bannou.SDK` already contains BannouClient for servers, and `BeyondImmersion.Bannou.Client.SDK` is for game clients.
