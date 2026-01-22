using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Events;
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
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IServiceNavigator> _mockNavigator;
    private readonly Mock<ILogger<ContractService>> _mockLogger;
    private readonly Mock<IEventConsumer> _mockEventConsumer;

    private const string STATE_STORE = "contract-statestore";

    public ContractServiceTests()
    {
        _mockStateStoreFactory = new Mock<IStateStoreFactory>();
        _mockTemplateStore = new Mock<IStateStore<ContractTemplateModel>>();
        _mockInstanceStore = new Mock<IStateStore<ContractInstanceModel>>();
        _mockBreachStore = new Mock<IStateStore<BreachModel>>();
        _mockStringStore = new Mock<IStateStore<string>>();
        _mockListStore = new Mock<IStateStore<List<string>>>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockNavigator = new Mock<IServiceNavigator>();
        _mockLogger = new Mock<ILogger<ContractService>>();
        _mockEventConsumer = new Mock<IEventConsumer>();

        // Setup factory to return typed stores
        _mockStateStoreFactory.Setup(f => f.GetStore<ContractTemplateModel>(STATE_STORE)).Returns(_mockTemplateStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<ContractInstanceModel>(STATE_STORE)).Returns(_mockInstanceStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<BreachModel>(STATE_STORE)).Returns(_mockBreachStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<string>(STATE_STORE)).Returns(_mockStringStore.Object);
        _mockStateStoreFactory.Setup(f => f.GetStore<List<string>>(STATE_STORE)).Returns(_mockListStore.Object);

        // Default message bus setup
        _mockMessageBus.Setup(m => m.TryPublishAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private ContractService CreateService()
    {
        return new ContractService(
            _mockMessageBus.Object,
            _mockNavigator.Object,
            _mockStateStoreFactory.Object,
            _mockLogger.Object,
            Configuration,
            _mockEventConsumer.Object);
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
        instance.Status = "draft";

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

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
        instance.Status = "proposed";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = partyEntityId.ToString(), EntityType = "Character", Role = "employer", ConsentStatus = "pending" },
            new() { EntityId = Guid.NewGuid().ToString(), EntityType = "Character", Role = "employee", ConsentStatus = "pending" }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "draft";

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

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
        instance.Status = "proposed";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = partyEntityId.ToString(), EntityType = "Character", Role = "employer", ConsentStatus = "pending" },
            new() { EntityId = Guid.NewGuid().ToString(), EntityType = "Character", Role = "employee", ConsentStatus = "consented", ConsentedAt = DateTimeOffset.UtcNow }
        };

        ContractInstanceModel? savedInstance = null;
        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<string, ContractInstanceModel, StateOptions?, CancellationToken>((k, m, _, _) => savedInstance = m)
            .ReturnsAsync("etag");

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
        Assert.Equal("active", savedInstance.Status);
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
        instance.Status = "active";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = requestingEntityId.ToString(), EntityType = "Character", Role = "employer", ConsentStatus = "consented" },
            new() { EntityId = Guid.NewGuid().ToString(), EntityType = "Character", Role = "employee", ConsentStatus = "consented" }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = Guid.NewGuid().ToString(), EntityType = "Character", Role = "employer", ConsentStatus = "consented" },
            new() { EntityId = Guid.NewGuid().ToString(), EntityType = "Character", Role = "employee", ConsentStatus = "consented" }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = "pending", Required = true, Sequence = 1 }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = "completed", Required = true, Sequence = 1, CompletedAt = DateTimeOffset.UtcNow }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = "pending", Required = true, Sequence = 1 }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new() { Code = "milestone_1", Name = "First Milestone", Status = "pending", Required = false, Sequence = 1 }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
            new() { Code = "milestone_1", Name = "First Milestone", Status = "pending", Required = true, Sequence = 1 }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

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
        instance.Status = "active";

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        _mockBreachStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BreachModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var breachingEntityId = Guid.NewGuid();
        var request = new ReportBreachRequest
        {
            ContractId = contractId,
            BreachingEntityId = breachingEntityId,
            BreachingEntityType = EntityType.Character,
            BreachType = BreachType.Term_violation,
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
            BreachType = BreachType.Term_violation,
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
            BreachId = breachId.ToString(),
            ContractId = contractId.ToString(),
            BreachingEntityId = Guid.NewGuid().ToString(),
            BreachingEntityType = "Character",
            BreachType = "term_violation",
            Status = "detected",
            DetectedAt = DateTimeOffset.UtcNow
        };

        _mockBreachStore
            .Setup(s => s.GetAsync($"breach:{breachId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(breach);

        _mockBreachStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<BreachModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
            BreachId = breachId.ToString(),
            ContractId = contractId.ToString(),
            BreachingEntityId = Guid.NewGuid().ToString(),
            BreachingEntityType = "Character",
            BreachType = "term_violation",
            Status = "detected",
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
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

        var request = new UpdateContractMetadataRequest
        {
            ContractId = contractId,
            MetadataType = MetadataType.Instance_data,
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
            MetadataType = MetadataType.Instance_data,
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
        instance.Status = "active";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = entityId.ToString(), EntityType = "Character", Role = "employer", ConsentStatus = "consented" }
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
            TemplateId = templateId.ToString(),
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
            DefaultEnforcementMode = "event_only",
            Transferable = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static ContractInstanceModel CreateTestInstanceModel(Guid contractId)
    {
        return new ContractInstanceModel
        {
            ContractId = contractId.ToString(),
            TemplateId = Guid.NewGuid().ToString(),
            TemplateCode = "test_template",
            Status = "draft",
            Parties = new List<ContractPartyModel>
            {
                new() { EntityId = Guid.NewGuid().ToString(), EntityType = "Character", Role = "employer", ConsentStatus = "pending" },
                new() { EntityId = Guid.NewGuid().ToString(), EntityType = "Character", Role = "employee", ConsentStatus = "pending" }
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
        instance.Status = "active";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = employerId.ToString(), EntityType = "Character", Role = "employer", ConsentStatus = "consented" },
            new() { EntityId = employeeId.ToString(), EntityType = "Character", Role = "employee", ConsentStatus = "consented" }
        };
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "payment_milestone",
                Name = "Payment Milestone",
                Status = "active",
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
                        ExecutionMode = "sync"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "payment_milestone",
                Name = "Payment Milestone",
                Status = "active",
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/transfer",
                        PayloadTemplate = """{"amount": "{{missing.variable}}"}""",
                        ExecutionMode = "sync"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "payment_milestone",
                Name = "Payment Milestone",
                Status = "active",
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/transfer",
                        PayloadTemplate = """{"amount": 100}""",
                        ExecutionMode = "sync",
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
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "multi_action_milestone",
                Name = "Multi Action Milestone",
                Status = "active",
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "currency",
                        Endpoint = "/currency/transfer",
                        PayloadTemplate = """{"amount": 100}""",
                        ExecutionMode = "sync"
                    },
                    new()
                    {
                        ServiceName = "notification",
                        Endpoint = "/notification/send",
                        PayloadTemplate = """{"message": "Milestone completed"}""",
                        ExecutionMode = "fire_and_forget"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "deadline_milestone",
                Name = "Deadline Milestone",
                Status = "active",
                Required = true,
                Sequence = 1,
                OnExpire = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "notification",
                        Endpoint = "/notification/send",
                        PayloadTemplate = """{"type": "milestone_failed", "contractId": "{{contract.id}}"}""",
                        ExecutionMode = "fire_and_forget"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "milestone_1",
                Name = "Test Milestone",
                Status = "active",
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "unreachable",
                        Endpoint = "/api/call",
                        PayloadTemplate = """{}""",
                        ExecutionMode = "sync"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.Status = "active";
        instance.Milestones = new List<MilestoneInstanceModel>
        {
            new()
            {
                Code = "simple_milestone",
                Name = "Simple Milestone",
                Status = "active",
                Required = true,
                Sequence = 1
                // No OnComplete or OnExpire APIs
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        instance.TemplateId = templateId.ToString();
        instance.TemplateCode = "employment_contract";
        instance.Status = "active";
        instance.Parties = new List<ContractPartyModel>
        {
            new() { EntityId = employerId.ToString(), EntityType = "Character", Role = "employer", ConsentStatus = "consented" },
            new() { EntityId = employeeId.ToString(), EntityType = "Character", Role = "employee", ConsentStatus = "consented" }
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
                Status = "active",
                Required = true,
                Sequence = 1,
                OnComplete = new List<PreboundApiModel>
                {
                    new()
                    {
                        ServiceName = "test",
                        Endpoint = "/test",
                        PayloadTemplate = """{}""",
                        ExecutionMode = "sync"
                    }
                }
            }
        };

        _mockInstanceStore
            .Setup(s => s.GetAsync($"instance:{contractId}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(instance);

        _mockInstanceStore
            .Setup(s => s.SaveAsync(It.IsAny<string>(), It.IsAny<ContractInstanceModel>(), It.IsAny<StateOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("etag");

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
        Assert.Equal("active", capturedContext["contract.status"]);

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
}
