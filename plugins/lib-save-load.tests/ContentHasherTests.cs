using BeyondImmersion.BannouService.SaveLoad.Hashing;
using System.Text;

namespace BeyondImmersion.BannouService.SaveLoad.Tests;

/// <summary>
/// Tests for the ContentHasher SHA-256 implementation.
/// </summary>
public class ContentHasherTests
{
    #region ComputeHash Tests

    [Fact]
    public void ComputeHash_WithKnownData_ReturnsExpectedHash()
    {
        // Arrange - "hello world" SHA-256 hash is well-known
        var data = Encoding.UTF8.GetBytes("hello world");
        // SHA-256 of "hello world" = b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9
        var expectedHash = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";

        // Act
        var actualHash = ContentHasher.ComputeHash(data);

        // Assert
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public void ComputeHash_WithEmptyData_ReturnsEmptyStringHash()
    {
        // Arrange - SHA-256 of empty string is also well-known
        var data = Array.Empty<byte>();
        // SHA-256 of "" = e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        var expectedHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        var actualHash = ContentHasher.ComputeHash(data);

        // Assert
        Assert.Equal(expectedHash, actualHash);
    }

    [Fact]
    public void ComputeHash_ReturnsLowercaseHexString()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("test");

        // Act
        var hash = ContentHasher.ComputeHash(data);

        // Assert
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeHash_Returns64CharacterString()
    {
        // Arrange - SHA-256 always produces 32 bytes = 64 hex chars
        var data = Encoding.UTF8.GetBytes("any data");

        // Act
        var hash = ContentHasher.ComputeHash(data);

        // Assert
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void ComputeHash_DifferentData_ProducesDifferentHashes()
    {
        // Arrange
        var data1 = Encoding.UTF8.GetBytes("data1");
        var data2 = Encoding.UTF8.GetBytes("data2");

        // Act
        var hash1 = ContentHasher.ComputeHash(data1);
        var hash2 = ContentHasher.ComputeHash(data2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_SameData_ProducesSameHash()
    {
        // Arrange
        var data1 = Encoding.UTF8.GetBytes("identical");
        var data2 = Encoding.UTF8.GetBytes("identical");

        // Act
        var hash1 = ContentHasher.ComputeHash(data1);
        var hash2 = ContentHasher.ComputeHash(data2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ComputeHash_WithBinaryData_ProducesValidHash()
    {
        // Arrange - binary data including null bytes
        var data = new byte[] { 0x00, 0xFF, 0x7F, 0x80, 0x01 };

        // Act
        var hash = ContentHasher.ComputeHash(data);

        // Assert
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void ComputeHash_WithLargeData_ProducesValidHash()
    {
        // Arrange - 1MB of data
        var data = new byte[1024 * 1024];
        new Random(42).NextBytes(data);

        // Act
        var hash = ContentHasher.ComputeHash(data);

        // Assert
        Assert.NotNull(hash);
        Assert.Equal(64, hash.Length);
    }

    #endregion

    #region VerifyHash Tests

    [Fact]
    public void VerifyHash_WithMatchingHash_ReturnsTrue()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("test data");
        var hash = ContentHasher.ComputeHash(data);

        // Act
        var result = ContentHasher.VerifyHash(data, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyHash_WithNonMatchingHash_ReturnsFalse()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("test data");
        var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";

        // Act
        var result = ContentHasher.VerifyHash(data, wrongHash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyHash_WithUppercaseHash_ReturnsTrueCaseInsensitive()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("test data");
        var lowercaseHash = ContentHasher.ComputeHash(data);
        var uppercaseHash = lowercaseHash.ToUpperInvariant();

        // Act
        var result = ContentHasher.VerifyHash(data, uppercaseHash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyHash_WithMixedCaseHash_ReturnsTrueCaseInsensitive()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("test data");
        var hash = ContentHasher.ComputeHash(data);
        var mixedCase = string.Concat(hash.Select((c, i) => i % 2 == 0 ? char.ToUpper(c) : c));

        // Act
        var result = ContentHasher.VerifyHash(data, mixedCase);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyHash_WithEmptyExpectedHash_ReturnsFalse()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("test data");

        // Act
        var result = ContentHasher.VerifyHash(data, string.Empty);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyHash_WithEmptyData_AndMatchingEmptyHash_ReturnsTrue()
    {
        // Arrange
        var emptyHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        // Act
        var result = ContentHasher.VerifyHash(Array.Empty<byte>(), emptyHash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyHash_WithSlightlyModifiedData_ReturnsFalse()
    {
        // Arrange
        var originalData = Encoding.UTF8.GetBytes("test data");
        var originalHash = ContentHasher.ComputeHash(originalData);
        var modifiedData = Encoding.UTF8.GetBytes("test datb"); // one char different

        // Act
        var result = ContentHasher.VerifyHash(modifiedData, originalHash);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void ComputeAndVerify_Roundtrip_Works()
    {
        // Arrange
        var testCases = new[]
        {
            Encoding.UTF8.GetBytes("simple text"),
            Encoding.UTF8.GetBytes("""{"json":"object","array":[1,2,3]}"""),
            new byte[] { 0, 1, 2, 3, 255, 254, 253 },
            Encoding.UTF8.GetBytes(new string('x', 10000))
        };

        foreach (var data in testCases)
        {
            // Act
            var hash = ContentHasher.ComputeHash(data);
            var verified = ContentHasher.VerifyHash(data, hash);

            // Assert
            Assert.True(verified, $"Failed to verify hash for data of length {data.Length}");
        }
    }

    [Fact]
    public void Hash_IsDeterministic_AcrossMultipleCalls()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("deterministic test");

        // Act
        var hashes = Enumerable.Range(0, 100)
            .Select(_ => ContentHasher.ComputeHash(data))
            .Distinct()
            .ToList();

        // Assert - all hashes should be identical
        Assert.Single(hashes);
    }

    #endregion
}
