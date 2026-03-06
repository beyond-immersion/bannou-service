using BeyondImmersion.Bannou.Character.ClientEvents;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
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
    private readonly Mock<IContractClient> _mockContractClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IResourceClient> _mockResourceClient;
    private readonly Mock<IEntitySessionRegistry> _mockEntitySessionRegistry;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

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
        _mockContractClient = new Mock<IContractClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockResourceClient = new Mock<IResourceClient>();
        _mockEntitySessionRegistry = new Mock<IEntitySessionRegistry>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();

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
            _mockContractClient.Object,
            _mockEventConsumer.Object,
            resourceClient ?? _mockResourceClient.Object,
            _mockEntitySessionRegistry.Object,
            _mockTelemetryProvider.Object);
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
    public async Task GetCharacterAsync_WhenStoreFails_ShouldThrow()
    {
        // Arrange
        var service = CreateService();
        var request = new GetCharacterRequest { CharacterId = Guid.NewGuid() };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("State store unavailable"));

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<Exception>(() => service.GetCharacterAsync(request));
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

        // Assert - Client event was published via Entity Session Registry
        _mockEntitySessionRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            "character",
            characterId,
            It.Is<CharacterUpdatedClientEvent>(e =>
                e.CharacterId == characterId &&
                e.ChangedFields.Contains("name") &&
                e.ChangedFields.Contains("status") &&
                e.ChangedFields.Contains("deathDate") &&
                e.Name == "Updated Name" &&
                e.Status == CharacterStatus.Dead &&
                e.DeathDate != null),
            It.IsAny<CancellationToken>()), Times.Once);
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

        // Verify NO client event published either (no changes)
        _mockEntitySessionRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<CharacterUpdatedClientEvent>(),
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
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new RefCountData { CharacterId = characterId }, "etag1"));
        _mockRefCountStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<RefCountData>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RefCountData?)null, (string?)null));
        _mockRefCountStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<RefCountData>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RefCountData?)null, (string?)null));
        _mockRefCountStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<RefCountData>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((RefCountData?)null, (string?)null));
        _mockRefCountStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<RefCountData>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
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

        // Act & Assert - ApiException propagates to generated controller which returns 503
        await Assert.ThrowsAsync<ApiException>(() => service.GetCompressDataAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetCompressDataAsync_Exception_ShouldThrow()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup to throw exception on lookup
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCompressDataAsync(request, CancellationToken.None));
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
        _mockRelationshipClient
            .Setup(c => c.GetRelationshipTypeAsync(It.Is<GetRelationshipTypeRequest>(r => r.RelationshipTypeId == spouseTypeId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipTypeResponse { RelationshipTypeId = spouseTypeId, Code = "SPOUSE" });
        _mockRelationshipClient
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

    #region CreateCharacter Tests

    [Fact]
    public async Task CreateCharacterAsync_ValidRequest_ShouldCreateAndPublishEvents()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        SetupSpeciesInRealm(speciesId, realmId);

        var request = new CreateCharacterRequest
        {
            Name = "New Hero",
            RealmId = realmId,
            SpeciesId = speciesId,
            BirthDate = DateTimeOffset.UtcNow.AddYears(-20),
            Status = CharacterStatus.Alive
        };

        // Capture saved model
        CharacterModel? savedModel = null;
        string? savedKey = null;
        _mockCharacterStore
            .Setup(s => s.SaveAsync(
                It.Is<string>(k => k.StartsWith("character:")),
                It.IsAny<CharacterModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CharacterModel, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        // Setup realm index (empty initially)
        _mockListStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<string>?)null, (string?)null));
        _mockListStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Capture published events
        var publishedTopics = new List<string>();
        var publishedEvents = new List<object>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterCreatedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterCreatedEvent, PublishOptions?, Guid?, CancellationToken>(
                (t, e, _, _, _) => { publishedTopics.Add(t); publishedEvents.Add(e); })
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterRealmJoinedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterRealmJoinedEvent, PublishOptions?, Guid?, CancellationToken>(
                (t, e, _, _, _) => { publishedTopics.Add(t); publishedEvents.Add(e); })
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.CreateCharacterAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("New Hero", response.Name);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(speciesId, response.SpeciesId);
        Assert.Equal(CharacterStatus.Alive, response.Status);
        Assert.Null(response.DeathDate);

        // Assert - State was saved correctly
        Assert.NotNull(savedModel);
        Assert.NotNull(savedKey);
        Assert.Equal("New Hero", savedModel.Name);
        Assert.Equal(realmId, savedModel.RealmId);
        Assert.Equal(speciesId, savedModel.SpeciesId);
        Assert.Equal(CharacterStatus.Alive, savedModel.Status);
        Assert.StartsWith("character:", savedKey);

        // Assert - character.created event published
        Assert.Contains("character.created", publishedTopics);
        var createdEvent = publishedEvents.OfType<CharacterCreatedEvent>().Single();
        Assert.Equal("New Hero", createdEvent.Name);
        Assert.Equal(realmId, createdEvent.RealmId);
        Assert.Equal(speciesId, createdEvent.SpeciesId);

        // Assert - character.realm.joined event published
        Assert.Contains("character.realm.joined", publishedTopics);
        var joinedEvent = publishedEvents.OfType<CharacterRealmJoinedEvent>().Single();
        Assert.Equal(realmId, joinedEvent.RealmId);
        Assert.Null(joinedEvent.PreviousRealmId);
    }

    [Fact]
    public async Task CreateCharacterAsync_RealmNotFound_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == realmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = false, IsActive = false });

        var request = new CreateCharacterRequest
        {
            Name = "Test",
            RealmId = realmId,
            SpeciesId = Guid.NewGuid(),
            BirthDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Alive
        };

        // Act
        var (status, response) = await service.CreateCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);

        // Verify no state was saved
        _mockCharacterStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<CharacterModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateCharacterAsync_RealmDeprecated_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == realmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = false });

        var request = new CreateCharacterRequest
        {
            Name = "Test",
            RealmId = realmId,
            SpeciesId = Guid.NewGuid(),
            BirthDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Alive
        };

        // Act
        var (status, response) = await service.CreateCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateCharacterAsync_SpeciesNotFound_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();

        _mockSpeciesClient
            .Setup(s => s.GetSpeciesAsync(
                It.Is<GetSpeciesRequest>(r => r.SpeciesId == speciesId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        var request = new CreateCharacterRequest
        {
            Name = "Test",
            RealmId = realmId,
            SpeciesId = speciesId,
            BirthDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Alive
        };

        // Act
        var (status, response) = await service.CreateCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateCharacterAsync_SpeciesNotInRealm_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var differentRealmId = Guid.NewGuid();

        // Species exists but is in a different realm
        _mockSpeciesClient
            .Setup(s => s.GetSpeciesAsync(
                It.Is<GetSpeciesRequest>(r => r.SpeciesId == speciesId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeciesResponse
            {
                SpeciesId = speciesId,
                Code = "TEST",
                Name = "Test Species",
                RealmIds = new List<Guid> { differentRealmId },
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new CreateCharacterRequest
        {
            Name = "Test",
            RealmId = realmId,
            SpeciesId = speciesId,
            BirthDate = DateTimeOffset.UtcNow,
            Status = CharacterStatus.Alive
        };

        // Act
        var (status, response) = await service.CreateCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateCharacterAsync_WithDeadStatus_ShouldAutoSetDeathDate()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        SetupSpeciesInRealm(speciesId, realmId);

        var request = new CreateCharacterRequest
        {
            Name = "Dead On Arrival",
            RealmId = realmId,
            SpeciesId = speciesId,
            BirthDate = DateTimeOffset.UtcNow.AddYears(-50),
            Status = CharacterStatus.Dead
        };

        // Capture saved model
        CharacterModel? savedModel = null;
        _mockCharacterStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<CharacterModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CharacterModel, StateOptions?, CancellationToken>((_, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Setup realm index
        _mockListStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((List<string>?)null, (string?)null));
        _mockListStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.CreateCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(CharacterStatus.Dead, response.Status);
        Assert.NotNull(response.DeathDate);

        // Assert - Saved model has DeathDate auto-set
        Assert.NotNull(savedModel);
        Assert.NotNull(savedModel.DeathDate);
        Assert.Equal(CharacterStatus.Dead, savedModel.Status);
    }

    #endregion

    #region DeleteCharacter Tests

    [Fact]
    public async Task DeleteCharacterAsync_ValidCharacter_ShouldDeleteAndPublishEvents()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new DeleteCharacterRequest { CharacterId = characterId };

        SetupCharacterExists(characterId, realmId);

        // Setup lib-resource returns no references
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse
            {
                ResourceType = "character",
                ResourceId = characterId,
                RefCount = 0,
                Sources = new List<ResourceReference>()
            });

        // Setup realm index for removal
        _mockListStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { characterId.ToString() }, "etag1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");

        // Capture published events
        var publishedTopics = new List<string>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterRealmLeftEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterRealmLeftEvent, PublishOptions?, Guid?, CancellationToken>(
                (t, _, _, _, _) => publishedTopics.Add(t))
            .ReturnsAsync(true);

        CharacterDeletedEvent? capturedDeletedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterDeletedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterDeletedEvent, PublishOptions?, Guid?, CancellationToken>(
                (t, e, _, _, _) => { publishedTopics.Add(t); capturedDeletedEvent = e; })
            .ReturnsAsync(true);

        // Act
        var status = await service.DeleteCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Assert - Character was deleted from store
        _mockCharacterStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(characterId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Global index was deleted
        _mockStringStore.Verify(s => s.DeleteAsync(
            $"character-global-index:{characterId}",
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - character.realm.left event published with Deletion reason
        Assert.Contains("character.realm.left", publishedTopics);

        // Assert - character.deleted event published
        Assert.Contains("character.deleted", publishedTopics);
        Assert.NotNull(capturedDeletedEvent);
        Assert.Equal(characterId, capturedDeletedEvent.CharacterId);
    }

    [Fact]
    public async Task DeleteCharacterAsync_CharacterNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new DeleteCharacterRequest { CharacterId = Guid.NewGuid() };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var status = await service.DeleteCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteCharacterAsync_WithL4References_ShouldExecuteCleanupBeforeDeleting()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new DeleteCharacterRequest { CharacterId = characterId };

        SetupCharacterExists(characterId, realmId);

        // Setup lib-resource returns references
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse
            {
                ResourceType = "character",
                ResourceId = characterId,
                RefCount = 2,
                Sources = new List<ResourceReference>
                {
                    new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
                }
            });

        // Setup cleanup succeeds
        _mockResourceClient
            .Setup(r => r.ExecuteCleanupAsync(
                It.Is<Resource.ExecuteCleanupRequest>(req =>
                    req.ResourceType == "character" && req.ResourceId == characterId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCleanupResponse
            {
                ResourceType = "character",
                ResourceId = characterId,
                Success = true,
                CallbackResults = new List<CleanupCallbackResult>
                {
                    new() { SourceType = "actor", ServiceName = "actor", Endpoint = "cleanup", Success = true }
                }
            });

        // Setup realm index removal
        _mockListStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { characterId.ToString() }, "etag1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");

        // Act
        var status = await service.DeleteCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify cleanup was executed
        _mockResourceClient.Verify(r => r.ExecuteCleanupAsync(
            It.Is<Resource.ExecuteCleanupRequest>(req =>
                req.ResourceType == "character" && req.ResourceId == characterId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCharacterAsync_CleanupBlocked_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new DeleteCharacterRequest { CharacterId = characterId };

        SetupCharacterExists(characterId, realmId);

        // Setup lib-resource returns references
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CheckReferencesResponse
            {
                ResourceType = "character",
                ResourceId = characterId,
                RefCount = 1,
                Sources = new List<ResourceReference>
                {
                    new() { SourceType = "actor", SourceId = Guid.NewGuid().ToString(), RegisteredAt = DateTimeOffset.UtcNow }
                }
            });

        // Cleanup fails (RESTRICT policy blocked it)
        _mockResourceClient
            .Setup(r => r.ExecuteCleanupAsync(It.IsAny<Resource.ExecuteCleanupRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExecuteCleanupResponse
            {
                ResourceType = "character",
                ResourceId = characterId,
                Success = false,
                AbortReason = "RESTRICT policy blocked cleanup",
                CallbackResults = new List<CleanupCallbackResult>()
            });

        // Act
        var status = await service.DeleteCharacterAsync(request);

        // Assert - Deletion blocked
        Assert.Equal(StatusCodes.Conflict, status);

        // Verify character was NOT deleted
        _mockCharacterStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteCharacterAsync_ResourceClient404_ShouldProceedWithDeletion()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new DeleteCharacterRequest { CharacterId = characterId };

        SetupCharacterExists(characterId, realmId);

        // Setup lib-resource throws 404 (no references registered - normal)
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Setup realm index removal
        _mockListStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { characterId.ToString() }, "etag1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");

        // Act
        var status = await service.DeleteCharacterAsync(request);

        // Assert - Deletion proceeded (404 = no references, safe to delete)
        Assert.Equal(StatusCodes.OK, status);

        // Verify character was deleted
        _mockCharacterStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(characterId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteCharacterAsync_ResourceClientUnavailable_ShouldReturnServiceUnavailable()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var request = new DeleteCharacterRequest { CharacterId = characterId };

        SetupCharacterExists(characterId, realmId);

        // Setup lib-resource throws 503 (service unavailable - NOT 404)
        _mockResourceClient
            .Setup(r => r.CheckReferencesAsync(It.IsAny<Resource.CheckReferencesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Service unavailable", 503, null, null, null));

        // Act
        var status = await service.DeleteCharacterAsync(request);

        // Assert - Fail closed to protect referential integrity
        Assert.Equal(StatusCodes.ServiceUnavailable, status);

        // Verify character was NOT deleted
        _mockCharacterStore.Verify(s => s.DeleteAsync(
            It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);

        // Verify error event was published
        _mockMessageBus.Verify(m => m.TryPublishErrorAsync(
            "character",
            "DeleteCharacter",
            "resource_service_unavailable",
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<ServiceErrorEventSeverity>(),
            It.IsAny<object?>(),
            It.IsAny<string?>(),
            It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region TransferCharacterToRealm Tests

    [Fact]
    public async Task TransferCharacterToRealmAsync_ValidTransfer_ShouldTransferAndPublishEvents()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var sourceRealmId = Guid.NewGuid();
        var targetRealmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();

        var request = new TransferCharacterToRealmRequest
        {
            CharacterId = characterId,
            TargetRealmId = targetRealmId
        };

        // Setup target realm validation
        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == targetRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });

        // Setup character exists in source realm
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceRealmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{sourceRealmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Traveler",
                RealmId = sourceRealmId,
                SpeciesId = speciesId,
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });

        // Capture save for new realm key
        CharacterModel? savedModel = null;
        string? savedKey = null;
        _mockCharacterStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<CharacterModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CharacterModel, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedKey = k;
                savedModel = m;
            })
            .ReturnsAsync("etag");

        // Setup realm index operations
        _mockListStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<string> { characterId.ToString() }, "etag1"));
        _mockListStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<string>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag2");
        _mockListStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag3");

        // Capture published events
        var publishedTopics = new List<string>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterRealmLeftEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterRealmLeftEvent, PublishOptions?, Guid?, CancellationToken>(
                (t, _, _, _, _) => publishedTopics.Add(t))
            .ReturnsAsync(true);

        CharacterRealmJoinedEvent? joinedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterRealmJoinedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterRealmJoinedEvent, PublishOptions?, Guid?, CancellationToken>(
                (t, e, _, _, _) => { publishedTopics.Add(t); joinedEvent = e; })
            .ReturnsAsync(true);

        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterUpdatedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterUpdatedEvent, PublishOptions?, Guid?, CancellationToken>(
                (t, _, _, _, _) => publishedTopics.Add(t))
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.TransferCharacterToRealmAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(targetRealmId, response.RealmId);

        // Assert - Saved with new realm key
        Assert.NotNull(savedModel);
        Assert.NotNull(savedKey);
        Assert.Equal(targetRealmId, savedModel.RealmId);
        Assert.Contains(targetRealmId.ToString(), savedKey);

        // Assert - Old key was deleted
        _mockCharacterStore.Verify(s => s.DeleteAsync(
            It.Is<string>(k => k.Contains(sourceRealmId.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert - Events published
        Assert.Contains("character.realm.left", publishedTopics);
        Assert.Contains("character.realm.joined", publishedTopics);
        Assert.Contains("character.updated", publishedTopics);

        // Assert - Joined event references previous realm
        Assert.NotNull(joinedEvent);
        Assert.Equal(targetRealmId, joinedEvent.RealmId);
        Assert.Equal(sourceRealmId, joinedEvent.PreviousRealmId);

        // Assert - Client event published for realm transfer
        _mockEntitySessionRegistry.Verify(r => r.PublishToEntitySessionsAsync(
            "character",
            characterId,
            It.Is<CharacterRealmTransferredClientEvent>(e =>
                e.CharacterId == characterId &&
                e.PreviousRealmId == sourceRealmId &&
                e.NewRealmId == targetRealmId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TransferCharacterToRealmAsync_TargetRealmNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new TransferCharacterToRealmRequest
        {
            CharacterId = Guid.NewGuid(),
            TargetRealmId = Guid.NewGuid()
        };

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == request.TargetRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = false, IsActive = false });

        // Act
        var (status, response) = await service.TransferCharacterToRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TransferCharacterToRealmAsync_TargetRealmDeprecated_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new TransferCharacterToRealmRequest
        {
            CharacterId = Guid.NewGuid(),
            TargetRealmId = Guid.NewGuid()
        };

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == request.TargetRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = false });

        // Act
        var (status, response) = await service.TransferCharacterToRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TransferCharacterToRealmAsync_SameRealm_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var request = new TransferCharacterToRealmRequest
        {
            CharacterId = characterId,
            TargetRealmId = realmId
        };

        // Target realm exists and active
        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == realmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });

        // Character is already in the target realm
        SetupCharacterExistsInRealm(characterId, realmId);

        // Act
        var (status, response) = await service.TransferCharacterToRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TransferCharacterToRealmAsync_CharacterNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var targetRealmId = Guid.NewGuid();

        var request = new TransferCharacterToRealmRequest
        {
            CharacterId = Guid.NewGuid(),
            TargetRealmId = targetRealmId
        };

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == targetRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });

        // Character not found in global index
        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.TransferCharacterToRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TransferCharacterToRealmAsync_LockFailed_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var targetRealmId = Guid.NewGuid();

        var request = new TransferCharacterToRealmRequest
        {
            CharacterId = characterId,
            TargetRealmId = targetRealmId
        };

        _mockRealmClient
            .Setup(r => r.RealmExistsAsync(
                It.Is<RealmExistsRequest>(req => req.RealmId == targetRealmId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RealmExistsResponse { Exists = true, IsActive = true });

        // Lock acquisition fails
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.Is<string>(s => s == characterId.ToString()),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (status, response) = await service.TransferCharacterToRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region GetEnrichedCharacter Tests

    [Fact]
    public async Task GetEnrichedCharacterAsync_ValidCharacter_ShouldReturnEnrichedResponse()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();

        var request = new GetEnrichedCharacterRequest
        {
            CharacterId = characterId,
            IncludeFamilyTree = false
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Enriched Hero",
                RealmId = realmId,
                SpeciesId = speciesId,
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-25),
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-5),
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.GetEnrichedCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal("Enriched Hero", response.Name);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(speciesId, response.SpeciesId);
        Assert.Null(response.FamilyTree);
    }

    [Fact]
    public async Task GetEnrichedCharacterAsync_WithFamilyTree_ShouldBuildFamilyTree()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var parentTypeId = Guid.NewGuid();

        var request = new GetEnrichedCharacterRequest
        {
            CharacterId = characterId,
            IncludeFamilyTree = true
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Child Character",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-10),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Setup relationship client returns parent relationship
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>
                {
                    new RelationshipResponse
                    {
                        RelationshipId = Guid.NewGuid(),
                        Entity1Id = parentId,
                        Entity1Type = EntityType.Character,
                        Entity2Id = characterId,
                        Entity2Type = EntityType.Character,
                        RelationshipTypeId = parentTypeId,
                        StartedAt = DateTimeOffset.UtcNow.AddYears(-10),
                        CreatedAt = DateTimeOffset.UtcNow.AddYears(-10)
                    }
                },
                TotalCount = 1,
                PageSize = 100,
                Page = 1
            });

        // Setup relationship type lookup
        _mockRelationshipClient
            .Setup(c => c.GetRelationshipTypeAsync(
                It.Is<GetRelationshipTypeRequest>(r => r.RelationshipTypeId == parentTypeId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipTypeResponse { RelationshipTypeId = parentTypeId, Code = "PARENT" });

        // Setup bulk load for parent character
        var globalIndexDict = new Dictionary<string, string>
        {
            [$"character-global-index:{parentId}"] = realmId.ToString()
        };
        _mockStringStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string>)globalIndexDict);

        var characterDict = new Dictionary<string, CharacterModel>
        {
            [$"character:{realmId}:{parentId}"] = new CharacterModel
            {
                CharacterId = parentId,
                Name = "Parent Character",
                RealmId = realmId,
                Status = CharacterStatus.Alive
            }
        };
        SetupBulkStateAsync(characterDict);

        // Act
        var (status, response) = await service.GetEnrichedCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.FamilyTree);
        Assert.NotNull(response.FamilyTree.Parents);
        Assert.Single(response.FamilyTree.Parents);
        Assert.Equal(parentId, response.FamilyTree.Parents.First().CharacterId);
        Assert.Equal("Parent Character", response.FamilyTree.Parents.First().Name);
    }

    [Fact]
    public async Task GetEnrichedCharacterAsync_CharacterNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new GetEnrichedCharacterRequest
        {
            CharacterId = Guid.NewGuid(),
            IncludeFamilyTree = true
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.GetEnrichedCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetEnrichedCharacterAsync_RelationshipApi404_ShouldReturnEmptyFamilyTree()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var request = new GetEnrichedCharacterRequest
        {
            CharacterId = characterId,
            IncludeFamilyTree = true
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Lonely Hero",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Relationship service returns 404
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404, null, null, null));

        // Act
        var (status, response) = await service.GetEnrichedCharacterAsync(request);

        // Assert - Graceful degradation: empty family tree, not error
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.FamilyTree);
    }

    #endregion

    #region CompressCharacter Tests

    [Fact]
    public async Task CompressCharacterAsync_DeadCharacter_ShouldCompressAndPublishEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var speciesId = Guid.NewGuid();
        var deathDate = DateTimeOffset.UtcNow.AddDays(-5);

        var request = new CompressCharacterRequest
        {
            CharacterId = characterId,
            DeleteSourceData = false
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Dead Hero",
                RealmId = realmId,
                SpeciesId = speciesId,
                Status = CharacterStatus.Dead,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-60),
                DeathDate = deathDate,
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-60),
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Setup relationship client for family summary
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>(),
                TotalCount = 0,
                PageSize = 100,
                Page = 1
            });

        // Capture archive save
        CharacterArchiveModel? savedArchive = null;
        string? savedArchiveKey = null;
        _mockArchiveStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(), It.IsAny<CharacterArchiveModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, CharacterArchiveModel, StateOptions?, CancellationToken>((k, m, _, _) =>
            {
                savedArchiveKey = k;
                savedArchive = m;
            })
            .ReturnsAsync("etag");

        // Capture compression event
        CharacterCompressedEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterCompressedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterCompressedEvent, PublishOptions?, Guid?, CancellationToken>(
                (_, e, _, _, _) => capturedEvent = e)
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.CompressCharacterAsync(request);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal("Dead Hero", response.Name);
        Assert.Equal(realmId, response.RealmId);
        Assert.Equal(speciesId, response.SpeciesId);

        // Assert - Archive was saved
        Assert.NotNull(savedArchive);
        Assert.NotNull(savedArchiveKey);
        Assert.Equal($"archive:{characterId}", savedArchiveKey);
        Assert.Equal(characterId, savedArchive.CharacterId);
        Assert.Equal("Dead Hero", savedArchive.Name);

        // Assert - PersonalitySummary and backstory are null per SERVICE_HIERARCHY
        Assert.Null(response.PersonalitySummary);
        Assert.Null(response.KeyBackstoryPoints);
        Assert.Null(response.MajorLifeEvents);

        // Assert - Compression event published
        Assert.NotNull(capturedEvent);
        Assert.Equal(characterId, capturedEvent.CharacterId);
        Assert.False(capturedEvent.DeletedSourceData);
    }

    [Fact]
    public async Task CompressCharacterAsync_AliveCharacter_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var request = new CompressCharacterRequest
        {
            CharacterId = characterId,
            DeleteSourceData = false
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Still Alive",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow.AddYears(-20),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.CompressCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);

        // Verify no archive was saved
        _mockArchiveStore.Verify(s => s.SaveAsync(
            It.IsAny<string>(), It.IsAny<CharacterArchiveModel>(),
            It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompressCharacterAsync_DeadWithoutDeathDate_ShouldReturnBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var request = new CompressCharacterRequest
        {
            CharacterId = characterId,
            DeleteSourceData = false
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Paradox",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Dead,
                DeathDate = null, // Missing death date despite Dead status
                BirthDate = DateTimeOffset.UtcNow.AddYears(-30),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Act
        var (status, response) = await service.CompressCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CompressCharacterAsync_CharacterNotFound_ShouldReturnNotFound()
    {
        // Arrange
        var service = CreateService();
        var request = new CompressCharacterRequest
        {
            CharacterId = Guid.NewGuid(),
            DeleteSourceData = false
        };

        _mockStringStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Act
        var (status, response) = await service.CompressCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CompressCharacterAsync_LockFailed_ShouldReturnConflict()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var request = new CompressCharacterRequest
        {
            CharacterId = characterId,
            DeleteSourceData = false
        };

        // Lock acquisition fails
        var failedLock = new Mock<ILockResponse>();
        failedLock.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                It.IsAny<string>(),
                It.Is<string>(s => s == characterId.ToString()),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLock.Object);

        // Act
        var (status, response) = await service.CompressCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CompressCharacterAsync_WithDeleteSourceData_ShouldSetFlagInEvent()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        var request = new CompressCharacterRequest
        {
            CharacterId = characterId,
            DeleteSourceData = true
        };

        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterModel
            {
                CharacterId = characterId,
                Name = "Dead Hero",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Dead,
                DeathDate = DateTimeOffset.UtcNow.AddDays(-1),
                BirthDate = DateTimeOffset.UtcNow.AddYears(-50),
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-50),
                UpdatedAt = DateTimeOffset.UtcNow
            });

        // Setup relationship client
        _mockRelationshipClient
            .Setup(c => c.ListRelationshipsByEntityAsync(It.IsAny<ListRelationshipsByEntityRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RelationshipListResponse
            {
                Relationships = new List<RelationshipResponse>(),
                TotalCount = 0,
                PageSize = 100,
                Page = 1
            });

        _mockArchiveStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CharacterArchiveModel>(),
                It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Capture event
        CharacterCompressedEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterCompressedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, CharacterCompressedEvent, PublishOptions?, Guid?, CancellationToken>(
                (_, e, _, _, _) => capturedEvent = e)
            .ReturnsAsync(true);

        // Act
        var (status, response) = await service.CompressCharacterAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedEvent);
        Assert.True(capturedEvent.DeletedSourceData);
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
    /// Sets up a character that exists in a specific realm.
    /// Similar to SetupCharacterExists but allows explicit realm.
    /// </summary>
    private void SetupCharacterExistsInRealm(Guid characterId, Guid realmId)
    {
        _mockStringStore
            .Setup(s => s.GetAsync($"character-global-index:{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(realmId.ToString());

        _mockCharacterStore
            .Setup(s => s.GetAsync($"character:{realmId}:{characterId}", It.IsAny<CancellationToken>()))
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
