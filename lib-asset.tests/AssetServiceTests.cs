using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Asset.Bundles;
using BeyondImmersion.BannouService.Asset.Events;
using BeyondImmersion.BannouService.Asset.Models;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Storage;
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
    private BundleConverter _bundleConverter = null!;
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
        _bundleConverter = new BundleConverter(new Mock<ILogger<BundleConverter>>().Object);
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup state store factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<InternalAssetRecord>(STATE_STORE)).Returns(_mockAssetStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockIndexStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BundleMetadata>(STATE_STORE)).Returns(_mockBundleStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<UploadSession>(STATE_STORE)).Returns(_mockUploadSessionStore.Object);
        _mockStateStoreFactory.Setup(f => f.SupportsSearch(STATE_STORE)).Returns(false);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            _bundleConverter,
            _mockEventConsumer.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            null!,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            _bundleConverter,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            _mockStateStoreFactory.Object,
            null!,
            _mockLogger.Object,
            _configuration,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            _bundleConverter,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            null!,
            _configuration,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            _bundleConverter,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            null!,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            _bundleConverter,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullAssetEventEmitter_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            null!,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            _bundleConverter,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullStorageProvider_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockAssetEventEmitter.Object,
            null!,
            _mockOrchestratorClient.Object,
            _bundleConverter,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullOrchestratorClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            null!,
            _bundleConverter,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullBundleConverter_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            null!,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AssetService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            _configuration,
            _mockAssetEventEmitter.Object,
            _mockStorageProvider.Object,
            _mockOrchestratorClient.Object,
            _bundleConverter,
            null!));
    }

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
            Content_type = "image/png"
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
            Content_type = "image/png"
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
            Content_type = "image/png"
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
            Content_type = ""
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
            Content_type = "image/png",
            Metadata = new AssetMetadataInput
            {
                Asset_type = AssetType.Texture,
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
        Assert.NotNull(result.Upload_url);
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
            Content_type = "model/gltf-binary"
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
        Assert.NotNull(result.Multipart?.Upload_urls);
        Assert.True(result.Multipart.Upload_urls.Count > 0);
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
            Asset_id = "non-existent-asset"
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
        var request = new GetAssetRequest { Asset_id = assetId };

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
        Assert.Equal(assetId, result.Asset_id);
        Assert.NotNull(result.Download_url);
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
            Asset_id = "non-existent-asset",
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
            Asset_id = assetId,
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
        Assert.False(versionsList[0].Is_archived); // STANDARD storage
        Assert.True(versionsList[1].Is_archived); // GLACIER storage
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
            Asset_type = AssetType.Texture,
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
            Asset_type = AssetType.Texture,
            Realm = Realm.Arcadia,
            Content_type = "image/png",
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
        Assert.Equal("asset-1", result.Assets.First().Asset_id);
    }

    [Fact]
    public async Task SearchAssetsAsync_WithTagFilter_ShouldFilterByTags()
    {
        // Arrange
        var service = CreateService();
        var request = new AssetSearchRequest
        {
            Asset_type = AssetType.Texture,
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
        Assert.Equal("asset-1", result.Assets.First().Asset_id);
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
            Bundle_id = "",
            Asset_ids = new List<string> { "asset-1" }
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
            Bundle_id = "test-bundle",
            Asset_ids = new List<string>()
        };

        // Act
        var (status, result) = await service.CreateBundleAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(result);
    }

    #endregion

    #region GetBundleAsync Tests

    [Fact]
    public async Task GetBundleAsync_WhenBundleNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetBundleRequest { Bundle_id = "non-existent" };

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
            _bundleConverter,
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
        Assert.Equal("http://minio:9000", config.StorageEndpoint);
        Assert.Equal(3600, config.TokenTtlSeconds);
        Assert.Equal(500, config.MaxUploadSizeMb);
        Assert.Equal(50, config.MultipartThresholdMb);
    }
}
