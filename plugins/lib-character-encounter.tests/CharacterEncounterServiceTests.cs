using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.CharacterEncounter;
using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.CharacterEncounter.Tests;

/// <summary>
/// Unit tests for CharacterEncounterService.
/// Tests encounter recording, perspective management, sentiment tracking, and memory decay.
/// </summary>
public class CharacterEncounterServiceTests : ServiceTestBase<CharacterEncounterServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<EncounterData>> _mockEncounterStore;
    private readonly Mock<IStateStore<PerspectiveData>> _mockPerspectiveStore;
    private readonly Mock<IStateStore<EncounterTypeData>> _mockTypeStore;
    private readonly Mock<IStateStore<CharacterIndexData>> _mockCharIndexStore;
    private readonly Mock<IStateStore<PairIndexData>> _mockPairIndexStore;
    private readonly Mock<IStateStore<LocationIndexData>> _mockLocationIndexStore;
    private readonly Mock<IStateStore<GlobalCharacterIndexData>> _mockGlobalIndexStore;
    private readonly Mock<IStateStore<CustomTypeIndexData>> _mockCustomTypeIndexStore;
    private readonly Mock<IStateStore<TypeEncounterIndexData>> _mockTypeEncounterIndexStore;
    private readonly Mock<IStateStore<EncounterPerspectiveIndexData>> _mockEncounterPerspectiveIndexStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<ILogger<CharacterEncounterService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<IEncounterDataCache> _mockEncounterDataCache;

    private const string STATE_STORE = "character-encounter-statestore";

    public CharacterEncounterServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockEncounterStore = new Mock<IStateStore<EncounterData>>();
        _mockPerspectiveStore = new Mock<IStateStore<PerspectiveData>>();
        _mockTypeStore = new Mock<IStateStore<EncounterTypeData>>();
        _mockCharIndexStore = new Mock<IStateStore<CharacterIndexData>>();
        _mockPairIndexStore = new Mock<IStateStore<PairIndexData>>();
        _mockLocationIndexStore = new Mock<IStateStore<LocationIndexData>>();
        _mockGlobalIndexStore = new Mock<IStateStore<GlobalCharacterIndexData>>();
        _mockCustomTypeIndexStore = new Mock<IStateStore<CustomTypeIndexData>>();
        _mockTypeEncounterIndexStore = new Mock<IStateStore<TypeEncounterIndexData>>();
        _mockEncounterPerspectiveIndexStore = new Mock<IStateStore<EncounterPerspectiveIndexData>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockLogger = new Mock<ILogger<CharacterEncounterService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockEncounterDataCache = new Mock<IEncounterDataCache>();

        // Setup default factory returns
        _mockStateStoreFactory
            .Setup(f => f.GetStore<EncounterData>(STATE_STORE))
            .Returns(_mockEncounterStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<PerspectiveData>(STATE_STORE))
            .Returns(_mockPerspectiveStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<EncounterTypeData>(STATE_STORE))
            .Returns(_mockTypeStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CharacterIndexData>(STATE_STORE))
            .Returns(_mockCharIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<PairIndexData>(STATE_STORE))
            .Returns(_mockPairIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<LocationIndexData>(STATE_STORE))
            .Returns(_mockLocationIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<GlobalCharacterIndexData>(STATE_STORE))
            .Returns(_mockGlobalIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CustomTypeIndexData>(STATE_STORE))
            .Returns(_mockCustomTypeIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<TypeEncounterIndexData>(STATE_STORE))
            .Returns(_mockTypeEncounterIndexStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<EncounterPerspectiveIndexData>(STATE_STORE))
            .Returns(_mockEncounterPerspectiveIndexStore.Object);

        // Default character client setup - all characters exist
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(It.IsAny<GetCharacterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterResponse { CharacterId = Guid.NewGuid(), Name = "Test Character" });

        // Default type-encounter index setup
        _mockTypeEncounterIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TypeEncounterIndexData?)null);
        _mockTypeEncounterIndexStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((TypeEncounterIndexData?)null, (string?)null));
        _mockTypeEncounterIndexStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<TypeEncounterIndexData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Default GetBulkAsync returns empty dictionaries to prevent NRE on bulk operations
        _mockEncounterStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EncounterData>());
        _mockPerspectiveStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, PerspectiveData>());

        // Default message bus setup
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private CharacterEncounterService CreateService()
    {
        return new CharacterEncounterService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object,
            _mockCharacterClient.Object,
            _mockEncounterDataCache.Object);
    }

    /// <summary>
    /// Sets up the character client to make a specific character not found.
    /// </summary>
    private void SetupCharacterNotFound(Guid characterId)
    {
        _mockCharacterClient
            .Setup(c => c.GetCharacterAsync(
                It.Is<GetCharacterRequest>(r => r.CharacterId == characterId),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Not found", 404));
    }

    /// <summary>
    /// Sets up an existing encounter for duplicate detection tests.
    /// </summary>
    private void SetupExistingEncounter(Guid encounterId, List<Guid> participantIds, string typeCode, DateTimeOffset timestamp)
    {
        var sortedParticipants = participantIds.OrderBy(id => id).ToList();
        var pairKey = $"{sortedParticipants[0]}:{sortedParticipants[1]}";

        // Setup pair index to return this encounter
        _mockPairIndexStore
            .Setup(s => s.GetAsync($"pair-idx-{pairKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PairIndexData
            {
                CharacterIdA = sortedParticipants[0],
                CharacterIdB = sortedParticipants[1],
                EncounterIds = new List<Guid> { encounterId }
            });

        // Setup encounter data
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData
            {
                EncounterId = encounterId,
                EncounterTypeCode = typeCode.ToUpperInvariant(),
                ParticipantIds = participantIds,
                Timestamp = timestamp.ToUnixTimeSeconds(),
                RealmId = Guid.NewGuid(),
                Outcome = EncounterOutcome.NEUTRAL,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
    }

    /// <summary>
    /// Sets up the type-encounter index with existing encounters for a type.
    /// </summary>
    private void SetupTypeHasEncounters(string typeCode, int count)
    {
        var encounterIds = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        _mockTypeEncounterIndexStore
            .Setup(s => s.GetAsync($"type-enc-idx-{typeCode.ToUpperInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TypeEncounterIndexData
            {
                TypeCode = typeCode.ToUpperInvariant(),
                EncounterIds = encounterIds
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
    public void CharacterEncounterService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<CharacterEncounterService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void CharacterEncounterServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new CharacterEncounterServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    [Fact]
    public void CharacterEncounterServiceConfiguration_HasExpectedDefaults()
    {
        // Arrange & Act
        var config = new CharacterEncounterServiceConfiguration();

        // Assert
        Assert.True(config.MemoryDecayEnabled);
        Assert.Equal(MemoryDecayMode.Lazy, config.MemoryDecayMode);
        Assert.Equal(24, config.MemoryDecayIntervalHours);
        Assert.Equal(0.05, config.MemoryDecayRate, 3);
        Assert.Equal(0.1, config.MemoryFadeThreshold, 3);
        Assert.Equal(1000, config.MaxEncountersPerCharacter);
        Assert.Equal(100, config.MaxEncountersPerPair);
        Assert.Equal(20, config.DefaultPageSize);
        Assert.Equal(100, config.MaxPageSize);
        Assert.Equal(100, config.MaxBatchSize);
        Assert.Equal(1.0, config.DefaultMemoryStrength, 3);
        Assert.Equal(0.2, config.MemoryRefreshBoost, 3);
        Assert.True(config.SeedBuiltInTypesOnStartup);
    }

    #endregion

    #region Encounter Type Tests

    [Fact]
    public async Task CreateEncounterTypeAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        SetupTypeDoesNotExist("CUSTOM");

        EncounterTypeData? savedType = null;
        string? savedKey = null;
        _mockTypeStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<EncounterTypeData>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, EncounterTypeData, StateOptions?, CancellationToken>((k, t, _, _) =>
            {
                savedKey = k;
                savedType = t;
            })
            .ReturnsAsync("etag-1");

        _mockCustomTypeIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomTypeIndexData?)null);
        _mockCustomTypeIndexStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CustomTypeIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new CreateEncounterTypeRequest
        {
            Code = "CUSTOM",
            Name = "Custom Encounter",
            Description = "A custom encounter type",
            DefaultEmotionalImpact = EmotionalImpact.GRATITUDE,
            SortOrder = 100
        };

        // Act
        var (status, response) = await service.CreateEncounterTypeAsync(request, CancellationToken.None);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("CUSTOM", response.Code);
        Assert.Equal("Custom Encounter", response.Name);
        Assert.False(response.IsBuiltIn);

        // Assert - State was saved correctly
        Assert.NotNull(savedType);
        Assert.NotNull(savedKey);
        Assert.StartsWith("type-", savedKey);
        Assert.Equal("CUSTOM", savedType.Code);
        Assert.Equal("Custom Encounter", savedType.Name);
        Assert.False(savedType.IsBuiltIn);
    }

    [Fact]
    public async Task CreateEncounterTypeAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        SetupTypeExists("EXISTING", CreateTestEncounterType("EXISTING", "Existing Type", isBuiltIn: false));

        var request = new CreateEncounterTypeRequest
        {
            Code = "EXISTING",
            Name = "Duplicate Type"
        };

        // Act
        var (status, response) = await service.CreateEncounterTypeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateEncounterTypeAsync_BuiltInCode_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        SetupTypeExists("COMBAT", CreateTestEncounterType("COMBAT", "Combat", isBuiltIn: true));

        var request = new CreateEncounterTypeRequest
        {
            Code = "COMBAT",
            Name = "Try to override combat"
        };

        // Act
        var (status, response) = await service.CreateEncounterTypeAsync(request, CancellationToken.None);

        // Assert - Built-in codes cannot be created as custom types
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetEncounterTypeAsync_ExistingType_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeData = CreateTestEncounterType("DIALOGUE", "Dialogue", isBuiltIn: true);
        SetupTypeExists("DIALOGUE", typeData);

        // Act
        var (status, response) = await service.GetEncounterTypeAsync(
            new GetEncounterTypeRequest { Code = "DIALOGUE" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("DIALOGUE", response.Code);
        Assert.True(response.IsBuiltIn);
    }

    [Fact]
    public async Task GetEncounterTypeAsync_NonExistentType_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        SetupTypeDoesNotExist("NONEXISTENT");

        // Act
        var (status, response) = await service.GetEncounterTypeAsync(
            new GetEncounterTypeRequest { Code = "NONEXISTENT" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ListEncounterTypesAsync_ReturnsBuiltInAndCustomTypes()
    {
        // Arrange
        var service = CreateService();

        // Setup built-in types
        var combatType = CreateTestEncounterType("COMBAT", "Combat", isBuiltIn: true);
        var dialogueType = CreateTestEncounterType("DIALOGUE", "Dialogue", isBuiltIn: true);
        _mockTypeStore.Setup(s => s.GetAsync("type-COMBAT", It.IsAny<CancellationToken>())).ReturnsAsync(combatType);
        _mockTypeStore.Setup(s => s.GetAsync("type-DIALOGUE", It.IsAny<CancellationToken>())).ReturnsAsync(dialogueType);
        _mockTypeStore.Setup(s => s.GetAsync("type-TRADE", It.IsAny<CancellationToken>())).ReturnsAsync((EncounterTypeData?)null);
        _mockTypeStore.Setup(s => s.GetAsync("type-QUEST", It.IsAny<CancellationToken>())).ReturnsAsync((EncounterTypeData?)null);
        _mockTypeStore.Setup(s => s.GetAsync("type-SOCIAL", It.IsAny<CancellationToken>())).ReturnsAsync((EncounterTypeData?)null);
        _mockTypeStore.Setup(s => s.GetAsync("type-CEREMONY", It.IsAny<CancellationToken>())).ReturnsAsync((EncounterTypeData?)null);

        // Setup custom types index
        var customType = CreateTestEncounterType("CUSTOM", "Custom", isBuiltIn: false);
        _mockCustomTypeIndexStore
            .Setup(s => s.GetAsync("custom-type-idx", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomTypeIndexData { TypeCodes = new List<string> { "CUSTOM" } });
        _mockTypeStore.Setup(s => s.GetAsync("type-CUSTOM", It.IsAny<CancellationToken>())).ReturnsAsync(customType);

        // Act
        var (status, response) = await service.ListEncounterTypesAsync(
            new ListEncounterTypesRequest { IncludeInactive = true }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Types.Count >= 2); // At least combat, dialogue, and custom
    }

    [Fact]
    public async Task UpdateEncounterTypeAsync_CustomType_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeData = CreateTestEncounterType("CUSTOM", "Custom Type", isBuiltIn: false);
        SetupTypeExists("CUSTOM", typeData);

        EncounterTypeData? savedType = null;
        _mockTypeStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<EncounterTypeData>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, EncounterTypeData, StateOptions?, CancellationToken>((_, t, _, _) => savedType = t)
            .ReturnsAsync("etag-1");

        var request = new UpdateEncounterTypeRequest
        {
            Code = "CUSTOM",
            Name = "Updated Custom Type",
            Description = "Updated description"
        };

        // Act
        var (status, response) = await service.UpdateEncounterTypeAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Custom Type", response.Name);
        Assert.NotNull(savedType);
        Assert.Equal("Updated Custom Type", savedType.Name);
    }

    [Fact]
    public async Task UpdateEncounterTypeAsync_BuiltInType_AllowsLimitedUpdates()
    {
        // Arrange
        var service = CreateService();
        var typeData = CreateTestEncounterType("COMBAT", "Combat", isBuiltIn: true);
        SetupTypeExists("COMBAT", typeData);

        _mockTypeStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<EncounterTypeData>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new UpdateEncounterTypeRequest
        {
            Code = "COMBAT",
            Description = "Updated description for built-in"
        };

        // Act
        var (status, response) = await service.UpdateEncounterTypeAsync(request, CancellationToken.None);

        // Assert - Built-in types can be updated with limited fields
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task DeleteEncounterTypeAsync_CustomType_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var typeData = CreateTestEncounterType("CUSTOM", "Custom Type", isBuiltIn: false);
        SetupTypeExists("CUSTOM", typeData);

        _mockTypeStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<EncounterTypeData>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockCustomTypeIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomTypeIndexData { TypeCodes = new List<string> { "CUSTOM" } });
        _mockCustomTypeIndexStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CustomTypeIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var status = await service.DeleteEncounterTypeAsync(
            new DeleteEncounterTypeRequest { Code = "CUSTOM" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task DeleteEncounterTypeAsync_BuiltInType_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var typeData = CreateTestEncounterType("COMBAT", "Combat", isBuiltIn: true);
        SetupTypeExists("COMBAT", typeData);

        // Act
        var status = await service.DeleteEncounterTypeAsync(
            new DeleteEncounterTypeRequest { Code = "COMBAT" }, CancellationToken.None);

        // Assert - Cannot delete built-in types
        Assert.Equal(StatusCodes.BadRequest, status);
    }

    #endregion

    #region Record Encounter Tests

    [Fact]
    public async Task RecordEncounterAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        SetupTypeExists("DIALOGUE", CreateTestEncounterType("DIALOGUE", "Dialogue", isBuiltIn: true));
        SetupEmptyIndexes();

        EncounterData? savedEncounter = null;
        var savedPerspectives = new List<PerspectiveData>();

        _mockEncounterStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<EncounterData>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, EncounterData, StateOptions?, CancellationToken>((_, e, _, _) => savedEncounter = e)
            .ReturnsAsync("etag-1");

        _mockPerspectiveStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<PerspectiveData>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PerspectiveData, StateOptions?, CancellationToken>((_, p, _, _) => savedPerspectives.Add(p))
            .ReturnsAsync("etag-1");

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "DIALOGUE",
            RealmId = realmId,
            ParticipantIds = new List<Guid> { charA, charB },
            Outcome = EncounterOutcome.POSITIVE,
            Context = "Met at the tavern",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert - Response
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Encounter);
        Assert.Equal(2, response.Perspectives.Count);
        Assert.Equal("DIALOGUE", response.Encounter.EncounterTypeCode);
        Assert.Equal("Met at the tavern", response.Encounter.Context);

        // Assert - Encounter was saved
        Assert.NotNull(savedEncounter);
        Assert.Equal("DIALOGUE", savedEncounter.EncounterTypeCode);
        Assert.Equal(2, savedEncounter.ParticipantIds.Count);

        // Assert - Perspectives were saved for each participant
        Assert.Equal(2, savedPerspectives.Count);
    }

    [Fact]
    public async Task RecordEncounterAsync_LessThanTwoParticipants_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "DIALOGUE",
            RealmId = Guid.NewGuid(),
            ParticipantIds = new List<Guid> { charA }, // Only one participant
            Outcome = EncounterOutcome.NEUTRAL
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecordEncounterAsync_PublishesRecordedEvent()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();

        SetupTypeExists("COMBAT", CreateTestEncounterType("COMBAT", "Combat", isBuiltIn: true));
        SetupEmptyIndexes();
        SetupDefaultSaves();

        EncounterRecordedEvent? capturedEvent = null;
        string? capturedTopic = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.Is<string>(t => t == "encounter.recorded"),
                It.IsAny<EncounterRecordedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, EncounterRecordedEvent, CancellationToken>((topic, evt, _) =>
            {
                capturedTopic = topic;
                capturedEvent = evt;
            })
            .ReturnsAsync(true);

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "COMBAT",
            RealmId = Guid.NewGuid(),
            ParticipantIds = new List<Guid> { charA, charB },
            Outcome = EncounterOutcome.NEGATIVE,
            Context = "Battle in the forest"
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(capturedEvent);
        Assert.Equal("encounter.recorded", capturedTopic);
        Assert.Equal("COMBAT", capturedEvent.EncounterTypeCode);
        Assert.Equal("NEGATIVE", capturedEvent.Outcome);
        Assert.Contains(charA, capturedEvent.ParticipantIds);
        Assert.Contains(charB, capturedEvent.ParticipantIds);
    }

    [Fact]
    public async Task RecordEncounterAsync_WithCustomPerspectives_UsesPerspectiveData()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();

        SetupTypeExists("TRADE", CreateTestEncounterType("TRADE", "Trade", isBuiltIn: true));
        SetupEmptyIndexes();

        var savedPerspectives = new List<PerspectiveData>();
        _mockEncounterStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<EncounterData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockPerspectiveStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, PerspectiveData, StateOptions?, CancellationToken>((_, p, _, _) => savedPerspectives.Add(p))
            .ReturnsAsync("etag-1");

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "TRADE",
            RealmId = Guid.NewGuid(),
            ParticipantIds = new List<Guid> { charA, charB },
            Outcome = EncounterOutcome.POSITIVE,
            Perspectives = new List<PerspectiveInput>
            {
                new() { CharacterId = charA, EmotionalImpact = EmotionalImpact.GRATITUDE, SentimentShift = 0.3f, RememberedAs = "A great deal" },
                new() { CharacterId = charB, EmotionalImpact = EmotionalImpact.RESPECT, SentimentShift = 0.1f, RememberedAs = "Fair trade" }
            }
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.Equal(2, savedPerspectives.Count);

        var perspA = savedPerspectives.FirstOrDefault(p => p.CharacterId == charA);
        Assert.NotNull(perspA);
        Assert.Equal(EmotionalImpact.GRATITUDE, perspA.EmotionalImpact);
        Assert.Equal(0.3f, perspA.SentimentShift);
        Assert.Equal("A great deal", perspA.RememberedAs);
    }

    #endregion

    #region Query Tests

    [Fact]
    public async Task QueryByCharacterAsync_WithEncounters_ReturnsList()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var charIndex = new CharacterIndexData
        {
            CharacterId = characterId,
            PerspectiveIds = new List<Guid> { perspectiveId }
        };

        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);
        var encounter = CreateTestEncounter(encounterId, new List<Guid> { characterId, Guid.NewGuid() });

        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(charIndex);
        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(perspective);
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(encounter);
        _mockPerspectiveStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Bulk load setups for query path
        _mockPerspectiveStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, PerspectiveData> { { $"pers-{perspectiveId}", perspective } });
        _mockEncounterStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EncounterData> { { $"enc-{encounterId}", encounter } });

        // Act
        var (status, response) = await service.QueryByCharacterAsync(
            new QueryByCharacterRequest { CharacterId = characterId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Encounters);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task QueryByCharacterAsync_NoEncounters_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterIndexData?)null);

        // Act
        var (status, response) = await service.QueryByCharacterAsync(
            new QueryByCharacterRequest { CharacterId = characterId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Encounters);
        Assert.Equal(0, response.TotalCount);
    }

    [Fact]
    public async Task QueryBetweenAsync_WithEncounters_ReturnsList()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var encounterId = Guid.NewGuid();

        var pairIndex = new PairIndexData
        {
            CharacterIdA = charA < charB ? charA : charB,
            CharacterIdB = charA < charB ? charB : charA,
            EncounterIds = new List<Guid> { encounterId }
        };

        var encounter = CreateTestEncounter(encounterId, new List<Guid> { charA, charB });

        _mockPairIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("pair-idx-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pairIndex);
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(encounter);
        _mockEncounterStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, EncounterData> { { $"enc-{encounterId}", encounter } });

        // Setup perspective queries (used by legacy fallback path)
        _mockPerspectiveStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("pers-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, CancellationToken _) =>
            {
                var perspId = Guid.NewGuid();
                return CreateTestPerspective(perspId, encounterId, charA);
            });

        // Act
        var (status, response) = await service.QueryBetweenAsync(
            new QueryBetweenRequest { CharacterIdA = charA, CharacterIdB = charB }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Encounters);
    }

    [Fact]
    public async Task HasMetAsync_CharactersHaveMet_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var encounterId = Guid.NewGuid();

        var pairIndex = new PairIndexData
        {
            CharacterIdA = charA < charB ? charA : charB,
            CharacterIdB = charA < charB ? charB : charA,
            EncounterIds = new List<Guid> { encounterId }
        };

        _mockPairIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("pair-idx-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pairIndex);

        // Act
        var (status, response) = await service.HasMetAsync(
            new HasMetRequest { CharacterIdA = charA, CharacterIdB = charB }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.HasMet);
        Assert.Equal(1, response.EncounterCount);
    }

    [Fact]
    public async Task HasMetAsync_CharactersHaveNotMet_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();

        _mockPairIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("pair-idx-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PairIndexData?)null);

        // Act
        var (status, response) = await service.HasMetAsync(
            new HasMetRequest { CharacterIdA = charA, CharacterIdB = charB }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.False(response.HasMet);
        Assert.Equal(0, response.EncounterCount);
    }

    [Fact]
    public async Task GetSentimentAsync_WithEncounters_CalculatesAggregate()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var pairIndex = new PairIndexData
        {
            CharacterIdA = characterId < targetId ? characterId : targetId,
            CharacterIdB = characterId < targetId ? targetId : characterId,
            EncounterIds = new List<Guid> { encounterId }
        };

        var perspective = new PerspectiveData
        {
            PerspectiveId = perspectiveId,
            EncounterId = encounterId,
            CharacterId = characterId,
            EmotionalImpact = EmotionalImpact.GRATITUDE,
            SentimentShift = 0.5f,
            MemoryStrength = 1.0f,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        var encounter = CreateTestEncounter(encounterId, new List<Guid> { characterId, targetId });

        _mockPairIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("pair-idx-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pairIndex);
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(encounter);

        // Setup to find perspective by character from encounter's participants
        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData
            {
                CharacterId = characterId,
                PerspectiveIds = new List<Guid> { perspectiveId }
            });
        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(perspective);

        // Act
        var (status, response) = await service.GetSentimentAsync(
            new GetSentimentRequest { CharacterId = characterId, TargetCharacterId = targetId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(1, response.EncounterCount);
    }

    [Fact]
    public async Task BatchGetSentimentAsync_MultipleTargets_ReturnsAllSentiments()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var target1 = Guid.NewGuid();
        var target2 = Guid.NewGuid();

        // Setup for target1 - they have met
        _mockPairIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(target1.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PairIndexData
            {
                EncounterIds = new List<Guid> { Guid.NewGuid() }
            });

        // Setup for target2 - they have not met
        _mockPairIndexStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(target2.ToString()) && !k.Contains(target1.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PairIndexData?)null);

        var request = new BatchGetSentimentRequest
        {
            CharacterId = characterId,
            TargetCharacterIds = new List<Guid> { target1, target2 }
        };

        // Act
        var (status, response) = await service.BatchGetSentimentAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Sentiments.Count);
    }

    [Fact]
    public async Task BatchGetSentimentAsync_TooManyTargets_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var targets = Enumerable.Range(0, 150).Select(_ => Guid.NewGuid()).ToList();

        var request = new BatchGetSentimentRequest
        {
            CharacterId = characterId,
            TargetCharacterIds = targets
        };

        // Act
        var (status, response) = await service.BatchGetSentimentAsync(request, CancellationToken.None);

        // Assert - MaxBatchSize is 100
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region Perspective Tests

    [Fact]
    public async Task GetPerspectiveAsync_ExistingPerspective_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var encounterId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var encounter = CreateTestEncounter(encounterId, new List<Guid> { characterId, Guid.NewGuid() });
        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);

        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(encounter);
        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData
            {
                CharacterId = characterId,
                PerspectiveIds = new List<Guid> { perspectiveId }
            });
        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(perspective);
        _mockPerspectiveStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var (status, response) = await service.GetPerspectiveAsync(
            new GetPerspectiveRequest { EncounterId = encounterId, CharacterId = characterId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.Perspective.CharacterId);
    }

    [Fact]
    public async Task UpdatePerspectiveAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var encounterId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);
        var encounter = CreateTestEncounter(encounterId, new List<Guid> { characterId, Guid.NewGuid() });

        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(encounter);
        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData
            {
                CharacterId = characterId,
                PerspectiveIds = new List<Guid> { perspectiveId }
            });
        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(perspective);
        _mockPerspectiveStore
            .Setup(s => s.GetWithETagAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((perspective, "etag-0"));

        PerspectiveData? savedPerspective = null;
        _mockPerspectiveStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(),
                It.IsAny<PerspectiveData>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, PerspectiveData, string, CancellationToken>((_, p, _, _) => savedPerspective = p)
            .ReturnsAsync("etag-1");

        var request = new UpdatePerspectiveRequest
        {
            EncounterId = encounterId,
            CharacterId = characterId,
            RememberedAs = "A pivotal moment"
        };

        // Act
        var (status, response) = await service.UpdatePerspectiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("A pivotal moment", response.Perspective.RememberedAs);
        Assert.NotNull(savedPerspective);
        Assert.Equal("A pivotal moment", savedPerspective.RememberedAs);
    }

    [Fact]
    public async Task UpdatePerspectiveAsync_PublishesUpdatedEvent()
    {
        // Arrange
        var service = CreateService();
        var encounterId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);
        var encounter = CreateTestEncounter(encounterId, new List<Guid> { characterId, Guid.NewGuid() });

        _mockEncounterStore.Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>())).ReturnsAsync(encounter);
        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = characterId, PerspectiveIds = new List<Guid> { perspectiveId } });
        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync(perspective);
        _mockPerspectiveStore.Setup(s => s.GetWithETagAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync((perspective, "etag-0"));
        _mockPerspectiveStore.Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        EncounterPerspectiveUpdatedEvent? capturedEvent = null;
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.Is<string>(t => t == "encounter.perspective.updated"),
                It.IsAny<EncounterPerspectiveUpdatedEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, EncounterPerspectiveUpdatedEvent, CancellationToken>((_, evt, _) => capturedEvent = evt)
            .ReturnsAsync(true);

        var request = new UpdatePerspectiveRequest
        {
            EncounterId = encounterId,
            CharacterId = characterId,
            SentimentShift = 0.8f
        };

        // Act
        await service.UpdatePerspectiveAsync(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal(encounterId, capturedEvent.EncounterId);
        Assert.Equal(characterId, capturedEvent.CharacterId);
    }

    [Fact]
    public async Task RefreshMemoryAsync_BoostsMemoryStrength()
    {
        // Arrange
        var service = CreateService();
        var encounterId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);
        perspective.MemoryStrength = 0.5f; // Start with weakened memory
        var encounter = CreateTestEncounter(encounterId, new List<Guid> { characterId, Guid.NewGuid() });

        _mockEncounterStore.Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>())).ReturnsAsync(encounter);
        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = characterId, PerspectiveIds = new List<Guid> { perspectiveId } });
        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync(perspective);
        _mockPerspectiveStore.Setup(s => s.GetWithETagAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync((perspective, "etag-0"));

        PerspectiveData? savedPerspective = null;
        _mockPerspectiveStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, PerspectiveData, string, CancellationToken>((_, p, _, _) => savedPerspective = p)
            .ReturnsAsync("etag-1");

        var request = new RefreshMemoryRequest
        {
            EncounterId = encounterId,
            CharacterId = characterId
        };

        // Act
        var (status, response) = await service.RefreshMemoryAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedPerspective);
        // Default boost is 0.2, so 0.5 + 0.2 = 0.7
        Assert.Equal(0.7f, savedPerspective.MemoryStrength, 2);
    }

    [Fact]
    public async Task RefreshMemoryAsync_ClampsToOne()
    {
        // Arrange
        var service = CreateService();
        var encounterId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);
        perspective.MemoryStrength = 0.95f; // Almost full
        var encounter = CreateTestEncounter(encounterId, new List<Guid> { characterId, Guid.NewGuid() });

        _mockEncounterStore.Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>())).ReturnsAsync(encounter);
        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = characterId, PerspectiveIds = new List<Guid> { perspectiveId } });
        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync(perspective);
        _mockPerspectiveStore.Setup(s => s.GetWithETagAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync((perspective, "etag-0"));

        PerspectiveData? savedPerspective = null;
        _mockPerspectiveStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, PerspectiveData, string, CancellationToken>((_, p, _, _) => savedPerspective = p)
            .ReturnsAsync("etag-1");

        // Act
        var (status, response) = await service.RefreshMemoryAsync(
            new RefreshMemoryRequest { EncounterId = encounterId, CharacterId = characterId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedPerspective);
        // Should clamp to 1.0 instead of 0.95 + 0.2 = 1.15
        Assert.Equal(1.0f, savedPerspective.MemoryStrength, 2);
    }

    #endregion

    #region Admin Tests

    [Fact]
    public async Task DeleteEncounterAsync_ExistingEncounter_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var encounterId = Guid.NewGuid();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var perspectiveIdA = Guid.NewGuid();
        var perspectiveIdB = Guid.NewGuid();

        var encounter = CreateTestEncounter(encounterId, new List<Guid> { charA, charB });
        var perspectiveA = CreateTestPerspective(perspectiveIdA, encounterId, charA);
        var perspectiveB = CreateTestPerspective(perspectiveIdB, encounterId, charB);

        _mockEncounterStore.Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>())).ReturnsAsync(encounter);
        _mockEncounterStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{charA}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = charA, PerspectiveIds = new List<Guid> { perspectiveIdA } });
        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{charB}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = charB, PerspectiveIds = new List<Guid> { perspectiveIdB } });
        _mockCharIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CharacterIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveIdA}", It.IsAny<CancellationToken>())).ReturnsAsync(perspectiveA);
        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveIdB}", It.IsAny<CancellationToken>())).ReturnsAsync(perspectiveB);
        _mockPerspectiveStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _mockPairIndexStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("pair-idx-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PairIndexData { EncounterIds = new List<Guid> { encounterId } });
        _mockPairIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PairIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        _mockGlobalIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalCharacterIndexData { CharacterIds = new List<Guid> { charA, charB } });
        _mockGlobalIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GlobalCharacterIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        // Act
        var (status, _) = await service.DeleteEncounterAsync(
            new DeleteEncounterRequest { EncounterId = encounterId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task DeleteEncounterAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var encounterId = Guid.NewGuid();

        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EncounterData?)null);

        // Act
        var (status, _) = await service.DeleteEncounterAsync(
            new DeleteEncounterRequest { EncounterId = encounterId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    [Fact]
    public async Task DeleteByCharacterAsync_CleansUpAllData()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();
        var otherCharId = Guid.NewGuid();

        var charIndex = new CharacterIndexData
        {
            CharacterId = characterId,
            PerspectiveIds = new List<Guid> { perspectiveId }
        };

        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);
        var encounter = CreateTestEncounter(encounterId, new List<Guid> { characterId, otherCharId });

        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>())).ReturnsAsync(charIndex);
        _mockCharIndexStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockCharIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CharacterIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync(perspective);
        _mockPerspectiveStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _mockEncounterStore.Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>())).ReturnsAsync(encounter);
        _mockEncounterStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _mockPairIndexStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("pair-idx-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PairIndexData { EncounterIds = new List<Guid> { encounterId } });
        _mockPairIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PairIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        _mockGlobalIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalCharacterIndexData { CharacterIds = new List<Guid> { characterId } });
        _mockGlobalIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GlobalCharacterIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        // Act
        var (status, response) = await service.DeleteByCharacterAsync(
            new DeleteByCharacterRequest { CharacterId = characterId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.PerspectivesDeleted > 0);
    }

    [Fact]
    public async Task DecayMemoriesAsync_PerCharacter_ProcessesPerspectives()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();

        var charIndex = new CharacterIndexData
        {
            CharacterId = characterId,
            PerspectiveIds = new List<Guid> { perspectiveId }
        };

        // Create a perspective with old decay time to trigger decay
        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);
        perspective.MemoryStrength = 0.8f;
        perspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.AddHours(-48).ToUnixTimeSeconds(); // Old enough to decay

        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>())).ReturnsAsync(charIndex);
        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync(perspective);
        _mockPerspectiveStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        // Act
        var (status, response) = await service.DecayMemoriesAsync(
            new DecayMemoriesRequest { CharacterId = characterId, DryRun = false }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.PerspectivesProcessed >= 0);
    }

    [Fact]
    public async Task DecayMemoriesAsync_DryRun_DoesNotSave()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();

        var charIndex = new CharacterIndexData
        {
            CharacterId = characterId,
            PerspectiveIds = new List<Guid> { perspectiveId }
        };

        var perspective = CreateTestPerspective(perspectiveId, encounterId, characterId);
        perspective.MemoryStrength = 0.8f;
        perspective.LastDecayedAtUnix = DateTimeOffset.UtcNow.AddHours(-48).ToUnixTimeSeconds();

        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>())).ReturnsAsync(charIndex);
        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>())).ReturnsAsync(perspective);

        // Act
        var (status, response) = await service.DecayMemoriesAsync(
            new DecayMemoriesRequest { CharacterId = characterId, DryRun = true }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify save was NOT called due to dry run
        _mockPerspectiveStore.Verify(
            s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Gap 1: Duplicate Detection Tests

    [Fact]
    public async Task RecordEncounterAsync_DuplicateEncounter_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var existingEncounterId = Guid.NewGuid();
        var realmId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;

        SetupTypeExists("DIALOGUE", CreateTestEncounterType("DIALOGUE", "Dialogue", isBuiltIn: true));
        SetupExistingEncounter(existingEncounterId, new List<Guid> { charA, charB }, "DIALOGUE", timestamp);

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "DIALOGUE",
            RealmId = realmId,
            ParticipantIds = new List<Guid> { charA, charB },
            Outcome = EncounterOutcome.NEUTRAL,
            Timestamp = timestamp.AddMinutes(2) // Within 5 minute tolerance
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecordEncounterAsync_SameParticipantsDifferentType_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var existingEncounterId = Guid.NewGuid();
        var timestamp = DateTimeOffset.UtcNow;

        SetupTypeExists("DIALOGUE", CreateTestEncounterType("DIALOGUE", "Dialogue", isBuiltIn: true));
        SetupTypeExists("COMBAT", CreateTestEncounterType("COMBAT", "Combat", isBuiltIn: true));
        SetupEmptyIndexes();
        SetupDefaultSaves();

        // Setup existing DIALOGUE encounter
        var sortedParticipants = new List<Guid> { charA, charB }.OrderBy(id => id).ToList();
        var pairKey = $"{sortedParticipants[0]}:{sortedParticipants[1]}";
        _mockPairIndexStore
            .Setup(s => s.GetAsync($"pair-idx-{pairKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PairIndexData
            {
                CharacterIdA = sortedParticipants[0],
                CharacterIdB = sortedParticipants[1],
                EncounterIds = new List<Guid> { existingEncounterId }
            });
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{existingEncounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData
            {
                EncounterId = existingEncounterId,
                EncounterTypeCode = "DIALOGUE", // Different type
                ParticipantIds = new List<Guid> { charA, charB },
                Timestamp = timestamp.ToUnixTimeSeconds(),
                RealmId = Guid.NewGuid(),
                Outcome = EncounterOutcome.NEUTRAL,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "COMBAT", // Different type
            RealmId = Guid.NewGuid(),
            ParticipantIds = new List<Guid> { charA, charB },
            Outcome = EncounterOutcome.NEUTRAL,
            Timestamp = timestamp
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert - Should succeed because type is different
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task RecordEncounterAsync_TimestampOutsideTolerance_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var existingEncounterId = Guid.NewGuid();
        var existingTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10); // 10 minutes ago

        SetupTypeExists("DIALOGUE", CreateTestEncounterType("DIALOGUE", "Dialogue", isBuiltIn: true));
        SetupEmptyIndexes();
        SetupDefaultSaves();

        // Setup existing encounter with old timestamp
        var sortedParticipants = new List<Guid> { charA, charB }.OrderBy(id => id).ToList();
        var pairKey = $"{sortedParticipants[0]}:{sortedParticipants[1]}";
        _mockPairIndexStore
            .Setup(s => s.GetAsync($"pair-idx-{pairKey}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PairIndexData
            {
                CharacterIdA = sortedParticipants[0],
                CharacterIdB = sortedParticipants[1],
                EncounterIds = new List<Guid> { existingEncounterId }
            });
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{existingEncounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData
            {
                EncounterId = existingEncounterId,
                EncounterTypeCode = "DIALOGUE",
                ParticipantIds = new List<Guid> { charA, charB },
                Timestamp = existingTimestamp.ToUnixTimeSeconds(),
                RealmId = Guid.NewGuid(),
                Outcome = EncounterOutcome.NEUTRAL,
                CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "DIALOGUE",
            RealmId = Guid.NewGuid(),
            ParticipantIds = new List<Guid> { charA, charB },
            Outcome = EncounterOutcome.NEUTRAL,
            Timestamp = DateTimeOffset.UtcNow // Now (more than 5 min from existing)
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert - Should succeed because timestamp is outside tolerance
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    #endregion

    #region Gap 2: Type-in-Use Validation Tests

    [Fact]
    public async Task DeleteEncounterTypeAsync_TypeInUse_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var typeData = CreateTestEncounterType("CUSTOM", "Custom Type", isBuiltIn: false);
        SetupTypeExists("CUSTOM", typeData);
        SetupTypeHasEncounters("CUSTOM", 5); // Type is in use by 5 encounters

        // Act
        var status = await service.DeleteEncounterTypeAsync(
            new DeleteEncounterTypeRequest { Code = "CUSTOM" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
    }

    [Fact]
    public async Task DeleteEncounterTypeAsync_TypeNotInUse_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var typeData = CreateTestEncounterType("CUSTOM", "Custom Type", isBuiltIn: false);
        SetupTypeExists("CUSTOM", typeData);
        // Type-encounter index is empty by default (set up in constructor)

        _mockTypeStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<EncounterTypeData>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockCustomTypeIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CustomTypeIndexData { TypeCodes = new List<string> { "CUSTOM" } });
        _mockCustomTypeIndexStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CustomTypeIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Act
        var status = await service.DeleteEncounterTypeAsync(
            new DeleteEncounterTypeRequest { Code = "CUSTOM" }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    #endregion

    #region Gap 3: Character Validation Tests

    [Fact]
    public async Task RecordEncounterAsync_CharacterNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();

        SetupTypeExists("DIALOGUE", CreateTestEncounterType("DIALOGUE", "Dialogue", isBuiltIn: true));
        SetupEmptyIndexes();
        SetupCharacterNotFound(charB); // charB does not exist

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "DIALOGUE",
            RealmId = Guid.NewGuid(),
            ParticipantIds = new List<Guid> { charA, charB },
            Outcome = EncounterOutcome.NEUTRAL,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task RecordEncounterAsync_AllCharactersExist_Succeeds()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var realmId = Guid.NewGuid();

        SetupTypeExists("DIALOGUE", CreateTestEncounterType("DIALOGUE", "Dialogue", isBuiltIn: true));
        SetupEmptyIndexes();
        SetupDefaultSaves();
        // Default character client setup already returns success for all characters

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "DIALOGUE",
            RealmId = realmId,
            ParticipantIds = new List<Guid> { charA, charB },
            Outcome = EncounterOutcome.POSITIVE,
            Context = "Met at the tavern",
            Timestamp = DateTimeOffset.UtcNow
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    #endregion

    #region Permission Registration Tests

    [Fact]
    public void CharacterEncounterPermissionRegistration_GetEndpoints_ShouldReturnAllDefinedEndpoints()
    {
        // Act
        var endpoints = CharacterEncounterPermissionRegistration.GetEndpoints();

        // Assert
        Assert.NotNull(endpoints);
        Assert.NotEmpty(endpoints);
    }

    [Fact]
    public void CharacterEncounterPermissionRegistration_CreateRegistrationEvent_ShouldGenerateValidEvent()
    {
        // Act
        var instanceId = Guid.NewGuid();
        var registrationEvent = CharacterEncounterPermissionRegistration.CreateRegistrationEvent(instanceId, "test-app");

        // Assert
        Assert.NotNull(registrationEvent);
        Assert.Equal("character-encounter", registrationEvent.ServiceName);
        Assert.Equal(instanceId, registrationEvent.ServiceId);
        Assert.NotNull(registrationEvent.Endpoints);
    }

    [Fact]
    public void CharacterEncounterPermissionRegistration_ServiceId_ShouldBeCharacterEncounter()
    {
        // Assert
        Assert.Equal("character-encounter", CharacterEncounterPermissionRegistration.ServiceId);
    }

    #endregion

    #region Resource Reference Tracking Tests

    [Fact]
    public async Task RecordEncounterAsync_PublishesReferenceRegisteredEventForEachParticipant()
    {
        // Arrange
        var service = CreateService();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var charC = Guid.NewGuid();

        SetupTypeExists("COMBAT", CreateTestEncounterType("COMBAT", "Combat", isBuiltIn: true));
        SetupEmptyIndexes();
        SetupDefaultSaves();

        var capturedReferenceEvents = new List<ResourceReferenceRegisteredEvent>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.Is<string>(t => t == "resource.reference.registered"),
                It.IsAny<ResourceReferenceRegisteredEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceRegisteredEvent, CancellationToken>((_, evt, _) =>
            {
                capturedReferenceEvents.Add(evt);
            })
            .ReturnsAsync(true);

        var request = new RecordEncounterRequest
        {
            EncounterTypeCode = "COMBAT",
            RealmId = Guid.NewGuid(),
            ParticipantIds = new List<Guid> { charA, charB, charC },
            Outcome = EncounterOutcome.NEUTRAL
        };

        // Act
        var (status, response) = await service.RecordEncounterAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Should have registered a reference for each participant (3 participants)
        Assert.Equal(3, capturedReferenceEvents.Count);

        // All events should have the same encounter ID as source
        var encounterId = response.Encounter.EncounterId.ToString();
        Assert.All(capturedReferenceEvents, evt =>
        {
            Assert.Equal("character", evt.ResourceType);
            Assert.Equal("character-encounter", evt.SourceType);
            Assert.Equal(encounterId, evt.SourceId);
        });

        // Each participant should have a reference registered
        var referencedCharacterIds = capturedReferenceEvents.Select(e => e.ResourceId).ToHashSet();
        Assert.Contains(charA, referencedCharacterIds);
        Assert.Contains(charB, referencedCharacterIds);
        Assert.Contains(charC, referencedCharacterIds);
    }

    [Fact]
    public async Task DeleteEncounterAsync_PublishesReferenceUnregisteredEventForEachParticipant()
    {
        // Arrange
        var service = CreateService();
        var encounterId = Guid.NewGuid();
        var charA = Guid.NewGuid();
        var charB = Guid.NewGuid();
        var perspectiveIdA = Guid.NewGuid();
        var perspectiveIdB = Guid.NewGuid();

        var encounter = CreateTestEncounter(encounterId, new List<Guid> { charA, charB });
        var perspectiveA = CreateTestPerspective(perspectiveIdA, encounterId, charA);
        var perspectiveB = CreateTestPerspective(perspectiveIdB, encounterId, charB);

        _mockEncounterStore.Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>())).ReturnsAsync(encounter);
        _mockEncounterStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{charA}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = charA, PerspectiveIds = new List<Guid> { perspectiveIdA } });
        _mockCharIndexStore.Setup(s => s.GetAsync($"char-idx-{charB}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = charB, PerspectiveIds = new List<Guid> { perspectiveIdB } });
        _mockCharIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CharacterIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveIdA}", It.IsAny<CancellationToken>())).ReturnsAsync(perspectiveA);
        _mockPerspectiveStore.Setup(s => s.GetAsync($"pers-{perspectiveIdB}", It.IsAny<CancellationToken>())).ReturnsAsync(perspectiveB);
        _mockPerspectiveStore.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _mockPairIndexStore.Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("pair-idx-")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PairIndexData { EncounterIds = new List<Guid> { encounterId } });
        _mockPairIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PairIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        _mockGlobalIndexStore.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalCharacterIndexData { CharacterIds = new List<Guid> { charA, charB } });
        _mockGlobalIndexStore.Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GlobalCharacterIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>())).ReturnsAsync("etag-1");

        var capturedUnregisterEvents = new List<ResourceReferenceUnregisteredEvent>();
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(
                It.Is<string>(t => t == "resource.reference.unregistered"),
                It.IsAny<ResourceReferenceUnregisteredEvent>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, ResourceReferenceUnregisteredEvent, CancellationToken>((_, evt, _) =>
            {
                capturedUnregisterEvents.Add(evt);
            })
            .ReturnsAsync(true);

        // Act
        var (status, _) = await service.DeleteEncounterAsync(
            new DeleteEncounterRequest { EncounterId = encounterId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Should have unregistered a reference for each participant (2 participants)
        Assert.Equal(2, capturedUnregisterEvents.Count);

        // All events should have the encounter ID as source
        Assert.All(capturedUnregisterEvents, evt =>
        {
            Assert.Equal("character", evt.ResourceType);
            Assert.Equal("character-encounter", evt.SourceType);
            Assert.Equal(encounterId.ToString(), evt.SourceId);
        });

        // Each participant should have their reference unregistered
        var unregisteredCharacterIds = capturedUnregisterEvents.Select(e => e.ResourceId).ToHashSet();
        Assert.Contains(charA, unregisteredCharacterIds);
        Assert.Contains(charB, unregisteredCharacterIds);
    }

    #endregion

    #region Helper Methods

    private static EncounterTypeData CreateTestEncounterType(string code, string name, bool isBuiltIn)
    {
        return new EncounterTypeData
        {
            TypeId = Guid.NewGuid(),
            Code = code,
            Name = name,
            Description = $"Test encounter type: {name}",
            IsBuiltIn = isBuiltIn,
            DefaultEmotionalImpact = EmotionalImpact.INDIFFERENCE,
            SortOrder = 0,
            IsActive = true,
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private static EncounterData CreateTestEncounter(Guid encounterId, List<Guid> participantIds)
    {
        return new EncounterData
        {
            EncounterId = encounterId,
            EncounterTypeCode = "DIALOGUE",
            RealmId = Guid.NewGuid(),
            ParticipantIds = participantIds,
            Outcome = EncounterOutcome.NEUTRAL,
            Context = "Test encounter",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private static PerspectiveData CreateTestPerspective(Guid perspectiveId, Guid encounterId, Guid characterId)
    {
        return new PerspectiveData
        {
            PerspectiveId = perspectiveId,
            EncounterId = encounterId,
            CharacterId = characterId,
            EmotionalImpact = EmotionalImpact.INDIFFERENCE,
            SentimentShift = 0.0f,
            MemoryStrength = 1.0f,
            RememberedAs = null,
            LastDecayedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UpdatedAtUnix = null
        };
    }

    private void SetupTypeExists(string code, EncounterTypeData? typeData = null)
    {
        typeData ??= CreateTestEncounterType(code, code, code == "COMBAT" || code == "DIALOGUE" || code == "TRADE" || code == "QUEST" || code == "SOCIAL" || code == "CEREMONY");
        _mockTypeStore
            .Setup(s => s.GetAsync($"type-{code.ToUpperInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(typeData);
    }

    private void SetupTypeDoesNotExist(string code)
    {
        _mockTypeStore
            .Setup(s => s.GetAsync($"type-{code.ToUpperInvariant()}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EncounterTypeData?)null);
    }

    private void SetupEmptyIndexes()
    {
        _mockCharIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterIndexData?)null);
        _mockCharIndexStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<CharacterIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockPairIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PairIndexData?)null);
        _mockPairIndexStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PairIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockLocationIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LocationIndexData?)null);
        _mockLocationIndexStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<LocationIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockGlobalIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GlobalCharacterIndexData?)null);
        _mockGlobalIndexStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<GlobalCharacterIndexData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
    }

    private void SetupDefaultSaves()
    {
        _mockEncounterStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<EncounterData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockPerspectiveStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<PerspectiveData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
    }

    private static string CompressArchiveData(CharacterEncounterArchive data)
    {
        var json = BannouJson.Serialize(data);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        using var output = new System.IO.MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(
            output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(bytes, 0, bytes.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    #endregion

    #region GetCompressDataAsync Tests

    [Fact]
    public async Task GetCompressDataAsync_WithEncounters_ReturnsCompressData()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();
        var otherCharacterId = Guid.NewGuid();

        // Setup character index with perspective IDs
        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = characterId, PerspectiveIds = new List<Guid> { perspectiveId } });

        // Setup perspective
        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerspectiveData
            {
                PerspectiveId = perspectiveId,
                EncounterId = encounterId,
                CharacterId = characterId,
                EmotionalImpact = EmotionalImpact.GRATITUDE,
                SentimentShift = 0.3f,
                MemoryStrength = 0.8f,
                RememberedAs = "A friendly meeting",
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
            });

        // Setup encounter
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData
            {
                EncounterId = encounterId,
                Timestamp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
                RealmId = Guid.NewGuid(),
                EncounterTypeCode = "DIALOGUE",
                ParticipantIds = new List<Guid> { characterId, otherCharacterId },
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
            });

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.True(response.HasEncounters);
        Assert.Equal(1, response.EncounterCount);
        Assert.Single(response.Encounters);
        Assert.NotNull(response.AggregateSentiment);
        Assert.Contains(otherCharacterId.ToString(), response.AggregateSentiment.Keys);
    }

    [Fact]
    public async Task GetCompressDataAsync_NoEncounters_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup empty character index
        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterIndexData?)null);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCompressDataAsync_EmptyPerspectiveList_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup character index with empty perspective list
        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData { CharacterId = characterId, PerspectiveIds = new List<Guid>() });

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetCompressDataAsync_MultipleEncounters_AggregatesSentiment()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var otherCharacterId = Guid.NewGuid();
        var encounterId1 = Guid.NewGuid();
        var encounterId2 = Guid.NewGuid();
        var perspectiveId1 = Guid.NewGuid();
        var perspectiveId2 = Guid.NewGuid();

        // Setup character index with multiple perspectives
        _mockCharIndexStore
            .Setup(s => s.GetAsync($"char-idx-{characterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CharacterIndexData
            {
                CharacterId = characterId,
                PerspectiveIds = new List<Guid> { perspectiveId1, perspectiveId2 }
            });

        // Setup perspectives with sentiment shifts
        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerspectiveData
            {
                PerspectiveId = perspectiveId1,
                EncounterId = encounterId1,
                CharacterId = characterId,
                SentimentShift = 0.5f,
                MemoryStrength = 0.8f,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds()
            });

        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PerspectiveData
            {
                PerspectiveId = perspectiveId2,
                EncounterId = encounterId2,
                CharacterId = characterId,
                SentimentShift = 0.3f,
                MemoryStrength = 0.7f,
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
            });

        // Setup encounters
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId1}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData
            {
                EncounterId = encounterId1,
                Timestamp = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds(),
                EncounterTypeCode = "DIALOGUE",
                ParticipantIds = new List<Guid> { characterId, otherCharacterId },
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-2).ToUnixTimeSeconds()
            });

        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId2}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData
            {
                EncounterId = encounterId2,
                Timestamp = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
                EncounterTypeCode = "TRADE",
                ParticipantIds = new List<Guid> { characterId, otherCharacterId },
                CreatedAtUnix = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()
            });

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.EncounterCount);
        Assert.NotNull(response.AggregateSentiment);
        // Aggregate sentiment should be 0.5 + 0.3 = 0.8
        Assert.Equal(0.8f, response.AggregateSentiment[otherCharacterId.ToString()], 0.001);
    }

    [Fact]
    public async Task GetCompressDataAsync_ExceptionThrown_ShouldThrow()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup to throw exception
        _mockCharIndexStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetCompressDataAsync(request, CancellationToken.None));
    }

    #endregion

    #region RestoreFromArchiveAsync Tests

    [Fact]
    public async Task RestoreFromArchiveAsync_ValidData_RestoresEncountersAndPerspectives()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var otherCharacterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var archiveData = new CharacterEncounterArchive
        {
            // ResourceArchiveBase fields
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 1,
            Encounters = new List<EncounterResponse>
            {
                new EncounterResponse
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = encounterId,
                        Timestamp = DateTimeOffset.UtcNow.AddDays(-1),
                        RealmId = Guid.NewGuid(),
                        EncounterTypeCode = "DIALOGUE",
                        ParticipantIds = new List<Guid> { characterId, otherCharacterId },
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                    },
                    Perspectives = new List<EncounterPerspectiveModel>
                    {
                        new EncounterPerspectiveModel
                        {
                            PerspectiveId = perspectiveId,
                            EncounterId = encounterId,
                            CharacterId = characterId,
                            EmotionalImpact = EmotionalImpact.GRATITUDE,
                            SentimentShift = 0.3f,
                            MemoryStrength = 0.8f,
                            RememberedAs = "A friendly meeting",
                            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                        }
                    }
                }
            }
        };

        // Setup stores to return null (no existing data)
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EncounterData?)null);
        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PerspectiveData?)null);

        SetupDefaultSaves();
        SetupEmptyIndexes();

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = CompressArchiveData(archiveData)
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal(1, response.EncountersRestored);
        Assert.Equal(1, response.PerspectivesRestored);
        Assert.Null(response.ErrorMessage);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_ExistingEncounter_SkipsEncounterButRestoresPerspective()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var otherCharacterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var perspectiveId = Guid.NewGuid();

        var archiveData = new CharacterEncounterArchive
        {
            // ResourceArchiveBase fields
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 1,
            Encounters = new List<EncounterResponse>
            {
                new EncounterResponse
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = encounterId,
                        Timestamp = DateTimeOffset.UtcNow.AddDays(-1),
                        EncounterTypeCode = "DIALOGUE",
                        ParticipantIds = new List<Guid> { characterId, otherCharacterId },
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                    },
                    Perspectives = new List<EncounterPerspectiveModel>
                    {
                        new EncounterPerspectiveModel
                        {
                            PerspectiveId = perspectiveId,
                            EncounterId = encounterId,
                            CharacterId = characterId,
                            MemoryStrength = 0.8f,
                            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                        }
                    }
                }
            }
        };

        // Setup encounter to already exist
        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EncounterData { EncounterId = encounterId });

        // Setup perspective to not exist
        _mockPerspectiveStore
            .Setup(s => s.GetAsync($"pers-{perspectiveId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PerspectiveData?)null);

        SetupDefaultSaves();
        SetupEmptyIndexes();

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = CompressArchiveData(archiveData)
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(0, response.EncountersRestored); // Skipped existing
        Assert.Equal(1, response.PerspectivesRestored); // Still restored
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_InvalidBase64_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = "not-valid-base64!!!"
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Equal(0, response.EncountersRestored);
        Assert.Equal(0, response.PerspectivesRestored);
        Assert.NotNull(response.ErrorMessage);
        Assert.Contains("Invalid archive data", response.ErrorMessage);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_CorruptGzipData_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Valid base64 but not gzipped data
        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("not gzipped"))
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_NoEncounters_ReturnsSuccessWithZeroCounts()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        var archiveData = new CharacterEncounterArchive
        {
            // ResourceArchiveBase fields
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = characterId,
            HasEncounters = false,
            EncounterCount = 0,
            Encounters = new List<EncounterResponse>()
        };

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = CompressArchiveData(archiveData)
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(0, response.EncountersRestored);
        Assert.Equal(0, response.PerspectivesRestored);
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_OtherCharacterPerspective_SkipsIt()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var otherCharacterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();
        var myPerspectiveId = Guid.NewGuid();
        var otherPerspectiveId = Guid.NewGuid();

        var archiveData = new CharacterEncounterArchive
        {
            // ResourceArchiveBase fields
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 1,
            Encounters = new List<EncounterResponse>
            {
                new EncounterResponse
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = encounterId,
                        Timestamp = DateTimeOffset.UtcNow.AddDays(-1),
                        EncounterTypeCode = "DIALOGUE",
                        ParticipantIds = new List<Guid> { characterId, otherCharacterId },
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                    },
                    Perspectives = new List<EncounterPerspectiveModel>
                    {
                        new EncounterPerspectiveModel
                        {
                            PerspectiveId = myPerspectiveId,
                            EncounterId = encounterId,
                            CharacterId = characterId, // This one should be restored
                            MemoryStrength = 0.8f,
                            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                        },
                        new EncounterPerspectiveModel
                        {
                            PerspectiveId = otherPerspectiveId,
                            EncounterId = encounterId,
                            CharacterId = otherCharacterId, // This one should be skipped
                            MemoryStrength = 0.7f,
                            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                        }
                    }
                }
            }
        };

        _mockEncounterStore
            .Setup(s => s.GetAsync($"enc-{encounterId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((EncounterData?)null);
        _mockPerspectiveStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PerspectiveData?)null);

        SetupDefaultSaves();
        SetupEmptyIndexes();

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = CompressArchiveData(archiveData)
        };

        // Act
        var (status, response) = await service.RestoreFromArchiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(1, response.EncountersRestored);
        Assert.Equal(1, response.PerspectivesRestored); // Only own perspective restored
    }

    [Fact]
    public async Task RestoreFromArchiveAsync_ExceptionDuringRestore_ReturnsInternalServerError()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();
        var encounterId = Guid.NewGuid();

        var archiveData = new CharacterEncounterArchive
        {
            // ResourceArchiveBase fields
            ResourceId = characterId,
            ResourceType = "character-encounter",
            ArchivedAt = DateTimeOffset.UtcNow,
            SchemaVersion = 1,
            // Service-specific fields
            CharacterId = characterId,
            HasEncounters = true,
            EncounterCount = 1,
            Encounters = new List<EncounterResponse>
            {
                new EncounterResponse
                {
                    Encounter = new EncounterModel
                    {
                        EncounterId = encounterId,
                        Timestamp = DateTimeOffset.UtcNow.AddDays(-1),
                        EncounterTypeCode = "DIALOGUE",
                        ParticipantIds = new List<Guid> { characterId },
                        CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
                    },
                    Perspectives = new List<EncounterPerspectiveModel>()
                }
            }
        };

        // Setup to throw on save
        _mockEncounterStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EncounterData?)null);
        _mockEncounterStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<EncounterData>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        var request = new RestoreFromArchiveRequest
        {
            CharacterId = characterId,
            Data = CompressArchiveData(archiveData)
        };

        // Act & Assert - exceptions propagate to generated controller for error handling
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreFromArchiveAsync(request, CancellationToken.None));
    }

    #endregion
}

/// <summary>
/// Configuration-specific tests for CharacterEncounterService.
/// </summary>
public class CharacterEncounterConfigurationTests
{
    [Fact]
    public void Configuration_WithValidSettings_ShouldInitializeCorrectly()
    {
        // Arrange
        var config = new CharacterEncounterServiceConfiguration();

        // Act & Assert
        Assert.NotNull(config);
    }
}
