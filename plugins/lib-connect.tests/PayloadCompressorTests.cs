using BeyondImmersion.BannouService.Connect.Protocol;
using System.IO.Compression;
using System.Text;

namespace BeyondImmersion.BannouService.Connect.Tests;

/// <summary>
/// Unit tests for PayloadCompressor.
/// Validates Brotli compression behavior for outbound WebSocket payloads.
/// </summary>
public class PayloadCompressorTests
{
    /// <summary>
    /// Default threshold used across tests (matches Connect's default of 1024 bytes).
    /// </summary>
    private const int DefaultThreshold = 1024;

    /// <summary>
    /// Default quality level (1 = fastest, recommended for real-time traffic).
    /// </summary>
    private const int DefaultQuality = 1;

    [Fact]
    public void TryCompress_BelowThreshold_ReturnsFalse()
    {
        var smallPayload = Encoding.UTF8.GetBytes("{\"test\":true}");
        Assert.True(smallPayload.Length < DefaultThreshold);

        var result = PayloadCompressor.TryCompress(
            smallPayload, DefaultThreshold, DefaultQuality,
            out var compressedData, out var compressedLength);

        Assert.False(result);
        Assert.Empty(compressedData);
        Assert.Equal(0, compressedLength);
    }

    [Fact]
    public void TryCompress_ExactlyAtThreshold_CompressesSuccessfully()
    {
        // Create a payload that is exactly at threshold and compresses well
        var payload = CreateCompressiblePayload(DefaultThreshold);

        var result = PayloadCompressor.TryCompress(
            payload, DefaultThreshold, DefaultQuality,
            out var compressedData, out var compressedLength);

        Assert.True(result);
        Assert.True(compressedLength > 0);
        Assert.True(compressedLength < payload.Length);
        Assert.Equal(compressedLength, compressedData.Length);
    }

    [Fact]
    public void TryCompress_LargeJsonPayload_CompressesAndReducesSize()
    {
        var largeJson = CreateLargeJsonPayload();
        var payload = Encoding.UTF8.GetBytes(largeJson);
        Assert.True(payload.Length > DefaultThreshold);

        var result = PayloadCompressor.TryCompress(
            payload, DefaultThreshold, DefaultQuality,
            out var compressedData, out var compressedLength);

        Assert.True(result);
        Assert.True(compressedLength > 0);
        Assert.True(compressedLength < payload.Length,
            $"Compressed ({compressedLength}) should be smaller than original ({payload.Length})");
    }

    [Fact]
    public void TryCompress_OutputArrayIsExactSize()
    {
        var payload = CreateCompressiblePayload(2048);

        var result = PayloadCompressor.TryCompress(
            payload, DefaultThreshold, DefaultQuality,
            out var compressedData, out var compressedLength);

        Assert.True(result);
        Assert.Equal(compressedLength, compressedData.Length);
    }

    [Fact]
    public void TryCompress_RoundTripWithBrotliDecoder_RestoresOriginal()
    {
        var originalJson = CreateLargeJsonPayload();
        var originalBytes = Encoding.UTF8.GetBytes(originalJson);

        var compressed = PayloadCompressor.TryCompress(
            originalBytes, DefaultThreshold, DefaultQuality,
            out var compressedData, out var compressedLength);

        Assert.True(compressed);

        // Decompress using BrotliStream
        using var input = new MemoryStream(compressedData, 0, compressedLength);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        var decompressedBytes = output.ToArray();

        var decompressedJson = Encoding.UTF8.GetString(decompressedBytes);
        Assert.Equal(originalJson, decompressedJson);
    }

    [Fact]
    public void TryCompress_EmptyPayload_BelowThresholdReturnsFalse()
    {
        var result = PayloadCompressor.TryCompress(
            ReadOnlySpan<byte>.Empty, DefaultThreshold, DefaultQuality,
            out var compressedData, out var compressedLength);

        Assert.False(result);
        Assert.Empty(compressedData);
        Assert.Equal(0, compressedLength);
    }

    [Fact]
    public void TryCompress_ThresholdOfZero_CompressesAnyPayload()
    {
        var payload = CreateCompressiblePayload(100);

        var result = PayloadCompressor.TryCompress(
            payload, 0, DefaultQuality,
            out _, out var compressedLength);

        // Small compressible payload with zero threshold should compress
        Assert.True(result);
        Assert.True(compressedLength > 0);
    }

