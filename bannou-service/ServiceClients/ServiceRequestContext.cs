namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Provides ambient context for service requests using AsyncLocal storage.
/// Captures session ID and correlation ID from incoming requests for:
/// 1. Forwarding to downstream service calls (auto-propagation)
/// 2. Pushing client events back to the originating WebSocket session
/// </summary>
/// <remarks>
/// The session ID is extracted from the X-Bannou-Session-Id header, which Connect
/// attaches when proxying WebSocket requests to backend services.
///
/// WARNING: Session ID is for correlation/tracing and client event delivery only.
/// DO NOT use for authentication, authorization, or ownership decisions.
/// See ConnectService.cs comments for security guidance.
/// </remarks>
public static class ServiceRequestContext
{
    private static readonly AsyncLocal<string?> _sessionId = new();
    private static readonly AsyncLocal<string?> _correlationId = new();

    /// <summary>
    /// The session ID of the WebSocket client that initiated this request chain.
    /// Null if the request did not originate from a WebSocket client (e.g., internal/scheduled).
    /// </summary>
    public static string? SessionId
    {
        get => _sessionId.Value;
        set => _sessionId.Value = value;
    }

    /// <summary>
    /// Optional correlation ID for distributed tracing.
    /// </summary>
    public static string? CorrelationId
    {
        get => _correlationId.Value;
        set => _correlationId.Value = value;
    }

    /// <summary>
    /// Returns true if this request originated from a WebSocket client.
    /// </summary>
    public static bool HasClientContext => !string.IsNullOrEmpty(_sessionId.Value);

    /// <summary>
    /// Clears all context values. Called by middleware after request completion.
    /// </summary>
    internal static void Clear()
    {
        _sessionId.Value = null;
        _correlationId.Value = null;
    }
}
