using Microsoft.AspNetCore.Http;
using System.IO;
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
            eventJson = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(rawBody);
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
    /// <param name="options">Optional JSON serializer options (defaults to PropertyNameCaseInsensitive)</param>
    /// <returns>The deserialized event model, or null if parsing fails</returns>
    public static async Task<T?> ReadEventAsync<T>(HttpRequest request, JsonSerializerOptions? options = null) where T : class
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
            eventJson = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(rawBody);
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

        // Deserialize to the target type
        var serializerOptions = options ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return System.Text.Json.JsonSerializer.Deserialize<T>(actualEventData.GetRawText(), serializerOptions);
    }
}
