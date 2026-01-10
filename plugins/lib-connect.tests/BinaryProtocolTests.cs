using BeyondImmersion.BannouService.Connect.Protocol;
using System.Text;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Cross-platform compatibility tests for the binary protocol.
/// These tests validate that the protocol works consistently across different endianness systems.
/// </summary>
public class BinaryProtocolTests
{
    [Fact]
    public void NetworkByteOrderSupport_ShouldBeSupported()
    {
        Assert.True(NetworkByteOrder.IsNetworkByteOrderSupported());
        Assert.True(NetworkByteOrder.TestNetworkByteOrderCompatibility());
    }

    [Fact]
    public void NetworkByteOrderOperations_ShouldReadWriteCorrectly()
    {
        Span<byte> buffer = stackalloc byte[32];

        // Test UInt16
        NetworkByteOrder.WriteUInt16(buffer.Slice(0, 2), 0x1234);
        var readUInt16 = NetworkByteOrder.ReadUInt16(buffer.Slice(0, 2));
        Assert.Equal((ushort)0x1234, readUInt16);

        // Verify actual byte order (should be big-endian)
        Assert.Equal(0x12, buffer[0]);
        Assert.Equal(0x34, buffer[1]);

        // Test UInt32
        NetworkByteOrder.WriteUInt32(buffer.Slice(2, 4), 0x12345678);
        var readUInt32 = NetworkByteOrder.ReadUInt32(buffer.Slice(2, 4));
        Assert.Equal(0x12345678U, readUInt32);

        // Verify actual byte order (should be big-endian)
        Assert.Equal(0x12, buffer[2]);
        Assert.Equal(0x34, buffer[3]);
        Assert.Equal(0x56, buffer[4]);
        Assert.Equal(0x78, buffer[5]);

        // Test UInt64
        NetworkByteOrder.WriteUInt64(buffer.Slice(6, 8), 0x123456789ABCDEF0);
        var readUInt64 = NetworkByteOrder.ReadUInt64(buffer.Slice(6, 8));
        Assert.Equal(0x123456789ABCDEF0UL, readUInt64);
    }

    [Fact]
    public void BinaryMessageRoundTrip_ShouldPreserveAllFields()
    {
        var originalGuid = new Guid("12345678-1234-5678-9ABC-DEF012345678");
        var originalPayload = Encoding.UTF8.GetBytes("Hello, cross-platform world!");

        var originalMessage = new BinaryMessage(
            MessageFlags.Binary | MessageFlags.HighPriority,
            1234,
            5678,
            originalGuid,
            0x123456789ABCDEF0,
            originalPayload
        );

        // Serialize and deserialize
        var serialized = originalMessage.ToByteArray();
        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

        // Verify all fields match exactly
        Assert.Equal(originalMessage.Flags, deserialized.Flags);
        Assert.Equal(originalMessage.Channel, deserialized.Channel);
        Assert.Equal(originalMessage.SequenceNumber, deserialized.SequenceNumber);
        Assert.Equal(originalMessage.ServiceGuid, deserialized.ServiceGuid);
        Assert.Equal(originalMessage.MessageId, deserialized.MessageId);
        Assert.True(deserialized.Payload.Span.SequenceEqual(originalMessage.Payload.Span));
    }

