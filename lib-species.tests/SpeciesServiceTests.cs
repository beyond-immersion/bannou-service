using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.Testing;
using Dapr.Client;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Species.Tests;

/// <summary>
/// Unit tests for SpeciesService.
/// Tests species management operations including realm associations and merge functionality.
/// </summary>
public class SpeciesServiceTests : ServiceTestBase<SpeciesServiceConfiguration>
{
    private readonly Mock<DaprClient> _mockDaprClient;
    private readonly Mock<ILogger<SpeciesService>> _mockLogger;
    private readonly Mock<IErrorEventEmitter> _mockErrorEventEmitter;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    public SpeciesServiceTests()
    {
        _mockDaprClient = new Mock<DaprClient>();
        _mockLogger = new Mock<ILogger<SpeciesService>>();
        _mockErrorEventEmitter = new Mock<IErrorEventEmitter>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Default realm validation to pass (realm exists and is active)
        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });
    }

    private SpeciesService CreateService()
    {
        return new SpeciesService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockCharacterClient.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object);
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
    public void Constructor_WithNullDaprClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            null!,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockCharacterClient.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockDaprClient.Object,
            null!,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockCharacterClient.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            null!,
            _mockErrorEventEmitter.Object,
            _mockCharacterClient.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullErrorEventEmitter_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            null!,
            _mockCharacterClient.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullCharacterClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            null!,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullRealmClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockDaprClient.Object,
            _mockLogger.Object,
            Configuration,
            _mockErrorEventEmitter.Object,
            _mockCharacterClient.Object,
            null!,
            _mockEventConsumer.Object));
    }

    #endregion

    #region GetSpecies Tests

    [Fact]
    public async Task GetSpeciesAsync_ExistingSpecies_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "TEST001", "Test Species");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetSpeciesAsync(
            new GetSpeciesRequest { SpeciesId = speciesId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(speciesId, response.SpeciesId);
        Assert.Equal("TEST001", response.Code);
    }

    [Fact]
    public async Task GetSpeciesAsync_NonExistentSpecies_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<SpeciesModel?>(null));

        // Act
        var (status, response) = await service.GetSpeciesAsync(
            new GetSpeciesRequest { SpeciesId = speciesId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetSpeciesByCode Tests

    [Fact]
    public async Task GetSpeciesByCodeAsync_ExistingCode_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var code = "HUMAN";

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                "species-statestore",
                "code-index:HUMAN",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(speciesId.ToString());

        var model = CreateTestSpeciesModel(speciesId, code, "Human");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetSpeciesByCodeAsync(
            new GetSpeciesByCodeRequest { Code = code });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(code, response.Code);
    }

    [Fact]
    public async Task GetSpeciesByCodeAsync_NonExistentCode_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                "species-statestore",
                "code-index:UNKNOWN",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<string?>(null));

        // Act
        var (status, response) = await service.GetSpeciesByCodeAsync(
            new GetSpeciesByCodeRequest { Code = "UNKNOWN" });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region CreateSpecies Tests

    [Fact]
    public async Task CreateSpeciesAsync_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var service = CreateService();
        SetupCreateSpeciesMocks(codeExists: false);

        var request = new CreateSpeciesRequest
        {
            Code = "ELF",
            Name = "Elf",
            Description = "A magical species",
            IsPlayable = true,
            BaseLifespan = 1000,
            MaturityAge = 100
        };

        // Act
        var (status, response) = await service.CreateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        Assert.NotNull(response);
        Assert.Equal("ELF", response.Code);
        Assert.Equal("Elf", response.Name);
    }

    [Fact]
    public async Task CreateSpeciesAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        SetupCreateSpeciesMocks(codeExists: true);

        var request = new CreateSpeciesRequest
        {
            Code = "ELF",
            Name = "Elf"
        };

        // Act
        var (status, response) = await service.CreateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateSpeciesAsync_PublishesCreatedEvent()
    {
        // Arrange
        var service = CreateService();
        SetupCreateSpeciesMocks(codeExists: false);

        var request = new CreateSpeciesRequest
        {
            Code = "DWARF",
            Name = "Dwarf"
        };

        // Act
        var (status, _) = await service.CreateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Created, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "species.created",
            It.IsAny<SpeciesCreatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region UpdateSpecies Tests

    [Fact]
    public async Task UpdateSpeciesAsync_ExistingSpecies_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "ELF", "Elf");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UpdateSpeciesRequest
        {
            SpeciesId = speciesId,
            Name = "High Elf",
            Description = "An updated description"
        };

        // Act
        var (status, response) = await service.UpdateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("High Elf", response.Name);
    }

    [Fact]
    public async Task UpdateSpeciesAsync_NonExistentSpecies_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<SpeciesModel?>(null));

        var request = new UpdateSpeciesRequest
        {
            SpeciesId = speciesId,
            Name = "Updated Name"
        };

        // Act
        var (status, response) = await service.UpdateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateSpeciesAsync_PublishesUpdatedEvent()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "ELF", "Elf");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UpdateSpeciesRequest
        {
            SpeciesId = speciesId,
            Description = "Updated"
        };

        // Act
        var (status, _) = await service.UpdateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockDaprClient.Verify(d => d.PublishEventAsync(
            "bannou-pubsub",
            "species.updated",
            It.IsAny<SpeciesUpdatedEvent>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteSpecies Tests

    [Fact]
    public async Task DeleteSpeciesAsync_ExistingSpecies_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "TEST", "Test Species");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Setup all-species list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "species-statestore",
                "all-species",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { speciesId.ToString() });

        // Act
        var (status, response) = await service.DeleteSpeciesAsync(
            new DeleteSpeciesRequest { SpeciesId = speciesId });

        // Assert
        Assert.Equal(StatusCodes.NoContent, status);
    }

    [Fact]
    public async Task DeleteSpeciesAsync_NonExistentSpecies_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<SpeciesModel?>(null));

        // Act
        var (status, response) = await service.DeleteSpeciesAsync(
            new DeleteSpeciesRequest { SpeciesId = speciesId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region ListSpecies Tests

    [Fact]
    public async Task ListSpeciesAsync_NoSpecies_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "species-statestore",
                "all-species",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<List<string>?>(null));

        // Act
        var (status, response) = await service.ListSpeciesAsync(new ListSpeciesRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Species);
    }

    [Fact]
    public async Task ListSpeciesAsync_WithSpecies_ReturnsList()
    {
        // Arrange
        var service = CreateService();
        var speciesIds = new List<string> { Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "species-statestore",
                "all-species",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(speciesIds);

        var bulkResults = speciesIds.Select((id, idx) => new BulkStateItem(
            $"species:{id}",
            BannouJson.Serialize(CreateTestSpeciesModel(Guid.Parse(id), $"SPEC{idx}", $"Species {idx}")),
            "etag")).ToList();

        _mockDaprClient
            .Setup(d => d.GetBulkStateAsync(
                "species-statestore",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<int?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListSpeciesAsync(new ListSpeciesRequest { IncludeDeprecated = true });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Species.Count);
    }

    [Fact]
    public async Task ListSpeciesAsync_FiltersPlayable_WhenRequested()
    {
        // Arrange
        var service = CreateService();
        var speciesId1 = Guid.NewGuid();
        var speciesId2 = Guid.NewGuid();
        var speciesIds = new List<string> { speciesId1.ToString(), speciesId2.ToString() };

        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "species-statestore",
                "all-species",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(speciesIds);

        var model1 = CreateTestSpeciesModel(speciesId1, "PLAY", "Playable Species");
        model1.IsPlayable = true;
        var model2 = CreateTestSpeciesModel(speciesId2, "NPC", "NPC Species");
        model2.IsPlayable = false;

        var bulkResults = new List<BulkStateItem>
        {
            new($"species:{speciesId1}", BannouJson.Serialize(model1), "etag"),
            new($"species:{speciesId2}", BannouJson.Serialize(model2), "etag")
        };

        _mockDaprClient
            .Setup(d => d.GetBulkStateAsync(
                "species-statestore",
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<int?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(bulkResults);

        // Act
        var (status, response) = await service.ListSpeciesAsync(
            new ListSpeciesRequest { IsPlayable = true });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Species);
        Assert.True(response.Species.First().IsPlayable);
    }

    #endregion

    #region Realm Association Tests

    [Fact]
    public async Task AddSpeciesToRealmAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "ELF", "Elf");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Setup realm index
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "species-statestore",
                It.Is<string>(k => k.StartsWith("realm-idx:")),
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var request = new AddSpeciesToRealmRequest
        {
            SpeciesId = speciesId,
            RealmId = realmId
        };

        // Act
        var (status, response) = await service.AddSpeciesToRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Contains(realmId, response.RealmIds);
    }

    [Fact]
    public async Task RemoveSpeciesFromRealmAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "ELF", "Elf");
        model.RealmIds.Add(realmId.ToString());

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Setup realm index
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "species-statestore",
                $"realm-idx:{realmId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { speciesId.ToString() });

        var request = new RemoveSpeciesFromRealmRequest
        {
            SpeciesId = speciesId,
            RealmId = realmId
        };

        // Act
        var (status, response) = await service.RemoveSpeciesFromRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.DoesNotContain(realmId, response.RealmIds);
    }

    #endregion

    #region Deprecation Tests

    [Fact]
    public async Task DeprecateSpeciesAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "OLD", "Old Species");

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new DeprecateSpeciesRequest
        {
            SpeciesId = speciesId,
            Reason = "No longer used"
        };

        // Act
        var (status, response) = await service.DeprecateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);
    }

    [Fact]
    public async Task UndeprecateSpeciesAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "OLD", "Old Species");
        model.IsDeprecated = true;
        model.DeprecatedAt = DateTimeOffset.UtcNow;

        _mockDaprClient
            .Setup(d => d.GetStateAsync<SpeciesModel>(
                "species-statestore",
                $"species:{speciesId}",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        var request = new UndeprecateSpeciesRequest { SpeciesId = speciesId };

        // Act
        var (status, response) = await service.UndeprecateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.IsDeprecated);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void SpeciesPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = SpeciesPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
    }

    [Fact]
    public void SpeciesPermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Act
        var registrationEvent = SpeciesPermissionRegistration.CreateRegistrationEvent();

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("species", registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.EventId);
        Assert.NotNull(registrationEvent.Endpoints);
    }

    [Fact]
    public void SpeciesPermissionRegistration_ServiceId_ShouldBeSpecies()
    {
        // Assert
        Assert.Equal("species", SpeciesPermissionRegistration.ServiceId);
    }

    #endregion

    #region Helper Methods

    private static SpeciesModel CreateTestSpeciesModel(Guid speciesId, string code, string name)
    {
        return new SpeciesModel
        {
            SpeciesId = speciesId.ToString(),
            Code = code,
            Name = name,
            Description = "Test species description",
            IsPlayable = true,
            IsDeprecated = false,
            RealmIds = new List<string>(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private void SetupCreateSpeciesMocks(bool codeExists)
    {
        // Setup code existence check
        _mockDaprClient
            .Setup(d => d.GetStateAsync<string>(
                "species-statestore",
                It.Is<string>(k => k.StartsWith("code-index:")),
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<string?>(codeExists ? Guid.NewGuid().ToString() : null));

        // Setup all-species list
        _mockDaprClient
            .Setup(d => d.GetStateAsync<List<string>>(
                "species-statestore",
                "all-species",
                It.IsAny<ConsistencyMode?>(),
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
    }

    #endregion
}

public class SpeciesConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new SpeciesServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }
}
