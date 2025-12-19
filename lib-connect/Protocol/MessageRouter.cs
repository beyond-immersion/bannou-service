using System;
using System.Collections.Generic;
using System.Linq;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Dependency-free message routing logic for binary protocol messages.
/// Can be extracted to Client SDK for consistent routing behavior.
/// </summary>
public static class MessageRouter
{
    /// <summary>
    /// Analyzes a message and determines routing information.
    /// </summary>
    public static MessageRouteInfo AnalyzeMessage(BinaryMessage message, ConnectionState connectionState)
    {
        var routeInfo = new MessageRouteInfo
        {
            Message = message,
            IsValid = true
        };

        // Validate message structure
        if (message.Payload.IsEmpty && !message.Flags.HasFlag(MessageFlags.Event))
        {
            routeInfo.IsValid = false;
            routeInfo.ErrorCode = ResponseCodes.RequestError;
            routeInfo.ErrorMessage = "Message payload is required for non-event messages";
            return routeInfo;
        }

        // Determine routing target
        if (message.IsClientRouted)
        {
            routeInfo.RouteType = RouteType.Client;
            routeInfo.TargetType = "client";
            routeInfo.TargetId = message.ServiceGuid.ToString();
        }
        else
        {
            routeInfo.RouteType = RouteType.Service;

            // Look up service name from GUID (thread-safe)
            if (connectionState.TryGetServiceName(message.ServiceGuid, out var serviceName) && serviceName != null)
            {
                routeInfo.TargetType = "service";
                routeInfo.TargetId = serviceName;
                routeInfo.ServiceName = serviceName;
            }
            else
            {
                routeInfo.IsValid = false;
                routeInfo.ErrorCode = ResponseCodes.ServiceNotFound;
                routeInfo.ErrorMessage = $"Service GUID {message.ServiceGuid} not found in session mappings";
                return routeInfo;
            }
        }

        // Determine processing priority
        routeInfo.Priority = message.IsHighPriority ? MessagePriority.High : MessagePriority.Normal;

        // Validate channel
        if (message.Channel > 1000) // Arbitrary reasonable limit
        {
            routeInfo.IsValid = false;
            routeInfo.ErrorCode = ResponseCodes.InvalidRequestChannel;
            routeInfo.ErrorMessage = $"Invalid channel {message.Channel}";
            return routeInfo;
        }

        routeInfo.Channel = message.Channel;
        routeInfo.RequiresResponse = message.ExpectsResponse;

        return routeInfo;
    }

    /// <summary>
    /// Creates an error response message for routing failures.
    /// Error responses have the response code in the binary header (byte 15) and an empty payload.
    /// </summary>
    public static BinaryMessage CreateErrorResponse(
        BinaryMessage originalMessage,
        ResponseCodes errorCode,
        string? errorMessage = null)
    {
        // Error responses have empty payloads - the response code in the header tells the story
        // The errorMessage parameter is kept for logging/debugging but not sent over the wire
        _ = errorMessage; // Suppress unused parameter warning

        return BinaryMessage.CreateResponse(originalMessage, errorCode);
    }

    /// <summary>
    /// Validates message rate limiting.
    /// </summary>
    public static RateLimitResult CheckRateLimit(
        ConnectionState connectionState,
        int maxMessagesPerMinute = 1000)
    {
        // This is a simplified rate limiting - in production you'd want more sophisticated logic
        var now = DateTimeOffset.UtcNow;
        var oneMinuteAgo = now.AddMinutes(-1);

        // Count recent messages (this would be more efficient with a sliding window)
        var recentMessageCount = connectionState.PendingMessages.Values
            .Count(p => p.SentAt > oneMinuteAgo);

        if (recentMessageCount >= maxMessagesPerMinute)
        {
            return new RateLimitResult
            {
                IsAllowed = false,
                RemainingQuota = 0,
                ResetTime = now.AddMinutes(1)
            };
        }

        return new RateLimitResult
        {
            IsAllowed = true,
            RemainingQuota = maxMessagesPerMinute - recentMessageCount,
            ResetTime = now.AddMinutes(1)
        };
    }

    /// <summary>
    /// Generates a unique message ID.
    /// </summary>
    public static ulong GenerateMessageId()
    {
        return GuidGenerator.GenerateMessageId();
    }

}

/// <summary>
/// Information about how a message should be routed.
/// </summary>
public class MessageRouteInfo
{
    public BinaryMessage Message { get; set; }
    public bool IsValid { get; set; }
    public ResponseCodes ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    public RouteType RouteType { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? ServiceName { get; set; }

    public ushort Channel { get; set; }
    public MessagePriority Priority { get; set; }
    public bool RequiresResponse { get; set; }
}

/// <summary>
/// Message routing destination types.
/// </summary>
public enum RouteType
{
    Service,
    Client
}

/// <summary>
/// Message processing priority levels.
/// </summary>
public enum MessagePriority
{
    Normal,
    High
}

/// <summary>
/// Rate limiting check result.
/// </summary>
public class RateLimitResult
{
    public bool IsAllowed { get; set; }
    public int RemainingQuota { get; set; }
    public DateTimeOffset ResetTime { get; set; }
}
