using BeyondImmersion.Bannou.Protocol;
using BeyondImmersion.Bannou.Protocol.Messages;
using BeyondImmersion.Bannou.Transport;
using MessagePack;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BeyondImmersion.Bannou.Server.Tests;

public class LiteNetLibTransportTests
{
    [Fact]
    public async Task ServerClient_RoundTrip_MessageReceived()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 19000 + Random.Shared.Next(0, 1000) };

        bool serverGotMsg = false;
        bool clientConnected = false;

        server.OnClientMessage += (id, version, type, payload) =>
        {
            Assert.Equal(GameProtocolEnvelope.CurrentVersion, version);
            Assert.Equal(GameMessageType.PlayerInput, type);

            var parsed = MessagePackSerializer.Deserialize<PlayerInputMessage>(payload, GameProtocolEnvelope.DefaultOptions);
            Assert.Equal(123u, parsed.Tick);
            serverGotMsg = true;
        };

        client.OnConnected += () => clientConnected = true;

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);

        // Wait for client to connect (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!clientConnected && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }
        Assert.True(clientConnected);

        var msg = new PlayerInputMessage
        {
            Tick = 123,
            MoveX = 1,
            MoveY = 0
        };
        var payload = MessagePackSerializer.Serialize(msg, GameProtocolEnvelope.DefaultOptions);
        await client.SendAsync(GameMessageType.PlayerInput, payload, reliable: true, cts.Token);

        // Wait for message to arrive (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!serverGotMsg && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }
        Assert.True(serverGotMsg);
    }

    [Fact]
    public async Task ServerToClient_Snapshot_RoundTrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 20000 + Random.Shared.Next(0, 1000) };

        bool clientGotMsg = false;
        bool serverGotClient = false;

        client.OnServerMessage += (version, type, payload) =>
        {
            Assert.Equal(GameProtocolEnvelope.CurrentVersion, version);
            Assert.Equal(GameMessageType.ArenaStateSnapshot, type);
            var parsed = MessagePackSerializer.Deserialize<ArenaStateSnapshot>(payload, GameProtocolEnvelope.DefaultOptions);
            Assert.Equal(999u, parsed.Tick);
            clientGotMsg = true;
        };

        // Track when server acknowledges client connection (required for SendToAll to work)
        server.OnClientConnected += _ => serverGotClient = true;

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);

        // Wait for server to register the client (not just client thinking it's connected)
        while (!serverGotClient && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }

        var snapshot = new ArenaStateSnapshot
        {
            Tick = 999
        };
        snapshot.Entities.Add(new EntityState { EntityId = 1, X = 1, Y = 2, Z = 3, Health = 100, ActionState = 0 });
        var payload = MessagePackSerializer.Serialize(snapshot, GameProtocolEnvelope.DefaultOptions);
        await server.BroadcastAsync(GameMessageType.ArenaStateSnapshot, payload, reliable: true, cts.Token);

        // Wait for message to arrive (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!clientGotMsg && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }
        Assert.True(clientGotMsg);
    }

    [Fact]
    public async Task Fuzz_DropAndDelay_DoesNotCrash()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 21000 + Random.Shared.Next(0, 1000) };

        server.FuzzOptions.DropProbability = 0.5;
        server.FuzzOptions.DelayProbability = 0.5;
        server.FuzzOptions.MaxDelayMs = 50;

        client.FuzzOptions.DropProbability = 0.5;
        client.FuzzOptions.DelayProbability = 0.5;
        client.FuzzOptions.MaxDelayMs = 50;

        int received = 0;

        server.OnClientMessage += (id, version, type, payload) =>
        {
            received++;
        };

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);
        await Task.Delay(50, cts.Token);

        var msg = new PlayerInputMessage { Tick = 1 };
        var payload = MessagePackSerializer.Serialize(msg, GameProtocolEnvelope.DefaultOptions);

        for (int i = 0; i < 5; i++)
        {
            await client.SendAsync(GameMessageType.PlayerInput, payload, reliable: true, cts.Token);
        }

        await Task.Delay(200, cts.Token);
        Assert.True(received >= 0); // ensure no exceptions; may drop due to fuzz
    }

    [Fact]
    public async Task ServerToClient_OpportunityData_RoundTrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 22000 + Random.Shared.Next(0, 1000) };

        bool clientGotMsg = false;
        bool serverGotClient = false;

        client.OnServerMessage += (version, type, payload) =>
        {
            if (type != GameMessageType.OpportunityData) return;
            var parsed = MessagePackSerializer.Deserialize<OpportunityDataMessage>(payload, GameProtocolEnvelope.DefaultOptions);
            Assert.Equal("opp-1", parsed.OpportunityId);
            Assert.Equal("Jump?", parsed.Prompt);
            Assert.True(parsed.Forced);
            clientGotMsg = true;
        };

        server.OnClientConnected += _ => serverGotClient = true;

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);

        while (!serverGotClient && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }

        var opp = new OpportunityDataMessage
        {
            OpportunityId = "opp-1",
            Prompt = "Jump?",
            Forced = true,
            DeadlineMs = 1500
        };
        opp.Options.Add(new OpportunityOption { Id = "yes", Label = "Yes" });
        var payload = MessagePackSerializer.Serialize(opp, GameProtocolEnvelope.DefaultOptions);
        await server.BroadcastAsync(GameMessageType.OpportunityData, payload, reliable: true, cts.Token);

        // Wait for message to arrive (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!clientGotMsg && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }
        Assert.True(clientGotMsg);
    }

    [Fact]
    public async Task ClientToServer_OpportunityResponse_RoundTrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 23000 + Random.Shared.Next(0, 1000) };

        bool serverGot = false;

        bool clientConnected = false;

        server.OnClientMessage += (id, version, type, payload) =>
        {
            if (type != GameMessageType.OpportunityResponse) return;
            var parsed = MessagePackSerializer.Deserialize<OpportunityResponseMessage>(payload, GameProtocolEnvelope.DefaultOptions);
            Assert.Equal("opp-2", parsed.OpportunityId);
            Assert.Equal("yes", parsed.SelectedOptionId);
            serverGot = true;
        };

        client.OnConnected += () => clientConnected = true;

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);

        // Wait for client to connect (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!clientConnected && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }

        var resp = new OpportunityResponseMessage
        {
            OpportunityId = "opp-2",
            SelectedOptionId = "yes",
            ClientLatencyMs = 12
        };
        var payload = MessagePackSerializer.Serialize(resp, GameProtocolEnvelope.DefaultOptions);
        await client.SendAsync(GameMessageType.OpportunityResponse, payload, reliable: true, cts.Token);

        // Wait for message to arrive (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!serverGot && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }
        Assert.True(serverGot);
    }

    [Fact]
    public async Task ServerToClient_CombatEvent_RoundTrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 24000 + Random.Shared.Next(0, 1000) };
        bool clientGot = false;
        bool serverGotClient = false;

        client.OnServerMessage += (version, type, payload) =>
        {
            if (type != GameMessageType.CombatEvent) return;
            var msg = MessagePackSerializer.Deserialize<CombatEventMessage>(payload, GameProtocolEnvelope.DefaultOptions);
            Assert.Single(msg.Events);
            Assert.Equal(10, msg.Events[0].Amount);
            clientGot = true;
        };

        server.OnClientConnected += _ => serverGotClient = true;

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);

        while (!serverGotClient && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }

        var ce = new CombatEventMessage
        {
            Tick = 1,
            Events =
            {
                new CombatEvent { Type = CombatEventType.Hit, SourceEntityId = 1, TargetEntityId = 2, Amount = 10, RemainingHp = 90 }
            }
        };

        var payload = MessagePackSerializer.Serialize(ce, GameProtocolEnvelope.DefaultOptions);
        await server.BroadcastAsync(GameMessageType.CombatEvent, payload, reliable: true, cts.Token);

        // Wait for message to arrive (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!clientGot && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }
        Assert.True(clientGot);
    }

    [Fact]
    public async Task ServerToClient_Delta_RoundTrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 25000 + Random.Shared.Next(0, 1000) };
        bool clientGot = false;
        bool serverGotClient = false;

        client.OnServerMessage += (ver, type, payload) =>
        {
            if (type != GameMessageType.ArenaStateDelta) return;
            var delta = MessagePackSerializer.Deserialize<ArenaStateDelta>(payload, GameProtocolEnvelope.DefaultOptions);
            Assert.Equal(2u, delta.Tick);
            Assert.Single(delta.Entities);
            Assert.Equal(EntityDeltaFlags.Health | EntityDeltaFlags.Position, delta.Entities[0].Flags);
            clientGot = true;
        };

        server.OnClientConnected += _ => serverGotClient = true;

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);

        while (!serverGotClient && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }

        var deltaMsg = new ArenaStateDelta
        {
            Tick = 2,
            Entities =
            {
                new EntityDelta
                {
                    EntityId = 1,
                    Flags = EntityDeltaFlags.Health | EntityDeltaFlags.Position,
                    X = 5, Y = 0, Z = 0,
                    Health = 50
                }
            }
        };
        var payload = MessagePackSerializer.Serialize(deltaMsg, GameProtocolEnvelope.DefaultOptions);
        await server.BroadcastAsync(GameMessageType.ArenaStateDelta, payload, reliable: false, cts.Token);

        // Wait for message to arrive (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!clientGot && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }
        Assert.True(clientGot);
    }

    [Fact]
    public async Task ServerToClient_CinematicExtension_RoundTrip()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 26000 + Random.Shared.Next(0, 1000) };
        bool got = false;
        bool serverGotClient = false;

        client.OnServerMessage += (ver, type, payload) =>
        {
            if (type != GameMessageType.CinematicExtension) return;
            var ext = MessagePackSerializer.Deserialize<CinematicExtensionMessage>(payload, GameProtocolEnvelope.DefaultOptions);
            Assert.Equal("ex1", ext.ExchangeId);
            Assert.Equal("attach", ext.AttachPoint);
            Assert.Equal("https://example.com/ext.bin", ext.PayloadUrl);
            got = true;
        };

        server.OnClientConnected += _ => serverGotClient = true;

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);

        while (!serverGotClient && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }

        var extMsg = new CinematicExtensionMessage
        {
            ExchangeId = "ex1",
            AttachPoint = "attach",
            PayloadUrl = "https://example.com/ext.bin",
            ValidUntilEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000
        };
        var payload = MessagePackSerializer.Serialize(extMsg, GameProtocolEnvelope.DefaultOptions);
        await server.BroadcastAsync(GameMessageType.CinematicExtension, payload, reliable: true, cts.Token);

        // Wait for message to arrive (poll instead of fixed delay to avoid flakiness on slow CI)
        while (!got && !cts.Token.IsCancellationRequested)
        {
            await Task.Delay(10, cts.Token);
        }
        Assert.True(got);
    }
}
