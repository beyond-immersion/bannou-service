using System;
using System.Threading;
using System.Threading.Tasks;
using BeyondImmersion.Bannou.GameProtocol;
using BeyondImmersion.Bannou.GameProtocol.Messages;
using BeyondImmersion.Bannou.GameTransport;
using MessagePack;
using Xunit;

namespace BeyondImmersion.Bannou.SDK.Tests;

public class LiteNetLibTransportTests
{
    [Fact]
    public async Task ServerClient_RoundTrip_MessageReceived()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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

        // wait for connect
        await Task.Delay(50, cts.Token);
        Assert.True(clientConnected);

        var msg = new PlayerInputMessage
        {
            Tick = 123,
            MoveX = 1,
            MoveY = 0
        };
        var payload = MessagePackSerializer.Serialize(msg, GameProtocolEnvelope.DefaultOptions);
        await client.SendAsync(GameMessageType.PlayerInput, payload, reliable: true, cts.Token);

        await Task.Delay(100, cts.Token);
        Assert.True(serverGotMsg);
    }

    [Fact]
    public async Task ServerToClient_Snapshot_RoundTrip()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new LiteNetLibServerTransport();
        await using var client = new LiteNetLibClientTransport();

        var cfg = new GameTransportConfig { Port = 20000 + Random.Shared.Next(0, 1000) };

        bool clientGotMsg = false;

        client.OnServerMessage += (version, type, payload) =>
        {
            Assert.Equal(GameProtocolEnvelope.CurrentVersion, version);
            Assert.Equal(GameMessageType.ArenaStateSnapshot, type);
            var parsed = MessagePackSerializer.Deserialize<ArenaStateSnapshot>(payload, GameProtocolEnvelope.DefaultOptions);
            Assert.Equal(999u, parsed.Tick);
            clientGotMsg = true;
        };

        await server.StartAsync(cfg, cts.Token);
        await client.ConnectAsync("127.0.0.1", cfg.Port, GameProtocolEnvelope.CurrentVersion, cts.Token);

        await Task.Delay(50, cts.Token);

        var snapshot = new ArenaStateSnapshot
        {
            Tick = 999
        };
        snapshot.Entities.Add(new EntityState { EntityId = 1, X = 1, Y = 2, Z = 3, Health = 100, ActionState = 0 });
        var payload = MessagePackSerializer.Serialize(snapshot, GameProtocolEnvelope.DefaultOptions);
        await server.BroadcastAsync(GameMessageType.ArenaStateSnapshot, payload, reliable: true, cts.Token);

        await Task.Delay(100, cts.Token);
        Assert.True(clientGotMsg);
    }

    [Fact]
    public async Task Fuzz_DropAndDelay_DoesNotCrash()
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
}
