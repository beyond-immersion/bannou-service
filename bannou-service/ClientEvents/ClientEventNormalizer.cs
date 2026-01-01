using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.BannouService.ClientEvents;

/// <summary>
/// Normalizes client event names from their potentially mangled form (from JSON serialization)
/// back to canonical whitelist format.
///
/// NSwag/JsonStringEnumConverter converts event names like "system.notification" to
/// "system_notification" or "System_notification". This class maps these mangled names
/// back to their canonical form for:
/// 1. Validation against the whitelist
/// 2. Rewriting the event_name in the payload before sending to clients
/// </summary>
public static class ClientEventNormalizer
{
    // Lookup from mangled name (lowercase) to canonical name
    private static readonly Dictionary<string, string> MangledToCanonical;

    static ClientEventNormalizer()
    {
        MangledToCanonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Build lookup from all valid event names
        foreach (var canonicalName in ClientEventWhitelist.GetAllValidEventNames())
        {
            // The canonical name (e.g., "system.notification") maps to itself
            MangledToCanonical[canonicalName] = canonicalName;

            // The mangled version (e.g., "system_notification") also maps to canonical
            var mangledName = canonicalName.Replace('.', '_');
            if (!MangledToCanonical.ContainsKey(mangledName))
            {
                MangledToCanonical[mangledName] = canonicalName;
            }
        }
    }

    /// <summary>
    /// Attempts to normalize an event name from its potentially mangled form to canonical form.
    /// </summary>
    /// <param name="eventName">The event name as received (possibly mangled, e.g., "system_notification")</param>
    /// <param name="canonicalName">The canonical name if valid (e.g., "system.notification")</param>
    /// <returns>True if the event is valid and was normalized, false if not in whitelist</returns>
    public static bool TryGetCanonicalName(string? eventName, out string? canonicalName)
    {
        canonicalName = null;

        if (string.IsNullOrWhiteSpace(eventName))
        {
            return false;
        }

        if (MangledToCanonical.TryGetValue(eventName, out canonicalName))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if an event name (possibly mangled) is valid according to the whitelist.
    /// </summary>
    /// <param name="eventName">The event name to check</param>
    /// <returns>True if valid (in whitelist), false otherwise</returns>
    public static bool IsValidEventName(string? eventName)
    {
        return TryGetCanonicalName(eventName, out _);
    }

    /// <summary>
    /// Normalizes the event_name field in a JSON payload to its canonical form.
    /// If the event is not in the whitelist, returns null.
    /// </summary>
    /// <param name="payload">The JSON event payload bytes</param>
    /// <returns>
    /// Tuple of (canonicalEventName, normalizedPayload) if valid,
    /// or (null, originalPayload) if event not in whitelist or parsing fails
    /// </returns>
    public static (string? CanonicalName, byte[] Payload) NormalizeEventPayload(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return (null, payload ?? Array.Empty<byte>());
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            byte[] effectivePayload = payload;

            // Unwrap MassTransit envelope - MassTransit wraps messages with metadata,
            // and the actual event data is in the "message" property
            if (root.TryGetProperty("message", out var messageElement))
            {
                root = messageElement;
                // Extract the unwrapped message as the payload to send to clients
                effectivePayload = System.Text.Encoding.UTF8.GetBytes(messageElement.GetRawText());
            }

            // Try to find the eventName property
            string? receivedEventName = null;
            string? propertyName = null;

            if (root.TryGetProperty("eventName", out var eventNameElement))
            {
                receivedEventName = eventNameElement.GetString();
                propertyName = "eventName";
            }

            if (string.IsNullOrEmpty(receivedEventName) || string.IsNullOrEmpty(propertyName))
            {
                // No event_name field - can't validate or normalize
                return (null, effectivePayload);
            }

            // Try to get canonical name
            if (!TryGetCanonicalName(receivedEventName, out var canonicalName) || canonicalName == null)
            {
                // Not in whitelist - reject
                return (null, effectivePayload);
            }

            // If the name is already canonical, return as-is
            if (receivedEventName == canonicalName)
            {
                return (canonicalName, effectivePayload);
            }

            // Need to rewrite the event_name to canonical form
            // Build a new JSON object with the corrected event_name
            var normalizedPayload = RewriteEventName(root, propertyName, canonicalName);
            return (canonicalName, normalizedPayload);
        }
        catch (JsonException)
        {
            // Not valid JSON
            return (null, payload);
        }
    }

    /// <summary>
    /// Rewrites the event_name property in a JSON object to a new value.
    /// </summary>
    private static byte[] RewriteEventName(JsonElement root, string propertyName, string newValue)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name == propertyName)
            {
                // Write the corrected event_name
                writer.WriteString(propertyName, newValue);
            }
            else
            {
                // Copy other properties as-is
                property.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
        writer.Flush();

        return stream.ToArray();
    }
}
