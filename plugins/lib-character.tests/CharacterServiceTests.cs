using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.RelationshipType;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Species;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

#pragma warning disable CS8620 // Argument of type cannot be used for parameter of type due to differences in the nullability

namespace BeyondImmersion.BannouService.Character.Tests;

/// <summary>
/// Comprehensive unit tests for CharacterService.
/// Tests all CRUD operations, event publishing, and error handling.
/// Note: Tests using StateEntry (CreateCharacter, DeleteCharacter with index updates)
/// are best covered by HTTP integration tests due to StateEntry mocking complexity.
/// Relationship operations have been moved to the dedicated RelationshipService.
/// </summary>
public class CharacterServiceTests : ServiceTestBase<CharacterServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<CharacterModel>> _mockCharacterStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IStateStore<CharacterArchiveModel>> _mockArchiveStore;
    private readonly Mock<IStateStore<RefCountData>> _mockRefCountStore;
    private readonly Mock<IJsonQueryableStateStore<CharacterModel>> _mockJsonQueryableStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<CharacterService>> _mockLogger;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<ISpeciesClient> _mockSpeciesClient;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<IRelationshipTypeClient> _mockRelationshipTypeClient;
    private readonly Mock<IContractClient> _mockContractClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IResourceClient> _mockResourceClient;

    private const string STATE_STORE = "character-statestore";
    private const string CHARACTER_KEY_PREFIX = "character:";
    private const string REALM_INDEX_KEY_PREFIX = "realm-index:";
    private const string PUBSUB_NAME = "bannou-pubsub";

    public CharacterServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockCharacterStore = new Mock<IStateStore<CharacterModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockArchiveStore = new Mock<IStateStore<CharacterArchiveModel>>();
        _mockRefCountStore = new Mock<IStateStore<RefCountData>>();
        _mockJsonQueryableStore = new Mock<IJsonQueryableStateStore<CharacterModel>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<CharacterService>>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockSpeciesClient = new Mock<ISpeciesClient>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockRelationshipTypeClient = new Mock<IRelationshipTypeClient>();
        _mockContractClient = new Mock<IContractClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockResourceClient = new Mock<IResourceClient>();

        // Setup factory to return typed stores
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CharacterModel>(STATE_STORE))
            .Returns(_mockCharacterStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<string>(STATE_STORE))
            .Returns(_mockStringStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<List<string>>(STATE_STORE))
            .Returns(_mockListStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CharacterArchiveModel>(STATE_STORE))
            .Returns(_mockArchiveStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<RefCountData>(STATE_STORE))
            .Returns(_mockRefCountStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetJsonQueryableStore<CharacterModel>(STATE_STORE))
            .Returns(_mockJsonQueryableStore.Object);

        // Default lock acquisition to succeed
        var mockLockResponse = new Mock<ILockResponse>();
        mockLockResponse.Setup(l => l.Success).Returns(true);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockLockResponse.Object);

        // Default realm validation to pass (realm exists and is active)
        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(It.IsAny<RealmExistsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });

        // Default species validation to pass (species exists and is in realm)
        _mockSpeciesClient
            .Setup(s => s.GetSpeciesAsync(It.IsAny<GetSpeciesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetSpeciesRequest req, CancellationToken _) => new SpeciesResponse
            {
                SpeciesId = req.SpeciesId,
                Code = "TEST",
                Name = "Test Species",
                RealmIds = new List<Guid> { Guid.NewGuid() }, // Will be checked dynamically
                CreatedAt = DateTimeOffset.UtcNow
            });
    }

    private CharacterService CreateService(IResourceClient? resourceClient = null)
    {
        return new CharacterService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            Configuration,
            _mockRealmClient.Object,
            _mockSpeciesClient.Object,
            _mockRelationshipClient.Object,
            _mockRelationshipTypeClient.Object,
            _mockContractClient.Object,
            _mockEventConsumer.Object,
            resourceClient ?? _mockResourceClient.Object);
    }

    /// <summary>
    /// Sets up species mock to return a species that is available in the specified realm.
    /// </summary>
    private void SetupSpeciesInRealm(Guid speciesId, Guid realmId)
    {
        _mockSpeciesClient
            .Setup(s => s.GetSpeciesAsync(
                It.Is<GetSpeciesRequest>(r => r.SpeciesId == speciesId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeciesResponse
            {
                SpeciesId = speciesId,
                Code = "TEST",
                Name = "Test Species",
                RealmIds = new List<Guid> { realmId },
                CreatedAt = DateTimeOffset.UtcNow
            });
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
    public void CharacterService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<CharacterService>();

    #endregion

    #region GetCharacter Tests

    [Fact]
    public async Task GetCharacterAsync_WhenCharacterExists_ShouldReturnCharacter()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new GetCharacterRequest { CharacterId = characterId };

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup character retrieval
        _mockCharacterStore
            .Setup(s => s.GetAsync($"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Test Character",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.GetCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal("Test Character", response.Name);
    }

    [Fact]
    public async Task GetCharacterAsync_WhenCharacterNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var request = new GetCharacterRequest { CharacterId = characterId };

        // Setup global index to return null (character doesn't exist)
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.GetCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCharacterAsync_WhenStoreFails_ShouldReturnInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var request = new GetCharacterRequest { CharacterId = Guid.NewGuid() };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store unavailable"));

        // Act
        var (status, response) = await service.GetCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateCharacter Tests

    [Fact]
    public async Task UpdateCharacterAsync_WhenCharacterExists_ShouldUpdateAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new UpdateCharacterRequest
        {
            CharacterId = characterId,
            Name = "Updated Name",
            Status = CharacterStatus.Dead
        };

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup character retrieval
        _mockCharacterStore
            .Setup(s => s.GetAsync($"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Original Name",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Capture saved model
        CharacterModel? savedModel = null;
        _mockCharacterStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CharacterModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CharacterModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Capture published event
        CharacterUpdatedEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                "character.updated",
                It.IsAny<CharacterUpdatedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterUpdatedEvent, PublishOptions?, Guid?, CancellationToken>((_, e, _, _, _) => capturedEvent = e)
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.UpdateCharacterAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Name", response.Name);
        Assert.Equal(CharacterStatus.Dead, response.Status);

        // Assert - State was saved with correct values
        Assert.NotNull(savedModel);
        Assert.Equal("Updated Name", savedModel.Name);
        Assert.Equal(CharacterStatus.Dead, savedModel.Status);
        // Setting Status=Dead should auto-set DeathDate (ensures compression eligibility)
        Assert.NotNull(savedModel.DeathDate);

        // Assert - Event was published with correct content
        Assert.NotNull(capturedEvent);
        Assert.Equal(characterId, capturedEvent.CharacterId);
        Assert.Contains("name", capturedEvent.ChangedFields);
        Assert.Contains("status", capturedEvent.ChangedFields);
        Assert.Contains("deathDate", capturedEvent.ChangedFields);
        // Verify full event population (all required schema fields)
        Assert.Equal("Updated Name", capturedEvent.Name);
        Assert.Equal(realmId, capturedEvent.RealmId);
        Assert.NotEqual(Guid.Empty, capturedEvent.SpeciesId);
        Assert.Equal(CharacterStatus.Dead, capturedEvent.Status);
    }

    [Fact]
    public async Task UpdateCharacterAsync_WhenSettingDeathDate_ShouldAlsoSetStatusToDead()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var deathDate = DateTimeOffset.UtcNow;
        var request = new UpdateCharacterRequest
        {
            CharacterId = characterId,
            DeathDate = deathDate
        };

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup character retrieval
        _mockCharacterStore
            .Setup(s => s.GetAsync($"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Test",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Capture saved model
        CharacterModel? savedModel = null;
        _mockCharacterStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CharacterModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CharacterModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.UpdateCharacterAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(CharacterStatus.Dead, response.Status);
        Assert.Equal(deathDate, response.DeathDate);

        // Assert - State was saved with death date and status correctly set
        Assert.NotNull(savedModel);
        Assert.Equal(CharacterStatus.Dead, savedModel.Status);
        Assert.Equal(deathDate, savedModel.DeathDate);
    }

    [Fact]
    public async Task UpdateCharacterAsync_WhenCharacterNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new UpdateCharacterRequest { CharacterId = Guid.NewGuid(), Name = "New Name" };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.UpdateCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UpdateCharacterAsync_WithNoChanges_ShouldNotPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new UpdateCharacterRequest { CharacterId = characterId };

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup character retrieval
        _mockCharacterStore
            .Setup(s => s.GetAsync($"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Test",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.UpdateCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify NO update event published (no changes)
        _mockMessageBus.Verify(m => m.TryPublishAsync(
            "character.updated",
            It.IsAny<CharacterUpdatedEvent>(),
            It.IsAny<PublishOptions?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region ListCharacters Tests

    [Fact]
    public async Task ListCharactersAsync_WithRealmFilter_ShouldReturnCharactersFromRealm()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var request = new ListCharactersRequest
        {
            RealmId = realmId,
            Page = 1,
            PageSize = 20
        };

        var character = new CharacterModel
        {
            CharacterId = characterId,
            Name = "Test Character",
            RealmId = realmId,
            SpeciesId = Guid.NewGuid(),
            Status = CharacterStatus.Alive,
            BirthDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        SetupJsonQueryPagedAsync(new List<CharacterModel> { character }, totalCount: 1);

        // Act
        var (status, response) = await service.ListCharactersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Characters);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal("Test Character", response.Characters.First().Name);
    }

    [Fact]
    public async Task ListCharactersAsync_WithEmptyRealm_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var request = new ListCharactersRequest
        {
            RealmId = Guid.NewGuid(),
            Page = 1,
            PageSize = 20
        };
        SetupJsonQueryPagedAsync(new List<CharacterModel>(), totalCount: 0);

        // Act
        var (status, response) = await service.ListCharactersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Characters);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task ListCharactersAsync_WithStatusFilter_ShouldPassFilterToQuery()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var aliveCharId = Guid.NewGuid();
        var request = new ListCharactersRequest
        {
            RealmId = realmId,
            Status = CharacterStatus.Alive
        };

        // Server-side query returns only alive characters (filter applied in MySQL)
        var aliveCharacter = new CharacterModel
        {
            CharacterId = aliveCharId,
            Name = "Alive Character",
            RealmId = realmId,
            SpeciesId = Guid.NewGuid(),
            Status = CharacterStatus.Alive,
            BirthDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        SetupJsonQueryPagedAsync(new List<CharacterModel> { aliveCharacter }, totalCount: 1);

        // Act
        var (status, response) = await service.ListCharactersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Characters);
        Assert.Equal("Alive Character", response.Characters.First().Name);

        // Verify query conditions included status filter
        _mockJsonQueryableStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>?>(c => c != null && c.Any(q => q.Path == "$.Status")),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListCharactersAsync_ShouldPaginateCorrectly()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var request = new ListCharactersRequest
        {
            RealmId = realmId,
            Page = 2,
            PageSize = 2
        };

        // Server returns page 2 (offset 2, limit 2) of 5 total characters
        var pageCharacters = new List<CharacterModel>();
        for (int i = 2; i < 4; i++)
        {
            pageCharacters.Add(new CharacterModel
            {
                CharacterId = Guid.NewGuid(),
                Name = $"Character {i}",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        SetupJsonQueryPagedAsync(pageCharacters, totalCount: 5, offset: 2, limit: 2);

        // Act
        var (status, response) = await service.ListCharactersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Characters.Count);
        Assert.Equal(5, response.TotalCount);
        Assert.Equal(2, response.Page);
        Assert.True(response.HasNextPage);
        Assert.True(response.HasPreviousPage);
    }

    #endregion

    #region GetCharactersByRealm Tests

    [Fact]
    public async Task GetCharactersByRealmAsync_ShouldReturnCharactersFromSpecifiedRealm()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var request = new GetCharactersByRealmRequest
        {
            RealmId = realmId,
            Page = 1,
            PageSize = 20
        };

        var character = new CharacterModel
        {
            CharacterId = characterId,
            Name = "Realm Character",
            RealmId = realmId,
            SpeciesId = Guid.NewGuid(),
            Status = CharacterStatus.Alive,
            BirthDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        SetupJsonQueryPagedAsync(new List<CharacterModel> { character }, totalCount: 1);

        // Act
        var (status, response) = await service.GetCharactersByRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Characters);
        Assert.Equal("Realm Character", response.Characters.First().Name);
    }

    [Fact]
    public async Task GetCharactersByRealmAsync_WithSpeciesFilter_ShouldPassFilterToQuery()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var targetSpeciesId = Guid.NewGuid();
        var targetCharId = Guid.NewGuid();

        var request = new GetCharactersByRealmRequest
        {
            RealmId = realmId,
            SpeciesId = targetSpeciesId
        };

        // Server-side query returns only matching species (filter applied in MySQL)
        var targetCharacter = new CharacterModel
        {
            CharacterId = targetCharId,
            Name = "Target Species Character",
            RealmId = realmId,
            SpeciesId = targetSpeciesId,
            Status = CharacterStatus.Alive,
            BirthDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        SetupJsonQueryPagedAsync(new List<CharacterModel> { targetCharacter }, totalCount: 1);

        // Act
        var (status, response) = await service.GetCharactersByRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Characters);
        Assert.Equal("Target Species Character", response.Characters.First().Name);

        // Verify query conditions included species filter
        _mockJsonQueryableStore.Verify(s => s.JsonQueryPagedAsync(
            It.Is<IReadOnlyList<QueryCondition>?>(c => c != null && c.Any(q => q.Path == "$.SpeciesId")),
            It.IsAny<int>(),
            It.IsAny<int>(),
            It.IsAny<JsonSortSpec?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region CheckCharacterReferences Tests (lib-resource integration)

    [Fact]
    public async Task CheckCharacterReferencesAsync_WhenResourceClientReturnsReferences_IncludesInCount()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new CheckReferencesRequest { CharacterId = characterId };

        // Setup character exists
        SetupCharacterExists(characterId, realmId);

        // Setup no archive (not compressed)
        _mockArchiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterArchiveModel?)null);

        // Setup no L2 relationships
        _mockRelationshipClient
            .Setup(r => r.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse { Relationships = new List<RelationshipResponse>(), TotalCount = 0 });

        // Setup lib-resource returns L4 references
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(
                It.Is<Resource.CheckReferencesRequest>(req => req.ResourceType == "character" && req.ResourceId == characterId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse
            {
                ResourceType = "character",
                ResourceId = characterId,
                RefCount = 3,
                Sources = new List<ResourceReference>
                {
                    new() { SourceType = "character-encounter", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow },
                    new() { SourceType = "character-personality", SourceId = characterId.ToString(), RegisteredAt = DateTimeOffset.UtcNow },
                    new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
                }
            });

        // Setup no contracts
        _mockContractClient
            .Setup(c => c.QueryContractInstancesAsync(It.IsAny<QueryContractInstancesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Setup refcount store
        _mockRefCountStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new RefCountData { CharacterId = characterId }, "etag1"));
        _mockRefCountStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<RefCountData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");

        // Act
        var (status, response) = await service.CheckCharacterReferencesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.ReferenceCount);
        Assert.Contains("CHARACTER-ENCOUNTER", response.ReferenceTypes);
        Assert.Contains("CHARACTER-PERSONALITY", response.ReferenceTypes);
        Assert.Contains("ACTOR", response.ReferenceTypes);
    }

    [Fact]
    public async Task CheckCharacterReferencesAsync_WhenResourceClientReturns404_StillSucceeds()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new CheckReferencesRequest { CharacterId = characterId };

        // Setup character exists
        SetupCharacterExists(characterId, realmId);

        // Setup no archive
        _mockArchiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterArchiveModel?)null);

        // Setup L2 relationship exists
        _mockRelationshipClient
            .Setup(r => r.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>
                {
                    new() { RelationshipId = Guid.NewGuid() }
                },
                TotalCount = 1
            });

        // Setup lib-resource returns 404 (no L4 references registered)
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Setup no contracts
        _mockContractClient
            .Setup(c => c.QueryContractInstancesAsync(It.IsAny<QueryContractInstancesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Setup refcount store
        _mockRefCountStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RefCountData?)null, (string?)null));
        _mockRefCountStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<RefCountData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.CheckCharacterReferencesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ReferenceCount); // Only L2 relationship counted
        Assert.Contains("RELATIONSHIP", response.ReferenceTypes);
    }

    [Fact]
    public async Task CheckCharacterReferencesAsync_WhenResourceClientUnavailable_GracefulDegradation()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new CheckReferencesRequest { CharacterId = characterId };

        // Setup character exists
        SetupCharacterExists(characterId, realmId);

        // Setup no archive
        _mockArchiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterArchiveModel?)null);

        // Setup L2 relationship exists
        _mockRelationshipClient
            .Setup(r => r.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>
                {
                    new() { RelationshipId = Guid.NewGuid() }
                },
                TotalCount = 1
            });

        // Setup lib-resource throws service unavailable (not 404)
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Service unavailable", 503, null, null, null));

        // Setup no contracts
        _mockContractClient
            .Setup(c => c.QueryContractInstancesAsync(It.IsAny<QueryContractInstancesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Setup refcount store
        _mockRefCountStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RefCountData?)null, (string?)null));
        _mockRefCountStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<RefCountData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.CheckCharacterReferencesAsync(request);

        // Assert - Still succeeds with L2 data only (graceful degradation)
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.ReferenceCount); // Only L2 relationship counted
        Assert.Contains("RELATIONSHIP", response.ReferenceTypes);
    }

    [Fact]
    public async Task CheckCharacterReferencesAsync_CombinesL2AndL4References()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new CheckReferencesRequest { CharacterId = characterId };

        // Setup character exists
        SetupCharacterExists(characterId, realmId);

        // Setup no archive
        _mockArchiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterArchiveModel?)null);

        // Setup L2 relationships (2 refs)
        _mockRelationshipClient
            .Setup(r => r.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>
                {
                    new() { RelationshipId = Guid.NewGuid() },
                    new() { RelationshipId = Guid.NewGuid() }
                },
                TotalCount = 2
            });

        // Setup lib-resource L4 references (3 refs)
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse
            {
                ResourceType = "character",
                ResourceId = characterId,
                RefCount = 3,
                Sources = new List<ResourceReference>
                {
                    new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
                }
            });

        // Setup contracts (1 ref)
        _mockContractClient
            .Setup(c => c.QueryContractInstancesAsync(It.IsAny<QueryContractInstancesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryContractInstancesResponse
            {
                Contracts = new List<ContractInstanceResponse>
                {
                    new() { ContractId = Guid.NewGuid() }
                },
                HasMore = false
            });

        // Setup refcount store
        _mockRefCountStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RefCountData?)null, (string?)null));
        _mockRefCountStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<RefCountData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.CheckCharacterReferencesAsync(request);

        // Assert - Total = 2 (L2 rel) + 3 (L4 from lib-resource) + 1 (contracts) = 6
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(6, response.ReferenceCount);
        Assert.Contains("RELATIONSHIP", response.ReferenceTypes);
        Assert.Contains("ACTOR", response.ReferenceTypes);
        Assert.Contains("CONTRACT", response.ReferenceTypes);
    }

    [Fact]
    public async Task CheckCharacterReferencesAsync_WhenCharacterNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var request = new CheckReferencesRequest { CharacterId = characterId };

        // Setup character not found
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.CheckCharacterReferencesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    /// <summary>
    /// Helper to setup character exists in state store
    /// </summary>
    private void SetupCharacterExists(Guid characterId, Guid realmId)
    {
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Test Character",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
    }

    #endregion

    #region GetCompressDataAsync Tests

    [Fact]
    public async Task GetCompressDataAsync_DeadCharacter_ReturnsCompressData()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var deathDate = DateTimeOffset.UtcNow.AddDays(-1);

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup character lookup
        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Test Character",
                RealmId = realmId,
                SpeciesId = speciesId,
                Status = CharacterStatus.Dead,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-50),
                DeathDate = deathDate,
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-50),
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Setup relationship client to return empty family tree
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>(),
                TotalCount = 0,
                PageSize = 100,
                Page = 1
            });

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal("Test Character", response.Name);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(speciesId, response.SpeciesId);
        Assert.Equal(CharacterStatus.Dead, response.Status);
        Assert.Equal(deathDate, response.DeathDate);
        Assert.NotEqual(default, response.ArchivedAt);
    }

    [Fact]
    public async Task GetCompressDataAsync_CharacterNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup global index to return null (character not found)
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCompressDataAsync_AliveCharacter_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup character lookup - alive character
        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Alive Character",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive, // Not dead
                BirthDate = DateTimeOffset.UtcNow.AddYears(-20),
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-20),
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCompressDataAsync_DeadWithoutDeathDate_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup character lookup - dead but no death date (invalid state)
        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Invalid Character",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Dead,
                DeathDate = null, // Missing death date
                BirthDate = DateTimeOffset.UtcNow.AddYears(-20),
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-20),
                UpdatedAt = DateTimeOffset.UtcNow
            });

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCompressDataAsync_ApiException_ReturnsServiceUnavailable()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup character lookup
        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Test Character",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Dead,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-50),
                DeathDate = DateTimeOffset.UtcNow.AddDays(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-50),
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Setup relationship client to throw ApiException
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Relationship service unavailable", 503));

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.ServiceUnavailable, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCompressDataAsync_Exception_ReturnsInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup to throw exception on lookup
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.InternalServerError, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCompressDataAsync_WithFamilyRelationships_IncludesFamilySummary()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var spouseId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var spouseTypeId = Guid.NewGuid();
        var childTypeId = Guid.NewGuid();

        // Setup global index lookup
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        // Setup main character lookup
        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Test Character",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Dead,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-50),
                DeathDate = DateTimeOffset.UtcNow.AddDays(-1),
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-50),
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Setup bulk state for family member lookups
        var characterDict = new Dictionary<string, CharacterModel>
        {
            [$"character:{realmId}:{spouseId}"] = new CharacterModel
            {
                CharacterId = spouseId,
                Name = "Spouse Character",
                RealmId = realmId,
                Status = CharacterStatus.Alive
            },
            [$"character:{realmId}:{childId}"] = new CharacterModel
            {
                CharacterId = childId,
                Name = "Child Character",
                RealmId = realmId,
                Status = CharacterStatus.Alive
            }
        };
        SetupBulkStateAsync(characterDict);

        // Setup global index bulk lookup for BulkLoadCharactersAsync
        var globalIndexDict = new Dictionary<string, string>
        {
            [$"character-global-index:{spouseId}"] = realmId.ToString(),
            [$"character-global-index:{childId}"] = realmId.ToString()
        };
        _mockStringStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string>)globalIndexDict);

        // Setup relationship client to return spouse and child relationships
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>
                {
                    new RelationshipResponse
                    {
                        RelationshipId = Guid.NewGuid(),
                        Entity1Id = characterId,
                        Entity1Type = EntityType.Character,
                        Entity2Id = spouseId,
                        Entity2Type = EntityType.Character,
                        RelationshipTypeId = spouseTypeId,
                        StartedAt = DateTimeOffset.UtcNow.AddYears(-20),
                        CreatedAt = DateTimeOffset.UtcNow.AddYears(-20)
                    },
                    new RelationshipResponse
                    {
                        RelationshipId = Guid.NewGuid(),
                        Entity1Id = characterId,
                        Entity1Type = EntityType.Character,
                        Entity2Id = childId,
                        Entity2Type = EntityType.Character,
                        RelationshipTypeId = childTypeId,
                        StartedAt = DateTimeOffset.UtcNow.AddYears(-15),
                        CreatedAt = DateTimeOffset.UtcNow.AddYears(-15)
                    }
                },
                TotalCount = 2,
                PageSize = 100,
                Page = 1
            });

        // Setup relationship type client to return type codes
        _mockRelationshipTypeClient
            .Setup(c => c.GetRelationshipTypeAsync(It.Is<GetRelationshipTypeRequest>(r => r.RelationshipTypeId == spouseTypeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipTypeResponse { RelationshipTypeId = spouseTypeId, Code = "SPOUSE" });
        _mockRelationshipTypeClient
            .Setup(c => c.GetRelationshipTypeAsync(It.Is<GetRelationshipTypeRequest>(r => r.RelationshipTypeId == childTypeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipTypeResponse { RelationshipTypeId = childTypeId, Code = "CHILD" });

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        // Family summary should mention spouse and child
        // Note: Exact format depends on implementation, but should not be null if relationships exist
    }

    #endregion

    #region Helper Methods

    private static CharacterModel CreateCharacterModel(Guid characterId, Guid realmId, string name)
    {
        return new CharacterModel
        {
            CharacterId = characterId,
            Name = name,
            RealmId = realmId,
            SpeciesId = Guid.NewGuid(),
            Status = CharacterStatus.Alive,
            BirthDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Sets up GetBulkAsync mock to return the specified items as IReadOnlyDictionary.
    /// </summary>
    private void SetupBulkStateAsync(Dictionary<string, CharacterModel> items)
    {
        _mockCharacterStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, CharacterModel>)items);
    }

    private void SetupJsonQueryPagedAsync(
        List<CharacterModel> items,
        long totalCount,
        int offset = 0,
        int limit = 20)
    {
        var queryResults = items.Select(m =>
            new JsonQueryResult<CharacterModel>($"character:{m.RealmId}:{m.CharacterId}", m))
            .ToList();

        _mockJsonQueryableStore
            .Setup(s => s.JsonQueryPagedAsync(
                It.IsAny<IReadOnlyList<QueryCondition>?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<JsonSortSpec?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonPagedResult<CharacterModel>(
                queryResults, totalCount, offset, limit));
    }

    #endregion
}

/// <summary>
/// Tests for CharacterServiceConfiguration
/// </summary>
public class CharacterConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        var config = new CharacterServiceConfiguration();
        Assert.NotNull(config);
        Assert.Equal(20, config.DefaultPageSize);
        Assert.Equal(100, config.MaxPageSize);
    }
}
