using System.Text;
using BeyondImmersion.Bannou.Client;
using BeyondImmersion.BannouService.Connect.Protocol;

namespace BeyondImmersion.EdgeTester.Infrastructure;

/// <summary>
/// Shared helper for parsing binary WebSocket messages with automatic decompression.
/// Wraps BinaryMessage.Parse and transparently handles the Compressed flag (0x04)
/// so that test handlers receive decompressed payloads regardless of server compression settings.
/// </summary>
public static class BinaryMessageHelper
{
    /// <summary>
    /// Parses a binary message from a buffer and automatically decompresses the payload
    /// if the Compressed flag is set.
    /// </summary>
    /// <param name="buffer">Buffer containing the raw message bytes.</param>
    /// <param name="count">Number of valid bytes in the buffer.</param>
    /// <returns>Parsed message with decompressed payload.</returns>
    public static BinaryMessage ParseAndDecompress(byte[] buffer, int count)
    {
        var message = BinaryMessage.Parse(buffer, count);

        if (message.Flags.HasFlag(MessageFlags.Compressed))
        {
            message = PayloadDecompressor.Decompress(message);
        }

        return message;
    }

    /// <summary>
    /// Parses a binary message and extracts the JSON payload as a string.
    /// Handles decompression transparently.
    /// </summary>
    /// <param name="buffer">Buffer containing the raw message bytes.</param>
    /// <param name="count">Number of valid bytes in the buffer.</param>
    /// <returns>JSON payload string, or null if parsing fails.</returns>
    public static string? TryParseJsonPayload(byte[] buffer, int count)
    {
        try
        {
            var message = ParseAndDecompress(buffer, count);
            return Encoding.UTF8.GetString(message.Payload.Span);
        }
        catch
        {
            return null;
        }
    }
}
