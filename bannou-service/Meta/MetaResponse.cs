using System.Text.Json;

namespace BeyondImmersion.BannouService.Meta;

/// <summary>
/// Response wrapper for meta endpoint requests.
/// Contains endpoint metadata, schema information, or full schema data.
/// </summary>
public class MetaResponse
{
    /// <summary>
    /// Type of metadata returned.
    /// Values: "endpoint-info", "request-schema", "response-schema", "full-schema"
    /// </summary>
    public string MetaType { get; set; } = "";

    /// <summary>
    /// Endpoint key in format "METHOD:/path" (e.g., "POST:/accounts/get")
    /// </summary>
    public string EndpointKey { get; set; } = "";

    /// <summary>
    /// Service name that owns this endpoint (e.g., "Accounts")
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// HTTP method (POST, GET, PUT, DELETE, etc.)
    /// </summary>
    public string Method { get; set; } = "";

    /// <summary>
    /// Endpoint path (e.g., "/account/get")
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Metadata payload. Structure varies by MetaType:
    /// - endpoint-info: { summary, description, tags, deprecated, operationId }
    /// - request-schema: JSON Schema object
    /// - response-schema: JSON Schema object
    /// - full-schema: { info, request, response }
    /// </summary>
    public JsonElement Data { get; set; }

    /// <summary>
    /// When this response was generated (UTC)
    /// </summary>
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>
    /// Schema version (assembly version) for cache invalidation
    /// </summary>
    public string SchemaVersion { get; set; } = "";
}
