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
}
