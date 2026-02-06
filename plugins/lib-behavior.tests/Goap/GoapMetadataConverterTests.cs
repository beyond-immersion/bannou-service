// ═══════════════════════════════════════════════════════════════════════════
// GoapMetadataConverter Unit Tests
// Tests for converting ABML parsed metadata to GOAP runtime types.
// ═══════════════════════════════════════════════════════════════════════════

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Goap;
using Xunit;

namespace BeyondImmersion.BannouService.Behavior.Tests.Goap;

/// <summary>
/// Tests for GoapMetadataConverter that bridges parser models to planning types.
/// </summary>
public class GoapMetadataConverterTests
{
    #region ToGoapGoal

    [Fact]
    public void ToGoapGoal_ConvertsBasicGoal()
    {
        // Arrange
        var definition = new GoapGoalDefinition
        {
            Priority = 100,
            Conditions = new Dictionary<string, string>
            {
                { "hunger", "<= 0.3" }
            }
        };

        // Act
        var goal = GoapMetadataConverter.ToGoapGoal("stay_fed", definition);

        // Assert
        Assert.Equal("stay_fed", goal.Id);
        Assert.Equal("stay_fed", goal.Name);
        Assert.Equal(100, goal.Priority);
        Assert.Single(goal.Conditions);
    }

    [Fact]
    public void ToGoapGoal_HandlesMultipleConditions()
    {
        // Arrange
        var definition = new GoapGoalDefinition
        {
            Priority = 80,
            Conditions = new Dictionary<string, string>
            {
                { "health", ">= 50" },
                { "stamina", ">= 20" },
                { "alive", "== true" }
            }
        };

        // Act
        var goal = GoapMetadataConverter.ToGoapGoal("survive", definition);

        // Assert
        Assert.Equal(3, goal.ConditionCount);
        Assert.True(goal.HasCondition("health"));
        Assert.True(goal.HasCondition("stamina"));
        Assert.True(goal.HasCondition("alive"));
    }

    [Fact]
    public void ToGoapGoal_PreservesPriority()
    {
        // Arrange
        var definition = new GoapGoalDefinition
        {
            Priority = 42,
            Conditions = new Dictionary<string, string>()
        };

        // Act
        var goal = GoapMetadataConverter.ToGoapGoal("test", definition);

        // Assert
        Assert.Equal(42, goal.Priority);
    }

    #endregion

    #region ToGoapAction

    [Fact]
    public void ToGoapAction_ConvertsBasicAction()
    {
        // Arrange
        var metadata = new GoapFlowMetadata
        {
            Preconditions = new Dictionary<string, string>
            {
                { "gold", ">= 5" }
            },
            Effects = new Dictionary<string, string>
            {
                { "hunger", "-0.5" }
            },
            Cost = 2.0f
        };

        // Act
        var action = GoapMetadataConverter.ToGoapAction("eat_meal", metadata);

        // Assert
        Assert.Equal("eat_meal", action.Id);
        Assert.Equal("eat_meal", action.Name);
        Assert.Equal(2.0f, action.Cost);
        Assert.Equal(1, action.Preconditions.Count);
        Assert.Equal(1, action.Effects.Count);
    }

    [Fact]
    public void ToGoapAction_HandlesEmptyPreconditions()
    {
        // Arrange
        var metadata = new GoapFlowMetadata
        {
            Preconditions = new Dictionary<string, string>(),
            Effects = new Dictionary<string, string>
            {
                { "rested", "true" }
            },
            Cost = 1.0f
        };

        // Act
        var action = GoapMetadataConverter.ToGoapAction("rest", metadata);

        // Assert
        Assert.Equal(0, action.Preconditions.Count);
    }

    [Fact]
    public void ToGoapAction_HandlesMultipleEffects()
    {
        // Arrange
        var metadata = new GoapFlowMetadata
        {
            Preconditions = new Dictionary<string, string>(),
            Effects = new Dictionary<string, string>
            {
                { "hunger", "-0.5" },
                { "gold", "-10" },
                { "at_location", "tavern" }
            },
            Cost = 3.0f
        };

        // Act
        var action = GoapMetadataConverter.ToGoapAction("dine_at_tavern", metadata);

        // Assert
        Assert.Equal(3, action.Effects.Count);
    }

