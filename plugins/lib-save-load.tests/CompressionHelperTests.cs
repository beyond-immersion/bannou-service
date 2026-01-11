using BeyondImmersion.BannouService.SaveLoad.Compression;
using System.Text;

namespace BeyondImmersion.BannouService.SaveLoad.Tests;

/// <summary>
/// Tests for the CompressionHelper class.
/// </summary>
public class CompressionHelperTests
{
    private static readonly byte[] TestData = Encoding.UTF8.GetBytes(
        "This is test data that will be compressed and decompressed. " +
        "It should be long enough to actually benefit from compression. " +
        "Repeating content helps compression: Lorem ipsum dolor sit amet, " +
        "consectetur adipiscing elit. Lorem ipsum dolor sit amet.");

    #region GZIP Compression Tests

    [Fact]
    public void Compress_WithGzip_CompressesData()
    {
        // Arrange & Act
        var compressed = CompressionHelper.Compress(TestData, CompressionType.GZIP);

        // Assert
        Assert.NotNull(compressed);
        Assert.NotEmpty(compressed);
        Assert.True(compressed.Length < TestData.Length,
            $"Compressed size {compressed.Length} should be less than original {TestData.Length}");
    }

    [Fact]
    public void Decompress_WithGzip_RestoresOriginalData()
    {
        // Arrange
        var compressed = CompressionHelper.Compress(TestData, CompressionType.GZIP);

        // Act
        var decompressed = CompressionHelper.Decompress(compressed, CompressionType.GZIP);

        // Assert
        Assert.Equal(TestData, decompressed);
    }

    [Fact]
    public void GzipRoundtrip_WithVariousDataSizes_RestoresOriginal()
    {
        // Arrange - test various sizes
        var testCases = new[]
        {
            Encoding.UTF8.GetBytes("Small"),
            Encoding.UTF8.GetBytes(new string('x', 100)),
            Encoding.UTF8.GetBytes(new string('y', 10000)),
            Encoding.UTF8.GetBytes("""{"json":"data","nested":{"value":12345}}""")
        };

        foreach (var originalData in testCases)
        {
            // Act
            var compressed = CompressionHelper.Compress(originalData, CompressionType.GZIP);
            var decompressed = CompressionHelper.Decompress(compressed, CompressionType.GZIP);

            // Assert
            Assert.Equal(originalData, decompressed);
        }
    }

    #endregion

    #region Brotli Compression Tests

    [Fact]
    public void Compress_WithBrotli_CompressesData()
    {
        // Arrange & Act
        var compressed = CompressionHelper.Compress(TestData, CompressionType.BROTLI);

        // Assert
        Assert.NotNull(compressed);
        Assert.NotEmpty(compressed);
        Assert.True(compressed.Length < TestData.Length,
            $"Compressed size {compressed.Length} should be less than original {TestData.Length}");
    }

    [Fact]
    public void Decompress_WithBrotli_RestoresOriginalData()
    {
        // Arrange
        var compressed = CompressionHelper.Compress(TestData, CompressionType.BROTLI);

        // Act
        var decompressed = CompressionHelper.Decompress(compressed, CompressionType.BROTLI);

        // Assert
        Assert.Equal(TestData, decompressed);
    }

    [Fact]
    public void BrotliRoundtrip_WithVariousDataSizes_RestoresOriginal()
    {
        // Arrange - test various sizes
        var testCases = new[]
        {
            Encoding.UTF8.GetBytes("Small"),
            Encoding.UTF8.GetBytes(new string('x', 100)),
            Encoding.UTF8.GetBytes(new string('y', 10000))
        };

        foreach (var originalData in testCases)
        {
            // Act
            var compressed = CompressionHelper.Compress(originalData, CompressionType.BROTLI);
            var decompressed = CompressionHelper.Decompress(compressed, CompressionType.BROTLI);

            // Assert
            Assert.Equal(originalData, decompressed);
        }
    }

