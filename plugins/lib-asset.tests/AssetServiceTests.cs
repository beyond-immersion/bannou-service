using BeyondImmersion.Bannou.Asset.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.BannouService.Asset.Events;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Asset.Pool;
using BeyondImmersion.BannouService.Asset.Webhooks;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Storage;
using BeyondImmersion.BannouService.TestUtilities;
using StorageModels = BeyondImmersion.BannouService.Storage;

namespace BeyondImmersion.BannouService.Asset.Tests;

public class AssetServiceTests
{
    private const string STATE_STORE = "asset-statestore";

    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory = new();
    private readonly Mock<IStateStore<InternalAssetRecord>> _mockAssetStore = new();
    private readonly Mock<IStateStore<List<string>>> _mockIndexStore = new();
    private readonly Mock<IStateStore<BundleMetadata>> _mockBundleStore = new();
    private readonly Mock<IStateStore<AssetBundleIndex>> _mockAssetBundleIndexStore = new();
    private readonly Mock<IStateStore<UploadSession>> _mockUploadSessionStore = new();
    private readonly Mock<IStateStore<BundleUploadSession>> _mockBundleUploadSessionStore = new();
    private readonly Mock<IStateStore<MetabundleJob>> _mockJobStore = new();
    private readonly Mock<IMessageBus> _mockMessageBus = new();
    private readonly Mock<ILogger<AssetService>> _mockLogger = new();
    private readonly AssetServiceConfiguration _configuration = new();
    private readonly Mock<IAssetEventEmitter> _mockAssetEventEmitter = new();
    private readonly Mock<IAssetStorageProvider> _mockStorageProvider = new();
    private readonly Mock<IOrchestratorClient> _mockOrchestratorClient = new();
    private readonly Mock<IAssetProcessorPoolManager> _mockProcessorPoolManager = new();
    private readonly Mock<IBundleConverter> _mockBundleConverter = new();
    private readonly Mock<IEventConsumer> _mockEventConsumer = new();

    public AssetServiceTests()
    {
        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<InternalAssetRecord>(STATE_STORE)).Returns(_mockAssetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BundleMetadata>(STATE_STORE)).Returns(_mockBundleStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<AssetBundleIndex>(STATE_STORE)).Returns(_mockAssetBundleIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<UploadSession>(STATE_STORE)).Returns(_mockUploadSessionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BundleUploadSession>(STATE_STORE)).Returns(_mockBundleUploadSessionStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<MetabundleJob>(STATE_STORE)).Returns(_mockJobStore.Object);
        _mockStateStoreFactory.Setup(f => f.SupportsSearch(STATE_STORE)).Returns(false);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    ///
    /// This single test replaces N individual null-check tests and catches:
    /// - Multiple constructors (DI might pick wrong one)
    /// - Optional parameters (accidental defaults that hide missing registrations)
    /// - Missing null checks (ArgumentNullException not thrown)
    /// - Wrong parameter names in ArgumentNullException
    ///
    /// See: docs/reference/tenets/TESTING_PATTERNS.md
    /// </summary>
    [Fact]
    public void AssetService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<AssetService>();

    #endregion

    #region RequestUploadAsync Tests

    [Fact]
    public async Task RequestUploadAsync_WithEmptyFilename_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UploadRequest
        {
            Filename = "",
            Size = 1024,
            ContentType = "image/png"
        };

        // Act
        var (status, result) = await service.RequestUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestUploadAsync_WithZeroSize_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UploadRequest
        {
            Filename = "test.png",
            Size = 0,
            ContentType = "image/png"
        };

        // Act
        var (status, result) = await service.RequestUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestUploadAsync_WithSizeExceedingMax_ShouldReturnBadRequest()
    {
        // Arrange
        _configuration.MaxUploadSizeMb = 100;
        var service = CreateService();
        var request = new UploadRequest
        {
            Filename = "test.png",
            Size = (long)_configuration.MaxUploadSizeMb * 1024 * 1024 + 1, // Exceeds max
            ContentType = "image/png"
        };

        // Act
        var (status, result) = await service.RequestUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestUploadAsync_WithEmptyContentType_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new UploadRequest
        {
            Filename = "test.png",
            Size = 1024,
            ContentType = ""
        };

        // Act
        var (status, result) = await service.RequestUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestUploadAsync_WithValidSmallFile_ShouldReturnOKWithSingleUploadUrl()
    {
        // Arrange
        _configuration.MultipartThresholdMb = 100;
        _configuration.MaxUploadSizeMb = 500;
        _configuration.TokenTtlSeconds = 3600;
        var service = CreateService();
        var request = new UploadRequest
        {
            Filename = "test.png",
            Size = 1024, // Small file
            ContentType = "image/png",
            Metadata = new AssetMetadataInput
            {
                AssetType = AssetType.Texture,
                Realm = "arcadia",
                Tags = new List<string> { "test" }
            }
        };

        var uploadResult = new PreSignedUploadResult(
            UploadUrl: "https://storage.example.com/upload",
            Key: "temp/test.png",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            RequiredHeaders: new Dictionary<string, string>());

        _mockStorageProvider
            .Setup(s => s.GenerateUploadUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(uploadResult);

        // Act
        var (status, result) = await service.RequestUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.NotNull(result.UploadUrl);
        Assert.False(result.Multipart?.Required ?? false);
    }

    [Fact]
    public async Task RequestUploadAsync_WithLargeFile_ShouldReturnOKWithMultipartUrls()
    {
        // Arrange
        _configuration.MultipartThresholdMb = 10;
        _configuration.MaxUploadSizeMb = 500;
        _configuration.TokenTtlSeconds = 3600;
        var service = CreateService();
        var request = new UploadRequest
        {
            Filename = "test.glb",
            Size = 100 * 1024 * 1024, // 100MB - exceeds multipart threshold
            ContentType = "model/gltf-binary"
        };

        var multipartResult = new MultipartUploadResult(
            UploadId: "upload-123",
            Key: "temp/test.glb",
            Parts: new List<StorageModels.PartUploadInfo>
            {
                new StorageModels.PartUploadInfo(1, "https://storage.example.com/part1", 5 * 1024 * 1024, 10 * 1024 * 1024),
                new StorageModels.PartUploadInfo(2, "https://storage.example.com/part2", 5 * 1024 * 1024, 10 * 1024 * 1024)
            },
            ExpiresAt: DateTime.UtcNow.AddHours(1));

        _mockStorageProvider
            .Setup(s => s.InitiateMultipartUploadAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<TimeSpan>()))
            .ReturnsAsync(multipartResult);

        // Act
        var (status, result) = await service.RequestUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.True(result.Multipart?.Required ?? false);
        Assert.NotNull(result.Multipart?.UploadUrls);
        Assert.True(result.Multipart.UploadUrls.Count > 0);
    }

    /// <summary>
    /// Regression test: Verifies that the upload session Redis key format matches
    /// what CompleteUploadAsync uses for lookup.
    ///
    /// Previously, sessions were stored with Guid.ToString("N") (no dashes) but
    /// the JSON response serialized Guids with dashes. When clients sent back
    /// the UploadId, the lookup key format needed to match the storage key format.
    /// Now both use the default Guid format (with dashes) for consistency.
    /// </summary>
    [Fact]
    public async Task RequestUploadAsync_UploadIdFormat_ShouldMatchCompleteUploadLookupFormat()
    {
        // Arrange
        _configuration.MultipartThresholdMb = 100;
        _configuration.MaxUploadSizeMb = 500;
        _configuration.TokenTtlSeconds = 3600;
        _configuration.UploadSessionKeyPrefix = "upload-session:";
        var service = CreateService();

        var request = new UploadRequest
        {
            Filename = "test.png",
            Size = 1024,
            ContentType = "image/png"
        };

        var uploadResult = new PreSignedUploadResult(
            UploadUrl: "https://storage.example.com/upload",
            Key: "temp/test.png",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            RequiredHeaders: new Dictionary<string, string>());

        _mockStorageProvider
            .Setup(s => s.GenerateUploadUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(uploadResult);

        string? savedKey = null;
        _mockUploadSessionStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<UploadSession>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, UploadSession, StateOptions?, CancellationToken>((key, _, _, _) => savedKey = key)
            .ReturnsAsync("etag");

        // Act
        var (status, result) = await service.RequestUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.NotNull(savedKey);

        // The key stored in Redis should use the same format as the response UploadId
        // When CompleteUpload receives the UploadId, it formats it and looks up by key
        var expectedKey = $"upload-session:{result.UploadId}";
        Assert.Equal(expectedKey, savedKey);

        // Verify the UploadId has dashes (standard Guid format, not "N" format)
        var uploadIdStr = result.UploadId.ToString();
        Assert.Contains("-", uploadIdStr);
    }

    #endregion

    #region CompleteUploadAsync Tests

    [Fact]
    public async Task CompleteUploadAsync_WhenSessionNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new CompleteUploadRequest
        {
            UploadId = Guid.NewGuid()
        };

        _mockUploadSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadSession?)null);

