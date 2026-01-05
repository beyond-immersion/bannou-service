using System;
using System.Text;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Cross-platform compatibility tests for the binary protocol.
/// These tests validate that the protocol works consistently across different endianness systems.
/// </summary>
public static class BinaryProtocolTests
{
    /// <summary>
    /// Comprehensive test to validate cross-platform binary protocol compatibility.
    /// This test should pass on all architectures (x86, x64, ARM, WebAssembly, etc.).
    /// </summary>
    public static bool RunCrossPlatformCompatibilityTests()
    {
        var results = new System.Collections.Generic.List<(string Test, bool Passed, string? Error)>();

        // Test 1: Network byte order utilities
        results.Add(RunTest("Network Byte Order Support", TestNetworkByteOrderSupport));
        results.Add(RunTest("Network Byte Order Operations", TestNetworkByteOrderOperations));

        // Test 2: Binary message serialization/deserialization
        results.Add(RunTest("Binary Message Round-Trip", TestBinaryMessageRoundTrip));
        results.Add(RunTest("Binary Message Edge Cases", TestBinaryMessageEdgeCases));

        // Test 3: Cross-endianness simulation
        results.Add(RunTest("Simulated Cross-Endianness", TestSimulatedCrossEndianness));

        // Test 4: GUID consistency
        results.Add(RunTest("GUID Network Order Consistency", TestGuidNetworkOrderConsistency));

        // Test 5: Real-world message scenarios
        results.Add(RunTest("Real-World Message Scenarios", TestRealWorldMessageScenarios));

        // Test 6: Meta flag and MetaType
        results.Add(RunTest("Meta Flag Support", TestMetaFlagSupport));
        results.Add(RunTest("Meta Type Channel Encoding", TestMetaTypeChannelEncoding));

        // Report results
        var totalTests = results.Count;
        var passedTests = 0;

        Console.WriteLine("=== Binary Protocol Cross-Platform Compatibility Tests ===");
        foreach (var (test, passed, error) in results)
        {
            var status = passed ? "✅ PASSED" : "❌ FAILED";
            Console.WriteLine($"{status}: {test}");
            if (!passed && !string.IsNullOrEmpty(error))
            {
                Console.WriteLine($"   Error: {error}");
            }
            if (passed) passedTests++;
        }

        Console.WriteLine($"\nResults: {passedTests}/{totalTests} tests passed");
        Console.WriteLine($"System Endianness: {NetworkByteOrder.GetSystemEndianness()}");

        return passedTests == totalTests;
    }

    private static (string, bool, string?) RunTest(string testName, Func<bool> testFunc)
    {
        try
        {
            var result = testFunc();
            return (testName, result, null);
        }
        catch (Exception ex)
        {
            return (testName, false, ex.Message);
        }
    }

    private static bool TestNetworkByteOrderSupport()
    {
        return NetworkByteOrder.IsNetworkByteOrderSupported() &&
                NetworkByteOrder.TestNetworkByteOrderCompatibility();
    }

    private static bool TestNetworkByteOrderOperations()
    {
        Span<byte> buffer = stackalloc byte[32];

        // Test UInt16
        NetworkByteOrder.WriteUInt16(buffer.Slice(0, 2), 0x1234);
        var readUInt16 = NetworkByteOrder.ReadUInt16(buffer.Slice(0, 2));
        if (readUInt16 != 0x1234) return false;

        // Verify actual byte order (should be big-endian)
        if (buffer[0] != 0x12 || buffer[1] != 0x34) return false;

        // Test UInt32
        NetworkByteOrder.WriteUInt32(buffer.Slice(2, 4), 0x12345678);
        var readUInt32 = NetworkByteOrder.ReadUInt32(buffer.Slice(2, 4));
        if (readUInt32 != 0x12345678) return false;

        // Verify actual byte order (should be big-endian)
        if (buffer[2] != 0x12 || buffer[3] != 0x34 || buffer[4] != 0x56 || buffer[5] != 0x78) return false;

        // Test UInt64
        NetworkByteOrder.WriteUInt64(buffer.Slice(6, 8), 0x123456789ABCDEF0);
        var readUInt64 = NetworkByteOrder.ReadUInt64(buffer.Slice(6, 8));
        if (readUInt64 != 0x123456789ABCDEF0) return false;

        return true;
    }

