using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.Client.SDK;

/// <summary>
/// Meta endpoint types for requesting endpoint metadata instead of executing.
/// When the Meta flag (0x80) is set, this value is encoded in the Channel field.
/// </summary>
public enum MetaType : ushort
{
    /// <summary>Human-readable endpoint description (summary, tags, deprecated)</summary>
    EndpointInfo = 0,

    /// <summary>JSON Schema for the request body</summary>
    RequestSchema = 1,

    /// <summary>JSON Schema for the response body</summary>
    ResponseSchema = 2,

    /// <summary>Complete endpoint schema (info + request + response combined)</summary>
    FullSchema = 3
}

/// <summary>
/// Response wrapper for meta endpoint requests.
/// Contains endpoint metadata, schema information, or full schema data.
/// </summary>
/// <typeparam name="T">The type of data contained in this meta response.</typeparam>
public class MetaResponse<T>
{
    /// <summary>
    /// Type of metadata returned.
    /// Values: "endpoint-info", "request-schema", "response-schema", "full-schema"
    /// </summary>
    [JsonPropertyName("metaType")]
    public string MetaType { get; set; } = "";

    /// <summary>
    /// Endpoint key in format "METHOD:/path" (e.g., "POST:/accounts/get")
    /// </summary>
    [JsonPropertyName("endpointKey")]
    public string EndpointKey { get; set; } = "";

    /// <summary>
    /// Service name that owns this endpoint (e.g., "Accounts")
    /// </summary>
    [JsonPropertyName("serviceName")]
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// HTTP method (POST, GET, PUT, DELETE, etc.)
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    /// <summary>
    /// Endpoint path (e.g., "/accounts/get")
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// Metadata payload. Structure varies by MetaType.
    /// </summary>
    [JsonPropertyName("data")]
    public T? Data { get; set; }

    /// <summary>
    /// When this response was generated (UTC)
    /// </summary>
    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>
    /// Schema version (assembly version) for cache invalidation
    /// </summary>
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "";
}

/// <summary>
/// Endpoint info metadata containing human-readable endpoint description.
/// Returned when MetaType is EndpointInfo.
/// </summary>
public class EndpointInfoData
{
    /// <summary>
    /// Brief summary of what the endpoint does.
    /// </summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "";

    /// <summary>
    /// Detailed description of endpoint behavior.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Categorization tags for the endpoint.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Whether this endpoint is deprecated.
    /// </summary>
    [JsonPropertyName("deprecated")]
    public bool Deprecated { get; set; }

    /// <summary>
    /// Operation ID from OpenAPI specification.
    /// </summary>
    [JsonPropertyName("operationId")]
    public string OperationId { get; set; } = "";
}

/// <summary>
/// JSON Schema data for request or response bodies.
/// Returned when MetaType is RequestSchema or ResponseSchema.
/// This is a flexible container that holds any valid JSON Schema.
/// </summary>
public class JsonSchemaData
{
    /// <summary>
    /// JSON Schema draft specification (e.g., "http://json-schema.org/draft-07/schema#")
    /// </summary>
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "";

    /// <summary>
    /// Root type of the schema (e.g., "object", "array", "string")
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>
    /// List of required property names (for object types).
    /// </summary>
    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }

    /// <summary>
    /// Property definitions (for object types).
    /// Values are JSON Schema property definitions.
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, JsonElement>? Properties { get; set; }

    /// <summary>
    /// Shared definitions referenced via $ref.
    /// </summary>
    [JsonPropertyName("$defs")]
    public Dictionary<string, JsonElement>? Defs { get; set; }

    /// <summary>
    /// Additional properties beyond standard JSON Schema fields.
    /// Captured via JsonExtensionData to preserve all schema content.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Full schema data combining info, request schema, and response schema.
/// Returned when MetaType is FullSchema.
/// </summary>
public class FullSchemaData
{
    /// <summary>
    /// Endpoint info (summary, description, tags, deprecated).
    /// </summary>
    [JsonPropertyName("info")]
    public EndpointInfoData Info { get; set; } = new();

    /// <summary>
    /// JSON Schema for the request body.
    /// </summary>
    [JsonPropertyName("request")]
    public JsonSchemaData Request { get; set; } = new();

    /// <summary>
    /// JSON Schema for the response body.
    /// </summary>
    [JsonPropertyName("response")]
    public JsonSchemaData Response { get; set; } = new();
}