    [Fact]
    public void BinaryMessageEdgeCases_EmptyPayload_ShouldWork()
    {
        var emptyMessage = new BinaryMessage(
            MessageFlags.Event,
            0,
            0,
            Guid.Empty,
            0,
            ReadOnlyMemory<byte>.Empty
        );

        var serialized = emptyMessage.ToByteArray();
        Assert.Equal(BinaryMessage.HeaderSize, serialized.Length);

        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);
        Assert.Equal(0, deserialized.Payload.Length);
    }

    [Fact]
    public void BinaryMessageEdgeCases_MaximumValues_ShouldWork()
    {
        // Test with maximum values (request message - exclude Response flag to use 31-byte header)
        var maxMessage = new BinaryMessage(
            (MessageFlags)0x3F, // All flags except Response (0x40) and Reserved (0x80)
            ushort.MaxValue,
            uint.MaxValue,
            Guid.NewGuid(),
            ulong.MaxValue,
            new byte[1000]
        );

        var maxSerialized = maxMessage.ToByteArray();
        var maxDeserialized = BinaryMessage.Parse(maxSerialized, maxSerialized.Length);

        Assert.Equal(maxMessage.Flags, maxDeserialized.Flags);
        Assert.Equal(maxMessage.Channel, maxDeserialized.Channel);
        Assert.Equal(maxMessage.SequenceNumber, maxDeserialized.SequenceNumber);
        Assert.Equal(maxMessage.ServiceGuid, maxDeserialized.ServiceGuid);
        Assert.Equal(maxMessage.MessageId, maxDeserialized.MessageId);
    }

    [Fact]
    public void SimulatedCrossEndianness_ShouldWorkIdentically()
    {
        // Create a message on "this system"
        var message = BinaryMessage.FromJson(
            100,
            200,
            new Guid("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            0x1122334455667788,
            "{\"test\":\"cross-endian\"}"
        );

        var bytes = message.ToByteArray();

        // Simulate what would happen if this byte array was sent over the network
        // and received by a system with different endianness
        // Since we're using network byte order, it should work identically
        var receivedMessage = BinaryMessage.Parse(bytes, bytes.Length);

        // Verify the message is identical
        Assert.Equal(message.Channel, receivedMessage.Channel);
        Assert.Equal(message.SequenceNumber, receivedMessage.SequenceNumber);
        Assert.Equal(message.ServiceGuid, receivedMessage.ServiceGuid);
        Assert.Equal(message.MessageId, receivedMessage.MessageId);
        Assert.Equal(message.GetJsonPayload(), receivedMessage.GetJsonPayload());
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("00000000-0000-0000-0000-000000000001")]
    [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
    [InlineData("12345678-1234-5678-9ABC-DEF012345678")]
    public void GuidNetworkOrderConsistency_ShouldRoundTrip(string guidString)
    {
        var guid = new Guid(guidString);
        Span<byte> buffer = stackalloc byte[16];

        NetworkByteOrder.WriteGuid(buffer, guid);
        var readGuid = NetworkByteOrder.ReadGuid(buffer);

        Assert.Equal(guid, readGuid);
    }

    [Fact]
    public void RealWorldMessageScenarios_AuthMessage_ShouldRoundTrip()
    {
        var authMessage = BinaryMessage.FromJson(
            1, // Auth channel
            1, // First message
            GuidGenerator.GenerateServiceGuid("session-123", "auth", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            "{\"action\":\"login\",\"username\":\"test\",\"password\":\"secret\"}"
        );

        var serialized = authMessage.ToByteArray();
        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

        Assert.Equal(authMessage.Flags, deserialized.Flags);
        Assert.Equal(authMessage.Channel, deserialized.Channel);
        Assert.Equal(authMessage.SequenceNumber, deserialized.SequenceNumber);
        Assert.Equal(authMessage.ServiceGuid, deserialized.ServiceGuid);
        Assert.Equal(authMessage.MessageId, deserialized.MessageId);
        Assert.True(deserialized.Payload.Span.SequenceEqual(authMessage.Payload.Span));
    }

    [Fact]
    public void RealWorldMessageScenarios_BinaryPhysicsData_ShouldRoundTrip()
    {
        var physicsData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF, 0xFE, 0xFD, 0xFC };
        var physicsMessage = BinaryMessage.FromBinary(
            2, // Physics channel
            100,
            GuidGenerator.GenerateServiceGuid("session-456", "physics", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            physicsData
        );

        var serialized = physicsMessage.ToByteArray();
        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

        Assert.Equal(physicsMessage.Flags, deserialized.Flags);
        Assert.Equal(physicsMessage.Channel, deserialized.Channel);
        Assert.True(deserialized.Payload.Span.SequenceEqual(physicsMessage.Payload.Span));
    }

    [Fact]
    public void RealWorldMessageScenarios_ResponseMessage_ShouldRoundTrip()
    {
        var requestMessage = BinaryMessage.FromJson(
            1,
            1,
            GuidGenerator.GenerateServiceGuid("session-123", "auth", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            "{\"action\":\"login\"}"
        );

        var responseMessage = BinaryMessage.CreateResponse(
            requestMessage,
            ResponseCodes.OK,
            Encoding.UTF8.GetBytes("{\"token\":\"jwt-token-here\"}")
        );

        var serialized = responseMessage.ToByteArray();
        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

        Assert.Equal(responseMessage.Flags, deserialized.Flags);
        Assert.True(deserialized.Payload.Span.SequenceEqual(responseMessage.Payload.Span));
    }

    [Fact]
    public void MetaFlagSupport_RegularMessage_ShouldNotBeMeta()
    {
        var regularMessage = BinaryMessage.FromJson(
            0,
            1,
            GuidGenerator.GenerateServiceGuid("session-123", "account", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            "{\"id\":\"test\"}"
        );

        Assert.False(regularMessage.IsMeta);
    }

    [Fact]
    public void MetaFlagSupport_MetaMessage_ShouldBeMeta()
    {
        var metaMessage = new BinaryMessage(
            MessageFlags.Meta,
            (ushort)MetaType.FullSchema,
            1,
            GuidGenerator.GenerateServiceGuid("session-123", "account", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        Assert.True(metaMessage.IsMeta);

        // Round-trip test
        var serialized = metaMessage.ToByteArray();
        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

        Assert.True(deserialized.IsMeta);
    }

    [Theory]
    [InlineData(MetaType.EndpointInfo)]
    [InlineData(MetaType.RequestSchema)]
    [InlineData(MetaType.ResponseSchema)]
    [InlineData(MetaType.FullSchema)]
    public void MetaTypeChannelEncoding_ShouldRoundTrip(MetaType metaType)
    {
        var message = new BinaryMessage(
            MessageFlags.Meta,
            (ushort)metaType,
            1,
            GuidGenerator.GenerateServiceGuid("session-123", "account", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        // Verify Channel matches MetaType
        Assert.Equal((ushort)metaType, message.Channel);

        // Round-trip
        var serialized = message.ToByteArray();
        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

        // Verify Channel survives serialization
        Assert.Equal((ushort)metaType, deserialized.Channel);

        // Verify can cast back to MetaType
        var extractedMetaType = (MetaType)deserialized.Channel;
        Assert.Equal(metaType, extractedMetaType);
    }
}
