using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
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
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<ILogger<RealmService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

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
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockLogger = new Mock<ILogger<RealmService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RealmModel>(STATE_STORE))
            .Returns(_mockRealmStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockListStore.Object);
    }

    private RealmService CreateService()
    {
        return new RealmService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object);
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
            RealmId = id.ToString(),
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

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        var service = CreateService();
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            null!,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            _mockStateStoreFactory.Object,
            null!,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            null!,
            Configuration,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            null!,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullEventConsumer_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            null!));
    }

    #endregion

    #region GetRealm Tests

    [Fact]
    public async Task GetRealmAsync_WhenRealmExists_ShouldReturnRealm()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new GetRealmRequest { RealmId = realmId };
        var testModel = CreateTestRealmModel(realmId, "OMEGA", "Omega Realm");

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.GetRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal("OMEGA", response.Code);
        Assert.Equal("Omega Realm", response.Name);
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
        var (status, response) = await service.GetRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetRealmAsync_WhenStoreFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new GetRealmRequest { RealmId = realmId };

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store connection failed"));

        // Act
        var (status, response) = await service.GetRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "realm", "GetRealm", "unexpected_exception", It.IsAny<string>(),
            It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<string?>(), default), Times.Once);
    }

    #endregion

    #region GetRealmByCode Tests

    [Fact]
    public async Task GetRealmByCodeAsync_WhenCodeExists_ShouldReturnRealm()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var code = "ARCADIA";
        var request = new GetRealmByCodeRequest { Code = code };
        var testModel = CreateTestRealmModel(realmId, code, "Arcadia Realm");

        // Setup code index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup realm retrieval
        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(testModel);

        // Act
        var (status, response) = await service.GetRealmByCodeAsync(request);

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
        var (status, response) = await service.GetRealmByCodeAsync(request);

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
        var (status, response) = await service.GetRealmByCodeAsync(request);

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
        var (status, response) = await service.RealmExistsAsync(request);

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
        var (status, response) = await service.RealmExistsAsync(request);

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
        var (status, response) = await service.RealmExistsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Exists);
        Assert.False(response.IsActive); // Deprecated realms are not active
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
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.UpdateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Name", response.Name);

        // Verify save was called
        _mockRealmStore.Verify(s => s.SaveAsync(
            $"{REALM_KEY_PREFIX}{realmId}", It.IsAny<RealmModel>(), null, It.IsAny<CancellationToken>()), Times.Once);

        // Verify event was published via IMessageBus
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm.updated", It.IsAny<RealmUpdatedEvent>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateRealmAsync_WhenRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new UpdateRealmRequest { RealmId = realmId, Name = "New Name" };

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.UpdateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);

        // Verify no save or event publishing occurred
        _mockRealmStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<RealmModel>(), null, It.IsAny<CancellationToken>()), Times.Never);
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
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.UpdateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify no event was published (no changes)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            It.IsAny<string>(), It.IsAny<object>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var status = await service.DeleteRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteRealmAsync_WhenRealmNotDeprecated_ShouldReturnConflict()
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
        var status = await service.DeleteRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);

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
            .ReturnsAsync(new List<string> { realmId.ToString() });

        // Act
        var status = await service.DeleteRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NoContent, status);

        // Verify delete operations
        _mockRealmStore.Verify(s => s.DeleteAsync(
            $"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()), Times.Once);
        _mockStringStore.Verify(s => s.DeleteAsync(
            $"{CODE_INDEX_PREFIX}DELETED", It.IsAny<CancellationToken>()), Times.Once);

        // Verify event was published via IMessageBus
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm.deleted", It.IsAny<RealmDeletedEvent>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
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
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.DeprecateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeprecateRealmAsync_WhenAlreadyDeprecated_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new DeprecateRealmRequest { RealmId = realmId, Reason = "Test" };
        var existingModel = CreateTestRealmModel(realmId, isDeprecated: true);

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.DeprecateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
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
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.DeprecateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
        Assert.Equal("No longer needed", response.DeprecationReason);
        Assert.NotNull(response.DeprecatedAt);

        // Verify event was published via IMessageBus
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm.updated", It.IsAny<RealmUpdatedEvent>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
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
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.UndeprecateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task UndeprecateRealmAsync_WhenNotDeprecated_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new UndeprecateRealmRequest { RealmId = realmId };
        var existingModel = CreateTestRealmModel(realmId, isDeprecated: false);

        _mockRealmStore
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.UndeprecateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
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
            .Setup(s => s.GetAsync($"{REALM_KEY_PREFIX}{realmId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.UndeprecateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsDeprecated);
        Assert.Null(response.DeprecationReason);
        Assert.Null(response.DeprecatedAt);

        // Verify event was published via IMessageBus
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "realm.updated", It.IsAny<RealmUpdatedEvent>(), It.IsAny<PublishOptions?>(), It.IsAny<CancellationToken>()), Times.Once);
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
