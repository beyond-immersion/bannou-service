using Amazon.S3;
using Amazon.S3.Model;
using BeyondImmersion.BannouService.Asset.Storage;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Asset.Tests.Storage;

/// <summary>
/// Unit tests for MinioStorageProvider focusing on URL rewriting behavior.
/// Tests that pre-signed URLs are correctly rewritten when PublicEndpoint differs from Endpoint.
/// </summary>
public class MinioStorageProviderTests
{
    private readonly Mock<IMinioClient> _mockMinioClient;
    private readonly Mock<IAmazonS3> _mockS3Client;
    private readonly Mock<ILogger<MinioStorageProvider>> _mockLogger;
    private readonly Mock<IMessageBus> _mockMessageBus;

    public MinioStorageProviderTests()
    {
        _mockMinioClient = new Mock<IMinioClient>();
        _mockS3Client = new Mock<IAmazonS3>();
        _mockLogger = new Mock<ILogger<MinioStorageProvider>>();
        _mockMessageBus = new Mock<IMessageBus>();
    }

    private MinioStorageProvider CreateProvider(MinioStorageOptions options)
    {
        return new MinioStorageProvider(
            _mockMinioClient.Object,
            _mockS3Client.Object,
            Options.Create(options),
            _mockLogger.Object,
            _mockMessageBus.Object);
    }

    #region URL Rewriting Tests - GenerateUploadUrlAsync (uses AWS SDK)

