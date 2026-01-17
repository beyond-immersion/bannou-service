using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.TestUtilities;

namespace BeyondImmersion.BannouService.State.Tests;

public class StateServiceTests
{
    private readonly Mock<ILogger<StateService>> _mockLogger = new();
    private readonly StateServiceConfiguration _configuration = new();
    private readonly Mock<IMessageBus> _mockMessageBus = new();
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory = new();
    private readonly Mock<IStateStore<object>> _mockStateStore = new();

    public StateServiceTests()
    {
    }

    private StateService CreateService()
    {
        return new StateService(
            _mockLogger.Object,
            _configuration,
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object);
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
    /// </summary>
    [Fact]
    public void StateService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<StateService>();

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
        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.GetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
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
        _mockStateStore.Setup(s => s.SaveAsync("test-key", It.IsAny<object>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedEtag);

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
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
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
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
            .ReturnsAsync("new-etag-456");

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("new-etag-456", response.Etag);
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

        StateOptions? capturedOptions = null;
        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetStore<object>("test-store")).Returns(_mockStateStore.Object);
        _mockStateStore.Setup(s => s.SaveAsync("test-key", It.IsAny<object>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, StateOptions?, CancellationToken>((_, _, opts, _) => capturedOptions = opts)
            .ReturnsAsync("etag");

        // Act
        await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal(300, capturedOptions.Ttl);
        Assert.Equal(StateOptionsConsistency.Eventual, capturedOptions.Consistency);
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
        _mockStateStore.Setup(s => s.SaveAsync("test-key", It.IsAny<object>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Save failed"));
        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.SaveStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
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
        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.DeleteStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
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
        var request = new QueryStateRequest { StoreName = "non-existing-store" };

        _mockStateStoreFactory.Setup(f => f.HasStore("non-existing-store")).Returns(false);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryStateAsync_WithRedisBackendWithoutSearch_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest { StoreName = "redis-store" };

        _mockStateStoreFactory.Setup(f => f.HasStore("redis-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("redis-store")).Returns(StateBackend.Redis);
        _mockStateStoreFactory.Setup(f => f.SupportsSearch("redis-store")).Returns(false);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task QueryStateAsync_WithMySqlBackend_ReturnsQueryResults()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest { StoreName = "mysql-store", Page = 0, PageSize = 10 };

        var mockJsonStore = new Mock<IJsonQueryableStateStore<object>>();
        var queryResults = new JsonPagedResult<object>(
            Items: new List<JsonQueryResult<object>>
            {
                new JsonQueryResult<object>("key1", new { Name = "Value1" }),
                new JsonQueryResult<object>("key2", new { Name = "Value2" })
            },
            TotalCount: 2,
            Offset: 0,
            Limit: 10);

        _mockStateStoreFactory.Setup(f => f.HasStore("mysql-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("mysql-store")).Returns(StateBackend.MySql);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<object>("mysql-store")).Returns(mockJsonStore.Object);
        mockJsonStore.Setup(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(queryResults);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Results.Count);
        Assert.Equal(2, response.TotalCount);
        Assert.Equal(0, response.Page);
        Assert.Equal(10, response.PageSize);
    }

    [Fact]
    public async Task QueryStateAsync_WithMySqlBackendAndSortSpec_PassesSortToStore()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest
        {
            StoreName = "mysql-store",
            Sort = new List<SortField> { new SortField { Field = "$.name", Order = SortFieldOrder.Desc } },
            Page = 0,
            PageSize = 10
        };

        var mockJsonStore = new Mock<IJsonQueryableStateStore<object>>();
        JsonSortSpec? capturedSort = null;

        _mockStateStoreFactory.Setup(f => f.HasStore("mysql-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("mysql-store")).Returns(StateBackend.MySql);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<object>("mysql-store")).Returns(mockJsonStore.Object);
        mockJsonStore.Setup(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>((_, _, _, sort, _) => capturedSort = sort)
            .ReturnsAsync(new JsonPagedResult<object>(
                Items: new List<JsonQueryResult<object>>(),
                TotalCount: 0,
                Offset: 0,
                Limit: 10));

        // Act
        await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedSort);
        Assert.Equal("$.name", capturedSort.Path);
        Assert.True(capturedSort.Descending);
    }

    [Fact]
    public async Task QueryStateAsync_WithMySqlBackendAndConditions_PassesConditionsToStore()
    {
        // Arrange
        var service = CreateService();
        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.status", Operator = QueryOperator.Equals, Value = "active" },
            new QueryCondition { Path = "$.name", Operator = QueryOperator.Contains, Value = "test" }
        };
        var request = new QueryStateRequest
        {
            StoreName = "mysql-store",
            Conditions = conditions,
            Page = 0,
            PageSize = 10
        };

        var mockJsonStore = new Mock<IJsonQueryableStateStore<object>>();
        IReadOnlyList<QueryCondition>? capturedConditions = null;

        _mockStateStoreFactory.Setup(f => f.HasStore("mysql-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("mysql-store")).Returns(StateBackend.MySql);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<object>("mysql-store")).Returns(mockJsonStore.Object);
        mockJsonStore.Setup(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>((cond, _, _, _, _) => capturedConditions = cond)
            .ReturnsAsync(new JsonPagedResult<object>(
                Items: new List<JsonQueryResult<object>>(),
                TotalCount: 0,
                Offset: 0,
                Limit: 10));

        // Act
        await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedConditions);
        Assert.Equal(2, capturedConditions.Count);
        Assert.Equal("$.status", capturedConditions[0].Path);
        Assert.Equal(QueryOperator.Equals, capturedConditions[0].Operator);
        Assert.Equal("active", capturedConditions[0].Value?.ToString());
        Assert.Equal("$.name", capturedConditions[1].Path);
        Assert.Equal(QueryOperator.Contains, capturedConditions[1].Operator);
        Assert.Equal("test", capturedConditions[1].Value?.ToString());
    }

    [Fact]
    public async Task QueryStateAsync_WithMySqlBackendAndNullConditions_PassesEmptyConditionsToStore()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest
        {
            StoreName = "mysql-store",
            Conditions = null,
            Page = 0,
            PageSize = 10
        };

        var mockJsonStore = new Mock<IJsonQueryableStateStore<object>>();
        IReadOnlyList<QueryCondition>? capturedConditions = null;

        _mockStateStoreFactory.Setup(f => f.HasStore("mysql-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("mysql-store")).Returns(StateBackend.MySql);
        _mockStateStoreFactory.Setup(f => f.GetJsonQueryableStore<object>("mysql-store")).Returns(mockJsonStore.Object);
        mockJsonStore.Setup(s => s.JsonQueryPagedAsync(
            It.IsAny<IReadOnlyList<QueryCondition>?>(),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<QueryCondition>?, int, int, JsonSortSpec?, CancellationToken>((cond, _, _, _, _) => capturedConditions = cond)
            .ReturnsAsync(new JsonPagedResult<object>(
                Items: new List<JsonQueryResult<object>>(),
                TotalCount: 0,
                Offset: 0,
                Limit: 10));

        // Act
        await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedConditions);
        Assert.Empty(capturedConditions);
    }

    [Fact]
    public async Task QueryStateAsync_WithRedisBackendAndSearchEnabled_ReturnsSearchResults()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest
        {
            StoreName = "redis-search-store",
            Query = "@name:John",
            Page = 0,
            PageSize = 10
        };

        var mockSearchStore = new Mock<ISearchableStateStore<object>>();
        var searchResults = new SearchPagedResult<object>(
            Items: new List<SearchResult<object>>
            {
                new SearchResult<object>("key1", new { Name = "John" }, 1.0)
            },
            TotalCount: 1,
            Offset: 0,
            Limit: 10);

        _mockStateStoreFactory.Setup(f => f.HasStore("redis-search-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("redis-search-store")).Returns(StateBackend.Redis);
        _mockStateStoreFactory.Setup(f => f.SupportsSearch("redis-search-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetSearchableStore<object>("redis-search-store")).Returns(mockSearchStore.Object);
        mockSearchStore.Setup(s => s.SearchAsync(
            It.IsAny<string>(),
            "@name:John",
            It.IsAny<SearchQueryOptions?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(searchResults);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Results);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task QueryStateAsync_WithRedisBackendAndCustomIndexName_UsesProvidedIndex()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest
        {
            StoreName = "redis-search-store",
            IndexName = "custom-idx",
            Query = "*",
            Page = 0,
            PageSize = 10
        };

        var mockSearchStore = new Mock<ISearchableStateStore<object>>();
        string? capturedIndexName = null;

        _mockStateStoreFactory.Setup(f => f.HasStore("redis-search-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("redis-search-store")).Returns(StateBackend.Redis);
        _mockStateStoreFactory.Setup(f => f.SupportsSearch("redis-search-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetSearchableStore<object>("redis-search-store")).Returns(mockSearchStore.Object);
        mockSearchStore.Setup(s => s.SearchAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<SearchQueryOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, string, SearchQueryOptions?, CancellationToken>((idx, _, _, _) => capturedIndexName = idx)
            .ReturnsAsync(new SearchPagedResult<object>(
                Items: new List<SearchResult<object>>(),
                TotalCount: 0,
                Offset: 0,
                Limit: 10));

        // Act
        await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal("custom-idx", capturedIndexName);
    }

    [Fact]
    public async Task QueryStateAsync_WithRedisBackendDefaultsIndexName_UsesStoreNameBasedIndex()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest
        {
            StoreName = "my-store",
            Query = "*",
            Page = 0,
            PageSize = 10
        };

        var mockSearchStore = new Mock<ISearchableStateStore<object>>();
        string? capturedIndexName = null;

        _mockStateStoreFactory.Setup(f => f.HasStore("my-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("my-store")).Returns(StateBackend.Redis);
        _mockStateStoreFactory.Setup(f => f.SupportsSearch("my-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetSearchableStore<object>("my-store")).Returns(mockSearchStore.Object);
        mockSearchStore.Setup(s => s.SearchAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<SearchQueryOptions?>(),
            It.IsAny<CancellationToken>()))
            .Callback<string, string, SearchQueryOptions?, CancellationToken>((idx, _, _, _) => capturedIndexName = idx)
            .ReturnsAsync(new SearchPagedResult<object>(
                Items: new List<SearchResult<object>>(),
                TotalCount: 0,
                Offset: 0,
                Limit: 10));

        // Act
        await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal("my-store-idx", capturedIndexName);
    }

    [Fact]
    public async Task QueryStateAsync_WhenExceptionThrown_ReturnsInternalServerErrorAndEmitsErrorEvent()
    {
        // Arrange
        var service = CreateService();
        var request = new QueryStateRequest { StoreName = "test-store" };

        _mockStateStoreFactory.Setup(f => f.HasStore("test-store")).Returns(true);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("test-store"))
            .Throws(new Exception("Backend check failed"));
        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.QueryStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
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
        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.BulkGetStateAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
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
        _mockStateStoreFactory.Setup(f => f.GetBackendType("redis-store")).Returns(StateBackend.Redis);
        _mockStateStoreFactory.Setup(f => f.GetBackendType("mysql-store")).Returns(StateBackend.MySql);

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

        _mockStateStoreFactory.Setup(f => f.GetStoreNames(StateBackend.Redis))
            .Returns(new[] { "redis-store-1", "redis-store-2" });
        _mockStateStoreFactory.Setup(f => f.GetBackendType(It.IsAny<string>())).Returns(StateBackend.Redis);

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

        _mockStateStoreFactory.Setup(f => f.GetStoreNames(StateBackend.MySql))
            .Returns(new[] { "mysql-store" });
        _mockStateStoreFactory.Setup(f => f.GetBackendType(It.IsAny<string>())).Returns(StateBackend.MySql);

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
        _mockMessageBus.Setup(m => m.TryPublishErrorAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.ListStoresAsync(null, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
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
