using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Testing;
using Dapr.Client;
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
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<RealmService>> _mockLogger;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;

    private const string STATE_STORE = "realm-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string REALM_KEY_PREFIX = "realm:";
    private const string CODE_INDEX_PREFIX = "code-index:";
    private const string ALL_REALMS_KEY = "all-realms";

    public RealmServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<RealmService>>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
    }

    private RealmService CreateService()
    {
        return new RealmService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object);
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
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            null!,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            _mockDaprClient.Object,
            null!,
            Configuration,
            _mockErrorEventEmitter.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new RealmService(
            _mockDaprClient.Object,
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.GetRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetRealmAsync_WhenDaprFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new GetRealmRequest { RealmId = realmId };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ThrowsAsync(new Exception("Dapr connection failed"));

        // Act
        var (status, response) = await service.GetRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
        _mockErrorEventEmitter.Verify(e => e.TryPublishAsync(
            "realm", "GetRealm", "unexpected_exception", It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                STATE_STORE, $"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}", null, null, default))
            .ReturnsAsync(realmId.ToString());

        // Setup realm retrieval
        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                STATE_STORE, $"{CODE_INDEX_PREFIX}NONEXISTENT", null, null, default))
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
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                STATE_STORE, $"{CODE_INDEX_PREFIX}{code.ToUpperInvariant()}", null, null, default))
            .ReturnsAsync(realmId.ToString());

        // But realm data is missing
        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.UpdateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Name", response.Name);

        // Verify save was called
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", It.IsAny<RealmModel>(), null, null, default), Times.Once);

        // Verify event was published
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            PUBSUB_NAME, "realm.updated", It.IsAny<RealmUpdatedEvent>(), default), Times.Once);
    }

    [Fact]
    public async Task UpdateRealmAsync_WhenRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new UpdateRealmRequest { RealmId = realmId, Name = "New Name" };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.UpdateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);

        // Verify no save or event publishing occurred
        _mockDaprClient.Verify(d => d.SaveStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RealmModel>(), null, null, default), Times.Never);
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.UpdateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify no event was published (no changes)
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<object>(), default), Times.Never);
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync((RealmModel?)null);

        // Act
        var (status, response) = await service.DeleteRealmAsync(request);

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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.DeleteRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);

        // Verify no delete occurred
        _mockDaprClient.Verify(d => d.DeleteStateAsync(
            It.IsAny<string>(), It.IsAny<string>(), null, null, default), Times.Never);
    }

    [Fact]
    public async Task DeleteRealmAsync_WhenDeprecatedRealm_ShouldDeleteAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new DeleteRealmRequest { RealmId = realmId };
        var existingModel = CreateTestRealmModel(realmId, "DELETED", isDeprecated: true);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync(existingModel);

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                STATE_STORE, ALL_REALMS_KEY, null, null, default))
            .ReturnsAsync(new List<string> { realmId.ToString() });

        // Act
        var (status, response) = await service.DeleteRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NoContent, status);

        // Verify delete operations
        _mockDaprClient.Verify(d => d.DeleteStateAsync(
            STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default), Times.Once);
        _mockDaprClient.Verify(d => d.DeleteStateAsync(
            STATE_STORE, $"{CODE_INDEX_PREFIX}DELETED", null, null, default), Times.Once);

        // Verify event was published
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            PUBSUB_NAME, "realm.deleted", It.IsAny<RealmDeletedEvent>(), default), Times.Once);
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.DeprecateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
        Assert.Equal("No longer needed", response.DeprecationReason);
        Assert.NotNull(response.DeprecatedAt);

        // Verify event was published
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            PUBSUB_NAME, "realm.updated", It.IsAny<RealmUpdatedEvent>(), default), Times.Once);
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
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

        _mockDaprClient
            .Setup(d => d.GetStateAsync<RealmModel>(
                STATE_STORE, $"{REALM_KEY_PREFIX}{realmId}", null, null, default))
            .ReturnsAsync(existingModel);

        // Act
        var (status, response) = await service.UndeprecateRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsDeprecated);
        Assert.Null(response.DeprecationReason);
        Assert.Null(response.DeprecatedAt);

        // Verify event was published
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            PUBSUB_NAME, "realm.updated", It.IsAny<RealmUpdatedEvent>(), default), Times.Once);
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