    #endregion

    #region ExtractGoals

    [Fact]
    public void ExtractGoals_ExtractsAllGoals()
    {
        // Arrange
        var document = CreateDocumentWithGoals(new Dictionary<string, GoapGoalDefinition>
        {
            { "stay_fed", new GoapGoalDefinition { Priority = 100, Conditions = new Dictionary<string, string> { { "hunger", "<= 0.3" } } } },
            { "earn_money", new GoapGoalDefinition { Priority = 80, Conditions = new Dictionary<string, string> { { "gold", ">= 50" } } } }
        });

        // Act
        var goals = GoapMetadataConverter.ExtractGoals(document);

        // Assert
        Assert.Equal(2, goals.Count);
        Assert.Contains(goals, g => g.Id == "stay_fed");
        Assert.Contains(goals, g => g.Id == "earn_money");
    }

    [Fact]
    public void ExtractGoals_ReturnsEmptyForNoGoals()
    {
        // Arrange
        var document = CreateDocumentWithGoals(new Dictionary<string, GoapGoalDefinition>());

        // Act
        var goals = GoapMetadataConverter.ExtractGoals(document);

        // Assert
        Assert.Empty(goals);
    }

    #endregion

    #region ExtractActions

    [Fact]
    public void ExtractActions_ExtractsGoapFlows()
    {
        // Arrange
        var document = CreateDocumentWithFlows(new Dictionary<string, Flow>
        {
            { "eat_meal", new Flow
            {
                Goap = new GoapFlowMetadata
                {
                    Preconditions = new Dictionary<string, string> { { "gold", ">= 5" } },
                    Effects = new Dictionary<string, string> { { "hunger", "-0.5" } },
                    Cost = 2.0f
                }
            }},
            { "work", new Flow
            {
                Goap = new GoapFlowMetadata
                {
                    Preconditions = new Dictionary<string, string>(),
                    Effects = new Dictionary<string, string> { { "gold", "+10" } },
                    Cost = 5.0f
                }
            }}
        });

        // Act
        var actions = GoapMetadataConverter.ExtractActions(document);

        // Assert
        Assert.Equal(2, actions.Count);
        Assert.Contains(actions, a => a.Id == "eat_meal");
        Assert.Contains(actions, a => a.Id == "work");
    }

    [Fact]
    public void ExtractActions_IgnoresNonGoapFlows()
    {
        // Arrange
        var document = CreateDocumentWithFlows(new Dictionary<string, Flow>
        {
            { "goap_flow", new Flow
            {
                Goap = new GoapFlowMetadata
                {
                    Effects = new Dictionary<string, string> { { "done", "true" } },
                    Cost = 1.0f
                }
            }},
            { "normal_flow", new Flow { Goap = null } }
        });

        // Act
        var actions = GoapMetadataConverter.ExtractActions(document);

        // Assert
        Assert.Single(actions);
        Assert.Equal("goap_flow", actions[0].Id);
    }

    [Fact]
    public void ExtractActions_ReturnsEmptyForNoGoapFlows()
    {
        // Arrange
        var document = CreateDocumentWithFlows(new Dictionary<string, Flow>
        {
            { "flow1", new Flow { Goap = null } },
            { "flow2", new Flow { Goap = null } }
        });

        // Act
        var actions = GoapMetadataConverter.ExtractActions(document);

        // Assert
        Assert.Empty(actions);
    }

    #endregion

    #region ExtractAll

