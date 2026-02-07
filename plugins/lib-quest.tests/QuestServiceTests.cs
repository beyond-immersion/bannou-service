using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Quest;
using BeyondImmersion.BannouService.Quest.Caching;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Quest.Tests;

/// <summary>
/// Unit tests for QuestService.
/// Tests quest definition and instance management operations.
/// </summary>
public class QuestServiceTests : ServiceTestBase<QuestServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IQueryableStateStore<QuestDefinitionModel>> _mockDefinitionStore;
    private readonly Mock<IQueryableStateStore<QuestInstanceModel>> _mockInstanceStore;
    private readonly Mock<IStateStore<QuestDefinitionModel>> _mockDefinitionCache;
    private readonly Mock<IStateStore<ObjectiveProgressModel>> _mockProgressStore;
    private readonly Mock<ICacheableStateStore<CharacterQuestIndex>> _mockCharacterIndex;
    private readonly Mock<IStateStore<CooldownEntry>> _mockCooldownStore;
    private readonly Mock<IStateStore<IdempotencyRecord>> _mockIdempotencyStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IContractClient> _mockContractClient;
    private readonly Mock<ICharacterClient> _mockCharacterClient;
    private readonly Mock<ICurrencyClient> _mockCurrencyClient;
    private readonly Mock<IInventoryClient> _mockInventoryClient;
    private readonly Mock<IItemClient> _mockItemClient;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<QuestService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IServiceProvider> _mockServiceProvider;
    private readonly Mock<IQuestDataCache> _mockQuestDataCache;
    private readonly List<IPrerequisiteProviderFactory> _prerequisiteProviders;

    public QuestServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockDefinitionStore = new Mock<IQueryableStateStore<QuestDefinitionModel>>();
        _mockInstanceStore = new Mock<IQueryableStateStore<QuestInstanceModel>>();
        _mockDefinitionCache = new Mock<IStateStore<QuestDefinitionModel>>();
        _mockProgressStore = new Mock<IStateStore<ObjectiveProgressModel>>();
        _mockCharacterIndex = new Mock<ICacheableStateStore<CharacterQuestIndex>>();
        _mockCooldownStore = new Mock<IStateStore<CooldownEntry>>();
        _mockIdempotencyStore = new Mock<IStateStore<IdempotencyRecord>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockContractClient = new Mock<IContractClient>();
        _mockCharacterClient = new Mock<ICharacterClient>();
        _mockCurrencyClient = new Mock<ICurrencyClient>();
        _mockInventoryClient = new Mock<IInventoryClient>();
        _mockItemClient = new Mock<IItemClient>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<QuestService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockServiceProvider = new Mock<IServiceProvider>();
        _mockQuestDataCache = new Mock<IQuestDataCache>();
        _prerequisiteProviders = new List<IPrerequisiteProviderFactory>();

        // Setup factory to return typed stores
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<QuestDefinitionModel>(StateStoreDefinitions.QuestDefinition))
            .Returns(_mockDefinitionStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetQueryableStore<QuestInstanceModel>(StateStoreDefinitions.QuestInstance))
            .Returns(_mockInstanceStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<QuestDefinitionModel>(StateStoreDefinitions.QuestDefinitionCache))
            .Returns(_mockDefinitionCache.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<ObjectiveProgressModel>(StateStoreDefinitions.QuestObjectiveProgress))
            .Returns(_mockProgressStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetCacheableStore<CharacterQuestIndex>(StateStoreDefinitions.QuestCharacterIndex))
            .Returns(_mockCharacterIndex.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<CooldownEntry>(StateStoreDefinitions.QuestCooldown))
            .Returns(_mockCooldownStore.Object);
        _mockStateStoreFactory
            .Setup(f => f.GetStore<IdempotencyRecord>(StateStoreDefinitions.QuestIdempotency))
            .Returns(_mockIdempotencyStore.Object);

        // Default message bus setup
        _mockMessageBus
            .Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Setup lock provider to always succeed
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
    }

    private QuestService CreateService()
    {
        return new QuestService(
            _mockMessageBus.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            Configuration,
            _mockContractClient.Object,
            _mockCharacterClient.Object,
            _mockCurrencyClient.Object,
            _mockInventoryClient.Object,
            _mockItemClient.Object,
            _mockLockProvider.Object,
            _mockEventConsumer.Object,
            _mockServiceProvider.Object,
            _mockQuestDataCache.Object,
            _prerequisiteProviders);
    }

    #region Constructor Validation

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
    public void QuestService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<QuestService>();

    #endregion

    #region Configuration Tests

    [Fact]
    public void QuestServiceConfiguration_CanBeInstantiated()
    {
        // Arrange & Act
        var config = new QuestServiceConfiguration();

        // Assert
        Assert.NotNull(config);
    }

    #endregion

    #region CreateQuestDefinition Tests

    [Fact]
    public async Task CreateQuestDefinitionAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidDefinitionRequest();

        // Setup: no existing definition with same code
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel>());

        // Setup: contract template creation succeeds
        var templateResponse = new ContractTemplateResponse
        {
            TemplateId = Guid.NewGuid(),
            Code = "quest_test_quest",
            Name = "Test Quest Contract",
            IsActive = true
        };
        _mockContractClient
            .Setup(c => c.CreateContractTemplateAsync(
                It.IsAny<CreateContractTemplateRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateResponse);

        QuestDefinitionModel? savedModel = null;
        _mockDefinitionStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<QuestDefinitionModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, QuestDefinitionModel, StateOptions?, CancellationToken>((k, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.CreateQuestDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(request.Code.ToUpperInvariant(), response.Code);
        Assert.Equal(request.Name, response.Name);
        Assert.NotNull(savedModel);
        Assert.Equal(request.Code.ToUpperInvariant(), savedModel.Code);
    }

    [Fact]
    public async Task CreateQuestDefinitionAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidDefinitionRequest();

        // Setup: existing definition with same code
        var existingDefinition = CreateTestDefinitionModel(Guid.NewGuid());
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { existingDefinition });

        // Act
        var (status, response) = await service.CreateQuestDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateQuestDefinitionAsync_EmptyCode_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidDefinitionRequest();
        request.Code = "";

        // Act
        var (status, response) = await service.CreateQuestDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CreateQuestDefinitionAsync_ContractTemplateConflict_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidDefinitionRequest();

        // Setup: no existing quest definition
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel>());

        // Setup: contract template creation returns conflict
        _mockContractClient
            .Setup(c => c.CreateContractTemplateAsync(
                It.IsAny<CreateContractTemplateRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException("Conflict", 409));

        // Act
        var (status, response) = await service.CreateQuestDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region GetQuestDefinition Tests

    [Fact]
    public async Task GetQuestDefinitionAsync_ById_ExistingDefinition_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var model = CreateTestDefinitionModel(definitionId);

        // Setup: cache miss
        _mockDefinitionCache
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(definitionId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuestDefinitionModel?)null);

        // Setup: found in main store
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { model });

        // Act
        var (status, response) = await service.GetQuestDefinitionAsync(
            new GetQuestDefinitionRequest { DefinitionId = definitionId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(definitionId, response.DefinitionId);
    }

    [Fact]
    public async Task GetQuestDefinitionAsync_ById_CacheHit_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var model = CreateTestDefinitionModel(definitionId);

        // Setup: cache hit
        _mockDefinitionCache
            .Setup(s => s.GetAsync(It.Is<string>(k => k.Contains(definitionId.ToString())), It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetQuestDefinitionAsync(
            new GetQuestDefinitionRequest { DefinitionId = definitionId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(definitionId, response.DefinitionId);

        // Verify main store was never queried
        _mockDefinitionStore.Verify(s => s.QueryAsync(
            It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetQuestDefinitionAsync_ById_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();

        // Setup: cache miss
        _mockDefinitionCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuestDefinitionModel?)null);

        // Setup: not found in main store
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel>());

        // Act
        var (status, response) = await service.GetQuestDefinitionAsync(
            new GetQuestDefinitionRequest { DefinitionId = definitionId }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetQuestDefinitionAsync_ByCode_ExistingDefinition_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var code = "test_quest";
        var model = CreateTestDefinitionModel(definitionId, code.ToUpperInvariant());

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { model });

        // Act
        var (status, response) = await service.GetQuestDefinitionAsync(
            new GetQuestDefinitionRequest { Code = code }, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(code.ToUpperInvariant(), response.Code);
    }

    [Fact]
    public async Task GetQuestDefinitionAsync_NeitherIdNorCode_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (status, response) = await service.GetQuestDefinitionAsync(
            new GetQuestDefinitionRequest(), CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateQuestDefinition Tests

    [Fact]
    public async Task UpdateQuestDefinitionAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var existingModel = CreateTestDefinitionModel(definitionId);

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { existingModel });

        _mockDefinitionStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "etag123"));

        _mockDefinitionStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(),
                It.IsAny<QuestDefinitionModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        var request = new UpdateQuestDefinitionRequest
        {
            DefinitionId = definitionId,
            Name = "Updated Quest Name",
            Description = "Updated description"
        };

        // Act
        var (status, response) = await service.UpdateQuestDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Quest Name", response.Name);
    }

    [Fact]
    public async Task UpdateQuestDefinitionAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel>());

        var request = new UpdateQuestDefinitionRequest
        {
            DefinitionId = definitionId,
            Name = "Updated Quest Name"
        };

        // Act
        var (status, response) = await service.UpdateQuestDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region DeprecateQuestDefinition Tests

    [Fact]
    public async Task DeprecateQuestDefinitionAsync_ExistingDefinition_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var existingModel = CreateTestDefinitionModel(definitionId);
        existingModel.Deprecated = false;

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { existingModel });

        _mockDefinitionStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((existingModel, "etag123"));

        _mockDefinitionStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(),
                It.IsAny<QuestDefinitionModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        var request = new DeprecateQuestDefinitionRequest { DefinitionId = definitionId };

        // Act
        var (status, response) = await service.DeprecateQuestDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Deprecated);
    }

    [Fact]
    public async Task DeprecateQuestDefinitionAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel>());

        var request = new DeprecateQuestDefinitionRequest { DefinitionId = definitionId };

        // Act
        var (status, response) = await service.DeprecateQuestDefinitionAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ListQuestDefinitions Tests

    [Fact]
    public async Task ListQuestDefinitionsAsync_ReturnsFilteredDefinitions()
    {
        // Arrange
        var service = CreateService();
        var definition1 = CreateTestDefinitionModel(Guid.NewGuid(), "QUEST_1");
        var definition2 = CreateTestDefinitionModel(Guid.NewGuid(), "QUEST_2");

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { definition1, definition2 });

        var request = new ListQuestDefinitionsRequest { Limit = 50, Offset = 0 };

        // Act
        var (status, response) = await service.ListQuestDefinitionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Definitions.Count);
    }

    [Fact]
    public async Task ListQuestDefinitionsAsync_WithCategoryFilter_ReturnsFiltered()
    {
        // Arrange
        var service = CreateService();
        var definition = CreateTestDefinitionModel(Guid.NewGuid());
        definition.Category = QuestCategory.MAIN;

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { definition });

        var request = new ListQuestDefinitionsRequest { Category = QuestCategory.MAIN };

        // Act
        var (status, response) = await service.ListQuestDefinitionsAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Definitions);
        Assert.Equal(QuestCategory.MAIN, response.Definitions.First().Category);
    }

    #endregion

    #region AcceptQuest Tests

    [Fact]
    public async Task AcceptQuestAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var definition = CreateTestDefinitionModel(definitionId);
        var gameServiceId = Guid.NewGuid();
        definition.GameServiceId = gameServiceId;

        // Setup: definition exists and is not deprecated
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { definition });

        // Setup: character index shows no current quests
        _mockCharacterIndex
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterQuestIndex?)null);

        // Setup: contract instance creation succeeds
        var contractResponse = new ContractInstanceResponse
        {
            ContractId = Guid.NewGuid(),
            Status = ContractStatus.Active
        };
        _mockContractClient
            .Setup(c => c.CreateContractInstanceAsync(
                It.IsAny<CreateContractInstanceRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(contractResponse);

        // Setup: consent call succeeds
        _mockContractClient
            .Setup(c => c.ConsentToContractAsync(
                It.IsAny<ConsentToContractRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(contractResponse);

        // Setup: instance store saves
        _mockInstanceStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<QuestInstanceModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup: character index saves
        _mockCharacterIndex
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterQuestIndex>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup: progress store saves
        _mockProgressStore
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<ObjectiveProgressModel>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new AcceptQuestRequest
        {
            DefinitionId = definitionId,
            QuestorCharacterId = characterId
        };

        // Act
        var (status, response) = await service.AcceptQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(QuestStatus.ACTIVE, response.Status);
        Assert.Contains(characterId, response.QuestorCharacterIds);
    }

    [Fact]
    public async Task AcceptQuestAsync_DefinitionNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel>());

        var request = new AcceptQuestRequest
        {
            DefinitionId = definitionId,
            QuestorCharacterId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.AcceptQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AcceptQuestAsync_DeprecatedDefinition_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var definition = CreateTestDefinitionModel(definitionId);
        definition.Deprecated = true;

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { definition });

        var request = new AcceptQuestRequest
        {
            DefinitionId = definitionId,
            QuestorCharacterId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.AcceptQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AcceptQuestAsync_MaxQuestsExceeded_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var definition = CreateTestDefinitionModel(definitionId);

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { definition });

        // Setup: character already at max quests
        var maxQuestIds = Enumerable.Range(0, Configuration.MaxActiveQuestsPerCharacter)
            .Select(_ => Guid.NewGuid())
            .ToList();
        var characterIndex = new CharacterQuestIndex
        {
            CharacterId = characterId,
            ActiveQuestIds = maxQuestIds
        };
        _mockCharacterIndex
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(characterIndex);

        var request = new AcceptQuestRequest
        {
            DefinitionId = definitionId,
            QuestorCharacterId = characterId
        };

        // Act
        var (status, response) = await service.AcceptQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AcceptQuestAsync_OnCooldown_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var definitionId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var definition = CreateTestDefinitionModel(definitionId);
        definition.Repeatable = true;
        definition.CooldownSeconds = 3600; // 1 hour cooldown

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { definition });

        // Setup: no current quests
        _mockCharacterIndex
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterQuestIndex?)null);

        // Setup: cooldown still active
        var cooldownEntry = new CooldownEntry
        {
            CharacterId = characterId,
            QuestCode = definition.Code,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        _mockCooldownStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cooldownEntry);

        var request = new AcceptQuestRequest
        {
            DefinitionId = definitionId,
            QuestorCharacterId = characterId
        };

        // Act
        var (status, response) = await service.AcceptQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region AbandonQuest Tests

    [Fact]
    public async Task AbandonQuestAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.Status = QuestStatus.ACTIVE;

        // Setup: quest instance exists - service uses GetWithETagAsync for direct key lookup
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag123"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(),
                It.IsAny<QuestInstanceModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        // Setup: contract termination succeeds
        _mockContractClient
            .Setup(c => c.TerminateContractInstanceAsync(
                It.IsAny<TerminateContractInstanceRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ContractInstanceResponse { ContractId = instance.ContractInstanceId });

        // Setup: character index for cleanup
        var characterIndex = new CharacterQuestIndex
        {
            CharacterId = characterId,
            ActiveQuestIds = new List<Guid> { instanceId }
        };
        _mockCharacterIndex
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(characterIndex);
        _mockCharacterIndex
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterQuestIndex>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        // Setup: definition lookup for response mapping (GetDefinitionModelAsync checks cache then store)
        var definition = CreateTestDefinitionModel(instance.DefinitionId);
        _mockDefinitionCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        // Setup: progress store for objectives
        _mockProgressStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ObjectiveProgressModel?)null);

        var request = new AbandonQuestRequest
        {
            QuestInstanceId = instanceId,
            QuestorCharacterId = characterId
        };

        // Act
        var (status, response) = await service.AbandonQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(QuestStatus.ABANDONED, response.Status);
    }

    [Fact]
    public async Task AbandonQuestAsync_QuestNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();

        // Setup: quest instance not found - service uses GetWithETagAsync for direct key lookup
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((QuestInstanceModel?)null, (string?)null));

        var request = new AbandonQuestRequest
        {
            QuestInstanceId = instanceId,
            QuestorCharacterId = Guid.NewGuid()
        };

        // Act
        var (status, response) = await service.AbandonQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task AbandonQuestAsync_NotActive_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.Status = QuestStatus.COMPLETED;

        // Setup: quest instance exists but not active - service uses GetWithETagAsync
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag123"));

        var request = new AbandonQuestRequest
        {
            QuestInstanceId = instanceId,
            QuestorCharacterId = characterId
        };

        // Act
        var (status, response) = await service.AbandonQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region GetQuest Tests

    [Fact]
    public async Task GetQuestAsync_ExistingQuest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, characterId);

        // Setup: quest instance exists - service uses GetAsync for direct key lookup
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        // Setup: definition lookup (GetDefinitionModelAsync checks cache then store)
        var definition = CreateTestDefinitionModel(instance.DefinitionId);
        _mockDefinitionCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        // Setup: progress store for objectives
        _mockProgressStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ObjectiveProgressModel?)null);

        var request = new GetQuestRequest { QuestInstanceId = instanceId };

        // Act
        var (status, response) = await service.GetQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(instanceId, response.QuestInstanceId);
    }

    [Fact]
    public async Task GetQuestAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();

        // Setup: quest instance not found - service uses GetAsync for direct key lookup
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuestInstanceModel?)null);

        var request = new GetQuestRequest { QuestInstanceId = instanceId };

        // Act
        var (status, response) = await service.GetQuestAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ReportObjectiveProgress Tests

    [Fact]
    public async Task ReportObjectiveProgressAsync_ValidProgress_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var objectiveCode = "KILL_WOLVES";
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.Status = QuestStatus.ACTIVE;

        var progress = new ObjectiveProgressModel
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode,
            Name = "Kill Wolves",
            ObjectiveType = ObjectiveType.KILL,
            CurrentCount = 3,
            RequiredCount = 10,
            IsComplete = false,
            TrackedEntityIds = new HashSet<Guid>()
        };

        // Setup: quest instance exists - service uses GetAsync for direct key lookup
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        // Setup: progress exists
        _mockProgressStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((progress, "etag123"));

        _mockProgressStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(),
                It.IsAny<ObjectiveProgressModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        var request = new ReportProgressRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode,
            IncrementBy = 2
        };

        // Act
        var (status, response) = await service.ReportObjectiveProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(5, response.Objective.CurrentCount); // 3 + 2
        Assert.False(response.Objective.IsComplete);
    }

    [Fact]
    public async Task ReportObjectiveProgressAsync_CompletesObjective_ReturnsOKWithCompletion()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var objectiveCode = "KILL_WOLVES";
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.Status = QuestStatus.ACTIVE;

        var progress = new ObjectiveProgressModel
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode,
            Name = "Kill Wolves",
            ObjectiveType = ObjectiveType.KILL,
            CurrentCount = 8,
            RequiredCount = 10,
            IsComplete = false,
            TrackedEntityIds = new HashSet<Guid>()
        };

        // Setup: quest instance exists - service uses GetAsync for direct key lookup
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockProgressStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((progress, "etag123"));

        _mockProgressStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(),
                It.IsAny<ObjectiveProgressModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        // Setup: contract milestone completion
        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(
                It.IsAny<CompleteMilestoneRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse { ContractId = instance.ContractInstanceId });

        var request = new ReportProgressRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode,
            IncrementBy = 5 // More than needed - should cap at RequiredCount
        };

        // Act
        var (status, response) = await service.ReportObjectiveProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(10, response.Objective.CurrentCount); // Capped at RequiredCount
        Assert.True(response.Objective.IsComplete);
    }

    [Fact]
    public async Task ReportObjectiveProgressAsync_QuestNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();

        // Setup: quest instance not found - service uses GetAsync for direct key lookup
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuestInstanceModel?)null);

        var request = new ReportProgressRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = "KILL_WOLVES",
            IncrementBy = 1
        };

        // Act
        var (status, response) = await service.ReportObjectiveProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ReportObjectiveProgressAsync_QuestNotActive_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.Status = QuestStatus.COMPLETED;

        // Setup: quest instance exists but not active - service uses GetAsync
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        var request = new ReportProgressRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = "KILL_WOLVES",
            IncrementBy = 1
        };

        // Act
        var (status, response) = await service.ReportObjectiveProgressAsync(request, CancellationToken.None);

        // Assert - BadRequest for client error (reporting progress on non-active quest)
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ReportObjectiveProgressAsync_ObjectiveNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.Status = QuestStatus.ACTIVE;

        // Setup: quest instance exists - service uses GetAsync
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        // Setup: objective progress not found
        _mockProgressStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((ObjectiveProgressModel?)null, (string?)null));

        var request = new ReportProgressRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = "NON_EXISTENT_OBJECTIVE",
            IncrementBy = 1
        };

        // Act
        var (status, response) = await service.ReportObjectiveProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ReportObjectiveProgressAsync_WithTrackedEntity_PreventsDuplicateCounting()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var objectiveCode = "KILL_WOLVES";
        var trackedEntityId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.Status = QuestStatus.ACTIVE;

        var progress = new ObjectiveProgressModel
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode,
            Name = "Kill Wolves",
            ObjectiveType = ObjectiveType.KILL,
            CurrentCount = 3,
            RequiredCount = 10,
            IsComplete = false,
            TrackedEntityIds = new HashSet<Guid> { trackedEntityId }
        };

        // Setup: quest instance exists - service uses GetAsync
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockProgressStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((progress, "etag123"));

        var request = new ReportProgressRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode,
            IncrementBy = 1,
            TrackedEntityId = trackedEntityId // Already tracked
        };

        // Act
        var (status, response) = await service.ReportObjectiveProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.Objective.CurrentCount); // Unchanged - duplicate entity
    }

    #endregion

    #region ForceCompleteObjective Tests

    [Fact]
    public async Task ForceCompleteObjectiveAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var objectiveCode = "KILL_WOLVES";
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.Status = QuestStatus.ACTIVE;

        var progress = new ObjectiveProgressModel
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode,
            Name = "Kill Wolves",
            ObjectiveType = ObjectiveType.KILL,
            CurrentCount = 3,
            RequiredCount = 10,
            IsComplete = false,
            TrackedEntityIds = new HashSet<Guid>()
        };

        // Setup: quest instance exists - service uses GetAsync
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockProgressStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((progress, "etag123"));

        _mockProgressStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(),
                It.IsAny<ObjectiveProgressModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        _mockContractClient
            .Setup(c => c.CompleteMilestoneAsync(
                It.IsAny<CompleteMilestoneRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MilestoneResponse { ContractId = instance.ContractInstanceId });

        var request = new ForceCompleteObjectiveRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode
        };

        // Act
        var (status, response) = await service.ForceCompleteObjectiveAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(10, response.Objective.CurrentCount); // Set to RequiredCount
        Assert.True(response.Objective.IsComplete);
    }

    #endregion

    #region GetObjectiveProgress Tests

    [Fact]
    public async Task GetObjectiveProgressAsync_ExistingProgress_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var objectiveCode = "KILL_WOLVES";
        var instance = CreateTestInstanceModel(instanceId, characterId);

        var progress = new ObjectiveProgressModel
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode,
            Name = "Kill Wolves",
            ObjectiveType = ObjectiveType.KILL,
            CurrentCount = 5,
            RequiredCount = 10,
            IsComplete = false,
            TrackedEntityIds = new HashSet<Guid>()
        };

        // Setup: quest instance exists - service uses GetAsync
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockProgressStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        var request = new GetObjectiveProgressRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = objectiveCode
        };

        // Act
        var (status, response) = await service.GetObjectiveProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(5, response.Objective.CurrentCount);
        Assert.Equal(10, response.Objective.RequiredCount);
    }

    [Fact]
    public async Task GetObjectiveProgressAsync_ObjectiveNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, characterId);

        // Setup: quest instance exists - service uses GetAsync
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockProgressStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ObjectiveProgressModel?)null);

        var request = new GetObjectiveProgressRequest
        {
            QuestInstanceId = instanceId,
            ObjectiveCode = "NON_EXISTENT"
        };

        // Act
        var (status, response) = await service.GetObjectiveProgressAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Event Handler Tests

    [Fact]
    public async Task HandleMilestoneCompletedAsync_ValidCallback_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, Guid.NewGuid());
        instance.ContractInstanceId = contractId;
        instance.Status = QuestStatus.ACTIVE;

        _mockInstanceStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestInstanceModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestInstanceModel> { instance });

        var callback = new MilestoneCompletedCallback
        {
            ContractInstanceId = contractId,
            MilestoneCode = "KILL_WOLVES"
        };

        // Act
        var status = await service.HandleMilestoneCompletedAsync(callback, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task HandleQuestCompletedAsync_ValidCallback_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var characterId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(instanceId, characterId);
        instance.ContractInstanceId = contractId;
        instance.Status = QuestStatus.ACTIVE;

        _mockInstanceStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestInstanceModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestInstanceModel> { instance });

        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag123"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(
                It.IsAny<string>(),
                It.IsAny<QuestInstanceModel>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-etag");

        // Setup: definition for response
        var definition = CreateTestDefinitionModel(instance.DefinitionId);
        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { definition });

        // Setup: character index
        var characterIndex = new CharacterQuestIndex
        {
            CharacterId = characterId,
            ActiveQuestIds = new List<Guid> { instanceId },
            CompletedQuestCodes = new List<string>()
        };
        _mockCharacterIndex
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(characterIndex);
        _mockCharacterIndex
            .Setup(s => s.SaveAsync(
                It.IsAny<string>(),
                It.IsAny<CharacterQuestIndex>(),
                It.IsAny<StateOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var callback = new QuestCompletedCallback
        {
            ContractInstanceId = contractId
        };

        // Act
        var status = await service.HandleQuestCompletedAsync(callback, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    #endregion

    #region GetCompressData Tests

    [Fact]
    public async Task GetCompressDataAsync_WithActiveAndCompletedQuests_ReturnsArchiveModel()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup: character index with active and completed quests
        var activeQuestId = Guid.NewGuid();
        var completedQuestCode = "COMPLETED_QUEST";
        var characterIndex = new CharacterQuestIndex
        {
            CharacterId = characterId,
            ActiveQuestIds = new List<Guid> { activeQuestId },
            CompletedQuestCodes = new List<string> { completedQuestCode }
        };
        _mockCharacterIndex
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(characterIndex);

        // Setup: active quest instance
        var activeInstance = CreateTestInstanceModel(activeQuestId, characterId);
        activeInstance.Status = QuestStatus.ACTIVE;
        _mockInstanceStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeInstance);

        // Setup: definition for active quest
        var definition = CreateTestDefinitionModel(activeInstance.DefinitionId);
        definition.Category = QuestCategory.MAIN;
        _mockDefinitionCache
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);

        // Setup: objective progress
        var progress = new ObjectiveProgressModel
        {
            QuestInstanceId = activeQuestId,
            ObjectiveCode = "KILL_WOLVES",
            Name = "Kill Wolves",
            ObjectiveType = ObjectiveType.KILL,
            CurrentCount = 5,
            RequiredCount = 10,
            IsComplete = false,
            TrackedEntityIds = new HashSet<Guid>()
        };
        _mockProgressStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Equal("quest", response.ResourceType);
        Assert.Single(response.ActiveQuests);
        Assert.Equal(1, response.CompletedQuests);
    }

    [Fact]
    public async Task GetCompressDataAsync_WithNoQuests_ReturnsEmptyArchive()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup: no character index (no quests)
        _mockCharacterIndex
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CharacterQuestIndex?)null);

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(characterId, response.CharacterId);
        Assert.Empty(response.ActiveQuests);
        Assert.Equal(0, response.CompletedQuests);
        Assert.Empty(response.QuestCategories);
    }

    [Fact]
    public async Task GetCompressDataAsync_AggregatesCategories_Correctly()
    {
        // Arrange
        var service = CreateService();
        var characterId = Guid.NewGuid();

        // Setup: character index with completed quests from different categories
        var characterIndex = new CharacterQuestIndex
        {
            CharacterId = characterId,
            ActiveQuestIds = new List<Guid>(),
            CompletedQuestCodes = new List<string> { "MAIN_1", "MAIN_2", "SIDE_1" }
        };
        _mockCharacterIndex
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(characterIndex);

        // Setup: definitions for completed quests
        var mainDef1 = CreateTestDefinitionModel(Guid.NewGuid(), "MAIN_1");
        mainDef1.Category = QuestCategory.MAIN;
        var mainDef2 = CreateTestDefinitionModel(Guid.NewGuid(), "MAIN_2");
        mainDef2.Category = QuestCategory.MAIN;
        var sideDef = CreateTestDefinitionModel(Guid.NewGuid(), "SIDE_1");
        sideDef.Category = QuestCategory.SIDE;

        _mockDefinitionStore
            .Setup(s => s.QueryAsync(
                It.IsAny<System.Linq.Expressions.Expression<Func<QuestDefinitionModel, bool>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuestDefinitionModel> { mainDef1, mainDef2, sideDef });

        var request = new GetCompressDataRequest { CharacterId = characterId };

        // Act
        var (status, response) = await service.GetCompressDataAsync(request, CancellationToken.None);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(3, response.CompletedQuests);
        Assert.True(response.QuestCategories.ContainsKey("MAIN"));
        Assert.True(response.QuestCategories.ContainsKey("SIDE"));
        Assert.Equal(2, response.QuestCategories["MAIN"]);
        Assert.Equal(1, response.QuestCategories["SIDE"]);
    }

    #endregion

    #region Helper Methods

    private static CreateQuestDefinitionRequest CreateValidDefinitionRequest()
    {
        return new CreateQuestDefinitionRequest
        {
            Code = "test_quest",
            Name = "Test Quest",
            Description = "A test quest for unit testing",
            Category = QuestCategory.SIDE,
            Difficulty = QuestDifficulty.NORMAL,
            Repeatable = false,
            MaxQuestors = 1,
            GameServiceId = Guid.NewGuid(),
            Objectives = new List<ObjectiveDefinition>
            {
                new()
                {
                    Code = "KILL_WOLVES",
                    Name = "Kill Wolves",
                    Description = "Kill 10 wolves",
                    ObjectiveType = ObjectiveType.KILL,
                    RequiredCount = 10,
                    TargetEntityType = "creature",
                    TargetEntitySubtype = "wolf"
                }
            }
        };
    }

    private static QuestDefinitionModel CreateTestDefinitionModel(Guid definitionId, string code = "TEST_QUEST")
    {
        return new QuestDefinitionModel
        {
            DefinitionId = definitionId,
            ContractTemplateId = Guid.NewGuid(),
            Code = code,
            Name = "Test Quest",
            Description = "A test quest",
            Category = QuestCategory.SIDE,
            Difficulty = QuestDifficulty.NORMAL,
            LevelRequirement = null,
            Repeatable = false,
            CooldownSeconds = null,
            DeadlineSeconds = null,
            MaxQuestors = 1,
            Objectives = new List<ObjectiveDefinitionModel>
            {
                new()
                {
                    Code = "KILL_WOLVES",
                    Name = "Kill Wolves",
                    Description = "Kill 10 wolves",
                    ObjectiveType = ObjectiveType.KILL,
                    RequiredCount = 10,
                    TargetEntityType = "creature",
                    TargetEntitySubtype = "wolf",
                    Hidden = false,
                    RevealBehavior = ObjectiveRevealBehavior.ALWAYS,
                    Optional = false
                }
            },
            Prerequisites = null,
            Rewards = null,
            Tags = null,
            QuestGiverCharacterId = null,
            GameServiceId = Guid.NewGuid(),
            Deprecated = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static QuestInstanceModel CreateTestInstanceModel(Guid instanceId, Guid questorCharacterId)
    {
        var definitionId = Guid.NewGuid();
        return new QuestInstanceModel
        {
            QuestInstanceId = instanceId,
            DefinitionId = definitionId,
            ContractInstanceId = Guid.NewGuid(),
            Code = "TEST_QUEST",
            Name = "Test Quest",
            Status = QuestStatus.ACTIVE,
            QuestorCharacterIds = new List<Guid> { questorCharacterId },
            QuestGiverCharacterId = null,
            AcceptedAt = DateTimeOffset.UtcNow,
            Deadline = null,
            CompletedAt = null,
            GameServiceId = Guid.NewGuid()
        };
    }

    #endregion
}
