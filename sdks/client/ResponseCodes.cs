namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// Response codes used in the binary WebSocket protocol for success/error indication.
/// These codes are returned in the response header's ResponseCode field.
/// </summary>
/// <remarks>
/// Codes 0-49 are protocol-level errors (Connect service).
/// Codes 50-69 are service-level errors (downstream service responses).
/// Codes 70+ are shortcut-specific errors.
/// </remarks>
public enum ResponseCodes : byte
{
    /// <summary>Request completed successfully.</summary>
    OK = 0,

    /// <summary>Generic request error (malformed message, invalid format).</summary>
    RequestError = 10,

    /// <summary>Request payload exceeds maximum allowed size.</summary>
    RequestTooLarge = 11,

    /// <summary>Rate limit exceeded for this client/session.</summary>
    TooManyRequests = 12,

    /// <summary>Invalid channel number in request header.</summary>
    InvalidRequestChannel = 13,

    /// <summary>Text WebSocket frame received; binary protocol required after AUTH.</summary>
    TextProtocolNotSupported = 14,

    /// <summary>Authentication required but not provided or invalid.</summary>
    Unauthorized = 20,

    /// <summary>Target service GUID not found in capability manifest.</summary>
    ServiceNotFound = 30,

    /// <summary>Target client GUID not found (for client-to-client messages).</summary>
    ClientNotFound = 31,

    /// <summary>Referenced message ID not found.</summary>
    MessageNotFound = 32,

    /// <summary>Broadcast not allowed in this connection mode (External mode blocks broadcast).</summary>
    BroadcastNotAllowed = 40,

    /// <summary>Service returned 400 Bad Request.</summary>
    Service_BadRequest = 50,

    /// <summary>Service returned 404 Not Found.</summary>
    Service_NotFound = 51,

    /// <summary>Service returned 401/403 Unauthorized/Forbidden.</summary>
    Service_Unauthorized = 52,

    /// <summary>Service returned 409 Conflict.</summary>
    Service_Conflict = 53,

    /// <summary>Service returned 500 Internal Server Error.</summary>
    Service_InternalServerError = 60,

    /// <summary>Shortcut has expired and is no longer valid.</summary>
    ShortcutExpired = 70,

    /// <summary>Shortcut target endpoint no longer exists.</summary>
    ShortcutTargetNotFound = 71,

    /// <summary>Shortcut was explicitly revoked.</summary>
    ShortcutRevoked = 72
}
