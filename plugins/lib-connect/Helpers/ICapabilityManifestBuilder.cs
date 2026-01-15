using BeyondImmersion.BannouService.Connect.Protocol;

namespace BeyondImmersion.BannouService.Connect.Helpers;

/// <summary>
/// Builds capability manifests for WebSocket clients.
/// Extracts and filters endpoint information from service mappings for client consumption.
/// </summary>
public interface ICapabilityManifestBuilder
{
    /// <summary>
    /// Builds a list of available API endpoints from service mappings.
    /// Filters for POST-only endpoints and skips template paths (required for zero-copy routing).
    /// </summary>
    /// <param name="serviceMappings">Dictionary mapping endpoint keys to client-salted GUIDs.</param>
    /// <param name="serviceFilter">Optional service name prefix to filter results.</param>
    /// <returns>List of parsed and filtered API entries.</returns>
    IReadOnlyList<ManifestApiEntry> BuildApiList(
        IReadOnlyDictionary<string, Guid> serviceMappings,
        string? serviceFilter = null);

    /// <summary>
    /// Builds a list of shortcuts from connection state shortcuts.
    /// Removes expired shortcuts from the provided collection.
    /// </summary>
    /// <param name="shortcuts">Collection of shortcuts to process.</param>
    /// <param name="onExpiredShortcut">Callback to invoke when an expired shortcut is found (for cleanup).</param>
    /// <returns>List of active shortcut entries.</returns>
    IReadOnlyList<ManifestShortcutEntry> BuildShortcutList(
        IEnumerable<SessionShortcutData> shortcuts,
        Action<Guid>? onExpiredShortcut = null);

    /// <summary>
    /// Parses an endpoint key into its component parts.
    /// Format: "serviceName:METHOD:/path"
    /// </summary>
    /// <param name="endpointKey">The endpoint key to parse.</param>
    /// <returns>Parsed endpoint info, or null if the format is invalid.</returns>
    ParsedEndpoint? ParseEndpointKey(string endpointKey);
}

/// <summary>
/// Represents a parsed API endpoint entry in the capability manifest.
/// </summary>
public sealed class ManifestApiEntry
{
    /// <summary>
    /// Client-salted GUID for routing this endpoint.
    /// </summary>
    public required Guid ServiceGuid { get; init; }

    /// <summary>
    /// HTTP method (typically "POST" for WebSocket-accessible endpoints).
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// API path without template parameters.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Combined endpoint key for client use (format: "METHOD:/path").
    /// </summary>
    public required string EndpointKey { get; init; }

    /// <summary>
    /// Service name (informational only, not used for routing).
    /// </summary>
    public required string ServiceName { get; init; }
}

/// <summary>
/// Represents a shortcut entry in the capability manifest.
/// </summary>
public sealed class ManifestShortcutEntry
{
    /// <summary>
    /// Route GUID for this shortcut.
    /// </summary>
    public required Guid RouteGuid { get; init; }

    /// <summary>
    /// Human-readable name for the shortcut.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Target service this shortcut routes to.
    /// </summary>
    public required string TargetService { get; init; }

    /// <summary>
    /// Target endpoint path.
    /// </summary>
    public required string TargetEndpoint { get; init; }

    /// <summary>
    /// Optional description for the shortcut.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Result of parsing an endpoint key string.
/// </summary>
public sealed class ParsedEndpoint
{
    /// <summary>
    /// Service name extracted from the endpoint key.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// HTTP method (GET, POST, etc.).
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// API path.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Whether this endpoint has template parameters (e.g., {id}).
    /// </summary>
    public bool HasTemplateParams => Path.Contains('{');

    /// <summary>
    /// Whether this is a POST endpoint (required for WebSocket binary protocol).
    /// </summary>
    public bool IsPost => Method == "POST";
}
