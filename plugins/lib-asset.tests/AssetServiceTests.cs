using BeyondImmersion.Bannou.Asset.ClientEvents;
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

    private Mock<IStateStoreFactory> _mockStateStoreFactory = null!;
    private Mock<IStateStore<InternalAssetRecord>> _mockAssetStore = null!;
    private Mock<IStateStore<List<string>>> _mockIndexStore = null!;
    private Mock<IStateStore<BundleMetadata>> _mockBundleStore = null!;
    private Mock<IStateStore<UploadSession>> _mockUploadSessionStore = null!;
    private Mock<IMessageBus> _mockMessageBus = null!;
    private Mock<ILogger<AssetService>> _mockLogger = null!;
    private AssetServiceConfiguration _configuration = null!;
    private Mock<IAssetEventEmitter> _mockAssetEventEmitter = null!;
    private Mock<IAssetStorageProvider> _mockStorageProvider = null!;
    private Mock<IOrchestratorClient> _mockOrchestratorClient = null!;
    private Mock<IAssetProcessorPoolManager> _mockProcessorPoolManager = null!;
    private Mock<IBundleConverter> _mockBundleConverter = null!;
    private Mock<IEventConsumer> _mockEventConsumer = null!;

    public AssetServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockAssetStore = new Mock<IStateStore<InternalAssetRecord>>();
        _mockIndexStore = new Mock<IStateStore<List<string>>>();
        _mockBundleStore = new Mock<IStateStore<BundleMetadata>>();
        _mockUploadSessionStore = new Mock<IStateStore<UploadSession>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<AssetService>>();
        _configuration = new AssetServiceConfiguration();
        _mockAssetEventEmitter = new Mock<IAssetEventEmitter>();
        _mockStorageProvider = new Mock<IAssetStorageProvider>();
        _mockOrchestratorClient = new Mock<IOrchestratorClient>();
        _mockProcessorPoolManager = new Mock<IAssetProcessorPoolManager>();
        _mockBundleConverter = new Mock<IBundleConverter>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<InternalAssetRecord>(STATE_STORE)).Returns(_mockAssetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BundleMetadata>(STATE_STORE)).Returns(_mockBundleStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<UploadSession>(STATE_STORE)).Returns(_mockUploadSessionStore.Object);
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
                Realm = Realm.Arcadia,
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
                Realm = Realm.Arcadia,
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
            Realm = Realm.Arcadia,
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
            Realm = Realm.Arcadia,
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
            Realm = Realm.Arcadia,
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
            Realm = Realm.Arcadia,
            ContentType = "image/png",
            Limit = 10,
            Offset = 0
        };

        var assetIds = new List<string> { "asset-1", "asset-2", "asset-3" };

        var asset1 = new InternalAssetRecord
        {
            AssetId = "asset-1",
            AssetType = AssetType.Texture,
            Realm = Realm.Arcadia,
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
            Realm = Realm.Arcadia,
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
            Realm = Realm.Omega, // Different realm
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
            Realm = Realm.Arcadia,
            Tags = new List<string> { "character", "sword" },
            Limit = 10,
            Offset = 0
        };

        var assetIds = new List<string> { "asset-1", "asset-2" };

        var asset1 = new InternalAssetRecord
        {
            AssetId = "asset-1",
            AssetType = AssetType.Texture,
            Realm = Realm.Arcadia,
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
            Realm = Realm.Arcadia,
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
            AssetIds = new List<string> { "asset-1" },
            StorageKey = "bundles/current/existing-bundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = BundleStatus.Ready
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
        var request = new CreateBundleRequest
        {
            BundleId = "small-bundle",
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
            Realm = Realm.Arcadia,
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
        Assert.Equal("small-bundle", result.BundleId);
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
            AssetIds = new List<string> { "asset-1" },
            StorageKey = "bundles/current/pending-bundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = BundleStatus.Processing // Not ready
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
            Realm = Realm.Arcadia,
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
            Realm = Realm.Arcadia,
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
            AssetIds = new List<string> { "asset-1" },
            StorageKey = "bundles/current/existing-bundle.bundle",
            SizeBytes = 1024,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = BundleStatus.Ready
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
        Assert.Equal("minio", config.StorageProvider);
        Assert.Equal("bannou-assets", config.StorageBucket);
        Assert.Equal("minio:9000", config.StorageEndpoint);
        Assert.Equal(3600, config.TokenTtlSeconds);
        Assert.Equal(500, config.MaxUploadSizeMb);
        Assert.Equal(50, config.MultipartThresholdMb);
    }
}

public class MinioWebhookHandlerTests
{
    private Mock<IStateStoreFactory> _mockStateStoreFactory = null!;
    private Mock<IStateStore<UploadSession>> _mockUploadSessionStore = null!;
    private Mock<IMessageBus> _mockMessageBus = null!;
    private Mock<ILogger<MinioWebhookHandler>> _mockLogger = null!;
    private AssetServiceConfiguration _configuration = null!;

    public MinioWebhookHandlerTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockUploadSessionStore = new Mock<IStateStore<UploadSession>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<MinioWebhookHandler>>();
        _configuration = new AssetServiceConfiguration();

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
    private Mock<IClientEventPublisher> _mockClientEventPublisher = null!;
    private Mock<ILogger<AssetEventEmitter>> _mockLogger = null!;

    public AssetEventEmitterTests()
    {
        _mockClientEventPublisher = new Mock<IClientEventPublisher>();
        _mockLogger = new Mock<ILogger<AssetEventEmitter>>();
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
