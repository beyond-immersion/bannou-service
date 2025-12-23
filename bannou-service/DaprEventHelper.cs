using BeyondImmersion.BannouService.Configuration;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService;

/// <summary>
/// Helper class for parsing Dapr pub/sub events with CloudEvents format handling.
/// Handles both CloudEvents-wrapped events and raw payloads since Dapr's RabbitMQ
/// component can deliver events in either format depending on timing and configuration.
/// </summary>
public static class DaprEventHelper
{
    /// <summary>
    /// Reads and parses a Dapr pub/sub event from the HTTP request body, returning the unwrapped JSON element.
    /// Automatically handles both CloudEvents format (with 'data' wrapper) and raw format.
    /// Use this method when you need to manually extract properties from the event.
    /// </summary>
    /// <param name="request">The HTTP request containing the event</param>
    /// <returns>The unwrapped event JSON element, or null if parsing fails</returns>
    public static async Task<JsonElement?> ReadEventJsonAsync(HttpRequest request)
    {
        // Read the raw request body
        string rawBody;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        // Parse as JSON
        JsonElement eventJson;
        try
        {
            eventJson = BannouJson.Deserialize<JsonElement>(rawBody);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        // Extract data from CloudEvents wrapper if present, otherwise use raw body
        JsonElement actualEventData = eventJson;
        if (eventJson.ValueKind == JsonValueKind.Object &&
            eventJson.TryGetProperty("data", out var dataElement))
        {
            actualEventData = dataElement;
        }

        return actualEventData;
    }

    /// <summary>
    /// Reads and parses a Dapr pub/sub event from the HTTP request body.
    /// Automatically handles both CloudEvents format (with 'data' wrapper) and raw format.
    /// </summary>
    /// <typeparam name="T">The event model type to deserialize</typeparam>
    /// <param name="request">The HTTP request containing the event</param>
    /// <returns>The deserialized event model, or null if parsing fails</returns>
    public static async Task<T?> ReadEventAsync<T>(HttpRequest request) where T : class
    {
        // Read the raw request body
        string rawBody;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return null;
        }

        // Parse as JSON
        JsonElement eventJson;
        try
        {
            eventJson = BannouJson.Deserialize<JsonElement>(rawBody);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }

        // Extract data from CloudEvents wrapper if present, otherwise use raw body
        JsonElement actualEventData = eventJson;
        if (eventJson.ValueKind == JsonValueKind.Object &&
            eventJson.TryGetProperty("data", out var dataElement))
        {
            actualEventData = dataElement;
        }

        // Deserialize to the target type using centralized BannouJson
        return BannouJson.Deserialize<T>(actualEventData.GetRawText());
    }

    /// <summary>
    /// Unwraps a CloudEvents envelope from raw byte payload.
    /// Automatically handles both CloudEvents format (with 'data' wrapper) and raw format.
    /// Used by Connect service for RabbitMQ subscriber which receives raw bytes.
    /// </summary>
    /// <param name="payload">Raw event payload bytes (potentially CloudEvents-wrapped)</param>
    /// <returns>Unwrapped event data bytes, or original payload if not CloudEvents format</returns>
    public static byte[] UnwrapCloudEventsEnvelope(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return payload ?? Array.Empty<byte>();
        }

        try
        {
            // Try to parse as JSON to check for CloudEvents structure
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // CloudEvents format has "specversion" and "data" properties
            // See: https://github.com/cloudevents/spec/blob/main/cloudevents/spec.md
            if (root.TryGetProperty("specversion", out _) &&
                root.TryGetProperty("data", out var dataElement))
            {
                // This is a CloudEvents envelope - extract the data payload
                var dataJson = dataElement.GetRawText();
                return Encoding.UTF8.GetBytes(dataJson);
            }

            // Not CloudEvents format - return original payload
            return payload;
        }
        catch (JsonException)
        {
            // Not valid JSON - return original payload (might be binary data)
            return payload;
        }
    }

    /// <summary>
    /// Unwraps a CloudEvents envelope and returns the data as a JsonElement.
    /// Useful when you need to inspect or modify the unwrapped data before forwarding.
    /// </summary>
    /// <param name="payload">Raw event payload bytes (potentially CloudEvents-wrapped)</param>
    /// <returns>Tuple of (unwrapped JsonElement, raw bytes), or (null, original bytes) if parsing fails</returns>
    public static (JsonElement? Element, byte[] Bytes) UnwrapCloudEventsToJson(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return (null, payload ?? Array.Empty<byte>());
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // CloudEvents format has "specversion" and "data" properties
            if (root.TryGetProperty("specversion", out _) &&
                root.TryGetProperty("data", out var dataElement))
            {
                // This is a CloudEvents envelope - extract the data payload
                var dataJson = dataElement.GetRawText();
                var dataBytes = Encoding.UTF8.GetBytes(dataJson);

                // Re-parse the extracted data
                using var dataDoc = JsonDocument.Parse(dataBytes);
                return (dataDoc.RootElement.Clone(), dataBytes);
            }

            // Not CloudEvents format - return parsed root
            return (root.Clone(), payload);
        }
        catch (JsonException)
        {
            // Not valid JSON
            return (null, payload);
        }
    }
}
