using BeyondImmersion.Bannou.BehaviorCompiler.Compiler;
using BeyondImmersion.Bannou.BehaviorCompiler.Goap;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Behavior;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ApiGoapGoal = BeyondImmersion.BannouService.Behavior.GoapGoal;
// Aliases to distinguish between API and internal GOAP types
using InternalGoapGoal = BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal;

namespace BeyondImmersion.BannouService.Behavior.Tests;

/// <summary>
/// Unit tests for BehaviorService
/// This test project can reference other service clients for integration testing.
/// </summary>
public class BehaviorServiceTests
{
    private readonly Mock<ILogger<BehaviorService>> _mockLogger;
    private readonly Mock<BehaviorServiceConfiguration> _mockConfiguration;
    private readonly Mock<IMessageBus> _mockMessageBus;
    private readonly Mock<IEventConsumer> _mockEventConsumer;
    private readonly Mock<IGoapPlanner> _mockGoapPlanner;
    private readonly BehaviorCompiler _compiler;
    private readonly Mock<IAssetClient> _mockAssetClient;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<IBehaviorBundleManager> _mockBundleManager;
    private readonly Mock<ITelemetryProvider> _mockTelemetryProvider;

    public BehaviorServiceTests()
    {
        _mockLogger = new Mock<ILogger<BehaviorService>>();
        _mockConfiguration = new Mock<BehaviorServiceConfiguration>();
        _mockMessageBus = new Mock<IMessageBus>();
        _mockEventConsumer = new Mock<IEventConsumer>();
        _mockGoapPlanner = new Mock<IGoapPlanner>();
        _compiler = new BehaviorCompiler();
        _mockAssetClient = new Mock<IAssetClient>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockBundleManager = new Mock<IBehaviorBundleManager>();
        _mockTelemetryProvider = new Mock<ITelemetryProvider>();
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
    public void BehaviorService_ConstructorIsValid() =>
        ServiceConstructorValidator.ValidateServiceConstructor<BehaviorService>();

    #endregion

    #region GenerateGoapPlanAsync Tests

    [Fact]
    public async Task GenerateGoapPlanAsync_MissingBehaviorId_ReturnsBadRequest()
    {
        // Arrange
        var service = CreateService();
        var request = new GoapPlanRequest
        {
            AgentId = "agent-1",
            BehaviorId = string.Empty,
            Goal = new ApiGoapGoal
            {
                Name = "test_goal",
                Priority = 50,
                Conditions = new Dictionary<string, string> { { "key", "==value" } }
            },
            WorldState = new Dictionary<string, object>()
        };

        // Act
        var (status, response) = await service.GenerateGoapPlanAsync(request);

        // Assert
        Assert.Equal(StatusCodes.BadRequest, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GenerateGoapPlanAsync_BehaviorNotFound_ReturnsNotFound()
    {
        // Arrange
        _mockBundleManager
            .Setup(m => m.GetGoapMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CachedGoapMetadata?)null);

        var service = CreateService();
        var request = new GoapPlanRequest
        {
            AgentId = "agent-1",
            BehaviorId = "nonexistent-behavior",
            Goal = new ApiGoapGoal
            {
                Name = "test_goal",
                Priority = 50,
                Conditions = new Dictionary<string, string> { { "key", "==value" } }
            },
            WorldState = new Dictionary<string, object>()
        };

        // Act
        var (status, response) = await service.GenerateGoapPlanAsync(request);

        // Assert
        Assert.Equal(StatusCodes.NotFound, status);
        Assert.Null(response);
    }

    [Fact]
    public async Task GenerateGoapPlanAsync_NoActions_ReturnsFailure()
    {
        // Arrange
        var cachedMetadata = new CachedGoapMetadata
        {
            BehaviorId = "behavior-123",
            Goals = new List<CachedGoapGoal>(),
            Actions = new List<CachedGoapAction>() // No actions
        };

        _mockBundleManager
            .Setup(m => m.GetGoapMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedMetadata);

        var service = CreateService();
        var request = new GoapPlanRequest
        {
            AgentId = "agent-1",
            BehaviorId = "behavior-123",
            Goal = new ApiGoapGoal
            {
                Name = "test_goal",
                Priority = 50,
                Conditions = new Dictionary<string, string> { { "key", "==value" } }
            },
            WorldState = new Dictionary<string, object>()
        };

        // Act
        var (status, response) = await service.GenerateGoapPlanAsync(request);

        // Assert - OK with no plan because no actions available
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.Plan);
        Assert.Contains("No GOAP actions", response.FailureReason);
    }

    [Fact]
    public async Task GenerateGoapPlanAsync_PlanningFails_ReturnsFailure()
    {
        // Arrange
        var cachedMetadata = new CachedGoapMetadata
        {
            BehaviorId = "behavior-123",
            Goals = new List<CachedGoapGoal>(),
            Actions = new List<CachedGoapAction>
            {
                new CachedGoapAction
                {
                    FlowName = "action1",
                    Preconditions = new Dictionary<string, string> { { "impossible", "==true" } },
                    Effects = new Dictionary<string, string> { { "goal_met", "true" } },
                    Cost = 1.0f
                }
            }
        };

        _mockBundleManager
            .Setup(m => m.GetGoapMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedMetadata);

        // Planner returns null (no plan found)
        _mockGoapPlanner
            .Setup(p => p.PlanAsync(
                It.IsAny<WorldState>(),
                It.IsAny<InternalGoapGoal>(),
                It.IsAny<IReadOnlyList<GoapAction>>(),
                It.IsAny<PlanningOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoapPlan?)null);

        var service = CreateService();
        var request = new GoapPlanRequest
        {
            AgentId = "agent-1",
            BehaviorId = "behavior-123",
            Goal = new ApiGoapGoal
            {
                Name = "test_goal",
                Priority = 50,
                Conditions = new Dictionary<string, string> { { "goal_met", "==true" } }
            },
            WorldState = new Dictionary<string, object> { { "some_state", "value" } }
        };

        // Act
        var (status, response) = await service.GenerateGoapPlanAsync(request);

        // Assert - OK with no plan because goal unreachable
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.Null(response.Plan);
        Assert.Contains("No valid plan found", response.FailureReason);
    }

    [Fact]
    public async Task GenerateGoapPlanAsync_SuccessfulPlan_ReturnsOkWithPlan()
    {
        // Arrange
        var cachedMetadata = new CachedGoapMetadata
        {
            BehaviorId = "behavior-123",
            Goals = new List<CachedGoapGoal>(),
            Actions = new List<CachedGoapAction>
            {
                new CachedGoapAction
                {
                    FlowName = "action1",
                    Preconditions = new Dictionary<string, string>(),
                    Effects = new Dictionary<string, string> { { "goal_met", "true" } },
                    Cost = 2.0f
                }
            }
        };

        _mockBundleManager
            .Setup(m => m.GetGoapMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedMetadata);

        // Create a mock plan result
        var mockGoal = new InternalGoapGoal("test_goal", "test_goal", 50);
        var mockAction = new GoapAction("action1", "action1", new GoapPreconditions(), new GoapActionEffects(), 2.0f);
        var mockPlan = new GoapPlan(
            goal: mockGoal,
            actions: new List<PlannedAction> { new PlannedAction(mockAction, 0) },
            totalCost: 2.0f,
            nodesExpanded: 5,
            planningTimeMs: 10,
            initialState: new WorldState(),
            expectedFinalState: new WorldState());

        _mockGoapPlanner
            .Setup(p => p.PlanAsync(
                It.IsAny<WorldState>(),
                It.IsAny<InternalGoapGoal>(),
                It.IsAny<IReadOnlyList<GoapAction>>(),
                It.IsAny<PlanningOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPlan);

        var service = CreateService();
        var request = new GoapPlanRequest
        {
            AgentId = "agent-1",
            BehaviorId = "behavior-123",
            Goal = new ApiGoapGoal
            {
                Name = "test_goal",
                Priority = 50,
                Conditions = new Dictionary<string, string> { { "goal_met", "==true" } }
            },
            WorldState = new Dictionary<string, object>()
        };

        // Act
        var (status, response) = await service.GenerateGoapPlanAsync(request);

        // Assert
        Assert.Equal(StatusCodes.OK, status);
        Assert.NotNull(response);
        Assert.NotNull(response.Plan);
        Assert.Single(response.Plan.Actions);
        Assert.Equal("action1", response.Plan.Actions.First().ActionId);
        Assert.Equal(2.0f, response.Plan.TotalCost);
        Assert.Equal(10, response.PlanningTimeMs);
        Assert.Equal(5, response.NodesExpanded);
    }

    [Fact]
    public async Task GenerateGoapPlanAsync_WithCustomOptions_PassesOptionsToPlannerCorrectly()
    {
        // Arrange
        var cachedMetadata = new CachedGoapMetadata
        {
            BehaviorId = "behavior-123",
            Goals = new List<CachedGoapGoal>(),
            Actions = new List<CachedGoapAction>
            {
                new CachedGoapAction
                {
                    FlowName = "action1",
                    Preconditions = new Dictionary<string, string>(),
                    Effects = new Dictionary<string, string> { { "goal_met", "true" } },
                    Cost = 1.0f
                }
            }
        };

        _mockBundleManager
            .Setup(m => m.GetGoapMetadataAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedMetadata);

        PlanningOptions? capturedOptions = null;
        _mockGoapPlanner
            .Setup(p => p.PlanAsync(
                It.IsAny<WorldState>(),
                It.IsAny<InternalGoapGoal>(),
                It.IsAny<IReadOnlyList<GoapAction>>(),
                It.IsAny<PlanningOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<WorldState, InternalGoapGoal, IReadOnlyList<GoapAction>, PlanningOptions?, CancellationToken>(
                (ws, g, a, o, ct) => capturedOptions = o)
            .ReturnsAsync((GoapPlan?)null);

        var service = CreateService();
        var request = new GoapPlanRequest
        {
            AgentId = "agent-1",
            BehaviorId = "behavior-123",
            Goal = new ApiGoapGoal
            {
                Name = "test_goal",
                Priority = 50,
                Conditions = new Dictionary<string, string> { { "goal_met", "==true" } }
            },
            WorldState = new Dictionary<string, object>(),
            Options = new GoapPlanningOptions
            {
                MaxDepth = 15,
                MaxNodes = 500,
                TimeoutMs = 200
            }
        };

        // Act
        await service.GenerateGoapPlanAsync(request);

        // Assert
        Assert.NotNull(capturedOptions);
        Assert.Equal(15, capturedOptions.MaxDepth);
        Assert.Equal(500, capturedOptions.MaxNodesExpanded);
        Assert.Equal(200, capturedOptions.TimeoutMs);
    }

    #endregion

    #region Helper Methods

    private BehaviorService CreateService()
    {
        return new BehaviorService(
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockMessageBus.Object,
            _mockEventConsumer.Object,
            _mockGoapPlanner.Object,
            _compiler,
            _mockAssetClient.Object,
            _mockHttpClientFactory.Object,
            _mockBundleManager.Object,
            _mockTelemetryProvider.Object);
    }

    #endregion
}
