// =============================================================================
// Trigger GOAP Replan Handler Unit Tests
// Tests for GOAP replan triggering (Cognition Stage 5).
// =============================================================================

using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.Bannou.BehaviorCompiler.Goap;
using BeyondImmersion.Bannou.Behavior.Handlers;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Handlers;

/// <summary>
/// Unit tests for TriggerGoapReplanHandler.
/// The handler invokes IGoapPlanner when goal, actions, and world state are provided.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Test-Implementation Coupling:</strong> The urgency mapping tests
/// (<c>ExecuteAsync_Urgency_MapsToCorrectPlanningOptions</c>) use InlineData values
/// that are coupled to <see cref="CognitionConstants"/>. If urgency thresholds or
/// planning parameters change, the test data must be updated to match.
/// </para>
/// <para>
/// Urgency bands (see <see cref="CognitionConstants"/>):
/// <list type="bullet">
/// <item>Low (&lt;0.3): MaxDepth=10, TimeoutMs=100, MaxNodes=1000</item>
/// <item>Medium (0.3-0.7): MaxDepth=6, TimeoutMs=50, MaxNodes=500</item>
/// <item>High (&gt;=0.7): MaxDepth=3, TimeoutMs=20, MaxNodes=200</item>
/// </list>
/// </para>
/// </remarks>
public class TriggerGoapReplanHandlerTests : CognitionHandlerTestBase
{
    private readonly Mock<IGoapPlanner> _mockPlanner;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<ILogger<TriggerGoapReplanHandler>> _mockLogger;
    private readonly TriggerGoapReplanHandler _handler;

    public TriggerGoapReplanHandlerTests()
    {
        _mockPlanner = new Mock<IGoapPlanner>();
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockLogger = new Mock<ILogger<TriggerGoapReplanHandler>>();

        // Setup empty scope that returns null for bundle manager (tests don't use it)
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);
        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _handler = new TriggerGoapReplanHandler(_mockPlanner.Object, _mockScopeFactory.Object, _mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void ConstructorIsValid()
    {
        ServiceConstructorValidator.ValidateServiceConstructor<TriggerGoapReplanHandler>();
        Assert.NotNull(_handler);
    }

    #endregion

    #region CanHandle Tests

    [Fact]
    public void CanHandle_TriggerGoapReplanAction_ReturnsTrue()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>());

        var result = _handler.CanHandle(action);

