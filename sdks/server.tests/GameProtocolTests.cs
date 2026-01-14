using BeyondImmersion.Bannou.Protocol;
using MessagePack;
using System;
using Xunit;

namespace BeyondImmersion.Bannou.Server.Tests;

[MessagePackObject]
public class TestPayload
{
    [Key(0)]
    public int Value { get; set; }
}

public class GameProtocolTests
{
    [Fact]
    public void SerializeAndParse_RoundTripsVersionAndType()
    {
        var payload = new TestPayload { Value = 42 };
        var bytes = GameProtocolEnvelope.Serialize(GameMessageType.ArenaStateSnapshot, payload, version: 7);

        var (version, type, parsedPayload) = GameProtocolEnvelope.Parse(bytes);

        Assert.Equal((byte)7, version);
        Assert.Equal(GameMessageType.ArenaStateSnapshot, type);
        Assert.False(parsedPayload.IsEmpty);
    }

    [Fact]
    public void ParseAndDeserialize_RoundTripsPayload()
    {
        var payload = new TestPayload { Value = 1234 };
        var bytes = GameProtocolEnvelope.Serialize(GameMessageType.PlayerInput, payload);

        var (version, type, parsed) = GameProtocolEnvelope.ParseAndDeserialize<TestPayload>(bytes);

        Assert.Equal(GameProtocolEnvelope.CurrentVersion, version);
        Assert.Equal(GameMessageType.PlayerInput, type);
        Assert.Equal(payload.Value, parsed.Value);
    }

    [Fact]
    public void Parse_ThrowsOnShortBuffer()
    {
        Assert.Throws<ArgumentException>(() => GameProtocolEnvelope.Parse(ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentException>(() => GameProtocolEnvelope.Parse(new byte[] { 1 }));
    }
}
