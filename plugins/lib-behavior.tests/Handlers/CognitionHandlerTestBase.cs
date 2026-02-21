// =============================================================================
// Cognition Handler Test Base
// Shared test infrastructure for cognition handler tests.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.Bannou.BehaviorCompiler.Documents.Actions;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.Bannou.BehaviorExpressions.Runtime;
using BeyondImmersion.BannouService.Abml.Cognition;
using BeyondImmersion.BannouService.Abml.Execution;
using Moq;
using AbmlExecutionContext = BeyondImmersion.BannouService.Abml.Execution.ExecutionContext;
using CognitionMemory = BeyondImmersion.BannouService.Abml.Cognition.Memory;

namespace BeyondImmersion.BannouService.Behavior.Tests.Handlers;

/// <summary>
/// Base class for cognition handler tests providing shared test infrastructure.
/// </summary>
public abstract class CognitionHandlerTestBase
{
    /// <summary>
    /// Creates a mock execution context for testing handlers.
    /// </summary>
    protected static AbmlExecutionContext CreateTestContext(VariableScope? scope = null)
    {
        var rootScope = scope ?? new VariableScope();
        var mockEvaluator = new Mock<IExpressionEvaluator>();
        var mockRegistry = new Mock<IActionHandlerRegistry>();

        // Set up evaluator to return values directly if they start with ${
        mockEvaluator.Setup(e => e.Evaluate(It.IsAny<string>(), It.IsAny<IVariableScope>()))
            .Returns<string, IVariableScope>((expr, s) =>
            {
                if (expr.StartsWith("${") && expr.EndsWith("}"))
                {
                    var varName = expr[2..^1];
                    return s.GetValue(varName);
                }
                return expr;
            });

        return new AbmlExecutionContext
        {
            Document = new AbmlDocument
            {
                Version = "2.0",
                Metadata = new DocumentMetadata { Id = "test-doc" }
            },
            RootScope = rootScope,
            Evaluator = mockEvaluator.Object,
            Handlers = mockRegistry.Object
        };
    }

    /// <summary>
    /// Creates a DomainAction with the given name and parameters.
    /// </summary>
    protected static DomainAction CreateDomainAction(string name, Dictionary<string, object?> parameters)
    {
        return new DomainAction(name, parameters);
    }

    /// <summary>
    /// Creates a test perception.
    /// </summary>
    protected static Perception CreatePerception(
        string category = "routine",
        string content = "Test perception",
        float urgency = 0.5f,
        string source = "test-source",
        Dictionary<string, object>? data = null)
    {
        return new Perception
        {
            Id = Guid.NewGuid().ToString(),
            Category = category,
            Content = content,
            Urgency = urgency,
            Source = source,
            Timestamp = DateTimeOffset.UtcNow,
            Data = data ?? new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Creates a test memory.
    /// </summary>
    protected static CognitionMemory CreateMemory(
        string? id = null,
        string entityId = "test-entity",
        string category = "routine",
        string content = "Test memory",
        float significance = 0.5f)
    {
        return new CognitionMemory
        {
            Id = id ?? Guid.NewGuid().ToString(),
            EntityId = entityId,
            Category = category,
            Content = content,
            Significance = significance,
            Timestamp = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// Gets a value from the scope after handler execution.
    /// </summary>
    protected static T? GetScopeValue<T>(AbmlExecutionContext context, string variableName)
    {
        var value = context.RootScope.GetValue(variableName);
        if (value is T typedValue)
        {
            return typedValue;
        }
        return default;
    }
}