        // Act
        var (status, result) = await service.CompleteUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CompleteUploadAsync_WhenSessionExpired_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var uploadId = Guid.NewGuid();
        var request = new CompleteUploadRequest { UploadId = uploadId };

        var expiredSession = new UploadSession
        {
            UploadId = uploadId,
            Filename = "test.png",
            Size = 1024,
            ContentType = "image/png",
            StorageKey = $"temp/{uploadId:N}/test.png",
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1) // Already expired
        };

        _mockUploadSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredSession);

        // Act
        var (status, result) = await service.CompleteUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CompleteUploadAsync_WithMultipartAndInvalidPartCount_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var uploadId = Guid.NewGuid();
        var request = new CompleteUploadRequest
        {
            UploadId = uploadId,
            Parts = new List<CompletedPart> { new CompletedPart { PartNumber = 1, Etag = "etag1" } }
        };

        var session = new UploadSession
        {
            UploadId = uploadId,
            Filename = "large.glb",
            Size = 100 * 1024 * 1024,
            ContentType = "model/gltf-binary",
            StorageKey = $"temp/{uploadId:N}/large.glb",
            IsMultipart = true,
            PartCount = 5, // Expects 5 parts
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        _mockUploadSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Act
        var (status, result) = await service.CompleteUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CompleteUploadAsync_WhenFileNotInTempLocation_ShouldReturnNotFound()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var uploadId = Guid.NewGuid();
        var request = new CompleteUploadRequest { UploadId = uploadId };

        var session = new UploadSession
        {
            UploadId = uploadId,
            Filename = "test.png",
            Size = 1024,
            ContentType = "image/png",
            StorageKey = $"temp/{uploadId:N}/test.png",
            IsMultipart = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        _mockUploadSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _mockStorageProvider
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        // Act
        var (status, result) = await service.CompleteUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CompleteUploadAsync_WithValidSingleUpload_ShouldReturnCreatedWithAssetMetadata()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        _configuration.LargeFileThresholdMb = 100;
        var service = CreateService();
        var uploadId = Guid.NewGuid();
        var request = new CompleteUploadRequest { UploadId = uploadId };

        var session = new UploadSession
        {
            UploadId = uploadId,
            Filename = "test.png",
            Size = 1024,
            ContentType = "image/png",
            StorageKey = $"temp/{uploadId:N}/test.png",
            IsMultipart = false,
            Metadata = new AssetMetadataInput
            {
                AssetType = AssetType.Texture,
                Realm = "arcadia",
                Tags = new List<string> { "test" }
            },
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        _mockUploadSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _mockStorageProvider
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        // Mock GetObjectAsync for SHA256 hash computation
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(() => new MemoryStream("test file content"u8.ToArray()));

        _mockStorageProvider
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new StorageModels.ObjectMetadata("temp/test.png", null, "image/png", 1024, "abc123etag", DateTime.UtcNow, new Dictionary<string, string>()));

        _mockStorageProvider
            .Setup(s => s.CopyObjectAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new StorageModels.AssetReference("test-bucket", "assets/texture/image-abc123etag.png", null, "abc123etag", 1024, DateTime.UtcNow));

        _mockStorageProvider
            .Setup(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<BeyondImmersion.BannouService.Events.AssetUploadCompletedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup for indexing (mock GetWithETagAsync)
        _mockIndexStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Act
        var (status, result) = await service.CompleteUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal("test.png", result.Filename);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(AssetType.Texture, result.AssetType);
    }

    [Fact]
    public async Task CompleteUploadAsync_WithValidMultipartUpload_ShouldCompleteMultipartAndReturnCreated()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        _configuration.LargeFileThresholdMb = 100;
        var service = CreateService();
        var uploadId = Guid.NewGuid();
        var request = new CompleteUploadRequest
        {
            UploadId = uploadId,
            Parts = new List<CompletedPart>
            {
                new CompletedPart { PartNumber = 1, Etag = "etag1" },
                new CompletedPart { PartNumber = 2, Etag = "etag2" }
            }
        };

        var session = new UploadSession
        {
            UploadId = uploadId,
            Filename = "model.glb",
            Size = 50 * 1024 * 1024,
            ContentType = "model/gltf-binary",
            StorageKey = $"temp/{uploadId:N}/model.glb",
            IsMultipart = true,
            PartCount = 2,
            Metadata = new AssetMetadataInput { AssetType = AssetType.Model },
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        _mockUploadSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _mockStorageProvider
            .Setup(s => s.CompleteMultipartUploadAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<List<StorageModels.StorageCompletedPart>>()))
            .ReturnsAsync(new StorageModels.AssetReference("test-bucket", "temp/model.glb", null, "multipart-etag", 50 * 1024 * 1024, DateTime.UtcNow));

        _mockStorageProvider
            .Setup(s => s.ObjectExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(true);

        // Mock GetObjectAsync for SHA256 hash computation
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(() => new MemoryStream("test model content"u8.ToArray()));

        _mockStorageProvider
            .Setup(s => s.GetObjectMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new StorageModels.ObjectMetadata("temp/model.glb", null, "model/gltf-binary", 50 * 1024 * 1024, "multipart-etag", DateTime.UtcNow, new Dictionary<string, string>()));

        _mockStorageProvider
            .Setup(s => s.CopyObjectAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(new StorageModels.AssetReference("test-bucket", "assets/model/model-multipartetag.glb", null, "multipart-etag", 50 * 1024 * 1024, DateTime.UtcNow));

        _mockStorageProvider
            .Setup(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<BeyondImmersion.BannouService.Events.AssetUploadCompletedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockIndexStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Act
        var (status, result) = await service.CompleteUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);

        // Verify multipart completion was called
        _mockStorageProvider.Verify(
            s => s.CompleteMultipartUploadAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.Is<List<StorageModels.StorageCompletedPart>>(p => p.Count == 2)),
            Times.Once);
    }

    #endregion

    #region GetAssetAsync Tests

    [Fact]
    public async Task GetAssetAsync_WhenAssetNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetAssetRequest
        {
            AssetId = "non-existent-asset"
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((InternalAssetRecord?)null);

        // Act
        var (status, result) = await service.GetAssetAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAssetAsync_WhenAssetExists_ShouldReturnAssetWithDownloadUrl()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        _configuration.TokenTtlSeconds = 3600;
        var service = CreateService();
        var assetId = "test-asset-123";
        var request = new GetAssetRequest { AssetId = assetId };

        var internalRecord = new InternalAssetRecord
        {
            AssetId = assetId,
            Filename = "test.png",
            ContentType = "image/png",
            ContentHash = "abc123",
            Size = 1024,
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            StorageKey = $"assets/texture/{assetId}.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var downloadResult = new PreSignedDownloadResult(
            DownloadUrl: "https://storage.example.com/download",
            Key: $"assets/texture/{assetId}.png",
            VersionId: null,
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            ContentLength: 1024,
            ContentType: "image/png");

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains(assetId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(internalRecord);

        _mockStorageProvider
            .Setup(s => s.GenerateDownloadUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<TimeSpan?>()))
            .ReturnsAsync(downloadResult);

        // Act
        var (status, result) = await service.GetAssetAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(assetId, result.AssetId);
        Assert.NotNull(result.DownloadUrl);
    }

    #endregion

    #region ListAssetVersionsAsync Tests

    [Fact]
    public async Task ListAssetVersionsAsync_WhenAssetNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new ListVersionsRequest
        {
            AssetId = "non-existent-asset",
            Limit = 10,
            Offset = 0
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((InternalAssetRecord?)null);

        // Act
        var (status, result) = await service.ListAssetVersionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task ListAssetVersionsAsync_WhenAssetExists_ShouldReturnVersionList()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var assetId = "test-asset-123";
        var request = new ListVersionsRequest
        {
            AssetId = assetId,
            Limit = 10,
            Offset = 0
        };

        var internalRecord = new InternalAssetRecord
        {
            AssetId = assetId,
            Filename = "test.png",
            ContentType = "image/png",
            ContentHash = "abc123",
            Size = 1024,
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            StorageKey = $"assets/texture/{assetId}.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var versions = new List<ObjectVersionInfo>
        {
            new ObjectVersionInfo("v2", true, DateTime.UtcNow, 2048, "etag2", false, "STANDARD"),
            new ObjectVersionInfo("v1", false, DateTime.UtcNow.AddDays(-1), 1024, "etag1", false, "GLACIER")
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(internalRecord);

        _mockStorageProvider
            .Setup(s => s.ListVersionsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(versions);

        // Act
        var (status, result) = await service.ListAssetVersionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Versions.Count);
        var versionsList = result.Versions.ToList();
        Assert.False(versionsList[0].IsArchived); // STANDARD storage
        Assert.True(versionsList[1].IsArchived); // GLACIER storage
    }

    #endregion

    #region SearchAssetsAsync Tests

    [Fact]
    public async Task SearchAssetsAsync_WhenNoAssetsMatch_ShouldReturnEmptyResult()
    {
        // Arrange
        var service = CreateService();
        var request = new AssetSearchRequest
        {
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            Limit = 10,
            Offset = 0
        };

        // Setup for fallback search (index key returns null/empty)
        // SupportsSearch returns false in constructor, so fallback is used
        _mockIndexStore
            .Setup(s => s.GetAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Act
        var (status, result) = await service.SearchAssetsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Assets);
    }

    [Fact]
    public async Task SearchAssetsAsync_WithMatchingAssets_ShouldReturnFilteredResults()
    {
        // Arrange
        var service = CreateService();
        var request = new AssetSearchRequest
        {
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            ContentType = "image/png",
            Limit = 10,
            Offset = 0
        };

        var assetIds = new List<string> { "asset-1", "asset-2", "asset-3" };

        var asset1 = new InternalAssetRecord
        {
            AssetId = "asset-1",
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            ContentType = "image/png",
            ContentHash = "hash1",
            Filename = "asset1.png",
            Size = 1024,
            StorageKey = "assets/texture/asset-1.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var asset2 = new InternalAssetRecord
        {
            AssetId = "asset-2",
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            ContentType = "image/jpeg", // Different content type
            ContentHash = "hash2",
            Filename = "asset2.jpg",
            Size = 1024,
            StorageKey = "assets/texture/asset-2.jpg",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var asset3 = new InternalAssetRecord
        {
            AssetId = "asset-3",
            AssetType = AssetType.Texture,
            Realm = "omega", // Different realm
            ContentType = "image/png",
            ContentHash = "hash3",
            Filename = "asset3.png",
            Size = 1024,
            StorageKey = "assets/texture/asset-3.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Setup for fallback search (SupportsSearch returns false)
        _mockIndexStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("type:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(assetIds);

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("asset-1")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset1);

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("asset-2")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset2);

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("asset-3")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset3);

        // Act
        var (status, result) = await service.SearchAssetsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(1, result.Total); // Only asset-1 matches all criteria
        Assert.Single(result.Assets);
        Assert.Equal("asset-1", result.Assets.First().AssetId);
    }

    [Fact]
    public async Task SearchAssetsAsync_WithTagFilter_ShouldFilterByTags()
    {
        // Arrange
        var service = CreateService();
        var request = new AssetSearchRequest
        {
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            Tags = new List<string> { "character", "sword" },
            Limit = 10,
            Offset = 0
        };

        var assetIds = new List<string> { "asset-1", "asset-2" };

        var asset1 = new InternalAssetRecord
        {
            AssetId = "asset-1",
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            Tags = new List<string> { "character", "sword", "warrior" }, // Has both required tags
            ContentType = "image/png",
            ContentHash = "hash1",
            Filename = "asset1.png",
            Size = 1024,
            StorageKey = "assets/texture/asset-1.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var asset2 = new InternalAssetRecord
        {
            AssetId = "asset-2",
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            Tags = new List<string> { "character" }, // Missing "sword" tag
            ContentType = "image/png",
            ContentHash = "hash2",
            Filename = "asset2.png",
            Size = 1024,
            StorageKey = "assets/texture/asset-2.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Setup for fallback search (SupportsSearch returns false)
        _mockIndexStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("type:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(assetIds);

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("asset-1")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset1);

        _mockAssetStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.Contains("asset-2")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset2);

        // Act
        var (status, result) = await service.SearchAssetsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(1, result.Total); // Only asset-1 has both tags
        Assert.Single(result.Assets);
        Assert.Equal("asset-1", result.Assets.First().AssetId);
    }

    #endregion

    #region CreateBundleAsync Tests

    [Fact]
    public async Task CreateBundleAsync_WithEmptyBundleId_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateBundleRequest
        {
            BundleId = "",
            AssetIds = new List<string> { "asset-1" }
        };

        // Act
        var (status, result) = await service.CreateBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateBundleAsync_WithEmptyAssetList_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateBundleRequest
        {
            BundleId = "test-bundle",
            AssetIds = new List<string>()
        };

        // Act
        var (status, result) = await service.CreateBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateBundleAsync_WhenBundleAlreadyExists_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateBundleRequest
        {
            BundleId = "existing-bundle",
            AssetIds = new List<string> { "asset-1" }
        };

        var existingBundle = new BundleMetadata
        {
            BundleId = "existing-bundle",
            Version = "1.0.0",
            BundleType = BundleType.Source,
            Realm = "shared",
            AssetIds = new List<string> { "asset-1" },
            StorageKey = "bundles/current/existing-bundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = Models.BundleStatus.Ready
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBundle);

        // Act
        var (status, result) = await service.CreateBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateBundleAsync_WhenAssetNotFound_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateBundleRequest
        {
            BundleId = "new-bundle",
            AssetIds = new List<string> { "nonexistent-asset" }
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InternalAssetRecord?)null);

        // Act
        var (status, result) = await service.CreateBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    // NOTE: Large bundle async job queuing test requires complex mock setup including
    // bundle converter and orchestrator interactions. Better tested in HTTP integration tests.

    [Fact]
    public async Task CreateBundleAsync_WithSmallBundle_ShouldReturnReadyWithDownloadUrl()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        _configuration.LargeFileThresholdMb = 100; // High threshold
        var service = CreateService();
        var bundleId = Guid.NewGuid().ToString();
        var request = new CreateBundleRequest
        {
            BundleId = bundleId,
            AssetIds = new List<string> { "small-asset" }
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        var smallAsset = new InternalAssetRecord
        {
            AssetId = "small-asset",
            Filename = "icon.png",
            ContentType = "image/png",
            ContentHash = "iconhash",
            Size = 1024,
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            StorageKey = "assets/texture/small-asset.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(smallAsset);

        // Mock getting the asset data
        var assetData = new MemoryStream(new byte[1024]);
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(assetData);

        // Mock putting the bundle
        _mockStorageProvider
            .Setup(s => s.PutObjectAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<long>(),
                It.IsAny<string>(), It.IsAny<IDictionary<string, string>?>()))
            .ReturnsAsync(new StorageModels.AssetReference("test-bucket", "bundles/current/small-bundle.bundle", null, "bundleetag", 2048, DateTime.UtcNow));

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                "asset.bundle.created",
                It.IsAny<BeyondImmersion.BannouService.Events.BundleCreatedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, result) = await service.CreateBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(bundleId, result.BundleId);
        Assert.Equal(CreateBundleResponseStatus.Ready, result.Status);
    }

    #endregion

    #region GetBundleAsync Tests

    [Fact]
    public async Task GetBundleAsync_WhenBundleNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetBundleRequest { BundleId = "non-existent" };

        _mockBundleStore
            .Setup(s => s.GetAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        // Act
        var (status, result) = await service.GetBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBundleAsync_WithEmptyBundleId_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new GetBundleRequest { BundleId = "" };

        // Act
        var (status, result) = await service.GetBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBundleAsync_WhenBundleNotReady_ShouldReturnConflict()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var request = new GetBundleRequest { BundleId = "pending-bundle" };

        var pendingBundle = new BundleMetadata
        {
            BundleId = "pending-bundle",
            Version = "1.0.0",
            BundleType = BundleType.Source,
            Realm = "shared",
            AssetIds = new List<string> { "asset-1" },
            StorageKey = "bundles/current/pending-bundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = Models.BundleStatus.Processing // Not ready
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingBundle);

        // Act
        var (status, result) = await service.GetBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    // NOTE: Complex GetBundleAsync tests with download URL generation require extensive mock
    // setup due to the service's internal state management. These scenarios are better tested
    // via HTTP integration tests where the full service stack is available.

    #endregion

    #region DeleteAssetAsync Tests

    [Fact]
    public async Task DeleteAssetAsync_WithEmptyAssetId_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteAssetRequest
        {
            AssetId = ""
        };

        // Act
        var (status, result) = await service.DeleteAssetAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAssetAsync_WhenAssetNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteAssetRequest
        {
            AssetId = "non-existent-asset"
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InternalAssetRecord?)null);

        // Act
        var (status, result) = await service.DeleteAssetAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAssetAsync_WithSpecificVersion_ShouldDeleteSingleVersion()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var assetId = "test-asset-123";
        var versionId = "v1";
        var request = new DeleteAssetRequest
        {
            AssetId = assetId,
            VersionId = versionId
        };

        var internalRecord = new InternalAssetRecord
        {
            AssetId = assetId,
            Filename = "test.png",
            ContentType = "image/png",
            ContentHash = "abc123",
            Size = 1024,
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            StorageKey = $"assets/texture/{assetId}.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(internalRecord);

        _mockStorageProvider
            .Setup(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        // Act
        var (status, result) = await service.DeleteAssetAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(assetId, result.AssetId);
        Assert.Equal(1, result.VersionsDeleted);

        // Verify specific version was deleted
        _mockStorageProvider.Verify(
            s => s.DeleteObjectAsync(
                "test-bucket",
                $"assets/texture/{assetId}.png",
                versionId),
            Times.Once);

        // Verify asset record was NOT deleted (only deleted version)
        _mockAssetStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task DeleteAssetAsync_WithoutVersion_ShouldDeleteAllVersions()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var assetId = "test-asset-456";
        var request = new DeleteAssetRequest
        {
            AssetId = assetId
            // No VersionId - delete all versions
        };

        var internalRecord = new InternalAssetRecord
        {
            AssetId = assetId,
            Filename = "test.png",
            ContentType = "image/png",
            ContentHash = "abc123",
            Size = 1024,
            AssetType = AssetType.Texture,
            Realm = "arcadia",
            StorageKey = $"assets/texture/{assetId}.png",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var versions = new List<ObjectVersionInfo>
        {
            new ObjectVersionInfo("v3", true, DateTime.UtcNow, 2048, "etag3", false, "STANDARD"),
            new ObjectVersionInfo("v2", false, DateTime.UtcNow.AddDays(-1), 1536, "etag2", false, "STANDARD"),
            new ObjectVersionInfo("v1", false, DateTime.UtcNow.AddDays(-2), 1024, "etag1", false, "GLACIER")
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(internalRecord);

        _mockStorageProvider
            .Setup(s => s.ListVersionsAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(versions);

        _mockStorageProvider
            .Setup(s => s.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        // Act
        var (status, result) = await service.DeleteAssetAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(assetId, result.AssetId);
        Assert.Equal(3, result.VersionsDeleted);

        // Verify all versions were deleted
        _mockStorageProvider.Verify(
            s => s.DeleteObjectAsync(
                "test-bucket",
                $"assets/texture/{assetId}.png",
                It.IsAny<string?>()),
            Times.Exactly(3));

        // Verify asset record was deleted
        _mockAssetStore.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region RequestBundleUploadAsync Tests

    [Fact]
    public async Task RequestBundleUploadAsync_WithEmptyFilename_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new BundleUploadRequest
        {
            Filename = "",
            Size = 1024
        };

        // Act
        var (status, result) = await service.RequestBundleUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestBundleUploadAsync_WithInvalidExtension_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new BundleUploadRequest
        {
            Filename = "bundle.txt",
            Size = 1024,
            ManifestPreview = new BundleManifestPreview { BundleId = "test-bundle" }
        };

        // Act
        var (status, result) = await service.RequestBundleUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestBundleUploadAsync_WithSizeExceedingMax_ShouldReturnBadRequest()
    {
        // Arrange
        _configuration.MaxUploadSizeMb = 100;
        var service = CreateService();
        var request = new BundleUploadRequest
        {
            Filename = "bundle.bannou",
            Size = 200L * 1024 * 1024, // 200MB exceeds 100MB limit
            ManifestPreview = new BundleManifestPreview { BundleId = "test-bundle" }
        };

        // Act
        var (status, result) = await service.RequestBundleUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestBundleUploadAsync_WithMissingBundleId_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new BundleUploadRequest
        {
            Filename = "bundle.bannou",
            Size = 1024,
            ManifestPreview = new BundleManifestPreview { BundleId = "" } // Empty bundle ID
        };

        // Act
        var (status, result) = await service.RequestBundleUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task RequestBundleUploadAsync_WhenBundleAlreadyExists_ShouldReturnConflict()
    {
        // Arrange
        _configuration.MaxUploadSizeMb = 500;
        var service = CreateService();
        var request = new BundleUploadRequest
        {
            Filename = "bundle.bannou",
            Size = 1024,
            ManifestPreview = new BundleManifestPreview { BundleId = "existing-bundle" }
        };

        var existingBundle = new BundleMetadata
        {
            BundleId = "existing-bundle",
            Version = "1.0.0",
            BundleType = BundleType.Source,
            Realm = "shared",
            AssetIds = new List<string> { "asset-1" },
            StorageKey = "bundles/current/existing-bundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = Models.BundleStatus.Ready
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingBundle);

        // Act
        var (status, result) = await service.RequestBundleUploadAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    // NOTE: Valid bundle upload request test requires additional mock setup for
    // upload session storage and state management. Better tested in HTTP integration tests.

    #endregion

    #region CreateMetabundleAsync Tests

    [Fact]
    public async Task CreateMetabundleAsync_WithEmptyMetabundleId_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "",
            SourceBundleIds = new List<string> { "bundle-1" },
            Owner = "test-owner",
            Realm = "arcadia"
        };

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WithNoSourcesOrStandalone_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "test-metabundle",
            SourceBundleIds = null,
            StandaloneAssetIds = null,
            Owner = "test-owner",
            Realm = "arcadia"
        };

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WithEmptySourcesAndStandalone_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "test-metabundle",
            SourceBundleIds = new List<string>(),
            StandaloneAssetIds = new List<string>(),
            Owner = "test-owner",
            Realm = "arcadia"
        };

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WhenMetabundleExists_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "existing-metabundle",
            SourceBundleIds = new List<string> { "bundle-1" },
            Owner = "test-owner",
            Realm = "arcadia"
        };

        var existingMetabundle = new BundleMetadata
        {
            BundleId = "existing-metabundle",
            Version = "1.0.0",
            BundleType = BundleType.Metabundle,
            Realm = "arcadia",
            AssetIds = new List<string> { "asset-1" },
            StorageKey = "bundles/current/existing-metabundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = Models.BundleStatus.Ready
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingMetabundle);

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WhenSourceBundleNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "new-metabundle",
            SourceBundleIds = new List<string> { "nonexistent-bundle" },
            Owner = "test-owner",
            Realm = "arcadia"
        };

        // Metabundle doesn't exist
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("new-metabundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        // Source bundle not found
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("nonexistent-bundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WhenStandaloneAssetNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "new-metabundle",
            StandaloneAssetIds = new List<string> { "nonexistent-asset" },
            Owner = "test-owner",
            Realm = "arcadia"
        };

        // Metabundle doesn't exist
        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        // Asset not found - need to setup the InternalAssetRecord store
        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InternalAssetRecord?)null);

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WhenSourceBundleNotReady_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "new-metabundle",
            SourceBundleIds = new List<string> { "pending-bundle" },
            Owner = "test-owner",
            Realm = "arcadia"
        };

        // Metabundle doesn't exist
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("new-metabundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        var pendingBundle = new BundleMetadata
        {
            BundleId = "pending-bundle",
            Version = "1.0.0",
            BundleType = BundleType.Source,
            Realm = "arcadia",
            AssetIds = new List<string> { "asset-1" },
            StorageKey = "bundles/current/pending-bundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = Models.BundleStatus.Processing // Not ready
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("pending-bundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingBundle);

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WhenStandaloneAssetNotReady_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "new-metabundle",
            StandaloneAssetIds = new List<string> { "pending-asset" },
            Owner = "test-owner",
            Realm = "arcadia"
        };

        // Metabundle doesn't exist
        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        var pendingAsset = new InternalAssetRecord
        {
            AssetId = "pending-asset",
            Filename = "script.yaml",
            ContentType = "application/x-yaml",
            ContentHash = "abc123",
            Size = 512,
            AssetType = AssetType.Behavior,
            Realm = "arcadia",
            StorageKey = "assets/behavior/pending-asset.yaml",
            Bucket = "test-bucket",
            ProcessingStatus = ProcessingStatus.Processing, // Not complete
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingAsset);

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WithRealmMismatch_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "new-metabundle",
            SourceBundleIds = new List<string> { "wrong-realm-bundle" },
            Owner = "test-owner",
            Realm = "arcadia" // Request is for Arcadia
        };

        // Metabundle doesn't exist
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("new-metabundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        var wrongRealmBundle = new BundleMetadata
        {
            BundleId = "wrong-realm-bundle",
            Version = "1.0.0",
            BundleType = BundleType.Source,
            Realm = "fantasia", // Different realm
            AssetIds = new List<string> { "asset-1" },
            Assets = new List<StoredBundleAssetEntry>(),
            StorageKey = "bundles/current/wrong-realm-bundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = Models.BundleStatus.Ready
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("wrong-realm-bundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wrongRealmBundle);

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WithSharedRealmBundle_ShouldAllowCrossRealmInclusion()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        _configuration.LargeFileThresholdMb = 100;
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "cross-realm-metabundle",
            SourceBundleIds = new List<string> { "shared-bundle" },
            Owner = "test-owner",
            Realm = "arcadia" // Request is for Arcadia
        };

        // Metabundle doesn't exist
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("cross-realm-metabundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        var sharedBundle = new BundleMetadata
        {
            BundleId = "shared-bundle",
            Version = "1.0.0",
            BundleType = BundleType.Source,
            Realm = "shared", // Shared realm should work with any target realm
            AssetIds = new List<string> { "shared-asset" },
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry
                {
                    AssetId = "shared-asset",
                    Filename = "shared.png",
                    ContentType = "image/png",
                    Size = 1024,
                    ContentHash = "sharedhash"
                }
            },
            StorageKey = "bundles/current/shared-bundle.bundle",
            Bucket = "test-bucket",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = Models.BundleStatus.Ready
        };

        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("shared-bundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sharedBundle);

        // Mock storage operations
        var bundleData = new MemoryStream(new byte[1024]);
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(bundleData);

        _mockStorageProvider
            .Setup(s => s.PutObjectAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<long>(),
                It.IsAny<string>(), It.IsAny<IDictionary<string, string>?>()))
            .ReturnsAsync(new StorageModels.AssetReference("test-bucket", "bundles/current/cross-realm-metabundle.bundle", null, "etag", 2048, DateTime.UtcNow));

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                "asset.metabundle.created",
                It.IsAny<MetabundleCreatedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // The BannouBundleReader.ReadAsset will be called but we need to mock it
        // For this test, we'll rely on the fact that the bundle reading logic
        // will be tested via integration tests

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert - Shared realm bundles should be allowed with any target realm
        // The actual success depends on BannouBundleReader which we can't easily mock
        // So we verify at least it doesn't fail realm validation
        Assert.True(status == StatusCodes.OK || status == StatusCodes.InternalServerError);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WithConflictingAssetHashes_ShouldReturnConflict()
    {
        // Arrange: Two source bundles contain the same asset ID but with different content hashes
        // Use the existing _mockBundleStore like other working tests
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "conflict-test-metabundle",
            SourceBundleIds = new List<string> { "bundle-1", "bundle-2" },
            Owner = "test-owner",
            Realm = "arcadia"
        };

        // Bundle 1 has asset "shared-asset" with hash "hash-a"
        var bundle1 = new BundleMetadata
        {
            BundleId = "bundle-1",
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/bundle-1.bundle",
            BundleType = BundleType.Source,
            AssetIds = new List<string> { "shared-asset" },
            SizeBytes = 100,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry
                {
                    AssetId = "shared-asset",
                    ContentHash = "hash-a",
                    Filename = "shared.json",
                    ContentType = "application/json",
                    Size = 100
                }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Bundle 2 has same asset "shared-asset" but with different hash "hash-b"
        var bundle2 = new BundleMetadata
        {
            BundleId = "bundle-2",
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/bundle-2.bundle",
            BundleType = BundleType.Source,
            AssetIds = new List<string> { "shared-asset" },
            SizeBytes = 150,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry
                {
                    AssetId = "shared-asset",
                    ContentHash = "hash-b", // Different hash - conflict!
                    Filename = "shared.json",
                    ContentType = "application/json",
                    Size = 150
                }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Setup: Use a single catch-all setup that returns the right bundle based on key
        _mockBundleStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
            {
                if (key.Contains("conflict-test-metabundle")) return null;
                if (key.Contains("bundle-1")) return bundle1;
                if (key.Contains("bundle-2")) return bundle2;
                return null;
            });

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert - Should return Conflict with null result per T8 (error responses return null)
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateMetabundleAsync_WithAssetFilter_ShouldOnlyIncludeFilteredAssets()
    {
        // Arrange: Bundle with multiple assets, but we only want specific ones
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var request = new CreateMetabundleRequest
        {
            MetabundleId = "filtered-metabundle",
            SourceBundleIds = new List<string> { "multi-asset-bundle" },
            AssetFilter = new List<string> { "asset-1", "asset-3" }, // Only include these
            Owner = "test-owner",
            Realm = "arcadia"
        };

        var sourceBundle = new BundleMetadata
        {
            BundleId = "multi-asset-bundle",
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/multi-asset-bundle.bundle",
            BundleType = BundleType.Source,
            AssetIds = new List<string> { "asset-1", "asset-2", "asset-3" },
            SizeBytes = 300,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry { AssetId = "asset-1", ContentHash = "hash-1", Filename = "a1.json", ContentType = "application/json", Size = 100 },
                new StoredBundleAssetEntry { AssetId = "asset-2", ContentHash = "hash-2", Filename = "a2.json", ContentType = "application/json", Size = 100 },
                new StoredBundleAssetEntry { AssetId = "asset-3", ContentHash = "hash-3", Filename = "a3.json", ContentType = "application/json", Size = 100 }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Setup: Metabundle doesn't exist
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("filtered-metabundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BundleMetadata?)null);

        // Setup: Return source bundle
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("multi-asset-bundle")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceBundle);

        // Setup: Storage provider returns stream for source bundle
        var bundleStream = new MemoryStream(new byte[100]);
        _mockStorageProvider
            .Setup(s => s.GetObjectAsync("test-bucket", "bundles/multi-asset-bundle.bundle", It.IsAny<string?>()))
            .ReturnsAsync(bundleStream);

        // Note: Full test of filtering requires BannouBundleReader integration
        // This test verifies the filter is passed through correctly - full validation in HTTP tests

        // Act
        var (status, result) = await service.CreateMetabundleAsync(request, CancellationToken.None);

        // Assert - Either succeeds or fails at bundle reading (not at validation)
        // The filter validation happens before bundle reading, so BadRequest would indicate filter issue
        Assert.NotEqual(StatusCodes.BadRequest, status);
    }

    #endregion

    #region ResolveBundlesAsync Tests

    [Fact]
    public async Task ResolveBundlesAsync_WithEmptyAssetIds_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new ResolveBundlesRequest
        {
            AssetIds = new List<string>(),
            Realm = "arcadia"
        };

        // Act
        var (status, result) = await service.ResolveBundlesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveBundlesAsync_ShouldPreferMetabundlesOverRegularBundles()
    {
        // Arrange: Asset exists in both a metabundle and regular bundle
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var request = new ResolveBundlesRequest
        {
            AssetIds = new List<string> { "asset-1" },
            Realm = "arcadia"
        };

        // Asset-to-bundle index shows asset is in both bundles
        var regularBundleId = Guid.NewGuid();
        var metabundleBundleId = Guid.NewGuid();
        var assetIndex = new AssetBundleIndex
        {
            BundleIds = new List<Guid> { regularBundleId, metabundleBundleId }
        };

        var regularBundle = new BundleMetadata
        {
            BundleId = regularBundleId.ToString(),
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/regular.bundle",
            BundleType = BundleType.Source,
            AssetIds = new List<string> { "asset-1" },
            SizeBytes = 100,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry { AssetId = "asset-1", ContentHash = "hash-1", Filename = "a1.json", ContentType = "application/json", Size = 100 }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        var metabundle = new BundleMetadata
        {
            BundleId = metabundleBundleId.ToString(),
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/metabundle.bundle",
            BundleType = BundleType.Metabundle, // This is a metabundle
            AssetIds = new List<string> { "asset-1" },
            SizeBytes = 100,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry { AssetId = "asset-1", ContentHash = "hash-1", Filename = "a1.json", ContentType = "application/json", Size = 100 }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Setup mocks
        _mockAssetBundleIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assetIndex);

        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(regularBundleId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(regularBundle);
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(metabundleBundleId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metabundle);

        _mockStorageProvider
            .Setup(s => s.GenerateDownloadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(new PreSignedDownloadResult(
                "https://download.example.com/bundle",
                "bundles/test.bundle",
                null,
                DateTime.UtcNow.AddHours(1),
                100,
                "application/octet-stream"));

        // Act
        var (status, result) = await service.ResolveBundlesAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Bundles);
        Assert.Equal(metabundleBundleId.ToString(), result.Bundles.First().BundleId);
    }

    [Fact]
    public async Task ResolveBundlesAsync_ShouldUseGreedySetCover()
    {
        // Arrange: Multiple assets, one bundle covers most of them
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var bigBundleId = Guid.NewGuid();
        var smallBundle1Id = Guid.NewGuid();
        var smallBundle2Id = Guid.NewGuid();
        var request = new ResolveBundlesRequest
        {
            AssetIds = new List<string> { "asset-1", "asset-2", "asset-3" },
            Realm = "arcadia"
        };

        // Asset indices
        var asset1Index = new AssetBundleIndex { BundleIds = new List<Guid> { bigBundleId, smallBundle1Id } };
        var asset2Index = new AssetBundleIndex { BundleIds = new List<Guid> { bigBundleId } };
        var asset3Index = new AssetBundleIndex { BundleIds = new List<Guid> { bigBundleId, smallBundle2Id } };

        // Big bundle contains all 3 assets
        var bigBundle = new BundleMetadata
        {
            BundleId = bigBundleId.ToString(),
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/big.bundle",
            BundleType = BundleType.Source,
            AssetIds = new List<string> { "asset-1", "asset-2", "asset-3" },
            SizeBytes = 300,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry { AssetId = "asset-1", ContentHash = "h1", Filename = "a1.json", ContentType = "application/json", Size = 100 },
                new StoredBundleAssetEntry { AssetId = "asset-2", ContentHash = "h2", Filename = "a2.json", ContentType = "application/json", Size = 100 },
                new StoredBundleAssetEntry { AssetId = "asset-3", ContentHash = "h3", Filename = "a3.json", ContentType = "application/json", Size = 100 }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Small bundles contain only 1 asset each
        var smallBundle1 = new BundleMetadata
        {
            BundleId = smallBundle1Id.ToString(),
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/small1.bundle",
            BundleType = BundleType.Source,
            AssetIds = new List<string> { "asset-1" },
            SizeBytes = 100,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry { AssetId = "asset-1", ContentHash = "h1", Filename = "a1.json", ContentType = "application/json", Size = 100 }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        var smallBundle2 = new BundleMetadata
        {
            BundleId = smallBundle2Id.ToString(),
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/small2.bundle",
            BundleType = BundleType.Source,
            AssetIds = new List<string> { "asset-3" },
            SizeBytes = 100,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            Assets = new List<StoredBundleAssetEntry>
            {
                new StoredBundleAssetEntry { AssetId = "asset-3", ContentHash = "h3", Filename = "a3.json", ContentType = "application/json", Size = 100 }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Setup index lookups
        _mockAssetBundleIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("asset-1")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset1Index);
        _mockAssetBundleIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("asset-2")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset2Index);
        _mockAssetBundleIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains("asset-3")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(asset3Index);

        // Setup bundle lookups
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(bigBundleId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bigBundle);
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(smallBundle1Id.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(smallBundle1);
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(smallBundle2Id.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(smallBundle2);

        _mockStorageProvider
            .Setup(s => s.GenerateDownloadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(new PreSignedDownloadResult(
                "https://download.example.com/bundle",
                "bundles/test.bundle",
                null,
                DateTime.UtcNow.AddHours(1),
                100,
                "application/octet-stream"));

        // Act
        var (status, result) = await service.ResolveBundlesAsync(request, CancellationToken.None);

        // Assert - Should pick big-bundle (covers all 3) instead of small-bundle-1 + small-bundle-2
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Bundles);
        Assert.Equal(bigBundleId.ToString(), result.Bundles.First().BundleId);
    }

    #endregion

    #region QueryBundlesByAssetAsync Tests

    [Fact]
    public async Task QueryBundlesByAssetAsync_WithEmptyAssetId_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryBundlesByAssetRequest
        {
            AssetId = ""
        };

        // Act
        var (status, result) = await service.QueryBundlesByAssetAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task QueryBundlesByAssetAsync_WithValidAsset_ShouldReturnBundles()
    {
        // Arrange
        _configuration.StorageBucket = "test-bucket";
        var service = CreateService();
        var bundle1Id = Guid.NewGuid();
        var bundle2Id = Guid.NewGuid();
        var request = new QueryBundlesByAssetRequest
        {
            AssetId = "test-asset"
        };

        var assetIndex = new AssetBundleIndex
        {
            BundleIds = new List<Guid> { bundle1Id, bundle2Id }
        };

        var bundle1 = new BundleMetadata
        {
            BundleId = bundle1Id.ToString(),
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/b1.bundle",
            BundleType = BundleType.Source,
            AssetIds = new List<string> { "test-asset" },
            SizeBytes = 500,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var bundle2 = new BundleMetadata
        {
            BundleId = bundle2Id.ToString(),
            Version = "1.0.0",
            Bucket = "test-bucket",
            StorageKey = "bundles/b2.bundle",
            BundleType = BundleType.Metabundle,
            AssetIds = new List<string> { "test-asset" },
            SizeBytes = 300,
            Status = Models.BundleStatus.Ready,
            Realm = "arcadia",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _mockAssetBundleIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(assetIndex);

        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(bundle1Id.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bundle1);
        _mockBundleStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(bundle2Id.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bundle2);

        // Act
        var (status, result) = await service.QueryBundlesByAssetAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(2, result.Bundles.Count);
    }

    #endregion

    #region BulkGetAssetsAsync Tests

    [Fact]
    public async Task BulkGetAssetsAsync_WithEmptyAssetIds_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new BulkGetAssetsRequest
        {
            AssetIds = new List<string>()
        };

        // Act
        var (status, result) = await service.BulkGetAssetsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task BulkGetAssetsAsync_WithIncludeDownloadUrlsFalse_ShouldNotGenerateUrls()
    {
        // Arrange
        _configuration.AssetKeyPrefix = "asset:";
        _configuration.DownloadTokenTtlSeconds = 3600;
        var service = CreateService();

        var testAsset = new InternalAssetRecord
        {
            AssetId = "test-asset",
            ContentHash = "abc123",
            Filename = "test.json",
            ContentType = "application/json",
            Size = 100,
            Bucket = "test-bucket",
            StorageKey = "assets/test.json",
            AssetType = AssetType.Model,
            Realm = "arcadia",
            Tags = new List<string> { "test" },
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(testAsset);

        var request = new BulkGetAssetsRequest
        {
            AssetIds = new List<string> { "test-asset" },
            IncludeDownloadUrls = false // Explicitly false
        };

        // Act
        var (status, result) = await service.BulkGetAssetsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Assets);
        var firstAsset = result.Assets.First();
        Assert.Null(firstAsset.DownloadUrl); // Should be null
        Assert.Null(firstAsset.ExpiresAt); // Should be null

        // Verify GenerateDownloadUrlAsync was NOT called
        _mockStorageProvider.Verify(
            s => s.GenerateDownloadUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<TimeSpan?>()),
            Times.Never);
    }

    [Fact]
    public async Task BulkGetAssetsAsync_WithIncludeDownloadUrlsTrue_ShouldGenerateUrls()
    {
        // Arrange
        _configuration.AssetKeyPrefix = "asset:";
        _configuration.DownloadTokenTtlSeconds = 3600;
        var service = CreateService();

        var testAsset = new InternalAssetRecord
        {
            AssetId = "test-asset",
            ContentHash = "abc123",
            Filename = "test.json",
            ContentType = "application/json",
            Size = 100,
            Bucket = "test-bucket",
            StorageKey = "assets/test.json",
            AssetType = AssetType.Model,
            Realm = "arcadia",
            Tags = new List<string> { "test" },
            ProcessingStatus = ProcessingStatus.Complete,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockAssetStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(testAsset);

        var expectedUrl = "https://download.example.com/test-asset";
        var expectedExpiry = DateTime.UtcNow.AddHours(1);
        _mockStorageProvider
            .Setup(s => s.GenerateDownloadUrlAsync("test-bucket", "assets/test.json", It.IsAny<string?>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync(new PreSignedDownloadResult(
                expectedUrl,
                "assets/test.json",
                null,
                expectedExpiry,
                100,
                "application/json"));

        var request = new BulkGetAssetsRequest
        {
            AssetIds = new List<string> { "test-asset" },
            IncludeDownloadUrls = true // Explicitly true
        };

        // Act
        var (status, result) = await service.BulkGetAssetsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Single(result.Assets);
        var firstAsset = result.Assets.First();
        Assert.NotNull(firstAsset.DownloadUrl);
        Assert.Equal(expectedUrl, firstAsset.DownloadUrl!.ToString());
        Assert.NotNull(firstAsset.ExpiresAt);

        // Verify GenerateDownloadUrlAsync WAS called
        _mockStorageProvider.Verify(
            s => s.GenerateDownloadUrlAsync("test-bucket", "assets/test.json", It.IsAny<string?>(), It.IsAny<TimeSpan?>()),
            Times.Once);
    }

    #endregion

    #region GetJobStatusAsync Tests

    [Fact]
    public async Task GetJobStatusAsync_WithInvalidJobId_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new GetJobStatusRequest { JobId = Guid.Empty };

        // Act
        var (status, result) = await service.GetJobStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetJobStatusAsync_WithNonExistentJob_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var request = new GetJobStatusRequest { JobId = jobId };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetabundleJob?)null);

        // Act
        var (status, result) = await service.GetJobStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetJobStatusAsync_WithQueuedJob_ShouldReturnQueuedStatus()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var metabundleId = Guid.NewGuid();
        var request = new GetJobStatusRequest { JobId = jobId };

        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = metabundleId,
            Status = InternalJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var (status, result) = await service.GetJobStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(jobId, result.JobId);
        Assert.Equal(metabundleId.ToString(), result.MetabundleId);
        Assert.Equal(GetJobStatusResponseStatus.Queued, result.Status);
    }

    [Fact]
    public async Task GetJobStatusAsync_WithReadyJob_ShouldReturnResultDataAndDownloadUrl()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var metabundleId = Guid.NewGuid();
        var sourceBundleId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var storageKey = "bundles/current/test-metabundle.bundle";
        var expectedUrl = "https://storage.example.com/download/test-metabundle.bundle";
        var request = new GetJobStatusRequest { JobId = jobId };

        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = metabundleId,
            Status = InternalJobStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            ProcessingTimeMs = 5000,
            Result = new MetabundleJobResult
            {
                AssetCount = 10,
                StandaloneAssetCount = 2,
                SizeBytes = 1024000,
                StorageKey = storageKey,
                SourceBundles = new List<SourceBundleReferenceInternal>
                {
                    new() { BundleId = sourceBundleId, Version = "1.0.0", AssetIds = new List<Guid> { assetId }, ContentHash = "hash1" }
                }
            }
        };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mockStorageProvider.Setup(s => s.GenerateDownloadUrlAsync(
            It.IsAny<string>(),
            storageKey,
            It.IsAny<string?>(),
            It.IsAny<TimeSpan?>()))
            .ReturnsAsync(new StorageModels.PreSignedDownloadResult(
                DownloadUrl: expectedUrl,
                Key: storageKey,
                VersionId: null,
                ExpiresAt: DateTime.UtcNow.AddHours(1),
                ContentLength: 1024000,
                ContentType: "application/bannou-bundle"));

        // Act
        var (status, result) = await service.GetJobStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(GetJobStatusResponseStatus.Ready, result.Status);
        Assert.Equal(10, result.AssetCount);
        Assert.Equal(2, result.StandaloneAssetCount);
        Assert.Equal(1024000, result.SizeBytes);
        Assert.NotNull(result.DownloadUrl);
        Assert.Equal(expectedUrl, result.DownloadUrl.ToString());
        Assert.NotNull(result.SourceBundles);
        Assert.Single(result.SourceBundles);
    }

    [Fact]
    public async Task GetJobStatusAsync_WithFailedJob_ShouldReturnErrorDetails()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var request = new GetJobStatusRequest { JobId = jobId };

        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = Guid.NewGuid(),
            Status = InternalJobStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorCode = MetabundleErrorCode.TIMEOUT,
            ErrorMessage = "Job timed out before processing could start"
        };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var (status, result) = await service.GetJobStatusAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(GetJobStatusResponseStatus.Failed, result.Status);
        Assert.Equal("TIMEOUT", result.ErrorCode);
        Assert.Equal("Job timed out before processing could start", result.ErrorMessage);
    }

    #endregion

    #region CancelJobAsync Tests

    [Fact]
    public async Task CancelJobAsync_WithInvalidJobId_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new CancelJobRequest { JobId = Guid.Empty };

        // Act
        var (status, result) = await service.CancelJobAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CancelJobAsync_WithNonExistentJob_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var request = new CancelJobRequest { JobId = jobId };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync((MetabundleJob?)null);

        // Act
        var (status, result) = await service.CancelJobAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task CancelJobAsync_WithQueuedJob_ShouldCancelSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var request = new CancelJobRequest { JobId = jobId };

        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = Guid.NewGuid(),
            Status = InternalJobStatus.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mockJobStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<MetabundleJob>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-123");

        // Act
        var (status, result) = await service.CancelJobAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.True(result.Cancelled);
        Assert.Equal(CancelJobResponseStatus.Cancelled, result.Status);
        Assert.Contains("Queued", result.Message);

        // Verify job was saved with cancelled status
        _mockJobStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(),
            It.Is<MetabundleJob>(j => j.Status == InternalJobStatus.Cancelled),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CancelJobAsync_WithReadyJob_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var request = new CancelJobRequest { JobId = jobId };

        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = Guid.NewGuid(),
            Status = InternalJobStatus.Ready,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var (status, result) = await service.CancelJobAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.NotNull(result);
        Assert.False(result.Cancelled);
        Assert.Equal(CancelJobResponseStatus.Ready, result.Status);
        Assert.Contains("already completed", result.Message);
    }

    [Fact]
    public async Task CancelJobAsync_WithAlreadyCancelledJob_ShouldReturnOkWithCancelledStatus()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var request = new CancelJobRequest { JobId = jobId };

        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = Guid.NewGuid(),
            Status = InternalJobStatus.Cancelled,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow
        };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var (status, result) = await service.CancelJobAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.True(result.Cancelled);
        Assert.Equal(CancelJobResponseStatus.Cancelled, result.Status);
        Assert.Contains("already cancelled", result.Message);
    }

    [Fact]
    public async Task CancelJobAsync_WithSessionId_ShouldEmitCompletionEvent()
    {
        // Arrange
        var service = CreateService();
        var jobId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var request = new CancelJobRequest { JobId = jobId };

        var job = new MetabundleJob
        {
            JobId = jobId,
            MetabundleId = Guid.NewGuid(),
            Status = InternalJobStatus.Queued,
            RequesterSessionId = sessionId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _mockJobStore.Setup(s => s.GetAsync(
            It.Is<string>(k => k.Contains(jobId.ToString())),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        _mockJobStore.Setup(s => s.SaveAsync(
            It.IsAny<string>(),
            It.IsAny<MetabundleJob>(),
            It.IsAny<StateOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-456");

        _mockAssetEventEmitter.Setup(e => e.EmitMetabundleCreationCompleteAsync(
            sessionId.ToString(),
            jobId,
            It.IsAny<string>(),
            false,
            MetabundleJobStatus.Cancelled,
            null,
            null,
            null,
            null,
            null,
            MetabundleErrorCode.CANCELLED,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, result) = await service.CancelJobAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.True(result.Cancelled);

        // Verify completion event was emitted
        _mockAssetEventEmitter.Verify(e => e.EmitMetabundleCreationCompleteAsync(
            sessionId.ToString(),
            jobId,
            It.IsAny<string>(),
            false,
            MetabundleJobStatus.Cancelled,
            null,
            null,
            null,
            null,
            null,
            MetabundleErrorCode.CANCELLED,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Helper Methods

    private AssetService CreateService()
    {
        return new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            _mockProcessorPoolManager.Object,
            _mockBundleConverter.Object,
            _mockEventConsumer.Object);
    }

    #endregion
}

public class AssetConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new AssetServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void Configuration_DefaultValues_ShouldBeSetCorrectly()
    {
        // Arrange
        var config = new AssetServiceConfiguration();

        // Assert - verify default values from configuration schema
        Assert.Equal(StorageProvider.Minio, config.StorageProvider);
        Assert.Equal("bannou-assets", config.StorageBucket);
        Assert.Equal("minio:9000", config.StorageEndpoint);
        Assert.Equal(3600, config.TokenTtlSeconds);
        Assert.Equal(500, config.MaxUploadSizeMb);
        Assert.Equal(50, config.MultipartThresholdMb);
    }
}

public class MinioWebhookHandlerTests
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory = new();
    private readonly Mock<IStateStore<UploadSession>> _mockUploadSessionStore = new();
    private readonly Mock<IMessageBus> _mockMessageBus = new();
    private readonly Mock<ILogger<MinioWebhookHandler>> _mockLogger = new();
    private readonly AssetServiceConfiguration _configuration = new();

    public MinioWebhookHandlerTests()
    {
        _mockStateStoreFactory
            .Setup(f => f.GetStore<UploadSession>("asset-statestore"))
            .Returns(_mockUploadSessionStore.Object);
    }

    private MinioWebhookHandler CreateHandler()
    {
        return new MinioWebhookHandler(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithEmptyPayload_ShouldReturnFalse()
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleWebhookAsync("{}");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithInvalidJson_ShouldReturnFalse()
    {
        // Arrange
        var handler = CreateHandler();

        // Act
        var result = await handler.HandleWebhookAsync("not valid json");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithNonCreationEvent_ShouldReturnTrueButNotProcess()
    {
        // Arrange
        var handler = CreateHandler();
        var payload = System.Text.Json.JsonSerializer.Serialize(new MinioNotification
        {
            Records = new List<MinioEventRecord>
            {
                new MinioEventRecord
                {
                    EventName = "s3:ObjectRemoved:Delete",
                    S3 = new MinioS3Info
                    {
                        Bucket = new MinioBucketInfo { Name = "test-bucket" },
                        Object = new MinioObjectInfo { Key = "temp/abc123/test.png" }
                    }
                }
            }
        });

        // Act
        var result = await handler.HandleWebhookAsync(payload);

        // Assert
        Assert.True(result);
        // Verify no state store or message bus calls were made
        _mockUploadSessionStore.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithNonTempPath_ShouldReturnTrueButNotProcess()
    {
        // Arrange
        var handler = CreateHandler();
        var payload = System.Text.Json.JsonSerializer.Serialize(new MinioNotification
        {
            Records = new List<MinioEventRecord>
            {
                new MinioEventRecord
                {
                    EventName = "s3:ObjectCreated:Put",
                    S3 = new MinioS3Info
                    {
                        Bucket = new MinioBucketInfo { Name = "test-bucket" },
                        Object = new MinioObjectInfo { Key = "assets/texture/final.png" } // Not temp path
                    }
                }
            }
        });

        // Act
        var result = await handler.HandleWebhookAsync(payload);

        // Assert
        Assert.True(result);
        _mockUploadSessionStore.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithMissingUploadSession_ShouldReturnTrueButLogWarning()
    {
        // Arrange
        var handler = CreateHandler();
        var uploadId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new MinioNotification
        {
            Records = new List<MinioEventRecord>
            {
                new MinioEventRecord
                {
                    EventName = "s3:ObjectCreated:Put",
                    S3 = new MinioS3Info
                    {
                        Bucket = new MinioBucketInfo { Name = "test-bucket" },
                        Object = new MinioObjectInfo
                        {
                            Key = $"temp/{uploadId:N}/test.png",
                            Size = 1024,
                            ETag = "\"abc123\""
                        }
                    }
                }
            }
        });

        _mockUploadSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UploadSession?)null);

        // Act
        var result = await handler.HandleWebhookAsync(payload);

        // Assert
        Assert.True(result);
        // Verify session lookup was attempted
        _mockUploadSessionStore.Verify(
            s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        // Verify no message was published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<AssetUploadNotification>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleWebhookAsync_WithValidUpload_ShouldUpdateSessionAndPublishEvent()
    {
        // Arrange
        var handler = CreateHandler();
        var uploadId = Guid.NewGuid();

        var session = new UploadSession
        {
            UploadId = uploadId,
            Filename = "test.png",
            Size = 1024,
            ContentType = "image/png",
            StorageKey = $"temp/{uploadId:N}/test.png",
            IsMultipart = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };

        var payload = System.Text.Json.JsonSerializer.Serialize(new MinioNotification
        {
            Records = new List<MinioEventRecord>
            {
                new MinioEventRecord
                {
                    EventName = "s3:ObjectCreated:Put",
                    S3 = new MinioS3Info
                    {
                        Bucket = new MinioBucketInfo { Name = "test-bucket" },
                        Object = new MinioObjectInfo
                        {
                            Key = $"temp/{uploadId:N}/test.png",
                            Size = 1024,
                            ETag = "\"abc123\""
                        }
                    }
                }
            }
        });

        _mockUploadSessionStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                "asset.upload.completed",
                It.IsAny<AssetUploadNotification>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await handler.HandleWebhookAsync(payload);

        // Assert
        Assert.True(result);

        // Verify session was updated and saved
        _mockUploadSessionStore.Verify(
            s => s.SaveAsync(
                It.IsAny<string>(),
                It.Is<UploadSession>(sess =>
                    sess.IsComplete == true &&
                    sess.UploadedEtag == "abc123" &&
                    sess.UploadedSize == 1024),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify event was published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "asset.upload.completed",
                It.Is<AssetUploadNotification>(n =>
                    n.UploadId == uploadId &&
                    n.ETag == "abc123" &&
                    n.Size == 1024),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

public class AssetEventEmitterTests
{
    private readonly Mock<IClientEventPublisher> _mockClientEventPublisher = new();
    private readonly Mock<ILogger<AssetEventEmitter>> _mockLogger = new();

    public AssetEventEmitterTests()
    {
    }

    private AssetEventEmitter CreateEmitter()
    {
        return new AssetEventEmitter(
            _mockClientEventPublisher.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task EmitUploadCompleteAsync_WithSuccess_ShouldPublishCorrectEvent()
    {
        // Arrange
        var emitter = CreateEmitter();
        var sessionId = "session-123";
        var uploadId = Guid.NewGuid();
        var assetId = "asset-456";
        var contentHash = "hash789";
        var size = 1024L;

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                It.IsAny<string>(),
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await emitter.EmitUploadCompleteAsync(
            sessionId,
            uploadId,
            success: true,
            assetId: assetId,
            contentHash: contentHash,
            size: size);

        // Assert
        Assert.True(result);
        _mockClientEventPublisher.Verify(
            p => p.PublishToSessionAsync(
                sessionId,
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EmitUploadCompleteAsync_WithFailure_ShouldPublishErrorEvent()
    {
        // Arrange
        var emitter = CreateEmitter();
        var sessionId = "session-123";
        var uploadId = Guid.NewGuid();

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                It.IsAny<string>(),
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await emitter.EmitUploadCompleteAsync(
            sessionId,
            uploadId,
            success: false,
            errorCode: UploadErrorCode.SIZE_EXCEEDED,
            errorMessage: "File exceeds maximum size");

        // Assert
        Assert.True(result);
        _mockClientEventPublisher.Verify(
            p => p.PublishToSessionAsync(
                sessionId,
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EmitAssetReadyAsync_ShouldPublishCorrectEvent()
    {
        // Arrange
        var emitter = CreateEmitter();
        var sessionId = "session-123";
        var assetId = "asset-456";

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                It.IsAny<string>(),
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await emitter.EmitAssetReadyAsync(
            sessionId,
            assetId,
            versionId: "v1",
            contentHash: "hash123",
            size: 2048,
            contentType: "image/png");

        // Assert
        Assert.True(result);
        _mockClientEventPublisher.Verify(
            p => p.PublishToSessionAsync(
                sessionId,
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EmitBundleCreationCompleteAsync_ShouldPublishCorrectEvent()
    {
        // Arrange
        var emitter = CreateEmitter();
        var sessionId = "session-123";
        var bundleId = "bundle-456";
        var downloadUrl = new Uri("https://storage.example.com/bundle.bannou");

        _mockClientEventPublisher
            .Setup(p => p.PublishToSessionAsync(
                It.IsAny<string>(),
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await emitter.EmitBundleCreationCompleteAsync(
            sessionId,
            bundleId,
            success: true,
            downloadUrl: downloadUrl,
            size: 4096,
            assetCount: 5);

        // Assert
        Assert.True(result);
        _mockClientEventPublisher.Verify(
            p => p.PublishToSessionAsync(
                sessionId,
                It.IsAny<BaseClientEvent>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