    [Fact]
    public async Task GenerateUploadUrlAsync_WithPublicEndpoint_RewritesHttpUrlToHttps()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = "demo.example.com:9000"
        };
        var provider = CreateProvider(options);
        var internalUrl = "http://minio:9000/bucket/test-key?X-Amz-Signature=abc123";

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync(internalUrl);

        // Act
        var result = await provider.GenerateUploadUrlAsync(
            "bucket", "test-key", "application/json", 1024, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal("https://demo.example.com:9000/bucket/test-key?X-Amz-Signature=abc123", result.UploadUrl);
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_WithPublicEndpoint_RewritesHttpsUrl()
    {
        // Arrange: MinIO configured with HTTPS internally
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = "storage.example.com"
        };
        var provider = CreateProvider(options);
        var internalUrl = "https://minio:9000/bucket/test-key?X-Amz-Signature=abc123";

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync(internalUrl);

        // Act
        var result = await provider.GenerateUploadUrlAsync(
            "bucket", "test-key", "application/json", 1024, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal("https://storage.example.com/bucket/test-key?X-Amz-Signature=abc123", result.UploadUrl);
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_WithNullPublicEndpoint_ReturnsOriginalUrl()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = null
        };
        var provider = CreateProvider(options);
        var internalUrl = "http://minio:9000/bucket/test-key?X-Amz-Signature=abc123";

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync(internalUrl);

        // Act
        var result = await provider.GenerateUploadUrlAsync(
            "bucket", "test-key", "application/json", 1024, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(internalUrl, result.UploadUrl);
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_WithSamePublicEndpoint_ReturnsOriginalUrl()
    {
        // Arrange: PublicEndpoint same as Endpoint (no rewriting needed)
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = "minio:9000"
        };
        var provider = CreateProvider(options);
        var internalUrl = "http://minio:9000/bucket/test-key?X-Amz-Signature=abc123";

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync(internalUrl);

        // Act
        var result = await provider.GenerateUploadUrlAsync(
            "bucket", "test-key", "application/json", 1024, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(internalUrl, result.UploadUrl);
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_SetsContentTypeInRequest()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = null
        };
        var provider = CreateProvider(options);
        GetPreSignedUrlRequest? capturedRequest = null;

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .Callback<GetPreSignedUrlRequest>(req => capturedRequest = req)
            .ReturnsAsync("http://minio:9000/bucket/test-key?X-Amz-Signature=abc123");

        // Act
        await provider.GenerateUploadUrlAsync(
            "bucket", "test-key", "application/x-bannou-bundle", 1024, TimeSpan.FromHours(1));

        // Assert - Content-Type is set in the request (will be signed)
        Assert.NotNull(capturedRequest);
        Assert.Equal("application/x-bannou-bundle", capturedRequest.ContentType);
        Assert.Equal(HttpVerb.PUT, capturedRequest.Verb);
        Assert.Equal("bucket", capturedRequest.BucketName);
        Assert.Equal("test-key", capturedRequest.Key);
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_SetsMetadataInRequest()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = null
        };
        var provider = CreateProvider(options);
        GetPreSignedUrlRequest? capturedRequest = null;
        var metadata = new Dictionary<string, string>
        {
            { "upload-id", "test-upload-123" },
            { "bundle-id", "bundle-456" }
        };

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .Callback<GetPreSignedUrlRequest>(req => capturedRequest = req)
            .ReturnsAsync("http://minio:9000/bucket/test-key?X-Amz-Signature=abc123");

        // Act
        await provider.GenerateUploadUrlAsync(
            "bucket", "test-key", "application/json", 1024, TimeSpan.FromHours(1), metadata);

        // Assert - Metadata is added to the request (will be signed as x-amz-meta-* headers)
        Assert.NotNull(capturedRequest);
        Assert.Equal("test-upload-123", capturedRequest.Metadata["upload-id"]);
        Assert.Equal("bundle-456", capturedRequest.Metadata["bundle-id"]);
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_ReturnsRequiredHeadersIncludingMetadata()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = null
        };
        var provider = CreateProvider(options);
        var metadata = new Dictionary<string, string>
        {
            { "upload-id", "test-upload-123" }
        };

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync("http://minio:9000/bucket/test-key?X-Amz-Signature=abc123");

        // Act
        var result = await provider.GenerateUploadUrlAsync(
            "bucket", "test-key", "application/json", 1024, TimeSpan.FromHours(1), metadata);

        // Assert - RequiredHeaders includes Content-Type and x-amz-meta headers
        Assert.Contains("Content-Type", result.RequiredHeaders.Keys);
        Assert.Equal("application/json", result.RequiredHeaders["Content-Type"]);
        Assert.Contains("x-amz-meta-upload-id", result.RequiredHeaders.Keys);
        Assert.Equal("test-upload-123", result.RequiredHeaders["x-amz-meta-upload-id"]);
    }

    #endregion

    #region URL Rewriting Tests - GenerateDownloadUrlAsync

    [Fact]
    public async Task GenerateDownloadUrlAsync_WithPublicEndpoint_RewritesUrl()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = "demo.example.com:9000"
        };
        var provider = CreateProvider(options);
        var internalUrl = "http://minio:9000/bucket/asset-123?X-Amz-Signature=xyz789";

        _mockMinioClient
            .Setup(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
            .ReturnsAsync(internalUrl);

        // Mock StatObject for metadata retrieval (called by GenerateDownloadUrlAsync)
        _mockMinioClient
            .Setup(c => c.StatObjectAsync(It.IsAny<StatObjectArgs>(), default))
            .ThrowsAsync(new Exception("Object not found"));

        // Act
        var result = await provider.GenerateDownloadUrlAsync("bucket", "asset-123");

        // Assert
        Assert.Equal("https://demo.example.com:9000/bucket/asset-123?X-Amz-Signature=xyz789", result.DownloadUrl);
    }

    [Fact]
    public async Task GenerateDownloadUrlAsync_WithNullPublicEndpoint_ReturnsOriginalUrl()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = null
        };
        var provider = CreateProvider(options);
        var internalUrl = "http://minio:9000/bucket/asset-123?X-Amz-Signature=xyz789";

        _mockMinioClient
            .Setup(c => c.PresignedGetObjectAsync(It.IsAny<PresignedGetObjectArgs>()))
            .ReturnsAsync(internalUrl);

        _mockMinioClient
            .Setup(c => c.StatObjectAsync(It.IsAny<StatObjectArgs>(), default))
            .ThrowsAsync(new Exception("Object not found"));

        // Act
        var result = await provider.GenerateDownloadUrlAsync("bucket", "asset-123");

        // Assert
        Assert.Equal(internalUrl, result.DownloadUrl);
    }

    #endregion

    #region URL Rewriting Tests - InitiateMultipartUploadAsync

    [Fact]
    public async Task InitiateMultipartUploadAsync_WithPublicEndpoint_RewritesAllPartUrls()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = "storage.example.com:9000"
        };
        var provider = CreateProvider(options);
        var callCount = 0;

        // Mock to return internal URLs for each part
        _mockMinioClient
            .Setup(c => c.PresignedPutObjectAsync(It.IsAny<PresignedPutObjectArgs>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return $"http://minio:9000/bucket/large-file.part{callCount}?X-Amz-Signature=sig";
            });

        // Act
        var result = await provider.InitiateMultipartUploadAsync(
            "bucket", "large-file", "application/octet-stream", 3, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(3, result.Parts.Count);
        foreach (var part in result.Parts)
        {
            Assert.StartsWith("https://storage.example.com:9000/bucket/", part.UploadUrl);
            Assert.DoesNotContain("minio:9000", part.UploadUrl);
        }
    }

    [Fact]
    public async Task InitiateMultipartUploadAsync_WithNullPublicEndpoint_ReturnsOriginalUrls()
    {
        // Arrange
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = null
        };
        var provider = CreateProvider(options);
        var callCount = 0;

        _mockMinioClient
            .Setup(c => c.PresignedPutObjectAsync(It.IsAny<PresignedPutObjectArgs>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return $"http://minio:9000/bucket/large-file.part{callCount}?X-Amz-Signature=sig";
            });

        // Act
        var result = await provider.InitiateMultipartUploadAsync(
            "bucket", "large-file", "application/octet-stream", 3, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(3, result.Parts.Count);
        foreach (var part in result.Parts)
        {
            Assert.StartsWith("http://minio:9000/bucket/", part.UploadUrl);
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task GenerateUploadUrlAsync_WithEmptyPublicEndpoint_ReturnsOriginalUrl()
    {
        // Arrange: Empty string should be treated like null
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = ""
        };
        var provider = CreateProvider(options);
        var internalUrl = "http://minio:9000/bucket/test-key?X-Amz-Signature=abc123";

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync(internalUrl);

        // Act
        var result = await provider.GenerateUploadUrlAsync(
            "bucket", "test-key", "application/json", 1024, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(internalUrl, result.UploadUrl);
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_WithSubdomainPublicEndpoint_RewritesCorrectly()
    {
        // Arrange: Subdomain pattern (no port)
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = "storage.demo.beyond-immersion.com"
        };
        var provider = CreateProvider(options);
        var internalUrl = "http://minio:9000/bannou-assets/test-key?X-Amz-Signature=abc123";

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync(internalUrl);

        // Act
        var result = await provider.GenerateUploadUrlAsync(
            "bannou-assets", "test-key", "application/json", 1024, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal("https://storage.demo.beyond-immersion.com/bannou-assets/test-key?X-Amz-Signature=abc123", result.UploadUrl);
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_PreservesQueryParameters()
    {
        // Arrange: Verify full signature and all query params are preserved
        var options = new MinioStorageOptions
        {
            Endpoint = "minio:9000",
            PublicEndpoint = "demo.example.com:9000"
        };
        var provider = CreateProvider(options);
        var internalUrl = "http://minio:9000/bucket/key?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=abc&X-Amz-Date=20250115&X-Amz-Expires=3600&X-Amz-Signature=xyz123";

        _mockS3Client
            .Setup(c => c.GetPreSignedURLAsync(It.IsAny<GetPreSignedUrlRequest>()))
            .ReturnsAsync(internalUrl);

        // Act
        var result = await provider.GenerateUploadUrlAsync(
            "bucket", "key", "application/json", 1024, TimeSpan.FromHours(1));

        // Assert - All query parameters should be preserved
        Assert.Contains("X-Amz-Algorithm=AWS4-HMAC-SHA256", result.UploadUrl);
        Assert.Contains("X-Amz-Credential=abc", result.UploadUrl);
        Assert.Contains("X-Amz-Date=20250115", result.UploadUrl);
        Assert.Contains("X-Amz-Expires=3600", result.UploadUrl);
        Assert.Contains("X-Amz-Signature=xyz123", result.UploadUrl);
        Assert.StartsWith("https://demo.example.com:9000/", result.UploadUrl);
    }

    #endregion
}