    [Fact]
    public void Brotli_AchievesBetterCompressionThanGzip_OnRepetitiveData()
    {
        // Arrange - highly repetitive data benefits from Brotli
        var repetitiveData = Encoding.UTF8.GetBytes(new string('A', 10000));

        // Act
        var gzipCompressed = CompressionHelper.Compress(repetitiveData, CompressionType.GZIP);
        var brotliCompressed = CompressionHelper.Compress(repetitiveData, CompressionType.BROTLI);

        // Assert - Brotli typically achieves better compression on repetitive data
        Assert.True(brotliCompressed.Length <= gzipCompressed.Length,
            $"Brotli ({brotliCompressed.Length}) should compress at least as well as GZIP ({gzipCompressed.Length})");
    }

    #endregion

    #region No Compression Tests

    [Fact]
    public void Compress_WithNone_ReturnsOriginalData()
    {
        // Arrange & Act
        var result = CompressionHelper.Compress(TestData, CompressionType.NONE);

        // Assert
        Assert.Same(TestData, result);
    }

    [Fact]
    public void Decompress_WithNone_ReturnsOriginalData()
    {
        // Arrange & Act
        var result = CompressionHelper.Decompress(TestData, CompressionType.NONE);

        // Assert
        Assert.Same(TestData, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Compress_WithNullData_ReturnsEmptyArray()
    {
        // Arrange & Act
        var result = CompressionHelper.Compress(null!, CompressionType.GZIP);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Compress_WithEmptyData_ReturnsEmptyArray()
    {
        // Arrange & Act
        var result = CompressionHelper.Compress(Array.Empty<byte>(), CompressionType.GZIP);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Decompress_WithNullData_ReturnsEmptyArray()
    {
        // Arrange & Act
        var result = CompressionHelper.Decompress(null!, CompressionType.GZIP);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Decompress_WithEmptyData_ReturnsEmptyArray()
    {
        // Arrange & Act
        var result = CompressionHelper.Decompress(Array.Empty<byte>(), CompressionType.GZIP);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Compress_WithSingleByte_Works()
    {
        // Arrange
        var singleByte = new byte[] { 42 };

        // Act
        var compressed = CompressionHelper.Compress(singleByte, CompressionType.GZIP);
        var decompressed = CompressionHelper.Decompress(compressed, CompressionType.GZIP);

        // Assert
        Assert.Equal(singleByte, decompressed);
    }

    #endregion

    #region ShouldCompress Tests

    [Fact]
    public void ShouldCompress_WhenAboveThreshold_ReturnsTrue()
    {
        // Arrange & Act & Assert
        Assert.True(CompressionHelper.ShouldCompress(1000, 500));
        Assert.True(CompressionHelper.ShouldCompress(1000, 1000));
    }

    [Fact]
    public void ShouldCompress_WhenBelowThreshold_ReturnsFalse()
    {
        // Arrange & Act & Assert
        Assert.False(CompressionHelper.ShouldCompress(500, 1000));
        Assert.False(CompressionHelper.ShouldCompress(0, 1000));
    }

    #endregion

    #region CalculateCompressionRatio Tests

    [Fact]
    public void CalculateCompressionRatio_WithValidSizes_ReturnsCorrectRatio()
    {
        // Arrange & Act
        var ratio = CompressionHelper.CalculateCompressionRatio(100, 50);

        // Assert
        Assert.Equal(0.5, ratio);
    }

    [Fact]
    public void CalculateCompressionRatio_WithZeroOriginal_ReturnsOne()
    {
        // Arrange & Act
        var ratio = CompressionHelper.CalculateCompressionRatio(0, 0);

        // Assert
        Assert.Equal(1.0, ratio);
    }

    [Fact]
    public void CalculateCompressionRatio_WhenCompressedIsLarger_ReturnsGreaterThanOne()
    {
        // Arrange & Act - happens with incompressible data
        var ratio = CompressionHelper.CalculateCompressionRatio(10, 15);

        // Assert
        Assert.Equal(1.5, ratio);
    }

    [Fact]
    public void CalculateCompressionRatio_WithRealCompression_ReturnsExpectedRange()
    {
        // Arrange
        var compressed = CompressionHelper.Compress(TestData, CompressionType.GZIP);

        // Act
        var ratio = CompressionHelper.CalculateCompressionRatio(TestData.Length, compressed.Length);

        // Assert - should be less than 1 (good compression)
        Assert.True(ratio < 1.0, $"Expected compression ratio < 1.0, got {ratio}");
        Assert.True(ratio > 0.0, $"Expected compression ratio > 0.0, got {ratio}");
    }

    #endregion
}
