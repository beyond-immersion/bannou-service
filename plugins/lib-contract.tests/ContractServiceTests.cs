using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Location;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Testing;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Contract.Tests;

/// <summary>
/// Unit tests for ContractService.
/// Tests contract template and instance management operations.
/// </summary>
public class ContractServiceTests : ServiceTestBase<ContractServiceConfiguration>
{
    private readonly Mock<IStateStoreFactory> _mockStateStoreFactory;
    private readonly Mock<IStateStore<ContractTemplateModel>> _mockTemplateStore;
    private readonly Mock<IStateStore<ContractInstanceModel>> _mockInstanceStore;
    private readonly Mock<IStateStore<BreachModel>> _mockBreachStore;
    private readonly Mock<IStateStore<string>> _mockStringStore;
    private readonly Mock<IStateStore<List<string>>> _mockListStore;
    private readonly Mock<IStateStore<ClauseTypeModel>> _mockClauseTypeStore;
    private readonly Mock<IStateStore<LockContractResponse>> _mockLockResponseStore;
    private readonly Mock<IStateStore<UnlockContractResponse>> _mockUnlockResponseStore;
    private readonly Mock<IStateStore<TransferContractPartyResponse>> _mockTransferResponseStore;
    private readonly Mock<IStateStore<ExecuteContractResponse>> _mockExecuteResponseStore;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IServiceNavigator> _mockNavigator;
    private readonly Mock<IDistributedLockProvider> _mockLockProvider;
    private readonly Mock<ILogger<ContractService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<ILocationClient> _mockLocationClient;

    private const string STATE_STORE = "contract-statestore";

