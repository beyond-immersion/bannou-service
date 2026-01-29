using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Configuration;
using System.Reflection;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Meta;

/// <summary>
/// Builds MetaResponse objects from pre-computed schema strings.
/// Uses BannouJson for all JSON operations per IMPLEMENTATION TENETS.
/// </summary>
public static class MetaResponseBuilder
{
    private static readonly string SchemaVersion = Assembly.GetExecutingAssembly()
        .GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>
    /// Builds an endpoint-info meta response.
    /// </summary>
    /// <param name="serviceName">Service name (e.g., "Account")</param>
    /// <param name="method">HTTP method (e.g., "POST")</param>
    /// <param name="path">Endpoint path (e.g., "/account/get")</param>
    /// <param name="infoJson">Pre-embedded endpoint info JSON string</param>
    /// <returns>MetaResponse with endpoint-info type</returns>
    public static MetaResponse BuildInfoResponse(
        string serviceName,
        string method,
        string path,
        string infoJson)
    {
        return new MetaResponse
        {
            MetaType = "endpoint-info",
            ServiceName = serviceName,
            Method = method,
            Path = path,
            Data = ParseJsonElement(infoJson),
            GeneratedAt = DateTimeOffset.UtcNow,
            SchemaVersion = SchemaVersion
        };
    }

    /// <summary>
    /// Builds a schema meta response (request-schema or response-schema).
    /// </summary>
    /// <param name="serviceName">Service name (e.g., "Account")</param>
    /// <param name="method">HTTP method (e.g., "POST")</param>
    /// <param name="path">Endpoint path (e.g., "/account/get")</param>
    /// <param name="metaType">Schema type: "request-schema" or "response-schema"</param>
    /// <param name="schemaJson">Pre-embedded JSON Schema string</param>
    /// <returns>MetaResponse with schema data</returns>
    public static MetaResponse BuildSchemaResponse(
        string serviceName,
        string method,
        string path,
        string metaType,
        string schemaJson)
    {
        return new MetaResponse
        {
            MetaType = metaType,
            ServiceName = serviceName,
            Method = method,
            Path = path,
            Data = ParseJsonElement(schemaJson),
            GeneratedAt = DateTimeOffset.UtcNow,
            SchemaVersion = SchemaVersion
        };
    }

    /// <summary>
    /// Builds a full-schema meta response combining info, request, and response schemas.
    /// </summary>
    /// <param name="serviceName">Service name (e.g., "Account")</param>
    /// <param name="method">HTTP method (e.g., "POST")</param>
    /// <param name="path">Endpoint path (e.g., "/account/get")</param>
    /// <param name="infoJson">Pre-embedded endpoint info JSON string</param>
    /// <param name="requestSchemaJson">Pre-embedded request JSON Schema string</param>
    /// <param name="responseSchemaJson">Pre-embedded response JSON Schema string</param>
    /// <returns>MetaResponse with combined schema data</returns>
    public static MetaResponse BuildFullSchemaResponse(
        string serviceName,
        string method,
        string path,
        string infoJson,
        string requestSchemaJson,
        string responseSchemaJson)
    {
        // Build combined structure using BannouJson
        var fullSchema = new
        {
            info = ParseJsonElement(infoJson),
            request = ParseJsonElement(requestSchemaJson),
            response = ParseJsonElement(responseSchemaJson)
        };

        var fullSchemaJson = BannouJson.Serialize(fullSchema);

        return new MetaResponse
        {
            MetaType = "full-schema",
            ServiceName = serviceName,
            Method = method,
            Path = path,
            Data = ParseJsonElement(fullSchemaJson),
            GeneratedAt = DateTimeOffset.UtcNow,
            SchemaVersion = SchemaVersion
        };
    }

    /// <summary>
    /// Parses a JSON string to JsonElement using BannouJson options.
    /// </summary>
    /// <param name="json">JSON string to parse</param>
    /// <returns>Parsed JsonElement</returns>
    private static JsonElement ParseJsonElement(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        try
        {
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            // If parsing fails, return empty object
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}
