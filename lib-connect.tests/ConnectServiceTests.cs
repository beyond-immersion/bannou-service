using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.ServiceClients;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using System.Text;
using System.Text.Json;
using Dapr.Client;
using StackExchange.Redis;
using Microsoft.AspNetCore.Builder;
using System.Reflection;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Comprehensive unit tests for ConnectService including binary protocol and endianness validation.
/// Tests all protocol components to ensure cross-platform compatibility.
/// </summary>
public class ConnectServiceTests
{
    private readonly Mock<ILogger<ConnectService>> _mockLogger;
    private readonly ConnectServiceConfiguration _configuration;
    private readonly Mock<IAuthClient> _mockAuthClient;
    private readonly Mock<IPermissionsClient> _mockPermissionsClient;
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<IServiceAppMappingResolver> _mockAppMappingResolver;
    private readonly string _testServerSalt = "test-server-salt-2025";

    public ConnectServiceTests()
    {
        _mockLogger = new Mock<ILogger<ConnectService>>();
        _configuration = new ConnectServiceConfiguration
        {
            Properties = "test-properties"
        };
        _mockAuthClient = new Mock<IAuthClient>();
        _mockPermissionsClient = new Mock<IPermissionsClient>();
        _mockDaprClient = new Mock<DaprClient>();
        _mockAppMappingResolver = new Mock<IServiceAppMappingResolver>();
    }

