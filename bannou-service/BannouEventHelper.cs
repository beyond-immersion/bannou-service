using System.Text;
using System.Text.Json;

namespace BeyondImmersion.BannouService;

/// <summary>
/// Helper class for parsing pub/sub events with CloudEvents format handling.
/// Handles both CloudEvents-wrapped events and raw payloads since RabbitMQ
/// can deliver events in either format depending on timing and configuration.
/// </summary>
/// <remarks>
/// NOTE: HTTP-based ReadEventAsync methods were removed - events now flow via
/// IEventConsumer/MassTransit, not HTTP controllers with [Topic] attributes.
/// </remarks>
public static class BannouEventHelper
{
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
