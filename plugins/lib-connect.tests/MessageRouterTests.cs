using BeyondImmersion.BannouService.Connect.Protocol;
using Xunit;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for MessageRouter branches not covered by ConnectServiceTests or SessionShortcutTests.
/// Covers: ServiceNotFound, channel validation, empty payload rejection, client routing,
/// rate limiting, and missing shortcut field validation.
/// </summary>
public class MessageRouterTests
{
    private const string TEST_SERVER_SALT = "test-server-salt-abc123";

    #region ServiceNotFound Tests

    /// <summary>
    /// Verifies that a service GUID not in session mappings returns ServiceNotFound.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_UnknownServiceGuid_ReturnsServiceNotFound()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var unknownGuid = Guid.NewGuid();

        var message = new BinaryMessage(
            MessageFlags.None, // Service routing (no Client flag)
            1, 1,
            unknownGuid,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"action\":\"test\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.ServiceNotFound, routeInfo.ErrorCode);
        Assert.Contains(unknownGuid.ToString(), routeInfo.ErrorMessage);
    }

    #endregion

    #region Channel Validation Tests

    /// <summary>
    /// Verifies that a channel exceeding maxChannelNumber returns InvalidRequestChannel.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_ChannelExceedsMax_ReturnsInvalidRequestChannel()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", TEST_SERVER_SALT);
        connectionState.AddServiceMapping("account", serviceGuid);

        var message = new BinaryMessage(
            MessageFlags.None,
            1001, // channel > default maxChannelNumber (1000)
            1,
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"action\":\"test\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.InvalidRequestChannel, routeInfo.ErrorCode);
        Assert.Contains("1001", routeInfo.ErrorMessage);
    }

    /// <summary>
    /// Verifies that a channel exactly at maxChannelNumber is valid.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_ChannelAtMax_IsValid()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", TEST_SERVER_SALT);
        connectionState.AddServiceMapping("account", serviceGuid);

        var message = new BinaryMessage(
            MessageFlags.None,
            1000, // exactly at maxChannelNumber
            1,
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"action\":\"test\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
        Assert.Equal(1000, routeInfo.Channel);
    }

    /// <summary>
    /// Verifies that a custom maxChannelNumber is respected.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_CustomMaxChannel_IsRespected()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", TEST_SERVER_SALT);
        connectionState.AddServiceMapping("account", serviceGuid);

        var message = new BinaryMessage(
            MessageFlags.None,
            51, // channel > custom max of 50
            1,
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"action\":\"test\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState, maxChannelNumber: 50);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.InvalidRequestChannel, routeInfo.ErrorCode);
    }

    #endregion

    #region Empty Payload Tests

    /// <summary>
    /// Verifies that a non-shortcut, non-event, non-meta message with empty payload is rejected.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_EmptyPayload_NonEvent_ReturnsRequestError()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", TEST_SERVER_SALT);
        connectionState.AddServiceMapping("account", serviceGuid);

