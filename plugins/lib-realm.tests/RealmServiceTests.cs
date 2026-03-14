using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using BeyondImmersion.BannouService.Worldstate;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

#pragma warning disable CS8620 // Argument of type cannot be used for parameter of type due to differences in the nullability

namespace BeyondImmersion.BannouService.Realm.Tests;

/// <summary>
/// Comprehensive unit tests for RealmService.
/// Tests all CRUD operations, deprecation lifecycle, event publishing, and error handling.
/// Note: Tests using StateEntry (for atomic index operations) are best covered by
/// HTTP integration tests due to StateEntry mocking complexity.
/// </summary>
public class RealmServiceTests : ServiceTestBase<RealmServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IStateStore<RealmModel>> _mockRealmStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockListStore;
    private readonly Mock<ILogger<RealmService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ISpeciesClient> _mockSpeciesClient;
    private readonly Mock<ILocationClient> _mockLocationClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IWorldstateClient> _mockWorldstateClient;

    private const string STATE_STORE = "realm-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string REALM_KEY_PREFIX = "realm:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string ALL_REALMS_KEY = "all-realms";

    public RealmServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockRealmStore = new Mock<IStateStore<RealmModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
        _mockLogger = new Mock<ILogger<RealmService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockSpeciesClient = new Mock<ISpeciesClient>();
        _mockLocationClient = new Mock<ILocationClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockWorldstateClient = new Mock<IWorldstateClient>();

        // Setup factory to return typed stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RealmModel>(STATE_STORE))
            .Returns(_mockRealmStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
            .Returns(_mockListStore.Object);

        // Setup 3-param TryPublishAsync (the convenience overload services actually call)
        // Moq doesn't call through default interface implementations, so we must mock both overloads
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private RealmService CreateService()
    {
        return new RealmService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object,
            _mockResourceClient.Object,
            _mockSpeciesClient.Object,
            _mockLocationClient.Object,
            _mockCharacterClient.Object,
            _mockWorldstateClient.Object);
    }

    /// <summary>
    /// Creates a test RealmModel for use in tests.
    /// </summary>
    private static RealmModel CreateTestRealmModel(
        Guid? realmId = null,
        string code = "TEST",
        string name = "Test Realm",
        bool isActive = true,
        bool isDeprecated = false)
    {
        var id = realmId ?? Guid.NewGuid();
        return new RealmModel
        {
            RealmId = id,
            Code = code,
            Name = name,
            Description = "Test Description",
            Category = "test",
            IsActive = isActive,
            IsDeprecated = isDeprecated,
            DeprecatedAt = isDeprecated ? DateTimeOffset.UtcNow : null,
            DeprecationReason = isDeprecated ? "Test reason" : null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    #region Constructor Tests

    #endregion

    #region GetRealm Tests

    [Fact]
    public async Task GetRealmAsync_WhenRealmExists_ShouldReturnRealm()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new GetRealmRequest { RealmId = realmId };
        var testModel = CreateTestRealmModel(realmId, "TEST_REALM", "Test Realm");

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.GetRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal("TEST_REALM", response.Code);
        Assert.Equal("Test Realm", response.Name);
    }

    [Fact]
    public async Task GetRealmAsync_WhenRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new GetRealmRequest { RealmId = realmId };

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.GetRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetRealmByCode Tests

    [Fact]
    public async Task GetRealmByCodeAsync_WhenCodeExists_ShouldReturnRealm()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var code = "TEST_REALM_2";
        var request = new GetRealmByCodeRequest { Code = code };
        var testModel = CreateTestRealmModel(realmId, code, "Test Realm 2");

        // Setup code index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup realm retrieval
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.GetRealmByCodeAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(code.ToUpperInvariant(), response.Code);
    }

    [Fact]
    public async Task GetRealmByCodeAsync_WhenCodeNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetRealmByCodeRequest { Code = "NONEXISTENT" };

        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}NONEXISTENT", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.GetRealmByCodeAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetRealmByCodeAsync_WhenIndexExistsButRealmMissing_ShouldReturnNotFound()
    {
        // Arrange - Tests data inconsistency handling
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var code = "ORPHAN";
        var request = new GetRealmByCodeRequest { Code = code };

        // Code index exists
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // But realm data is missing
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.GetRealmByCodeAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region RealmExists Tests

    [Fact]
    public async Task RealmExistsAsync_WhenActiveRealmExists_ShouldReturnExistsAndActive()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new RealmExistsRequest { RealmId = realmId };
        var testModel = CreateTestRealmModel(realmId, isActive: true, isDeprecated: false);

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.RealmExistsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Exists);
        Assert.True(response.IsActive);
        Assert.Equal(realmId, response.RealmId);
    }

    [Fact]
    public async Task RealmExistsAsync_WhenRealmNotFound_ShouldReturnNotExists()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new RealmExistsRequest { RealmId = realmId };

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.RealmExistsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.Exists);
        Assert.False(response.IsActive);
        Assert.Null(response.RealmId);
    }

    [Fact]
    public async Task RealmExistsAsync_WhenRealmIsDeprecated_ShouldReturnNotActive()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new RealmExistsRequest { RealmId = realmId };
        var testModel = CreateTestRealmModel(realmId, isActive: true, isDeprecated: true);

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.RealmExistsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Exists);
        Assert.False(response.IsActive); // Deprecated realms are not active
    }

    #endregion

    #region RealmsExistBatch Tests

    [Fact]
    public async Task RealmsExistBatchAsync_WhenEmpty_ShouldReturnSuccessWithAllFlags()
    {
        // Arrange
        var service = CreateService();
        var request = new RealmsExistBatchRequest { RealmIds = new List<Guid>() };

        // Act
        var (status, response) = await service.RealmsExistBatchAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Results);
        Assert.True(response.AllExist);
        Assert.True(response.AllActive);
        Assert.Empty(response.InvalidRealmIds);
        Assert.Empty(response.DeprecatedRealmIds);
    }

    [Fact]
    public async Task RealmsExistBatchAsync_WhenAllExistAndActive_ShouldReturnAllFlagsTrue()
    {
        // Arrange
        var service = CreateService();
        var realmId1 = Guid.NewGuid();
        var realmId2 = Guid.NewGuid();
        var request = new RealmsExistBatchRequest { RealmIds = new List<Guid> { realmId1, realmId2 } };

        var model1 = CreateTestRealmModel(realmId1, "REALM1", isActive: true, isDeprecated: false);
        var model2 = CreateTestRealmModel(realmId2, "REALM2", isActive: true, isDeprecated: false);

        var bulkResult = new Dictionary<string, RealmModel?>
        {
            [$"{REALM_KEY_PREFIX}{realmId1}"] = model1,
            [$"{REALM_KEY_PREFIX}{realmId2}"] = model2
        };

        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResult);

        // Act
        var (status, response) = await service.RealmsExistBatchAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Results.Count);
        Assert.True(response.AllExist);
        Assert.True(response.AllActive);
        Assert.Empty(response.InvalidRealmIds);
        Assert.Empty(response.DeprecatedRealmIds);

        // Verify order matches request
        var resultsList = response.Results.ToList();
        Assert.True(resultsList[0].Exists);
        Assert.True(resultsList[0].IsActive);
        Assert.True(resultsList[1].Exists);
        Assert.True(resultsList[1].IsActive);
    }

    [Fact]
    public async Task RealmsExistBatchAsync_WhenSomeNotFound_ShouldReturnInvalidIds()
    {
        // Arrange
        var service = CreateService();
        var realmId1 = Guid.NewGuid();
        var realmId2 = Guid.NewGuid(); // This one won't exist
        var request = new RealmsExistBatchRequest { RealmIds = new List<Guid> { realmId1, realmId2 } };

        var model1 = CreateTestRealmModel(realmId1, "REALM1", isActive: true, isDeprecated: false);

        // Only return model1, model2 is not in results (simulating missing realm)
        var bulkResult = new Dictionary<string, RealmModel?>
        {
            [$"{REALM_KEY_PREFIX}{realmId1}"] = model1,
            [$"{REALM_KEY_PREFIX}{realmId2}"] = null
        };

        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResult);

        // Act
        var (status, response) = await service.RealmsExistBatchAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Results.Count);
        Assert.False(response.AllExist);
        Assert.False(response.AllActive);
        Assert.Single(response.InvalidRealmIds);
        Assert.Contains(realmId2, response.InvalidRealmIds);
        Assert.Empty(response.DeprecatedRealmIds);

        // First realm exists and is active
        var resultsList = response.Results.ToList();
        Assert.True(resultsList[0].Exists);
        Assert.True(resultsList[0].IsActive);

        // Second realm doesn't exist
        Assert.False(resultsList[1].Exists);
        Assert.False(resultsList[1].IsActive);
    }

    [Fact]
    public async Task RealmsExistBatchAsync_WhenSomeDeprecated_ShouldReturnDeprecatedIds()
    {
        // Arrange
        var service = CreateService();
        var realmId1 = Guid.NewGuid();
        var realmId2 = Guid.NewGuid(); // This one is deprecated
        var request = new RealmsExistBatchRequest { RealmIds = new List<Guid> { realmId1, realmId2 } };

        var model1 = CreateTestRealmModel(realmId1, "REALM1", isActive: true, isDeprecated: false);
        var model2 = CreateTestRealmModel(realmId2, "REALM2", isActive: true, isDeprecated: true);

        var bulkResult = new Dictionary<string, RealmModel?>
        {
            [$"{REALM_KEY_PREFIX}{realmId1}"] = model1,
            [$"{REALM_KEY_PREFIX}{realmId2}"] = model2
        };

        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResult);

        // Act
        var (status, response) = await service.RealmsExistBatchAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Results.Count);
        Assert.True(response.AllExist); // Both exist
        Assert.False(response.AllActive); // One is deprecated
        Assert.Empty(response.InvalidRealmIds);
        Assert.Single(response.DeprecatedRealmIds);
        Assert.Contains(realmId2, response.DeprecatedRealmIds);

        // First realm is active
        var resultsList = response.Results.ToList();
        Assert.True(resultsList[0].IsActive);

        // Second realm exists but is not active (deprecated)
        Assert.True(resultsList[1].Exists);
        Assert.False(resultsList[1].IsActive);
    }

    #endregion

    #region UpdateRealm Tests

    [Fact]
    public async Task UpdateRealmAsync_WhenRealmExists_ShouldUpdateAndReturnOK()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var existingModel = CreateTestRealmModel(realmId, "TEST", "Original Name");
        var request = new UpdateRealmRequest
        {
            RealmId = realmId,
            Name = "Updated Name",
            Description = "Updated Description"
        };

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "mock-etag"));
        _mockRealmStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        // Act
        var (status, response) = await service.UpdateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Name", response.Name);

        // Verify save was called with optimistic concurrency
        _mockRealmStore.Verify(s => s.TrySaveAsync(
            $"{REALM_KEY_PREFIX}{realmId}", It.IsAny<RealmModel>(), "mock-etag", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Once);

        // Verify event was published via IMessageBus (3-param convenience overload)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm.updated", It.IsAny<RealmUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRealmAsync_WhenRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new UpdateRealmRequest { RealmId = realmId, Name = "New Name" };

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RealmModel?)null, (string?)null));

        // Act
        var (status, response) = await service.UpdateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);

        // Verify no save or event publishing occurred
        _mockRealmStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<RealmModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRealmAsync_WithNoChanges_ShouldNotPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var existingModel = CreateTestRealmModel(realmId, "TEST", "Same Name");
        var request = new UpdateRealmRequest
        {
            RealmId = realmId
            // No updates specified
        };

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "mock-etag"));
        _mockRealmStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        // Act
        var (status, response) = await service.UpdateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify no event was published (no changes) - 3-param convenience overload
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region DeleteRealm Tests

    [Fact]
    public async Task DeleteRealmAsync_WhenRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new DeleteRealmRequest { RealmId = realmId };

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var status = await service.DeleteRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteRealmAsync_WhenRealmNotDeprecated_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new DeleteRealmRequest { RealmId = realmId };
        var existingModel = CreateTestRealmModel(realmId, isDeprecated: false);

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var status = await service.DeleteRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert — per IMPLEMENTATION TENETS: reject delete with BadRequest if not deprecated
        Assert.Equal(StatusCodes.BadRequest, status);

        // Verify no delete occurred
        _mockRealmStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteRealmAsync_WhenDeprecatedRealm_ShouldDeleteAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new DeleteRealmRequest { RealmId = realmId };
        var existingModel = CreateTestRealmModel(realmId, "DELETED", isDeprecated: true);

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { realmId });

        // Act
        var status = await service.DeleteRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify delete operations
        _mockRealmStore.Verify(s => s.DeleteAsync(
            $"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()), Times.Once);
        _mockStringStore.Verify(s => s.DeleteAsync(
            $"{CODE_INDEX_PREFIX}DELETED", It.IsAny<CancellationToken>()), Times.Once);

        // Verify event was published via IMessageBus (3-param convenience overload)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm.deleted", It.IsAny<RealmDeletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeprecateRealm Tests

    [Fact]
    public async Task DeprecateRealmAsync_WhenRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new DeprecateRealmRequest { RealmId = realmId, Reason = "Test" };

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RealmModel?)null, (string?)null));

        // Act
        var (status, response) = await service.DeprecateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeprecateRealmAsync_WhenAlreadyDeprecated_ShouldReturnOk()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new DeprecateRealmRequest { RealmId = realmId, Reason = "Test" };
        var existingModel = CreateTestRealmModel(realmId, isDeprecated: true);

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "mock-etag"));
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.DeprecateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert — idempotent per IMPLEMENTATION TENETS: caller's intent is already satisfied
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task DeprecateRealmAsync_WhenValid_ShouldDeprecateAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new DeprecateRealmRequest { RealmId = realmId, Reason = "No longer needed" };
        var existingModel = CreateTestRealmModel(realmId, isDeprecated: false);

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "mock-etag"));
        _mockRealmStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        // Act
        var (status, response) = await service.DeprecateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
        Assert.Equal("No longer needed", response.DeprecationReason);
        Assert.NotNull(response.DeprecatedAt);

        // Verify event was published via IMessageBus (3-param convenience overload)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm.updated", It.IsAny<RealmUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UndeprecateRealm Tests

    [Fact]
    public async Task UndeprecateRealmAsync_WhenRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new UndeprecateRealmRequest { RealmId = realmId };

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RealmModel?)null, (string?)null));

        // Act
        var (status, response) = await service.UndeprecateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UndeprecateRealmAsync_WhenNotDeprecated_ShouldReturnOk()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new UndeprecateRealmRequest { RealmId = realmId };
        var existingModel = CreateTestRealmModel(realmId, isDeprecated: false);

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "mock-etag"));
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.UndeprecateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert — idempotent per IMPLEMENTATION TENETS: caller's intent is already satisfied
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task UndeprecateRealmAsync_WhenDeprecated_ShouldRestoreAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new UndeprecateRealmRequest { RealmId = realmId };
        var existingModel = CreateTestRealmModel(realmId, isDeprecated: true);

        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "mock-etag"));
        _mockRealmStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        // Act
        var (status, response) = await service.UndeprecateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsDeprecated);
        Assert.Null(response.DeprecationReason);
        Assert.Null(response.DeprecatedAt);

        // Verify event was published via IMessageBus (3-param convenience overload)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm.updated", It.IsAny<RealmUpdatedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CreateRealm Tests

    [Fact]
    public async Task CreateRealmAsync_WhenValid_ShouldCreateAndReturnOK()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var request = new CreateRealmRequest
        {
            Code = "new_realm",
            Name = "New Realm",
            GameServiceId = gameServiceId,
            Description = "A new realm",
            Category = "test",
            IsActive = true,
            IsSystemType = false
        };

        // Code index returns null (no duplicate)
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Capture saved realm model
        string? savedRealmKey = null;
        RealmModel? savedModel = null;
        _mockRealmStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith(REALM_KEY_PREFIX)),
                It.IsAny<RealmModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, RealmModel, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedRealmKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag-1");

        // Capture saved code index
        string? savedCodeKey = null;
        string? savedCodeValue = null;
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)),
                It.IsAny<string>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((k, v, _, _) =>
            {
                savedCodeKey = k;
                savedCodeValue = v;
            })
            .ReturnsAsync("etag-2");

        // Setup realm list store for AddToRealmListAsync
        _mockListStore
            .Setup(s => s.GetWithETagAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)new List<Guid>(), "list-etag"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(ALL_REALMS_KEY, It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("list-etag-2");

        // Capture published event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.CreateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("NEW_REALM", response.Code); // Code normalized to uppercase
        Assert.Equal("New Realm", response.Name);
        Assert.Equal(gameServiceId, response.GameServiceId);
        Assert.Equal("A new realm", response.Description);
        Assert.Equal("test", response.Category);
        Assert.True(response.IsActive);
        Assert.False(response.IsSystemType);
        Assert.False(response.IsDeprecated);

        // Assert on captured state - realm model
        Assert.NotNull(savedRealmKey);
        Assert.StartsWith(REALM_KEY_PREFIX, savedRealmKey);
        Assert.NotNull(savedModel);
        Assert.Equal("NEW_REALM", savedModel.Code);
        Assert.Equal("New Realm", savedModel.Name);
        Assert.Equal(gameServiceId, savedModel.GameServiceId);
        Assert.False(savedModel.IsDeprecated);

        // Assert on captured state - code index
        Assert.Equal($"{CODE_INDEX_PREFIX}NEW_REALM", savedCodeKey);
        Assert.NotNull(savedCodeValue);
        Assert.True(Guid.TryParse(savedCodeValue, out _));

        // Assert on captured event
        Assert.Equal("realm.created", capturedTopic);
        Assert.NotNull(capturedEvent);
        var typedEvent = Assert.IsType<RealmCreatedEvent>(capturedEvent);
        Assert.Equal(response.RealmId, typedEvent.RealmId);
        Assert.Equal("NEW_REALM", typedEvent.Code);
        Assert.True(typedEvent.Timestamp > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task CreateRealmAsync_WhenDuplicateCode_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateRealmRequest
        {
            Code = "EXISTING",
            Name = "Existing Realm",
            GameServiceId = Guid.NewGuid()
        };

        // Code index returns an existing ID (duplicate)
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}EXISTING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var (status, response) = await service.CreateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify no save occurred
        _mockRealmStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<RealmModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateRealmAsync_CodeNormalization_ShouldUppercaseCode()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateRealmRequest
        {
            Code = "lower_case_realm",
            Name = "Realm",
            GameServiceId = Guid.NewGuid()
        };

        // No duplicate
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Capture code index key
        string? savedCodeKey = null;
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith(CODE_INDEX_PREFIX)),
                It.IsAny<string>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, StateOptions?, CancellationToken>((k, _, _, _) => savedCodeKey = k)
            .ReturnsAsync("etag-1");

        _mockRealmStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Setup realm list for AddToRealmListAsync
        _mockListStore
            .Setup(s => s.GetWithETagAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)new List<Guid>(), "list-etag"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(ALL_REALMS_KEY, It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("list-etag-2");

        // Act
        var (status, response) = await service.CreateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert - code normalized to uppercase
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("LOWER_CASE_REALM", response.Code);
        Assert.Equal($"{CODE_INDEX_PREFIX}LOWER_CASE_REALM", savedCodeKey);
    }

    [Fact]
    public async Task CreateRealmAsync_ShouldAddToAllRealmsList()
    {
        // Arrange
        var service = CreateService();
        var request = new CreateRealmRequest
        {
            Code = "LISTED",
            Name = "Listed Realm",
            GameServiceId = Guid.NewGuid()
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockRealmStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Capture all-realms list save
        List<Guid>? savedList = null;
        _mockListStore
            .Setup(s => s.GetWithETagAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)new List<Guid>(), "list-etag"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(ALL_REALMS_KEY, It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, List<Guid>, string, StateOptions?, CancellationToken>((_, list, _, _, _) => savedList = list)
            .ReturnsAsync("list-etag-2");

        // Act
        var (status, _) = await service.CreateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedList);
        Assert.Single(savedList);
    }

    [Fact]
    public async Task CreateRealmAsync_WithAutoInitializeWorldstateClock_ShouldCallWorldstateClient()
    {
        // Arrange
        Configuration.AutoInitializeWorldstateClock = true;
        Configuration.DefaultCalendarTemplateCode = "standard-arcadia";

        var service = CreateService();
        var request = new CreateRealmRequest
        {
            Code = "CLOCK_REALM",
            Name = "Clock Realm",
            GameServiceId = Guid.NewGuid()
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockRealmStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");
        _mockListStore
            .Setup(s => s.GetWithETagAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)new List<Guid>(), "list-etag"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(ALL_REALMS_KEY, It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("list-etag-2");

        // Capture worldstate client call
        InitializeRealmClockRequest? capturedClockRequest = null;
        _mockWorldstateClient
            .Setup(w => w.InitializeRealmClockAsync(It.IsAny<InitializeRealmClockRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InitializeRealmClockRequest, CancellationToken>((r, _) => capturedClockRequest = r)
            .Returns(Task.FromResult(new InitializeRealmClockResponse()));

        // Act
        var (status, response) = await service.CreateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert - realm created successfully and worldstate clock initialized
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(capturedClockRequest);
        Assert.Equal(response.RealmId, capturedClockRequest.RealmId);
        Assert.Equal("standard-arcadia", capturedClockRequest.CalendarTemplateCode);
    }

    [Fact]
    public async Task CreateRealmAsync_WithAutoInitializeWorldstateClock_WhenWorldstateFails_ShouldStillSucceed()
    {
        // Arrange
        Configuration.AutoInitializeWorldstateClock = true;
        Configuration.DefaultCalendarTemplateCode = "standard-arcadia";

        var service = CreateService();
        var request = new CreateRealmRequest
        {
            Code = "CLOCK_FAIL",
            Name = "Clock Fail Realm",
            GameServiceId = Guid.NewGuid()
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockRealmStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");
        _mockListStore
            .Setup(s => s.GetWithETagAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)new List<Guid>(), "list-etag"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(ALL_REALMS_KEY, It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("list-etag-2");

        // Worldstate client throws ApiException
        _mockWorldstateClient
            .Setup(w => w.InitializeRealmClockAsync(It.IsAny<InitializeRealmClockRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Worldstate unavailable", 503, null, null, null));

        // Act
        var (status, response) = await service.CreateRealmAsync(request, TestContext.Current.CancellationToken);

        // Assert - realm creation still succeeds despite worldstate failure
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("CLOCK_FAIL", response.Code);
    }

    #endregion

    #region ListRealms Tests

    [Fact]
    public async Task ListRealmsAsync_WhenNoRealms_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var request = new ListRealmsRequest
        {
            Page = 1,
            PageSize = 20
        };

        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Guid>?)null);

        // Act
        var (status, response) = await service.ListRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Realms);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(1, response.Page);
        Assert.Equal(20, response.PageSize);
    }

    [Fact]
    public async Task ListRealmsAsync_WithPagination_ShouldReturnCorrectPage()
    {
        // Arrange
        var service = CreateService();
        var realmIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var request = new ListRealmsRequest
        {
            Page = 2,
            PageSize = 2
        };

        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmIds);

        // Build bulk results
        var bulkResults = new Dictionary<string, RealmModel?>();
        for (var i = 0; i < realmIds.Count; i++)
        {
            bulkResults[$"{REALM_KEY_PREFIX}{realmIds[i]}"] = CreateTestRealmModel(
                realmIds[i], $"REALM_{i}", $"Realm {i}");
        }
        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Realms.Count); // pageSize = 2
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Page);
        Assert.True(response.HasNextPage);
        Assert.True(response.HasPreviousPage);
    }

    [Fact]
    public async Task ListRealmsAsync_WithCategoryFilter_ShouldFilterByCategory()
    {
        // Arrange
        var service = CreateService();
        var realmId1 = Guid.NewGuid();
        var realmId2 = Guid.NewGuid();
        var realmIds = new List<Guid> { realmId1, realmId2 };
        var request = new ListRealmsRequest
        {
            Category = "physical",
            Page = 1,
            PageSize = 20
        };

        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmIds);

        var model1 = CreateTestRealmModel(realmId1, "PHYSICAL_1", "Physical Realm");
        model1.Category = "physical";
        var model2 = CreateTestRealmModel(realmId2, "SYSTEM_1", "System Realm");
        model2.Category = "system";

        var bulkResults = new Dictionary<string, RealmModel?>
        {
            [$"{REALM_KEY_PREFIX}{realmId1}"] = model1,
            [$"{REALM_KEY_PREFIX}{realmId2}"] = model2
        };
        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Realms);
        Assert.Equal("PHYSICAL_1", response.Realms.First().Code);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task ListRealmsAsync_WithIsActiveFilter_ShouldFilterByActiveStatus()
    {
        // Arrange
        var service = CreateService();
        var activeId = Guid.NewGuid();
        var inactiveId = Guid.NewGuid();
        var realmIds = new List<Guid> { activeId, inactiveId };
        var request = new ListRealmsRequest
        {
            IsActive = false,
            Page = 1,
            PageSize = 20
        };

        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmIds);

        var activeModel = CreateTestRealmModel(activeId, "ACTIVE", isActive: true);
        var inactiveModel = CreateTestRealmModel(inactiveId, "INACTIVE", isActive: false);

        var bulkResults = new Dictionary<string, RealmModel?>
        {
            [$"{REALM_KEY_PREFIX}{activeId}"] = activeModel,
            [$"{REALM_KEY_PREFIX}{inactiveId}"] = inactiveModel
        };
        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Realms);
        Assert.Equal("INACTIVE", response.Realms.First().Code);
    }

    [Fact]
    public async Task ListRealmsAsync_WithIncludeDeprecatedFalse_ShouldExcludeDeprecated()
    {
        // Arrange
        var service = CreateService();
        var normalId = Guid.NewGuid();
        var deprecatedId = Guid.NewGuid();
        var realmIds = new List<Guid> { normalId, deprecatedId };
        var request = new ListRealmsRequest
        {
            IncludeDeprecated = false,
            Page = 1,
            PageSize = 20
        };

        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmIds);

        var normalModel = CreateTestRealmModel(normalId, "NORMAL", isDeprecated: false);
        var deprecatedModel = CreateTestRealmModel(deprecatedId, "DEPRECATED", isDeprecated: true);

        var bulkResults = new Dictionary<string, RealmModel?>
        {
            [$"{REALM_KEY_PREFIX}{normalId}"] = normalModel,
            [$"{REALM_KEY_PREFIX}{deprecatedId}"] = deprecatedModel
        };
        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert - deprecated realm excluded
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Realms);
        Assert.Equal("NORMAL", response.Realms.First().Code);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task ListRealmsAsync_WithIncludeDeprecatedTrue_ShouldIncludeDeprecated()
    {
        // Arrange
        var service = CreateService();
        var normalId = Guid.NewGuid();
        var deprecatedId = Guid.NewGuid();
        var realmIds = new List<Guid> { normalId, deprecatedId };
        var request = new ListRealmsRequest
        {
            IncludeDeprecated = true,
            Page = 1,
            PageSize = 20
        };

        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmIds);

        var normalModel = CreateTestRealmModel(normalId, "NORMAL", isDeprecated: false);
        var deprecatedModel = CreateTestRealmModel(deprecatedId, "DEPRECATED", isDeprecated: true);

        var bulkResults = new Dictionary<string, RealmModel?>
        {
            [$"{REALM_KEY_PREFIX}{normalId}"] = normalModel,
            [$"{REALM_KEY_PREFIX}{deprecatedId}"] = deprecatedModel
        };
        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert - both realms included
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Realms.Count);
        Assert.Equal(2, response.TotalCount);
    }

    [Fact]
    public async Task ListRealmsAsync_WithCombinedFilters_ShouldApplyAllFilters()
    {
        // Arrange
        var service = CreateService();
        var matchId = Guid.NewGuid();
        var noMatchCategory = Guid.NewGuid();
        var noMatchActive = Guid.NewGuid();
        var realmIds = new List<Guid> { matchId, noMatchCategory, noMatchActive };
        var request = new ListRealmsRequest
        {
            Category = "physical",
            IsActive = true,
            IncludeDeprecated = true,
            Page = 1,
            PageSize = 20
        };

        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmIds);

        var matchModel = CreateTestRealmModel(matchId, "MATCH", isActive: true);
        matchModel.Category = "physical";

        var wrongCategoryModel = CreateTestRealmModel(noMatchCategory, "WRONG_CAT", isActive: true);
        wrongCategoryModel.Category = "system";

        var inactiveModel = CreateTestRealmModel(noMatchActive, "INACTIVE", isActive: false);
        inactiveModel.Category = "physical";

        var bulkResults = new Dictionary<string, RealmModel?>
        {
            [$"{REALM_KEY_PREFIX}{matchId}"] = matchModel,
            [$"{REALM_KEY_PREFIX}{noMatchCategory}"] = wrongCategoryModel,
            [$"{REALM_KEY_PREFIX}{noMatchActive}"] = inactiveModel
        };
        _mockRealmStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert - only the matching realm returned
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Realms);
        Assert.Equal("MATCH", response.Realms.First().Code);
    }

    #endregion

    #region MergeRealms Tests

    /// <summary>
    /// Helper to create a mock lock response.
    /// </summary>
    private static Mock<ILockResponse> CreateMockLock(bool success)
    {
        var mockLock = new Mock<ILockResponse>();
        mockLock.Setup(l => l.Success).Returns(success);
        mockLock.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);
        return mockLock;
    }

    [Fact]
    public async Task MergeRealmsAsync_WhenSourceEqualsTarget_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = realmId,
            TargetRealmId = realmId
        };

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRealmsAsync_WhenLockFails_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = sourceId,
            TargetRealmId = targetId
        };

        // Lock acquisition fails
        var failedLock = CreateMockLock(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRealmsAsync_WhenSourceNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = sourceId,
            TargetRealmId = targetId
        };

        var successLock = CreateMockLock(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        // Source realm not found
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRealmsAsync_WhenSourceNotDeprecated_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = sourceId,
            TargetRealmId = targetId
        };

        var successLock = CreateMockLock(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        // Source realm exists but is NOT deprecated
        var sourceModel = CreateTestRealmModel(sourceId, "SOURCE", isDeprecated: false);
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRealmsAsync_WhenSourceIsSystemType_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = sourceId,
            TargetRealmId = targetId
        };

        var successLock = CreateMockLock(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        // Source realm is deprecated but is a system realm
        var sourceModel = CreateTestRealmModel(sourceId, "VOID", isDeprecated: true);
        sourceModel.IsSystemType = true;
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRealmsAsync_WhenTargetNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = sourceId,
            TargetRealmId = targetId
        };

        var successLock = CreateMockLock(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        // Source realm found and deprecated
        var sourceModel = CreateTestRealmModel(sourceId, "SOURCE", isDeprecated: true);
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);

        // Target realm not found
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task MergeRealmsAsync_WithAllEmptyEntities_ShouldSucceedWithZeroCounts()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = sourceId,
            TargetRealmId = targetId
        };

        var successLock = CreateMockLock(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        var sourceModel = CreateTestRealmModel(sourceId, "SOURCE", isDeprecated: true);
        var targetModel = CreateTestRealmModel(targetId, "TARGET");

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        // All entity lists return empty
        _mockSpeciesClient
            .Setup(s => s.ListSpeciesByRealmAsync(It.IsAny<ListSpeciesByRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeciesListResponse { Species = new List<SpeciesResponse>(), TotalCount = 0 });
        _mockLocationClient
            .Setup(l => l.ListRootLocationsAsync(It.IsAny<ListRootLocationsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationListResponse
            {
                Locations = new List<LocationResponse>(),
                TotalCount = 0,
                Page = 1,
                PageSize = 50,
                HasNextPage = false,
                HasPreviousPage = false
            });
        _mockCharacterClient
            .Setup(c => c.GetCharactersByRealmAsync(It.IsAny<GetCharactersByRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterListResponse
            {
                Characters = new List<CharacterResponse>(),
                TotalCount = 0,
                Page = 1,
                PageSize = 50,
                HasNextPage = false,
                HasPreviousPage = false
            });

        // Capture published merge event
        string? capturedTopic = null;
        object? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, object, CancellationToken>((t, e, _) =>
            {
                capturedTopic = t;
                capturedEvent = e;
            })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.SpeciesMigrated);
        Assert.Equal(0, response.SpeciesFailed);
        Assert.Equal(0, response.LocationsMigrated);
        Assert.Equal(0, response.LocationsFailed);
        Assert.Equal(0, response.CharactersMigrated);
        Assert.Equal(0, response.CharactersFailed);
        Assert.False(response.SourceDeleted);

        // Assert merge event published
        Assert.Equal("realm.merged", capturedTopic);
        Assert.NotNull(capturedEvent);
        var mergedEvent = Assert.IsType<RealmMergedEvent>(capturedEvent);
        Assert.Equal(sourceId, mergedEvent.SourceRealmId);
        Assert.Equal(targetId, mergedEvent.TargetRealmId);
    }

    [Fact]
    public async Task MergeRealmsAsync_WithEntities_ShouldReportMigrationCounts()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = sourceId,
            TargetRealmId = targetId
        };

        var successLock = CreateMockLock(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        var sourceModel = CreateTestRealmModel(sourceId, "SOURCE", isDeprecated: true);
        var targetModel = CreateTestRealmModel(targetId, "TARGET");

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        // Species: 1 species, successfully migrated, then empty on second call
        var speciesCallCount = 0;
        _mockSpeciesClient
            .Setup(s => s.ListSpeciesByRealmAsync(It.IsAny<ListSpeciesByRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                speciesCallCount++;
                if (speciesCallCount == 1)
                {
                    return new SpeciesListResponse
                    {
                        Species = new List<SpeciesResponse>
                        {
                            new SpeciesResponse { SpeciesId = Guid.NewGuid(), Code = "HUMAN", Name = "Human" }
                        },
                        TotalCount = 1
                    };
                }
                return new SpeciesListResponse { Species = new List<SpeciesResponse>(), TotalCount = 0 };
            });

        // Locations: empty (no root locations)
        _mockLocationClient
            .Setup(l => l.ListRootLocationsAsync(It.IsAny<ListRootLocationsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationListResponse
            {
                Locations = new List<LocationResponse>(),
                TotalCount = 0,
                Page = 1,
                PageSize = 50,
                HasNextPage = false,
                HasPreviousPage = false
            });

        // Characters: 1 character, successfully migrated, then empty on second call
        var charCallCount = 0;
        _mockCharacterClient
            .Setup(c => c.GetCharactersByRealmAsync(It.IsAny<GetCharactersByRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                charCallCount++;
                if (charCallCount == 1)
                {
                    return new CharacterListResponse
                    {
                        Characters = new List<CharacterResponse>
                        {
                            new CharacterResponse
                            {
                                CharacterId = Guid.NewGuid(),
                                Name = "Hero",
                                RealmId = sourceId,
                                SpeciesId = Guid.NewGuid(),
                                BirthDate = DateTimeOffset.UtcNow,
                                Status = CharacterStatus.Alive,
                                CreatedAt = DateTimeOffset.UtcNow
                            }
                        },
                        TotalCount = 1,
                        Page = 1,
                        PageSize = 50,
                        HasNextPage = false,
                        HasPreviousPage = false
                    };
                }
                return new CharacterListResponse
                {
                    Characters = new List<CharacterResponse>(),
                    TotalCount = 0,
                    Page = 1,
                    PageSize = 50,
                    HasNextPage = false,
                    HasPreviousPage = false
                };
            });

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.SpeciesMigrated);
        Assert.Equal(0, response.SpeciesFailed);
        Assert.Equal(0, response.LocationsMigrated);
        Assert.Equal(0, response.LocationsFailed);
        Assert.Equal(1, response.CharactersMigrated);
        Assert.Equal(0, response.CharactersFailed);
    }

    [Fact]
    public async Task MergeRealmsAsync_WithDeleteAfterMerge_WhenNoFailures_ShouldDeleteSource()
    {
        // Arrange
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var request = new MergeRealmsRequest
        {
            SourceRealmId = sourceId,
            TargetRealmId = targetId,
            DeleteAfterMerge = true
        };

        var successLock = CreateMockLock(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(successLock.Object);

        var sourceModel = CreateTestRealmModel(sourceId, "SOURCE", isDeprecated: true);
        var targetModel = CreateTestRealmModel(targetId, "TARGET");

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{sourceId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceModel);
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{targetId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetModel);

        // All empty entities
        _mockSpeciesClient
            .Setup(s => s.ListSpeciesByRealmAsync(It.IsAny<ListSpeciesByRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeciesListResponse { Species = new List<SpeciesResponse>(), TotalCount = 0 });
        _mockLocationClient
            .Setup(l => l.ListRootLocationsAsync(It.IsAny<ListRootLocationsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationListResponse
            {
                Locations = new List<LocationResponse>(),
                TotalCount = 0,
                Page = 1,
                PageSize = 50,
                HasNextPage = false,
                HasPreviousPage = false
            });
        _mockCharacterClient
            .Setup(c => c.GetCharactersByRealmAsync(It.IsAny<GetCharactersByRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterListResponse
            {
                Characters = new List<CharacterResponse>(),
                TotalCount = 0,
                Page = 1,
                PageSize = 50,
                HasNextPage = false,
                HasPreviousPage = false
            });

        // Setup for delete operation (called after merge)
        // CheckReferences returns 404 (no references)
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<BeyondImmersion.BannouService.Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Setup for RemoveFromRealmListAsync
        _mockListStore
            .Setup(s => s.GetAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { sourceId });
        _mockListStore
            .Setup(s => s.GetWithETagAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)new List<Guid> { sourceId }, "list-etag"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(ALL_REALMS_KEY, It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("list-etag-2");

        // Act
        var (status, response) = await service.MergeRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.SourceDeleted);

        // Verify delete operations occurred
        _mockRealmStore.Verify(s => s.DeleteAsync(
            $"{REALM_KEY_PREFIX}{sourceId}", It.IsAny<CancellationToken>()), Times.Once);
        _mockStringStore.Verify(s => s.DeleteAsync(
            $"{CODE_INDEX_PREFIX}SOURCE", It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region SeedRealms Tests

    [Fact]
    public async Task SeedRealmsAsync_WhenEmptyList_ShouldReturnZeroCounts()
    {
        // Arrange
        var service = CreateService();
        var request = new SeedRealmsRequest
        {
            Realms = new List<SeedRealm>()
        };

        // Act
        var (status, response) = await service.SeedRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public async Task SeedRealmsAsync_WithNewRealm_ShouldCreateIt()
    {
        // Arrange
        var service = CreateService();
        var gameServiceId = Guid.NewGuid();
        var request = new SeedRealmsRequest
        {
            Realms = new List<SeedRealm>
            {
                new SeedRealm
                {
                    Code = "omega",
                    Name = "Omega",
                    GameServiceId = gameServiceId,
                    Description = "The Omega realm",
                    Category = "physical",
                    IsActive = true,
                    IsSystemType = false
                }
            }
        };

        // No existing realm for this code
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Setup for CreateRealmAsync
        _mockRealmStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");
        _mockListStore
            .Setup(s => s.GetWithETagAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)new List<Guid>(), "list-etag"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(ALL_REALMS_KEY, It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("list-etag-2");

        // Act
        var (status, response) = await service.SeedRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public async Task SeedRealmsAsync_WithExistingRealm_WhenUpdateExistingFalse_ShouldSkip()
    {
        // Arrange
        var service = CreateService();
        var existingId = Guid.NewGuid();
        var request = new SeedRealmsRequest
        {
            Realms = new List<SeedRealm>
            {
                new SeedRealm
                {
                    Code = "EXISTING",
                    Name = "Existing",
                    GameServiceId = Guid.NewGuid()
                }
            },
            UpdateExisting = false
        };

        // Code index returns existing ID
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}EXISTING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        // Act
        var (status, response) = await service.SeedRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(1, response.Skipped);
        Assert.Empty(response.Errors);

        // Verify no save was attempted
        _mockRealmStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<RealmModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedRealmsAsync_WithExistingRealm_WhenUpdateExistingTrue_ShouldUpdate()
    {
        // Arrange
        var service = CreateService();
        var existingId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();
        var request = new SeedRealmsRequest
        {
            Realms = new List<SeedRealm>
            {
                new SeedRealm
                {
                    Code = "EXISTING",
                    Name = "Updated Name",
                    GameServiceId = gameServiceId,
                    Category = "updated-category",
                    IsActive = true,
                    IsSystemType = false
                }
            },
            UpdateExisting = true
        };

        // Code index returns existing ID
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}EXISTING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        // Existing model with different values
        var existingModel = CreateTestRealmModel(existingId, "EXISTING", "Old Name");
        existingModel.GameServiceId = Guid.NewGuid(); // Different game service
        existingModel.Category = "old-category";
        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{existingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "mock-etag"));

        // Capture saved model
        RealmModel? savedModel = null;
        _mockRealmStore
            .Setup(s => s.TrySaveAsync(
                $"{REALM_KEY_PREFIX}{existingId}", It.IsAny<RealmModel>(), "mock-etag", It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, RealmModel, string, StateOptions?, CancellationToken>((_, m, _, _, _) => savedModel = m)
            .ReturnsAsync("new-etag");

        // Act
        var (status, response) = await service.SeedRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(1, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Errors);

        // Assert the model was updated
        Assert.NotNull(savedModel);
        Assert.Equal("Updated Name", savedModel.Name);
        Assert.Equal(gameServiceId, savedModel.GameServiceId);
        Assert.Equal("updated-category", savedModel.Category);
    }

    [Fact]
    public async Task SeedRealmsAsync_WithExistingRealmNoChanges_WhenUpdateExistingTrue_ShouldCountAsUpdated()
    {
        // Arrange
        var service = CreateService();
        var existingId = Guid.NewGuid();
        var gameServiceId = Guid.NewGuid();
        var request = new SeedRealmsRequest
        {
            Realms = new List<SeedRealm>
            {
                new SeedRealm
                {
                    Code = "EXISTING",
                    Name = "Same Name",
                    GameServiceId = gameServiceId,
                    IsActive = true,
                    IsSystemType = false
                }
            },
            UpdateExisting = true
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}EXISTING", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        // Existing model with SAME values
        var existingModel = CreateTestRealmModel(existingId, "EXISTING", "Same Name");
        existingModel.GameServiceId = gameServiceId;
        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{existingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "mock-etag"));

        // Act
        var (status, response) = await service.SeedRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert - no changes means seedUpdated = true (skips save but counts as updated)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(1, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Empty(response.Errors);

        // Verify no save was attempted (no changes to persist)
        _mockRealmStore.Verify(s => s.TrySaveAsync(
            It.IsAny<string>(), It.IsAny<RealmModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedRealmsAsync_WithETagConflict_ShouldRetryAndReportError()
    {
        // Arrange
        Configuration.OptimisticRetryAttempts = 2;
        var service = CreateService();
        var existingId = Guid.NewGuid();
        var request = new SeedRealmsRequest
        {
            Realms = new List<SeedRealm>
            {
                new SeedRealm
                {
                    Code = "CONFLICT",
                    Name = "Updated Name",
                    GameServiceId = Guid.NewGuid(),
                    IsActive = true,
                    IsSystemType = false
                }
            },
            UpdateExisting = true
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}CONFLICT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        // Return a fresh model on each call to avoid mutation across retries
        _mockRealmStore
            .Setup(s => s.GetWithETagAsync($"{REALM_KEY_PREFIX}{existingId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (CreateTestRealmModel(existingId, "CONFLICT", "Old Name"), "mock-etag"));

        // TrySaveAsync always returns null (ETag conflict)
        _mockRealmStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.SeedRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert - error reported for the conflict
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Single(response.Errors);
        Assert.Contains("concurrent modification", response.Errors.First());
    }

    [Fact]
    public async Task SeedRealmsAsync_PartialSuccess_ShouldReportMixedResults()
    {
        // Arrange
        var service = CreateService();
        var existingId = Guid.NewGuid();
        var request = new SeedRealmsRequest
        {
            Realms = new List<SeedRealm>
            {
                new SeedRealm
                {
                    Code = "new_realm",
                    Name = "New Realm",
                    GameServiceId = Guid.NewGuid(),
                    IsActive = true,
                    IsSystemType = false
                },
                new SeedRealm
                {
                    Code = "EXISTING_SKIP",
                    Name = "Existing",
                    GameServiceId = Guid.NewGuid(),
                    IsActive = true,
                    IsSystemType = false
                }
            },
            UpdateExisting = false
        };

        // First realm: code not found (create)
        // Second realm: code found (skip)
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}NEW_REALM", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}EXISTING_SKIP", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingId.ToString());

        // Setup for CreateRealmAsync (first realm)
        _mockRealmStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<RealmModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");
        _mockListStore
            .Setup(s => s.GetWithETagAsync(ALL_REALMS_KEY, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<Guid>?)new List<Guid>(), "list-etag"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(ALL_REALMS_KEY, It.IsAny<List<Guid>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("list-etag-2");

        // Act
        var (status, response) = await service.SeedRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(1, response.Skipped);
        Assert.Empty(response.Errors);
    }

    [Fact]
    public async Task SeedRealmsAsync_WhenExceptionOccurs_ShouldCollectError()
    {
        // Arrange
        var service = CreateService();
        var request = new SeedRealmsRequest
        {
            Realms = new List<SeedRealm>
            {
                new SeedRealm
                {
                    Code = "ERROR_REALM",
                    Name = "Error Realm",
                    GameServiceId = Guid.NewGuid(),
                    IsActive = true,
                    IsSystemType = false
                }
            }
        };

        // Code index check throws an exception
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}ERROR_REALM", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("State store failure"));

        // Act
        var (status, response) = await service.SeedRealmsAsync(request, TestContext.Current.CancellationToken);

        // Assert - error collected, not thrown
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(0, response.Created);
        Assert.Equal(0, response.Updated);
        Assert.Equal(0, response.Skipped);
        Assert.Single(response.Errors);
        Assert.Contains("ERROR_REALM", response.Errors.First());
        Assert.Contains("State store failure", response.Errors.First());
    }

    #endregion
}

/// <summary>
/// Tests for RealmServiceConfiguration binding and defaults.
/// </summary>
public class RealmConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        var config = new RealmServiceConfiguration();
        Assert.NotNull(config);
    }
}

/// <summary>
/// Tests for realm location compression context retrieval.
/// </summary>
public class RealmLocationCompressionTests : ServiceTestBase<RealmServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<RealmModel>> _mockRealmStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<Guid>>> _mockListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<RealmService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<ISpeciesClient> _mockSpeciesClient;
    private readonly Mock<ILocationClient> _mockLocationClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;
    private readonly Mock<IWorldstateClient> _mockWorldstateClient;

    private const string STATE_STORE = "realm-statestore";
    private const string REALM_KEY_PREFIX = "realm:";

    public RealmLocationCompressionTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockRealmStore = new Mock<IStateStore<RealmModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<RealmService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockSpeciesClient = new Mock<ISpeciesClient>();
        _mockLocationClient = new Mock<ILocationClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
        _mockWorldstateClient = new Mock<IWorldstateClient>();

        _mockStateStoreFactory.Setup(f => f.GetStore<RealmModel>(STATE_STORE)).Returns(_mockRealmStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<Guid>>(STATE_STORE)).Returns(_mockListStore.Object);

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private RealmService CreateService()
    {
        return new RealmService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object,
            _mockLockProvider.Object,
            _mockTelemetryProvider.Object,
            _mockResourceClient.Object,
            _mockSpeciesClient.Object,
            _mockLocationClient.Object,
            _mockCharacterClient.Object,
            _mockWorldstateClient.Object);
    }

    [Fact]
    public async Task GetLocationCompressContextAsync_ReturnsRealmContext()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        _mockLocationClient
            .Setup(c => c.GetLocationAsync(It.IsAny<GetLocationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResponse
            {
                LocationId = locationId,
                RealmId = realmId,
                Code = "CITY_A",
                Name = "City A",
                LocationType = LocationType.City,
                Depth = 1,
                IsDeprecated = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmModel
            {
                RealmId = realmId,
                Code = "OMEGA",
                Name = "Omega Realm",
                Description = "The primary realm",
                GameServiceId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var service = CreateService();

        // Act
        var (status, result) = await service.GetLocationCompressContextAsync(
            new GetLocationCompressContextRequest { LocationId = locationId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(result);
        Assert.Equal(locationId, result.ResourceId);
        Assert.Equal("location", result.ResourceType);
        Assert.Equal(realmId, result.RealmId);
        Assert.Equal("Omega Realm", result.RealmName);
        Assert.Equal("OMEGA", result.RealmCode);
        Assert.Equal("The primary realm", result.RealmDescription);
    }

    [Fact]
    public async Task GetLocationCompressContextAsync_WhenLocationNotFound_ReturnsNotFound()
    {
        // Arrange
        var locationId = Guid.NewGuid();

        _mockLocationClient
            .Setup(c => c.GetLocationAsync(It.IsAny<GetLocationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var service = CreateService();

        // Act
        var (status, result) = await service.GetLocationCompressContextAsync(
            new GetLocationCompressContextRequest { LocationId = locationId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLocationCompressContextAsync_WhenRealmNotFound_ReturnsNotFound()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        _mockLocationClient
            .Setup(c => c.GetLocationAsync(It.IsAny<GetLocationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LocationResponse
            {
                LocationId = locationId,
                RealmId = realmId,
                Code = "ORPHAN",
                Name = "Orphaned Location",
                LocationType = LocationType.Other,
                Depth = 0,
                IsDeprecated = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        var service = CreateService();

        // Act
        var (status, result) = await service.GetLocationCompressContextAsync(
            new GetLocationCompressContextRequest { LocationId = locationId }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(result);
    }
}
