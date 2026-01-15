using System.Text;
using BeyondImmersion.Bannou.Bundle.Format;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BeyondImmersion.Bannou.AssetBundler.Tests.Bundle;

/// <summary>
/// Tests for BundleValidator validation logic.
/// </summary>
public class BundleValidatorTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BundleValidator _validator;

    public BundleValidatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"validator-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _validator = new BundleValidator(NullLogger<BundleValidator>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateValidBundle(string name = "test-bundle")
    {
        var bundlePath = Path.Combine(_tempDir, $"{name}.bannou");
        using var stream = File.Create(bundlePath);
        using var writer = new BannouBundleWriter(stream);

        writer.AddAsset("asset-1", "file1.txt", "text/plain", Encoding.UTF8.GetBytes("Content 1"));
        writer.AddAsset("asset-2", "file2.txt", "text/plain", Encoding.UTF8.GetBytes("Content 2"));
        writer.Finalize(name, "Test Bundle", "1.0.0", "test-author");

        return bundlePath;
    }

    [Fact]
    public async Task ValidateAsync_ValidBundle_ReturnsValid()
    {
        // Arrange
        var bundlePath = CreateValidBundle();

        // Act
        await using var stream = File.OpenRead(bundlePath);
        var result = await _validator.ValidateAsync(stream, "test-bundle");

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(ValidationStage.Complete, result.Stage);
    }

    [Fact]
    public async Task ValidateAsync_EmptyFile_ReturnsFailed()
    {
        // Arrange
        var bundlePath = Path.Combine(_tempDir, "empty.bannou");
        await File.WriteAllBytesAsync(bundlePath, Array.Empty<byte>());

        // Act
        await using var stream = File.OpenRead(bundlePath);
        var result = await _validator.ValidateAsync(stream, "empty");

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public async Task ValidateAsync_BundleIdMismatch_ReturnsError()
    {
        // Arrange
        var bundlePath = CreateValidBundle("actual-id");

        // Act
        await using var stream = File.OpenRead(bundlePath);
        var result = await _validator.ValidateAsync(stream, "expected-id"); // Different ID

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "BUNDLE_ID_MISMATCH");
    }

    [Fact]
    public async Task ValidateAsync_ValidBundle_ExtractsAssetInfo()
    {
        // Arrange
        var bundlePath = CreateValidBundle();

        // Act
        await using var stream = File.OpenRead(bundlePath);
        var result = await _validator.ValidateAsync(stream, "test-bundle");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(2, result.ExtractedAssets.Count);
        Assert.Contains(result.ExtractedAssets, a => a.AssetId == "asset-1");
        Assert.Contains(result.ExtractedAssets, a => a.AssetId == "asset-2");
    }

    [Fact]
    public void BundleValidatorOptions_HasSensibleDefaults()
    {
        // Arrange & Act
        var options = new BundleValidatorOptions();

        // Assert
        Assert.True(options.MaxAssetCount > 0);
        Assert.True(options.MaxAssetSizeBytes > 0);
        Assert.NotEmpty(options.ForbiddenContentTypes);
    }

    [Fact]
    public async Task ValidateAsync_WithCustomOptions_UsesOptions()
    {
        // Arrange
        var options = new BundleValidatorOptions
        {
            MaxAssetCount = 1 // Only allow 1 asset
        };
        var validator = new BundleValidator(NullLogger<BundleValidator>.Instance, options);
        var bundlePath = CreateValidBundle(); // Has 2 assets

        // Act
        await using var stream = File.OpenRead(bundlePath);
        var result = await validator.ValidateAsync(stream, "test-bundle");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "TOO_MANY_ASSETS");
    }

    [Fact]
    public void ValidationError_HasRequiredFields()
    {
        // Arrange & Act
        var error = new ValidationError
        {
            Code = "TEST_ERROR",
            Message = "Test message",
            Severity = ValidationSeverity.Error
        };

        // Assert
        Assert.Equal("TEST_ERROR", error.Code);
        Assert.Equal("Test message", error.Message);
        Assert.Equal(ValidationSeverity.Error, error.Severity);
    }

    [Fact]
    public void ExtractedAssetInfo_HasAllFields()
    {
        // Arrange & Act
        var info = new ExtractedAssetInfo
        {
            AssetId = "test-asset",
            Filename = "test.txt",
            ContentType = "text/plain",
            ContentHash = "abc123",
            Size = 1000
        };

        // Assert
        Assert.Equal("test-asset", info.AssetId);
        Assert.Equal("test.txt", info.Filename);
        Assert.Equal("text/plain", info.ContentType);
        Assert.Equal("abc123", info.ContentHash);
        Assert.Equal(1000, info.Size);
    }
}