    [Fact]
    public void ExtractAll_ExtractsBothGoalsAndActions()
    {
        // Arrange
        var document = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test", Type = "behavior" },
            Goals = new Dictionary<string, GoapGoalDefinition>
            {
                { "goal1", new GoapGoalDefinition { Priority = 100, Conditions = new Dictionary<string, string> { { "done", "== true" } } } }
            },
            Flows = new Dictionary<string, Flow>
            {
                { "action1", new Flow
                {
                    Goap = new GoapFlowMetadata
                    {
                        Effects = new Dictionary<string, string> { { "done", "true" } },
                        Cost = 1.0f
                    }
                }}
            }
        };

        // Act
        var (goals, actions) = GoapMetadataConverter.ExtractAll(document);

        // Assert
        Assert.Single(goals);
        Assert.Single(actions);
        Assert.Equal("goal1", goals[0].Id);
        Assert.Equal("action1", actions[0].Id);
    }

    #endregion

    #region HasGoapContent

    [Fact]
    public void HasGoapContent_TrueWhenHasGoals()
    {
        // Arrange
        var document = CreateDocumentWithGoals(new Dictionary<string, GoapGoalDefinition>
        {
            { "goal", new GoapGoalDefinition() }
        });

        // Act
        var hasContent = GoapMetadataConverter.HasGoapContent(document);

        // Assert
        Assert.True(hasContent);
    }

    [Fact]
    public void HasGoapContent_TrueWhenHasGoapFlows()
    {
        // Arrange
        var document = CreateDocumentWithFlows(new Dictionary<string, Flow>
        {
            { "flow", new Flow { Goap = new GoapFlowMetadata() } }
        });

        // Act
        var hasContent = GoapMetadataConverter.HasGoapContent(document);

        // Assert
        Assert.True(hasContent);
    }

    [Fact]
    public void HasGoapContent_FalseWhenEmpty()
    {
        // Arrange
        var document = new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test", Type = "behavior" }
        };

        // Act
        var hasContent = GoapMetadataConverter.HasGoapContent(document);

        // Assert
        Assert.False(hasContent);
    }

    [Fact]
    public void HasGoapContent_FalseWhenOnlyNonGoapFlows()
    {
        // Arrange
        var document = CreateDocumentWithFlows(new Dictionary<string, Flow>
        {
            { "flow1", new Flow { Goap = null } },
            { "flow2", new Flow { Goap = null } }
        });

        // Act
        var hasContent = GoapMetadataConverter.HasGoapContent(document);

        // Assert
        Assert.False(hasContent);
    }

    #endregion

    #region GetGoalCount / GetActionCount

    [Fact]
    public void GetGoalCount_ReturnsCorrectCount()
    {
        // Arrange
        var document = CreateDocumentWithGoals(new Dictionary<string, GoapGoalDefinition>
        {
            { "goal1", new GoapGoalDefinition() },
            { "goal2", new GoapGoalDefinition() },
            { "goal3", new GoapGoalDefinition() }
        });

        // Act
        var count = GoapMetadataConverter.GetGoalCount(document);

        // Assert
        Assert.Equal(3, count);
    }

    [Fact]
    public void GetActionCount_ReturnsCorrectCount()
    {
        // Arrange
        var document = CreateDocumentWithFlows(new Dictionary<string, Flow>
        {
            { "action1", new Flow { Goap = new GoapFlowMetadata() } },
            { "action2", new Flow { Goap = new GoapFlowMetadata() } },
            { "normal", new Flow { Goap = null } }
        });

        // Act
        var count = GoapMetadataConverter.GetActionCount(document);

        // Assert
        Assert.Equal(2, count); // Only GOAP-enabled flows
    }

    #endregion

    #region Helpers

    private static AbmlDocument CreateDocumentWithGoals(IReadOnlyDictionary<string, GoapGoalDefinition> goals)
    {
        return new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test", Type = "behavior" },
            Goals = goals
        };
    }

    private static AbmlDocument CreateDocumentWithFlows(IReadOnlyDictionary<string, Flow> flows)
    {
        return new AbmlDocument
        {
            Version = "2.0",
            Metadata = new DocumentMetadata { Id = "test", Type = "behavior" },
            Flows = flows
        };
    }

    #endregion
}