        Assert.True(result);
    }

    [Fact]
    public void CanHandle_OtherAction_ReturnsFalse()
    {
        var action = CreateDomainAction("other_action", new Dictionary<string, object?>());

        var result = _handler.CanHandle(action);

        Assert.False(result);
    }

    [Fact]
    public void CanHandle_NonDomainAction_ReturnsFalse()
    {
        var action = new SetAction("var", "value");

        var result = _handler.CanHandle(action);

        Assert.False(result);
    }

    #endregion

    #region ExecuteAsync Tests - Basic Execution

    [Fact]
    public async Task ExecuteAsync_ValidParams_ReturnsTriggeredStatus()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "entity_id", "test-entity" },
            { "behavior_id", "test-behavior" },
            { "goals", new List<string> { "survive" } },
            { "urgency", 0.5f }
        });
        var context = CreateTestContext();

        var result = await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(ActionResult.Continue, result);
        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.True(status.Triggered);
    }

    [Fact]
    public async Task ExecuteAsync_MinimalParams_StillSucceeds()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>());
        var context = CreateTestContext();

        var result = await _handler.ExecuteAsync(action, context, CancellationToken.None);

        Assert.Equal(ActionResult.Continue, result);
        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.True(status.Triggered);
    }

    #endregion

    #region ExecuteAsync Tests - Urgency Mapping

    [Theory]
    [InlineData(0.0f, 10, 100, 1000)]   // Low urgency
    [InlineData(0.2f, 10, 100, 1000)]   // Low urgency
    [InlineData(0.4f, 6, 50, 500)]      // Medium urgency
    [InlineData(0.6f, 6, 50, 500)]      // Medium urgency
    [InlineData(0.8f, 3, 20, 200)]      // High urgency
    [InlineData(1.0f, 3, 20, 200)]      // High urgency
    public async Task ExecuteAsync_Urgency_MapsToCorrectPlanningOptions(
        float urgency, int expectedMaxDepth, int expectedTimeoutMs, int expectedMaxNodes)
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "urgency", urgency }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.NotNull(status.PlanningOptions);
        Assert.Equal(expectedMaxDepth, status.PlanningOptions.MaxDepth);
        Assert.Equal(expectedTimeoutMs, status.PlanningOptions.TimeoutMs);
        Assert.Equal(expectedMaxNodes, status.PlanningOptions.MaxNodes);
    }

    [Fact]
    public async Task ExecuteAsync_DefaultUrgency_UsesMedium()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>());
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Equal(0.5f, status.Urgency);
        // Medium urgency options
        Assert.Equal(6, status.PlanningOptions?.MaxDepth);
    }

    #endregion

    #region ExecuteAsync Tests - Entity and Behavior IDs

    [Fact]
    public async Task ExecuteAsync_EntityId_StoredInStatus()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "entity_id", "npc-123" }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Equal("npc-123", status.EntityId);
    }

    [Fact]
    public async Task ExecuteAsync_BehaviorId_StoredInStatus()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "behavior_id", "combat-behavior" }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Equal("combat-behavior", status.BehaviorId);
    }

    [Fact]
    public async Task ExecuteAsync_MissingIds_UsesUnknownDefaults()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>());
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Equal("unknown", status.EntityId);
        Assert.Equal("unknown", status.BehaviorId);
    }

    #endregion

    #region ExecuteAsync Tests - Affected Goals

    [Fact]
    public async Task ExecuteAsync_GoalsList_StoredInStatus()
    {
        var goals = new List<string> { "survive", "find_food", "rest" };
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Equal(3, status.AffectedGoals.Count);
        Assert.Contains("survive", status.AffectedGoals);
        Assert.Contains("find_food", status.AffectedGoals);
        Assert.Contains("rest", status.AffectedGoals);
    }

    [Fact]
    public async Task ExecuteAsync_SingleGoalString_ParsesAsList()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "goals", "survive" }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Single(status.AffectedGoals);
        Assert.Contains("survive", status.AffectedGoals);
    }

    [Fact]
    public async Task ExecuteAsync_NoGoals_EmptyList()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>());
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Empty(status.AffectedGoals);
    }

    #endregion

    #region ExecuteAsync Tests - World State

    [Fact]
    public async Task ExecuteAsync_WithGoalsAndWorldState_InsufficientContextWithoutGoalObject()
    {
        // Without a GoapGoal object or bundle manager, string goal names alone are insufficient
        var worldState = new Dictionary<string, object?>
        {
            { "health", 0.5f },
            { "has_weapon", true }
        };
        var goals = new List<string> { "attack" };

        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "goals", goals },
            { "world_state", worldState }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        // Without a proper GoapGoal object, planning cannot proceed
        Assert.Contains("Insufficient context", status.Message ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_NoWorldState_InsufficientContextMessage()
    {
        var goals = new List<string> { "attack" };

        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "goals", goals }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Contains("Insufficient context", status.Message ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_WorldStateObject_ConvertedCorrectly()
    {
        // WorldState object without GoapGoal still results in insufficient context
        var worldState = new WorldState()
            .SetBoolean("has_weapon", true)
            .SetNumeric("health", 0.8f);
        var goals = new List<string> { "attack" };

        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "goals", goals },
            { "world_state", worldState }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        // WorldState is properly converted but without goal object, planning can't proceed
        Assert.Contains("Insufficient context", status.Message ?? "");
    }

    #endregion

    #region ExecuteAsync Tests - Result Variable

    [Fact]
    public async Task ExecuteAsync_DefaultResultVariable_StoresAsReplanStatus()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>());
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
    }

    [Fact]
    public async Task ExecuteAsync_CustomResultVariable_UsesCustomName()
    {
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "result_variable", "my_replan_status" }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "my_replan_status");
        Assert.NotNull(status);
    }

    #endregion

    #region Planner Invocation Tests

    [Fact]
    public async Task ExecuteAsync_WithFullContext_InvokesPlanner()
    {
        // Provide complete context: goal object, actions, and world state
        var goal = BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal.FromMetadata("attack", 50, new Dictionary<string, string>
        {
            { "target_eliminated", "== true" }
        });
        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata("strike",
                new Dictionary<string, string> { { "has_weapon", "== true" } },
                new Dictionary<string, string> { { "target_eliminated", "== true" } }, 1.0f)
        };
        var worldState = new WorldState()
            .SetBoolean("has_weapon", true)
            .SetNumeric("health", 0.8f);

        // Setup mock planner to return a plan
        var mockPlan = new GoapPlan(
            goal,
            new List<PlannedAction> { new PlannedAction(actions[0], 0) },
            1.0f, 5, 10, worldState, worldState.SetBoolean("target_eliminated", true));

        _mockPlanner
            .Setup(p => p.PlanAsync(
                It.IsAny<WorldState>(),
                It.IsAny<BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal>(),
                It.IsAny<IReadOnlyList<GoapAction>>(),
                It.IsAny<PlanningOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockPlan);

        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "entity_id", "test-entity" },
            { "goal", goal },
            { "available_actions", actions },
            { "world_state", worldState }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        // Verify planner was called
        _mockPlanner.Verify(
            p => p.PlanAsync(
                It.IsAny<WorldState>(),
                It.IsAny<BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal>(),
                It.IsAny<IReadOnlyList<GoapAction>>(),
                It.IsAny<PlanningOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Handler should invoke planner when all context is provided");

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.NotNull(status.Plan);
        Assert.Contains("Plan found", status.Message ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_PlannerReturnsNull_NoPlanMessage()
    {
        var goal = BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal.FromMetadata("impossible", 50, new Dictionary<string, string>
        {
            { "impossible_condition", "== true" }
        });
        var actions = new List<GoapAction>
        {
            GoapAction.FromMetadata("useless_action",
                new Dictionary<string, string>(),
                new Dictionary<string, string> { { "other_condition", "== true" } }, 1.0f)
        };
        var worldState = new WorldState().SetBoolean("start", true);

        // Planner returns null (no plan found)
        _mockPlanner
            .Setup(p => p.PlanAsync(
                It.IsAny<WorldState>(),
                It.IsAny<BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal>(),
                It.IsAny<IReadOnlyList<GoapAction>>(),
                It.IsAny<PlanningOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((GoapPlan?)null);

        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "goal", goal },
            { "available_actions", actions },
            { "world_state", worldState }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Null(status.Plan);
        Assert.Contains("No plan found", status.Message ?? "");
    }

    [Fact]
    public async Task ExecuteAsync_InsufficientContext_DoesNotInvokePlanner()
    {
        // No goal object provided, only goal strings - planner should not be invoked
        var action = CreateDomainAction("trigger_goap_replan", new Dictionary<string, object?>
        {
            { "entity_id", "test" },
            { "goals", new List<string> { "survive" } },
            { "world_state", new Dictionary<string, object?> { { "health", 1.0f } } }
        });
        var context = CreateTestContext();

        await _handler.ExecuteAsync(action, context, CancellationToken.None);

        // Verify planner was never called (insufficient context)
        _mockPlanner.Verify(
            p => p.PlanAsync(
                It.IsAny<WorldState>(),
                It.IsAny<BeyondImmersion.Bannou.BehaviorCompiler.Goap.GoapGoal>(),
                It.IsAny<IReadOnlyList<GoapAction>>(),
                It.IsAny<PlanningOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "Handler should not invoke planner without proper goal object");

        var status = GetScopeValue<ReplanStatus>(context, "replan_status");
        Assert.NotNull(status);
        Assert.Contains("Insufficient context", status.Message ?? "");
    }

    #endregion
}
