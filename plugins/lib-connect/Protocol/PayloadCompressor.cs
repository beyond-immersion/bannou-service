using System.Buffers;
using System.IO.Compression;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Brotli compression for outbound WebSocket payloads.
/// Uses BrotliEncoder.TryCompress with ArrayPool to minimize allocations.
/// </summary>
public static class PayloadCompressor
{
    /// <summary>
    /// Brotli window size for compression. Window 20 is sufficient for payloads
    /// up to ~1MB and uses less memory than the default 22.
    /// </summary>
    private const int BrotliWindow = 20;

    /// <summary>
    /// Attempts to compress the payload using Brotli.
    /// Returns true if compression reduced the payload size.
    /// Returns false if the payload is below the threshold or compression
    /// did not reduce size (caller should send uncompressed).
    /// </summary>
    /// <param name="source">The payload bytes to compress.</param>
    /// <param name="thresholdBytes">Minimum payload size before compression is attempted.</param>
    /// <param name="quality">Brotli quality level (0-11). Quality 1 recommended for real-time traffic.</param>
    /// <param name="compressedData">On success, contains the compressed payload bytes.</param>
    /// <param name="compressedLength">On success, the number of valid bytes in compressedData.</param>
    /// <returns>True if compression was applied and reduced size; false otherwise.</returns>
    public static bool TryCompress(
        ReadOnlySpan<byte> source,
        int thresholdBytes,
        int quality,
        out byte[] compressedData,
        out int compressedLength)
    {
        compressedData = Array.Empty<byte>();
        compressedLength = 0;

        if (source.Length < thresholdBytes)
        {
            return false;
        }

        var maxCompressedLength = BrotliEncoder.GetMaxCompressedLength(source.Length);
        var rentedBuffer = ArrayPool<byte>.Shared.Rent(maxCompressedLength);

        try
        {
            if (!BrotliEncoder.TryCompress(source, rentedBuffer, out var bytesWritten, quality, BrotliWindow))
            {
                return false;
            }

            // Only use compression if it actually reduced the size
            if (bytesWritten >= source.Length)
            {
                return false;
            }

            // Copy to exact-size array to avoid holding the rented buffer
            compressedData = new byte[bytesWritten];
            rentedBuffer.AsSpan(0, bytesWritten).CopyTo(compressedData);
            compressedLength = bytesWritten;
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }
}