    public ContractServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockTemplateStore = new Mock<IStateStore<ContractTemplateModel>>();
        _mockInstanceStore = new Mock<IStateStore<ContractInstanceModel>>();
        _mockBreachStore = new Mock<IStateStore<BreachModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockClauseTypeStore = new Mock<IStateStore<ClauseTypeModel>>();
        _mockLockResponseStore = new Mock<IStateStore<LockContractResponse>>();
        _mockUnlockResponseStore = new Mock<IStateStore<UnlockContractResponse>>();
        _mockTransferResponseStore = new Mock<IStateStore<TransferContractPartyResponse>>();
        _mockExecuteResponseStore = new Mock<IStateStore<ExecuteContractResponse>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockNavigator = new Mock<IServiceNavigator>();
        _mockLockProvider = new Mock<IDistributedLockProvider>();
        _mockLogger = new Mock<ILogger<ContractService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockLocationClient = new Mock<ILocationClient>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<ContractTemplateModel>(STATE_STORE)).Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<ContractInstanceModel>(STATE_STORE)).Returns(_mockInstanceStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BreachModel>(STATE_STORE)).Returns(_mockBreachStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockListStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<ClauseTypeModel>(STATE_STORE)).Returns(_mockClauseTypeStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<LockContractResponse>(STATE_STORE)).Returns(_mockLockResponseStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<UnlockContractResponse>(STATE_STORE)).Returns(_mockUnlockResponseStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<TransferContractPartyResponse>(STATE_STORE)).Returns(_mockTransferResponseStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<ExecuteContractResponse>(STATE_STORE)).Returns(_mockExecuteResponseStore.Object);

        // Default message bus setup
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
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

    private ContractService CreateService()
    {
        return new ContractService(
            _mockMessageBus.Object,
            _mockNavigator.Object,
            _mockStateStoreFactory.Object,
            _mockLockProvider.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object,
            _mockLocationClient.Object);
    }

    #region Constructor Tests

    /// <summary>
    /// Validates the service constructor follows proper DI patterns.
    /// </summary>
    [Fact]
    public void ContractService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<ContractService>();

    #endregion

    #region CreateContractTemplate Tests

    [Fact]
    public async Task CreateContractTemplateAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidTemplateRequest();

        // Setup: no existing template with same code
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template-code:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Setup: no existing templates list
        _mockListStore
            .Setup(s => s.GetAsync("all-templates", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        ContractTemplateModel? savedModel = null;
        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContractTemplateModel, StateOptions?, CancellationToken>((k, m, _, _) => savedModel = m)
            .ReturnsAsync("etag");

        // Act
        var (status, response) = await service.CreateContractTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(request.Code, response.Code);
        Assert.Equal(request.Name, response.Name);
        Assert.NotNull(savedModel);
        Assert.Equal(request.Code, savedModel.Code);
    }

    [Fact]
    public async Task CreateContractTemplateAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidTemplateRequest();

        // Setup: existing template with same code
        _mockStringStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("template-code:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid().ToString());

        // Act
        var (status, response) = await service.CreateContractTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region GetContractTemplate Tests

    [Fact]
    public async Task GetContractTemplateAsync_ById_ExistingTemplate_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateTestTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetContractTemplateAsync(
            new GetContractTemplateRequest { TemplateId = templateId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(templateId, response.TemplateId);
    }

    [Fact]
    public async Task GetContractTemplateAsync_ById_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractTemplateModel?)null);

        // Act
        var (status, response) = await service.GetContractTemplateAsync(
            new GetContractTemplateRequest { TemplateId = templateId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GetContractTemplateAsync_ByCode_ExistingTemplate_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var code = "test_template";
        var model = CreateTestTemplateModel(templateId, code);

        _mockStringStore
            .Setup(s => s.GetAsync($"template-code:{code}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(templateId.ToString());

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        // Act
        var (status, response) = await service.GetContractTemplateAsync(
            new GetContractTemplateRequest { Code = code });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(code, response.Code);
    }

    [Fact]
    public async Task GetContractTemplateAsync_NeitherIdNorCode_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();

        // Act
        var (status, response) = await service.GetContractTemplateAsync(
            new GetContractTemplateRequest());

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region DeleteContractTemplate Tests

    [Fact]
    public async Task DeleteContractTemplateAsync_ExistingTemplate_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateTestTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockListStore
            .Setup(s => s.GetAsync("all-templates", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { templateId.ToString() });

        // Act
        var status = await service.DeleteContractTemplateAsync(
            new DeleteContractTemplateRequest { TemplateId = templateId });

        // Assert
        Assert.Equal(StatusCodes.OK, status);
    }

    [Fact]
    public async Task DeleteContractTemplateAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractTemplateModel?)null);

        // Act
        var status = await service.DeleteContractTemplateAsync(
            new DeleteContractTemplateRequest { TemplateId = templateId });

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
    }

    #endregion

    #region CreateContractInstance Tests

    [Fact]
    public async Task CreateContractInstanceAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var template = CreateTestTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        ContractInstanceModel? savedInstance = null;
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContractInstanceModel, StateOptions?, CancellationToken>((k, m, _, _) => savedInstance = m)
            .ReturnsAsync("etag");

        _mockListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        var request = new CreateContractInstanceRequest
        {
            TemplateId = templateId,
            Parties = new List<ContractPartyInput>
            {
                new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employer" },
                new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employee" }
            }
        };

        // Act
        var (status, response) = await service.CreateContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(ContractStatus.Draft, response.Status);
        Assert.NotNull(savedInstance);
    }

    [Fact]
    public async Task CreateContractInstanceAsync_TemplateNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractTemplateModel?)null);

        var request = new CreateContractInstanceRequest
        {
            TemplateId = templateId,
            Parties = new List<ContractPartyInput>
            {
                new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employer" },
                new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employee" }
            }
        };

        // Act
        var (status, response) = await service.CreateContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ListContractTemplates Tests

    [Fact]
    public async Task ListContractTemplatesAsync_ReturnsAllTemplates()
    {
        // Arrange
        var service = CreateService();
        var templateId1 = Guid.NewGuid();
        var templateId2 = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync("all-templates", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { templateId1.ToString(), templateId2.ToString() });

        var template1 = CreateTestTemplateModel(templateId1, "template_1");
        var template2 = CreateTestTemplateModel(templateId2, "template_2");

        _mockTemplateStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ContractTemplateModel>
            {
                [$"template:{templateId1}"] = template1,
                [$"template:{templateId2}"] = template2
            });

        // Act
        var (status, response) = await service.ListContractTemplatesAsync(new ListContractTemplatesRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.Templates.Count);
    }

    [Fact]
    public async Task ListContractTemplatesAsync_EmptyList_ReturnsEmptyResponse()
    {
        // Arrange
        var service = CreateService();

        _mockListStore
            .Setup(s => s.GetAsync("all-templates", It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        // Act
        var (status, response) = await service.ListContractTemplatesAsync(new ListContractTemplatesRequest());

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Templates);
    }

    #endregion

    #region UpdateContractTemplate Tests

    [Fact]
    public async Task UpdateContractTemplateAsync_ExistingTemplate_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();
        var model = CreateTestTemplateModel(templateId);

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(model);

        _mockTemplateStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractTemplateModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new UpdateContractTemplateRequest
        {
            TemplateId = templateId,
            Name = "Updated Name",
            Description = "Updated description"
        };

        // Act
        var (status, response) = await service.UpdateContractTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("Updated Name", response.Name);
    }

    [Fact]
    public async Task UpdateContractTemplateAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var templateId = Guid.NewGuid();

        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractTemplateModel?)null);

        var request = new UpdateContractTemplateRequest
        {
            TemplateId = templateId,
            Name = "Updated Name"
        };

        // Act
        var (status, response) = await service.UpdateContractTemplateAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ProposeContractInstance Tests

    [Fact]
    public async Task ProposeContractInstanceAsync_ValidDraft_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Draft;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new ProposeContractInstanceRequest { ContractId = contractId };

        // Act
        var (status, response) = await service.ProposeContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(ContractStatus.Proposed, response.Status);
    }

    [Fact]
    public async Task ProposeContractInstanceAsync_NotDraft_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new ProposeContractInstanceRequest { ContractId = contractId };

        // Act
        var (status, response) = await service.ProposeContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region ConsentToContract Tests

    [Fact]
    public async Task ConsentToContractAsync_ValidParty_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var partyEntityId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Proposed;
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = partyEntityId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Pending },
            new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employee", ConsentStatus = ConsentStatus.Pending }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new ConsentToContractRequest
        {
            ContractId = contractId,
            PartyEntityId = partyEntityId,
            PartyEntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.ConsentToContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task ConsentToContractAsync_NotProposed_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Draft;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new ConsentToContractRequest
        {
            ContractId = contractId,
            PartyEntityId = Guid.NewGuid(),
            PartyEntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.ConsentToContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ConsentToContractAsync_AllPartiesConsent_BecomesAccepted()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var partyEntityId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Proposed;
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = partyEntityId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Pending },
            new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employee", ConsentStatus = ConsentStatus.Consented, ConsentedAt = DateTimeOffset.UtcNow }
        };
        // Contracts without milestones auto-fulfill; add milestone so contract stays Active
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "work", Name = "Complete Work", Sequence = 0, Required = true, Status = MilestoneStatus.Pending }
        };

        ContractInstanceModel? savedInstance = null;
        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContractInstanceModel, string, CancellationToken>((k, m, _, _) => savedInstance = m)
            .ReturnsAsync("etag-1");

        var request = new ConsentToContractRequest
        {
            ContractId = contractId,
            PartyEntityId = partyEntityId,
            PartyEntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.ConsentToContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedInstance);
        // When EffectiveFrom is null, contract becomes "active" immediately after all consent
        Assert.Equal(ContractStatus.Active, savedInstance.Status);
    }

    [Fact]
    public async Task ConsentToContractAsync_NoMilestones_BecomesFulfilled()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var partyEntityId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Proposed;
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = partyEntityId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Pending },
            new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employee", ConsentStatus = ConsentStatus.Consented, ConsentedAt = DateTimeOffset.UtcNow }
        };
        // No milestones = nothing to be "active" about, contract goes directly to Fulfilled

        ContractInstanceModel? savedInstance = null;
        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContractInstanceModel, string, CancellationToken>((k, m, _, _) => savedInstance = m)
            .ReturnsAsync("etag-1");

        var request = new ConsentToContractRequest
        {
            ContractId = contractId,
            PartyEntityId = partyEntityId,
            PartyEntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.ConsentToContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(savedInstance);
        // Contracts without milestones have nothing to do, so they're immediately fulfilled (ready for execution)
        Assert.Equal(ContractStatus.Fulfilled, savedInstance.Status);
    }

    #endregion

    #region GetContractInstance Tests

    [Fact]
    public async Task GetContractInstanceAsync_ExistingContract_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new GetContractInstanceRequest { ContractId = contractId };

        // Act
        var (status, response) = await service.GetContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(contractId, response.ContractId);
    }

    [Fact]
    public async Task GetContractInstanceAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractInstanceModel?)null);

        var request = new GetContractInstanceRequest { ContractId = contractId };

        // Act
        var (status, response) = await service.GetContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region TerminateContractInstance Tests

    [Fact]
    public async Task TerminateContractInstanceAsync_ActiveContract_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var requestingEntityId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = requestingEntityId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Consented },
            new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employee", ConsentStatus = ConsentStatus.Consented }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new TerminateContractInstanceRequest
        {
            ContractId = contractId,
            RequestingEntityId = requestingEntityId,
            RequestingEntityType = EntityType.Character,
            Reason = "Mutual agreement"
        };

        // Act
        var (status, response) = await service.TerminateContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(ContractStatus.Terminated, response.Status);
    }

    [Fact]
    public async Task TerminateContractInstanceAsync_NotAParty_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var nonPartyEntityId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Consented },
            new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employee", ConsentStatus = ConsentStatus.Consented }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new TerminateContractInstanceRequest
        {
            ContractId = contractId,
            RequestingEntityId = nonPartyEntityId,
            RequestingEntityType = EntityType.Character,
            Reason = "Test reason"
        };

        // Act
        var (status, response) = await service.TerminateContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region GetContractInstanceStatus Tests

    [Fact]
    public async Task GetContractInstanceStatusAsync_ExistingContract_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new GetContractInstanceStatusRequest { ContractId = contractId };

        // Act
        var (status, response) = await service.GetContractInstanceStatusAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(contractId, response.ContractId);
    }

    #endregion

    #region CompleteMilestone Tests

    [Fact]
    public async Task CompleteMilestoneAsync_ValidMilestone_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = MilestoneStatus.Pending, Required = true, Sequence = 1 }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "milestone_1"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(MilestoneStatus.Completed, response.Milestone.Status);
    }

    [Fact]
    public async Task CompleteMilestoneAsync_AlreadyCompleted_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = MilestoneStatus.Completed, Required = true, Sequence = 1, CompletedAt = DateTimeOffset.UtcNow }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "milestone_1"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    #endregion

    #region FailMilestone Tests

    [Fact]
    public async Task FailMilestoneAsync_RequiredMilestone_ReturnsFailed()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = MilestoneStatus.Pending, Required = true, Sequence = 1 }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new FailMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "milestone_1",
            Reason = "Deadline exceeded"
        };

        // Act
        var (status, response) = await service.FailMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(MilestoneStatus.Failed, response.Milestone.Status);
    }

    [Fact]
    public async Task FailMilestoneAsync_OptionalMilestone_ReturnsSkipped()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = MilestoneStatus.Pending, Required = false, Sequence = 1 }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new FailMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "milestone_1",
            Reason = "Optional milestone skipped"
        };

        // Act
        var (status, response) = await service.FailMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(MilestoneStatus.Skipped, response.Milestone.Status);
    }

    #endregion

    #region GetMilestone Tests

    [Fact]
    public async Task GetMilestoneAsync_ExistingMilestone_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = MilestoneStatus.Pending, Required = true, Sequence = 1 }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new GetMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "milestone_1"
        };

        // Act
        var (status, response) = await service.GetMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal("milestone_1", response.Milestone.Code);
    }

    [Fact]
    public async Task GetMilestoneAsync_NonExistentMilestone_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Milestones = new List<MilestoneInstanceModel>();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new GetMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "nonexistent"
        };

        // Act
        var (status, response) = await service.GetMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region ReportBreach Tests

    [Fact]
    public async Task ReportBreachAsync_ValidBreach_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockBreachStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BreachModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var breachingEntityId = Guid.NewGuid();
        var request = new ReportBreachRequest
        {
            ContractId = contractId,
            BreachingEntityId = breachingEntityId,
            BreachingEntityType = EntityType.Character,
            BreachType = BreachType.TermViolation,
            Description = "Failed to deliver goods"
        };

        // Act
        var (status, response) = await service.ReportBreachAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(BreachStatus.Detected, response.Status);
    }

    [Fact]
    public async Task ReportBreachAsync_ContractNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractInstanceModel?)null);

        var request = new ReportBreachRequest
        {
            ContractId = contractId,
            BreachingEntityId = Guid.NewGuid(),
            BreachingEntityType = EntityType.Character,
            BreachType = BreachType.TermViolation,
            BreachedTermOrMilestone = "payment_term"
        };

        // Act
        var (status, response) = await service.ReportBreachAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region CureBreach Tests

    [Fact]
    public async Task CureBreachAsync_ExistingBreach_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var breachId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var breach = new BreachModel
        {
            BreachId = breachId,
            ContractId = contractId,
            BreachingEntityId = Guid.NewGuid(),
            BreachingEntityType = EntityType.Character,
            BreachType = BreachType.TermViolation,
            Status = BreachStatus.Detected,
            DetectedAt = DateTimeOffset.UtcNow
        };

        _mockBreachStore
            .Setup(s => s.GetWithETagAsync($"breach:{breachId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((breach, "etag-0"));

        _mockBreachStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<BreachModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new CureBreachRequest
        {
            BreachId = breachId,
            CureEvidence = "Issue resolved"
        };

        // Act
        var (status, response) = await service.CureBreachAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(BreachStatus.Cured, response.Status);
    }

    [Fact]
    public async Task CureBreachAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var breachId = Guid.NewGuid();

        _mockBreachStore
            .Setup(s => s.GetAsync($"breach:{breachId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BreachModel?)null);

        var request = new CureBreachRequest { BreachId = breachId };

        // Act
        var (status, response) = await service.CureBreachAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetBreach Tests

    [Fact]
    public async Task GetBreachAsync_ExistingBreach_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var breachId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var breach = new BreachModel
        {
            BreachId = breachId,
            ContractId = contractId,
            BreachingEntityId = Guid.NewGuid(),
            BreachingEntityType = EntityType.Character,
            BreachType = BreachType.TermViolation,
            Status = BreachStatus.Detected,
            DetectedAt = DateTimeOffset.UtcNow
        };

        _mockBreachStore
            .Setup(s => s.GetAsync($"breach:{breachId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(breach);

        var request = new GetBreachRequest { BreachId = breachId };

        // Act
        var (status, response) = await service.GetBreachAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(breachId, response.BreachId);
    }

    [Fact]
    public async Task GetBreachAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var breachId = Guid.NewGuid();

        _mockBreachStore
            .Setup(s => s.GetAsync($"breach:{breachId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BreachModel?)null);

        var request = new GetBreachRequest { BreachId = breachId };

        // Act
        var (status, response) = await service.GetBreachAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region UpdateContractMetadata Tests

    [Fact]
    public async Task UpdateContractMetadataAsync_ExistingContract_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new UpdateContractMetadataRequest
        {
            ContractId = contractId,
            MetadataType = MetadataType.InstanceData,
            Data = new Dictionary<string, object> { ["quest_id"] = "quest_123" }
        };

        // Act
        var (status, response) = await service.UpdateContractMetadataAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task UpdateContractMetadataAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractInstanceModel?)null);

        var request = new UpdateContractMetadataRequest
        {
            ContractId = contractId,
            MetadataType = MetadataType.InstanceData,
            Data = new Dictionary<string, object> { ["key"] = "value" }
        };

        // Act
        var (status, response) = await service.UpdateContractMetadataAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region GetContractMetadata Tests

    [Fact]
    public async Task GetContractMetadataAsync_ExistingContract_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new GetContractMetadataRequest { ContractId = contractId };

        // Act
        var (status, response) = await service.GetContractMetadataAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
    }

    [Fact]
    public async Task GetContractMetadataAsync_NonExistent_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractInstanceModel?)null);

        var request = new GetContractMetadataRequest { ContractId = contractId };

        // Act
        var (status, response) = await service.GetContractMetadataAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region CheckContractConstraint Tests

    [Fact]
    public async Task CheckContractConstraintAsync_NoActiveContracts_ReturnsAllowed()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("party-idx:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        var request = new CheckConstraintRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character,
            ConstraintType = ConstraintType.Exclusivity
        };

        // Act
        var (status, response) = await service.CheckContractConstraintAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Allowed);
    }

    #endregion

    #region QueryActiveContracts Tests

    [Fact]
    public async Task QueryActiveContractsAsync_NoContracts_ReturnsEmptyList()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();

        _mockListStore
            .Setup(s => s.GetAsync(It.Is<string>(k => k.StartsWith("party-idx:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<string>?)null);

        var request = new QueryActiveContractsRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.QueryActiveContractsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Empty(response.Contracts);
    }

    [Fact]
    public async Task QueryActiveContractsAsync_HasActiveContracts_ReturnsContracts()
    {
        // Arrange
        var service = CreateService();
        var entityId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = entityId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Consented }
        };

        _mockListStore
            .Setup(s => s.GetAsync($"party-idx:Character:{entityId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { contractId.ToString() });

        _mockInstanceStore
            .Setup(s => s.GetBulkAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ContractInstanceModel>
            {
                [$"instance:{contractId}"] = instance
            });

        var request = new QueryActiveContractsRequest
        {
            EntityId = entityId,
            EntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.QueryActiveContractsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Single(response.Contracts);
    }

    #endregion

    #region Helper Methods

    private static CreateContractTemplateRequest CreateValidTemplateRequest()
    {
        return new CreateContractTemplateRequest
        {
            Code = "employment_contract",
            Name = "Employment Contract",
            Description = "Standard employment agreement",
            MinParties = 2,
            MaxParties = 2,
            PartyRoles = new List<PartyRoleDefinition>
            {
                new() { Role = "employer", MinCount = 1, MaxCount = 1 },
                new() { Role = "employee", MinCount = 1, MaxCount = 1 }
            }
        };
    }

    private static ContractTemplateModel CreateTestTemplateModel(Guid templateId, string code = "test_template")
    {
        return new ContractTemplateModel
        {
            TemplateId = templateId,
            Code = code,
            Name = "Test Template",
            Description = "A test contract template",
            MinParties = 2,
            MaxParties = 2,
            PartyRoles = new List<PartyRoleModel>
            {
                new() { Role = "employer", MinCount = 1, MaxCount = 1 },
                new() { Role = "employee", MinCount = 1, MaxCount = 1 }
            },
            DefaultEnforcementMode = EnforcementMode.EventOnly,
            Transferable = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ContractInstanceModel CreateTestInstanceModel(Guid contractId)
    {
        return new ContractInstanceModel
        {
            ContractId = contractId,
            TemplateId = Guid.NewGuid(),
            TemplateCode = "test_template",
            Status = ContractStatus.Draft,
            Parties = new List<ContractPartyModel>
            {
                new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Pending },
                new() { EntityId = Guid.NewGuid(), EntityType = EntityType.Character, Role = "employee", ConsentStatus = ConsentStatus.Pending }
            },
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion

    #region Prebound API Execution Tests

    [Fact]
    public async Task CompleteMilestoneAsync_WithPreboundApi_ExecutesApi()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var employerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = employerId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Consented },
            new() { EntityId = employeeId, EntityType = EntityType.Character, Role = "employee", ConsentStatus = ConsentStatus.Consented }
        };
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "payment_milestone",
                Name = "Payment Milestone",
                Status = MilestoneStatus.Pending,
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/transfer",
                        PayloadTemplate = """{"fromEntityId": "{{contract.party.employer.entityId}}", "toEntityId": "{{contract.party.employee.entityId}}", "amount": 100}""",
                        Description = "Transfer payment on completion",
                        ExecutionMode = PreboundApiExecutionMode.Sync
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Setup navigator to return success
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceClients.PreboundApiResult.Success(
                new ServiceClients.PreboundApiDefinition
                {
                    ServiceName = "currency",
                    Endpoint = "/currency/transfer"
                },
                """{"fromEntityId": "xxx", "toEntityId": "yyy", "amount": 100}""",
                ServiceClients.RawApiResult.Success(200, """{"success": true}""", TimeSpan.FromMilliseconds(50))));

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "payment_milestone"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(MilestoneStatus.Completed, response.Milestone.Status);

        // Verify navigator was called
        _mockNavigator.Verify(
            n => n.ExecutePreboundApiAsync(
                It.Is<ServiceClients.PreboundApiDefinition>(api =>
                    api.ServiceName == "currency" &&
                    api.Endpoint == "/currency/transfer"),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify execution event was published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "contract.prebound-api.executed",
                It.IsAny<ContractPreboundApiExecutedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteMilestoneAsync_WithPreboundApiSubstitutionFailure_PublishesFailedEvent()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "payment_milestone",
                Name = "Payment Milestone",
                Status = MilestoneStatus.Pending,
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/transfer",
                        PayloadTemplate = """{"amount": "{{missing.variable}}"}""",
                        ExecutionMode = PreboundApiExecutionMode.Sync
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Setup navigator to return substitution failure
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceClients.PreboundApiResult.SubstitutionFailed(
                new ServiceClients.PreboundApiDefinition
                {
                    ServiceName = "currency",
                    Endpoint = "/currency/transfer"
                },
                "Variable 'missing.variable' not found in context"));

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "payment_milestone"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert - milestone still completes even if prebound API fails
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(MilestoneStatus.Completed, response.Milestone.Status);

        // Verify failed event was published (not executed event)
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "contract.prebound-api.failed",
                It.IsAny<ContractPreboundApiFailedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteMilestoneAsync_WithPreboundApiValidationFailure_PublishesValidationFailedEvent()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "payment_milestone",
                Name = "Payment Milestone",
                Status = MilestoneStatus.Pending,
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/transfer",
                        PayloadTemplate = """{"amount": 100}""",
                        ExecutionMode = PreboundApiExecutionMode.Sync,
                        ResponseValidation = new ResponseValidation
                        {
                            SuccessConditions = new List<ValidationCondition>
                            {
                                new()
                                {
                                    Type = ValidationConditionType.StatusCodeIn,
                                    StatusCodes = new List<int> { 200 }
                                }
                            }
                        }
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Setup navigator to return HTTP 400 (which will fail the statusCode validation)
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceClients.PreboundApiResult.Success(
                new ServiceClients.PreboundApiDefinition
                {
                    ServiceName = "currency",
                    Endpoint = "/currency/transfer"
                },
                """{"amount": 100}""",
                ServiceClients.RawApiResult.Success(400, """{"error": "Insufficient funds"}""", TimeSpan.FromMilliseconds(50))));

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "payment_milestone"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert - milestone still completes
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify validation-failed event was published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "contract.prebound-api.validation-failed",
                It.IsAny<ContractPreboundApiValidationFailedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteMilestoneAsync_WithMultiplePreboundApis_ExecutesAll()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "multi_action_milestone",
                Name = "Multi Action Milestone",
                Status = MilestoneStatus.Pending,
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/transfer",
                        PayloadTemplate = """{"amount": 100}""",
                        ExecutionMode = PreboundApiExecutionMode.Sync
                    },
                    new()
                    {
                        ServiceName = "notification",
                        Endpoint = "/notification/send",
                        PayloadTemplate = """{"message": "Milestone completed"}""",
                        ExecutionMode = PreboundApiExecutionMode.FireAndForget
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Setup navigator to return success for both calls
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ServiceClients.PreboundApiDefinition api, IReadOnlyDictionary<string, object?> ctx, CancellationToken ct) =>
                ServiceClients.PreboundApiResult.Success(
                    api,
                    api.PayloadTemplate,
                    ServiceClients.RawApiResult.Success(200, """{"success": true}""", TimeSpan.FromMilliseconds(50))));

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "multi_action_milestone"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);

        // Verify navigator was called twice
        _mockNavigator.Verify(
            n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Verify execution events were published for both
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "contract.prebound-api.executed",
                It.IsAny<ContractPreboundApiExecutedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task FailMilestoneAsync_WithOnExpirePreboundApi_ExecutesApi()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "deadline_milestone",
                Name = "Deadline Milestone",
                Status = MilestoneStatus.Pending,
                Required = true,
                Sequence = 1,
                OnExpire = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "notification",
                        Endpoint = "/notification/send",
                        PayloadTemplate = """{"type": "milestone_failed", "contractId": "{{contract.id}}"}""",
                        ExecutionMode = PreboundApiExecutionMode.FireAndForget
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceClients.PreboundApiResult.Success(
                new ServiceClients.PreboundApiDefinition
                {
                    ServiceName = "notification",
                    Endpoint = "/notification/send"
                },
                """{"type": "milestone_failed"}""",
                ServiceClients.RawApiResult.Success(200, """{"sent": true}""", TimeSpan.FromMilliseconds(20))));

        var request = new FailMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "deadline_milestone",
            Reason = "Deadline exceeded"
        };

        // Act
        var (status, response) = await service.FailMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(MilestoneStatus.Failed, response.Milestone.Status);

        // Verify onExpire API was called
        _mockNavigator.Verify(
            n => n.ExecutePreboundApiAsync(
                It.Is<ServiceClients.PreboundApiDefinition>(api =>
                    api.ServiceName == "notification" &&
                    api.Endpoint == "/notification/send"),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteMilestoneAsync_PreboundApiException_PublishesFailedEventAndContinues()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "milestone_1",
                Name = "Test Milestone",
                Status = MilestoneStatus.Pending,
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "unreachable",
                        Endpoint = "/api/call",
                        PayloadTemplate = """{}""",
                        ExecutionMode = PreboundApiExecutionMode.Sync
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        // Setup navigator to throw exception
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "milestone_1"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert - milestone still completes even though API threw exception
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(MilestoneStatus.Completed, response.Milestone.Status);

        // Verify failed event was published
        _mockMessageBus.Verify(
            m => m.TryPublishAsync(
                "contract.prebound-api.failed",
                It.IsAny<ContractPreboundApiFailedEvent>(),
                It.IsAny<PublishOptions?>(),
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompleteMilestoneAsync_NoPreboundApis_NoNavigatorCall()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "simple_milestone",
                Name = "Simple Milestone",
                Status = MilestoneStatus.Pending,
                Required = true,
                Sequence = 1
                // No OnComplete or OnExpire APIs
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "simple_milestone"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);

        // Verify navigator was NOT called
        _mockNavigator.Verify(
            n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteMilestoneAsync_PreboundApiContextIncludesContractData()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var employerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.TemplateId = templateId;
        instance.TemplateCode = "employment_contract";
        instance.Status = ContractStatus.Active;
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = employerId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Consented },
            new() { EntityId = employeeId, EntityType = EntityType.Character, Role = "employee", ConsentStatus = ConsentStatus.Consented }
        };
        instance.Terms = new ContractTermsModel
        {
            Duration = "P30D",
            PaymentFrequency = "weekly",
            CustomTerms = new Dictionary<string, object>
            {
                ["wage"] = 500
            }
        };
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "payment",
                Name = "Payment",
                Status = MilestoneStatus.Pending,
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "test",
                        Endpoint = "/test",
                        PayloadTemplate = """{}""",
                        ExecutionMode = PreboundApiExecutionMode.Sync
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        IReadOnlyDictionary<string, object?>? capturedContext = null;
        _mockNavigator
            .Setup(n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()))
            .Callback<ServiceClients.PreboundApiDefinition, IReadOnlyDictionary<string, object?>, CancellationToken>((api, ctx, ct) =>
                capturedContext = ctx)
            .ReturnsAsync(ServiceClients.PreboundApiResult.Success(
                new ServiceClients.PreboundApiDefinition(),
                "{}",
                ServiceClients.RawApiResult.Success(200, "{}", TimeSpan.Zero)));

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "payment"
        };

        // Act
        await service.CompleteMilestoneAsync(request);

        // Assert - verify context contains expected contract data
        Assert.NotNull(capturedContext);
        Assert.Equal(contractId.ToString(), capturedContext["contract.id"]);
        Assert.Equal(templateId.ToString(), capturedContext["contract.templateId"]);
        Assert.Equal("employment_contract", capturedContext["contract.templateCode"]);
        Assert.Equal("fulfilled", capturedContext["contract.status"]);

        // Verify party shortcuts
        Assert.Equal(employerId.ToString(), capturedContext["contract.party.employer.entityId"]);
        Assert.Equal("Character", capturedContext["contract.party.employer.entityType"]);
        Assert.Equal(employeeId.ToString(), capturedContext["contract.party.employee.entityId"]);

        // Verify terms
        Assert.Equal("P30D", capturedContext["contract.terms.duration"]);
        Assert.Equal("weekly", capturedContext["contract.terms.paymentFrequency"]);
        Assert.Equal(500, capturedContext["contract.terms.custom.wage"]);
    }

    #endregion

    #region Guardian System Tests

    [Fact]
    public async Task LockContractAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var guardianId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.TemplateId = templateId;
        instance.Status = ContractStatus.Active;

        var template = CreateTestTemplateModel(templateId);
        template.Transferable = true;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new LockContractRequest
        {
            ContractInstanceId = contractId,
            GuardianId = guardianId,
            GuardianType = "escrow"
        };

        // Act
        var (status, response) = await service.LockContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Locked);
        Assert.Equal(contractId, response.ContractId);
        Assert.Equal(guardianId, response.GuardianId);
    }

    [Fact]
    public async Task LockContractAsync_AlreadyLocked_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.TemplateId = templateId;
        instance.GuardianId = Guid.NewGuid();
        instance.GuardianType = "escrow";

        var template = CreateTestTemplateModel(templateId);
        template.Transferable = true;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new LockContractRequest
        {
            ContractInstanceId = contractId,
            GuardianId = Guid.NewGuid(),
            GuardianType = "escrow"
        };

        // Act
        var (status, response) = await service.LockContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task LockContractAsync_NotTransferable_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.TemplateId = templateId;

        var template = CreateTestTemplateModel(templateId);
        template.Transferable = false;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new LockContractRequest
        {
            ContractInstanceId = contractId,
            GuardianId = Guid.NewGuid(),
            GuardianType = "escrow"
        };

        // Act
        var (status, response) = await service.LockContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task UnlockContractAsync_ValidGuardian_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var guardianId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.GuardianId = guardianId;
        instance.GuardianType = "escrow";
        instance.LockedAt = DateTimeOffset.UtcNow.AddHours(-1);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new UnlockContractRequest
        {
            ContractInstanceId = contractId,
            GuardianId = guardianId,
            GuardianType = "escrow"
        };

        // Act
        var (status, response) = await service.UnlockContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Unlocked);
    }

    [Fact]
    public async Task UnlockContractAsync_WrongGuardian_ReturnsForbidden()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.GuardianId = Guid.NewGuid();
        instance.GuardianType = "escrow";

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new UnlockContractRequest
        {
            ContractInstanceId = contractId,
            GuardianId = Guid.NewGuid(),
            GuardianType = "escrow"
        };

        // Act
        var (status, response) = await service.UnlockContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TransferContractPartyAsync_ValidGuardian_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var guardianId = Guid.NewGuid();
        var fromEntityId = Guid.NewGuid();
        var toEntityId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.GuardianId = guardianId;
        instance.GuardianType = "escrow";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = fromEntityId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Consented }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");
        _mockListStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { contractId.ToString() });
        _mockListStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new TransferContractPartyRequest
        {
            ContractInstanceId = contractId,
            GuardianId = guardianId,
            GuardianType = "escrow",
            FromEntityId = fromEntityId,
            FromEntityType = EntityType.Character,
            ToEntityId = toEntityId,
            ToEntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.TransferContractPartyAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Transferred);
        Assert.Equal(toEntityId, response.ToEntityId);
        Assert.Equal("employer", response.Role);
    }

    [Fact]
    public async Task TransferContractPartyAsync_NotLocked_ReturnsForbidden()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        // No guardian set (not locked)

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new TransferContractPartyRequest
        {
            ContractInstanceId = contractId,
            GuardianId = Guid.NewGuid(),
            GuardianType = "escrow",
            FromEntityId = Guid.NewGuid(),
            FromEntityType = EntityType.Character,
            ToEntityId = Guid.NewGuid(),
            ToEntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.TransferContractPartyAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    #endregion

    #region Clause Type System Tests

    [Fact]
    public async Task RegisterClauseTypeAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();

        _mockClauseTypeStore
            .Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClauseTypeModel?)null);
        _mockClauseTypeStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ClauseTypeModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");
        _mockListStore
            .Setup(s => s.GetAsync("all-clause-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());
        _mockListStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new RegisterClauseTypeRequest
        {
            TypeCode = "custom_fee",
            Description = "Custom fee clause",
            Category = ClauseCategory.Execution,
            ExecutionHandler = new ClauseHandlerDefinition
            {
                Service = "currency",
                Endpoint = "/currency/transfer"
            }
        };

        // Act
        var (status, response) = await service.RegisterClauseTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Registered);
        Assert.Equal("custom_fee", response.TypeCode);
    }

    [Fact]
    public async Task RegisterClauseTypeAsync_DuplicateCode_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();

        _mockClauseTypeStore
            .Setup(s => s.GetAsync("clause-type:custom_fee", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClauseTypeModel
            {
                TypeCode = "custom_fee",
                Category = ClauseCategory.Execution,
                IsBuiltIn = false,
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new RegisterClauseTypeRequest
        {
            TypeCode = "custom_fee",
            Description = "Custom fee clause",
            Category = ClauseCategory.Execution
        };

        // Act
        var (status, response) = await service.RegisterClauseTypeAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ListClauseTypesAsync_ReturnsRegisteredTypes()
    {
        // Arrange
        var service = CreateService();

        // Setup: built-in types already registered
        _mockListStore
            .Setup(s => s.GetAsync("all-clause-types", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "asset_requirement", "currency_transfer" });

        _mockClauseTypeStore
            .Setup(s => s.GetAsync("clause-type:asset_requirement", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClauseTypeModel
            {
                TypeCode = "asset_requirement",
                Description = "Validates assets",
                Category = ClauseCategory.Validation,
                IsBuiltIn = true,
                ValidationHandler = new ClauseHandlerModel { Service = "currency", Endpoint = "/currency/balance/get" },
                CreatedAt = DateTimeOffset.UtcNow
            });
        _mockClauseTypeStore
            .Setup(s => s.GetAsync("clause-type:currency_transfer", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClauseTypeModel
            {
                TypeCode = "currency_transfer",
                Description = "Transfers currency",
                Category = ClauseCategory.Execution,
                IsBuiltIn = true,
                ExecutionHandler = new ClauseHandlerModel { Service = "currency", Endpoint = "/currency/transfer" },
                CreatedAt = DateTimeOffset.UtcNow
            });

        var request = new ListClauseTypesRequest();

        // Act
        var (status, response) = await service.ListClauseTypesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Equal(2, response.ClauseTypes.Count);
    }

    #endregion

    #region Template Values Tests

    [Fact]
    public async Task SetContractTemplateValuesAsync_ValidRequest_ReturnsOK()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new SetTemplateValuesRequest
        {
            ContractInstanceId = contractId,
            TemplateValues = new Dictionary<string, string>
            {
                ["PartyA_EscrowWalletId"] = Guid.NewGuid().ToString(),
                ["PartyB_WalletId"] = Guid.NewGuid().ToString(),
                ["base_amount"] = "10000"
            }
        };

        // Act
        var (status, response) = await service.SetContractTemplateValuesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Updated);
        Assert.Equal(3, response.ValueCount);
    }

    [Fact]
    public async Task SetContractTemplateValuesAsync_InvalidKey_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new SetTemplateValuesRequest
        {
            ContractInstanceId = contractId,
            TemplateValues = new Dictionary<string, string>
            {
                ["valid_key"] = "value",
                ["invalid-key-with-hyphens"] = "value"
            }
        };

        // Act
        var (status, response) = await service.SetContractTemplateValuesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task SetContractTemplateValuesAsync_MergesWithExisting()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.TemplateValues = new Dictionary<string, string>
        {
            ["existing_key"] = "existing_value"
        };

        ContractInstanceModel? savedModel = null;
        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContractInstanceModel, StateOptions?, CancellationToken>((k, m, o, c) => savedModel = m)
            .ReturnsAsync("etag");

        var request = new SetTemplateValuesRequest
        {
            ContractInstanceId = contractId,
            TemplateValues = new Dictionary<string, string>
            {
                ["new_key"] = "new_value"
            }
        };

        // Act
        var (status, response) = await service.SetContractTemplateValuesAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(savedModel);
        Assert.Equal(2, savedModel.TemplateValues?.Count);
        Assert.Equal("existing_value", savedModel.TemplateValues?["existing_key"]);
        Assert.Equal("new_value", savedModel.TemplateValues?["new_key"]);
    }

    #endregion

    #region Execution System Tests

    [Fact]
    public async Task CheckAssetRequirementsAsync_NoClauses_AllSatisfied()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.TemplateId = templateId;
        instance.TemplateValues = new Dictionary<string, string> { ["key"] = "value" };

        var template = CreateTestTemplateModel(templateId);
        // No clauses defined in template

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new CheckAssetRequirementsRequest
        {
            ContractInstanceId = contractId
        };

        // Act
        var (status, response) = await service.CheckAssetRequirementsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.AllSatisfied);
    }

    [Fact]
    public async Task CheckAssetRequirementsAsync_MissingTemplateValues_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.TemplateId = templateId;
        instance.TemplateValues = null;

        // Create template with asset_requirement clauses - this triggers the BadRequest check
        var template = CreateTestTemplateModel(templateId);
        template.DefaultTerms = new ContractTermsModel
        {
            CustomTerms = new Dictionary<string, object>
            {
                ["clauses"] = new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = "clause-1",
                        ["type"] = "asset_requirement",
                        ["role"] = "employer",
                        ["description"] = "Test asset requirement"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);

        var request = new CheckAssetRequirementsRequest
        {
            ContractInstanceId = contractId
        };

        // Act
        var (status, response) = await service.CheckAssetRequirementsAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ExecuteContractAsync_NotFulfilled_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.TemplateValues = new Dictionary<string, string> { ["key"] = "value" };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new ExecuteContractRequest
        {
            ContractInstanceId = contractId
        };

        // Act
        var (status, response) = await service.ExecuteContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ExecuteContractAsync_MissingTemplateValues_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Fulfilled;
        instance.TemplateValues = null;

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new ExecuteContractRequest
        {
            ContractInstanceId = contractId
        };

        // Act
        var (status, response) = await service.ExecuteContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ExecuteContractAsync_AlreadyExecuted_ReturnsCachedResult()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var executedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Fulfilled;
        instance.TemplateValues = new Dictionary<string, string> { ["key"] = "value" };
        instance.ExecutedAt = executedAt;
        instance.ExecutionIdempotencyKey = "test-key";
        instance.ExecutionDistributions = new List<DistributionRecordModel>();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new ExecuteContractRequest
        {
            ContractInstanceId = contractId
        };

        // Act
        var (status, response) = await service.ExecuteContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Executed);
        Assert.True(response.AlreadyExecuted);
        Assert.Equal(executedAt, response.ExecutedAt);
    }

    [Fact]
    public async Task ExecuteContractAsync_FulfilledWithNoClauses_ExecutesSuccessfully()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var templateId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.TemplateId = templateId;
        instance.Status = ContractStatus.Fulfilled;
        instance.TemplateValues = new Dictionary<string, string> { ["key"] = "value" };

        var template = CreateTestTemplateModel(templateId);
        // No clauses defined

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));
        _mockTemplateStore
            .Setup(s => s.GetAsync($"template:{templateId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(template);
        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag-1");

        var request = new ExecuteContractRequest
        {
            ContractInstanceId = contractId,
            IdempotencyKey = "test-exec-key"
        };

        // Act
        var (status, response) = await service.ExecuteContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.True(response.Executed);
        Assert.False(response.AlreadyExecuted);
        Assert.NotNull(response.ExecutedAt);
    }

    [Fact]
    public async Task ExecuteContractAsync_ContractNotFound_ReturnsNotFound()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContractInstanceModel?)null);

        var request = new ExecuteContractRequest
        {
            ContractInstanceId = contractId
        };

        // Act
        var (status, response) = await service.ExecuteContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    #endregion

    #region Guardian Enforcement Tests

    [Fact]
    public async Task TerminateContractAsync_LockedContract_ReturnsForbidden()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.GuardianId = Guid.NewGuid();
        instance.GuardianType = "escrow";

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new TerminateContractInstanceRequest
        {
            ContractId = contractId,
            RequestingEntityId = Guid.NewGuid(),
            RequestingEntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.TerminateContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ConsentToContractAsync_LockedContract_ReturnsForbidden()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var entityId = Guid.NewGuid();

        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Proposed;
        instance.GuardianId = Guid.NewGuid();
        instance.GuardianType = "escrow";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = entityId, EntityType = EntityType.Character, Role = "employer", ConsentStatus = ConsentStatus.Pending }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);
        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        var request = new ConsentToContractRequest
        {
            ContractId = contractId,
            PartyEntityId = entityId,
            PartyEntityType = EntityType.Character
        };

        // Act
        var (status, response) = await service.ConsentToContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Forbidden, status);
        Assert.Null(response);
    }

    #endregion

    #region Concurrency Lock Failure Tests

    [Fact]
    public async Task ProposeContractInstanceAsync_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Draft;

        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        // Override lock to fail for this contract
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                "contract-instance",
                contractId.ToString(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);

        var request = new ProposeContractInstanceRequest { ContractId = contractId };

        // Act
        var (status, response) = await service.ProposeContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task CompleteMilestoneAsync_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        // Override lock to fail for this contract
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                "contract-instance",
                contractId.ToString(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "milestone_1"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task TerminateContractInstanceAsync_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        // Override lock to fail for this contract
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                "contract-instance",
                contractId.ToString(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);

        var request = new TerminateContractInstanceRequest
        {
            ContractId = contractId,
            RequestingEntityId = Guid.NewGuid(),
            RequestingEntityType = EntityType.Character,
            Reason = "test termination"
        };

        // Act
        var (status, response) = await service.TerminateContractInstanceAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task ExecuteContractAsync_LockFails_ReturnsConflict()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();

        // Override lock to fail for this contract
        var failedLockResponse = new Mock<ILockResponse>();
        failedLockResponse.Setup(l => l.Success).Returns(false);
        _mockLockProvider
            .Setup(l => l.LockAsync(
                "contract-instance",
                contractId.ToString(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedLockResponse.Object);

        var request = new ExecuteContractRequest { ContractInstanceId = contractId };

        // Act
        var (status, response) = await service.ExecuteContractAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);
    }

    #endregion

    #region Ordering Correctness Tests

    [Fact]
    public async Task CompleteMilestoneAsync_TrySaveFails_DoesNotExecutePreboundApis()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "test_milestone",
                Name = "Test Milestone",
                Status = MilestoneStatus.Active,
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/credit",
                        PayloadTemplate = "{}",
                        Description = "Pay on completion"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        // TrySaveAsync returns null (concurrent modification)
        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new CompleteMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "test_milestone"
        };

        // Act
        var (status, response) = await service.CompleteMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify prebound APIs were NOT executed (persist failed, so side effects should not fire)
        _mockNavigator.Verify(
            n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FailMilestoneAsync_TrySaveFails_DoesNotExecutePreboundApis()
    {
        // Arrange
        var service = CreateService();
        var contractId = Guid.NewGuid();
        var instance = CreateTestInstanceModel(contractId);
        instance.Status = ContractStatus.Active;
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "test_milestone",
                Name = "Test Milestone",
                Status = MilestoneStatus.Active,
                Required = false,
                Sequence = 1,
                OnExpire = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/debit",
                        PayloadTemplate = "{}",
                        Description = "Penalize on failure"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetWithETagAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((instance, "etag-0"));

        // TrySaveAsync returns null (concurrent modification)
        _mockInstanceStore
            .Setup(s => s.TrySaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var request = new FailMilestoneRequest
        {
            ContractId = contractId,
            MilestoneCode = "test_milestone",
            Reason = "test failure"
        };

        // Act
        var (status, response) = await service.FailMilestoneAsync(request);

        // Assert
        Assert.Equal(StatusCodes.Conflict, status);
        Assert.Null(response);

        // Verify prebound APIs were NOT executed
        _mockNavigator.Verify(
            n => n.ExecutePreboundApiAsync(
                It.IsAny<ServiceClients.PreboundApiDefinition>(),
                It.IsAny<IReadOnlyDictionary<string, object?>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
