using BeyondImmersion.Bannou.Core;
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
using BeyondImmersion.BannouService.TestUtilities;
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
    private readonly Mock<IStateStore<List<Guid>>> _mockListStore;
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
        _mockListStore = new Mock<IStateStore<List<Guid>>>();
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
            .Setup(f => f.GetStore<List<Guid>>(STATE_STORE))
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
    public void SpeciesService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<SpeciesService>();

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

        SpeciesModel? savedModel = null;
        string? savedKey = null;
        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SpeciesModel, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag-1");

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

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("ELF", response.Code);
        Assert.Equal("Elf", response.Name);

        // Assert - State was saved correctly
        Assert.NotNull(savedModel);
        Assert.NotNull(savedKey);
        Assert.StartsWith("species:", savedKey);
        Assert.Equal("ELF", savedModel.Code);
        Assert.Equal("Elf", savedModel.Name);
        Assert.Equal("A magical species", savedModel.Description);
        Assert.True(savedModel.IsPlayable);
        Assert.Equal(1000, savedModel.BaseLifespan);
        Assert.Equal(100, savedModel.MaturityAge);
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

        SpeciesCreatedEvent? capturedEvent = null;
        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesCreatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SpeciesCreatedEvent, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new CreateSpeciesRequest
        {
            Code = "DWARF",
            Name = "Dwarf"
        };

        // Act
        var (status, response) = await service.CreateSpeciesAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Assert - Event was published with correct content
        Assert.NotNull(capturedEvent);
        Assert.Equal("species.created", capturedTopic);
        Assert.Equal(response.SpeciesId, capturedEvent.SpeciesId);
        Assert.Equal("DWARF", capturedEvent.Code);
        Assert.Equal("Dwarf", capturedEvent.Name);
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

        SpeciesModel? savedModel = null;
        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SpeciesModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag-1");

        var request = new UpdateSpeciesRequest
        {
            SpeciesId = speciesId,
            Name = "High Elf",
            Description = "An updated description"
        };

        // Act
        var (status, response) = await service.UpdateSpeciesAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("High Elf", response.Name);

        // Assert - State was saved with updated values
        Assert.NotNull(savedModel);
        Assert.Equal("High Elf", savedModel.Name);
        Assert.Equal("An updated description", savedModel.Description);
        Assert.Equal("ELF", savedModel.Code); // Code should not change
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

        SpeciesUpdatedEvent? capturedEvent = null;
        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SpeciesUpdatedEvent, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new UpdateSpeciesRequest
        {
            SpeciesId = speciesId,
            Description = "Updated"
        };

        // Act
        var (status, _) = await service.UpdateSpeciesAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);

        // Assert - Event was published with correct content
        Assert.NotNull(capturedEvent);
        Assert.Equal("species.updated", capturedTopic);
        Assert.Equal(speciesId, capturedEvent.SpeciesId);
        Assert.Equal("ELF", capturedEvent.Code);
        Assert.Contains("description", capturedEvent.ChangedFields);
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
        model.IsDeprecated = true; // Service requires deprecation before deletion

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
            .ReturnsAsync((new List<Guid> { speciesId }, "etag-1"));

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
                It.IsAny<List<Guid>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-2");

        // Act
        var status = await service.DeleteSpeciesAsync(
            new DeleteSpeciesRequest { SpeciesId = speciesId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
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
            .ReturnsAsync((List<Guid>?)null);

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
        var speciesIds = new List<Guid> { speciesId1, speciesId2 };

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
        var speciesIds = new List<Guid> { speciesId1, speciesId2 };

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

        SpeciesModel? savedModel = null;
        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SpeciesModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag-1");

        // Setup realm index with ETag
        List<Guid>? savedRealmIndex = null;
        _mockListStore
            .Setup(s => s.GetWithETagAsync(
                It.Is<string>(k => k.StartsWith("realm-index:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<Guid>(), (string?)null));

        _mockListStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("realm-index:")),
                It.IsAny<List<Guid>>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, List<Guid>, StateOptions?, CancellationToken>((_, list, _, _) => savedRealmIndex = list)
            .ReturnsAsync("etag-1");

        var request = new AddSpeciesToRealmRequest
        {
            SpeciesId = speciesId,
            RealmId = realmId
        };

        // Act
        var (status, response) = await service.AddSpeciesToRealmAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Contains(realmId, response.RealmIds);

        // Assert - Species model was saved with realm added
        Assert.NotNull(savedModel);
        Assert.Contains(realmId, savedModel.RealmIds);

        // Assert - Realm index was updated
        Assert.NotNull(savedRealmIndex);
        Assert.Contains(speciesId, savedRealmIndex);
    }

    [Fact]
    public async Task RemoveSpeciesFromRealmAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var speciesId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var model = CreateTestSpeciesModel(speciesId, "ELF", "Elf");
        model.RealmIds.Add(realmId);

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
            .ReturnsAsync((new List<Guid> { speciesId }, "etag-1"));

        _mockListStore
            .Setup(s => s.SaveAsync(
                $"realm-index:{realmId}",
                It.IsAny<List<Guid>>(),
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

        SpeciesModel? savedModel = null;
        _mockSpeciesStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<SpeciesModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, SpeciesModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag-1");

        var request = new DeprecateSpeciesRequest
        {
            SpeciesId = speciesId,
            Reason = "No longer used"
        };

        // Act
        var (status, response) = await service.DeprecateSpeciesAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.IsDeprecated);

        // Assert - State was saved with deprecation fields set
        Assert.NotNull(savedModel);
        Assert.True(savedModel.IsDeprecated);
        Assert.NotNull(savedModel.DeprecatedAt);
        Assert.Equal("No longer used", savedModel.DeprecationReason);
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
        var registrationEvent = SpeciesPermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");

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
            SpeciesId = speciesId,
            Code = code,
            Name = name,
            Description = "Test species description",
            IsPlayable = true,
            IsDeprecated = false,
            RealmIds = new List<Guid>(),
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
            .ReturnsAsync((new List<Guid>(), (string?)null));

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
                It.IsAny<List<Guid>>(),
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
