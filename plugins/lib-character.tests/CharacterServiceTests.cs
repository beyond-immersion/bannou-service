using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.CharacterHistory;
using BeyondImmersion.BannouService.CharacterPersonality;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Relationship;
using BeyondImmersion.BannouService.RelationshipType;
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
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<CharacterService>> _mockLogger;
    private readonly Mock<IRealmClient> _mockRealmClient;
    private readonly Mock<ISpeciesClient> _mockSpeciesClient;
    private readonly Mock<ICharacterPersonalityClient> _mockPersonalityClient;
    private readonly Mock<ICharacterHistoryClient> _mockHistoryClient;
    private readonly Mock<IRelationshipClient> _mockRelationshipClient;
    private readonly Mock<IRelationshipTypeClient> _mockRelationshipTypeClient;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

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
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<CharacterService>>();
        _mockRealmClient = new Mock<IRealmClient>();
        _mockSpeciesClient = new Mock<ISpeciesClient>();
        _mockPersonalityClient = new Mock<ICharacterPersonalityClient>();
        _mockHistoryClient = new Mock<ICharacterHistoryClient>();
        _mockRelationshipClient = new Mock<IRelationshipClient>();
        _mockRelationshipTypeClient = new Mock<IRelationshipTypeClient>();
        _mockEventConsumer = new Mock<IEventConsumer>();

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

    private CharacterService CreateService()
    {
        return new CharacterService(
            _mockStateStoreFactory.Object,
            _mockMessageBus.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            Configuration,
            _mockRealmClient.Object,
            _mockSpeciesClient.Object,
            _mockPersonalityClient.Object,
            _mockHistoryClient.Object,
            _mockRelationshipClient.Object,
            _mockRelationshipTypeClient.Object,
            _mockEventConsumer.Object);
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

        // Assert - Event was published with correct content
        Assert.NotNull(capturedEvent);
        Assert.Equal(characterId, capturedEvent.CharacterId);
        Assert.Contains("name", capturedEvent.ChangedFields);
        Assert.Contains("status", capturedEvent.ChangedFields);
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

        // Setup realm index
        _mockListStore
            .Setup(s => s.GetAsync(REALM_INDEX_KEY_PREFIX + realmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { characterId.ToString() });

        // Setup bulk character retrieval - IStateStore.GetBulkAsync returns IReadOnlyDictionary
        var characterModel = new CharacterModel
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
        SetupBulkStateAsync(new Dictionary<string, CharacterModel>
        {
            { $"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}", characterModel }
        });

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
    public async Task ListCharactersAsync_WithoutRealmFilter_ShouldReturnEmptyList()
    {
        // Arrange
        var service = CreateService();
        var request = new ListCharactersRequest { Page = 1, PageSize = 20 };

        // Act
        var (status, response) = await service.ListCharactersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Characters);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task ListCharactersAsync_WithStatusFilter_ShouldFilterByStatus()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var aliveCharId = Guid.NewGuid();
        var deadCharId = Guid.NewGuid();
        var request = new ListCharactersRequest
        {
            RealmId = realmId,
            Status = CharacterStatus.Alive
        };

        // Setup realm index with both characters
        _mockListStore
            .Setup(s => s.GetAsync(REALM_INDEX_KEY_PREFIX + realmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { aliveCharId.ToString(), deadCharId.ToString() });

        // Setup bulk character retrieval for both characters
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
        var deadCharacter = new CharacterModel
        {
            CharacterId = deadCharId,
            Name = "Dead Character",
            RealmId = realmId,
            SpeciesId = Guid.NewGuid(),
            Status = CharacterStatus.Dead,
            BirthDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        SetupBulkStateAsync(new Dictionary<string, CharacterModel>
        {
            { $"{CHARACTER_KEY_PREFIX}{realmId}:{aliveCharId}", aliveCharacter },
            { $"{CHARACTER_KEY_PREFIX}{realmId}:{deadCharId}", deadCharacter }
        });

        // Act
        var (status, response) = await service.ListCharactersAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Characters);
        Assert.Equal("Alive Character", response.Characters.First().Name);
    }

    [Fact]
    public async Task ListCharactersAsync_ShouldPaginateCorrectly()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var characterIds = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var request = new ListCharactersRequest
        {
            RealmId = realmId,
            Page = 2,
            PageSize = 2
        };

        // Setup realm index with 5 characters
        _mockListStore
            .Setup(s => s.GetAsync(REALM_INDEX_KEY_PREFIX + realmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(characterIds.Select(id => id.ToString()).ToList());

        // Setup bulk character retrieval for all 5 characters
        var bulkItems = new Dictionary<string, CharacterModel>();
        for (int i = 0; i < characterIds.Count; i++)
        {
            var charId = characterIds[i];
            bulkItems[$"{CHARACTER_KEY_PREFIX}{realmId}:{charId}"] = new CharacterModel
            {
                CharacterId = charId,
                Name = $"Character {i}",
                RealmId = realmId,
                SpeciesId = Guid.NewGuid(),
                Status = CharacterStatus.Alive,
                BirthDate = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        SetupBulkStateAsync(bulkItems);

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

        // Setup realm index
        _mockListStore
            .Setup(s => s.GetAsync(REALM_INDEX_KEY_PREFIX + realmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { characterId.ToString() });

        // Setup bulk character retrieval
        var characterModel = new CharacterModel
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
        SetupBulkStateAsync(new Dictionary<string, CharacterModel>
        {
            { $"{CHARACTER_KEY_PREFIX}{realmId}:{characterId}", characterModel }
        });

        // Act
        var (status, response) = await service.GetCharactersByRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Characters);
        Assert.Equal("Realm Character", response.Characters.First().Name);
    }

    [Fact]
    public async Task GetCharactersByRealmAsync_WithSpeciesFilter_ShouldFilterBySpecies()
    {
        // Arrange
        var service = CreateService();
        var realmId = Guid.NewGuid();
        var targetSpeciesId = Guid.NewGuid();
        var otherSpeciesId = Guid.NewGuid();
        var targetCharId = Guid.NewGuid();
        var otherCharId = Guid.NewGuid();

        var request = new GetCharactersByRealmRequest
        {
            RealmId = realmId,
            SpeciesId = targetSpeciesId
        };

        // Setup realm index
        _mockListStore
            .Setup(s => s.GetAsync(REALM_INDEX_KEY_PREFIX + realmId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { targetCharId.ToString(), otherCharId.ToString() });

        // Setup bulk character retrieval for both characters
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
        var otherCharacter = new CharacterModel
        {
            CharacterId = otherCharId,
            Name = "Other Species Character",
            RealmId = realmId,
            SpeciesId = otherSpeciesId,
            Status = CharacterStatus.Alive,
            BirthDate = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        SetupBulkStateAsync(new Dictionary<string, CharacterModel>
        {
            { $"{CHARACTER_KEY_PREFIX}{realmId}:{targetCharId}", targetCharacter },
            { $"{CHARACTER_KEY_PREFIX}{realmId}:{otherCharId}", otherCharacter }
        });

        // Act
        var (status, response) = await service.GetCharactersByRealmAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Characters);
        Assert.Equal("Target Species Character", response.Characters.First().Name);
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
        Assert.Equal(90, config.CharacterRetentionDays);
    }
}
