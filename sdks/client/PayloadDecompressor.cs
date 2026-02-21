using System.IO.Compression;
using BeyondImmersion.BannouService.Connect.Protocol;

namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// Brotli decompression for inbound WebSocket payloads.
/// Used when the server sends messages with the Compressed flag (0x04) set,
/// indicating the payload has been Brotli-compressed above a size threshold.
/// </summary>
public static class PayloadDecompressor
{
    /// <summary>
    /// Decompresses the payload of a message that has the Compressed flag set.
    /// Returns a new BinaryMessage with the decompressed payload and the Compressed flag cleared.
    /// </summary>
    /// <param name="message">The compressed message to decompress.</param>
    /// <returns>A new BinaryMessage with decompressed payload and Compressed flag removed.</returns>
    public static BinaryMessage Decompress(BinaryMessage message)
    {
        if (message.Payload.IsEmpty)
        {
            return message;
        }

        var decompressedBytes = DecompressBytes(message.Payload.Span);
        var flags = message.Flags & ~MessageFlags.Compressed;

        if (message.IsResponse)
        {
            return new BinaryMessage(
                flags,
                message.Channel,
                message.SequenceNumber,
                message.MessageId,
                message.ResponseCode,
                decompressedBytes);
        }

        return new BinaryMessage(
            flags,
            message.Channel,
            message.SequenceNumber,
            message.ServiceGuid,
            message.MessageId,
            decompressedBytes);
    }

    /// <summary>
    /// Decompresses Brotli-compressed bytes using BrotliStream.
    /// </summary>
    private static byte[] DecompressBytes(ReadOnlySpan<byte> compressed)
    {
        using var input = new MemoryStream(compressed.ToArray());
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return output.ToArray();
    }
}