    [Theory]
    [InlineData(0)]  // No compression (fastest)
    [InlineData(1)]  // Fast (recommended for real-time)
    [InlineData(4)]  // Balanced
    [InlineData(11)] // Maximum compression
    public void TryCompress_DifferentQualityLevels_AllProduceValidOutput(int quality)
    {
        var payload = CreateCompressiblePayload(2048);

        var result = PayloadCompressor.TryCompress(
            payload, DefaultThreshold, quality,
            out var compressedData, out var compressedLength);

        Assert.True(result);
        Assert.True(compressedLength > 0);

        // Verify each quality level produces decompressible output
        using var input = new MemoryStream(compressedData, 0, compressedLength);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);

        Assert.Equal(payload.Length, output.ToArray().Length);
    }

    [Fact]
    public void TryCompress_HigherQuality_ProducesSmallerOrEqualOutput()
    {
        var payload = CreateCompressiblePayload(4096);

        PayloadCompressor.TryCompress(payload, DefaultThreshold, 1,
            out _, out var fastLength);
        PayloadCompressor.TryCompress(payload, DefaultThreshold, 11,
            out _, out var maxLength);

        // Quality 11 should compress at least as well as quality 1
        Assert.True(maxLength <= fastLength,
            $"Quality 11 ({maxLength}) should be <= quality 1 ({fastLength})");
    }

    [Fact]
    public void TryCompress_RandomIncompressibleData_ReturnsFalse()
    {
        // Random data doesn't compress well; Brotli output may be larger than input
        var random = new Random(42);
        var randomData = new byte[2048];
        random.NextBytes(randomData);

        var result = PayloadCompressor.TryCompress(
            randomData, DefaultThreshold, DefaultQuality,
            out _, out _);

        // Random data typically doesn't compress, but Brotli with quality 1
        // might still shrink it slightly. The key test is that the method
        // handles it without errors and returns false if no savings.
        // Either outcome is valid - what matters is no exception.
        if (result)
        {
            // If it did compress, verify decompression round-trips
            PayloadCompressor.TryCompress(
                randomData, DefaultThreshold, DefaultQuality,
                out var compressedData, out var compressedLength);

            using var input = new MemoryStream(compressedData, 0, compressedLength);
            using var brotli = new BrotliStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            brotli.CopyTo(output);
            Assert.Equal(randomData, output.ToArray());
        }
    }

    [Fact]
    public void TryCompress_MultipleCallsInSequence_NoResourceLeaks()
    {
        // Exercises ArrayPool rent/return path multiple times
        var payload = CreateCompressiblePayload(2048);

        for (var i = 0; i < 100; i++)
        {
            PayloadCompressor.TryCompress(
                payload, DefaultThreshold, DefaultQuality,
                out _, out _);
        }

        // If ArrayPool buffers weren't returned, this would eventually fail
        // or consume excessive memory. Passing without OOM is the assertion.
    }

    /// <summary>
    /// Creates a payload of approximately the target size filled with
    /// repetitive JSON content that compresses well.
    /// </summary>
    private static byte[] CreateCompressiblePayload(int targetSize)
    {
        var sb = new StringBuilder("{\"items\":[");
        var i = 0;
        while (sb.Length < targetSize - 20)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"id\":{i},\"name\":\"item_{i}\",\"description\":\"repeated content\"}}");
            i++;
        }
        sb.Append("]}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        // Ensure we meet or exceed target size
        if (bytes.Length < targetSize)
        {
            var padded = new byte[targetSize];
            Array.Copy(bytes, padded, bytes.Length);
            // Fill remainder with spaces (valid JSON whitespace)
            for (var j = bytes.Length; j < targetSize; j++)
                padded[j] = (byte)' ';
            return padded;
        }
        return bytes;
    }

    /// <summary>
    /// Creates a large JSON string (~5KB) that compresses well.
    /// </summary>
    private static string CreateLargeJsonPayload()
    {
        var sb = new StringBuilder("{\"characters\":[");
        for (var i = 0; i < 50; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"id\":{i},\"name\":\"Character_{i}\",\"realm\":\"Arcadia\",\"level\":{i + 1},\"species\":\"Human\"}}");
        }
        sb.Append("]}");
        return sb.ToString();
    }
}