    private static bool TestBinaryMessageRoundTrip()
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
        return deserialized.Flags == originalMessage.Flags &&
                deserialized.Channel == originalMessage.Channel &&
                deserialized.SequenceNumber == originalMessage.SequenceNumber &&
                deserialized.ServiceGuid == originalMessage.ServiceGuid &&
                deserialized.MessageId == originalMessage.MessageId &&
                deserialized.Payload.Span.SequenceEqual(originalMessage.Payload.Span);
    }

    private static bool TestBinaryMessageEdgeCases()
    {
        // Test with empty payload (request message)
        var emptyMessage = new BinaryMessage(
            MessageFlags.Event,
            0,
            0,
            Guid.Empty,
            0,
            ReadOnlyMemory<byte>.Empty
        );

        var serialized = emptyMessage.ToByteArray();
        if (serialized.Length != BinaryMessage.HeaderSize) return false;

        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);
        if (deserialized.Payload.Length != 0) return false;

        // Test with maximum values (request message - exclude Response flag to use 31-byte header)
        // Note: Response flag (0x40) uses different 16-byte header format, so we test requests here
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

        return maxDeserialized.Flags == maxMessage.Flags &&
                maxDeserialized.Channel == maxMessage.Channel &&
                maxDeserialized.SequenceNumber == maxMessage.SequenceNumber &&
                maxDeserialized.ServiceGuid == maxMessage.ServiceGuid &&
                maxDeserialized.MessageId == maxMessage.MessageId;
    }

    private static bool TestSimulatedCrossEndianness()
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
        var originalJson = message.GetJsonPayload();
        var receivedJson = receivedMessage.GetJsonPayload();

        return message.Channel == receivedMessage.Channel &&
                message.SequenceNumber == receivedMessage.SequenceNumber &&
                message.ServiceGuid == receivedMessage.ServiceGuid &&
                message.MessageId == receivedMessage.MessageId &&
                originalJson == receivedJson;
    }

    private static bool TestGuidNetworkOrderConsistency()
    {
        var testGuids = new[]
        {
            Guid.Empty,
            new Guid("00000000-0000-0000-0000-000000000001"),
            new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
            new Guid("12345678-1234-5678-9ABC-DEF012345678"),
            Guid.NewGuid()
        };

        Span<byte> buffer = stackalloc byte[16]; // Move outside loop to fix CA2014

        foreach (var guid in testGuids)
        {
            NetworkByteOrder.WriteGuid(buffer, guid);
            var readGuid = NetworkByteOrder.ReadGuid(buffer);

            if (guid != readGuid) return false;
        }

        return true;
    }

    private static bool TestRealWorldMessageScenarios()
    {
        // Test typical authentication message
        var authMessage = BinaryMessage.FromJson(
            1, // Auth channel
            1, // First message
            GuidGenerator.GenerateServiceGuid("session-123", "auth", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            "{\"action\":\"login\",\"username\":\"test\",\"password\":\"secret\"}"
        );

        // Test binary data message (simulated game physics)
        var physicsData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0xFF, 0xFE, 0xFD, 0xFC };
        var physicsMessage = BinaryMessage.FromBinary(
            2, // Physics channel
            100,
            GuidGenerator.GenerateServiceGuid("session-456", "physics", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            physicsData
        );

        // Test response message
        var responseMessage = BinaryMessage.CreateResponse(
            authMessage,
            ResponseCodes.OK,
            Encoding.UTF8.GetBytes("{\"token\":\"jwt-token-here\"}")
        );

        // Serialize and deserialize all messages
        var scenarios = new[] { authMessage, physicsMessage, responseMessage };

        foreach (var scenario in scenarios)
        {
            var serialized = scenario.ToByteArray();
            var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

            if (!AreMessagesEqual(scenario, deserialized)) return false;
        }

        return true;
    }

    private static bool AreMessagesEqual(BinaryMessage a, BinaryMessage b)
    {
        return a.Flags == b.Flags &&
                a.Channel == b.Channel &&
                a.SequenceNumber == b.SequenceNumber &&
                a.ServiceGuid == b.ServiceGuid &&
                a.MessageId == b.MessageId &&
                a.Payload.Span.SequenceEqual(b.Payload.Span);
    }

    private static bool TestMetaFlagSupport()
    {
        // Test that Meta flag is correctly set and detected

        // Message without Meta flag
        var regularMessage = BinaryMessage.FromJson(
            0, // Channel
            1, // Sequence
            GuidGenerator.GenerateServiceGuid("session-123", "account", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            "{\"id\":\"test\"}"
        );

        if (regularMessage.IsMeta)
        {
            return false; // Regular message should not be meta
        }

        // Message with Meta flag
        var metaMessage = new BinaryMessage(
            MessageFlags.Meta,
            (ushort)MetaType.FullSchema,
            1,
            GuidGenerator.GenerateServiceGuid("session-123", "account", "server-salt"),
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>() // Meta requests have empty payload
        );

        if (!metaMessage.IsMeta)
        {
            return false; // Meta message should be detected
        }

        // Round-trip test
        var serialized = metaMessage.ToByteArray();
        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

        if (!deserialized.IsMeta)
        {
            return false; // Meta flag should survive serialization
        }

        return true;
    }

    private static bool TestMetaTypeChannelEncoding()
    {
        // Test that MetaType values are correctly encoded in Channel field
        var testCases = new[]
        {
            (MetaType.EndpointInfo, "endpoint-info"),
            (MetaType.RequestSchema, "request-schema"),
            (MetaType.ResponseSchema, "response-schema"),
            (MetaType.FullSchema, "full-schema")
        };

        foreach (var (metaType, _) in testCases)
        {
            var message = new BinaryMessage(
                MessageFlags.Meta,
                (ushort)metaType, // MetaType encoded in Channel
                1,
                GuidGenerator.GenerateServiceGuid("session-123", "account", "server-salt"),
                GuidGenerator.GenerateMessageId(),
                Array.Empty<byte>()
            );

            // Verify Channel matches MetaType
            if (message.Channel != (ushort)metaType)
            {
                return false;
            }

            // Round-trip
            var serialized = message.ToByteArray();
            var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

            // Verify Channel survives serialization
            if (deserialized.Channel != (ushort)metaType)
            {
                return false;
            }

            // Verify can cast back to MetaType
            var extractedMetaType = (MetaType)deserialized.Channel;
            if (extractedMetaType != metaType)
            {
                return false;
            }
        }

        return true;
    }
}
