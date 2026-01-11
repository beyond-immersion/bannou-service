using System.IO.Compression;

namespace BeyondImmersion.BannouService.SaveLoad.Compression;

/// <summary>
/// Helper class for compressing and decompressing save data.
/// Supports GZIP and Brotli compression algorithms.
/// </summary>
public static class CompressionHelper
{
    /// <summary>
    /// Compresses data using the specified compression type.
    /// </summary>
    /// <param name="data">The data to compress.</param>
    /// <param name="compressionType">The compression algorithm to use.</param>
    /// <returns>Compressed data, or original data if compression type is NONE.</returns>
    public static byte[] Compress(byte[] data, CompressionType compressionType)
    {
        if (data == null || data.Length == 0)
        {
            return data ?? Array.Empty<byte>();
        }

        return compressionType switch
        {
            CompressionType.NONE => data,
            CompressionType.GZIP => CompressGzip(data),
            CompressionType.BROTLI => CompressBrotli(data),
            _ => data
        };
    }

    /// <summary>
    /// Decompresses data using the specified compression type.
    /// </summary>
    /// <param name="compressedData">The compressed data.</param>
    /// <param name="compressionType">The compression algorithm that was used.</param>
    /// <returns>Decompressed data, or original data if compression type is NONE.</returns>
    public static byte[] Decompress(byte[] compressedData, CompressionType compressionType)
    {
        if (compressedData == null || compressedData.Length == 0)
        {
            return compressedData ?? Array.Empty<byte>();
        }

        return compressionType switch
        {
            CompressionType.NONE => compressedData,
            CompressionType.GZIP => DecompressGzip(compressedData),
            CompressionType.BROTLI => DecompressBrotli(compressedData),
            _ => compressedData
        };
    }

    /// <summary>
    /// Determines if compression is worthwhile based on size threshold and achievable ratio.
    /// </summary>
    /// <param name="originalSize">Original data size in bytes.</param>
    /// <param name="thresholdBytes">Minimum size to consider compression.</param>
    /// <returns>True if compression should be attempted.</returns>
    public static bool ShouldCompress(long originalSize, long thresholdBytes)
    {
        return originalSize >= thresholdBytes;
    }

    /// <summary>
    /// Calculates the compression ratio (compressedSize / originalSize).
    /// </summary>
    /// <param name="originalSize">Original data size.</param>
    /// <param name="compressedSize">Compressed data size.</param>
    /// <returns>Compression ratio between 0 and 1 (or greater if compression made data larger).</returns>
    public static double CalculateCompressionRatio(long originalSize, long compressedSize)
    {
        if (originalSize == 0)
        {
            return 1.0;
        }
        return (double)compressedSize / originalSize;
    }

    private static byte[] CompressGzip(byte[] data)
    {
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzipStream.Write(data, 0, data.Length);
        }
        return outputStream.ToArray();
    }

    private static byte[] DecompressGzip(byte[] compressedData)
    {
        using var inputStream = new MemoryStream(compressedData);
        using var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        gzipStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    private static byte[] CompressBrotli(byte[] data)
    {
        using var outputStream = new MemoryStream();
        using (var brotliStream = new BrotliStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotliStream.Write(data, 0, data.Length);
        }
        return outputStream.ToArray();
    }

    private static byte[] DecompressBrotli(byte[] compressedData)
    {
        using var inputStream = new MemoryStream(compressedData);
        using var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();
        brotliStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }
}
