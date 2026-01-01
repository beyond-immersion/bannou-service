using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
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
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<SpeciesModel>> _mockSpeciesStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<SpeciesService>> _mockLogger;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    private const string STATE_STORE = "species-statestore";

    public SpeciesServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockSpeciesStore = new Mock<IStateStore<SpeciesModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<SpeciesService>>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup default factory returns
        _mockStateStoreFactory
            .Setup(f => f.GetStore<SpeciesModel>(STATE_STORE))
            .Returns(_mockSpeciesStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockListStore.Object);

        // Default realm validation to pass (realm exists and is active)
        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });

        // Default character validation to pass (no characters using species)
        _mockCharacterClient
            .Setup(c => c.ListCharactersAsync(It.IsAny<ListCharactersRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterListResponse { Characters = new List<CharacterResponse>(), TotalCount = 0 });

        _mockCharacterClient
            .Setup(c => c.GetCharactersByRealmAsync(It.IsAny<GetCharactersByRealmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterListResponse { Characters = new List<CharacterResponse>(), TotalCount = 0 });
    }

    private SpeciesService CreateService()
    {
        return new SpeciesService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
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
    public void Constructor_WithNullStateStoreFactory_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            null!,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            _mockCharacterClient.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullMessageBus_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockStateStoreFactory.Object,
            null!,
            _mockLogger.Object,
            Configuration,
            _mockCharacterClient.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            null!,
            Configuration,
            _mockCharacterClient.Object,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
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
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
            null!,
            _mockRealmClient.Object,
            _mockEventConsumer.Object));
    }

    [Fact]
    public void Constructor_WithNullRealmClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SpeciesService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLogger.Object,
            Configuration,
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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SpeciesModel?)null);

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

        _mockStringStore
            .Setup(s => s.GetAsync(
                "code-index:HUMAN",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(speciesId.ToString());

        var model = CreateTestSpeciesModel(speciesId, code, "Human");

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
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

        _mockStringStore
            .Setup(s => s.GetAsync(
                "code-index:UNKNOWN",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

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
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "species.created",
            It.IsAny<SpeciesCreatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SpeciesModel?)null);

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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new UpdateSpeciesRequest
        {
            SpeciesId = speciesId,
            Description = "Updated"
        };

        // Act
        var (status, _) = await service.UpdateSpeciesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "species.updated",
            It.IsAny<SpeciesUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region DeleteSpecies Tests

    [Fact]
    public async Task DeleteSpeciesAsync_ExistingSpecies_ReturnsNoContent()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "TEST", "Test Species");

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Setup all-species list with ETag
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "all-species",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { speciesId.ToString() }, "etag-1"));

        _mockSpeciesStore
            .Setup(s => s.DeleteAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockStringStore
            .Setup(s => s.DeleteAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockListStore
            .Setup(s => s.SaveAsync(
                "all-species",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var status = await service.DeleteSpeciesAsync(
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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((SpeciesModel?)null);

        // Act
        var status = await service.DeleteSpeciesAsync(
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

        _mockListStore
            .Setup(s => s.GetAsync(
                "all-species",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

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
        var speciesId1 = Guid.NewGuid();
        var speciesId2 = Guid.NewGuid();
        var speciesIds = new List<string> { speciesId1.ToString(), speciesId2.ToString() };

        _mockListStore
            .Setup(s => s.GetAsync(
                "all-species",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(speciesIds);

        var model1 = CreateTestSpeciesModel(speciesId1, "SPEC0", "Species 0");
        var model2 = CreateTestSpeciesModel(speciesId2, "SPEC1", "Species 1");

        // Setup bulk get
        _mockSpeciesStore
            .Setup(s => s.GetBulkAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SpeciesModel>
            {
                { $"species:{speciesId1}", model1 },
                { $"species:{speciesId2}", model2 }
            });

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

        _mockListStore
            .Setup(s => s.GetAsync(
                "all-species",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(speciesIds);

        var model1 = CreateTestSpeciesModel(speciesId1, "PLAY", "Playable Species");
        model1.IsPlayable = true;
        var model2 = CreateTestSpeciesModel(speciesId2, "NPC", "NPC Species");
        model2.IsPlayable = false;

        _mockSpeciesStore
            .Setup(s => s.GetBulkAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SpeciesModel>
            {
                { $"species:{speciesId1}", model1 },
                { $"species:{speciesId2}", model2 }
            });

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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Setup realm index with ETag
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                It.Is<string>(k => k.StartsWith("realm-index:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        _mockListStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("realm-index:")),
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Setup realm index with ETag
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                $"realm-index:{realmId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { speciesId.ToString() }, "etag-1"));

        _mockListStore
            .Setup(s => s.SaveAsync(
                $"realm-index:{realmId}",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

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

        _mockSpeciesStore
            .Setup(s => s.GetAsync(
                $"species:{speciesId}",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

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
        var instanceId = Guid.NewGuid();
        var registrationEvent = SpeciesPermissionRegistration.CreateRegistrationEvent(instanceId);

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("species", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
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
        _mockStringStore
            .Setup(s => s.GetAsync(
                It.Is<string>(k => k.StartsWith("code-index:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(codeExists ? Guid.NewGuid().ToString() : null);

        // Setup all-species list with ETag
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                "all-species",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string>(), (string?)null));

        // Setup saves
        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockStringStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockListStore
            .Setup(s => s.SaveAsync(
                "all-species",
                It.IsAny<List<string>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
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
