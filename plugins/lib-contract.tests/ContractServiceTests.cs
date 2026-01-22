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

    #endregion
}