        var message = new BinaryMessage(
            MessageFlags.None, // Not event, not meta
            1, 1,
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>() // Empty payload
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.RequestError, routeInfo.ErrorCode);
        Assert.Contains("payload is required", routeInfo.ErrorMessage);
    }

    /// <summary>
    /// Verifies that an Event-flagged message with empty payload is allowed.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_EmptyPayload_EventFlag_IsAllowed()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", TEST_SERVER_SALT);
        connectionState.AddServiceMapping("account", serviceGuid);

        var message = new BinaryMessage(
            MessageFlags.Event, // Event flag allows empty payload
            1, 1,
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
    }

    /// <summary>
    /// Verifies that a Meta-flagged message with empty payload is allowed.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_EmptyPayload_MetaFlag_IsAllowed()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", TEST_SERVER_SALT);
        connectionState.AddServiceMapping("account", serviceGuid);

        var message = new BinaryMessage(
            MessageFlags.Meta, // Meta flag allows empty payload
            1, 1,
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
    }

    #endregion

    #region Client Routing Tests

    /// <summary>
    /// Verifies that a Client-flagged message (non-broadcast) routes as Client type.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_ClientFlag_RoutesAsClient()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var targetClientGuid = Guid.NewGuid(); // Not the broadcast GUID

        var message = new BinaryMessage(
            MessageFlags.Client, // Client flag for peer routing
            1, 1,
            targetClientGuid,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"data\":\"test\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
        Assert.Equal(RouteType.Client, routeInfo.RouteType);
        Assert.Equal("client", routeInfo.TargetType);
        Assert.Equal(targetClientGuid.ToString(), routeInfo.TargetId);
    }

    /// <summary>
    /// Verifies that Client routing still validates the channel number.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_ClientFlag_ChannelExceedsMax_ReturnsError()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");

        var message = new BinaryMessage(
            MessageFlags.Client,
            1001, // exceeds default max
            1,
            Guid.NewGuid(),
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"data\":\"test\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.InvalidRequestChannel, routeInfo.ErrorCode);
    }

    #endregion

    #region ExpectsResponse Tests

    /// <summary>
    /// Verifies RequiresResponse is true for a standard service request (no Event/Response flags).
    /// </summary>
    [Fact]
    public void AnalyzeMessage_StandardRequest_RequiresResponse()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", TEST_SERVER_SALT);
        connectionState.AddServiceMapping("account", serviceGuid);

        var message = new BinaryMessage(
            MessageFlags.None, // No Event or Response flag → ExpectsResponse = true
            1, 1,
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"action\":\"get\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
        Assert.True(routeInfo.RequiresResponse);
    }

    /// <summary>
    /// Verifies RequiresResponse is false for Event-flagged messages.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_EventFlag_DoesNotRequireResponse()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);
        var serviceGuid = GuidGenerator.GenerateServiceGuid(sessionId, "account", TEST_SERVER_SALT);
        connectionState.AddServiceMapping("account", serviceGuid);

        var message = new BinaryMessage(
            MessageFlags.Event, // Event flag → ExpectsResponse = false
            1, 1,
            serviceGuid,
            GuidGenerator.GenerateMessageId(),
            System.Text.Encoding.UTF8.GetBytes("{\"event\":\"test\"}")
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.True(routeInfo.IsValid);
        Assert.False(routeInfo.RequiresResponse);
    }

    #endregion

    #region Shortcut Missing Fields Tests

    /// <summary>
    /// Verifies that a shortcut missing TargetMethod returns ShortcutTargetNotFound error.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_ShortcutMissingTargetMethod_ReturnsError()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);

        var shortcutGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionId, "no_method", "test-service", TEST_SERVER_SALT);
        var shortcut = new SessionShortcutData
        {
            RouteGuid = shortcutGuid,
            TargetGuid = Guid.NewGuid(),
            BoundPayload = new byte[] { 0x01 },
            SourceService = "test-service",
            TargetService = "test-service", // Has service
            // TargetMethod intentionally NOT set
            Name = "no_method",
            CreatedAt = DateTimeOffset.UtcNow
        };
        connectionState.AddOrUpdateShortcut(shortcut);

        var message = new BinaryMessage(
            MessageFlags.Binary,
            1, 1,
            shortcutGuid,
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.ShortcutTargetNotFound, routeInfo.ErrorCode);
        Assert.Contains("target_method", routeInfo.ErrorMessage);
    }

    /// <summary>
    /// Verifies that a shortcut missing TargetEndpoint returns ShortcutTargetNotFound error.
    /// </summary>
    [Fact]
    public void AnalyzeMessage_ShortcutMissingTargetEndpoint_ReturnsError()
    {
        // Arrange
        var sessionId = "test-session";
        var connectionState = new ConnectionState(sessionId);

        var shortcutGuid = GuidGenerator.GenerateSessionShortcutGuid(
            sessionId, "no_endpoint", "test-service", TEST_SERVER_SALT);
        var shortcut = new SessionShortcutData
        {
            RouteGuid = shortcutGuid,
            TargetGuid = Guid.NewGuid(),
            BoundPayload = new byte[] { 0x01 },
            SourceService = "test-service",
            TargetService = "test-service", // Has service
            TargetMethod = "POST",          // Has method
            // TargetEndpoint intentionally NOT set
            Name = "no_endpoint",
            CreatedAt = DateTimeOffset.UtcNow
        };
        connectionState.AddOrUpdateShortcut(shortcut);

        var message = new BinaryMessage(
            MessageFlags.Binary,
            1, 1,
            shortcutGuid,
            GuidGenerator.GenerateMessageId(),
            Array.Empty<byte>()
        );

        // Act
        var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

        // Assert
        Assert.False(routeInfo.IsValid);
        Assert.Equal(ResponseCodes.ShortcutTargetNotFound, routeInfo.ErrorCode);
        Assert.Contains("target_endpoint", routeInfo.ErrorMessage);
    }

    #endregion

    #region CheckRateLimit Tests

    /// <summary>
    /// Verifies that rate limiting allows messages within the quota.
    /// </summary>
    [Fact]
    public void CheckRateLimit_WithinQuota_IsAllowed()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");

        // Act — single message well within limit
        var result = MessageRouter.CheckRateLimit(connectionState, maxMessagesPerMinute: 100);

        // Assert
        Assert.True(result.IsAllowed);
        Assert.Equal(99, result.RemainingQuota); // 100 - 1
    }

    /// <summary>
    /// Verifies that exceeding the rate limit returns IsAllowed=false with zero remaining.
    /// </summary>
    [Fact]
    public void CheckRateLimit_ExceedsQuota_IsNotAllowed()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");

        // Pre-fill rate limit window close to the limit
        for (int i = 0; i < 10; i++)
        {
            connectionState.RecordMessageForRateLimit();
        }

        // Act — 11th message via CheckRateLimit (which also records) pushes over the limit of 10
        var result = MessageRouter.CheckRateLimit(connectionState, maxMessagesPerMinute: 10);

        // Assert
        Assert.False(result.IsAllowed);
        Assert.Equal(0, result.RemainingQuota);
    }

    /// <summary>
    /// Verifies that the rate limit boundary (exactly at quota) is allowed.
    /// </summary>
    [Fact]
    public void CheckRateLimit_ExactlyAtQuota_IsAllowed()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");

        // Pre-fill with 4 messages, then CheckRateLimit adds the 5th
        for (int i = 0; i < 4; i++)
        {
            connectionState.RecordMessageForRateLimit();
        }

        // Act — 5th message via CheckRateLimit, limit is 5
        var result = MessageRouter.CheckRateLimit(connectionState, maxMessagesPerMinute: 5);

        // Assert — 5 messages == 5 max, so recentMessageCount (5) is NOT > 5
        Assert.True(result.IsAllowed);
        Assert.Equal(0, result.RemainingQuota); // 5 - 5 = 0
    }

    /// <summary>
    /// Verifies that ResetTime is set to now + window duration.
    /// </summary>
    [Fact]
    public void CheckRateLimit_ResetTime_IsSetCorrectly()
    {
        // Arrange
        var connectionState = new ConnectionState("test-session");
        var before = DateTimeOffset.UtcNow;

        // Act
        var result = MessageRouter.CheckRateLimit(connectionState, rateLimitWindowMinutes: 2);

        var after = DateTimeOffset.UtcNow;

        // Assert — ResetTime should be approximately 2 minutes from now
        Assert.True(result.ResetTime >= before.AddMinutes(2));
        Assert.True(result.ResetTime <= after.AddMinutes(2));
    }

    #endregion
}
