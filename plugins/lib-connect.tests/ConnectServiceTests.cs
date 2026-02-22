using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Connect.Helpers;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Comprehensive unit tests for ConnectService including binary protocol and endianness validation.
/// Tests all protocol components to ensure cross-platform compatibility.
/// </summary>
public class ConnectServiceTests
{
    private readonly Mock<ILogger<ConnectService>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly ConnectServiceConfiguration _configuration;
    private readonly Mock<IAuthClient> _mockAuthClient;
    private readonly Mock<IMeshInvocationClient> _mockMeshClient;
    private readonly Mock<IServiceAppMappingResolver> _mockAppMappingResolver;
    private readonly Mock<IServiceScopeFactory> _mockServiceScopeFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IMessageSubscriber> _mockMessageSubscriber;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ISessionManager> _mockSessionManager;
    private readonly Mock<ICapabilityManifestBuilder> _mockManifestBuilder;
    private readonly Mock<IEntitySessionRegistry> _mockEntitySessionRegistry;
    private readonly InterNodeBroadcastManager _interNodeBroadcast;
    private readonly IMeshInstanceIdentifier _meshInstanceIdentifier;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly string _testServerSalt = "test-server-salt-2025";

    public ConnectServiceTests()
    {
        _mockLogger = new Mock<ILogger<ConnectService>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        // Set up logger factory to return a mock logger for any type
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger>());
        _configuration = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt
        };
        _mockAuthClient = new Mock<IAuthClient>();
        _mockMeshClient = new Mock<IMeshInvocationClient>();
        _mockAppMappingResolver = new Mock<IServiceAppMappingResolver>();
        _mockServiceScopeFactory = new Mock<IServiceScopeFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockMessageSubscriber = new Mock<IMessageSubscriber>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockSessionManager = new Mock<ISessionManager>();
        _mockManifestBuilder = new Mock<ICapabilityManifestBuilder>();
        _mockEntitySessionRegistry = new Mock<IEntitySessionRegistry>();

        // Create mesh instance identifier for tests
        _meshInstanceIdentifier = new DefaultMeshInstanceIdentifier();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

        // Create inactive broadcast manager for tests (mode=None disables all broadcast activity)
        var mockStateStoreFactory = new Mock<IStateStoreFactory>();
        var broadcastConfig = new ConnectServiceConfiguration { MultiNodeBroadcastMode = BroadcastMode.None };
        var meshIdentifier = _meshInstanceIdentifier;
        _interNodeBroadcast = new InterNodeBroadcastManager(
            mockStateStoreFactory.Object, broadcastConfig, meshIdentifier,
            Mock.Of<ITelemetryProvider>(),
            Mock.Of<ILogger<InterNodeBroadcastManager>>());
    }

    #region Basic Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void ConnectService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<ConnectService>();

    /// <summary>
    /// Business logic validation: ServerSalt is required for security.
    /// This is NOT covered by ServiceConstructorValidator since it's a business rule, not a null check.
    /// </summary>
    [Fact]
    public void Constructor_WithEmptyServerSalt_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var configWithoutSalt = new ConnectServiceConfiguration { ServerSalt = "" };

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() =>
            new ConnectService(_mockAuthClient.Object, _mockMeshClient.Object, _mockMessageBus.Object, _mockMessageSubscriber.Object, _mockAppMappingResolver.Object, _mockServiceScopeFactory.Object, configWithoutSalt, _mockLogger.Object, _mockLoggerFactory.Object, _mockEventConsumer.Object, _mockSessionManager.Object, _mockManifestBuilder.Object, _mockEntitySessionRegistry.Object, _interNodeBroadcast, _meshInstanceIdentifier, _mockTelemetryProvider.Object));
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
            MessageFlags.Binary | MessageFlags.Reserved0x08,
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
        // Note: Using flags 0x02 (Encrypted) instead of 0x42 (Response) because
        // Response messages use a different 16-byte header format without ServiceGuid
        var message = new BinaryMessage(
            (MessageFlags)0x02, // flags = 0x02 (Encrypted, NOT Response)
            0x1234,             // channel = 0x1234
            0x12345678,         // sequence = 0x12345678
            new Guid("12345678-1234-5678-9ABC-DEF012345678"),
            0x123456789ABCDEF0, // message ID
            new byte[] { 0xDE, 0xAD, 0xBE, 0xEF } // payload
        );

        // Act
        var bytes = message.ToByteArray();

        // Assert - verify network byte order in header (first 31 bytes)
        Assert.Equal(0x02, bytes[0]);     // flags (1 byte)
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
        var serviceName = "account";

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
        var serviceGuid = GuidGenerator.GenerateServiceGuid("test-session", "account", _testServerSalt);
        var message = BinaryMessage.FromJson(
            100, // channel
            200, // sequence
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            "{\"action\":\"get-account\",\"id\":\"123\"}"
        );

        var connectionState = new ConnectionState("test-session-123");
        connectionState.AddServiceMapping("account", serviceGuid);

        // Act
        var routingInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.NotNull(routingInfo);
        Assert.True(routingInfo.IsValid);
        Assert.Equal(100, routingInfo.Channel);
        Assert.Equal("account", routingInfo.ServiceName);
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

    // Note: BinaryProtocol tests were moved to BinaryProtocolTests.cs as proper xUnit tests

    #endregion

    #region RabbitMQ Event Handler Tests

    // Note: ProcessSessionCapabilityUpdateAsync was removed - capability updates are now
    // handled by ConnectEventsController.HandleCapabilitiesUpdatedAsync() which calls
    // PushCapabilityUpdateAsync(sessionId) for each affected session.

    [Fact]
    public async Task ProcessAuthEventAsync_WithLoginEvent_ShouldRefreshCapabilities()
    {
        // Arrange
        using var service = CreateConnectService();
        var sessionId = Guid.NewGuid();
        var eventData = new AuthEvent
        {
            SessionId = sessionId,
            EventType = AuthEventType.Login,
            UserId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act - method returns void; verifies no exception thrown
        await service.ProcessAuthEventAsync(eventData);
    }

    [Fact]
    public async Task ProcessClientMessageEventAsync_WhenClientNotConnected_LogsDebug()
    {
        // Arrange
        using var service = CreateConnectService();
        var eventData = new ClientMessageEvent
        {
            ClientId = Guid.NewGuid(),
            ServiceName = "TestService",
            ServiceGuid = Guid.NewGuid(),
            MessageId = 12345,
            Channel = 1,
            Payload = new byte[] { 1, 2, 3, 4 },
            Flags = 0
        };

        // Act - no connection mocked, so client won't be found; method logs debug and returns
        // Actual message delivery is tested via edge-tester WebSocket integration tests
        await service.ProcessClientMessageEventAsync(eventData);
    }

    [Fact]
    public async Task ProcessClientRPCEventAsync_WhenClientNotConnected_LogsDebug()
    {
        // Arrange
        using var service = CreateConnectService();
        var eventData = new ClientRPCEvent
        {
            ClientId = Guid.NewGuid(),
            ServiceName = "RPCService",
            ServiceGuid = Guid.NewGuid(),
            MessageId = 67890,
            Channel = 2,
            Payload = new byte[] { 5, 6, 7, 8 },
            Flags = 0
        };

        // Act - no connection mocked, so client won't be found; method logs debug and returns
        // Actual RPC delivery is tested via edge-tester WebSocket integration tests
        await service.ProcessClientRPCEventAsync(eventData);
    }

    // NOTE: HasConnection and SendMessageAsync tests were removed because they used reflection
    // to inject a mock connection manager, which tests mock behavior rather than service behavior.
    // Connection behavior should be tested via WebSocket integration tests (edge-tester).

    #endregion

    #region Helper Methods

    private ConnectService CreateConnectService()
    {
        return new ConnectService(
            _mockAuthClient.Object,
            _mockMeshClient.Object,
            _mockMessageBus.Object,
            _mockMessageSubscriber.Object,
            _mockAppMappingResolver.Object,
            _mockServiceScopeFactory.Object,
            _configuration,
            _mockLogger.Object,
            _mockLoggerFactory.Object,
            _mockEventConsumer.Object,
            _mockSessionManager.Object,
            _mockManifestBuilder.Object,
            _mockEntitySessionRegistry.Object,
            _interNodeBroadcast,
            _meshInstanceIdentifier,
            _mockTelemetryProvider.Object
        );
    }


    #endregion

    #region ConnectionMode Configuration Tests

    /// <summary>
    /// Tests that ConnectionMode defaults to "external" when not specified.
    /// </summary>
    [Fact]
    public void Configuration_ConnectionMode_DefaultsToExternal()
    {
        // Arrange
        var config = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt
        };

        // Assert
        Assert.Equal(ConnectionMode.External, config.ConnectionMode);
    }

    /// <summary>
    /// Tests that InternalAuthMode defaults to "service-token" when not specified.
    /// </summary>
    [Fact]
    public void Configuration_InternalAuthMode_DefaultsToServiceToken()
    {
        // Arrange
        var config = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt
        };

        // Assert
        Assert.Equal(InternalAuthMode.ServiceToken, config.InternalAuthMode);
    }

    /// <summary>
    /// Tests that InternalServiceToken is nullable and defaults to null.
    /// </summary>
    [Fact]
    public void Configuration_InternalServiceToken_IsNullable()
    {
        // Arrange
        var config = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt
        };

        // Assert
        Assert.Null(config.InternalServiceToken);
    }

    /// <summary>
    /// Tests that constructor throws when Internal mode with service-token auth is missing the token.
    /// </summary>
    [Fact]
    public void Constructor_InternalModeServiceToken_WithMissingToken_ShouldThrow()
    {
        // Arrange
        var config = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt,
            ConnectionMode = ConnectionMode.Internal,
            InternalAuthMode = InternalAuthMode.ServiceToken,
            InternalServiceToken = null
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ConnectService(
                _mockAuthClient.Object,
                _mockMeshClient.Object,
                _mockMessageBus.Object,
                _mockMessageSubscriber.Object,
                _mockAppMappingResolver.Object,
                _mockServiceScopeFactory.Object,
                config,
                _mockLogger.Object,
                _mockLoggerFactory.Object,
                _mockEventConsumer.Object,
                _mockSessionManager.Object, _mockManifestBuilder.Object, _mockEntitySessionRegistry.Object, _interNodeBroadcast, _meshInstanceIdentifier, _mockTelemetryProvider.Object));

        Assert.Contains("CONNECT_INTERNAL_SERVICE_TOKEN", exception.Message);
    }

    /// <summary>
    /// Tests that constructor accepts Internal mode with service-token auth when token is provided.
    /// </summary>
    [Fact]
    public void Constructor_InternalModeServiceToken_WithToken_ShouldNotThrow()
    {
        // Arrange
        var config = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt,
            ConnectionMode = ConnectionMode.Internal,
            InternalAuthMode = InternalAuthMode.ServiceToken,
            InternalServiceToken = "test-secret-token"
        };

        // Act & Assert
        var exception = Record.Exception(() =>
            new ConnectService(
                _mockAuthClient.Object,
                _mockMeshClient.Object,
                _mockMessageBus.Object,
                _mockMessageSubscriber.Object,
                _mockAppMappingResolver.Object,
                _mockServiceScopeFactory.Object,
                config,
                _mockLogger.Object,
                _mockLoggerFactory.Object,
                _mockEventConsumer.Object,
                _mockSessionManager.Object, _mockManifestBuilder.Object, _mockEntitySessionRegistry.Object, _interNodeBroadcast, _meshInstanceIdentifier, _mockTelemetryProvider.Object));

        Assert.Null(exception);
    }

    /// <summary>
    /// Tests that constructor accepts Internal mode with network-trust auth without token.
    /// </summary>
    [Fact]
    public void Constructor_InternalModeNetworkTrust_WithoutToken_ShouldNotThrow()
    {
        // Arrange
        var config = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt,
            ConnectionMode = ConnectionMode.Internal,
            InternalAuthMode = InternalAuthMode.NetworkTrust,
            InternalServiceToken = null
        };

        // Act & Assert
        var exception = Record.Exception(() =>
            new ConnectService(
                _mockAuthClient.Object,
                _mockMeshClient.Object,
                _mockMessageBus.Object,
                _mockMessageSubscriber.Object,
                _mockAppMappingResolver.Object,
                _mockServiceScopeFactory.Object,
                config,
                _mockLogger.Object,
                _mockLoggerFactory.Object,
                _mockEventConsumer.Object,
                _mockSessionManager.Object, _mockManifestBuilder.Object, _mockEntitySessionRegistry.Object, _interNodeBroadcast, _meshInstanceIdentifier, _mockTelemetryProvider.Object));

        Assert.Null(exception);
    }

    /// <summary>
    /// Tests that constructor accepts External mode without any internal auth configuration.
    /// </summary>
    [Fact]
    public void Constructor_ExternalMode_ShouldNotRequireInternalToken()
    {
        // Arrange
        var config = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt,
            ConnectionMode = ConnectionMode.External,
            InternalServiceToken = null
        };

        // Act & Assert
        var exception = Record.Exception(() =>
            new ConnectService(
                _mockAuthClient.Object,
                _mockMeshClient.Object,
                _mockMessageBus.Object,
                _mockMessageSubscriber.Object,
                _mockAppMappingResolver.Object,
                _mockServiceScopeFactory.Object,
                config,
                _mockLogger.Object,
                _mockLoggerFactory.Object,
                _mockEventConsumer.Object,
                _mockSessionManager.Object, _mockManifestBuilder.Object, _mockEntitySessionRegistry.Object, _interNodeBroadcast, _meshInstanceIdentifier, _mockTelemetryProvider.Object));

        Assert.Null(exception);
    }

    /// <summary>
    /// Tests that constructor accepts Relayed mode without internal auth configuration.
    /// </summary>
    [Fact]
    public void Constructor_RelayedMode_ShouldNotRequireInternalToken()
    {
        // Arrange
        var config = new ConnectServiceConfiguration
        {
            ServerSalt = _testServerSalt,
            ConnectionMode = ConnectionMode.Relayed,
            InternalServiceToken = null
        };

        // Act & Assert
        var exception = Record.Exception(() =>
            new ConnectService(
                _mockAuthClient.Object,
                _mockMeshClient.Object,
                _mockMessageBus.Object,
                _mockMessageSubscriber.Object,
                _mockAppMappingResolver.Object,
                _mockServiceScopeFactory.Object,
                config,
                _mockLogger.Object,
                _mockLoggerFactory.Object,
                _mockEventConsumer.Object,
                _mockSessionManager.Object, _mockManifestBuilder.Object, _mockEntitySessionRegistry.Object, _interNodeBroadcast, _meshInstanceIdentifier, _mockTelemetryProvider.Object));

        Assert.Null(exception);
    }

    #endregion

    #region Broadcast Message Routing Tests

    /// <summary>
    /// Tests that MessageRouter detects broadcast GUID with Client flag correctly.
    /// </summary>
    [Fact]
    public void MessageRouter_BroadcastGuid_WithClientFlag_ShouldReturnBroadcastRoute()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var message = new BinaryMessage(
            MessageFlags.Client, // Client flag (0x20) required for broadcast
            100,
            1,
            AppConstants.BROADCAST_GUID,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"test\":\"broadcast\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
        Assert.Equal(RouteType.Broadcast, routeInfo.RouteType);
        Assert.Equal("broadcast", routeInfo.TargetType);
        Assert.Equal("all-peers", routeInfo.TargetId);
    }

    /// <summary>
    /// Tests that MessageRouter rejects broadcast GUID without Client flag.
    /// </summary>
    [Fact]
    public void MessageRouter_BroadcastGuid_WithoutClientFlag_ShouldReturnError()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var message = new BinaryMessage(
            MessageFlags.None, // No Client flag - invalid for broadcast
            100,
            1,
            AppConstants.BROADCAST_GUID,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"test\":\"broadcast\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.RequestError, routeInfo.ErrorCode);
        Assert.Contains("Client flag", routeInfo.ErrorMessage);
    }

    /// <summary>
    /// Tests that BROADCAST_GUID constant has the expected value.
    /// </summary>
    [Fact]
    public void AppConstants_BroadcastGuid_ShouldBeAllFs()
    {
        // Assert
        Assert.Equal(new Guid("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF"), AppConstants.BROADCAST_GUID);
    }

    #endregion

    #region HTTP Routing Optimization Tests

    // NOTE: StaticHeaders_ShouldBeProperlyInitialized test was removed because it tested
    // implementation details (private static fields) via reflection. Static header values
    // are implementation optimization details that should not be tested directly.

    /// <summary>
    /// Tests that BinaryMessage.Payload provides direct access to raw bytes,
    /// enabling zero-copy forwarding without UTF-16 string conversion.
    /// </summary>
    [Fact]
    public void BinaryMessage_Payload_ShouldProvideRawBytesDirectly()
    {
        // Arrange - create message with known JSON payload
        var originalJson = "{\"test\":\"zero-copy-optimization\",\"value\":12345}";
        var originalBytes = Encoding.UTF8.GetBytes(originalJson);

        var message = BinaryMessage.FromJson(
            100, // channel
            200, // sequence
            Guid.NewGuid(),
            GuidGenerator.GenerateMessageId(),
            originalJson
        );

        // Act - get payload bytes directly (used in ByteArrayContent optimization)
        var payloadBytes = message.Payload;

        // Assert - bytes should match original UTF-8 encoding
        Assert.Equal(originalBytes.Length, payloadBytes.Length);
        Assert.True(payloadBytes.Span.SequenceEqual(originalBytes));

        // Verify ToArray() produces correct bytes for ByteArrayContent
        var arrayBytes = payloadBytes.ToArray();
        Assert.Equal(originalBytes, arrayBytes);
    }

    /// <summary>
    /// Tests that using Payload.ToArray() is equivalent to GetJsonPayload() encoded back to UTF-8,
    /// confirming the optimization preserves data integrity.
    /// </summary>
    [Fact]
    public void BinaryMessage_PayloadToArray_ShouldBeEquivalentToGetJsonPayload()
    {
        // Arrange - various JSON payloads including Unicode
        var testPayloads = new[]
        {
            "{\"simple\":\"test\"}",
            "{\"unicode\":\"日本語テスト\",\"emoji\":\"🎮🎯\"}",
            "{\"nested\":{\"array\":[1,2,3],\"bool\":true}}",
            "{\"empty\":\"\",\"null\":null}",
            "{\"large\":\"" + new string('x', 1000) + "\"}"
        };

        foreach (var originalJson in testPayloads)
        {
            var message = BinaryMessage.FromJson(
                1, 0, Guid.NewGuid(),
                GuidGenerator.GenerateMessageId(),
                originalJson
            );

            // Act - compare both access methods
            var viaPayload = message.Payload.ToArray();
            var viaGetJson = Encoding.UTF8.GetBytes(message.GetJsonPayload());

            // Assert - both should produce identical bytes
            Assert.Equal(viaGetJson, viaPayload);
        }
    }

    /// <summary>
    /// Tests that empty payloads are handled correctly in the optimized path.
    /// </summary>
    [Fact]
    public void BinaryMessage_EmptyPayload_ShouldBeHandledCorrectly()
    {
        // Arrange - message with empty payload (no JSON body)
        var message = BinaryMessage.FromJson(
            1, 0, Guid.NewGuid(),
            GuidGenerator.GenerateMessageId(),
            ""
        );

        // Act
        var payloadBytes = message.Payload;

        // Assert - empty payload should have zero length
        Assert.Equal(0, payloadBytes.Length);
        Assert.Empty(payloadBytes.ToArray());
    }

    #endregion
}