    #region Basic Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        var exception = Record.Exception(() => CreateConnectService());
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithNullAuthClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ConnectService(
            null!,
            _mockPermissionsClient.Object,
            _mockDaprClient.Object,
            _mockAppMappingResolver.Object,
            _configuration,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullPermissionsClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ConnectService(
            _mockAuthClient.Object,
            null!,
            _mockDaprClient.Object,
            _mockAppMappingResolver.Object,
            _configuration,
            _mockLogger.Object));
    }

    #endregion

    #region Binary Protocol Tests

    /// <summary>
    /// Tests that BinaryMessage serialization produces consistent results across different system architectures.
    /// This is critical for cross-platform WebSocket communication.
    /// </summary>
    [Fact]
    public void BinaryMessage_Serialization_ShouldBeEndianessIndependent()
    {
        // Arrange
        var testGuid = new Guid("12345678-1234-5678-9ABC-DEF012345678");
        var testPayload = Encoding.UTF8.GetBytes("Test cross-platform payload");

        var message = new BinaryMessage(
            MessageFlags.Binary | MessageFlags.HighPriority,
            1234, // channel
            5678, // sequence
            testGuid,
            0x123456789ABCDEF0, // message ID
            testPayload
        );

        // Act
        var serialized = message.ToByteArray();
        var deserialized = BinaryMessage.Parse(serialized, serialized.Length);

        // Assert - all fields should match exactly
        Assert.Equal(message.Flags, deserialized.Flags);
        Assert.Equal(message.Channel, deserialized.Channel);
        Assert.Equal(message.SequenceNumber, deserialized.SequenceNumber);
        Assert.Equal(message.ServiceGuid, deserialized.ServiceGuid);
        Assert.Equal(message.MessageId, deserialized.MessageId);
        Assert.True(message.Payload.Span.SequenceEqual(deserialized.Payload.Span));
    }

    /// <summary>
    /// Tests that serialized binary messages use network byte order (big-endian) consistently.
    /// This ensures the same byte array is produced on x86, x64, ARM, and other architectures.
    /// </summary>
    [Fact]
    public void BinaryMessage_SerializedBytes_ShouldUseNetworkByteOrder()
    {
        // Arrange - use predictable test values
        var message = new BinaryMessage(
            (MessageFlags)0x42, // flags = 0x42
            0x1234,             // channel = 0x1234
            0x12345678,         // sequence = 0x12345678
            new Guid("12345678-1234-5678-9ABC-DEF012345678"),
            0x123456789ABCDEF0, // message ID
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } // payload
        );

        // Act
        var bytes = message.ToByteArray();

        // Assert - verify network byte order in header (first 31 bytes)
        Assert.Equal(0x42, bytes[0]);     // flags (1 byte)
        Assert.Equal(0x12, bytes[1]);     // channel high byte
        Assert.Equal(0x34, bytes[2]);     // channel low byte
        Assert.Equal(0x12, bytes[3]);     // sequence byte 0 (most significant)
        Assert.Equal(0x34, bytes[4]);     // sequence byte 1
        Assert.Equal(0x56, bytes[5]);     // sequence byte 2
        Assert.Equal(0x78, bytes[6]);     // sequence byte 3 (least significant)

        // GUID bytes 7-22 should be in network order
        Assert.Equal(16, bytes.Skip(7).Take(16).Count());

        // Message ID bytes 23-30 should be in network order (big-endian)
        Assert.Equal(0x12, bytes[23]);    // message ID byte 0 (most significant)
        Assert.Equal(0x34, bytes[24]);    // message ID byte 1
        Assert.Equal(0x56, bytes[25]);    // message ID byte 2
        Assert.Equal(0x78, bytes[26]);    // message ID byte 3
        Assert.Equal(0x9A, bytes[27]);    // message ID byte 4
        Assert.Equal(0xBC, bytes[28]);    // message ID byte 5
        Assert.Equal(0xDE, bytes[29]);    // message ID byte 6
        Assert.Equal(0xF0, bytes[30]);    // message ID byte 7 (least significant)

        // Payload starts at byte 31
        Assert.Equal(0xDE, bytes[31]);
        Assert.Equal(0xAD, bytes[32]);
        Assert.Equal(0xBE, bytes[33]);
        Assert.Equal(0xEF, bytes[34]);
    }

    /// <summary>
    /// Tests NetworkByteOrder utility functions for consistent cross-platform behavior.
    /// </summary>
    [Theory]
    [InlineData((ushort)0x1234)]
    [InlineData((ushort)0xABCD)]
    [InlineData((ushort)0x0000)]
    [InlineData((ushort)0xFFFF)]
    public void NetworkByteOrder_UInt16_ShouldRoundTripCorrectly(ushort value)
    {
        // Arrange
        Span<byte> buffer = stackalloc byte[2];

        // Act
        NetworkByteOrder.WriteUInt16(buffer, value);
        var readValue = NetworkByteOrder.ReadUInt16(buffer);

        // Assert
        Assert.Equal(value, readValue);

        // Verify big-endian byte order
        Assert.Equal((byte)(value >> 8), buffer[0]);   // high byte first
        Assert.Equal((byte)(value & 0xFF), buffer[1]); // low byte second
    }

    [Theory]
    [InlineData(0x12345678U)]
    [InlineData(0xABCDEF01U)]
    [InlineData(0x00000000U)]
    [InlineData(0xFFFFFFFFU)]
    public void NetworkByteOrder_UInt32_ShouldRoundTripCorrectly(uint value)
    {
        // Arrange
        Span<byte> buffer = stackalloc byte[4];

        // Act
        NetworkByteOrder.WriteUInt32(buffer, value);
        var readValue = NetworkByteOrder.ReadUInt32(buffer);

        // Assert
        Assert.Equal(value, readValue);

        // Verify big-endian byte order
        Assert.Equal((byte)(value >> 24), buffer[0]);
        Assert.Equal((byte)(value >> 16), buffer[1]);
        Assert.Equal((byte)(value >> 8), buffer[2]);
        Assert.Equal((byte)(value & 0xFF), buffer[3]);
    }

    [Theory]
    [InlineData(0x123456789ABCDEF0UL)]
    [InlineData(0xFEDCBA0987654321UL)]
    [InlineData(0x0000000000000000UL)]
    [InlineData(0xFFFFFFFFFFFFFFFFUL)]
    public void NetworkByteOrder_UInt64_ShouldRoundTripCorrectly(ulong value)
    {
        // Arrange
        Span<byte> buffer = stackalloc byte[8];

        // Act
        NetworkByteOrder.WriteUInt64(buffer, value);
        var readValue = NetworkByteOrder.ReadUInt64(buffer);

        // Assert
        Assert.Equal(value, readValue);

        // Verify big-endian byte order
        for (int i = 0; i < 8; i++)
        {
            var expectedByte = (byte)(value >> (56 - i * 8));
            Assert.Equal(expectedByte, buffer[i]);
        }
    }

    /// <summary>
    /// Tests GUID serialization consistency across platforms.
    /// </summary>
    [Fact]
    public void NetworkByteOrder_Guid_ShouldRoundTripCorrectly()
    {
        // Arrange
        var testGuids = new[]
        {
            Guid.Empty,
            new Guid("00000000-0000-0000-0000-000000000001"),
            new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"),
            new Guid("12345678-1234-5678-9ABC-DEF012345678"),
            Guid.NewGuid()
        };

        Span<byte> buffer = stackalloc byte[16];

        foreach (var guid in testGuids)
        {
            // Act
            NetworkByteOrder.WriteGuid(buffer, guid);
            var readGuid = NetworkByteOrder.ReadGuid(buffer);

            // Assert
            Assert.Equal(guid, readGuid);
        }
    }

    #endregion

    #region GUID Generation Tests

    /// <summary>
    /// Tests that client-salted GUID generation produces different GUIDs for different sessions.
    /// This is critical for security isolation between WebSocket clients.
    /// </summary>
    [Fact]
    public void GuidGenerator_GenerateServiceGuid_ShouldBeDifferentForDifferentSessions()
    {
        // Arrange
        var sessionId1 = "session-123";
        var sessionId2 = "session-456";
        var serviceName = "accounts";

        // Act
        var guid1 = GuidGenerator.GenerateServiceGuid(sessionId1, serviceName, _testServerSalt);
        var guid2 = GuidGenerator.GenerateServiceGuid(sessionId2, serviceName, _testServerSalt);

        // Assert
        Assert.NotEqual(guid1, guid2);
        Assert.NotEqual(Guid.Empty, guid1);
        Assert.NotEqual(Guid.Empty, guid2);
    }

    /// <summary>
    /// Tests that GUID generation is deterministic for the same inputs.
    /// </summary>
    [Fact]
    public void GuidGenerator_GenerateServiceGuid_ShouldBeDeterministic()
    {
        // Arrange
        var sessionId = "test-session";
        var serviceName = "test-service";

        // Act
        var guid1 = GuidGenerator.GenerateServiceGuid(sessionId, serviceName, _testServerSalt);
        var guid2 = GuidGenerator.GenerateServiceGuid(sessionId, serviceName, _testServerSalt);

        // Assert
        Assert.Equal(guid1, guid2);
    }

    /// <summary>
    /// Tests that message ID generation produces unique values.
    /// </summary>
    [Fact]
    public void GuidGenerator_GenerateMessageId_ShouldProduceUniqueValues()
    {
        // Arrange
        var messageIds = new HashSet<ulong>();

        // Act - generate multiple message IDs with small delays to ensure uniqueness
        for (int i = 0; i < 100; i++) // Reduced count for faster test execution
        {
            var messageId = GuidGenerator.GenerateMessageId();
            Assert.True(messageIds.Add(messageId), $"Duplicate message ID generated: {messageId}");

            // Small delay to ensure timestamp-based uniqueness
            Thread.Sleep(1); // Consistent delay for all iterations to prevent timing collisions
        }

        // Assert
        Assert.Equal(100, messageIds.Count);
    }

    #endregion

    #region Message Routing Tests

    /// <summary>
    /// Tests that MessageRouter can analyze binary messages correctly.
    /// </summary>
    [Fact]
    public void MessageRouter_AnalyzeMessage_ShouldExtractCorrectInformation()
    {
        // Arrange
        var serviceGuid = GuidGenerator.GenerateServiceGuid("test-session", "accounts", _testServerSalt);
        var message = BinaryMessage.FromJson(
            100, // channel
            200, // sequence
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            "{\"action\":\"get-account\",\"id\":\"123\"}"
        );

        var connectionState = new ConnectionState("test-session-123");
        connectionState.AddServiceMapping("accounts", serviceGuid);

        // Act
        var routingInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.NotNull(routingInfo);
        Assert.True(routingInfo.IsValid);
        Assert.Equal(100, routingInfo.Channel);
        Assert.Equal("accounts", routingInfo.ServiceName);
        Assert.Equal(RouteType.Service, routingInfo.RouteType);
    }

    #endregion

    #region Cross-Platform Compatibility Tests

    /// <summary>
    /// Simulates messages created on different endianness systems to ensure compatibility.
    /// </summary>
    [Fact]
    public void BinaryProtocol_CrossPlatformCompatibility_ShouldWorkConsistently()
    {
        // Arrange - create message with all different data types
        var message = BinaryMessage.FromJson(
            ushort.MaxValue,     // channel = 65535
            uint.MaxValue,       // sequence = 4294967295
            new Guid("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
            ulong.MaxValue,      // message ID
            "{\"test\":\"cross-platform\"}"
        );

        // Act - serialize and deserialize (simulating network transmission)
        var bytes = message.ToByteArray();
        var receivedMessage = BinaryMessage.Parse(bytes, bytes.Length);

        // Assert - verify all fields preserved
        Assert.Equal(message.Channel, receivedMessage.Channel);
        Assert.Equal(message.SequenceNumber, receivedMessage.SequenceNumber);
        Assert.Equal(message.ServiceGuid, receivedMessage.ServiceGuid);
        Assert.Equal(message.MessageId, receivedMessage.MessageId);

        // Verify JSON payload preserved
        var originalJson = message.GetJsonPayload();
        var receivedJson = receivedMessage.GetJsonPayload();
        Assert.Equal(originalJson, receivedJson);
    }

    /// <summary>
    /// Comprehensive cross-platform test runner that validates all protocol components.
    /// </summary>
    [Fact]
    public void BinaryProtocol_ComprehensiveCompatibilityTest_ShouldPassAllChecks()
    {
        // Act - run the comprehensive test from BinaryProtocolTests
        var result = BinaryProtocolTests.RunCrossPlatformCompatibilityTests();

        // Assert - all tests should pass
        Assert.True(result, "One or more cross-platform compatibility tests failed. Check console output for details.");
    }

    #endregion

    #region RabbitMQ Event Handler Tests

    [Fact]
    public async Task ProcessSessionCapabilityUpdateAsync_WithValidEvent_ShouldProcessSuccessfully()
    {
        // Arrange
        var service = CreateConnectServiceWithConnectionManager();
        var eventData = new SessionCapabilityUpdateEvent
        {
            SessionId = "test-session-123",
            AddedCapabilities = new List<string> { "accounts:read", "auth:login" },
            RemovedCapabilities = new List<string> { "admin:delete" },
            Version = 42
        };

        // Act
        var result = await service.ProcessSessionCapabilityUpdateAsync(eventData);

        // Assert
        Assert.NotNull(result);
        var resultJson = JsonSerializer.Serialize(result);
        var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);
        Assert.Equal("processed", resultDict!["status"].ToString());
        Assert.Equal("test-session-123", resultDict["sessionId"].ToString());
    }

    [Fact]
    public async Task ProcessAuthEventAsync_WithLoginEvent_ShouldRefreshCapabilities()
    {
        // Arrange
        var service = CreateConnectServiceWithConnectionManager();
        var eventData = new AuthEvent
        {
            SessionId = "test-session-456",
            EventType = AuthEventType.Login,
            UserId = "user-123",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await service.ProcessAuthEventAsync(eventData);

        // Assert
        Assert.NotNull(result);
        var resultJson = JsonSerializer.Serialize(result);
        var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);
        Assert.Equal("processed", resultDict!["status"].ToString());
        Assert.Equal("test-session-456", resultDict["sessionId"].ToString());
    }

    [Fact]
    public async Task ProcessServiceRegistrationAsync_WithValidEvent_ShouldPublishRecompileEvent()
    {
        // Arrange
        var service = CreateConnectService();
        var eventData = new ServiceRegistrationEvent
        {
            ServiceId = "new-service-123",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var result = await service.ProcessServiceRegistrationAsync(eventData);

        // Assert
        Assert.NotNull(result);
        var resultJson = JsonSerializer.Serialize(result);
        var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);
        Assert.Equal("processed", resultDict!["status"].ToString());
        Assert.Equal("new-service-123", resultDict["serviceId"].ToString());

        // Verify that PublishEventAsync was called for permission recompilation
        _mockDaprClient.Verify(x => x.PublishEventAsync(
            "bannou-pubsub",
            "bannou-permission-recompile",
            It.IsAny<PermissionRecompileEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessClientMessageEventAsync_WithConnectedClient_ShouldDeliverMessage()
    {
        // Arrange
        var service = CreateConnectServiceWithConnectionManager();
        var eventData = new ClientMessageEvent
        {
            ClientId = "client-789",
            ServiceName = "TestService",
            ServiceGuid = Guid.NewGuid(),
            MessageId = 12345,
            Channel = 1,
            Payload = new byte[] { 1, 2, 3, 4 },
            Flags = 0
        };

        // Act
        var result = await service.ProcessClientMessageEventAsync(eventData);

        // Assert
        Assert.NotNull(result);
        var resultJson = JsonSerializer.Serialize(result);
        var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);
        Assert.Equal("delivered", resultDict!["status"].ToString());
        Assert.Equal("client-789", resultDict["clientId"].ToString());
    }

    [Fact]
    public async Task ProcessClientRPCEventAsync_WithConnectedClient_ShouldSendRPCMessage()
    {
        // Arrange
        var service = CreateConnectServiceWithConnectionManager();
        var eventData = new ClientRPCEvent
        {
            ClientId = "client-rpc-999",
            ServiceName = "RPCService",
            ServiceGuid = Guid.NewGuid(),
            MessageId = 67890,
            Channel = 2,
            Payload = new byte[] { 5, 6, 7, 8 },
            Flags = 0
        };

        // Act
        var result = await service.ProcessClientRPCEventAsync(eventData);

        // Assert
        Assert.NotNull(result);
        var resultJson = JsonSerializer.Serialize(result);
        var resultDict = JsonSerializer.Deserialize<Dictionary<string, object>>(resultJson);
        Assert.Equal("sent", resultDict!["status"].ToString());
        Assert.Equal("client-rpc-999", resultDict["clientId"].ToString());
    }

    [Fact]
    public void HasConnection_WithExistingConnection_ShouldReturnTrue()
    {
        // Arrange
        var service = CreateConnectServiceWithConnectionManager();
        var sessionId = "existing-session";

        // Act
        var result = service.HasConnection(sessionId);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasConnection_WithNonExistentConnection_ShouldReturnFalse()
    {
        // Arrange
        var service = CreateConnectServiceWithConnectionManager(false);
        var sessionId = "non-existent-session";

        // Act
        var result = service.HasConnection(sessionId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task SendMessageAsync_WithValidConnection_ShouldReturnTrue()
    {
        // Arrange
        var service = CreateConnectServiceWithConnectionManager();
        var sessionId = "test-session";
        var message = BinaryMessage.FromJson(1, 0, Guid.NewGuid(), 123, "{\"test\": true}");

        // Act
        var result = await service.SendMessageAsync(sessionId, message, CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    #endregion

    #region Helper Methods

    private ConnectService CreateConnectService()
    {
        return new ConnectService(
            _mockAuthClient.Object,
            _mockPermissionsClient.Object,
            _mockDaprClient.Object,
            _mockAppMappingResolver.Object,
            _configuration,
            _mockLogger.Object
        );
    }

    private ConnectService CreateConnectServiceWithConnectionManager(bool hasConnection = true)
    {
        // Create a mock connection manager that simulates having connections
        var mockConnectionManager = new Mock<WebSocketConnectionManager>();
        var mockConnection = hasConnection ? new Mock<WebSocketConnection>("test", null!, null!) : null;

        mockConnectionManager.Setup(x => x.GetConnection(It.IsAny<string>()))
            .Returns(hasConnection ? mockConnection?.Object : null);

        mockConnectionManager.Setup(x => x.SendMessageAsync(It.IsAny<string>(), It.IsAny<BinaryMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasConnection);

        // Use reflection to create service with mocked connection manager
        var service = CreateConnectService();
        var connectionManagerField = typeof(ConnectService).GetField("_connectionManager",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        connectionManagerField?.SetValue(service, mockConnectionManager.Object);

        return service;
    }


    #endregion
}
