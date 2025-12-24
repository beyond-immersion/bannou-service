using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.State.Services;

namespace BeyondImmersion.BannouService.State.Tests;

public class StateServiceTests
{
    private Mock<ILogger<StateService>> _mockLogger = null!;
    private StateServiceConfiguration _configuration = null!;
    private Mock<IErrorEventEmitter> _mockErrorEventEmitter = null!;
    private Mock<IStateStoreFactory> _mockStateStoreFactory = null!;
    private Mock<IStateStore<object>> _mockStateStore = null!;

    public StateServiceTests()
    {
        _mockLogger = new Mock<ILogger<StateService>>();
        _configuration = new StateServiceConfiguration();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockStateStore = new Mock<IStateStore<object>>();
    }

    private StateService CreateService()
    {
        return new StateService(
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockStateStoreFactory.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        // Arrange & Act
        var service = CreateService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StateService(
            null!,
            _configuration,
            _mockErrorEventEmitter.Object,
            _mockStateStoreFactory.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StateService(
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object,
            _mockStateStoreFactory.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StateService(
            _mockLogger.Object,
            _configuration,
            null!,
            _mockStateStoreFactory.Object));
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new StateService(
            _mockLogger.Object,
            _configuration,
            _mockErrorEventEmitter.Object,
            null!));
    }

    #endregion

    #region GetStateAsync Tests

    [Fact]
    public async Task GetStateAsync_WithValidRequest_ReturnsOkWithValue()
    {
        // Arrange
        var service = CreateService();
        var request = new GetStateRequest { StoreName = "test-store", Key = "test-key" };
        var expectedValue = new { Name = "Test", Value = 123 };
        var expectedEtag = "etag-123";

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.GetWithETagAsync("test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync((expectedValue, expectedEtag));

        // Act
        var (status, response) = await service.GetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(expectedValue, response.Value);
        Assert.Equal(expectedEtag, response.Etag);
    }

    [Fact]
    public async Task GetStateAsync_WithNonExistingStore_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetStateRequest { StoreName = "non-existing-store", Key = "test-key" };

        _mockStateStoreFactory.Setup(f => f.HasStore("non-existing-store")).Returns(false);

        // Act
        var (status, response) = await service.GetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetStateAsync_WithNonExistingKey_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetStateRequest { StoreName = "test-store", Key = "non-existing-key" };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.GetWithETagAsync("non-existing-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync((null, null));

        // Act
        var (status, response) = await service.GetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetStateAsync_WhenExceptionThrown_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var request = new GetStateRequest { StoreName = "test-store", Key = "test-key" };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.GetWithETagAsync("test-key", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Redis connection failed"));
        _mockErrorEventEmitter.Setup(e => e.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.GetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "state", "GetState", "Exception", "Redis connection failed",
            "test-store", "post:/state/get", It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region SaveStateAsync Tests

    [Fact]
    public async Task SaveStateAsync_WithValidRequest_ReturnsOkWithSuccess()
    {
        // Arrange
        var service = CreateService();
        var request = new SaveStateRequest
        {
            StoreName = "test-store",
            Key = "test-key",
            Value = new { Name = "Test" }
        };
        var expectedEtag = "new-etag-123";

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.SaveAsync("test-key", It.IsAny<object>(), It.IsAny<Services.StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEtag);

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(expectedEtag, response.Etag);
    }

    [Fact]
    public async Task SaveStateAsync_WithNonExistingStore_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new SaveStateRequest
        {
            StoreName = "non-existing-store",
            Key = "test-key",
            Value = new { Name = "Test" }
        };

        _mockStateStoreFactory.Setup(f => f.HasStore("non-existing-store")).Returns(false);

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SaveStateAsync_WithETagMismatch_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var request = new SaveStateRequest
        {
            StoreName = "test-store",
            Key = "test-key",
            Value = new { Name = "Test" },
            Options = new BeyondImmersion.BannouService.State.StateOptions { Etag = "old-etag" }
        };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.TrySaveAsync("test-key", It.IsAny<object>(), "old-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
    }

    [Fact]
    public async Task SaveStateAsync_WithETagMatch_ReturnsOkWithSuccess()
    {
        // Arrange
        var service = CreateService();
        var request = new SaveStateRequest
        {
            StoreName = "test-store",
            Key = "test-key",
            Value = new { Name = "Test" },
            Options = new BeyondImmersion.BannouService.State.StateOptions { Etag = "matching-etag" }
        };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.TrySaveAsync("test-key", It.IsAny<object>(), "matching-etag", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task SaveStateAsync_WithOptions_MapsOptionsCorrectly()
    {
        // Arrange
        var service = CreateService();
        var request = new SaveStateRequest
        {
            StoreName = "test-store",
            Key = "test-key",
            Value = new { Name = "Test" },
            Options = new BeyondImmersion.BannouService.State.StateOptions
            {
                Ttl = 300, // 5 minutes
                Consistency = StateOptionsConsistency.Eventual
            }
        };

        Services.StateOptions? capturedOptions = null;
        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.SaveAsync("test-key", It.IsAny<object>(), It.IsAny<Services.StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, Services.StateOptions?, CancellationToken>((_, _, opts, _) => capturedOptions = opts)
            .ReturnsAsync("etag");

        // Act
        await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal(TimeSpan.FromSeconds(300), capturedOptions.Ttl);
        Assert.Equal(Services.StateConsistency.Eventual, capturedOptions.Consistency);
    }

    [Fact]
    public async Task SaveStateAsync_WhenExceptionThrown_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var request = new SaveStateRequest
        {
            StoreName = "test-store",
            Key = "test-key",
            Value = new { Name = "Test" }
        };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.SaveAsync("test-key", It.IsAny<object>(), It.IsAny<Services.StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Save failed"));
        _mockErrorEventEmitter.Setup(e => e.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "state", "SaveState", "Exception", "Save failed",
            "test-store", "post:/state/save", It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteStateAsync Tests

    [Fact]
    public async Task DeleteStateAsync_WithExistingKey_ReturnsOkWithDeletedTrue()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteStateRequest { StoreName = "test-store", Key = "test-key" };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.DeleteAsync("test-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.DeleteStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Deleted);
    }

    [Fact]
    public async Task DeleteStateAsync_WithNonExistingKey_ReturnsOkWithDeletedFalse()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteStateRequest { StoreName = "test-store", Key = "non-existing-key" };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.DeleteAsync("non-existing-key", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var (status, response) = await service.DeleteStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Deleted);
    }

    [Fact]
    public async Task DeleteStateAsync_WithNonExistingStore_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteStateRequest { StoreName = "non-existing-store", Key = "test-key" };

        _mockStateStoreFactory.Setup(f => f.HasStore("non-existing-store")).Returns(false);

        // Act
        var (status, response) = await service.DeleteStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task DeleteStateAsync_WhenExceptionThrown_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteStateRequest { StoreName = "test-store", Key = "test-key" };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.DeleteAsync("test-key", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Delete failed"));
        _mockErrorEventEmitter.Setup(e => e.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.DeleteStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "state", "DeleteState", "Exception", "Delete failed",
            "test-store", "post:/state/delete", It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region QueryStateAsync Tests

    [Fact]
    public async Task QueryStateAsync_WithNonExistingStore_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest { StoreName = "non-existing-store", Filter = new { } };

        _mockStateStoreFactory.Setup(f => f.HasStore("non-existing-store")).Returns(false);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryStateAsync_WithRedisBackend_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest { StoreName = "redis-store", Filter = new { } };

        _mockStateStoreFactory.Setup(f => f.HasStore("redis-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("redis-store")).Returns(Services.StateBackend.Redis);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryStateAsync_WithMySqlBackend_ReturnsInternalServerError()
    {
        // Arrange - Currently returns InternalServerError because JSON filter parsing is not implemented
        var service = CreateService();
        var request = new QueryStateRequest { StoreName = "mysql-store", Filter = new { } };

        _mockStateStoreFactory.Setup(f => f.HasStore("mysql-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("mysql-store")).Returns(Services.StateBackend.MySql);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert - Not yet implemented
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryStateAsync_WhenExceptionThrown_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest { StoreName = "test-store", Filter = new { } };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("test-store"))
            .Throws(new Exception("Backend check failed"));
        _mockErrorEventEmitter.Setup(e => e.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "state", "QueryState", "Exception", "Backend check failed",
            "test-store", "post:/state/query", It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region BulkGetStateAsync Tests

    [Fact]
    public async Task BulkGetStateAsync_WithValidRequest_ReturnsOkWithItems()
    {
        // Arrange
        var service = CreateService();
        var request = new BulkGetStateRequest
        {
            StoreName = "test-store",
            Keys = new List<string> { "key1", "key2", "key3" }
        };

        var results = new Dictionary<string, object>
        {
            { "key1", new { Name = "Value1" } },
            { "key3", new { Name = "Value3" } }
        };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);

        // Act
        var (status, response) = await service.BulkGetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.Items.Count);

        var items = response.Items.ToList();
        Assert.True(items.Single(i => i.Key == "key1").Found);
        Assert.False(items.Single(i => i.Key == "key2").Found);
        Assert.True(items.Single(i => i.Key == "key3").Found);
    }

    [Fact]
    public async Task BulkGetStateAsync_WithNonExistingStore_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new BulkGetStateRequest
        {
            StoreName = "non-existing-store",
            Keys = new List<string> { "key1" }
        };

        _mockStateStoreFactory.Setup(f => f.HasStore("non-existing-store")).Returns(false);

        // Act
        var (status, response) = await service.BulkGetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task BulkGetStateAsync_WithAllKeysMissing_ReturnsOkWithNotFoundItems()
    {
        // Arrange
        var service = CreateService();
        var request = new BulkGetStateRequest
        {
            StoreName = "test-store",
            Keys = new List<string> { "key1", "key2" }
        };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());

        // Act
        var (status, response) = await service.BulkGetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
        Assert.All(response.Items, item => Assert.False(item.Found));
    }

    [Fact]
    public async Task BulkGetStateAsync_WhenExceptionThrown_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var request = new BulkGetStateRequest
        {
            StoreName = "test-store",
            Keys = new List<string> { "key1" }
        };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Bulk get failed"));
        _mockErrorEventEmitter.Setup(e => e.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.BulkGetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "state", "BulkGetState", "Exception", "Bulk get failed",
            "test-store", "post:/state/bulk-get", It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region ListStoresAsync Tests

    [Fact]
    public async Task ListStoresAsync_WithNoFilter_ReturnsAllStores()
    {
        // Arrange
        var service = CreateService();
        var storeNames = new[] { "redis-store", "mysql-store" };

        _mockStateStoreFactory.Setup(f => f.GetStoreNames()).Returns(storeNames);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("redis-store")).Returns(Services.StateBackend.Redis);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("mysql-store")).Returns(Services.StateBackend.MySql);

        // Act
        var (status, response) = await service.ListStoresAsync(null, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Stores.Count);

        var stores = response.Stores.ToList();
        Assert.Contains(stores, s => s.Name == "redis-store" && s.Backend == StoreInfoBackend.Redis);
        Assert.Contains(stores, s => s.Name == "mysql-store" && s.Backend == StoreInfoBackend.Mysql);
    }

    [Fact]
    public async Task ListStoresAsync_WithRedisFilter_ReturnsOnlyRedisStores()
    {
        // Arrange
        var service = CreateService();
        var request = new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Redis };

        _mockStateStoreFactory.Setup(f => f.GetStoreNames(Services.StateBackend.Redis))
            .Returns(new[] { "redis-store-1", "redis-store-2" });
        _mockStateStoreFactory.Setup(f => f.GetBackendType(It.IsAny<string>())).Returns(Services.StateBackend.Redis);

        // Act
        var (status, response) = await service.ListStoresAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Stores.Count);
        Assert.All(response.Stores, s => Assert.Equal(StoreInfoBackend.Redis, s.Backend));
    }

    [Fact]
    public async Task ListStoresAsync_WithMySqlFilter_ReturnsOnlyMySqlStores()
    {
        // Arrange
        var service = CreateService();
        var request = new ListStoresRequest { BackendFilter = ListStoresRequestBackendFilter.Mysql };

        _mockStateStoreFactory.Setup(f => f.GetStoreNames(Services.StateBackend.MySql))
            .Returns(new[] { "mysql-store" });
        _mockStateStoreFactory.Setup(f => f.GetBackendType(It.IsAny<string>())).Returns(Services.StateBackend.MySql);

        // Act
        var (status, response) = await service.ListStoresAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Stores);
        Assert.Equal(StoreInfoBackend.Mysql, response.Stores.First().Backend);
    }

    [Fact]
    public async Task ListStoresAsync_WithEmptyStores_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        _mockStateStoreFactory.Setup(f => f.GetStoreNames()).Returns(Array.Empty<string>());

        // Act
        var (status, response) = await service.ListStoresAsync(null, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Stores);
    }

    [Fact]
    public async Task ListStoresAsync_WhenExceptionThrown_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var service = CreateService();

        _mockStateStoreFactory.Setup(f => f.GetStoreNames())
            .Throws(new Exception("Failed to list stores"));
        _mockErrorEventEmitter.Setup(e => e.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.ListStoresAsync(null, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "state", "ListStores", "Exception", "Failed to list stores",
            "state-factory", "post:/state/list-stores", It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}

public class StateConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new StateServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void Configuration_DefaultValues_ShouldBeSet()
    {
        // Arrange & Act
        var config = new StateServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }
}
